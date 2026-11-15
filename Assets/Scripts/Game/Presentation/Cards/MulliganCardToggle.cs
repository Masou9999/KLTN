using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DG.Tweening;

public class MulliganCardToggle : MonoBehaviour, IPointerClickHandler
{
    public Image replaceOverlay;
    public ulong cardInstanceId;
    public bool isSelected = false;
    public bool isInteractable = true;

    private Vector3 originalScale = Vector3.one;
    private GameObject replaceSpinnerOverlay;
    private CanvasGroup canvasGroup;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
    }

    private void Start()
    {
        Transform spinnerTransform = transform.Find("VisualRoot/ReplaceSpinnerOverlay");
        if (spinnerTransform != null)
        {
            replaceSpinnerOverlay = spinnerTransform.gameObject;
            replaceSpinnerOverlay.SetActive(isSelected); 
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!isInteractable) return;

        isSelected = !isSelected;

        if (replaceOverlay != null)
            replaceOverlay.gameObject.SetActive(isSelected);

        if (replaceSpinnerOverlay != null)
            replaceSpinnerOverlay.SetActive(isSelected);

        if (isSelected)
        {
            canvasGroup.alpha = 0.75f;
            transform.DOScale(originalScale * 0.85f, 0.15f).SetEase(Ease.OutQuad);
        }
        else
        {
            canvasGroup.alpha = 1f;
            transform.DOScale(originalScale, 0.15f).SetEase(Ease.OutQuad);
        }
    }
    public void LockAndClearOverlay()
    {
        isInteractable = false;
        transform.DOKill();

        if (replaceOverlay != null) replaceOverlay.gameObject.SetActive(false);
        if (replaceSpinnerOverlay != null) replaceSpinnerOverlay.SetActive(false);
        if (canvasGroup != null) canvasGroup.alpha = 1f;
    }
}
