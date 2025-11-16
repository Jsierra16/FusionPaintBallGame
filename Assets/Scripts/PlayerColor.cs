using Fusion;
using UnityEngine;

public class PlayerColor : NetworkBehaviour
{
    [SerializeField] private MeshRenderer meshRenderer;

    // Networked property for replication
    [Networked] public Color PlayerColorValue { get; set; }

    public override void Spawned()
    {
        // Ensure material instance (avoid global material changes)
        if (meshRenderer != null)
            meshRenderer.material = new Material(meshRenderer.material);

        // Only StateAuthority assigns the color
        if (Object.HasStateAuthority)
        {
            // Host (server) is ALWAYS red
            if (Runner.IsServer && Object.InputAuthority == Runner.LocalPlayer)
            {
                PlayerColorValue = Color.red;
            }
            else
            {
                // Every client that joins is ALWAYS blue
                PlayerColorValue = Color.blue;
            }
        }

        // Apply color immediately
        ApplyColor();
    }

    public override void Render()
    {
        // Apply color each render frame for smooth replication
        ApplyColor();
    }

    private void ApplyColor()
    {
        if (meshRenderer != null)
            meshRenderer.material.color = PlayerColorValue;
    }

    public void ForceApplyToMaterial(Material mat)
    {
        if (mat != null)
            mat.color = PlayerColorValue;
    }
}
