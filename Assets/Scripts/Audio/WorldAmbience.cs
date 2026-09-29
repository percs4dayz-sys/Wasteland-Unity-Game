using UnityEngine;

/// <summary>
/// Sounds synthesized in code, so the world can have ambience without any audio files in the project
/// (only music and a level-up jingle exist). They're simple on purpose: a wind bed, crackling fire,
/// bird chirps, and distant metal/creaks/thumps. Drop real recordings in later by swapping the clips.
/// Every clip is built once and cached.
/// </summary>
public static class ProceduralAudio
{
    const int SR = 22050;
    static AudioClip _wind, _crackle, _clang, _creak, _chirp, _thump;

    static AudioClip Make(string name, float[] d)
    {
        float peak = 0.0001f;
        foreach (float s in d) peak = Mathf.Max(peak, Mathf.Abs(s));
        for (int i = 0; i < d.Length; i++) d[i] /= peak;
        var clip = AudioClip.Create(name, d.Length, 1, SR, false);
        clip.SetData(d, 0);
        return clip;
    }

    /// <summary>Six seconds of low, slowly swelling wind, crossfaded so it loops without a click.</summary>
    public static AudioClip Wind()
    {
        if (_wind != null) return _wind;
        int n = SR * 6, fade = SR / 2;
        var rng = new System.Random(11);
        var raw = new float[n + fade];
        float y = 0f, y2 = 0f;
        for (int i = 0; i < raw.Length; i++)
        {
            float x = (float)(rng.NextDouble() * 2 - 1);
            y += (x - y) * 0.03f; y2 += (y - y2) * 0.05f;   // two low-pass stages: a soft rush
            float t = (float)i / SR;
            float swell = 0.55f + 0.25f * Mathf.Sin(2f * Mathf.PI * t / 6f) + 0.2f * Mathf.Sin(2f * Mathf.PI * 3f * t / 6f + 1f);
            raw[i] = y2 * swell;
        }
        var d = new float[n];
        for (int i = 0; i < n; i++)
        {
            d[i] = raw[i];
            if (i < fade) d[i] = Mathf.Lerp(raw[n + i], raw[i], (float)i / fade);   // blend the tail into the head
        }
        return _wind = Make("ambient_wind", d);
    }

    /// <summary>Three seconds of fire: a soft hiss with random pops. Loops.</summary>
    public static AudioClip Crackle()
    {
        if (_crackle != null) return _crackle;
        int n = SR * 3;
        var rng = new System.Random(5);
        var d = new float[n];
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            lp += ((float)(rng.NextDouble() * 2 - 1) - lp) * 0.15f;
            d[i] = lp * 0.12f;
        }
        int pos = 0;
        while (pos < n - 400)
        {
            pos += rng.Next(SR / 40, SR / 5);
            float amp = 0.4f + (float)rng.NextDouble() * 0.6f;
            int len = rng.Next(40, 160);
            for (int k = 0; k < len && pos + k < n; k++)
                d[pos + k] += (float)(rng.NextDouble() * 2 - 1) * amp * Mathf.Exp(-k / (len * 0.25f));
        }
        d[n - 1] = d[0] = 0f;
        return _crackle = Make("ambient_crackle", d);
    }

    /// <summary>A distant metallic clang: inharmonic partials with a fast decay.</summary>
    public static AudioClip Clang()
    {
        if (_clang != null) return _clang;
        int n = (int)(SR * 1.8f);
        var d = new float[n];
        float[] f = { 410f, 690f, 1240f, 1810f };
        float[] a = { 1f, 0.7f, 0.4f, 0.25f };
        var rng = new System.Random(3);
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SR, s = 0f;
            for (int k = 0; k < f.Length; k++) s += Mathf.Sin(2f * Mathf.PI * f[k] * t) * a[k] * Mathf.Exp(-t * (3f + k * 2f));
            if (i < 300) s += (float)(rng.NextDouble() * 2 - 1) * (1f - i / 300f);   // the strike
            d[i] = s;
        }
        return _clang = Make("ambient_clang", d);
    }

    /// <summary>A groaning wooden/metal creak: a wobbling sawtooth that rises and falls.</summary>
    public static AudioClip Creak()
    {
        if (_creak != null) return _creak;
        int n = (int)(SR * 0.9f);
        var d = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float f = 140f + 90f * Mathf.Sin(t * Mathf.PI) + 25f * Mathf.Sin(t * 60f);
            phase += f / SR;
            float saw = (phase % 1f) * 2f - 1f;
            d[i] = (float)System.Math.Tanh(saw * 2.5f) * Mathf.Sin(t * Mathf.PI);
        }
        return _creak = Make("ambient_creak", d);
    }

    /// <summary>Three quick rising bird chirps.</summary>
    public static AudioClip Chirp()
    {
        if (_chirp != null) return _chirp;
        int n = (int)(SR * 0.6f), note = (int)(SR * 0.09f);
        var d = new float[n];
        var rng = new System.Random(9);
        for (int c = 0; c < 3; c++)
        {
            float f0 = 2300f + (float)rng.NextDouble() * 1100f;
            int start = (int)(SR * (0.03f + c * 0.15f));
            float phase = 0f;
            for (int i = 0; i < note && start + i < n; i++)
            {
                float t = (float)i / note;
                phase += (f0 + 900f * t) / SR;
                d[start + i] = Mathf.Sin(2f * Mathf.PI * phase) * Mathf.Sin(t * Mathf.PI);
            }
        }
        return _chirp = Make("ambient_chirp", d);
    }

    /// <summary>A far-off low thump, like something heavy falling in the ruins.</summary>
    public static AudioClip Thump()
    {
        if (_thump != null) return _thump;
        int n = SR * 2;
        var d = new float[n];
        var rng = new System.Random(21);
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SR;
            lp += ((float)(rng.NextDouble() * 2 - 1) - lp) * 0.02f;
            d[i] = (Mathf.Sin(2f * Mathf.PI * 52f * t) + lp * 3f) * Mathf.Exp(-t * 2.6f);
        }
        return _thump = Make("ambient_thump", d);
    }
}

/// <summary>
/// The background sound of the wasteland: a wind bed that's always there once you're in the world, plus
/// occasional distant sounds (birds, clanging metal, creaks, a far thump) from random directions so the
/// world feels like it's going on without you. Self-bootstrapping; quiet by design.
/// </summary>
public class WorldAmbience : MonoBehaviour
{
    const float WindVolume = 0.16f;
    AudioSource _wind;
    AudioSource[] _spots;
    int _nextSpot;
    float _nextSound;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("WorldAmbience (auto)");
        DontDestroyOnLoad(go);
        go.AddComponent<WorldAmbience>();
    }

    void Awake()
    {
        _wind = gameObject.AddComponent<AudioSource>();
        _wind.clip = ProceduralAudio.Wind();
        _wind.loop = true;
        _wind.spatialBlend = 0f;
        _wind.volume = 0f;

        _spots = new AudioSource[3];
        for (int i = 0; i < _spots.Length; i++)
        {
            var child = new GameObject("Spot" + i);
            child.transform.SetParent(transform, false);
            var s = child.AddComponent<AudioSource>();
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 8f; s.maxDistance = 90f;
            _spots[i] = s;
        }
        _nextSound = Time.time + 8f;
    }

    void Update()
    {
        var p = PlayerEntity.Instance;
        bool inWorld = p != null;
        float target = inWorld ? WindVolume : 0f;
        _wind.volume = Mathf.MoveTowards(_wind.volume, target, Time.deltaTime * 0.1f);
        if (inWorld && !_wind.isPlaying) _wind.Play();
        if (!inWorld && _wind.volume <= 0.001f && _wind.isPlaying) _wind.Pause();

        if (!inWorld || Time.time < _nextSound) return;
        _nextSound = Time.time + Random.Range(12f, 32f);

        AudioClip clip; float vol;
        float roll = Random.value;
        if (roll < 0.40f)      { clip = ProceduralAudio.Chirp(); vol = 0.35f; }
        else if (roll < 0.62f) { clip = ProceduralAudio.Clang();  vol = 0.30f; }
        else if (roll < 0.82f) { clip = ProceduralAudio.Creak();  vol = 0.30f; }
        else                   { clip = ProceduralAudio.Thump();  vol = 0.45f; }

        var src = _spots[_nextSpot++ % _spots.Length];
        Vector2 dir = Random.insideUnitCircle.normalized * Random.Range(25f, 70f);
        src.transform.position = p.transform.position + new Vector3(dir.x, 2f, dir.y);
        src.pitch = Random.Range(0.9f, 1.1f);
        src.volume = vol;
        src.clip = clip;
        src.Play();
    }
}
