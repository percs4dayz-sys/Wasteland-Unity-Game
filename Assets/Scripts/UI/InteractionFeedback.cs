using UnityEngine;
using System.Collections;

public class InteractionFeedback : MonoBehaviour
{
    private SpriteRenderer _renderer;
    private Color _originalColor;
    private Coroutine _flashCoroutine;

    void Awake()
    {
        _renderer = GetComponent<SpriteRenderer>();
        if (_renderer != null) _originalColor = _renderer.color;
    }

    public void Flash()
    {
        if (_renderer == null) return;
        if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
        _flashCoroutine = StartCoroutine(DoFlash());
    }

    private IEnumerator DoFlash()
    {
        _renderer.color = Color.white * 2f; // Slight bloom effect
        yield return new WaitForSeconds(0.1f);
        _renderer.color = _originalColor;
    }
}
