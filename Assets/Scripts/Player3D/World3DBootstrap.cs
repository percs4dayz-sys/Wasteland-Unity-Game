using UnityEngine;

/// <summary>Placed in 3D scenes by the World3D builder. Flags the game as 3D while alive.</summary>
public class World3DBootstrap : MonoBehaviour
{
    void Awake() => GameMode.Is3D = true;
    void OnDestroy() => GameMode.Is3D = false;
}
