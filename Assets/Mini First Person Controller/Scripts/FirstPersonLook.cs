using UnityEngine;
using Fusion;

public class FirstPersonLook : MonoBehaviour
{
    [SerializeField] private Transform character;   // target to rotate
    public float sensitivity = 2f;
    public float smoothing = 1.5f;

    Vector2 velocity;
    Vector2 frameVelocity;

    void Reset()
    {
        var fp = GetComponentInParent<FirstPersonMovement>();
        if (fp != null)
            character = fp.transform;
    }

    void Start()
    {
        // Only lock cursor if this camera will be used by the local player.
        // If character has a NetworkObject, check input authority; otherwise assume local usage.
        if (character != null)
        {
            var netObj = character.GetComponent<NetworkObject>();
            if (netObj == null || netObj.HasInputAuthority)
                Cursor.lockState = CursorLockMode.Locked;
        }
        else
        {
            // If no character assigned yet, lock cursor — will be harmless for single-player dev.
            Cursor.lockState = CursorLockMode.Locked;
        }

        Cursor.visible = false;
    }

    void Update()
    {
        // If no character assigned yet, do nothing
        if (character == null) return;

        // If character has a NetworkObject, ensure this is the local player's character.
        var netObj = character.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            // If the character does not belong to this client, do nothing.
            if (!netObj.HasInputAuthority)
                return;
        }

        // Get raw mouse delta
        Vector2 mouseDelta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));

        // Scale by sensitivity
        Vector2 rawFrameVelocity = mouseDelta * sensitivity;

        // Smooth the frame velocity
        frameVelocity = Vector2.Lerp(frameVelocity, rawFrameVelocity, 1f / Mathf.Max(0.0001f, smoothing));

        // Integrate into total velocity
        velocity += frameVelocity;

        // Clamp pitch
        velocity.y = Mathf.Clamp(velocity.y, -90f, 90f);

        // Apply rotations:
        // Camera (this transform) rotates up/down (pitch)
        transform.localRotation = Quaternion.AngleAxis(-velocity.y, Vector3.right);

        // Character rotates left/right (yaw)
        character.localRotation = Quaternion.AngleAxis(velocity.x, Vector3.up);
    }

    /// <summary>
    /// Sets which character transform this camera should rotate
    /// </summary>
    public void SetCharacter(Transform t)
    {
        character = t;
    }
}
