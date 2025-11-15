using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using TMPro;

public class FirstPersonMovement : NetworkBehaviour
{
    [Header("Movement Settings")]
    public float speed = 5;
    public bool canRun = true;
    public float runSpeed = 9;
    public KeyCode runningKey = KeyCode.LeftShift;
    public List<Func<float>> speedOverrides = new List<Func<float>>();

    private Rigidbody _rb;
    private Vector3 _inputDirection;
    private bool _isRunning;
    public bool IsRunning => _isRunning;

    [Header("Projectile Prefabs (assign networked prefabs)")]
    [SerializeField] private PhysxBall _prefabPhysxBall;           
    [SerializeField] private DroppedPhysxBall _prefabDroppedBall;  
    [SerializeField] private LobbedPhysxBall _prefabLobbedBall;    

    [Header("Projectile Spawn Point")]
    [SerializeField] private Transform _projectileSpawnPoint; // Assign child empty in inspector

    [Networked] private TickTimer delay { get; set; }
    [Networked] public byte spawnedProjectileCounter { get; set; }

    // Visuals
    private ChangeDetector _changeDetector;
    private Material _instanceMaterial;

    // UI (TextMeshPro)
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
    }

    private void Update()
    {
        if (!Object.HasInputAuthority) return;

        // Movement input
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        _inputDirection = new Vector3(h, 0, v);

        // Running input
        _isRunning = canRun && Input.GetKey(runningKey);

        // TMP message test
        if (Input.GetKeyDown(KeyCode.R))
            RPC_SendMessage("Hey Mate!");

        // Test: explode last dropped ball
        if (Input.GetKeyDown(KeyCode.X) && DroppedPhysxBall.LastSpawned != null)
            DroppedPhysxBall.LastSpawned.Explode();
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasInputAuthority) return;

        // Calculate target speed
        float targetSpeed = _isRunning ? runSpeed : speed;
        if (speedOverrides.Count > 0)
            targetSpeed = speedOverrides[speedOverrides.Count - 1]();

        // Normalize input direction
        Vector3 moveDir = _inputDirection;
        if (moveDir.sqrMagnitude > 1) moveDir.Normalize();

        // Apply movement via Rigidbody
        Vector3 velocity = transform.rotation * new Vector3(moveDir.x * targetSpeed, _rb.linearVelocity.y, moveDir.z * targetSpeed);
        _rb.linearVelocity = velocity;

        // Forward direction for projectiles
        Vector3 forward = moveDir.sqrMagnitude > 0 ? moveDir.normalized : transform.forward;

        // Spawn projectiles if state authority and cooldown expired
        if (!HasStateAuthority || !delay.ExpiredOrNotRunning(Runner)) return;

        if (!GetInput(out NetworkInputData data)) return;
        if (!data.buttons.IsSet(NetworkInputData.MOUSEBUTTON0)) return;

        delay = TickTimer.CreateFromSeconds(Runner, 0.5f);

        int selected = data.selectedWeapon; // 0 = fired, 1 = mine, 2 = lobbed

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
        if (_prefabPhysxBall == null)
        {
            Debug.LogWarning("[Player] Cannot spawn PhysxBall.");
            return;
        }

        Runner.Spawn(
            _prefabPhysxBall,
            GetSpawnPosition(),
            Quaternion.LookRotation(forward),
            Object.InputAuthority,
            (runner, o) =>
            {
                var physxComp = o.GetComponent<PhysxBall>();
                physxComp?.Init(10 * forward);
            });

        spawnedProjectileCounter++;
    }

    private void SpawnDroppedBall(Vector3 forward)
    {
        if (_prefabDroppedBall == null)
        {
            Debug.LogWarning("[Player] Cannot spawn DroppedPhysxBall.");
            return;
        }

        Runner.Spawn(
            _prefabDroppedBall,
            GetSpawnPosition(),
            Quaternion.identity,
            Object.InputAuthority,
            (runner, o) =>
            {
                var dropComp = o.GetComponent<DroppedPhysxBall>();
                if (dropComp != null)
                {
                    Rigidbody rb = dropComp.GetComponent<Rigidbody>();
                    if (rb != null)
                    {
                        rb.useGravity = true;
                        rb.isKinematic = false;
                    }
                }
            });

        spawnedProjectileCounter++;
    }

    private void SpawnLobbedBall(Vector3 forward)
    {
        if (_prefabLobbedBall == null)
        {
            Debug.LogWarning("[Player] Cannot spawn LobbedPhysxBall.");
            return;
        }

        Runner.Spawn(
            _prefabLobbedBall,
            GetSpawnPosition(),
            Quaternion.LookRotation(forward),
            Object.InputAuthority,
            (runner, o) =>
            {
                var lobComp = o.GetComponent<LobbedPhysxBall>();
                lobComp?.Init(forward.normalized);
            });

        spawnedProjectileCounter++;
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
    public void RPC_SendMessage(string message, RpcInfo info = default)
    {
        RPC_RelayMessage(message, info.Source);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All, HostMode = RpcHostMode.SourceIsServer)]
    public void RPC_RelayMessage(string message, PlayerRef messageSource)
    {
        if (_messages == null) _messages = FindObjectOfType<TMP_Text>();
        if (_messages == null) return;

        _messages.text += messageSource == Runner.LocalPlayer ? $"You said: {message}\n" : $"Some other player said: {message}\n";
    }
}
