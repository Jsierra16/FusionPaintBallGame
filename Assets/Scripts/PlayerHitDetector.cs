using UnityEngine;
using TMPro;

public class PlayerHitDetectorTrigger : MonoBehaviour
{
    [Header("Identity (auto-assigned)")]
    public string playerName = "Player"; // HitManager overwrites this

    [Header("Per-player UI")]
    public TMP_Text hitsText; // Assign a UI text from your player prefab

    [Header("Gameplay")]
    public int maxHits = 3;

    [HideInInspector] public int currentHits = 0;

    private int assignedPlayerNumber = -1;

    private void Awake()
    {
        // Register player and get assigned number (1, 2, 3, ...)
        if (HitManager.Instance != null)
        {
            assignedPlayerNumber = HitManager.Instance.RegisterPlayer(this);

            // ✔ Move Player 2 UI to X = -200
            if (assignedPlayerNumber == 2 && hitsText != null)
            {
                RectTransform rt = hitsText.GetComponent<RectTransform>();
                if (rt != null)
                {
                    Vector2 pos = rt.anchoredPosition;
                    pos.x = -200;
                    rt.anchoredPosition = pos;
                }
            }

            UpdateHitsUI();
        }
        else
        {
            Debug.LogWarning("HitManager not found in scene.");
        }
    }

    private void OnDestroy()
    {
        if (HitManager.Instance != null)
            HitManager.Instance.UnregisterPlayer(this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Ball")) return;

        // make sure it has a BallHit component
        if (other.TryGetComponent<BallHit>(out BallHit ballHit))
        {
            if (ballHit.consumed) return;

            ballHit.Consume(); // destroy ball immediately

            HitManager.Instance?.RegisterHitForPlayer(this);
        }
    }

    public void UpdateHitsUI()
    {
        if (hitsText != null)
            hitsText.text = $"{playerName}: {currentHits}/{maxHits}";
    }

    public void IncrementHitLocal()
    {
        currentHits++;
        UpdateHitsUI();

        if (currentHits >= maxHits)
        {
            Destroy(gameObject);
        }
    }

    public void OnLoseAndDestroy()
    {
        // Add animations/effects here if needed
        Destroy(gameObject);
    }
}
