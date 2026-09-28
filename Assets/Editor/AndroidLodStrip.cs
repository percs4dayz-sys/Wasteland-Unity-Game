using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Android builds leave out each LOD group's most detailed level (LOD0), and the next level takes over up
/// close. Broken Crescent's LOD0 meshes (~0.9 GB, mostly the Hivemind buildings) never ship to the phone.
/// Runs only while building for Android; the editor, play mode and PC builds keep LOD0. A mesh that is
/// also used elsewhere (e.g. by a MeshCollider) stays in the build.
/// </summary>
[BuildCallbackVersion(1)]
class AndroidLodStrip : IProcessSceneWithReport
{
    public int callbackOrder => 0;

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        if (report == null || report.summary.platform != BuildTarget.Android) return;
        int groups = 0, stripped = 0;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var group in root.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = group.GetLODs();
                if (lods.Length < 2) continue;
                var kept = lods.Skip(1).ToArray();
                foreach (var r in lods[0].renderers)
                {
                    if (r == null || kept.Any(l => l.renderers.Contains(r))) continue;
                    var filter = r.GetComponent<MeshFilter>();
                    if (filter != null) filter.sharedMesh = null;
                    if (r is SkinnedMeshRenderer skinned) skinned.sharedMesh = null;
                    r.enabled = false;
                    stripped++;
                }
                group.SetLODs(kept);
                groups++;
            }
        if (groups > 0) Debug.Log($"[WASTELAND BUILD] {scene.name}: dropped LOD0 from {groups} LOD groups ({stripped} renderers)");
    }
}
