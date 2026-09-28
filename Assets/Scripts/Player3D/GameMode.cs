using UnityEngine;

/// <summary>True while a 3D scene is running. Lets shared systems (save, HUD bars)
/// behave correctly in both the 2D tile world and the 3D world.</summary>
public static class GameMode
{
    public static bool Is3D;
}
