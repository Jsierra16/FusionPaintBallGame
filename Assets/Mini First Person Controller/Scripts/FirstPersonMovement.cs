using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using TMPro;

public class FirstPersonMovement : NetworkBehaviour
{
    [Header("Movement Settings")]
    public float speed = 5f;
    public bool canRun = true;
    public float runSpeed = 9f;
    public KeyCode runningKey = KeyCode.LeftShift;
    public List<Func<float>> speedOverrides = new List<Func<float>>();

    private Rigidbody _rb;
    private Vector3 _inputDirection;
    private bool _isRunning;
    public bool IsRunning => _isRunning;

    [Header("Projectile Prefabs")]
    [SerializeField] private PhysxBall _prefabPhysxBall;
    [SerializeField] private DroppedPhysxBall _prefabDroppedBall;
    [SerializeField] private LobbedPhysxBall _prefabLobbedBall;

    [Header("Projectile Spawn Point")]
    [SerializeField] private Transform _projectileSpawnPoint;

    [Header("Player Hit Settings")]
    [SerializeField] private Transform _spawnPoint;
    [Networked] private int hitCount { get; set; }    // <-- added (networked)
    public int maxHits = 10;

    [Networked] private TickTimer delay { get; set; }
    [Networked] public byte spawnedProjectileCounter { get; set; }

    // Visuals
    private ChangeDetector _changeDetector;
    private Material _instanceMaterial;
    private TMP_Text _messages;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        var mr = GetComponentInChildren<MeshRenderer>();
        if (mr != null)
            _instanceMaterial = mr.material;
    }

    public override void Spawned()
    {
        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);
        if (_instanceMaterial != null)
            _instanceMaterial.color = Color.blue;

        _messages = FindObjectOfType<TMP_Text>();

        // Bind the main camera's FirstPersonLook to this player, but only for local player
        try
        {
            if (Runner != null)
                Debug.Log($"[Spawned] Runner.LocalPlayer={Runner.LocalPlayer} Object.InputAuthority={Object.InputAuthority} HasInputAuthority={Object.HasInputAuthority}");
            else
                Debug.Log("[Spawned] Runner is null!");

            if (Runner != null && Runner.LocalPlayer == Object.InputAuthority)
            {
                Debug.Log("[Spawned] This is the local player -> binding camera/look.");

                var fpsLook = Camera.main ? Camera.main.GetComponent<FirstPersonLook>() : null;
                if (fpsLook != null)
                {
                    fpsLook.SetCharacter(transform);   // assign player transform
                    fpsLook.enabled = true;
                    Cursor.lockState = CursorLockMode.Locked;
                }
                else
                {
                    Debug.LogWarning("[Spawned] Camera.main or FirstPersonLook not found. Make sure FirstPersonLook is on the main camera and disabled by default.");
                }
            }
            else
            {
                Debug.Log("[Spawned] Not local player - skipping camera bind.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[Spawned] Exception during camera bind: " + ex);
        }
    }

    private void Update()
    {
        // Only process local player's input for building movement intent, messages, and debug controls.
        if (!Object.HasInputAuthority) return;

        // Movement input (read here; apply in FixedUpdateNetwork)
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        _inputDirection = new Vector3(h, 0, v);

        // Running
        _isRunning = canRun && Input.GetKey(runningKey);

        // TMP message test
        if (Input.GetKeyDown(KeyCode.R))
            RPC_SendMessage("Hey Mate!");

        // Debug: explode last dropped ball
        if (Input.GetKeyDown(KeyCode.X) && DroppedPhysxBall.LastSpawned != null)
            DroppedPhysxBall.LastSpawned.Explode();
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasInputAuthority) return;

        // Movement (match original Mini-FPS behaviour: horizontal and vertical multiplied separately)
        float targetSpeed = _isRunning ? runSpeed : speed;
        if (speedOverrides.Count > 0)
            targetSpeed = speedOverrides[speedOverrides.Count - 1]();

        Vector2 targetVelocity = new Vector2(_inputDirection.x * targetSpeed, _inputDirection.z * targetSpeed);
        Vector3 velocity = transform.rotation * new Vector3(targetVelocity.x, _rb.linearVelocity.y, targetVelocity.y);
        _rb.linearVelocity = velocity;

        // Projectiles: only state authority handles spawning & cooldowns
        if (!HasStateAuthority || !delay.ExpiredOrNotRunning(Runner)) return;
        if (!GetInput(out NetworkInputData data)) return;
        if (!data.buttons.IsSet(NetworkInputData.MOUSEBUTTON0)) return;

        delay = TickTimer.CreateFromSeconds(Runner, 0.5f);

        Vector3 forward = transform.forward; // transform.forward will follow the yaw applied by FirstPersonLook's character rotation
        int selected = data.selectedWeapon;

        if (selected == 0) SpawnPhysxBall(forward);
        else if (selected == 1) SpawnDroppedBall(forward);
        else if (selected == 2) SpawnLobbedBall(forward);
    }

    private Vector3 GetSpawnPosition()
    {
        return _projectileSpawnPoint != null ? _projectileSpawnPoint.position : transform.position + transform.forward;
    }

    private void SpawnPhysxBall(Vector3 forward)
    {
        if (_prefabPhysxBall == null) return;

        Runner.Spawn(
            _prefabPhysxBall,
            GetSpawnPosition(),
            Quaternion.LookRotation(forward),
            Object.InputAuthority,
            (runner, o) => o.GetComponent<PhysxBall>()?.Init(10 * forward));

        spawnedProjectileCounter++;
    }

    private void SpawnDroppedBall(Vector3 forward)
    {
        if (_prefabDroppedBall == null) return;

        Runner.Spawn(
            _prefabDroppedBall,
            GetSpawnPosition(),
            Quaternion.identity,
            Object.InputAuthority,
            (runner, o) =>
            {
                var dropComp = o.GetComponent<DroppedPhysxBall>();
                Rigidbody rb = dropComp?.GetComponent<Rigidbody>();
                if (rb != null) { rb.useGravity = true; rb.isKinematic = false; }
            });

        spawnedProjectileCounter++;
    }

    private void SpawnLobbedBall(Vector3 forward)
    {
        if (_prefabLobbedBall == null) return;

        Runner.Spawn(
            _prefabLobbedBall,
            GetSpawnPosition(),
            Quaternion.LookRotation(forward),
            Object.InputAuthority,
            (runner, o) => o.GetComponent<LobbedPhysxBall>()?.Init(forward.normalized));

        spawnedProjectileCounter++;
    }

    // ===== HIT / RESPAWN SYSTEM =====
    public void TakeHit()
    {
        if (!HasStateAuthority) return;

        hitCount++;
        if (_spawnPoint != null)
        {
            _rb.position = _spawnPoint.position;
            _rb.linearVelocity = Vector3.zero;
        }

        if (_instanceMaterial != null)
            _instanceMaterial.color = Color.red;

        if (hitCount >= maxHits)
            EndGame();
    }

    private void EndGame()
    {
        Debug.Log("Game Over!");
        canRun = false;
        speed = 0;
        Cursor.lockState = CursorLockMode.None;
    }

    public override void Render()
    {
        if (_changeDetector == null || _instanceMaterial == null) return;

        foreach (var change in _changeDetector.DetectChanges(this))
        {
            if (change == nameof(spawnedProjectileCounter))
                _instanceMaterial.color = Color.white;
        }

        _instanceMaterial.color = Color.Lerp(_instanceMaterial.color, Color.blue, Time.deltaTime);
    }

    // RPCs
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    public void RPC_SendMessage(string message, RpcInfo info = default) => RPC_RelayMessage(message, info.Source);

    [Rpc(RpcSources.StateAuthority, RpcTargets.All, HostMode = RpcHostMode.SourceIsServer)]
    public void RPC_RelayMessage(string message, PlayerRef messageSource)
    {
        if (_messages == null) _messages = FindObjectOfType<TMP_Text>();
        if (_messages == null) return;

        _messages.text += messageSource == Runner.LocalPlayer ? $"You said: {message}\n" : $"Some other player said: {message}\n";
    }
}
