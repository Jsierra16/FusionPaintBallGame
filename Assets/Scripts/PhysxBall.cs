using UnityEngine;
using Fusion;

public class PhysxBall : NetworkBehaviour
{
    [Networked] private TickTimer life { get; set; }

    [Header("Runtime settings (editable in inspector)")]
    [Tooltip("Multiplier applied to the incoming forward vector magnitude. Increase to go faster.")]
    [SerializeField] private float speedMultiplier = 6f;

    [Tooltip("How many seconds the projectile lives before being despawned.")]
    [SerializeField] private float lifeSeconds = 10f;

    // cached rigidbody
    private Rigidbody _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb == null)
            Debug.LogWarning("[PhysxBall] No Rigidbody found on PhysxBall prefab.");
    }

    /// <summary>
    /// Initialize the physx ball. Pass a forward vector (can be normalized * speed or raw direction).
    /// The final linear velocity will be: forward * speedMultiplier
    /// If you pass a vector whose magnitude encodes an initial speed (e.g. 10 * forwardDir),
    /// it will be multiplied too (resulting speed = magnitude * speedMultiplier).
    /// </summary>
  public void Init(Vector3 forward)
{
    life = TickTimer.CreateFromSeconds(Runner, lifeSeconds);

    if (_rb == null)
        _rb = GetComponent<Rigidbody>();

    if (_rb != null)
    {
        // Initial velocity
        _rb.linearVelocity = forward * speedMultiplier;

        // EXTRA LIFT so projectile travels farther 
        // (tune 3f–8f depending on how flat you want the shot)
        _rb.AddForce(Vector3.up * 5f, ForceMode.VelocityChange);

        // Optional: make it even smoother
        _rb.linearDamping = 0f;
        _rb.angularDamping = 0f;
    }
}

    public override void FixedUpdateNetwork()
    {
        if (life.Expired(Runner))
        {
            Runner.Despawn(Object);
        }
    }
}
