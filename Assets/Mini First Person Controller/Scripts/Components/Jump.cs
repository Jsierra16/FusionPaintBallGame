using UnityEngine;

public class Jump : MonoBehaviour
{
    Rigidbody rigidbody;
    public float jumpStrength = 2;
    public event System.Action Jumped;

    [SerializeField, Tooltip("Prevents jumping when the transform is in mid-air.")]
    GroundCheck groundCheck;

    FirstPersonMovement fpm;

    private void Reset()
    {
        // default attempt to find child GroundCheck so the inspector is easier
        groundCheck = GetComponentInChildren<GroundCheck>();
    }

    private void Awake()
    {
        rigidbody = GetComponent<Rigidbody>();
        if (rigidbody == null)
        {
            rigidbody = GetComponentInParent<Rigidbody>();
        }

        if (groundCheck == null)
        {
            groundCheck = GetComponentInChildren<GroundCheck>();
        }

        fpm = GetComponent<FirstPersonMovement>();
    }

    private void Update()
    {
        // Example input check — replace with your input system if different
        if (Input.GetKeyDown(KeyCode.Space))
        {
            TryJump();
        }
    }

    public void TryJump()
    {
        // guard: need a rigidbody and groundcheck
        if (rigidbody == null)
        {
            Debug.LogWarning("[Jump] No Rigidbody found on player.");
            return;
        }

        bool grounded = groundCheck != null ? groundCheck.isGrounded : Physics.Raycast(transform.position, Vector3.down, 0.2f);

        if (!grounded)
        {
            // do nothing if not grounded
            return;
        }

        // LOCAL JUMP IMPULSE
        rigidbody.AddForce(Vector3.up * 100f * jumpStrength);

        // NETWORKED JUMP REQUEST (so server syncs vertical velocity)
        if (fpm != null && fpm.Object != null && fpm.Object.HasInputAuthority)
        {
            // match existing behavior in your FirstPersonMovement RPC naming
            fpm.RPC_RequestJump(jumpStrength * 2f);
        }

        Jumped?.Invoke();
    }
}
