using Fusion;
using Fusion.Sockets;
using Fusion.Addons.Physics;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro; // optional

public class BasicSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField] private NetworkPrefabRef _playerPrefab;

    [Header("Camera (assign the actual Camera GameObject here, or leave empty to use Camera.main)")]
    [SerializeField] private Camera mainCamera;

    [Header("Optional: name of child transform inside the player to parent the camera to (case-insensitive)")]
    [SerializeField] private string cameraAnchorName = "CameraAnchor";

    [Header("UI (optional)")]
    [SerializeField] private TMP_Text weaponIndicator;            // assign your TMP UI text here
    [SerializeField] private string weaponIndicatorPrefix = "Weapon: ";
    [SerializeField] public TMP_Text connectionStatusText;

    // server-side spawned tracking
    private Dictionary<PlayerRef, NetworkObject> _spawnedCharacters = new Dictionary<PlayerRef, NetworkObject>();

    private NetworkRunner _runner;

    // ---------- Input state & weapon selection ----------
    private bool _mouseButton0;
    private bool _mouseButton1;
    private int _localSelectedWeapon = 0; // 0 = Ball, 1 = PhysxBall
    private int _lastDisplayedWeapon = -1;

    private readonly string[] _weaponNames = new string[] { "PhysxBall", "DroppedBall", "LobbedBall" };

    // ---------- Public UI methods (hook these to Buttons) ----------
    /// <summary>Call this from a UI Button OnClick to start a Host session.</summary>
    public void StartHost() => _ = StartGame(GameMode.Host);

    /// <summary>Call this from a UI Button OnClick to start a Client session (join).</summary>
    public void StartClient() => _ = StartGame(GameMode.Client);

    /// <summary>Optional helper: set session name from UI.</summary>
    public string SessionName = "TestRoom";

    // ---------- Fusion callbacks ----------
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"[BasicSpawner] OnPlayerJoined called. runner.LocalPlayer={runner.LocalPlayer} joinedPlayer={player} runner.IsServer={runner.IsServer}");

        // Server spawns the networked player object
        if (runner.IsServer)
        {
            Vector3 spawnPosition = new Vector3((player.RawEncoded % 4) * 3, 1, 0);
            NetworkObject networkPlayerObject = runner.Spawn(_playerPrefab, spawnPosition, Quaternion.identity, player);
            _spawnedCharacters.Add(player, networkPlayerObject);
            Debug.Log($"[BasicSpawner] Server spawned player for {player} -> {networkPlayerObject.name} (InputAuth={networkPlayerObject.InputAuthority})");
        }

        // Attach camera only for the local client that owns this player
        if (runner.LocalPlayer == player)
        {
            Debug.Log("[BasicSpawner] Local player joined on this runner -> starting attach coroutine.");
            StartCoroutine(AttachCameraToLocalPlayerWhenReady(runner, player));
        }
        else
        {
            Debug.Log("[BasicSpawner] Not the local player on this runner - skipping camera coroutine start.");
        }
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (_spawnedCharacters.TryGetValue(player, out NetworkObject networkObject))
        {
            runner.Despawn(networkObject);
            _spawnedCharacters.Remove(player);
        }
    }

    // ---------- Unity Update: polling input & local selection ----------
    private void Update()
    {
        if (Input.GetMouseButtonDown(0)) _mouseButton0 = true;
        if (Input.GetMouseButtonDown(1)) _mouseButton1 = true;

        float scroll = Input.mouseScrollDelta.y;
        if (scroll > 0f)
        {
            _localSelectedWeapon = (_localSelectedWeapon + 1) % _weaponNames.Length;
            UpdateWeaponIndicator();
        }
        else if (scroll < 0f)
        {
            _localSelectedWeapon = (_localSelectedWeapon - 1 + _weaponNames.Length) % _weaponNames.Length;
            UpdateWeaponIndicator();
        }

        if (_lastDisplayedWeapon != _localSelectedWeapon)
            UpdateWeaponIndicator();
    }

    // ---------- Pack input for Fusion every tick ----------
    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        var data = new NetworkInputData();

        if (Input.GetKey(KeyCode.W)) data.direction += Vector3.forward;
        if (Input.GetKey(KeyCode.S)) data.direction += Vector3.back;
        if (Input.GetKey(KeyCode.A)) data.direction += Vector3.left;
        if (Input.GetKey(KeyCode.D)) data.direction += Vector3.right;

        if (_mouseButton0) data.buttons.Set(NetworkInputData.MOUSEBUTTON0, true);
        if (_mouseButton1) data.buttons.Set(NetworkInputData.MOUSEBUTTON1, true);

        data.selectedWeapon = (byte)_localSelectedWeapon;

        _mouseButton0 = false;
        _mouseButton1 = false;

        input.Set(data);
    }

    // ---------- Other Fusion callbacks (empty implementations) ----------
    public void OnInputMissing(NetworkRunner r, PlayerRef p, NetworkInput i) { }
    public void OnShutdown(NetworkRunner r, ShutdownReason s) { UpdateStatus("Shutdown"); }
    public void OnConnectedToServer(NetworkRunner r) { UpdateStatus("ConnectedToServer"); }
    public void OnDisconnectedFromServer(NetworkRunner r, NetDisconnectReason reason) { UpdateStatus($"Disconnected: {reason}"); }
    public void OnConnectRequest(NetworkRunner r, NetworkRunnerCallbackArgs.ConnectRequest req, byte[] token) { }
    public void OnConnectFailed(NetworkRunner r, NetAddress remote, NetConnectFailedReason reason) { UpdateStatus($"ConnectFailed: {reason}"); }
    public void OnUserSimulationMessage(NetworkRunner r, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner r, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner r, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner r, HostMigrationToken hostMigrationToken) { }
    public void OnSceneLoadDone(NetworkRunner r) { }
    public void OnSceneLoadStart(NetworkRunner r) { }
    public void OnObjectExitAOI(NetworkRunner r, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner r, NetworkObject obj, PlayerRef player) { }
    public void OnReliableDataReceived(NetworkRunner r, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner r, PlayerRef player, ReliableKey key, float progress) { }

    // ---------- Start/Host/Join ----------
    async Task StartGame(GameMode mode)
    {
        UpdateStatus(mode == GameMode.Host ? "Starting Host..." : "Starting Client...");

        // create runner
        _runner = gameObject.GetComponent<NetworkRunner>();
        if (_runner == null) _runner = gameObject.AddComponent<NetworkRunner>();
        _runner.ProvideInput = true;
        _runner.AddCallbacks(this);

        var physicsSim = gameObject.GetComponent<RunnerSimulatePhysics3D>();
        if (physicsSim == null) physicsSim = gameObject.AddComponent<RunnerSimulatePhysics3D>();
        physicsSim.ClientPhysicsSimulation = ClientPhysicsSimulation.SimulateAlways;

        var scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex);

        try
        {
            await _runner.StartGame(new StartGameArgs()
            {
                GameMode = mode,
                SessionName = string.IsNullOrEmpty(SessionName) ? "TestRoom" : SessionName,
                Scene = scene,
                SceneManager = gameObject.GetComponent<NetworkSceneManagerDefault>() ?? gameObject.AddComponent<NetworkSceneManagerDefault>()
            });

            UpdateStatus(mode == GameMode.Host ? "Host started" : "Client started");
        }
        catch (Exception ex)
        {
            Debug.LogError("[BasicSpawner] StartGame error: " + ex);
            UpdateStatus("Start failed: " + ex.Message);
        }
    }

    private void OnDestroy()
    {
        if (_runner != null)
            _runner.RemoveCallbacks(this);
    }

    // ---------- Robust camera attach coroutine ----------
    private IEnumerator AttachCameraToLocalPlayerWhenReady(NetworkRunner runner, PlayerRef player)
    {
        float timeout = 10f;
        float elapsed = 0f;
        float pollInterval = 0.05f;

        while (elapsed < timeout)
        {
            NetworkObject playerObj = null;

            // 1) Preferred: use Fusion API
            try
            {
                playerObj = runner.GetPlayerObject(player);
            }
            catch
            {
                playerObj = null;
            }

            // 2) Fallback on server: if we're server and tracked in _spawnedCharacters
            if (playerObj == null && runner.IsServer)
            {
                if (_spawnedCharacters.TryGetValue(player, out NetworkObject serverObj))
                {
                    playerObj = serverObj;
                }
            }

            // 3) Fallback on client: search scene for a NetworkObject that has input authority (local player object)
            if (playerObj == null)
            {
                try
                {
                    var all = GameObject.FindObjectsOfType<NetworkObject>();
                    foreach (var no in all)
                    {
                        bool hasInput = false;
                        try { hasInput = no.HasInputAuthority; } catch { /*safe*/ }

                        if (hasInput)
                        {
                            playerObj = no;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[BasicSpawner] Exception while scanning NetworkObjects: " + ex.Message);
                }
            }

            if (playerObj != null)
            {
                bool isLocalAuthority = false;
                try { isLocalAuthority = playerObj.HasInputAuthority; } catch { isLocalAuthority = false; }

                Debug.Log($"[BasicSpawner] Candidate playerObj found: {playerObj.name} (InputAuth={playerObj.InputAuthority}) hasInput={isLocalAuthority} runner.LocalPlayer={runner.LocalPlayer} targetPlayer={player}");

                if (!isLocalAuthority)
                {
                    elapsed += pollInterval;
                    yield return new WaitForSeconds(pollInterval);
                    continue;
                }

                AttachAndConfigureCamera(playerObj);
                yield break;
            }

            elapsed += pollInterval;
            yield return new WaitForSeconds(pollInterval);
        }

        Debug.LogWarning("[BasicSpawner] Timed out waiting for local player's NetworkObject.");
    }

    private void AttachAndConfigureCamera(NetworkObject playerNetworkObject)
    {
        if (playerNetworkObject == null)
        {
            Debug.LogWarning("[BasicSpawner] AttachAndConfigureCamera called with null playerNetworkObject.");
            return;
        }

        Camera cam = mainCamera != null ? mainCamera : Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[BasicSpawner] No camera assigned in inspector and Camera.main is null. Cannot attach.");
            return;
        }

        if (cam.transform.parent == playerNetworkObject.transform)
        {
            Debug.Log("[BasicSpawner] Camera already parented to this player object — skipping.");
            return;
        }

        cam.tag = "MainCamera";

        Transform parentTransform = null;
        if (!string.IsNullOrEmpty(cameraAnchorName))
        {
            var found = playerNetworkObject.transform.Find(cameraAnchorName);
            if (found != null)
                parentTransform = found;
            else
            {
                foreach (Transform t in playerNetworkObject.transform)
                {
                    if (string.Equals(t.name, cameraAnchorName, StringComparison.OrdinalIgnoreCase))
                    {
                        parentTransform = t;
                        break;
                    }
                }
            }
        }

        if (parentTransform == null)
            parentTransform = playerNetworkObject.transform;

        cam.transform.SetParent(parentTransform, worldPositionStays: false);

        Vector3 lp = cam.transform.localPosition;
        lp.y = 1.17f;
        lp.z = -2f;
        cam.transform.localPosition = lp;

        Vector3 lr = cam.transform.localEulerAngles;
        lr.x = 14f;
        cam.transform.localEulerAngles = lr;

        Debug.Log($"[BasicSpawner] Camera '{cam.name}' attached to '{playerNetworkObject.name}' at localPos={cam.transform.localPosition}, localRot={cam.transform.localEulerAngles}");
    }

    // ---------- Helper: update HUD indicator ----------
    private void UpdateWeaponIndicator()
    {
        _lastDisplayedWeapon = _localSelectedWeapon;

        if (weaponIndicator != null)
        {
            string name = (_localSelectedWeapon >= 0 && _localSelectedWeapon < _weaponNames.Length) ? _weaponNames[_localSelectedWeapon] : "Unknown";
            weaponIndicator.text = $"{weaponIndicatorPrefix}{name}";
        }
    }

    private void UpdateStatus(string s)
    {
        if (connectionStatusText != null)
            connectionStatusText.text = s;
        Debug.Log("[BasicSpawner] " + s);
    }
}
