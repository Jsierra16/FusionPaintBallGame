using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// WeaponManager - attach to your player GameObject (alongside FirstPersonMovement).
/// - Switch weapons with mouse scroll / number keys (owner only)
/// - Fire via RPC to state authority (server-authoritative spawn)
/// - Cooldowns enforced on the state authority (tick-based)
/// - Networked active weapon index so others see equipped weapon
/// - Optional localModel per weapon for owner visuals
/// 
/// IMPORTANT: Do not access networked properties in Awake(). Use Spawned() to read/write them.
/// This version uses a local _isSpawned flag to be compatible with Fusion variants that lack Object.IsSpawned.
/// </summary>
public class WeaponManager : NetworkBehaviour
{
    [Serializable]
    public class WeaponEntry
    {
        public string displayName = "Weapon";
        public NetworkObject projectilePrefab; // assign your networked projectile prefab (Ball, PhysxBall, etc.)
        public float cooldown = 0.5f;          // seconds between shots
        public float spawnDistance = 1f;       // forward distance from player to spawn
        public Vector3 spawnOffset = Vector3.up * 0.5f; // local offset applied before forward
        public GameObject localModel;          // optional owner-only model shown for equipped weapon (viewmodel)
    }

    [Header("Weapons")]
    public List<WeaponEntry> weapons = new List<WeaponEntry>();

    // networked active weapon index so others can see which weapon is equipped
    [Networked] public int activeWeaponIndex { get; set; }

    // server-side per-weapon next-allowed-tick tracker (kept in memory on the instance)
    private List<long> _nextFireTick;

    // cache reference to the FirstPersonMovement for forward direction if you want (optional)
    private FirstPersonMovement _fpm;

    // Local instantiated viewmodel reference (owner-only)
    private GameObject _activeLocalModel;
    private int _lastSeenActiveIndex = int.MinValue;
    private bool _lastSeenOwnerState = false;

    // Compatibility flag: set true in Spawned() so other methods know networked props are safe to use.
    private bool _isSpawned = false;

    private void Awake()
    {
        // Very important: DO NOT access networked properties here.
        // Only perform purely local initialization.
        _fpm = GetComponent<FirstPersonMovement>();
        EnsureNextFireList();
    }

    public override void Spawned()
    {
        // Mark as spawned (networked props are now safe)
        _isSpawned = true;

        // Now it's safe to read/write networked properties
        // Ensure valid active index
        if (weapons == null || weapons.Count == 0)
        {
            activeWeaponIndex = -1;
        }
        else
        {
            if (activeWeaponIndex < 0 || activeWeaponIndex >= weapons.Count)
                activeWeaponIndex = 0;
        }

        // Initialize last-seen so UpdateLocalModels runs on first check
        _lastSeenActiveIndex = int.MinValue;
        _lastSeenOwnerState = !Object.HasInputAuthority;

        // Show/hide owner models as appropriate
        UpdateLocalModels();
    }

    private void Update()
    {
        // Owner only: handle scroll/number key switching and update local model visibility.
        if (!Object.HasInputAuthority) return;

        // Scroll wheel weapon cycling
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
        {
            if (scroll > 0f) CycleWeapon(-1); // scroll up -> previous
            else CycleWeapon(1);              // scroll down -> next
        }

        // Number keys quick-switch (1..9)
        for (int i = 0; i < weapons.Count && i < 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                SetWeaponIndex(i);
            }
        }

        // Update owner-only local model visibility (only runs when something changed)
        UpdateLocalModels();
    }

    private void EnsureNextFireList()
    {
        if (weapons == null)
        {
            _nextFireTick = new List<long>();
            return;
        }

        if (_nextFireTick == null || _nextFireTick.Count != weapons.Count)
        {
            _nextFireTick = new List<long>(weapons.Count);
            for (int i = 0; i < weapons.Count; i++) _nextFireTick.Add(0L);
        }
    }

    /// <summary>
    /// Efficiently update local models only when owner state or active index changes.
    /// Uses the local _isSpawned flag to ensure networked props are ready.
    /// </summary>
    private void UpdateLocalModels()
    {
        // Only attempt to touch viewmodels if we've been spawned (networked props valid)
        if (!_isSpawned) return;

        bool isOwner = Object.HasInputAuthority;

        // If owner state or active index didn't change, do nothing
        if (_lastSeenActiveIndex == activeWeaponIndex && _lastSeenOwnerState == isOwner)
            return;

        _lastSeenActiveIndex = activeWeaponIndex;
        _lastSeenOwnerState = isOwner;

        // Destroy any existing local model (we'll recreate if appropriate)
        if (_activeLocalModel != null)
        {
            Destroy(_activeLocalModel);
            _activeLocalModel = null;
        }

        // Only show the model for the owner client (first-person viewmodel)
        if (!isOwner) return;

        if (weapons == null || activeWeaponIndex < 0 || activeWeaponIndex >= weapons.Count) return;

        var modelPrefab = weapons[activeWeaponIndex].localModel;
        if (modelPrefab != null)
        {
            Transform weaponHolder = FindWeaponHolderTransform();
            if (weaponHolder != null)
            {
                _activeLocalModel = Instantiate(modelPrefab, weaponHolder);
            }
            else
            {
                _activeLocalModel = Instantiate(modelPrefab, transform);
            }

            _activeLocalModel.transform.localPosition = Vector3.zero;
            _activeLocalModel.transform.localRotation = Quaternion.identity;
        }
    }

    // Helper: try to find a suitable weapon-holder transform under the player (common names)
    private Transform FindWeaponHolderTransform()
    {
        // Search direct child first
        var holder = transform.Find("WeaponHolder");
        if (holder != null) return holder;

        // try to find a camera and use it
        var cam = GetComponentInChildren<Camera>();
        if (cam != null)
        {
            var wh = cam.transform.Find("WeaponHolder");
            if (wh != null) return wh;
            return cam.transform; // fallback to camera transform
        }

        return null;
    }

    /// <summary>
    /// Cycle weapon by delta (1 forward, -1 backward). Owner-only input triggers server request.
    /// </summary>
    public void CycleWeapon(int delta)
    {
        if (!Object.HasInputAuthority || weapons == null || weapons.Count == 0) return;
        int next = activeWeaponIndex + delta;
        if (next < 0) next = weapons.Count - 1;
        if (next >= weapons.Count) next = 0;
        SetWeaponIndex(next);
    }

    /// <summary>
    /// Owner requests to set weapon index. This sends an RPC to the state authority to set it authoritatively.
    /// </summary>
    public void SetWeaponIndex(int idx)
    {
        if (!Object.HasInputAuthority || weapons == null || weapons.Count == 0) return;
        idx = Mathf.Clamp(idx, 0, weapons.Count - 1);
        RPC_RequestSetWeapon(idx);
    }

    /// <summary>
    /// Called by input-handling code to attempt to fire the currently selected weapon.
    /// Owner-only; sends RPC to state authority to spawn.
    /// </summary>
    public void TryFireFromInput(bool primary)
    {
        if (!Object.HasInputAuthority) return;
        if (weapons == null || weapons.Count == 0) return;
        if (activeWeaponIndex < 0 || activeWeaponIndex >= weapons.Count) return;

        Debug.Log($"[WeaponManager] TryFireFromInput: owner={Object.HasInputAuthority}, weaponIndex={activeWeaponIndex}, primary={primary}");
        RPC_RequestFire(activeWeaponIndex, primary);
    }

    #region RPCs

    // Owner -> StateAuthority: request to change active weapon (state authority will set networked value)
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestSetWeapon(int index, RpcInfo info = default)
    {
        if (!HasStateAuthority) return;
        if (weapons == null || weapons.Count == 0)
        {
            activeWeaponIndex = -1;
            return;
        }

        if (index < 0 || index >= weapons.Count) index = 0;
        activeWeaponIndex = index;
    }

    // Owner -> StateAuthority: request to fire the weapon authoritatively (server spawns the projectile)
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestFire(int weaponIndex, bool primaryFire, RpcInfo info = default)
    {
        Debug.Log($"[WeaponManager] RPC_RequestFire received on state? {HasStateAuthority}, weaponIndex={weaponIndex}, from={info.Source}");

        if (!HasStateAuthority) return;
        if (weapons == null || weaponIndex < 0 || weaponIndex >= weapons.Count) return;

        var w = weapons[weaponIndex];

        // ensure list matches
        EnsureNextFireList();

        // determine tick-based cooldown
        long currentTick = Runner.Tick;
        int cooldownTicks = 1;
        if (Runner.DeltaTime > 0f)
            cooldownTicks = Mathf.Max(1, Mathf.CeilToInt(w.cooldown / Runner.DeltaTime));

        // if still cooling down, ignore
        if (currentTick < _nextFireTick[weaponIndex])
            return;

        // set next allowed tick
        _nextFireTick[weaponIndex] = currentTick + cooldownTicks;

        // compute spawn position and forward direction (server uses transform)
        Vector3 forward = transform.forward.sqrMagnitude > 0.001f ? transform.forward.normalized : Vector3.forward;

        // safe spawn position for testing: use a small forward offset so it doesn't spawn in the player
        Vector3 spawnPos = transform.position + (transform.rotation * w.spawnOffset) + forward * w.spawnDistance;

        // spawn the projectile if assigned
        if (w.projectilePrefab != null)
        {
            Runner.Spawn(
                w.projectilePrefab,
                spawnPos,
                Quaternion.LookRotation(forward),
                Object.InputAuthority,
                (runner, o) =>
                {
                    // try to initialize common projectile types (Ball / PhysxBall) if they exist on the prefab
                    var ball = o.GetComponent<Ball>();
                    if (ball != null)
                        ball.Init();

                    var phys = o.GetComponent<PhysxBall>();
                    if (phys != null)
                        phys.Init(forward * 10f);
                });
        }
    }

    #endregion

    /// <summary>
    /// Helper: name of the active weapon for UI.
    /// </summary>
    public string GetActiveWeaponName()
    {
        if (weapons == null) return "None";
        if (activeWeaponIndex < 0 || activeWeaponIndex >= weapons.Count) return "None";
        return weapons[activeWeaponIndex].displayName;
    }
}
