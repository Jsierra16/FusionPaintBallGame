using Fusion;
using UnityEngine;

public struct NetworkInputData : INetworkInput
{
    // Use bit indices (0,1,2...) — not 1,2,4...
    public const byte MOUSEBUTTON0 = 0; // left click -> bit 0
    public const byte MOUSEBUTTON1 = 1; // right click -> bit 1

    public NetworkButtons buttons;
    public Vector3 direction;
}
