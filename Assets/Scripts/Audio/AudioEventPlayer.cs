// AudioEventPlayer.cs
// Place this script in Assets/Scripts/Audio/AudioEventPlayer.cs
// It loads AudioClips from Resources/SFX (or you can assign them manually) and provides
// a simple Play(string clipName) method to be called from animation events, interaction callbacks, or UI UnityEvents.

using System.Collections.Generic;
using UnityEngine;

[AddComponentMenu("Audio/Audio Event Player")]
public class AudioEventPlayer : MonoBehaviour
{
    // Optional: assign an AudioSource component, otherwise one will be added at runtime.
    [Tooltip("If left empty, a new AudioSource will be added to this GameObject.")]
    public AudioSource audioSource;

    // You can manually assign clips in the inspector or rely on Resources loading.
    [Header("Manual Clip Assignment (optional)")]
    public List<AudioClip> clips;
    public List<string> clipNames;

    // Internal map for fast lookup.
    private Dictionary<string, AudioClip> _clipMap = new Dictionary<string, AudioClip>();

    private void Awake()
    {
        // Ensure we have an AudioSource.
        if (audioSource == null)
        {
            audioSource = gameObject.GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
        }
        // Build map from manually assigned clips.
        if (clips != null && clipNames != null && clips.Count == clipNames.Count)
        {
            for (int i = 0; i < clips.Count; i++)
            {
                var name = clipNames[i];
                var clip = clips[i];
                if (!string.IsNullOrEmpty(name) && clip != null)
                {
                    _clipMap[name] = clip;
                }
            }
        }
        // Load any remaining clips from Resources/SFX.
        var resourcesClips = Resources.LoadAll<AudioClip>("SFX");
        foreach (var rc in resourcesClips)
        {
            if (!_clipMap.ContainsKey(rc.name))
            {
                _clipMap[rc.name] = rc;
            }
        }
    }

    /// <summary>
    /// Play a sound by its name. Use this from animation events, interaction scripts, or UnityEvents.
    /// </summary>
    /// <param name="clipName">The exact name of the AudioClip (case‑sensitive).</param>
    public void Play(string clipName)
    {
        if (string.IsNullOrEmpty(clipName)) return;
        if (_clipMap.TryGetValue(clipName, out var clip))
        {
            // PlayOneShot lets multiple sounds overlap.
            audioSource.PlayOneShot(clip);
        }
        else
        {
            Debug.LogWarning($"AudioEventPlayer: Clip '{clipName}' not found in manual list or Resources/SFX.");
        }
    }
}
