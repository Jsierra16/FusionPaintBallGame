using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Movement-only FirstPersonMovement for Fusion (Rigidbody-based).
/// - Uses FixedUpdateNetwork() to run on Fusion ticks
/// - Prefers NetworkInputData via GetInput(...); falls back to local Input when owner
/// - Forwards fire inputs to WeaponManager (so weapons own shooting)
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class FirstPersonMovement : NetworkBehaviour
{
    [Header("Movement")]
    public float speed = 5f;

    [Header("Running")]
    public bool canRun = true;
    public bool IsRunning { get; private set; }
    public float runSpeed = 9f;
    public KeyCode runningKey = KeyCode.LeftShift;

    /// <summary> Functions to override movement speed. Will use the last added override. </summary>
    public List<Func<float>> speedOverrides = new List<Func<float>>();

    // Components & state
    private Rigidbody rb;
    private Vector3 _forward;

    // fallback local input axis (owner testing)
    private Vector2 fallbackAxis = Vector2.zero;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        _forward = transform.forward;
    }

    private void Update()
    {
        // Capture owner local Input for editor/testing fallback
        if (Object.HasInputAuthority)
        {
            fallbackAxis.x = Input.GetAxis("Horizontal");
            fallbackAxis.y = Input.GetAxis("Vertical");
            IsRunning = canRun && Input.GetKey(runningKey);
        }
    }

    public override void FixedUpdateNetwork()
    {
        // Determine input source
        Vector2 inputDir = Vector2.zero;
        bool usedNetworkInput = false;
        NetworkInputData netData = default;

        // Prefer network input if present
        if (GetInput(out NetworkInputData data))
        {
            netData = data;
            // assuming NetworkInputData.direction is a Vector3 (x,z used)
            inputDir = new Vector2(data.direction.x, data.direction.z);
            usedNetworkInput = true;
        }
        else if (Object.HasInputAuthority)
        {
            // fallback to local captured axis
            inputDir = fallbackAxis;
        }

        // Update running state if owner and not provided via network input
        if (!usedNetworkInput && Object.HasInputAuthority)
            IsRunning = canRun && Input.GetKey(runningKey);

        // Compute speed
        float targetMovingSpeed = IsRunning ? runSpeed : speed;
        if (speedOverrides != null && speedOverrides.Count > 0)
        {
            try { targetMovingSpeed = speedOverrides[speedOverrides.Count - 1](); }
            catch { /* ignore override errors */ }
        }

        // Apply movement (preserve Y velocity)
        Vector3 localTarget = new Vector3(inputDir.x * targetMovingSpeed, rb.linearVelocity.y, inputDir.y * targetMovingSpeed);
        Vector3 worldVel = transform.rotation * new Vector3(localTarget.x, 0f, localTarget.z);
        worldVel.y = rb.linearVelocity.y;
        rb.linearVelocity = worldVel;

        // Update forward for WeaponManager orientation
        if (inputDir.sqrMagnitude > 0.001f)
        {
            Vector3 dir = new Vector3(inputDir.x, 0f, inputDir.y);
            _forward = transform.rotation * dir.normalized;
        }

        // Forward ONLY left click to WeaponManager (owner only)
var wm = GetComponent<WeaponManager>();
if (wm != null && Object.HasInputAuthority)
{
    if (usedNetworkInput)
    {
        if (netData.buttons.IsSet(NetworkInputData.MOUSEBUTTON0))
            wm.TryFireFromInput(true); // left click only -> primary fire
        // do NOT forward MOUSEBUTTON1 if you want only left click
    }
    else
    {
        // fallback local Input for editor testing
        if (Input.GetMouseButton(0))
            wm.TryFireFromInput(true);
    }
}

    }

    // Expose forward so WeaponManager can optionally query it (or WeaponManager can use transform.forward)
    public Vector3 GetForwardDirection()
    {
        return _forward;
    }
}
