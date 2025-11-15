using Fusion;
using UnityEngine;

public struct NetworkInputData : INetworkInput
{
    public const byte MOUSEBUTTON0 = 1;
    public const byte MOUSEBUTTON1 = 2;

    public NetworkButtons buttons;
    public Vector3 direction;

    // 0 = Ball, 1 = PhysxBall (add more if needed)
    public byte selectedWeapon;
}
