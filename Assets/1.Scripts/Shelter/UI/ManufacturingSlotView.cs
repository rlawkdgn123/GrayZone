using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 제조 슬롯 하나의 잠금, 작업 정보, 취소 버튼을 표시합니다.
/// 작업 생성/취소는 <see cref="ManufacturingUI"/>를 거쳐 <see cref="ManufacturingManager"/>에 전달합니다.
/// </summary>
public sealed class ManufacturingSlotView : MonoBehaviour
{
    [Header("Controls")]
    [SerializeField] private Button m_itemButton;
    [SerializeField] private Image m_itemImage;
    [SerializeField] private Button m_cancelButton;

    [Header("Sprites")]
    [SerializeField] private Sprite m_emptySprite;
    [SerializeField] private Sprite m_lockedSprite;
    [SerializeField] private Sprite m_usedSprite;

    [Header("Texts")]
    [SerializeField] private TMP_Text m_nameText;
    [SerializeField] private TMP_Text m_remainingQuantityText;
    [SerializeField] private TMP_Text m_remainingDaysText;

    private int m_slotIndex = -1;
    private bool m_isUnlocked;
    // 임시 빌드 전용: 매니저에서 전달받은 이 슬롯의 1회 사용 가능 상태.
    private bool m_isAvailable;
    private bool m_hasJob;
    private bool m_isInteractionEnabled = true;
    private Action<int> m_slotClicked;
    private Action<int> m_cancelClicked;

    public int SlotIndex => m_slotIndex;
    public bool IsAvailable => m_isAvailable;
    public bool HasJob => m_hasJob;

    private void Awake()
    {
        CacheReferences();
    }

    private void Reset()
    {
        CacheReferences();
    }

    public void Bind(
        int slotIndex,
        bool isUnlocked,
        bool isAvailable,
        ManufacturingJobRuntimeData job,
        ManufacturingRecipeDefinition recipe,
        int productivity,
        Action<int> onSlotClicked,
        Action<int> onCancelClicked)
    {
        CacheReferences();

        m_slotIndex = slotIndex;
        m_isUnlocked = isUnlocked;
        m_isAvailable = isAvailable;
        m_hasJob = job != null;
        m_slotClicked = onSlotClicked;
        m_cancelClicked = onCancelClicked;

        if (m_itemButton != null)
        {
            m_itemButton.onClick.RemoveListener(HandleSlotClicked);
            m_itemButton.onClick.AddListener(HandleSlotClicked);
            m_itemButton.interactable =
                m_isInteractionEnabled && m_isUnlocked && m_isAvailable && !m_hasJob;
        }

        if (m_cancelButton != null)
        {
            m_cancelButton.onClick.RemoveListener(HandleCancelClicked);
            m_cancelButton.onClick.AddListener(HandleCancelClicked);
            m_cancelButton.gameObject.SetActive(m_isUnlocked && m_isAvailable && m_hasJob);
            m_cancelButton.interactable = m_isInteractionEnabled;
        }

        RefreshVisual(job, recipe, productivity);
    }

    /// <summary>
    /// CreateView 같은 상위 팝업이 열렸을 때 슬롯과 작업 취소 입력을 함께 차단합니다.
    /// </summary>
    public void SetInteractionEnabled(bool isEnabled)
    {
        m_isInteractionEnabled = isEnabled;

        if (m_itemButton != null)
        {
            m_itemButton.interactable =
                m_isInteractionEnabled && m_isUnlocked && m_isAvailable && !m_hasJob;
        }

        if (m_cancelButton != null)
            m_cancelButton.interactable = m_isInteractionEnabled;
    }

    private void RefreshVisual(
        ManufacturingJobRuntimeData job,
        ManufacturingRecipeDefinition recipe,
        int productivity)
    {
        if (!m_isUnlocked)
        {
            SetImage(m_lockedSprite != null ? m_lockedSprite : m_emptySprite);
            SetTexts("잠김", string.Empty, string.Empty);
            return;
        }

        if (!m_isAvailable)
        {
            // usedSprite 미지정 시 현재 빈 슬롯 이미지를 임시 이미지로 사용한다.
            SetImage(m_usedSprite != null ? m_usedSprite : m_emptySprite);
            SetTexts("사용 완료", string.Empty, string.Empty);
            return;
        }

        if (job == null)
        {
            SetImage(m_emptySprite);
            SetTexts("비어 있음", "남은 제작 횟수: -", "남은 일자: -");
            return;
        }

        Sprite icon = recipe != null ? recipe.Icon : null;
        SetImage(icon != null ? icon : m_emptySprite);

        string displayName = recipe != null
            ? recipe.DisplayName
            : job.RecipeId;
        int remainingDays = productivity > 0
            ? Mathf.CeilToInt((float)job.RemainingWork / productivity)
            : 0;

        SetTexts(
            displayName,
            $"남은 제작 횟수: {job.RemainingBatchCount}",
            $"남은 일자: {remainingDays}");
    }

    private void SetImage(Sprite sprite)
    {
        if (m_itemImage == null)
            return;

        m_itemImage.sprite = sprite;
        m_itemImage.enabled = sprite != null;
    }

    private void SetTexts(string itemName, string remainingQuantity, string remainingDays)
    {
        if (m_nameText != null)
            m_nameText.text = itemName;
        if (m_remainingQuantityText != null)
            m_remainingQuantityText.text = remainingQuantity;
        if (m_remainingDaysText != null)
            m_remainingDaysText.text = remainingDays;
    }

    private void HandleSlotClicked()
    {
        if (m_isUnlocked && m_isAvailable && !m_hasJob && m_slotIndex >= 0)
            m_slotClicked?.Invoke(m_slotIndex);
    }

    private void HandleCancelClicked()
    {
        if (m_isUnlocked && m_isAvailable && m_hasJob && m_slotIndex >= 0)
            m_cancelClicked?.Invoke(m_slotIndex);
    }

    private void CacheReferences()
    {
        Button[] buttons = GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null)
                continue;

            if (string.Equals(button.name, "Cancel", StringComparison.OrdinalIgnoreCase))
                m_cancelButton ??= button;
            else
                m_itemButton ??= button;
        }

        if (m_itemImage == null && m_itemButton != null)
            m_itemImage = m_itemButton.GetComponent<Image>();

        if (m_emptySprite == null && m_itemImage != null)
            m_emptySprite = m_itemImage.sprite;

        TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text text = texts[i];
            if (text == null)
                continue;

            if (string.Equals(text.name, "Name", StringComparison.OrdinalIgnoreCase))
                m_nameText ??= text;
            else if (string.Equals(text.name, "State", StringComparison.OrdinalIgnoreCase))
                m_remainingQuantityText ??= text;
            else if (string.Equals(text.name, "Day", StringComparison.OrdinalIgnoreCase))
                m_remainingDaysText ??= text;
        }
    }
}
