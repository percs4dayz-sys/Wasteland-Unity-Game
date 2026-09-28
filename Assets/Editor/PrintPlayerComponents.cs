using UnityEditor;
using UnityEngine;

public static class PrintPlayerComponents
{
    [MenuItem("Wasteland/Debug/Print Player Components")]
    public static void Run()
    {
        var player = GameObject.Find("Player");
        if (player == null)
        {
            var pc = Object.FindAnyObjectByType<Player3DController>();
            if (pc != null) player = pc.gameObject;
        }

        if (player == null)
        {
            Debug.LogError("[DebugPlayer] No Player GameObject found in the scene.");
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== PLAYER DIAGNOSTIC FOR '{player.name}' ===");
        sb.AppendLine($"Position: {player.transform.position}, Rotation: {player.transform.rotation.eulerAngles}, Scale: {player.transform.localScale}");
        
        sb.AppendLine("\n--- Components on ROOT ---");
        foreach (var comp in player.GetComponents<Component>())
        {
            if (comp == null)
            {
                sb.AppendLine("  - NULL component!");
                continue;
            }
            sb.AppendLine($"  - {comp.GetType().Name} (enabled: {(comp is Behaviour ? ((Behaviour)comp).enabled : "n/a")})");
        }

        sb.AppendLine("\n--- Child Hierarchy & Components ---");
        PrintChildren(player.transform, "  ", sb);

        Debug.Log(sb.ToString());
        EditorUtility.DisplayDialog("Player Diagnostic", "Diagnostic printed to the Unity console. Check the Console log or editor.log.", "OK");
    }

    static void PrintChildren(Transform t, string indent, System.Text.StringBuilder sb)
    {
        foreach (Transform child in t)
        {
            sb.AppendLine($"{indent}* '{child.name}' (Pos: {child.localPosition}, Rot: {child.localRotation.eulerAngles}, Scale: {child.localScale})");
            foreach (var comp in child.GetComponents<Component>())
            {
                if (comp == null)
                {
                    sb.AppendLine($"{indent}    - NULL component!");
                    continue;
                }
                if (comp is Transform) continue;
                sb.AppendLine($"{indent}    - {comp.GetType().Name} (enabled: {(comp is Behaviour ? ((Behaviour)comp).enabled : "n/a")})");
            }
            PrintChildren(child, indent + "  ", sb);
        }
    }
}
