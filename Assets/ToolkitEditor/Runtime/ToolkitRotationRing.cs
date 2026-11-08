using UnityEngine;

/// <summary>Identifies one draggable local-axis ring on the rotation widget.</summary>
public class ToolkitRotationRing : MonoBehaviour
{
    public enum RingAxis
    {
        LocalHorizontal,
        LocalVertical
    }

    public RingAxis axis;
}
