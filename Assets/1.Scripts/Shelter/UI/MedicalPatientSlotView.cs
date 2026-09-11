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


    private int m_slotIndex = -1;
    private bool m_isUnlocked;
    // 임시 빌드 전용: 매니저에서 전달받은 이 슬롯의 1회 사용 가능 상태.
    private bool m_isAvailable;
    private string m_patientRuntimeId;           // null/공백 = 비점유
    private string m_patientDefinitionId;
    private Action<MedicalPatientSlotView> m_clicked;

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
    /// <param name="onClicked">슬롯 또는 취소 버튼 클릭 시 호출할 콜백</param>
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
        UpdateVisual();
        UpdateButtonStates();
        ClearStatusDisplay();
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
            m_injuryStateText.text = string.Empty;

        if (m_daysText != null)
            m_daysText.text = string.Empty;
    }

    /// <summary>
    /// 게이지, 부상 상태, 남은 일수 표시를 갱신
    /// </summary>
    /// <param name="status">표시할 환자 상태 투영본</param>
    /// <param name="showGauge">게이지 바를 표시할지 여부</param>
    public void ApplyStatus(PatientStatus status, bool showGauge)
    {
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
            m_injuryStateText.text = string.Format(m_stateFormat, CharacterInjuryStateText.ToWord(status.InjuryState));

        if (m_daysText != null)
            m_daysText.text = string.Format(m_daysFormat, status.RemainingDays);
    }

    private void HandleClick()
    {
        // 슬롯 버튼(빈칸→배치)과 취소 버튼(점유→해제) 모두 여기로 통지.
        // 빈칸/점유 분기는 컨트롤러가 PatientId로 판단.
        if (m_isUnlocked && m_isAvailable)
            m_clicked?.Invoke(this);
    }

    // 빈 슬롯: 슬롯 버튼 활성(배치), 취소 버튼 숨김.
    // 점유 슬롯: 슬롯 버튼 비활성, 취소 버튼 표시(배치취소).
    private void UpdateButtonStates()
    {
        if (m_button != null)
            m_button.interactable = m_isUnlocked && m_isAvailable && !HasPatient;

        if (m_cancelButton != null)
            m_cancelButton.gameObject.SetActive(m_isUnlocked && m_isAvailable && HasPatient);
    }

    private void UpdateVisual()
    {
        if (m_slotImage == null)
            return;

        if (!m_isUnlocked)
        {
            m_slotImage.sprite = m_lockedSprite;
            return;
        }

        if (!m_isAvailable)
        {
            // usedSprite 미지정 시에도 임시 빌드 상태를 구분할 수 있도록 잠금 이미지를 대신 사용한다.
            m_slotImage.sprite = m_usedSprite != null ? m_usedSprite : m_lockedSprite;
            ClearStatusDisplay();
            return;
        }

        if (HasPatient && m_characterCatalog != null)
        {
            Sprite portrait = m_characterCatalog.GetPortrait(m_patientDefinitionId);
            m_slotImage.sprite = portrait != null ? portrait : m_unlockedSprite;
        }
        else
        {
            m_slotImage.sprite = m_unlockedSprite;
        }
    }

    private void CacheReferences()
    {
        if (m_button == null)
            m_button = GetComponent<Button>();

        if (m_slotImage == null)
            m_slotImage = GetComponent<Image>();
    }
}
