using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MedicalPatientSlotView : MonoBehaviour
{
    [SerializeField] private Button m_button;
    [SerializeField] private Image m_slotImage;
    [SerializeField] private Sprite m_unlockedSprite;
    [SerializeField] private Sprite m_lockedSprite;
    [SerializeField] private Button m_cancelButton;   // 점유 시 표시되는 배치취소 버튼 (슬롯과 형제, 배경 아래)
    [SerializeField] private NpcPortraitCatalog m_npccatalog;

    [Header("Status Display (선택 — 없으면 무시)")]
    [SerializeField] private GameObject m_gaugeRoot;     // 레벨별 게이지 바 표시/숨김 대상
    [SerializeField] private Image m_gaugeFill;          // 부상게이지 (Image.fillAmount 방식, 0~1)
    [SerializeField] private Slider m_gaugeSlider;       // 부상게이지 (Slider 방식, 표시 전용·드래그 불가. Min=0/Max=1 권장)
    [SerializeField] private Image m_injuryIcon;
    [SerializeField] private NpcInjuryIconCatalog m_injuryCatalog;
    [SerializeField] private TextMeshProUGUI m_daysText;            // 남은 일수
    [SerializeField] private TextMeshProUGUI m_nameText;            // 이름
    [SerializeField] private TextMeshProUGUI m_injuryStateText;     // 부상상태

    //ToDo : 추후 세이브전용 ID 필요
    [SerializeField] private string m_slotId;   // 세이브/로드 전용 고정 식별자 (런타임 로직에서는 미사용)
    [SerializeField] private string m_nameFormat = "이름 : {0}";
    [SerializeField] private string m_stateFormat = "상태 : {0}";
    [SerializeField] private string m_daysFormat = "{0}일";


    private bool m_isUnlocked;
    private string m_patientId;                  // null/공백 = 비점유
    private Action<MedicalPatientSlotView> m_clicked;

    // 세이브/로드 시에만 사용하는 슬롯 식별자
    public string SlotId => m_slotId;

    // 점유 환자 식별 (런타임 분기/표시/매칭용)
    public string PatientId => m_patientId;
    public bool HasPatient => !string.IsNullOrWhiteSpace(m_patientId);

    private void Awake()
    {
        CacheReferences();
    }

    private void Reset()
    {
        CacheReferences();
    }

    // 잠금/해금 상태 + 클릭 콜백만 설정한다. 환자 배치는 SetPatient로 별도 처리(Refresh가 점유를 건드리지 않음).
    public void Bind(bool isUnlocked, Action<MedicalPatientSlotView> onClicked)
    {
        CacheReferences();
        m_isUnlocked = isUnlocked;
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

    // 환자 배치 — 이 슬롯에 id를 박아두고 유지(시각적 고정).
    public void SetPatient(string definitionId)
    {
        m_patientId = definitionId;
        UpdateVisual();
        UpdateButtonStates();
    }

    // 환자 해제(완치/취소) — 슬롯을 비운다.
    public void ClearPatient()
    {
        m_patientId = null;
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

    // 게이지/부상상태/남은 일수 표시 갱신. 참조가 없는 항목은 무시(null-safe).
    // showGauge=false면 게이지 바를 숨긴다(레벨1 등).
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
            m_injuryStateText.text = string.Format(m_stateFormat, NpcInjuryStateText.ToWord(status.InjuryState));

        if (m_daysText != null)
            m_daysText.text = string.Format(m_daysFormat, status.RemainingDays);
    }

    private void HandleClick()
    {
        // 슬롯 버튼(빈칸→배치)과 취소 버튼(점유→해제) 모두 여기로 통지.
        // 빈칸/점유 분기는 컨트롤러가 PatientId로 판단.
        m_clicked?.Invoke(this);
    }

    // 빈 슬롯: 슬롯 버튼 활성(배치), 취소 버튼 숨김.
    // 점유 슬롯: 슬롯 버튼 비활성, 취소 버튼 표시(배치취소).
    private void UpdateButtonStates()
    {
        if (m_button != null)
            m_button.interactable = m_isUnlocked && !HasPatient;

        if (m_cancelButton != null)
            m_cancelButton.gameObject.SetActive(m_isUnlocked && HasPatient);
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

        if (HasPatient && m_npccatalog != null)
        {
            Sprite portrait = m_npccatalog.GetPortrait(m_patientId);
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
