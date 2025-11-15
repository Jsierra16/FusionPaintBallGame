using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class HitManager : MonoBehaviour
{
    public static HitManager Instance;

    [Header("UI")]
    public TMP_Text gameOverText; // central Game Over UI (optional)

    [Header("Rules")]
    public int hitsToLose = 3; // global threshold (used if players didn't set their own)

    // player instanceID -> player component
    private Dictionary<int, PlayerHitDetectorTrigger> players = new Dictionary<int, PlayerHitDetectorTrigger>();
    // player instanceID -> hits count
    private Dictionary<int, int> playerHits = new Dictionary<int, int>();
    // player instanceID -> assigned player number (1,2,3,...)
    private Dictionary<int, int> playerNumberById = new Dictionary<int, int>();

    // number recycling
    private Queue<int> availableNumbers = new Queue<int>();
    private int nextNumber = 1;

    private bool gameOver = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (gameOverText != null)
            gameOverText.gameObject.SetActive(false);
    }

    /// <summary>
    /// Register a player and return the assigned sequential player number (1-based).
    /// </summary>
    public int RegisterPlayer(PlayerHitDetectorTrigger player)
    {
        if (player == null) return -1;

        int id = player.GetInstanceID();

        if (!players.ContainsKey(id))
        {
            players.Add(id, player);
            playerHits[id] = player.currentHits;

            // assign a player number (recycle if possible)
            int assigned;
            if (availableNumbers.Count > 0)
                assigned = availableNumbers.Dequeue();
            else
                assigned = nextNumber++;

            playerNumberById[id] = assigned;

            // set player name to "Player N" unless the prefab provided a custom name you want to keep
            player.playerName = $"Player {assigned}";
        }
        else
        {
            // refresh reference (if re-registered)
            players[id] = player;
            if (!playerNumberById.ContainsKey(id))
            {
                int assigned = availableNumbers.Count > 0 ? availableNumbers.Dequeue() : nextNumber++;
                playerNumberById[id] = assigned;
                player.playerName = $"Player {assigned}";
            }
        }

        // Ensure UI reflects correct name immediately
        player.UpdateHitsUI();

        return playerNumberById[id];
    }

    /// <summary>
    /// Unregister a player and recycle its number for future joins.
    /// Call this when the player is destroyed/disconnected.
    /// </summary>
    public void UnregisterPlayer(PlayerHitDetectorTrigger player)
    {
        if (player == null) return;
        int id = player.GetInstanceID();

        if (playerNumberById.TryGetValue(id, out int number))
        {
            // recycle number
            availableNumbers.Enqueue(number);
        }

        players.Remove(id);
        playerHits.Remove(id);
        playerNumberById.Remove(id);
    }

    // Called by PlayerHitDetectorTrigger when a hit happens
    public void RegisterHitForPlayer(PlayerHitDetectorTrigger player)
    {
        if (gameOver || player == null) return;

        int id = player.GetInstanceID();

        // ensure player is registered
        if (!playerHits.ContainsKey(id))
            playerHits[id] = player.currentHits;

        playerHits[id] += 1;
        player.currentHits = playerHits[id];
        player.UpdateHitsUI();

        int threshold = player.maxHits > 0 ? player.maxHits : hitsToLose;

        // Check if player reached threshold
        if (playerHits[id] >= threshold)
        {
            // mark game over and handle losing player
            gameOver = true;

            // Notify player to run its lose/destroy sequence
            player.OnLoseAndDestroy();

            // Show central game over UI if assigned
            if (gameOverText != null)
            {
                gameOverText.gameObject.SetActive(true);
                // try to get their assigned number for nicer text
                int number = playerNumberById.ContainsKey(id) ? playerNumberById[id] : -1;
                string name = number > 0 ? $"Player {number}" : player.playerName;
                gameOverText.text = $"{name} reached {threshold} hits!\nYaper-Dio";
            }

            // Optional cleanup
            OnGameOver(player);
        }
    }

    // Optional: custom logic on game over
    private void OnGameOver(PlayerHitDetectorTrigger loser)
    {
        // Example: disable all remaining balls in the scene to stop further hits
        var balls = GameObject.FindGameObjectsWithTag("Ball");
        foreach (var b in balls)
        {
            var bh = b.GetComponent<BallHit>();
            if (bh != null && !bh.consumed) bh.Consume();
            else Destroy(b, 0.05f);
        }

        // You can add additional logic: stop spawning, notify UI manager, switch scene, etc.
    }

    // Public reset (if you want to restart mid-game)
    public void ResetAll()
    {
        gameOver = false;
        playerHits.Clear();
        playerNumberById.Clear();
        players.Clear();
        availableNumbers.Clear();
        nextNumber = 1;

        if (gameOverText != null)
            gameOverText.gameObject.SetActive(false);
    }
}
