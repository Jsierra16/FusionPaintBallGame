using Fusion;
using Fusion.Sockets;
using Fusion.Addons.Physics;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class BasicSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField] private NetworkPrefabRef _playerPrefab;

    [Header("Camera (assign the actual Camera GameObject here, or leave empty to use Camera.main)")]
    [SerializeField] private Camera mainCamera;

    [Header("Optional: name of child transform inside the player to parent the camera to (case-insensitive)")]
    [SerializeField] private string cameraAnchorName = "CameraAnchor";

    // server-side spawned tracking
    private Dictionary<PlayerRef, NetworkObject> _spawnedCharacters = new Dictionary<PlayerRef, NetworkObject>();

    private NetworkRunner _runner;

    // ---------- Fusion callbacks ----------
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        // Server spawns the networked player object
        if (runner.IsServer)
        {
            Vector3 spawnPosition = new Vector3((player.RawEncoded % 4) * 3, 1, 0);
            NetworkObject networkPlayerObject = runner.Spawn(_playerPrefab, spawnPosition, Quaternion.identity, player);
            _spawnedCharacters.Add(player, networkPlayerObject);
            Debug.Log($"[BasicSpawner] Server spawned player for {player} -> {networkPlayerObject.name}");
        }

        // Attach camera only for the local client that owns this player
        if (runner.LocalPlayer == player)
        {
            Debug.Log("[BasicSpawner] Local player joined on this runner -> starting attach coroutine.");
            StartCoroutine(AttachCameraToLocalPlayerWhenReady(runner, player));
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

    // ---------- Input boilerplate ----------
    private bool _mouseButton0;
    private bool _mouseButton1;

    private void Update()
    {
        _mouseButton0 = _mouseButton0 || Input.GetMouseButton(0);
        _mouseButton1 = _mouseButton1 || Input.GetMouseButton(1);
    }

    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        var data = new NetworkInputData();

        if (Input.GetKey(KeyCode.W)) data.direction += Vector3.forward;
        if (Input.GetKey(KeyCode.S)) data.direction += Vector3.back;
        if (Input.GetKey(KeyCode.A)) data.direction += Vector3.left;
        if (Input.GetKey(KeyCode.D)) data.direction += Vector3.right;

        data.buttons.Set(NetworkInputData.MOUSEBUTTON0, _mouseButton0);
        _mouseButton0 = false;

        data.buttons.Set(NetworkInputData.MOUSEBUTTON1, _mouseButton1);
        _mouseButton1 = false;

        input.Set(data);
    }

    // other Fusion callbacks (empty implementations)
    public void OnInputMissing(NetworkRunner r, PlayerRef p, NetworkInput i) { }
    public void OnShutdown(NetworkRunner r, ShutdownReason s) { }
    public void OnConnectedToServer(NetworkRunner r) { }
    public void OnDisconnectedFromServer(NetworkRunner r, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner r, NetworkRunnerCallbackArgs.ConnectRequest req, byte[] token) { }
    public void OnConnectFailed(NetworkRunner r, NetAddress remote, NetConnectFailedReason reason) { }
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
    async void StartGame(GameMode mode)
    {
        _runner = gameObject.AddComponent<NetworkRunner>();
        _runner.ProvideInput = true;

        var physicsSim = gameObject.AddComponent<RunnerSimulatePhysics3D>();
        physicsSim.ClientPhysicsSimulation = ClientPhysicsSimulation.SimulateAlways;

        var scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex);

        await _runner.StartGame(new StartGameArgs()
        {
            GameMode = mode,
            SessionName = "TestRoom",
            Scene = scene,
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        });
    }

    private void OnGUI()
    {
        if (_runner == null)
        {
            if (GUI.Button(new Rect(0, 0, 200, 40), "Host")) StartGame(GameMode.Host);
            if (GUI.Button(new Rect(0, 40, 200, 40), "Join")) StartGame(GameMode.Client);
        }
    }

    // ---------- Robust camera attach coroutine ----------
    private IEnumerator AttachCameraToLocalPlayerWhenReady(NetworkRunner runner, PlayerRef player)
    {
        float timeout = 10f;           // longer timeout for slow networks
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
                        // safe check: prefer objects that report they have input authority
                        bool hasInput = false;
                        try { hasInput = no.HasInputAuthority; } catch { /* API differences might require method - this is safest */ }

                        if (hasInput)
                        {
                            // we found a local-controlled NetworkObject — assume it's our player
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

        // Resolve the actual camera instance: inspector assigned or Camera.main
        Camera cam = mainCamera != null ? mainCamera : Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[BasicSpawner] No camera assigned in inspector and Camera.main is null. Cannot attach.");
            return;
        }

        // Force tag to MainCamera so Camera.main queries work later
        cam.tag = "MainCamera";

        // Find anchor on player (case-insensitive if provided)
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

        // Fall back to player root if no anchor found
        if (parentTransform == null)
            parentTransform = playerNetworkObject.transform;

        // Parent the camera to the player; do NOT preserve world position so we control local transform
        cam.transform.SetParent(parentTransform, worldPositionStays: false);

        // Set **exact local** transform values required:
        Vector3 lp = cam.transform.localPosition;
        lp.y = 1.17f;
        lp.z = -2f;
        cam.transform.localPosition = lp;

        Vector3 lr = cam.transform.localEulerAngles;
        lr.x = 14f;
        cam.transform.localEulerAngles = lr;

        Debug.Log($"[BasicSpawner] Camera '{cam.name}' attached to '{playerNetworkObject.name}' at localPos={cam.transform.localPosition}, localRot={cam.transform.localEulerAngles}");
    }
}
