using UnityEngine;

public class BallHit : MonoBehaviour
{
    [HideInInspector] public bool consumed = false;

    private Rigidbody rb;
    private Collider col;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
    }

    // Call this when the ball impacts a player
    public void Consume()
    {
        if (consumed) return;
        consumed = true;

        // Disable collider so no more triggers
        if (col != null)
            col.enabled = false;

        // Stop all physics so it doesn't slide or linger
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        // Hide ball visuals immediately
        foreach (var r in GetComponentsInChildren<Renderer>())
            r.enabled = false;

        // Destroy shortly after to avoid Unity race conditions
        Destroy(gameObject, 0.05f);
    }
}
