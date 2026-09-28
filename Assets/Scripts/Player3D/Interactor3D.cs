using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Proximity interaction for the 3D world. Walk near a person, station, node or bank and a
/// prompt appears — press E (keyboard) or A (controller) to use it. The prompt is what makes
/// interaction discoverable on a controller, where there's no mouse cursor to click with.
/// </summary>
[RequireComponent(typeof(Player3DController))]
public class Interactor3D : MonoBehaviour
{
    public float interactRange = 3.2f;

    Player3DController _pc;
    GameObject _promptGo;
    TMP_Text _promptText;

    void Awake()
    {
        _pc = GetComponent<Player3DController>();
        BuildPrompt();
    }

    void Update()
    {
        if (ChatInput.IsTyping || BankGuideUI.IsShowing) { ShowPrompt(null); return; }

        // Walking away interrupts gathering (in 2D, distance gating did this implicitly).
        if (_pc.IsMoving && SkillingManager.Instance != null && SkillingManager.Instance.IsGathering)
            SkillingManager.Instance.StopGathering();

        // ...and likewise interrupts a background batch craft/cook the moment you step away.
        if (_pc.IsMoving && CraftingManager.Instance != null && CraftingManager.Instance.IsCrafting)
        {
            CraftingManager.Instance.StopCrafting();
            Msg("You stop crafting.");
        }

        var thing = FindNearest(out string verb);
        ShowPrompt(thing != null ? verb : null);

        if ((Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.JoystickButton0)) && thing != null)
        {
            // The prompt shows from a few metres out. Walk up to a solid node before working it, rather
            // than swinging at it from where you stand.
            var walker = GetComponent<ClickToMove3D>();
            if (walker != null && ClickToMove3D.IsSolidNode(thing) &&
                Planar(ClickToMove3D.UsePosition(thing) - transform.position) > ClickToMove3D.GatherReach)
                walker.UseFromMenu(thing);
            else
                InteractWith(thing);
        }
    }

    /// <summary>Nearest interactable (any type) within range, plus the action label to show.</summary>
    Component FindNearest(out string verb)
    {
        Component best = null;
        float bestD = float.MaxValue;
        foreach (var h in Physics.OverlapSphere(transform.position + Vector3.up * 0.5f, interactRange, ~0, QueryTriggerInteraction.Collide))
        {
            Component c = (Component)h.GetComponentInParent<GroundItem>()
                       ?? (Component)h.GetComponentInParent<ITalkableNPC>()
                       ?? (Component)h.GetComponentInParent<BankingCrate>()
                       ?? (Component)h.GetComponentInParent<CraftingStation>()
                       ?? (Component)h.GetComponentInParent<AFKStation>()
                       ?? h.GetComponentInParent<ResourceNode>();
            if (c == null) continue;
            float d = (ClickToMove3D.UsePosition(c) - transform.position).sqrMagnitude;
            if (d > interactRange * interactRange) continue;
            if (d < bestD) { bestD = d; best = c; }
        }
        verb = VerbFor(best);
        return best;
    }

    static string VerbFor(Component c) => c switch
    {
        GroundItem gi      => "Take " + gi.DisplayName,
        ITalkableNPC npc   => "Talk to " + npc.DisplayName,
        BankingCrate _     => "Open Bank",
        CraftingStation st => "Use " + st.DisplayName,
        AFKStation afk     => "Set " + afk.DisplayName,
        ResourceNode node  => "Gather " + node.DisplayName,
        _ => null
    };

    /// <summary>Use a specific thing — shared by press-E/A and click-to-move arrival.</summary>
    public bool InteractWith(Component thing)
    {
        if (BankGuideUI.IsShowing) return false;
        var player = PlayerEntity.Instance;

        if (thing is GroundItem gi)
        {
            gi.TryPickUp();
            return true;
        }
        if (thing is BankingCrate && player != null)
        {
            BankUI.Instance?.Open(player);
            return true;
        }
        if (thing is CraftingStation station)
        {
            _pc.ClearDestination();
            FaceToward(station.transform.position);
            if (CraftingUI.Instance != null) CraftingUI.Instance.Open(station);
            else CraftingManager.Instance?.Interact(station);
            return true;
        }
        if (thing is AFKStation afk)
        {
            FaceToward(afk.transform.position);
            afk.Use();
            return true;
        }
        if (thing is ResourceNode node)
        {
            if (node.IsDepleted) { Msg("Nothing left there right now."); return false; }
            // Gathering and fighting don't mix: a live combat target keeps turning you toward the enemy
            // and holds the aim pose, so you'd work the node facing away with the wrong animation.
            var combat = GetComponent<ActionCombat3D>();
            if (combat != null) combat.Disengage();
            FaceToward(node.FacingPosition);
            // A pending click destination must not make Update immediately cancel gathering.
            _pc.ClearDestination();
            SkillingManager.Instance?.StartGathering(node);
            return true;
        }
        if (thing is ITalkableNPC npc)
        {
            FaceToward(((Component)npc).transform.position);
            npc.Interact();
            return true;
        }
        return false;
    }

    static float Planar(Vector3 v) { v.y = 0f; return v.magnitude; }

    void FaceToward(Vector3 pos)
    {
        Vector3 d = pos - transform.position; d.y = 0f;
        if (d.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(d);
    }

    // ── on-screen prompt ──────────────────────────────────────────────────
    void ShowPrompt(string verb)
    {
        if (_promptGo == null) return;
        // Not on phones: there's no E / A to press — you tap the thing (OSRS mobile shows no such hint). And not
        // under an open dialogue box, which already says what to do.
        bool show = !string.IsNullOrEmpty(verb) && !Application.isMobilePlatform && !DialogueUI.IsShowing;
        if (_promptGo.activeSelf != show) _promptGo.SetActive(show);
        if (show) _promptText.text = $"<color=#FFD24A>[E]</color> / <color=#FFD24A>[A]</color>   {verb}";
    }

    void BuildPrompt()
    {
        var canvasGo = new GameObject("InteractPromptCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);   // overlay canvas ignores the world transform
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var rt = new GameObject("Prompt", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(canvasGo.transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0, 150);   // above the action bar / chat
        rt.sizeDelta = new Vector2(480, 40);
        var promptBg = rt.gameObject.AddComponent<Image>();
        UITheme.Panel(promptBg, 0.88f);                // same framed look as the rest of the HUD
        _promptGo = rt.gameObject;

        var txtRt = new GameObject("Text", typeof(RectTransform)).GetComponent<RectTransform>();
        txtRt.SetParent(rt, false);
        txtRt.anchorMin = Vector2.zero; txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = Vector2.zero; txtRt.offsetMax = Vector2.zero;
        _promptText = txtRt.gameObject.AddComponent<TextMeshProUGUI>();
        _promptText.fontSize = 18; _promptText.color = UITheme.Text;
        _promptText.alignment = TextAlignmentOptions.Center; _promptText.raycastTarget = false;

        _promptGo.SetActive(false);
    }

    static void Msg(string m) => HUDController.Emit("<color=#88DDFF>[USE]:</color> " + m);
}
