using Fusion;
using UnityEngine;
using TMPro; // TextMeshPro

public class Player : NetworkBehaviour
{
    [Header("Projectile Prefabs (assign networked prefabs)")]
    [SerializeField] private PhysxBall _prefabPhysxBall;           // existing fired projectile
    [SerializeField] private DroppedPhysxBall _prefabDroppedBall;  // dropped projectile (Init(Vector3, Vector3))
    [SerializeField] private LobbedPhysxBall _prefabLobbedBall;    // lobbed projectile

    [Networked] private TickTimer delay { get; set; }
    [Networked] public byte spawnedProjectileCounter { get; set; }

    private NetworkCharacterController _cc;
    private Vector3 _forward;

    // Visuals
    private ChangeDetector _changeDetector;
    private Material _instanceMaterial;

    // UI (TextMeshPro) for message display
    private TMP_Text _messages;

    private void Awake()
    {
        _cc = GetComponent<NetworkCharacterController>();
        _forward = transform.forward;

        var mr = GetComponentInChildren<MeshRenderer>();
        if (mr != null)
            _instanceMaterial = mr.material;
    }

    public override void Spawned()
    {
        _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);

        if (_instanceMaterial != null)
            _instanceMaterial.color = Color.blue;

        _messages = FindObjectOfType<TMP_Text>();
    }

    private void Update()
    {
        if (Object.HasInputAuthority && Input.GetKeyDown(KeyCode.R))
        {
            RPC_SendMessage("Hey Mate!");
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (GetInput(out NetworkInputData data))
        {
            // movement
            data.direction.Normalize();
            _cc.Move(5 * data.direction * Runner.DeltaTime);

            if (data.direction.sqrMagnitude > 0)
                _forward = data.direction;

            // Only state authority spawns and manages cooldowns
            if (HasStateAuthority && delay.ExpiredOrNotRunning(Runner))
            {
                // Only MouseButton0 triggers action (left click)
                if (data.buttons.IsSet(NetworkInputData.MOUSEBUTTON0))
                {
                    delay = TickTimer.CreateFromSeconds(Runner, 0.5f);

                    int selected = data.selectedWeapon; // 0 = fired physx, 1 = dropped, 2 = lobbed

                    if (selected == 0)
                    {
                        if (_prefabPhysxBall == null)
                        {
                            Debug.LogWarning("[Player] Cannot spawn PhysxBall: _prefabPhysxBall is not assigned on the Player prefab.");
                        }
                        else
                        {
                            Runner.Spawn(
                                _prefabPhysxBall,
                                transform.position + _forward,
                                Quaternion.LookRotation(_forward),
                                Object.InputAuthority,
                                (runner, o) =>
                                {
                                    var physxComp = o.GetComponent<PhysxBall>();
                                    if (physxComp != null)
                                        physxComp.Init(10 * _forward);
                                });

                            spawnedProjectileCounter++;
                        }
                    }
                    else if (selected == 1)
                    {
                        if (_prefabDroppedBall == null)
                        {
                            Debug.LogWarning("[Player] Cannot spawn DroppedPhysxBall: _prefabDroppedBall is not assigned on the Player prefab.");
                        }
                        else
                        {
                            Vector3 spawnPos = transform.position + _forward * 1.0f + Vector3.up * 0.5f;
                            Runner.Spawn(
                                _prefabDroppedBall,
                                spawnPos,
                                Quaternion.identity,
                                Object.InputAuthority,
                                (runner, o) =>
                                {
                                    var dropComp = o.GetComponent<DroppedPhysxBall>();
                                    if (dropComp != null)
                                    {
                                        // CALL THE CORRECT SIGNATURE: Init(Vector3 spawnPos, Vector3 spawnOffset)
                                        dropComp.Init(spawnPos, Vector3.zero);
                                    }
                                });

                            spawnedProjectileCounter++;
                        }
                    }
                    else if (selected == 2)
                    {
                        if (_prefabLobbedBall == null)
                        {
                            Debug.LogWarning("[Player] Cannot spawn LobbedPhysxBall: _prefabLobbedBall is not assigned on the Player prefab.");
                        }
                        else
                        {
                            Runner.Spawn(
                                _prefabLobbedBall,
                                transform.position + _forward,
                                Quaternion.LookRotation(_forward),
                                Object.InputAuthority,
                                (runner, o) =>
                                {
                                    var lobComp = o.GetComponent<LobbedPhysxBall>();
                                    if (lobComp != null)
                                        lobComp.Init(_forward.normalized);
                                });

                            spawnedProjectileCounter++;
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[Player] Unknown weapon selected: {selected}");
                    }
                }
            }
        }
    }

    public override void Render()
    {
        if (_changeDetector != null && _instanceMaterial != null)
        {
            foreach (var change in _changeDetector.DetectChanges(this))
            {
                if (change == nameof(spawnedProjectileCounter))
                {
                    _instanceMaterial.color = Color.white;
                }
            }

            _instanceMaterial.color = Color.Lerp(_instanceMaterial.color, Color.blue, Time.deltaTime);
        }
    }

    //
    // RPCs
    //
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    public void RPC_SendMessage(string message, RpcInfo info = default)
    {
        RPC_RelayMessage(message, info.Source);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All, HostMode = RpcHostMode.SourceIsServer)]
    public void RPC_RelayMessage(string message, PlayerRef messageSource)
    {
        if (_messages == null)
            _messages = FindObjectOfType<TMP_Text>();

        if (_messages == null)
            return;

        if (messageSource == Runner.LocalPlayer)
            _messages.text += $"You said: {message}\n";
        else
            _messages.text += $"Some other player said: {message}\n";
    }
}
