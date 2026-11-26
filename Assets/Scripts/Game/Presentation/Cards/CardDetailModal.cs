using System.Collections.Generic;
using System.Text;
using CMCMProductions;
using KLTN.Game.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Card detail modal shared by GameScene and the MainMenu collection. It also builds
    /// the same card face (artwork plus cost, damage and health badges) for collection tiles.
    /// </summary>
    public sealed class CardDetailModal : MonoBehaviour
    {
        #region Configuration

        /// <summary>Passive keywords shown under [Nội tại]; Play/Summon are triggers, not passives.</summary>
        private static readonly (UnitKeyword Keyword, string Name, string Description)[] Passives =
        {
            (UnitKeyword.Ephemeral, "Phù Du",
                "Chết ngay sau khi giao chiến, hoặc khi vòng đấu kết thúc."),
            (UnitKeyword.Fearsome, "Đáng sợ",
                "Chỉ có thể bị chặn bởi đơn vị có từ " +
                MatchRulesEngine.FearsomeMinimumBlockerDamage + " sát thương trở lên."),
            (UnitKeyword.CannotBlock, "Không thể thủ",
                "Không thể được chọn để chặn đòn tấn công."),
            (UnitKeyword.Lifesteal, "Hút máu",
                "Sát thương gây ra khi giao chiến hồi máu cho người chơi sở hữu."),
        };

        private const string SkillHeader = "[Chi tiết kỹ năng]";

        #endregion

        #region Serialized Fields

        public static CardDetailModal Instance { get; private set; }

        [Header("Modal Root")]
        [SerializeField] private GameObject modalRoot;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button backdropCloseButton;

        [Header("Left Column - Card Preview")]
        [SerializeField] private RectTransform previewArtworkMount;
        [SerializeField] private TMP_Text previewCostText;
        [SerializeField] private TMP_Text previewDamageText;
        [SerializeField] private TMP_Text previewHealthText;
        [SerializeField] private TMP_Text previewFallbackNameText;

        [Header("Right Column - Card Info")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text roleAndStatsText;
        [SerializeField] private TMP_Text skillText;
        [SerializeField] private TMP_Text loreText;

        [Header("Passive Tooltip")]
        [Tooltip("Rounded background for the keyword tip, drawn Sliced.")]
        [SerializeField] private Sprite tooltipBackground;

        #endregion

        #region Runtime State

        private CardArtworkView spawnedPreviewArtwork;
        private TMP_Text passiveText;
        private bool initialized;
        public bool IsOpen => modalRoot != null && modalRoot.activeSelf;
        public static int LastClosedFrame { get; private set; } = -1;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (initialized) return;
            initialized = true;
            Instance = this;
            if (closeButton != null) closeButton.onClick.AddListener(Hide);
            if (backdropCloseButton != null) backdropCloseButton.onClick.AddListener(Hide);
            BuildPassiveSection();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (closeButton != null) closeButton.onClick.RemoveListener(Hide);
            if (backdropCloseButton != null) backdropCloseButton.onClick.RemoveListener(Hide);
        }

        private void Update()
        {
            if (modalRoot != null && modalRoot.activeSelf && UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Hide();
            }
        }

        #endregion

        #region Public API

        public static void TryShow(NetworkCardVisual card)
        {
            if (card == null || string.IsNullOrWhiteSpace(card.DefinitionId)) return;
            if (Instance == null) Instance = FindAnyObjectByType<CardDetailModal>(FindObjectsInactive.Include);

            if (Instance != null) Instance.Show(card);
        }

        public void Show(NetworkCardVisual card)
        {
            if (card == null) return;
            // Keywords are static card content; live stats still come from the match snapshot.
            var definition = Resources.Load<Card>("Cards/" + card.DefinitionId);
            Show(new CardDetailData(
                string.IsNullOrWhiteSpace(card.DisplayName) ? card.DefinitionId : card.DisplayName,
                card.Energy, card.CurrentDamage, card.CurrentHealth, card.CurrentArtworkPrefab,
                keywords: definition != null ? definition.keywords : UnitKeyword.None));
        }

        /// <summary>Displays either a match snapshot or a collection's base card data.</summary>
        public void Show(CardDetailData card)
        {
            Initialize();
            if (modalRoot != null) modalRoot.SetActive(true);
            else gameObject.SetActive(true);

            SetupLeftCardPreview(card);

            string finalSkill = string.IsNullOrWhiteSpace(card.Rules)
                ? "Chưa có mô tả kỹ năng." : card.Rules;
            string finalLore = "Chưa có giai thoại dân gian.";

            if (card.Artwork != null)
            {
                var loreProfile = card.Artwork.GetComponent<CardLoreProfile>();
                if (loreProfile != null)
                {
                    finalSkill = loreProfile.skillDescription;
                    finalLore = loreProfile.folklore;
                }
            }

            if (titleText != null) titleText.text = card.Name.ToUpperInvariant();

            if (roleAndStatsText != null)
                roleAndStatsText.text =
                    $"Năng lượng: <color=#4EA8DE>{card.Energy}</color>   |   " +
                    $"Sát thương: <color=#EF476F>{card.Damage}</color>   |   " +
                    $"Máu: <color=#06D6A0>{card.Health}</color>";

            if (skillText != null)
            {
                // Cards whose only rules are passives (listed under [Nội tại]) have no
                // active skill left, so the empty "[Chi tiết kỹ năng]" heading is hidden.
                bool hasSkill = !string.IsNullOrWhiteSpace(finalSkill)
                    && finalSkill.Trim() != SkillHeader;
                skillText.gameObject.SetActive(hasSkill);
                skillText.text = hasSkill ? finalSkill.Trim() : "";
            }

            if (loreText != null) loreText.text = $"<i>\"{finalLore}\"</i>";

            if (passiveText != null)
            {
                string passives = PassiveList(card.Keywords);
                passiveText.gameObject.SetActive(passives != null);
                passiveText.text = passives != null ? "[Nội tại]\n" + passives : "";
            }
        }

        public void Hide()
        {
            LastClosedFrame = Time.frameCount;
            ClearPreviewArtwork();
            if (modalRoot != null) modalRoot.SetActive(false);
            else gameObject.SetActive(false);
        }

        /// <summary>
        /// Builds the detail preview's card face under <paramref name="parent"/>, scaled to
        /// <paramref name="width"/>: the same artwork and stat badges the modal shows.
        /// </summary>
        /// <returns>The face root, or null when this modal has no preview to copy.</returns>
        public RectTransform CreateCardFace(CardDetailData card, RectTransform parent,
            float width)
        {
            if (previewArtworkMount == null || previewArtworkMount.parent == null) return null;
            var template = (RectTransform)previewArtworkMount.parent;
            var face = Instantiate(template, parent, false);
            face.name = "CardFace";
            var keep = new HashSet<string> { previewArtworkMount.name };
            foreach (var text in new[] { previewCostText, previewDamageText, previewHealthText })
                if (text != null) keep.Add(text.name);
            // Match-only overlays (crack, pending, spinner…) are not part of a card face.
            for (int i = face.childCount - 1; i >= 0; i--)
            {
                var child = face.GetChild(i).gameObject;
                if (keep.Contains(child.name)) continue;
                child.SetActive(false);
                Destroy(child);
            }
            face.anchorMin = face.anchorMax = new Vector2(.5f, .5f);
            face.pivot = new Vector2(.5f, .5f);
            face.anchoredPosition = Vector2.zero;
            face.localScale = Vector3.one * (width / template.rect.width);
            var mount = (RectTransform)face.Find(previewArtworkMount.name);
            // Drop artwork the open preview may have spawned into the copied mount.
            foreach (var stale in mount.GetComponentsInChildren<CardArtworkView>(true))
            {
                stale.gameObject.SetActive(false);
                Destroy(stale.gameObject);
            }
            if (card.Artwork != null) MountArtwork(card.Artwork, mount);
            SetStat(face, previewCostText, card.Energy);
            SetStat(face, previewDamageText, card.Damage);
            SetStat(face, previewHealthText, card.Health);
            foreach (var graphic in face.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
            return face;
        }

        #endregion

        #region Passive Keywords

        private void BuildPassiveSection()
        {
            if (skillText == null || passiveText != null) return;
            // The layout group packs sections from the top; these margins separate the
            // lore, skill and passive blocks while title and stats stay close together.
            if (loreText != null) loreText.margin = new Vector4(0, 18, 0, 0);
            skillText.margin = new Vector4(0, 18, 0, 0);
            passiveText = Instantiate(skillText, skillText.transform.parent, false);
            passiveText.name = "PassiveText";
            passiveText.transform.SetSiblingIndex(skillText.transform.GetSiblingIndex() + 1);
            passiveText.richText = true;
            passiveText.raycastTarget = true;
            passiveText.gameObject.SetActive(false);

            // The prefab root carries its own override-sorting Canvas; parenting the tip
            // there keeps it above the modal in both MainMenu and GameScene.
            Transform root = transform;
            var tip = new GameObject("PassiveTooltip", typeof(RectTransform), typeof(Image))
                .GetComponent<RectTransform>();
            tip.gameObject.layer = gameObject.layer;
            tip.SetParent(root, false);
            tip.anchorMin = tip.anchorMax = new Vector2(.5f, .5f);
            // Top-right pivot: the tip opens to the cursor's bottom-left.
            tip.pivot = new Vector2(1, 1);
            var background = tip.GetComponent<Image>();
            background.sprite = tooltipBackground;
            background.type = tooltipBackground != null ? Image.Type.Sliced : Image.Type.Simple;
            // The shared rounded-fill corners are sized for large panels; shrink them for a small tip.
            background.pixelsPerUnitMultiplier = 3f;
            background.color = new Color(.07f, .055f, .065f, .96f);
            background.raycastTarget = false;
            var label = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI))
                .GetComponent<TextMeshProUGUI>();
            label.gameObject.layer = gameObject.layer;
            label.transform.SetParent(tip, false);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(22, 16);
            label.rectTransform.offsetMax = new Vector2(-22, -16);
            label.font = skillText.font;
            label.fontSize = 22;
            label.color = new Color(.94f, .88f, .80f);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
            passiveText.gameObject.AddComponent<KeywordTooltip>()
                .Initialize(passiveText, tip, label, Describe);
        }

        private static string PassiveList(UnitKeyword keywords)
        {
            var builder = new StringBuilder();
            foreach (var passive in Passives)
            {
                if ((keywords & passive.Keyword) == 0) continue;
                if (builder.Length > 0) builder.Append(", ");
                builder.Append($"<link={passive.Keyword}><color=#E57B73><u>")
                    .Append(passive.Name).Append("</u></color></link>");
            }
            return builder.Length > 0 ? builder.ToString() : null;
        }

        private static string Describe(string id)
        {
            foreach (var passive in Passives)
                if (passive.Keyword.ToString() == id)
                    return passive.Description;
            return null;
        }

        #endregion

        #region Preview

        private void SetupLeftCardPreview(CardDetailData card)
        {
            ClearPreviewArtwork();
            bool hasArtwork = previewArtworkMount != null && card.Artwork != null;

            if (hasArtwork) spawnedPreviewArtwork = MountArtwork(card.Artwork, previewArtworkMount);

            if (previewFallbackNameText != null) { previewFallbackNameText.gameObject.SetActive(!hasArtwork); previewFallbackNameText.text = card.Name; }
            if (previewCostText != null) { previewCostText.text = card.Energy.ToString(); previewCostText.gameObject.SetActive(true); }
            if (previewDamageText != null) { previewDamageText.text = card.Damage.ToString(); previewDamageText.gameObject.SetActive(true); }
            if (previewHealthText != null) { previewHealthText.text = card.Health.ToString(); previewHealthText.gameObject.SetActive(true); }
        }

        private static CardArtworkView MountArtwork(CardArtworkView artwork, RectTransform mount)
        {
            var spawned = Instantiate(artwork, mount, false);
            foreach (var graphic in spawned.GetComponentsInChildren<Graphic>())
                graphic.raycastTarget = false;
            if (spawned.transform is RectTransform rect)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.localScale = Vector3.one;
                rect.localRotation = Quaternion.identity;
                rect.SetAsFirstSibling();
            }
            return spawned;
        }

        private static void SetStat(Transform face, TMP_Text template, int value)
        {
            if (template == null) return;
            var copy = face.Find(template.name)?.GetComponent<TMP_Text>();
            if (copy == null) return;
            copy.text = value.ToString();
            copy.gameObject.SetActive(true);
        }

        private void ClearPreviewArtwork()
        {
            if (spawnedPreviewArtwork != null)
            {
                Destroy(spawnedPreviewArtwork.gameObject);
                spawnedPreviewArtwork = null;
            }
        }

        #endregion
    }
}
