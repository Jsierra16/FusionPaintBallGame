using Fusion;
using UnityEngine;

public class PlayerColor : NetworkBehaviour
{
    [SerializeField] private MeshRenderer meshRenderer;

    // Networked property (Fusion 2 style)
    [Networked] public Color PlayerColorValue { get; set; }

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            // Assign a random color when spawned on the server/host
            PlayerColorValue = Random.ColorHSV(0f, 1f, 0.6f, 1f, 0.7f, 1f);
        }

        // Apply immediately when spawning
        UpdateColor();
    }

    public override void Render()
    {
        // Called every render frame — ensures smooth color updates
        UpdateColor();
    }

    private void UpdateColor()
    {
        if (meshRenderer != null)
        {
            meshRenderer.material.color = PlayerColorValue;
        }
    }
}
