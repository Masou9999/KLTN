
using KLTN.Audio;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(AudioSource))]
public class UIClickManager : MonoBehaviour
{
    public static UIClickManager Instance { get; private set; }

    [SerializeField] private AudioClip clickSound;

    [SerializeField, Range(0f, 1f)]
    private float clickVolume = 0.5f;

    private AudioSource audioSource;

    // Âm lượng gốc đặt trong scene; slider SFX nhân lên trên mức này.
    private float baseVolume = 1f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;

        // Tiếng click là SFX: đi theo slider SFX (FR-SET-02).
        baseVolume = audioSource.volume;
        ApplySfxVolume();
        AudioVolumeSettings.Changed += ApplySfxVolume;
    }

    private void Update()
    {
        if (Mouse.current != null &&
            Mouse.current.leftButton.wasPressedThisFrame)
        {
            PlayClick();
        }
    }

    public void PlayClick()
    {
        if (clickSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(clickSound, clickVolume);
        }
    }

    private void ApplySfxVolume()
    {
        if (audioSource != null)
        {
            audioSource.volume = baseVolume * AudioVolumeSettings.SfxVolume;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            AudioVolumeSettings.Changed -= ApplySfxVolume;
            Instance = null;
        }
    }
}
