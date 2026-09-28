using UnityEngine;

/// <summary>
/// Draws the play-area boundary as a coloured wire box (with a faint fill) so you can SEE where the
/// invisible edge walls are while building. Shows in the Scene view always; shows in the Game view
/// while playing if the Game view's "Gizmos" button is on. Purely visual — no collider, no effect
/// on the game. Add/position it with the Wasteland ▸ Show Play Boundary menu.
/// </summary>
public class BoundaryVisualizer : MonoBehaviour
{
    public Vector3 size  = new Vector3(60f, 4f, 60f);
    public Color   color = new Color(1f, 0.45f, 0.2f, 1f);

    void OnDrawGizmos()
    {
        Vector3 c = transform.position;

        Gizmos.color = color;
        Gizmos.DrawWireCube(c, size);

        var fill = color; fill.a = 0.05f;
        Gizmos.color = fill;
        Gizmos.DrawCube(c, size);
    }
}
