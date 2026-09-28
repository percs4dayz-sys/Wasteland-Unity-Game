// MusicManager.cs
// Loads every AudioClip under Assets/Resources/Music and plays them, shuffled, on a
// rolling loop. Persists across scene loads. Exposes controls for the Music tab UI:
// pick a track, skip, and disable ("X") tracks you don't want (remembered between runs).
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    AudioSource _audioSource;
    readonly List<AudioClip> _clips = new();
    readonly HashSet<string> _disabled = new();   // clip names the player X'd out
    int _currentIndex = -1;

    const string DisabledPrefKey = "music_disabled";

    /// <summary>Fired whenever the current track or enabled-set changes, so the UI can refresh.</summary>
    public event System.Action OnTrackChanged;

    public IReadOnlyList<AudioClip> Clips => _clips;
    public int CurrentIndex => _currentIndex;
    public string CurrentTrackName =>
        (_currentIndex >= 0 && _currentIndex < _clips.Count) ? _clips[_currentIndex].name : "(nothing)";
    public bool IsEnabled(int i) => i >= 0 && i < _clips.Count && !_disabled.Contains(_clips[i].name);

    public float Volume
    {
        get => _audioSource != null ? _audioSource.volume : 0.5f;
        set { if (_audioSource != null) _audioSource.volume = Mathf.Clamp01(value); }
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.loop = false;
        _audioSource.volume = 0.5f;

        LoadClips();
        LoadDisabled();
        PlayNext();
    }

    void LoadClips()
    {
        _clips.AddRange(Resources.LoadAll<AudioClip>("Music"));
        // Stable alphabetical order so the UI list and saved disables line up run to run.
        _clips.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));
    }

    // ── enable / disable (the "X" next to a song) ────────────────────────────
    public void SetEnabled(int index, bool on)
    {
        if (index < 0 || index >= _clips.Count) return;
        string n = _clips[index].name;
        if (on) _disabled.Remove(n); else _disabled.Add(n);
        SaveDisabled();

        // If you just disabled the track that's playing, skip off it.
        if (!on && index == _currentIndex) PlayNext();
        else OnTrackChanged?.Invoke();
    }

    void LoadDisabled()
    {
        _disabled.Clear();
        foreach (var n in PlayerPrefs.GetString(DisabledPrefKey, "").Split('\n'))
            if (!string.IsNullOrEmpty(n)) _disabled.Add(n);
    }

    void SaveDisabled()
    {
        PlayerPrefs.SetString(DisabledPrefKey, string.Join("\n", _disabled));
        PlayerPrefs.Save();
    }

    // ── playback ─────────────────────────────────────────────────────────────
    /// <summary>Play a specific track now (used when the player clicks a song).</summary>
    public void PlayTrack(int index)
    {
        if (index < 0 || index >= _clips.Count) return;
        _currentIndex = index;
        StopAllCoroutines();
        _audioSource.clip = _clips[index];
        _audioSource.Play();
        StartCoroutine(WaitForTrackEnd());
        OnTrackChanged?.Invoke();
    }

    /// <summary>Skip to the next (random enabled) track.</summary>
    public void Next() => PlayNext();

    void PlayNext()
    {
        if (_clips.Count == 0) return;

        var pool = new List<int>();
        for (int i = 0; i < _clips.Count; i++) if (IsEnabled(i)) pool.Add(i);
        if (pool.Count == 0) { _audioSource.Stop(); _currentIndex = -1; OnTrackChanged?.Invoke(); return; }

        int next = pool[Random.Range(0, pool.Count)];
        // Avoid repeating the same track when there's a choice.
        if (pool.Count > 1) { int guard = 0; while (next == _currentIndex && guard++ < 8) next = pool[Random.Range(0, pool.Count)]; }
        PlayTrack(next);
    }

    IEnumerator WaitForTrackEnd()
    {
        yield return new WaitWhile(() => _audioSource.isPlaying);
        yield return new WaitForSeconds(0.5f);
        PlayNext();
    }
}

// Auto-create the manager even if it's not placed in a scene.
public static class MusicInitializer
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        if (MusicManager.Instance == null)
            new GameObject("MusicManager").AddComponent<MusicManager>();
    }
}
