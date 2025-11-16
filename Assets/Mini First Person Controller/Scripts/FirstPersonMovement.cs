using System;
using System.Collections.Generic;
using System.Reflection;
using Fusion;
using UnityEngine;
using TMPro;

/// <summary>
/// FirstPersonMovement - owner-predicted movement for a Rigidbody player using Fusion.
/// Reads Fusion network input for the local player using Runner.GetInputForPlayer&lt;NetworkInputData&gt; when available.
/// Disables NetworkTransform on the owner so local physics isn't overwritten.
/// </summary>
public class FirstPersonMovement : NetworkBehaviour
{
    [Header("Movement")]
    public float speed = 5f;
    public float acceleration = 20f;

    [Header("References")]
    [Tooltip("Rigidbody attached to the player root.")]
    public Rigidbody rb;

    [Tooltip("Optional camera or look root (for rotation visuals only).")]
    public Transform renderRoot;

    [Header("Debug / UI")]
    public TMP_Text messages;

    Camera localCamera;

    [Header("Compatibility")]
    public bool IsRunningPublicInspector;
    public bool IsRunning { get; private set; } = false;

    [Serializable]
    public class SpeedOverrides
    {
        public float walk = 1f;
        public float run = 1.6f;
        public float crouch = 0.5f;
        public float sprint = 2f;
        public float air = 0.85f;
        public List<string> keys = new List<string>();
        [NonSerialized] private Dictionary<string, float> namedValues = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        [NonSerialized] private List<Func<float>> funcOverrides = new List<Func<float>>();

        public bool TryGetValue(string key, out float value)
        {
            if (string.IsNullOrEmpty(key)) { value = walk; return false; }
            switch (key.ToLowerInvariant())
            {
                case "walk": value = walk; return true;
                case "run": value = run; return true;
                case "crouch": value = crouch; return true;
                case "sprint": value = sprint; return true;
                case "air": value = air; return true;
            }
            if (namedValues.TryGetValue(key, out value)) return true;
            value = walk; return false;
        }

        public float this[string key]
        {
            get { if (TryGetValue(key, out float v)) return v; return walk; }
            set
            {
                if (string.IsNullOrEmpty(key)) return;
                switch (key.ToLowerInvariant())
                {
                    case "walk": walk = value; return;
                    case "run": run = value; return;
                    case "crouch": crouch = value; return;
                    case "sprint": sprint = value; return;
                    case "air": air = value; return;
                }
                namedValues[key] = value;
                if (!keys.Contains(key)) keys.Add(key);
            }
        }

        public bool Contains(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            switch (key.ToLowerInvariant())
            {
                case "walk": case "run": case "crouch": case "sprint": case "air": return true;
            }
            if (keys != null && keys.Contains(key)) return true;
            if (namedValues != null && namedValues.ContainsKey(key)) return true;
            return false;
        }

        public void Add(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (keys == null) keys = new List<string>();
            if (!keys.Contains(key)) keys.Add(key);
            if (!namedValues.ContainsKey(key)) namedValues[key] = walk;
        }

        public bool Remove(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            bool removed = false;
            if (keys != null) removed = keys.Remove(key) || removed;
            if (namedValues != null) removed = namedValues.Remove(key) || removed;
            return removed;
        }

        public List<string> GetKeys() => new List<string>(keys ?? new List<string>());

        public bool Contains(Func<float> func) => func != null && funcOverrides != null && funcOverrides.Contains(func);
        public void Add(Func<float> func) { if (func == null) return; if (funcOverrides == null) funcOverrides = new List<Func<float>>(); if (!funcOverrides.Contains(func)) funcOverrides.Add(func); }
        public bool Remove(Func<float> func) { if (func == null || funcOverrides == null) return false; return funcOverrides.Remove(func); }
        public List<Func<float>> GetFunctionOverrides() => new List<Func<float>>(funcOverrides ?? new List<Func<float>>());
    }

    public SpeedOverrides speedOverrides = new SpeedOverrides();

    // internal movement state
    Vector3 _velocityTarget;
    Vector3 _localVelocity;

    private void Reset()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (renderRoot == null && transform.childCount > 0) renderRoot = transform.GetChild(0);
    }

    private void Awake()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (rb == null) Debug.LogError("[FirstPersonMovement] No Rigidbody assigned or found on the object.");
        if (messages == null) messages = FindObjectOfType<TMP_Text>();
    }

    public override void Spawned()
    {
        Debug.Log($"[FPM] Spawned() called. HasInputAuthority={Object.HasInputAuthority} Runner.LocalPlayer={Runner.LocalPlayer}");

        // ensure Rigidbody exists & allow movement (freeze rotation only)
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.useGravity = true;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
        }

        // owner vs remote physics
        if (Object.HasInputAuthority)
        {
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }
        }
        else
        {
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            }
        }

        // enable/disable child cameras only (BasicSpawner handles local camera prefab)
        Camera[] childCams = GetComponentsInChildren<Camera>(true);
        foreach (Camera c in childCams)
        {
            c.enabled = Object.HasInputAuthority;
            try { if (c.enabled) c.tag = "MainCamera"; else if (c.tag == "MainCamera") c.tag = "Untagged"; } catch { }
            if (c.enabled && localCamera == null) localCamera = c;
        }

        // disable NetworkTransform on owner so local physics isn't overwritten
        TryToggleNetworkTransform(!Object.HasInputAuthority);

        _localVelocity = Vector3.zero;
        _velocityTarget = Vector3.zero;

        Debug.Log("[FPM] Spawned() finished.");
    }

    void TryToggleNetworkTransform(bool enable)
    {
        try
        {
            Type ntType = null;
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name == "NetworkTransform")
                        {
                            ntType = t;
                            break;
                        }
                    }
                }
                catch { }
                if (ntType != null) break;
            }

            if (ntType == null) return;

            var ntComp = GetComponent(ntType) as Component;
            if (ntComp == null) return;

            // Use UnityEngine.Behaviour explicit to avoid ambiguity with Fusion.Behaviour
            var behaviour = ntComp as UnityEngine.Behaviour;
            if (behaviour != null)
            {
                behaviour.enabled = enable;
                Debug.Log($"[FPM][NT] NetworkTransform {(behaviour.enabled ? "ENABLED (remote)" : "DISABLED (owner)")}");
            }
            else
            {
                Debug.LogWarning("[FPM][NT] NetworkTransform exists but could not be cast to UnityEngine.Behaviour.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[FPM][NT] Exception while toggling NetworkTransform: " + ex.Message);
        }
    }

    // FixedUpdateNetwork: prefer Fusion network input via Runner.GetInputForPlayer<NetworkInputData>
    public override void FixedUpdateNetwork()
    {
        base.FixedUpdateNetwork();

        if (!Object.HasInputAuthority)
            return; // remote players driven by NetworkTransform

        bool hasNetInput = false;
        Vector3 netDirection = Vector3.zero;

        // Try to read Fusion input for the owner using Runner.GetInputForPlayer<T>()
        try
        {
            // Object.InputAuthority is the PlayerRef that has input for this object.
            // Runner.GetInputForPlayer<T> returns a nullable T? where T must be unmanaged & INetworkInput.
            // NetworkInputData in your project (used in BasicSpawner.OnInput) should satisfy those constraints.
            var maybe = Runner.GetInputForPlayer<NetworkInputData>(Object.InputAuthority);
            if (maybe.HasValue)
            {
                netDirection = maybe.Value.direction;
                hasNetInput = true;
            }
        }
        catch (Exception ex)
        {
            // If this fails (type mismatch or method unavailable), fall back to Unity Input below.
            Debug.Log("[FPM] Runner.GetInputForPlayer attempt failed (will fallback to Unity Input). " + ex.Message);
            hasNetInput = false;
        }

        if (!hasNetInput)
        {
            // Editor/testing fallback to Unity input so you can test without networked input.
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            netDirection = new Vector3(h, 0f, v);
        }

        netDirection = Vector3.ClampMagnitude(netDirection, 1f);

        // Convert to world space directions relative to body
        Vector3 forward = transform.forward;
        Vector3 right = transform.right;

        float multiplier = IsRunning ? speedOverrides.run : 1f;
        _velocityTarget = (right * netDirection.x + forward * netDirection.z) * speed * multiplier;

        // Smooth velocity
        _localVelocity = Vector3.MoveTowards(_localVelocity, _velocityTarget, acceleration * Runner.DeltaTime);

        // Apply via physics MovePosition
        if (rb != null)
        {
            Vector3 delta = _localVelocity * Runner.DeltaTime;
            Vector3 newPos = rb.position + delta;
            rb.MovePosition(newPos);
            Debug.Log($"[FPM][Fixed] netDir:{netDirection} localVel:{_localVelocity} rb.pos:{rb.position} -> newPos:{newPos}");
        }
    }

    // Jump RPC (unchanged)
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestJump(float strength, RpcInfo info = default)
    {
        if (!Object.HasStateAuthority) return;
        Rigidbody serverRb = rb ?? GetComponent<Rigidbody>();
        if (serverRb != null)
            serverRb.AddForce(Vector3.up * strength, ForceMode.Impulse);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SendMessage(string message, RpcInfo info = default)
    {
        if (Object.HasStateAuthority) RPC_RelayMessage(message, info.Source);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_RelayMessage(string message, PlayerRef messageSource, RpcInfo info = default)
    {
        if (messages == null) messages = FindObjectOfType<TMP_Text>();
        if (messages == null) return;
        bool isLocal = (messageSource == Runner.LocalPlayer);
        messages.text += isLocal ? $"You said: {message}\n" : $"Player {messageSource.PlayerId} said: {message}\n";
    }

    public void SendChatMessage(string msg)
    {
        if (Object == null) return;
        if (Object.HasInputAuthority) RPC_SendMessage(msg);
        else Debug.LogWarning("[FirstPersonMovement] Tried to SendChatMessage on non-input-authority object.");
    }
}
