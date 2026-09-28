using UnityEngine;

/// <summary>Scene-authored Broken Crescent shutters, unlocked permanently by one named boss.</summary>
public sealed class BCGateBossLink : MonoBehaviour
{
    public CombatTarget boss;
    public Transform[] shutters = new Transform[0];
    public Vector3 openOffset = Vector3.up * 18f;
    public string defeatFlag;
    [Range(0, 5)] public int unlockedTier;
    public float openingSeconds = 3.5f;
    public string openingMessage;

    Vector3[] closedPositions;
    CombatTarget subscribedBoss;
    bool defeatedThisSession;
    bool opened;
    float progress;
    public bool IsOpen => opened && progress >= 1f;

    void Awake() => CaptureClosedPose();
    void OnEnable() => Subscribe();
    void OnDisable()
    {
        if (subscribedBoss != null) subscribedBoss.OnDied -= BossDied;
        subscribedBoss = null;
    }

    void CaptureClosedPose()
    {
        if (closedPositions != null) return;
        closedPositions = new Vector3[shutters.Length];
        for (int i = 0; i < shutters.Length; i++)
            if (shutters[i] != null) closedPositions[i] = shutters[i].localPosition;
    }

    void Subscribe()
    {
        if (boss == subscribedBoss) return;
        if (subscribedBoss != null) subscribedBoss.OnDied -= BossDied;
        subscribedBoss = boss;
        if (subscribedBoss != null) subscribedBoss.OnDied += BossDied;
    }

    void BossDied() => defeatedThisSession = true;

    void Update() => Advance(PlayerEntity.Instance, Time.deltaTime);

    // Explicit player/time input also allows isolated editor verification without loading a save.
    public void Advance(PlayerEntity player, float deltaTime)
    {
        CaptureClosedPose();
        Subscribe();
        if (player == null || string.IsNullOrWhiteSpace(defeatFlag)) return;
        if (!opened && (defeatedThisSession || player.HasFlag(defeatFlag)))
        {
            bool restored = player.HasFlag(defeatFlag);
            player.SetFlag(defeatFlag);
            if (unlockedTier >= 2) player.SetFlag("tier_reached_" + unlockedTier);
            opened = true;
            progress = restored ? 1f : 0f;
            if (!restored && !string.IsNullOrEmpty(openingMessage)) HUDController.Emit(openingMessage);
        }
        if (!opened) return;
        progress = Mathf.Clamp01(progress + Mathf.Max(0f, deltaTime) / Mathf.Max(.01f, openingSeconds));
        float t = Mathf.SmoothStep(0f, 1f, progress);
        for (int i = 0; i < shutters.Length; i++)
            if (shutters[i] != null)
                shutters[i].localPosition = closedPositions[i] + openOffset * t;
    }
}
