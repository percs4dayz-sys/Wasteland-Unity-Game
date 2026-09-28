using UnityEngine;

/// <summary>
/// The authored world spawn — where a BRAND NEW character starts, and where anyone who falls under
/// the map / dies gets returned to.
///
/// This is deliberately separate from the SAVED position. Loading a save still puts you exactly
/// where you logged out (SaveData.ApplyTo) — that behaviour is untouched. This point is only the
/// fallback for the two cases where the saved position is absent or useless:
///   • no save file yet (new character)
///   • the player is in the void with no ground under them (PlayerRespawn's watchdog)
///   • death respawn
///
/// Before this existed, PlayerRespawn captured transform.position in Start(), which ran AFTER the
/// save had already stamped its position on the player. So a save with a bad position (e.g. Y=2865
/// in the sky) became the "spawn point" too, and every recovery teleported you straight back into
/// the void — the loop was self-sustaining.
///
/// Set it either way:
///   • Drop this component on an empty GameObject in the scene and position it. That wins.
///   • Or leave no marker in the scene and <see cref="Fallback"/> below is used.
/// Editor helper: Wasteland ▸ Fix ▸ Set World Spawn Point Here.
/// </summary>
public class WorldSpawnPoint : MonoBehaviour
{
    /// <summary>Used when no WorldSpawnPoint marker exists in the scene. Y is only a hint — the
    /// spawn is always snapped down onto real ground before use.</summary>
    public static readonly Vector3 Fallback = new Vector3(1.6f, 157f, -166.7f);

    [Tooltip("Face this direction on spawn (the marker's own Y rotation is used).")]
    public bool useMarkerRotation = true;

    static WorldSpawnPoint _cached;

    static WorldSpawnPoint Instance
    {
        get
        {
            if (_cached == null) _cached = Object.FindAnyObjectByType<WorldSpawnPoint>();
            return _cached;
        }
    }

    /// <summary>The authored spawn position — the scene marker if there is one, else the fallback.</summary>
    public static Vector3 Position => Instance != null ? Instance.transform.position : Fallback;

    /// <summary>The authored spawn facing, or <paramref name="fallbackRot"/> if there's no marker
    /// (or the marker opts out of supplying rotation).</summary>
    public static Quaternion Rotation(Quaternion fallbackRot)
    {
        var i = Instance;
        return (i != null && i.useMarkerRotation) ? i.transform.rotation : fallbackRot;
    }

    /// <summary>True when a marker exists in the scene (vs. falling back to the constant).</summary>
    public static bool HasMarker => Instance != null;

    void OnDrawGizmos()
    {
        // Visible in the Scene view so the spawn isn't an invisible empty you lose track of.
        Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.9f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up, 0.6f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 2.2f);
        Gizmos.DrawRay(transform.position + Vector3.up, transform.forward * 1.5f);
    }
}
