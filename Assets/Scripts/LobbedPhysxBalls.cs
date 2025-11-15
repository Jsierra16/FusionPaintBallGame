using UnityEngine;
using Fusion;

public class LobbedPhysxBall : NetworkBehaviour
{
    [Networked] private TickTimer life { get; set; }

    [Header("Lobbed Physx Settings")]
    [Tooltip("Base speed multiplier applied to forward vector.")]
    [SerializeField] private float speedMultiplier = 8f;

    [Tooltip("Upward boost applied to create a lob (higher -> higher arc).")]
    [SerializeField] private float lobUpwardBoost = 6f;

    [Tooltip("Lifetime in seconds before despawn.")]
    [SerializeField] private float lifeSeconds = 10f;

    private Rigidbody _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb == null)
            Debug.LogWarning("[LobbedPhysxBall] Missing Rigidbody on prefab.");
    }

    /// <summary>
    /// Init called on the server when spawned. Forward can be direction (optionally multiplied by a base speed).
    /// </summary>
    public void Init(Vector3 forward)
    {
        life = TickTimer.CreateFromSeconds(Runner, lifeSeconds);

        if (_rb == null)
            _rb = GetComponent<Rigidbody>();

        if (_rb != null)
        {
            _rb.linearDamping = 0f;
            _rb.angularDamping = 0f;
            _rb.useGravity = true;

            // set initial velocity (forward multiplied by speed multiplier)
            _rb.linearVelocity = forward * speedMultiplier;

            // apply upward boost to create the lob arc
            if (lobUpwardBoost != 0f)
                _rb.AddForce(Vector3.up * lobUpwardBoost, ForceMode.VelocityChange);
        }
        else
        {
            // fallback: move transform a small step so it's not stuck
            transform.position += forward * speedMultiplier * Runner.DeltaTime;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (life.Expired(Runner))
            Runner.Despawn(Object);
    }
}
