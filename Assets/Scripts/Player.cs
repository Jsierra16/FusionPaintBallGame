using Fusion;
using UnityEngine;
using TMPro; // TextMeshPro
using System.Collections;

public class Player : NetworkBehaviour
{
    [Header("Projectile Prefabs (assign networked prefabs)")]
    [SerializeField] private PhysxBall _prefabPhysxBall;           // existing fired projectile
    [SerializeField] private DroppedPhysxBall _prefabDroppedBall;  // mine-style dropped projectile
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
        if (Object.HasInputAuthority)
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                RPC_SendMessage("Hey Mate!");
            }

            // TEST: Explode the last dropped ball with X
            if (Input.GetKeyDown(KeyCode.X) && DroppedPhysxBall.LastSpawned != null)
            {
                DroppedPhysxBall.LastSpawned.Explode();
            }
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!GetInput(out NetworkInputData data)) return;

        // movement
        data.direction.Normalize();
        _cc.Move(5 * data.direction * Runner.DeltaTime);

        if (data.direction.sqrMagnitude > 0)
            _forward = data.direction;

        // Only state authority spawns and manages cooldowns
        if (!HasStateAuthority || !delay.ExpiredOrNotRunning(Runner)) return;

        // Only MouseButton0 triggers action (left click)
        if (!data.buttons.IsSet(NetworkInputData.MOUSEBUTTON0)) return;

        delay = TickTimer.CreateFromSeconds(Runner, 0.5f);

        int selected = data.selectedWeapon; // 0 = fired physx, 1 = dropped mine, 2 = lobbed

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
                // Spawn slightly above ground (small Y offset to avoid clipping into floor)
                Vector3 spawnPos = transform.position + _forward * 1.0f + Vector3.up * 0.05f;

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
                            // Let the Rigidbody settle naturally
                            Rigidbody rb = dropComp.GetComponent<Rigidbody>();
                            if (rb != null)
                            {
                                rb.useGravity = true;        // ensure gravity is on
                                rb.isKinematic = false;      // allow physics simulation
                            }

                            // Optional: ignore collision with player on spawn briefly
                            Collider ballCol = dropComp.GetComponent<Collider>();
                            Collider playerCol = GetComponent<Collider>();
                            if (ballCol != null && playerCol != null)
                            {
                                Physics.IgnoreCollision(ballCol, playerCol, true);
                                // Re-enable collision after 0.2s
                                StartCoroutine(ReenableCollision(ballCol, playerCol, 0.2f));
                            }
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

    public override void Render()
    {
        if (_changeDetector == null || _instanceMaterial == null) return;

        foreach (var change in _changeDetector.DetectChanges(this))
        {
            if (change == nameof(spawnedProjectileCounter))
                _instanceMaterial.color = Color.white;
        }

        _instanceMaterial.color = Color.Lerp(_instanceMaterial.color, Color.blue, Time.deltaTime);
    }

    // Helper coroutine to re-enable collision after a short delay
    private IEnumerator ReenableCollision(Collider ballCol, Collider playerCol, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (ballCol != null && playerCol != null)
            Physics.IgnoreCollision(ballCol, playerCol, false);
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
        if (_messages == null) return;

        if (messageSource == Runner.LocalPlayer)
            _messages.text += $"You said: {message}\n";
        else
            _messages.text += $"Some other player said: {message}\n";
    }
}
