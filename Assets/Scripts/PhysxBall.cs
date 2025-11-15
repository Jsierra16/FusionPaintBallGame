using UnityEngine;
using Fusion;

public class PhysxBall : NetworkBehaviour
{
    [Networked] private TickTimer life { get; set; }

    [Header("Fired Physx Settings")]
    [SerializeField] private float speedMultiplier = 10f;
    [SerializeField] private float firedUpwardBoost = 4f;
    [SerializeField] private float lifeSeconds = 8f;

    private Rigidbody _rb;
    private Collider _coll;
    private bool _hasCollided = false;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _coll = GetComponent<Collider>();
        if (_rb == null) Debug.LogWarning("[PhysxBall] Missing Rigidbody on prefab.");
        if (_coll == null) Debug.LogWarning("[PhysxBall] Missing Collider on prefab.");
    }

    public void Init(Vector3 forward)
    {
        life = TickTimer.CreateFromSeconds(Runner, lifeSeconds);

        if (_rb != null)
        {
            _rb.isKinematic = false;
            _rb.useGravity = true;
            _rb.linearDamping = 0f;
            _rb.angularDamping = 0f;
            _rb.linearVelocity = forward * speedMultiplier;

            if (firedUpwardBoost != 0f)
                _rb.AddForce(Vector3.up * firedUpwardBoost, ForceMode.VelocityChange);
        }
        else
        {
            // fallback movement if no rigidbody (unlikely)
            transform.position += forward * speedMultiplier * Runner.DeltaTime;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_hasCollided) return;
        _hasCollided = true;

        // avoid double-fire
        if (_coll != null)
            _coll.enabled = false;

        // Here you can check collision tags to apply effects:
        // if (collision.gameObject.CompareTag("Player")) { ... }

        // Despawn on server (authoritative)
        try
        {
            if (Runner != null && Runner.IsServer)
            {
                Runner.Despawn(Object);
                return;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[PhysxBall] Failed to Runner.Despawn: " + ex.Message);
        }

        // Fallback for non-server: disable the object so it doesn't linger
        gameObject.SetActive(false);
    }

    public override void FixedUpdateNetwork()
    {
        if (life.Expired(Runner))
        {
            try { Runner.Despawn(Object); } catch { gameObject.SetActive(false); }
        }
    }
}
