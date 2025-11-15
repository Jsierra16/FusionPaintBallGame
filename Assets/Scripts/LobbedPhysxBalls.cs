using Fusion;
using UnityEngine;

public class LobbedPhysxBall : NetworkBehaviour
{
    [Header("Explosion Settings")]
    public PhysxBall miniBallPrefab;  // same as DroppedPhysxBall
    public int miniBallCount = 8;
    public float miniBallForce = 5f;
    public float miniBallArc = 0.3f;

    private bool _exploded = false;

    private void OnCollisionEnter(Collision collision)
    {
        if (!_exploded && collision.gameObject.CompareTag("Ground"))
        {
            Explode();
        }
    }

    public void Explode()
    {
        if (_exploded) return;
        _exploded = true;

        if (miniBallPrefab != null && Runner != null)
        {
            for (int i = 0; i < miniBallCount; i++)
            {
                float angle = i * (360f / miniBallCount);
                Vector3 dir = Quaternion.Euler(0, angle, 0) * Vector3.forward;
                dir += Vector3.up * miniBallArc;
                dir.Normalize();

                Vector3 spawnPos = transform.position + dir * 0.1f;

                Runner.Spawn(
                    miniBallPrefab,
                    spawnPos,
                    Quaternion.LookRotation(dir),
                    Object.InputAuthority,
                    (runner, obj) =>
                    {
                        Rigidbody rb = obj.GetComponent<Rigidbody>();
                        if (rb != null)
                        {
                            rb.linearVelocity = Vector3.zero;
                            rb.AddForce(dir * miniBallForce, ForceMode.Impulse);
                        }
                    });
            }
        }

        // Remove the lobbed ball itself
        Runner.Despawn(Object);
    }

    // Initialize with a direction
   public void Init(Vector3 direction)
{
    Rigidbody rb = GetComponent<Rigidbody>();
    if (rb != null)
    {
        rb.useGravity = true;
        rb.isKinematic = false;

        // Adjust these values for higher arc and longer distance
        float forwardForce = 15f;  // increase from 10f
        float upwardForce = 8f;    // new upward component for higher arc

        Vector3 launchVelocity = direction.normalized * forwardForce + Vector3.up * upwardForce;
        rb.AddForce(launchVelocity, ForceMode.Impulse);
    }
}

}
