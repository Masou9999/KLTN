using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    [DisallowMultipleComponent]
    public sealed class CombatVfxPresenter : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Rendering")]
        [SerializeField]
        private Canvas sourceCanvas;

        [SerializeField]
        private Camera vfxCamera;

        [SerializeField]
        private RawImage outputImage;

        [SerializeField]
        private Transform effectRoot;

        [SerializeField]
        private string effectLayerName = "CombatVfx";

        [Range(0.25f, 1f)]
        [SerializeField]
        private float renderScale = 1f;

        [Min(0.1f)]
        [SerializeField]
        private float effectDistance = 10f;

        [Min(1f)]
        [SerializeField]
        private float maximumEffectLifetime = 5f;

        [Header("Prefabs")]
        [SerializeField]
        private ParticleSystem cardHitPrefab;

        [SerializeField]
        private ParticleSystem nexusHitPrefab;

        [SerializeField]
        private ParticleSystem deathDustPrefab;

        [SerializeField]
        private ParticleSystem slideEffectPrefab;

        [Header("Vong Nhi VFX")]
[SerializeField]
private GameObject vongNhiSoulOrbPrefab;

[SerializeField]
private GameObject vongNhiSoulBlastPrefab;

[Min(0.01f)]
[SerializeField]
private float vongNhiSoulOrbScale = 3f;

        [Header("Scale")]
        [Min(0.01f)]
        [SerializeField]
        private float cardHitScale = 1f;

        [Min(0.01f)]
        [SerializeField]
        private float nexusHitScale = 1f;

        [Min(0.01f)]
        [SerializeField]
        private float deathDustScale = 1f;

        [Min(0.01f)]
        [SerializeField]
        private float slideEffectScale = 1f;

        [Header("Pooling")]
        [Min(0)]
        [SerializeField]
        private int prewarmCountPerPrefab = 4;

        #endregion

        #region Runtime State

        private readonly Dictionary<
            ParticleSystem,
            Queue<ParticleSystem>
        > availableByPrefab = new Dictionary<ParticleSystem, Queue<ParticleSystem>>();

        private readonly Dictionary<ParticleSystem, ParticleSystem> sourceByInstance =
            new Dictionary<ParticleSystem, ParticleSystem>();

        private readonly HashSet<ParticleSystem> activeInstances =
            new HashSet<ParticleSystem>();

        private readonly List<ParticleSystem> activeBuffer = new List<ParticleSystem>();

        private RenderTexture renderTexture;
        private int effectLayer = -1;
        private int textureWidth;
        private int textureHeight;
        private bool initialized;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (Application.isBatchMode)
            {
                DisableOutput();
                enabled = false;
                return;
            }

            Initialize();
        }

        private void OnEnable()
        {
            if (!initialized)
            {
                return;
            }

            if (vfxCamera != null)
            {
                vfxCamera.enabled = true;
            }
            if (outputImage != null)
            {
                outputImage.enabled = true;
            }

            EnsureRenderTexture();
        }

        private void LateUpdate()
        {
            if (initialized)
            {
                EnsureRenderTexture();
            }
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            ReturnAllActive();

            if (vfxCamera != null)
            {
                vfxCamera.enabled = false;
            }
            if (outputImage != null)
            {
                outputImage.enabled = false;
            }
        }

        private void OnDestroy()
        {
            ReleaseRenderTexture();
        }

        #endregion

        #region Public API

        public void PlaySlide(Vector3 overlayWorldPosition, bool flip)
        {
            Spawn(
                slideEffectPrefab,
                overlayWorldPosition,
                flip ? 180f : 0f,
                slideEffectScale
            );
        }

        public void PlayCardHit(Vector3 overlayWorldPosition)
        {
            Spawn(cardHitPrefab, overlayWorldPosition, 0f, cardHitScale);
        }

        public void PlayNexusHit(Vector3 overlayWorldPosition, bool targetIsSelf)
        {
            Spawn(
                nexusHitPrefab,
                overlayWorldPosition,
                targetIsSelf ? 180f : 0f,
                nexusHitScale
            );
        }

        public void PlayDeathDust(Vector3 overlayWorldPosition)
        {
            Spawn(deathDustPrefab, overlayWorldPosition, 0f, deathDustScale);
        }

        public void PlayVongNhiSoulOrb(Vector3 overlayWorldPosition)
{
    
    if (vongNhiSoulOrbPrefab == null)
    {
        return;
    }

    if (!initialized ||
        !TryConvertPosition(overlayWorldPosition, out Vector3 vfxPosition))
    {
        return;
    }

    GameObject soulOrb = Instantiate(
        vongNhiSoulOrbPrefab,
        vfxPosition,
        Quaternion.identity,
        effectRoot
    );

    soulOrb.transform.localScale = Vector3.one * vongNhiSoulOrbScale;

    SetLayerRecursively(soulOrb, effectLayer);

    Destroy(soulOrb, 2f);
}
public IEnumerator PlayVongNhiSoulBlast(
    Vector3 startWorldPosition,
    Vector3 targetWorldPosition
)
{
    if (vongNhiSoulBlastPrefab == null)
    {
        yield break;
    }

    if (!initialized ||
        !TryConvertPosition(startWorldPosition, out Vector3 startVfxPosition))
    {
        yield break;
    }
    if (!TryConvertPosition(
    targetWorldPosition,
    out Vector3 targetVfxPosition))
{
    yield break;
}
GameObject soulBlast = Instantiate(
    vongNhiSoulBlastPrefab,
    startVfxPosition,
    Quaternion.identity,
    effectRoot
);
SetLayerRecursively(soulBlast, effectLayer);
float duration = 0.36f;
float elapsed = 0f;
while (elapsed < duration)
{
    elapsed += Time.unscaledDeltaTime;
    soulBlast.transform.position = Vector3.Lerp(startVfxPosition, targetVfxPosition, Mathf.Clamp01(elapsed / duration));
    yield return null;
}
PlayVongNhiSoulOrb(targetWorldPosition);
Destroy(soulBlast, 0.4f);
}

        #endregion

        #region Initialization

        private void Initialize()
        {
            if (initialized)
            {
                return;
            }

            if (sourceCanvas == null || vfxCamera == null || outputImage == null)
            {
                Debug.LogError("Combat VFX rendering references are incomplete.");
                enabled = false;
                return;
            }

            effectLayer = LayerMask.NameToLayer(effectLayerName);

            if (effectLayer < 0)
            {
                Debug.LogError($"Layer '{effectLayerName}' does not exist.");
                enabled = false;
                return;
            }

            if (effectRoot == null)
            {
                effectRoot = transform;
            }

            ConfigureCamera();
            RegisterPool(cardHitPrefab);
            RegisterPool(nexusHitPrefab);
            RegisterPool(deathDustPrefab);
            RegisterPool(slideEffectPrefab);

            initialized = true;
            EnsureRenderTexture();
        }

        private void ConfigureCamera()
        {
            vfxCamera.orthographic = true;
            vfxCamera.clearFlags = CameraClearFlags.SolidColor;
            vfxCamera.backgroundColor = Color.black;
            vfxCamera.cullingMask = 1 << effectLayer;
        }

        #endregion

        #region Render Texture

        private void EnsureRenderTexture()
        {
            int requiredWidth = Mathf.Max(
                16,
                Mathf.RoundToInt(Screen.width * renderScale)
            );
            int requiredHeight = Mathf.Max(
                16,
                Mathf.RoundToInt(Screen.height * renderScale)
            );

            if (
                renderTexture != null
                && requiredWidth == textureWidth
                && requiredHeight == textureHeight
            )
            {
                return;
            }

            RenderTexture previousTexture = renderTexture;

            textureWidth = requiredWidth;
            textureHeight = requiredHeight;

            renderTexture = new RenderTexture(
                textureWidth,
                textureHeight,
                24,
                RenderTextureFormat.ARGB32
            )
            {
                name = "CombatVfxRenderTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
            };

            renderTexture.Create();
            vfxCamera.targetTexture = renderTexture;
            outputImage.texture = renderTexture;

            if (previousTexture != null)
            {
                previousTexture.Release();
                Destroy(previousTexture);
            }
        }

        private void ReleaseRenderTexture()
        {
            if (renderTexture == null)
            {
                return;
            }

            if (vfxCamera != null && vfxCamera.targetTexture == renderTexture)
            {
                vfxCamera.targetTexture = null;
            }

            if (outputImage != null && outputImage.texture == renderTexture)
            {
                outputImage.texture = null;
            }

            renderTexture.Release();
            Destroy(renderTexture);
            renderTexture = null;
        }

        #endregion

        #region Spawning

        private void Spawn(
            ParticleSystem prefab,
            Vector3 overlayWorldPosition,
            float rotationDegrees,
            float scale
        )
        {
            if (!initialized || prefab == null)
            {
                return;
            }
            if (!TryConvertPosition(overlayWorldPosition, out Vector3 vfxPosition))
            {
                return;
            }

            ParticleSystem instance = Rent(prefab);

            if (instance == null)
            {
                return;
            }

            Quaternion rotation = Quaternion.AngleAxis(
                rotationDegrees,
                vfxCamera.transform.forward
            );

            instance.transform.SetPositionAndRotation(vfxPosition, rotation);
            instance.transform.localScale = Vector3.one * scale;
            instance.gameObject.SetActive(true);
            instance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            instance.Play(true);

            StartCoroutine(ReturnWhenComplete(instance));
        }

        private bool TryConvertPosition(
            Vector3 overlayWorldPosition,
            out Vector3 vfxPosition
        )
        {
            vfxPosition = Vector3.zero;

            if (Screen.width <= 0 || Screen.height <= 0)
            {
                return false;
            }

            Camera sourceCamera =
                sourceCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                    ? null
                    : sourceCanvas.worldCamera;

            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(
                sourceCamera,
                overlayWorldPosition
            );

            Vector3 viewportPoint = new Vector3(
                screenPoint.x / Screen.width,
                screenPoint.y / Screen.height,
                0f
            );

            Ray ray = vfxCamera.ViewportPointToRay(viewportPoint);

            Plane effectPlane = new Plane(
                vfxCamera.transform.forward,
                vfxCamera.transform.position
                    + vfxCamera.transform.forward * effectDistance
            );

            if (!effectPlane.Raycast(ray, out float enter))
            {
                return false;
            }

            vfxPosition = ray.GetPoint(enter);
            return true;
        }

        #endregion

        #region Pooling

        private void RegisterPool(ParticleSystem prefab)
        {
            if (prefab == null || availableByPrefab.ContainsKey(prefab))
            {
                return;
            }

            var queue = new Queue<ParticleSystem>();
            availableByPrefab.Add(prefab, queue);

            for (int i = 0; i < prewarmCountPerPrefab; i++)
            {
                queue.Enqueue(CreateInstance(prefab));
            }
        }

        private ParticleSystem CreateInstance(ParticleSystem prefab)
        {
            ParticleSystem instance = Instantiate(prefab, effectRoot);

            instance.name = $"{prefab.name}_Pooled";
            SetLayerRecursively(instance.gameObject, effectLayer);
            instance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            instance.gameObject.SetActive(false);

            sourceByInstance[instance] = prefab;
            return instance;
        }

        private ParticleSystem Rent(ParticleSystem prefab)
        {
            RegisterPool(prefab);
            Queue<ParticleSystem> queue = availableByPrefab[prefab];
            ParticleSystem instance = null;

            while (queue.Count > 0 && instance == null)
            {
                instance = queue.Dequeue();
            }

            if (instance == null)
            {
                instance = CreateInstance(prefab);
            }

            activeInstances.Add(instance);
            return instance;
        }

        private IEnumerator ReturnWhenComplete(ParticleSystem instance)
        {
            yield return null;

            float elapsed = 0f;

            while (
                instance != null
                && instance.IsAlive(true)
                && elapsed < maximumEffectLifetime
            )
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Return(instance);
        }

        private void Return(ParticleSystem instance)
        {
            if (instance == null || !activeInstances.Remove(instance))
            {
                return;
            }

            instance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            instance.gameObject.SetActive(false);

            if (
                sourceByInstance.TryGetValue(instance, out ParticleSystem prefab)
                && availableByPrefab.TryGetValue(prefab, out Queue<ParticleSystem> queue)
            )
            {
                queue.Enqueue(instance);
            }
        }

        private void ReturnAllActive()
        {
            activeBuffer.Clear();
            activeBuffer.AddRange(activeInstances);

            for (int i = 0; i < activeBuffer.Count; i++)
            {
                Return(activeBuffer[i]);
            }

            activeBuffer.Clear();
        }

        #endregion

        #region Helpers

        private void DisableOutput()
        {
            if (vfxCamera != null)
            {
                vfxCamera.enabled = false;
            }
            if (outputImage != null)
            {
                outputImage.enabled = false;
            }
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;

            Transform rootTransform = root.transform;

            for (int i = 0; i < rootTransform.childCount; i++)
            {
                SetLayerRecursively(rootTransform.GetChild(i).gameObject, layer);
            }
        }

        #endregion
    }
}
