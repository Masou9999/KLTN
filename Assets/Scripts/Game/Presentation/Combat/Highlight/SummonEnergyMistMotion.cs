
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class SummonEnergyMistMotion : MonoBehaviour
{
    [Header("Mist Motion")]
    [SerializeField] private float floatDistance = 8f;
    [SerializeField] private float floatSpeed = 1.2f;

    [Header("Mist Glow")]
    [SerializeField] private float pulseSpeed = 1.5f;
    [SerializeField, Range(0f, 1f)] private float minAlpha = 0.25f;
    [SerializeField, Range(0f, 1f)] private float maxAlpha = 0.55f;

    private RectTransform rectTransform;
    private Image mistImage;
    private Vector2 originalPosition;
    private float time;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        mistImage = GetComponent<Image>();
        originalPosition = rectTransform.anchoredPosition;
    }

    private void OnEnable()
    {
        time = 0f;

        if (rectTransform != null)
            originalPosition = rectTransform.anchoredPosition;
    }

    private void Update()
    {
        time += Time.deltaTime;

        // Sương nhẹ nhàng bay lên rồi trở về vị trí ban đầu.
        float floatOffset =
            (1f - Mathf.Cos(time * floatSpeed)) * 0.5f * floatDistance;

        rectTransform.anchoredPosition =
            originalPosition + Vector2.up * floatOffset;

        // Ánh sáng thay đổi mềm mại.
        float pulse = (Mathf.Sin(time * pulseSpeed) + 1f) * 0.5f;

        Color color = mistImage.color;
        color.a = Mathf.Lerp(minAlpha, maxAlpha, pulse);
        mistImage.color = color;
    }

    private void OnDisable()
    {
        if (rectTransform != null)
            rectTransform.anchoredPosition = originalPosition;
    }
}
