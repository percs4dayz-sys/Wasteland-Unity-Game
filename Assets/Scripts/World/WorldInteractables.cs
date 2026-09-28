using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One answer to "what's under the cursor, and what can I do with it?", shared by the right-click
/// menu, the top-left hover label and click-to-move, so they always agree. OSRS-style wording:
/// "Chop down Dead Tree", "Talk-to Roxy", "Take Wood Scrap", "Attack Husk", with the target coloured
/// by kind (NPCs and creatures yellow, objects cyan, items orange).
/// </summary>
public static class WorldInteractables
{
    const string NpcColour = "#FFFF40", ObjectColour = "#40FFFF", ItemColour = "#FF9A40";

    /// <summary>Do not let an oversized interaction collider claim empty screen space.
    /// Test each visible mesh separately so gaps between child meshes remain clickable ground.</summary>
    public static bool RayHitsVisual(Component target, Ray ray)
    {
        bool hasVisual = false;
        foreach (var renderer in target.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer || renderer is SpriteRenderer)) continue;
            hasVisual = true;
            Bounds bounds = renderer.bounds;
            bounds.Expand(0.1f);
            if (bounds.IntersectRay(ray)) return true;
        }
        return !hasVisual; // invisible authored interaction markers still work
    }

    /// <summary>The interactable a collider belongs to, if any.</summary>
    public static Component Resolve(Collider col)
    {
        if (col == null) return null;
        // A defeated golem keeps CombatTarget for its death state, but its corpse is now a node.
        var golem = col.GetComponentInParent<FissionGolem>();
        if (golem != null && golem.IsCorpse) return golem.GetComponent<ResourceNode>();
        return (Component)col.GetComponentInParent<CombatTarget>()
            ?? (Component)col.GetComponentInParent<GroundItem>()
            ?? (Component)col.GetComponentInParent<ResourceNode>()
            ?? (Component)col.GetComponentInParent<BankingCrate>()
            ?? (Component)col.GetComponentInParent<CraftingStation>()
            ?? (Component)col.GetComponentInParent<AFKStation>()
            ?? (Component)col.GetComponentInParent<ITalkableNPC>();
    }

    /// <summary>Everything clickable along the ray, nearest first: whatever the ray passes through,
    /// plus anything within <paramref name="assist"/> metres of where it lands (small or distant
    /// things don't need a pixel-perfect click). Also returns the ground point for "Walk here".</summary>
    public static List<Component> UnderCursor(Ray ray, float assist, out Vector3 point, out bool hitSomething)
    {
        var found = new List<Component>();
        point = Vector3.zero;
        var hits = Physics.RaycastAll(ray, 500f, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        hitSomething = false;
        foreach (var h in hits)
        {
            var c = Resolve(h.collider);
            if (c != null) { if (Usable(c) && RayHitsVisual(c, ray) && !found.Contains(c)) found.Add(c); continue; }
            if (h.collider.isTrigger) continue;
            if (PlayerEntity.Instance != null && h.collider.transform.IsChildOf(PlayerEntity.Instance.transform)) continue;
            point = h.point; hitSomething = true;   // first solid surface: the ground (or a wall) under the cursor
            break;
        }
        if (!hitSomething && found.Count > 0) { point = found[0].transform.position; hitSomething = true; }
        if (hitSomething && assist > 0f)
        {
            var near = new List<(Component c, float d)>();
            foreach (var col in Physics.OverlapSphere(point, assist, ~0, QueryTriggerInteraction.Collide))
            {
                var c = Resolve(col);
                if (c == null || !Usable(c) || found.Contains(c) ||
                    (c.transform.position - point).sqrMagnitude > assist * assist) continue;
                bool dup = false;
                foreach (var n in near) if (n.c == c) { dup = true; break; }
                if (!dup) near.Add((c, (c.transform.position - point).sqrMagnitude));
            }
            near.Sort((a, b) => a.d.CompareTo(b.d));
            foreach (var n in near) found.Add(n.c);
        }
        return found;
    }

    // A corpse is no longer anything to click (dummies never die for good).
    static bool Usable(Component c) => !(c is CombatTarget ct) || ct.isDummy || !ct.IsDead;

    /// <summary>Plain name of the thing ("Roxy", "Dead Tree", "Horned Ravager").</summary>
    public static string Name(Component c) => c is IExaminable ex ? ex.DisplayName : Pretty(c.name);

    /// <summary>The main thing you'd do: "Attack", "Talk-to", "Chop down"…</summary>
    public static string Verb(Component c) => c switch
    {
        CombatTarget _    => "Attack",
        GroundItem _      => "Take",
        ITalkableNPC _    => "Talk-to",
        BankingCrate _    => "Bank",
        CraftingStation _ => "Use",
        AFKStation _      => "Use",
        ResourceNode n    => n.skill switch
        {
            Skill.Woodcutting => "Chop down",
            Skill.Scrapping   => "Mine",
            Skill.Fishing     => "Fish at",
            _                 => "Gather",
        },
        _ => "Use",
    };

    /// <summary>The target name in its OSRS colour.</summary>
    public static string Coloured(Component c)
    {
        string col = c is CombatTarget || c is ITalkableNPC ? NpcColour : c is GroundItem ? ItemColour : ObjectColour;
        return $"<color={col}>{Name(c)}</color>";
    }

    public static string Examine(Component c) =>
        c is IExaminable ex && !string.IsNullOrEmpty(ex.ExamineText) ? ex.ExamineText : "Nothing interesting about it.";

    /// <summary>"HornedRavager(Clone)" → "Horned Ravager".</summary>
    public static string Pretty(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        int cut = raw.IndexOf('(');
        if (cut > 0) raw = raw.Substring(0, cut);
        raw = raw.Replace('_', ' ').Trim();
        var sb = new System.Text.StringBuilder(raw.Length + 8);
        for (int i = 0; i < raw.Length; i++)
        {
            char ch = raw[i];
            if (i > 0 && char.IsUpper(ch) && char.IsLower(raw[i - 1])) sb.Append(' ');
            sb.Append(ch);
        }
        return sb.ToString();
    }
}
