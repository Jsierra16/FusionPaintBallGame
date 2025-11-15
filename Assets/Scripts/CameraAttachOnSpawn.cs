using UnityEngine;

public class CameraAttachOnSpawn : MonoBehaviour
{
    [Header("Camera to attach")]
    public Camera cameraToAttach;

    [Header("Optional: where to attach on the player")]
    public Transform cameraAnchor; // assign a point OR leave empty to use player root

    private void Start()
    {
        if (cameraToAttach == null)
        {
            Debug.LogWarning("CameraAttachOnSpawn: No camera assigned.");
            return;
        }

        // If no anchor is assigned, parent to the player itself
        Transform parent = cameraAnchor != null ? cameraAnchor : transform;

        // Parent camera to the player
        cameraToAttach.transform.SetParent(parent, false);

        // Force required local position and rotation
        cameraToAttach.transform.localPosition = new Vector3(
            cameraToAttach.transform.localPosition.x,  
            1.17f,
            -2f
        );

        cameraToAttach.transform.localEulerAngles = new Vector3(
            14f,
            cameraToAttach.transform.localEulerAngles.y,
            cameraToAttach.transform.localEulerAngles.z
        );

        // Make sure it's tagged correctly
        cameraToAttach.tag = "MainCamera";
    }
}
