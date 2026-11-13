using System;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// 의료 UI의 단일 환자 슬롯 표시와 클릭 상태를 관리
/// </summary>
public class MedicalPatientSlotView : MonoBehaviour
{
    [SerializeField] private Button m_button;
    [SerializeField] private Image m_slotImage;
    [SerializeField] private Sprite m_unlockedSprite;
    [SerializeField] private Sprite m_lockedSprite;
    [SerializeField] private Sprite m_usedSprite;
    [SerializeField] private Button m_cancelButton;   // 점유 시 표시되는 배치취소 버튼 (슬롯과 형제, 배경 아래)
    [FormerlySerializedAs("m_npccatalog")]
    [SerializeField] private CharacterPortraitCatalog m_characterCatalog;
    [Tooltip("선택: 캐릭터별 부상 이미지. 환자의 현재 부상 상태 이미지가 있으면 기본 초상화 대신 표시합니다.")]
    [SerializeField] private CharacterInjuryPortraitCatalog m_characterInjuryCatalog;

    [Header("Status Display (선택 — 없으면 무시)")]
    [SerializeField] private GameObject m_gaugeRoot;     // 레벨별 게이지 바 표시/숨김 대상
    [SerializeField] private Image m_gaugeFill;          // 부상게이지 (Image.fillAmount 방식, 0~1)
    [SerializeField] private Slider m_gaugeSlider;       // 부상게이지 (Slider 방식, 표시 전용·드래그 불가. Min=0/Max=1 권장)
    [SerializeField] private Image m_injuryIcon;
    [SerializeField] private CharacterInjuryIconCatalog m_injuryCatalog;
    [SerializeField] private TextMeshProUGUI m_daysText;            // 남은 일수
    [SerializeField] private TextMeshProUGUI m_nameText;            // 이름
    [SerializeField] private TextMeshProUGUI m_injuryStateText;     // 부상상태

    //ToDo : 추후 세이브전용 ID 필요
    [SerializeField] private string m_slotId;   // 세이브/로드 전용 고정 식별자 (런타임 로직에서는 미사용)
    [SerializeField] private string m_nameFormat = "이름 : {0}";
    [SerializeField] private string m_stateFormat = "상태 : {0}";
    [SerializeField] private string m_daysFormat = "{0}일";

    [Header("Empty Slot State Text")]
    [Tooltip("시설 레벨이 부족해 해금되지 않은 슬롯")]
    [SerializeField] private string m_lockedStateText = "시설 업그레이드시 해제";
    [Tooltip("해금됐지만 치료 완료로 사용할 수 없게 된 슬롯")]
    [SerializeField] private string m_unavailableStateText = "치료 완료";
    [Tooltip("환자를 배치할 수 있는 빈 슬롯")]
    [SerializeField] private string m_availableStateText = "사용 가능";

    [Header("Treated Image (치료 완료로 잠긴 슬롯)")]
    [Tooltip("프리팹에서 비활성화해 두는 이미지. 치료 완료 시 마지막 배치 환자의 초상화를 넣고 활성화합니다.")]
    [SerializeField] private Image m_treatedImage;

    private int m_slotIndex = -1;
    private bool m_isUnlocked;
    // 임시 빌드 전용: 매니저에서 전달받은 이 슬롯의 1회 사용 가능 상태.
    private bool m_isAvailable;
    private string m_patientRuntimeId;           // null/공백 = 비점유
    private string m_patientDefinitionId;
    private CharacterInjuryState m_patientInjuryState;
    // 이 슬롯에 마지막으로 배치됐던 환자 정의 ID. 슬롯이 다시 사용 가능해질 때만 지운다.
    private string m_lastPatientDefinitionId;
    private Action<MedicalPatientSlotView> m_clicked;
    // 부상상태 텍스트의 프리팹 기본색. 빈 슬롯 문구나 색 미지정 상태에서 되돌릴 때 사용한다.
    private Color m_defaultInjuryStateTextColor;
    private bool m_hasDefaultInjuryStateTextColor;

    /// <summary>세이브/로드 시에만 사용하는 슬롯 식별자</summary>
    public string SlotId => m_slotId;

    /// <summary>현재 UI에 바인딩된 슬롯 인덱스</summary>
    public int SlotIndex => m_slotIndex;

    /// <summary>임시 빌드 전용 슬롯 사용 가능 상태</summary>
    public bool IsAvailable => m_isAvailable;

    /// <summary>현재 슬롯을 점유 중인 환자 런타임 ID</summary>
    public string PatientRuntimeId => m_patientRuntimeId;

    /// <summary>슬롯에 환자가 배치되어 있는지 여부</summary>
    public bool HasPatient => !string.IsNullOrWhiteSpace(m_patientRuntimeId);

    private void Awake()
    {
        CacheReferences();
    }

    private void Reset()
    {
        CacheReferences();
    }

    /// <summary>
    /// 슬롯의 잠금/사용 가능 상태와 클릭 콜백을 설정. 환자 점유 상태는 변경하지 않음
    /// </summary>
    /// <param name="slotIndex">UI 배열과 매니저 상태를 연결하는 슬롯 인덱스</param>
    /// <param name="isUnlocked">슬롯 해금 여부</param>
    /// <param name="isAvailable">임시 빌드에서 아직 사용 가능한 슬롯인지 여부</param>
    /// <param name="onClicked">배치된 환자 이미지를 클릭했을 때 호출할 취소 콜백</param>
    public void Bind(
        int slotIndex,
        bool isUnlocked,
        bool isAvailable,
        Action<MedicalPatientSlotView> onClicked)
    {
        CacheReferences();
        m_slotIndex = slotIndex;
        m_isUnlocked = isUnlocked;
        m_isAvailable = isAvailable;
        m_clicked = onClicked;

        // 사용 가능한 슬롯이면 치료 완료 기억이 필요 없다. 배치 중 환자는 이어지는 SetPatient에서 다시 기억한다.
        if (isAvailable)
            m_lastPatientDefinitionId = null;

        UpdateVisual();

        if (m_button != null)
        {
            m_button.onClick.RemoveAllListeners();
            m_button.onClick.AddListener(HandleClick);
        }

        if (m_cancelButton != null)
        {
            m_cancelButton.onClick.RemoveAllListeners();
            m_cancelButton.onClick.AddListener(HandleClick);
        }

        if (m_gaugeSlider != null)
            m_gaugeSlider.interactable = false;   // 표시 전용 — 사용자 드래그 방지

        UpdateButtonStates();
    }

    /// <summary>
    /// 이 슬롯에 환자 런타임 ID와 정의 ID를 배치하고 표시를 갱신
    /// </summary>
    public void SetPatient(string runtimeId, string definitionId)
    {
        m_patientRuntimeId = runtimeId;
        m_patientDefinitionId = definitionId;
        if (!string.IsNullOrWhiteSpace(definitionId))
            m_lastPatientDefinitionId = definitionId;
        UpdateVisual();
        UpdateButtonStates();
    }

    /// <summary>
    /// 환자 배치를 해제하고 슬롯 표시를 초기화
    /// </summary>
    public void ClearPatient()
    {
        m_patientRuntimeId = null;
        m_patientDefinitionId = null;
        m_patientInjuryState = CharacterInjuryState.Normal;
        UpdateVisual();
        UpdateButtonStates();
        ClearStatusDisplay();
        ApplyEmptySlotStateText();
    }

    // 환자가 없는 슬롯은 잠김/사용 불가/사용 가능 상태를 부상상태 텍스트 자리에 표시한다.
    private void ApplyEmptySlotStateText()
    {
        if (m_injuryStateText == null || HasPatient)
            return;

        ApplyInjuryStateTextColor(null);

        if (!m_isUnlocked)
            m_injuryStateText.text = m_lockedStateText;
        else if (!m_isAvailable)
            m_injuryStateText.text = m_unavailableStateText;
        else
            m_injuryStateText.text = m_availableStateText;
    }

    // 배치 이전 상태로 표시 초기화 (취소/완치 시) — 게이지·아이콘·텍스트 비움.
    private void ClearStatusDisplay()
    {
        if (m_gaugeRoot != null)
            m_gaugeRoot.SetActive(false);

        if (m_gaugeFill != null)
            m_gaugeFill.fillAmount = 0f;

        if (m_gaugeSlider != null)
            m_gaugeSlider.normalizedValue = 0f;

        if (m_injuryIcon != null)
        {
            m_injuryIcon.sprite = null;
            m_injuryIcon.enabled = false;
            m_injuryIcon.gameObject.SetActive(false);
        }

        if (m_nameText != null)
            m_nameText.text = string.Empty;

        if (m_injuryStateText != null)
        {
            m_injuryStateText.text = string.Empty;
            ApplyInjuryStateTextColor(null);
        }

        if (m_daysText != null)
            m_daysText.text = string.Empty;
    }

    // 부상 상태별 카탈로그 색을 적용한다. state가 null이거나 색이 지정되지 않았으면 프리팹 기본색으로 되돌린다.
    private void ApplyInjuryStateTextColor(CharacterInjuryState? state)
    {
        if (m_injuryStateText == null)
            return;

        CacheDefaultInjuryStateTextColor();

        Color color = m_defaultInjuryStateTextColor;
        if (state.HasValue && m_characterInjuryCatalog != null
            && m_characterInjuryCatalog.TryGetTextColor(state.Value, out Color stateColor))
            color = stateColor;

        if (m_injuryStateText.color != color)
            m_injuryStateText.color = color;
    }

    private void CacheDefaultInjuryStateTextColor()
    {
        if (m_hasDefaultInjuryStateTextColor || m_injuryStateText == null)
            return;

        m_defaultInjuryStateTextColor = m_injuryStateText.color;
        m_hasDefaultInjuryStateTextColor = true;
    }

    /// <summary>
    /// 게이지, 부상 상태, 남은 일수 표시를 갱신
    /// </summary>
    /// <param name="status">표시할 환자 상태 투영본</param>
    /// <param name="showGauge">게이지 바를 표시할지 여부</param>
    public void ApplyStatus(PatientStatus status, bool showGauge)
    {
        // 부상 상태별 초상화를 쓰도록 상태를 기억하고 슬롯 이미지를 다시 고른다.
        if (m_patientInjuryState != status.InjuryState)
        {
            m_patientInjuryState = status.InjuryState;
            UpdateVisual();
        }

        if (m_gaugeRoot != null)
            m_gaugeRoot.SetActive(showGauge);

        if (m_gaugeFill != null)
            m_gaugeFill.fillAmount = status.GaugeNormalized;

        if (m_gaugeSlider != null)
            m_gaugeSlider.normalizedValue = status.GaugeNormalized;   // min/max 무관하게 0~1 비율로 채움

        if (m_injuryIcon != null)
        {
            Sprite icon = m_injuryCatalog != null ? m_injuryCatalog.GetIcon(status.InjuryState) : null;
            m_injuryIcon.sprite = icon;
            m_injuryIcon.enabled = icon != null;
            m_injuryIcon.gameObject.SetActive(icon != null);
        }

        if (m_nameText != null)
            m_nameText.text = string.Format(m_nameFormat, status.DisplayName);

        if (m_injuryStateText != null)
        {
            m_injuryStateText.text = string.Format(m_stateFormat, CharacterInjuryStateText.ToWord(status.InjuryState));
            ApplyInjuryStateTextColor(status.InjuryState);
        }

        if (m_daysText != null)
            m_daysText.text = string.Format(m_daysFormat, status.RemainingDays);
    }

    private void HandleClick()
    {
        if (m_isUnlocked && m_isAvailable && HasPatient)
            m_clicked?.Invoke(this);
    }

    // 빈 슬롯은 직접 선택하지 않는다. 점유된 캐릭터 이미지 버튼만 배치 취소에 사용한다.
    private void UpdateButtonStates()
    {
        bool canCancel = m_isUnlocked && m_isAvailable && HasPatient;
        bool hasSeparateCancelButton = m_cancelButton != null && m_cancelButton != m_button;

        if (m_button != null)
            m_button.interactable = canCancel && !hasSeparateCancelButton;

        if (hasSeparateCancelButton)
        {
            m_cancelButton.gameObject.SetActive(canCancel);
            m_cancelButton.interactable = canCancel;
        }
    }

    // 치료 완료로 잠긴 슬롯이면 마지막 배치 환자 초상화를 치료 완료 이미지에 넣고 켠다.
    private void UpdateTreatedImage()
    {
        if (m_treatedImage == null)
            return;

        Sprite portrait = m_isUnlocked && !m_isAvailable
            ? ResolvePortrait(m_lastPatientDefinitionId, CharacterInjuryState.Normal)
            : null;

        m_treatedImage.sprite = portrait;
        if (m_treatedImage.gameObject.activeSelf != (portrait != null))
            m_treatedImage.gameObject.SetActive(portrait != null);
    }

    // 캐릭터 정의 ID와 부상 상태로 초상화를 찾는다. 부상 이미지 → 기본 초상화 순.
    private Sprite ResolvePortrait(string definitionId, CharacterInjuryState state)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            return null;

        Sprite portrait = m_characterInjuryCatalog != null
            ? m_characterInjuryCatalog.GetIcon(definitionId, state)
            : null;
        if (portrait == null && m_characterCatalog != null)
            portrait = m_characterCatalog.GetPortrait(definitionId);

        return portrait;
    }

    private void UpdateVisual()
    {
        UpdateTreatedImage();

        if (m_slotImage == null)
            return;

        if (!m_isUnlocked)
        {
            m_slotImage.sprite = m_lockedSprite;
            ApplyEmptySlotStateText();
            return;
        }

        if (!m_isAvailable)
        {
            // usedSprite 미지정 시에도 임시 빌드 상태를 구분할 수 있도록 잠금 이미지를 대신 사용한다.
            m_slotImage.sprite = m_usedSprite != null ? m_usedSprite : m_lockedSprite;
            ClearStatusDisplay();
            ApplyEmptySlotStateText();
            return;
        }

        if (HasPatient)
        {
            Sprite portrait = ResolvePortrait(m_patientDefinitionId, m_patientInjuryState);
            m_slotImage.sprite = portrait != null ? portrait : m_unlockedSprite;
        }
        else
        {
            m_slotImage.sprite = m_unlockedSprite;
        }

        ApplyEmptySlotStateText();
    }

    private void CacheReferences()
    {
        if (m_button == null)
            m_button = GetComponent<Button>();

        if (m_slotImage == null)
            m_slotImage = GetComponent<Image>();

        // Reset(에디터)에서는 런타임 기본색을 고정하지 않는다.
        if (Application.isPlaying)
            CacheDefaultInjuryStateTextColor();
    }
}
