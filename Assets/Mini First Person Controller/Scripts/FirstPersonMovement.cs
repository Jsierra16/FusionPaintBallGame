using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using TMPro;

/// <summary>
/// FirstPersonMovement - owner-predicted + server-authoritative movement using Fusion.
/// - Prefers external GroundCheck component when assigned (consistent with Jump.cs)
/// - Falls back to an internal spherecast ground snap otherwise
/// - Resolves penetration using Physics.ComputePenetration to avoid falling through ground / spawning intersecting
/// - Uses Rigidbody.MovePosition for movement (safer cross-Unity versions)
/// </summary>
public class FirstPersonMovement : NetworkBehaviour
{
    [Header("Movement")]
    public float speed = 5f;

    [Header("Running")]
    public bool canRun = true;
    public float runSpeed = 9f;
    public KeyCode runningKey = KeyCode.LeftShift;
    public bool IsRunning { get; private set; }

    // Delegate-based speed overrides. Each entry is Func<float> returning an alternate speed.
    public List<Func<float>> speedOverrides = new List<Func<float>>();

    [Header("References (assign in prefab)")]
    [SerializeField] private Rigidbody _rb;
    [SerializeField] private Collider _playerCollider;
    [SerializeField] private Transform _projectileSpawnPoint;
    [SerializeField] private Transform _spawnPoint;

    [Header("Optional external grounding")]
    [Tooltip("Optional: reference to external GroundCheck component (if assigned, FPM will use it instead of doing its own spherecast).")]
    public GroundCheck groundCheck;

    [Header("Prediction / Reconciliation")]
    public bool enableClientPrediction = true;
    public float positionSnapThreshold = 1.0f;
    public float positionSoftThreshold = 0.15f;
    public float positionLerpSpeed = 10f;
    public float rotationLerpSpeed = 15f;
    public float verticalSoftLerpSpeed = 5f;

    // --- Internal fallback ground snap tunables (only used if groundCheck == null)
    [Header("Ground / Internal Snap (fallback)")]
    [Tooltip("How far above ground we'll try to snap the player.")]
    public float groundSnapDistance = 0.25f;
    [Tooltip("Radius used for spherecasts when snapping to ground.")]
    public float groundCheckRadius = 0.25f;
    [Tooltip("Layers to consider ground (set to the 'Ground' layer mask in inspector).")]
    public LayerMask groundLayers = ~0;

    [Header("Jump / Gravity")]
    public float gravity = -9.81f;
    public float jumpVelocity = 5f;

    [Header("Optional Networked Prefabs (assign if using spawning)")]
    [SerializeField] private NetworkPrefabRef prefabPhysxBall;
    [SerializeField] private NetworkPrefabRef prefabDroppedBall;
    [SerializeField] private NetworkPrefabRef prefabLobbedBall;

    // networked authoritative pose
    [Networked] private Vector3 ServerPosition { get; set; }
    [Networked] private Quaternion ServerRotation { get; set; }

    // Some networked gameplay state left in in case other systems expect it
    [Networked] private TickTimer delay { get; set; }
    [Networked] public byte spawnedProjectileCounter { get; set; }

    // internal prediction state
    private Vector3 _predictedVelocity = Vector3.zero;
    private float _verticalVelocity = 0f;
    private bool _jumpRequested = false;

    // rendering interpolation state for remote clients
    private Vector3 _renderPosition;
    private Quaternion _renderRotation;

    private TMP_Text _messages;

    private void Reset()
    {
        if (_rb == null) _rb = GetComponent<Rigidbody>();
        if (_playerCollider == null) _playerCollider = GetComponent<Collider>();
    }

    private void Awake()
    {
        if (_rb == null) _rb = GetComponent<Rigidbody>();
        if (_playerCollider == null) _playerCollider = GetComponent<Collider>();

        _renderPosition = transform.position;
        _renderRotation = transform.rotation;
    }

    public override void Spawned()
    {
        Debug.Log($"[FPM] Spawned() HasInputAuthority={Object.HasInputAuthority} HasStateAuthority={Object.HasStateAuthority}");

        _messages = FindObjectOfType<TMP_Text>();

        // Owner uses active physics; non-owners are kinematic
        if (Object.HasInputAuthority)
        {
            if (_rb != null)
            {
                _rb.isKinematic = false;
                _rb.interpolation = RigidbodyInterpolation.Interpolate;
                _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                _rb.constraints = RigidbodyConstraints.FreezeRotation;
            }
        }
        else
        {
            if (_rb != null)
                _rb.isKinematic = true;
        }

        if (HasStateAuthority)
        {
            ServerPosition = transform.position;
            ServerRotation = transform.rotation;
        }
    }

    private void Update()
    {
        // Only owner handles local input/UX like immediate jump request and running flag
        if (!Object.HasInputAuthority) return;

        // Jump input (local feel) - also ping server via RPC
        if (Input.GetKeyDown(KeyCode.Space))
        {
            _jumpRequested = true;
            RPC_RequestJump(jumpVelocity);
        }

        IsRunning = canRun && Input.GetKey(runningKey);

        // debug message
        if (Input.GetKeyDown(KeyCode.R))
        {
            RPC_SendMessage("Hello from client");
        }
    }

    public override void FixedUpdateNetwork()
    {
        base.FixedUpdateNetwork();

        // Try to read Fusion input for this tick
        bool hasInput = GetInput(out NetworkInputData input);

        if (!hasInput)
        {
            // If there's no network input this tick, still let the server advance authoritative sim
            if (HasStateAuthority)
            {
                ServerSimulateNoInput(Runner.DeltaTime);
                ServerPosition = transform.position;
                ServerRotation = transform.rotation;
            }
            return;
        }

        Vector3 inputDirection = input.direction;
        if (inputDirection.sqrMagnitude > 1f) inputDirection.Normalize();

        // Owner prediction
        if (Object.HasInputAuthority && enableClientPrediction)
        {
            ApplyLocalPrediction(inputDirection, Runner.DeltaTime);
        }

        // Server authoritative simulation
        if (HasStateAuthority)
        {
            SimulateServerMovement(inputDirection, Runner.DeltaTime);
            ServerPosition = transform.position;
            ServerRotation = transform.rotation;
        }
    }

    // ---------------- Owner prediction (applied on InputAuthority) ----------------
    private void ApplyLocalPrediction(Vector3 inputDir, float dt)
    {
        if (inputDir.sqrMagnitude > 1f) inputDir.Normalize();

        float targetSpeed = GetCurrentTargetSpeed();
        Vector3 horizontal = transform.rotation * new Vector3(inputDir.x * targetSpeed, 0f, inputDir.z * targetSpeed);

        // preserve horizontal velocities
        _predictedVelocity = new Vector3(horizontal.x, _predictedVelocity.y, horizontal.z);

        if (_jumpRequested)
        {
            _verticalVelocity = jumpVelocity;
            _jumpRequested = false;
        }

        // gravity integration
        _verticalVelocity += gravity * dt;

        Vector3 final = new Vector3(_predictedVelocity.x, _verticalVelocity, _predictedVelocity.z);

        if (_rb != null && !_rb.isKinematic)
        {
            Vector3 newPos = _rb.position + final * dt;
            _rb.MovePosition(newPos);
        }
        else
        {
            transform.position += final * dt;
        }
    }

    // ---------------- Server authoritative simulation ----------------
    private void SimulateServerMovement(Vector3 inputDir, float dt)
    {
        if (inputDir.sqrMagnitude > 1f) inputDir.Normalize();

        float targetSpeed = GetCurrentTargetSpeed();
        Vector3 horizontal = transform.rotation * new Vector3(inputDir.x * targetSpeed, 0f, inputDir.z * targetSpeed);

        // vertical integration
        _verticalVelocity += gravity * dt;

        // proposed new position
        Vector3 proposed = transform.position + new Vector3(horizontal.x, _verticalVelocity, horizontal.z) * dt;

        // Ground handling: use external GroundCheck if provided, else internal spherecast
        if (groundCheck != null)
        {
            // If external groundCheck says we're grounded, find the actual ground Y and snap if necessary
            if (groundCheck.isGrounded)
            {
                float rayStartOffset = 0.5f;
                float rayLength = rayStartOffset + groundCheck.maxDistance + groundCheck.radius;
                Vector3 rayOrigin = proposed + Vector3.up * rayStartOffset;

                if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, rayLength, groundCheck.groundLayers, QueryTriggerInteraction.Ignore))
                {
                    float groundY = hit.point.y + 0.01f;
                    if (proposed.y <= groundY + 0.001f && _verticalVelocity <= 0f)
                    {
                        proposed.y = groundY;
                        _verticalVelocity = 0f;
                    }
                }
            }
            // else: not grounded; let integration proceed
        }
        else
        {
            // Internal spherecast fallback
            float checkUp = 0.2f;
            float checkDown = Mathf.Max(groundSnapDistance * 2f, 0.5f);
            Vector3 checkOrigin = proposed + Vector3.up * checkUp;

            if (Physics.SphereCast(checkOrigin, groundCheckRadius, Vector3.down, out RaycastHit hit, checkDown, groundLayers, QueryTriggerInteraction.Ignore))
            {
                float groundY = hit.point.y + 0.01f;
                if (proposed.y <= groundY + 0.001f && _verticalVelocity <= 0f)
                {
                    proposed.y = groundY;
                    _verticalVelocity = 0f;
                }
            }
        }

        // Resolve penetration before applying position
        Vector3 adjustedProposed = ResolvePenetration(proposed);

        // Apply position
        if (_rb != null)
        {
            if (!_rb.isKinematic)
            {
                _rb.MovePosition(adjustedProposed);
            }
            else
            {
                transform.position = adjustedProposed;
            }
        }
        else
        {
            transform.position = adjustedProposed;
        }
    }

    // Minimal server sim when no input is present (keeps vertical integration)
    private void ServerSimulateNoInput(float dt)
    {
        if (!HasStateAuthority) return;
        _verticalVelocity += gravity * dt;
        Vector3 proposed = transform.position + new Vector3(0f, _verticalVelocity * dt, 0f);

        // Try to snap to ground similarly as above
        if (groundCheck != null)
        {
            if (groundCheck.isGrounded)
            {
                float rayStartOffset = 0.5f;
                float rayLength = rayStartOffset + groundCheck.maxDistance + groundCheck.radius;
                Vector3 rayOrigin = proposed + Vector3.up * rayStartOffset;

                if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, rayLength, groundCheck.groundLayers, QueryTriggerInteraction.Ignore))
                {
                    float groundY = hit.point.y + 0.01f;
                    if (proposed.y <= groundY + 0.001f && _verticalVelocity <= 0f)
                    {
                        proposed.y = groundY;
                        _verticalVelocity = 0f;
                    }
                }
            }
        }
        else
        {
            float checkUp = 0.2f;
            float checkDown = Mathf.Max(groundSnapDistance * 2f, 0.5f);
            Vector3 checkOrigin = proposed + Vector3.up * checkUp;

            if (Physics.SphereCast(checkOrigin, groundCheckRadius, Vector3.down, out RaycastHit hit, checkDown, groundLayers, QueryTriggerInteraction.Ignore))
            {
                float groundY = hit.point.y + 0.01f;
                if (proposed.y <= groundY + 0.001f && _verticalVelocity <= 0f)
                {
                    proposed.y = groundY;
                    _verticalVelocity = 0f;
                }
            }
        }

        Vector3 adjusted = ResolvePenetration(proposed);

        if (_rb != null && !_rb.isKinematic)
        {
            _rb.MovePosition(adjusted);
        }
        else
        {
            transform.position = adjusted;
        }
    }

    public override void Render()
    {
        // Reconciliation & smoothing
        Vector3 serverPos = ServerPosition;
        Quaternion serverRot = ServerRotation;

        if (Object.HasInputAuthority)
        {
            float dist = Vector3.Distance(transform.position, serverPos);

            if (dist > positionSnapThreshold)
            {
                // big desync -> snap to server
                transform.position = serverPos;
            }
            else if (dist > positionSoftThreshold)
            {
                // soft correction
                Vector3 corrected = Vector3.Lerp(transform.position, serverPos, positionLerpSpeed * Time.deltaTime);

                float yDelta = Math.Abs(transform.position.y - serverPos.y);
                if (yDelta > 0.01f)
                {
                    corrected.y = Mathf.Lerp(transform.position.y, serverPos.y, verticalSoftLerpSpeed * Time.deltaTime);
                }

                transform.position = corrected;
            }

            transform.rotation = Quaternion.Slerp(transform.rotation, serverRot, rotationLerpSpeed * Time.deltaTime);
        }
        else
        {
            // remote interpolation
            _renderPosition = Vector3.Lerp(_renderPosition, serverPos, positionLerpSpeed * Time.deltaTime);
            _renderRotation = Quaternion.Slerp(_renderRotation, serverRot, rotationLerpSpeed * Time.deltaTime);
            transform.SetPositionAndRotation(_renderPosition, _renderRotation);
        }
    }

    // ---------------- RPCs ----------------

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestJump(float initialVelocity, RpcInfo info = default)
    {
        if (!HasStateAuthority) return;
        _verticalVelocity = initialVelocity;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestSpawn(int selectedWeapon, Vector3 forward, RpcInfo info = default)
    {
        if (!HasStateAuthority) return;
        if (!delay.ExpiredOrNotRunning(Runner)) return;

        delay = TickTimer.CreateFromSeconds(Runner, 0.5f);

        NetworkPrefabRef prefabToSpawn = default;
        switch (selectedWeapon)
        {
            case 0: prefabToSpawn = prefabPhysxBall; break;
            case 1: prefabToSpawn = prefabDroppedBall; break;
            case 2: prefabToSpawn = prefabLobbedBall; break;
        }

        if (prefabToSpawn.IsValid == false)
        {
            Debug.LogWarning("[FPM] Selected prefab is not assigned.");
            return;
        }

        Vector3 spawnPos = GetSpawnPosition();
        Runner.Spawn(prefabToSpawn, spawnPos, Quaternion.LookRotation(forward), Object.InputAuthority);
        spawnedProjectileCounter++;
    }

    private Vector3 GetSpawnPosition()
    {
        if (_projectileSpawnPoint != null) return _projectileSpawnPoint.position;
        return transform.position + transform.forward * 1.0f + Vector3.up * 0.5f;
    }

    // Simple chat RPCs for compatibility
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SendMessage(string message, RpcInfo info = default)
    {
        if (Object.HasStateAuthority)
        {
            RPC_RelayMessage(message, info.Source);
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_RelayMessage(string message, PlayerRef messageSource, RpcInfo info = default)
    {
        if (_messages == null) _messages = FindObjectOfType<TMP_Text>();
        if (_messages == null) return;
        bool isLocal = (messageSource == Runner.LocalPlayer);
        _messages.text += isLocal ? $"You said: {message}\n" : $"Player {messageSource.PlayerId} said: {message}\n";
    }

    // ---------------- Helpers ----------------

    private float GetCurrentTargetSpeed()
    {
        float baseSpeed = IsRunning ? runSpeed : speed;

        if (speedOverrides != null && speedOverrides.Count > 0)
        {
            int last = Math.Max(0, speedOverrides.Count - 1);
            try
            {
                var f = speedOverrides[last];
                if (f != null) return f();
            }
            catch (Exception)
            {
                // if delegate throws, ignore and use base
            }
        }

        return baseSpeed;
    }

    // --- Penetration resolution helper ---
    // Given a proposed world position for this object, adjust it so the player's collider is not penetrating nearby colliders.
    // Returns an adjusted position (same as input if no penetration).
    Vector3 ResolvePenetration(Vector3 proposedPosition)
    {
        if (_playerCollider == null) return proposedPosition;

        // Prefer capsule collider handling (typical for player characters)
        var cap = _playerCollider as CapsuleCollider;
        if (cap != null)
        {
            // Compute offset from current transform position to the proposed position
            Vector3 offset = proposedPosition - transform.position;

            // Local points for capsule: center +/- up * (height/2 - radius)
            float halfHeight = Mathf.Max(0f, (cap.height * 0.5f) - cap.radius);
            Vector3 localTop = cap.center + Vector3.up * halfHeight;
            Vector3 localBottom = cap.center - Vector3.up * halfHeight;

            // World points for the capsule after applying the offset
            Vector3 worldTop = cap.transform.TransformPoint(localTop) + offset;
            Vector3 worldBottom = cap.transform.TransformPoint(localBottom) + offset;

            // World radius (accounts for X/Z scale)
            float worldRadius = cap.radius * Mathf.Max(cap.transform.lossyScale.x, cap.transform.lossyScale.z);

            // Find overlapping colliders with this capsule shape
            Collider[] overlaps = Physics.OverlapCapsule(worldBottom, worldTop, worldRadius, groundLayers, QueryTriggerInteraction.Ignore);

            foreach (var other in overlaps)
            {
                if (other == cap) continue;

                // Compute penetration between capsule (moved to proposed) and the other collider
                bool ok = Physics.ComputePenetration(
                    cap,                                 // collider A
                    cap.transform.position + offset,     // position A (move capsule to proposed)
                    cap.transform.rotation,              // rotation A
                    other,                               // collider B
                    other.transform.position,            // position B
                    other.transform.rotation,            // rotation B
                    out Vector3 direction,               // minimal direction to separate (from A to B)
                    out float distance                   // penetration depth
                );

                if (ok && distance > 0.0001f)
                {
                    Vector3 mtv = direction * distance;
                    proposedPosition += mtv;
                    Debug.Log($"[FPM][Penetration] resolved {distance:F4} with '{other.name}' by moving {mtv}. NewPos={proposedPosition}");
                }
            }

            return proposedPosition;
        }
        else
        {
            // Fallback for other collider types: overlap-sphere near bottom of collider
            Bounds b = _playerCollider.bounds;
            Vector3 checkCenter = proposedPosition + Vector3.up * (b.extents.y * 0.5f);
            float checkRadius = Mathf.Min(b.extents.x, b.extents.z) * 0.9f;

            Collider[] overlaps = Physics.OverlapSphere(checkCenter, checkRadius, groundLayers, QueryTriggerInteraction.Ignore);
            if (overlaps.Length == 0) return proposedPosition;

            Vector3 adjusted = proposedPosition;
            int iter = 0;
            while (iter < 6)
            {
                bool any = false;
                foreach (var o in overlaps)
                {
                    if (o == _playerCollider) continue;
                    Vector3 closest = o.ClosestPoint(adjusted);
                    Vector3 myClosest = _playerCollider.ClosestPoint(closest);
                    Vector3 diff = myClosest - closest;
                    float dist = diff.magnitude;
                    if (dist < 0.001f)
                    {
                        adjusted += Vector3.up * 0.02f;
                        any = true;
                    }
                }
                if (!any) break;
                iter++;
                overlaps = Physics.OverlapSphere(checkCenter, checkRadius, groundLayers, QueryTriggerInteraction.Ignore);
            }
            return adjusted;
        }
    }
}
