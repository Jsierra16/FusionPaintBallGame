using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SpawnerUI : MonoBehaviour
{
    [SerializeField] private BasicSpawner spawner;
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private TMP_Text statusText;

    [Header("UI Root")]
    [SerializeField] private Canvas mainCanvas;     // assign your whole menu canvas here

    private void Awake()
    {
        hostButton.onClick.AddListener(OnHostPressed);
        joinButton.onClick.AddListener(OnJoinPressed);
    }

    private void Start()
    {
        // Give status text to spawner
        spawner.connectionStatusText = statusText;  
    }

    private void OnHostPressed()
    {
        mainCanvas.enabled = false;    // 🔥 hide UI
        spawner.StartHost();
    }

    private void OnJoinPressed()
    {
        mainCanvas.enabled = false;    // 🔥 hide UI
        spawner.StartClient();
    }
}
