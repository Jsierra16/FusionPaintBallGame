using UnityEngine;
using Fusion;
using System.Collections;

public class DroppedPhysxBall : NetworkBehaviour
{
    [Networked] private TickTimer life { get; set; }

    public void Init(Vector3 forward, Vector3 spawnOffset)
    {
        life = TickTimer.CreateFromSeconds(Runner, 5f);

        // Move ball slightly forward (drop from player)
        transform.position += spawnOffset;

        // No forward launch force (drop)
        GetComponent<Rigidbody>().isKinematic = false;
        GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
    }

    public override void FixedUpdateNetwork()
    {
        if (life.Expired(Runner))
            Runner.Despawn(Object);
    }
}
