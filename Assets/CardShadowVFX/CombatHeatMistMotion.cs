
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class CombatHeatMistMotion : MonoBehaviour
{
    [Header("Heat Motion")]
    [SerializeField] private float floatDistance = 8f;
    [SerializeField] private float floatSpeed = 1.5f;

    [Header("Heat Opacity")]
    [SerializeField] private float pulseSpeed = 2f;
    [SerializeField, Range(0f, 1f)] private float minAlpha = 0.35f;
    [SerializeField, Range(0f, 1f)] private float maxAlpha = 0.65f;

    private RectTransform rectTransform;
    private Image heatImage;
    private Vector2 originalPosition;
    private float time;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        heatImage = GetComponent<Image>();
        originalPosition = rectTransform.anchoredPosition;
    }

    private void OnEnable()
    {
        if (rectTransform == null)
            rectTransform = GetComponent<RectTransform>();

        if (heatImage == null)
            heatImage = GetComponent<Image>();

        originalPosition = rectTransform.anchoredPosition;
        time = 0f;
    }

    private void Update()
    {
        time += Time.deltaTime;

        float offset =
            (1f - Mathf.Cos(time * floatSpeed))
            * 0.5f * floatDistance;

        rectTransform.anchoredPosition =
            originalPosition + Vector2.up * offset;

        float pulse =
            (Mathf.Sin(time * pulseSpeed) + 1f) * 0.5f;

        Color color = heatImage.color;
        color.a = Mathf.Lerp(minAlpha, maxAlpha, pulse);
        heatImage.color = color;
    }

    private void OnDisable()
    {
        if (rectTransform != null)
            rectTransform.anchoredPosition = originalPosition;
    }
}
