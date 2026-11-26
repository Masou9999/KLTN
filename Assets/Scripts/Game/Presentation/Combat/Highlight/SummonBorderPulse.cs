using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class SummonBorderPulse : MonoBehaviour
{
    [Header("Pulse Settings")]
    [SerializeField] private float pulseSpeed = 2f;
    [SerializeField, Range(0f, 1f)] private float minAlpha = 0.55f;
    [SerializeField, Range(0f, 1f)] private float maxAlpha = 1f;

    private Image borderImage;

    private void Awake()
    {
        borderImage = GetComponent<Image>();
    }

    private void OnEnable()
    {
        if (borderImage == null)
            borderImage = GetComponent<Image>();

        UpdatePulse();
    }

    private void Update()
    {
        UpdatePulse();
    }

    private void UpdatePulse()
    {
        float wave = (Mathf.Sin(Time.unscaledTime * pulseSpeed) + 1f) * 0.5f;

        Color color = borderImage.color;
        color.a = Mathf.Lerp(minAlpha, maxAlpha, wave);

        borderImage.color = color;
    }
}