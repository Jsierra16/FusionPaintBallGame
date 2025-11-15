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

    [Header("Mouse Look Settings")]
    public Transform playerCamera;      // assign the camera (child of player)
    public float mouseSensitivity = 2f;
    public float maxLookAngle = 85f;

    private float _pitch = 0f;

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
    [Networked] private int hitCount { get; set; }
    public int maxHits = 10;

    [Networked] private TickTimer delay { get; set; }
    [Networked] public byte spawnedProjectileCounter { get; set; }

[Header("Camera Settings")]
[SerializeField] private FirstPersonLook fpsLook; // Drag your main camera's FirstPersonLook here

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

        // Lock cursor for FPS
        if (playerCamera != null)
            Cursor.lockState = CursorLockMode.Locked;
    }

public override void Spawned()
{
    _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);

    if (_instanceMaterial != null)
        _instanceMaterial.color = Color.blue;

    _messages = FindObjectOfType<TMP_Text>();

    // Enable FIRST PERSON LOOK only for the local player
    if (Object.HasInputAuthority)
    {
        FirstPersonLook fpsLook = Camera.main.GetComponent<FirstPersonLook>();

        if (fpsLook != null)
        {
            fpsLook.enabled = true;
            fpsLook.SetCharacter(transform);   // NOW WORKS — using the public method
        }
    }
}




    private void Update()
    {
        if (!Object.HasInputAuthority) return;

        // Movement input
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        _inputDirection = new Vector3(h, 0, v);

        // Running
        _isRunning = canRun && Input.GetKey(runningKey);

        // Mouse look
        HandleMouseLook();

        // TMP message test
        if (Input.GetKeyDown(KeyCode.R))
            RPC_SendMessage("Hey Mate!");

        // Explode last dropped ball
        if (Input.GetKeyDown(KeyCode.X) && DroppedPhysxBall.LastSpawned != null)
            DroppedPhysxBall.LastSpawned.Explode();
    }

    private void HandleMouseLook()
    {
        if (playerCamera == null) return;

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        // Rotate player (yaw)
        transform.Rotate(Vector3.up * mouseX);

        // Rotate camera (pitch)
        _pitch -= mouseY;
        _pitch = Mathf.Clamp(_pitch, -maxLookAngle, maxLookAngle);
        playerCamera.localEulerAngles = new Vector3(_pitch, 0f, 0f);
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasInputAuthority) return;

        // --- MOVEMENT ---
        float targetSpeed = _isRunning ? runSpeed : speed;
        if (speedOverrides.Count > 0)
            targetSpeed = speedOverrides[speedOverrides.Count - 1]();

        Vector2 targetVelocity = new Vector2(_inputDirection.x * targetSpeed, _inputDirection.z * targetSpeed);
        Vector3 velocity = transform.rotation * new Vector3(targetVelocity.x, _rb.linearVelocity.y, targetVelocity.y);
        _rb.linearVelocity = velocity;

        // --- PROJECTILE SPAWN ---
        if (!HasStateAuthority || !delay.ExpiredOrNotRunning(Runner)) return;
        if (!GetInput(out NetworkInputData data)) return;
        if (!data.buttons.IsSet(NetworkInputData.MOUSEBUTTON0)) return;

        delay = TickTimer.CreateFromSeconds(Runner, 0.5f);

        Vector3 forward = transform.forward; // FPS always uses camera/player forward
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
