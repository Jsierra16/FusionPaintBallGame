using UnityEngine;

[ExecuteInEditMode]
public class GroundCheck : MonoBehaviour
{
    [Header("Ground Check")]
    [Tooltip("Layers considered ground. Leave empty to automatically use layer named 'Ground' if it exists.")]
    public LayerMask groundLayers = 0;

    [Tooltip("Radius of the sphere used to detect ground.")]
    public float radius = 0.25f;

    [Tooltip("Vertical offset from transform.position to perform the check from.")]
    public float originYOffset = 0.1f;

    [Tooltip("Max distance below origin to consider grounded.")]
    public float maxDistance = 0.15f;

    [Tooltip("Frequency in seconds to update the ground query in edit mode.")]
    public float updateInterval = 0.05f;

    [HideInInspector]
    public bool isGrounded = false;

    public event System.Action Grounded;

    private float _nextUpdate = 0f;

    private void Awake()
    {
        // If the inspector left the LayerMask at 0, try to auto-select the "Ground" layer
        if (groundLayers == 0)
        {
            int groundLayerIndex = LayerMask.NameToLayer("Ground");
            if (groundLayerIndex >= 0)
            {
                groundLayers = 1 << groundLayerIndex;
            }
            else
            {
                // fallback to everything so we don't accidentally ignore ground entirely
                groundLayers = ~0;
            }
        }
    }

    private void Reset()
    {
        // sensible defaults when adding the component
        radius = 0.25f;
        originYOffset = 0.1f;
        maxDistance = 0.15f;
        groundLayers = 0; // allow Awake to resolve to "Ground" automatically
    }

    private void Update()
    {
        // Keep updating in edit mode too, but throttle it
        if (Application.isEditor)
        {
            if (Time.realtimeSinceStartup >= _nextUpdate)
            {
                _nextUpdate = Time.realtimeSinceStartup + Mathf.Max(0.01f, updateInterval);
                RunCheck();
            }
        }
        else
        {
            RunCheck();
        }
    }

    private void RunCheck()
    {
        Vector3 origin = transform.position + Vector3.up * originYOffset;
        // center of sphere at the bottom of the origin offset (we want to check below feet)
        Vector3 sphereCenter = origin + Vector3.down * (maxDistance + radius);

        // Physics.OverlapSphere is simple and robust for small character controllers
        Collider[] hits = Physics.OverlapSphere(sphereCenter, radius, groundLayers);
        bool groundedNow = false;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i];
            if (c == null) continue;
            // ignore triggers by default
            if (c.isTrigger) continue;

            groundedNow = true;
            break;
        }

        if (!isGrounded && groundedNow)
        {
            Grounded?.Invoke();
        }

        isGrounded = groundedNow;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 origin = transform.position + Vector3.up * originYOffset;
        Vector3 sphereCenter = origin + Vector3.down * (maxDistance + radius);

        Gizmos.color = isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(sphereCenter, radius);

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(origin, origin + Vector3.down * (maxDistance + radius));
    }
}
