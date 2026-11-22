using System;
using System.Collections.Generic;
using KLTN.Game.Domain;
using KLTN.Game.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    public sealed class NetworkCardVisual :
        MonoBehaviour,
        IPointerClickHandler,
        IPointerEnterHandler,
        IPointerExitHandler
    {
        #region Serialized Fields

        [Header("Artwork")]
        [SerializeField]
        private RectTransform artworkMount;

        [SerializeField]
        private Image cardBackImage;

        [Header("Runtime Text")]
        [SerializeField]
        private TMP_Text fallbackNameText;

        [SerializeField]
        private TMP_Text costText;

        [SerializeField]
        private TMP_Text damageText;

        [SerializeField]
        private TMP_Text healthText;

        [SerializeField]
        private TMP_Text keywordText;

        [Header("Visual State")]
        [SerializeField]
        private GameObject pendingOverlay;

        [SerializeField]
        private Image abilityTargetOverlay;

        [SerializeField]
        private CanvasGroup canvasGroup;

        [Tooltip("Opacity of a Last Breath companion (e.g. Ma Da) during preview and combat.")]
        [SerializeField, Range(0.05f, 1f)]
        private float attackCompanionAlpha = 0.45f;

        #endregion

        #region Runtime State

        private CardArtworkView currentArtworkPrefab;
        private CardArtworkView spawnedArtwork;
        private Action<string> abilityTargetClicked;
        private Action<string> abilityTargetEntered;
        private Action<string> abilityTargetExited;
        private int authoritativeDamage;
        private int authoritativeHealth;
        private bool interactableVisual = true;

        #endregion

        #region Properties

        public string InstanceId { get; private set; }
        public string DefinitionId { get; private set; }
        public string DisplayName { get; private set; }
        public int Energy { get; private set; }
        public int SupportDamageBonus { get; private set; }
        public int SupportHealthBonus { get; private set; }
        public int CurrentDamage => authoritativeDamage;
        public int CurrentHealth => authoritativeHealth;
        public CardArtworkView CurrentArtworkPrefab => currentArtworkPrefab;
        public bool IsOpponentCard { get; private set; }

        /// <summary>True for a Last Breath companion; the card stays translucent.</summary>
        public bool IsAttackCompanion { get; private set; }

        /// <summary>
        /// The opacity this card should rest at. Animations that hide or flash the card
        /// must return to this value instead of 1 so a companion stays translucent.
        /// </summary>
        public float RestingAlpha => IsAttackCompanion ? attackCompanionAlpha : 1f;

        public RectTransform ArtworkMount => artworkMount;
        public RectTransform CardRect => transform as RectTransform;
        public RectTransform ManaAnchor => costText != null ? costText.rectTransform : CardRect;

        #endregion

        #region Binding

        /// <summary>Stages the captured mana value until the final snapshot binds.</summary>
        public void SetDisplayedEnergy(int value)
        {
            Energy = value;
            if (costText != null) costText.text = value.ToString();
        }

        public void BindFaceUp(CardViewDto dto, CardArtworkView artworkPrefab)
        {
            InstanceId = dto.instanceId;
            DefinitionId = dto.definitionId;
            DisplayName = dto.displayName;
            Energy = dto.energy;
            SupportDamageBonus = dto.supportDamageBonus;
            SupportHealthBonus = dto.supportHealthBonus;
            authoritativeDamage = dto.damage;
            authoritativeHealth = dto.health;

            bool hasArtwork = artworkMount != null && artworkPrefab != null;

            SetCardBack(null);
            SetFaceArtwork(hasArtwork ? artworkPrefab : null);

            if (fallbackNameText != null)
            {
                fallbackNameText.gameObject.SetActive(!hasArtwork);
                fallbackNameText.text = dto.displayName;
            }

            if (costText != null)
            {
                costText.text = dto.energy.ToString();
                costText.gameObject.SetActive(true);
            }

            SetDisplayedStats(authoritativeDamage, authoritativeHealth);

            SetKeywords((UnitKeyword)dto.keywords);

            IsAttackCompanion = dto.isAttackCompanion;

            SetPending(false);
            ClearAbilityTargetState();
            SetInteractableVisual(true);
        }

        /// <summary>Marks or unmarks this view as a translucent Last Breath companion.</summary>
        public void SetAttackCompanion(bool value)
        {
            IsAttackCompanion = value;
            ApplyAlpha();
        }

        public void BindBack(Sprite cardBack)
        {
            InstanceId = null;
            DefinitionId = null;
            DisplayName = null;
            Energy = 0;
            SupportDamageBonus = 0;
            SupportHealthBonus = 0;
            authoritativeDamage = 0;
            authoritativeHealth = 0;

            ClearFaceArtwork();
            SetCardBack(cardBack);

            if (fallbackNameText != null)
            {
                fallbackNameText.gameObject.SetActive(false);
            }

            SetTextActive(costText, false);
            SetTextActive(damageText, false);
            SetTextActive(healthText, false);
            SetKeywords(UnitKeyword.None);
            IsAttackCompanion = false;
            SetPending(false);
            ClearAbilityTargetState();
            SetInteractableVisual(true);
        }

        #endregion

        #region Artwork

        private void SetFaceArtwork(CardArtworkView artworkPrefab)
        {
            if (artworkPrefab == currentArtworkPrefab && spawnedArtwork != null)
            {
                return;
            }

            ClearFaceArtwork();

            if (artworkPrefab == null || artworkMount == null)
            {
                return;
            }

            currentArtworkPrefab = artworkPrefab;
            spawnedArtwork = Instantiate(artworkPrefab, artworkMount, false);

            RectTransform artworkRect = spawnedArtwork.transform as RectTransform;

            if (artworkRect == null)
            {
                Debug.LogError(
                    $"Artwork prefab {artworkPrefab.name} requires a RectTransform.",
                    artworkPrefab
                );

                ClearFaceArtwork();
                return;
            }

            artworkRect.anchorMin = Vector2.zero;
            artworkRect.anchorMax = Vector2.one;
            artworkRect.pivot = new Vector2(0.5f, 0.5f);
            artworkRect.offsetMin = Vector2.zero;
            artworkRect.offsetMax = Vector2.zero;
            artworkRect.localScale = Vector3.one;
            artworkRect.localRotation = Quaternion.identity;
            artworkRect.SetAsFirstSibling();
        }

        private void ClearFaceArtwork()
        {
            currentArtworkPrefab = null;

            if (spawnedArtwork == null)
            {
                return;
            }

            spawnedArtwork.gameObject.SetActive(false);
            Destroy(spawnedArtwork.gameObject);
            spawnedArtwork = null;
        }

        private void SetCardBack(Sprite cardBack)
        {
            if (cardBackImage == null)
            {
                return;
            }

            bool visible = cardBack != null;

            cardBackImage.sprite = cardBack;
            cardBackImage.enabled = visible;
            cardBackImage.gameObject.SetActive(visible);
        }

        #endregion

        #region Visual State

        public void SetOwnerPerspective(bool isOpponentCard)
        {
            IsOpponentCard = isOpponentCard;
        }

        public void SetAbilityTargetEmphasis(bool emphasized, float scaleBoost, float lift)
        {
            GetComponent<CardVisualLayout>()?.SetSelectionEmphasis(
                emphasized,
                IsOpponentCard,
                scaleBoost,
                lift
            );
        }

        public void SetPending(bool value)
        {
            if (pendingOverlay != null)
            {
                pendingOverlay.SetActive(value);
            }
        }

        public void SetStatPreview(int damageBonus, int healthBonus)
        {
            SetDisplayedStats(
                Math.Max(0, authoritativeDamage + damageBonus),
                Math.Max(0, authoritativeHealth + healthBonus)
            );
        }

        public void ClearStatPreview()
        {
            SetDisplayedStats(authoritativeDamage, authoritativeHealth);
        }

        public void SetAbilityTargetState(
            Color color,
            Action<string> clickHandler,
            Action<string> enterHandler = null,
            Action<string> exitHandler = null
        )
        {
            abilityTargetClicked = clickHandler;
            abilityTargetEntered = enterHandler;
            abilityTargetExited = exitHandler;

            if (abilityTargetOverlay == null)
            {
                return;
            }

            abilityTargetOverlay.color = color;

            abilityTargetOverlay.raycastTarget = false;

            abilityTargetOverlay.gameObject.SetActive(true);
        }

        public void ClearAbilityTargetState()
        {
            abilityTargetClicked = null;
            abilityTargetEntered = null;
            abilityTargetExited = null;
            SetAbilityTargetEmphasis(false, 0f, 0f);

            if (abilityTargetOverlay != null)
            {
                abilityTargetOverlay.gameObject.SetActive(false);
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                if (!string.IsNullOrWhiteSpace(DefinitionId))
                {
                    CardDetailModal.TryShow(this);
                }
                return;
            }
            
            if (
                eventData.button != PointerEventData.InputButton.Left
                || string.IsNullOrWhiteSpace(InstanceId)
                || abilityTargetClicked == null
            )
            {
                return;
            }

            abilityTargetClicked.Invoke(InstanceId);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!string.IsNullOrWhiteSpace(InstanceId))
            {
                abilityTargetEntered?.Invoke(InstanceId);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!string.IsNullOrWhiteSpace(InstanceId))
            {
                abilityTargetExited?.Invoke(InstanceId);
            }
        }

        public void SetInteractableVisual(bool value)
        {
            interactableVisual = value;
            ApplyAlpha();
        }

        private void ApplyAlpha()
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = interactableVisual ? RestingAlpha : RestingAlpha * 0.55f;
            }
        }

        #endregion

        #region Helpers

        private static void SetTextActive(TMP_Text label, bool value)
        {
            if (label != null)
            {
                label.gameObject.SetActive(value);
            }
        }

        private void SetDisplayedStats(int damage, int health)
        {
            if (damageText != null)
            {
                damageText.text = damage.ToString();
                damageText.gameObject.SetActive(true);
            }

            if (healthText != null)
            {
                healthText.text = health.ToString();
                healthText.gameObject.SetActive(true);
            }
        }

        #endregion

        private void SetKeywords(UnitKeyword keywords)
        {
            if (keywordText == null)
            {
                return;
            }

            var labels = new List<string>();

            if ((keywords & UnitKeyword.Fearsome) != 0)
            {
                labels.Add("F");
            }

            if ((keywords & UnitKeyword.CannotBlock) != 0)
            {
                labels.Add("CB");
            }

            if ((keywords & UnitKeyword.Ephemeral) != 0)
            {
                labels.Add("E");
            }

            if ((keywords & UnitKeyword.Lifesteal) != 0)
            {
                labels.Add("L");
            }

            keywordText.text = string.Join("  ", labels);

            keywordText.gameObject.SetActive(labels.Count > 0);
        }
    }
}
