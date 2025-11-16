using Fusion;
using UnityEngine;
using System.Collections;

public class DroppedPhysxBall : NetworkBehaviour
{
    [Header("Mini Balls Settings")]
    public PhysxBall miniBallPrefab;       // prefab for the little balls
    public int miniBallCount = 8;          // number of mini balls spawned on explosion
    public float miniBallForce = 5f;       // impulse applied to mini balls
    public float miniBallArc = 0.3f;       // upward arc for mini balls
    public float miniBallDuration = 5f;    // how long mini balls stay in the scene

    [Header("Dropped Ball Settings")]
    public float autoExplodeTime = 15f;    // dropped ball auto-explodes after this many seconds

    private bool _exploded = false;
    private bool _landed = false;

    public static DroppedPhysxBall LastSpawned;

    public override void Spawned()
    {
        LastSpawned = this;

        // Start auto-explode timer
        StartCoroutine(AutoExplodeCoroutine());
    }

    private IEnumerator AutoExplodeCoroutine()
    {
        yield return new WaitForSeconds(autoExplodeTime);
        if (!_exploded)
            Explode();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.X) && LastSpawned != null)
        {
            LastSpawned.Explode();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!_landed && collision.gameObject.CompareTag("Ground"))
        {
            _landed = true;
        }

        // Explode only when touching player
        if (collision.gameObject.CompareTag("Player"))
            Explode();
    }

    public void Explode()
    {
        if (_exploded || !Runner.IsRunning) return;
        _exploded = true;

        if (miniBallPrefab != null)
        {
            for (int i = 0; i < miniBallCount; i++)
            {
                float angle = i * (360f / miniBallCount);
                Vector3 dir = Quaternion.Euler(0, angle, 0) * Vector3.forward;
                dir += Vector3.up * miniBallArc;
                dir.Normalize();

                Vector3 spawnPos = transform.position + dir * 0.1f;

                Runner.Spawn(
                    miniBallPrefab,
                    spawnPos,
                    Quaternion.LookRotation(dir),
                    Object.InputAuthority,
                    (runner, obj) =>
                    {
                        Rigidbody rb = obj.GetComponent<Rigidbody>();
                        if (rb != null)
                        {
                            rb.linearVelocity = Vector3.zero;
                            rb.AddForce(dir * miniBallForce, ForceMode.Impulse);
                        }

                        // Add MiniBall script for player collision despawn
                        MiniBall miniBall = obj.GetComponent<MiniBall>();
                        if (miniBall == null)
                            miniBall = obj.gameObject.AddComponent<MiniBall>();

                        // Set the explosion duration
                        miniBall.SetLifetime(miniBallDuration);
                    });
            }
        }

        Runner.Despawn(Object);

        if (LastSpawned == this) LastSpawned = null;
    }
}

// Helper class for miniballs
public class MiniBall : MonoBehaviour
{
    private float _lifetime = 10f;
    private float _spawnTime;

    public void SetLifetime(float duration)
    {
        _lifetime = duration;
        _spawnTime = Time.time;
    }

    private void Update()
    {
        // Optional: destroy automatically after lifetime even if no player touched
        if (Time.time - _spawnTime > _lifetime)
        {
            NetworkObject netObj = GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsValid)
            {
                NetworkRunner runner = netObj.Runner;
                if (runner != null)
                    runner.Despawn(netObj);
            }
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            NetworkObject netObj = GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsValid)
            {
                NetworkRunner runner = netObj.Runner;
                if (runner != null)
                    runner.Despawn(netObj);
            }
        }
    }
}
