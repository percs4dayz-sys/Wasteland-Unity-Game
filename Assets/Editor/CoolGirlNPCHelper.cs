using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-click: finds CoolGirl in the currently-open scene and attaches the Roxy NPC
/// quest system — 7 starter training quests with dialogue, item gifting, and skill guidance.
///
/// Menu: Wasteland ▸ NPCs ▸ Set Up CoolGirl as Roxy (Quest NPC)
///
/// Use this on your EXISTING MainWorld3D scene — it does NOT rebuild anything.
/// After running it, press Play and walk up to CoolGirl / press E to start her quests.
/// </summary>
public static class CoolGirlNPCHelper
{
    [MenuItem("Wasteland/NPCs/Set Up CoolGirl as Roxy (Quest NPC)")]
    public static void SetUpCoolGirl()
    {
        var scene = SceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(scene.path))
        {
            EditorUtility.DisplayDialog("CoolGirl NPC Setup",
                "Save your scene first (Ctrl+S) — the active scene must have a path on disk.", "OK");
            return;
        }

        // Find CoolGirl anywhere in the scene — exact name or case-insensitive match.
        GameObject coolGirl = GameObject.Find("CoolGirl");
        if (coolGirl == null)
        {
            // Try case-insensitive search.
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go.scene != scene || go.hideFlags != HideFlags.None) continue;
                if (go.name.IndexOf("coolgirl", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || go.name.IndexOf("cool girl", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    coolGirl = go;
                    break;
                }
            }
        }

        if (coolGirl == null)
        {
            EditorUtility.DisplayDialog("CoolGirl NPC Setup",
                "Couldn't find a GameObject named 'CoolGirl' in this scene.\n\n" +
                "Make sure the CoolGirl model is in your scene and named exactly 'CoolGirl'.", "OK");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(coolGirl, "Set up CoolGirl as Roxy quest NPC");

        // Ensure there's a collider so the interactor's OverlapSphere can find her.
        if (coolGirl.GetComponent<Collider>() == null)
        {
            var cap = Undo.AddComponent<CapsuleCollider>(coolGirl);
            cap.height = 1.8f;
            cap.radius = 0.4f;
            cap.center = new Vector3(0f, 0.9f, 0f);
        }

        // Remove any old NPC scripts first, then add Roxy's quest system.
        var oldMara = coolGirl.GetComponent<TutorialNPC>();
        if (oldMara != null) Undo.DestroyObjectImmediate(oldMara);

        if (coolGirl.GetComponent<RoxyNPC>() == null)
            Undo.AddComponent<RoxyNPC>(coolGirl);

        // Strip any stray cameras/lights/audio listeners from the model (common in FBX imports).
        foreach (var cam in coolGirl.GetComponentsInChildren<Camera>(true))
            if (cam != null) Undo.DestroyObjectImmediate(cam.gameObject);
        foreach (var lt in coolGirl.GetComponentsInChildren<Light>(true))
            if (lt != null) Undo.DestroyObjectImmediate(lt.gameObject);
        foreach (var al in coolGirl.GetComponentsInChildren<AudioListener>(true))
            if (al != null) Undo.DestroyObjectImmediate(al);

        EditorSceneManager.MarkSceneDirty(scene);

        EditorUtility.DisplayDialog("CoolGirl NPC Setup",
            $"CoolGirl is now set up as Roxy!\n\n" +
            $"• RoxyNPC attached — 7 starter training quests with item gifting\n" +
            $"  - Hooked on You (Fishing Rod)\n" +
            $"  - Stripping Down (Hatchet)\n" +
            $"  - Heavy Metal Romance (Pickaxe)\n" +
            $"  - Something's Fishy (Raw Shrimp → cook)\n" +
            $"  - Sparks Will Fly (Scrap Metal → smelt)\n" +
            $"  - Hardening Your Assets (Scrap Bar → craft)\n" +
            $"  - Deader is Better (Cracked Bones → bury)\n" +
            $"• Collider added so the E-key interactor can find her\n" +
            $"• Any old Mara script removed\n\n" +
            $"Press Play and walk up to her.", "Nice");
    }
}
