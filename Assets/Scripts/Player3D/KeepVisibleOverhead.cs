using UnityEngine;

/// <summary>
/// Opt-out marker for <see cref="OccluderHider"/>. Drop this on anything that sits above the player
/// but must STAY visible — a skybox dome, a hanging quest marker, a boss you fight from below, a
/// floating platform you are meant to see and aim at.
///
/// Applies to the whole subtree, so putting it on a prop's root covers every renderer under it.
/// </summary>
[DisallowMultipleComponent]
public class KeepVisibleOverhead : MonoBehaviour
{
}
