using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 의료시설 UI 안의 상황. 대사 표(FacilityStageLine 시트)의 Condition 열 값과 이름이 같습니다.
/// </summary>
/// <remarks>둘 이상 해당하면 위에 있는 값이 우선합니다.</remarks>
public enum MedicalCondition
{
    /// <summary>해금되고 이번 주기에 아직 쓰지 않은 환자 슬롯이 하나도 없음</summary>
    AllSlotsLocked,

    /// <summary>치료할 후보 캐릭터가 없음</summary>
    NoCandidates,

    /// <summary>치료할 후보 캐릭터가 있음</summary>
    HasCandidates
}

/// <summary>
/// 의료시설 UI의 치료 후보, 환자 슬롯, 일괄 치료 흐름을 조정합니다.
/// </summary>
public class MedicalUI : MonoBehaviour, IFacilityStageSource
{
    [Header("Root")]
    [SerializeField] private GameObject m_root;
    [SerializeField] private bool m_hideOnAwake = true;

    [Header("Menu (Optional)")]
    [Tooltip("의료실 UI를 열면 먼저 보이는 진료/업그레이드 선택 화면입니다. 비워 두면 진료 화면을 바로 엽니다.")]
    [SerializeField] private GameObject m_menuRoot;
    [SerializeField] private Button m_treatmentMenuButton;
    [Tooltip("Menu의 진료 버튼을 누르면 나타나는 진료 화면입니다.")]
    [SerializeField] private GameObject m_treatmentRoot;
    [Tooltip("의료실 UI가 열려 있는 동안 항상 표시되며, 누를 때마다 Esc처럼 한 단계 뒤로 돌아갑니다.")]
    [SerializeField] private Button m_backButton;

    [Header("Treatment Candidates")]
    [SerializeField] private TestButton[] m_treatmentCandidateViews;
    [SerializeField] private TextMeshProUGUI m_noTreatmentTargetText;

    [Header("Patient Slots")]
    [SerializeField] private MedicalPatientSlotView[] m_patientSlots;
    [SerializeField] private Button m_healButton;

    [Header("Character Access")]
    [SerializeField] private CharacterManager m_characterManager;

    private readonly List<ShelterMemberRuntimeData> m_treatmentCandidates = new();

    private MedicalManager m_currentManager;
    private CharacterManager m_boundCharacterManager;
    private bool m_isOpening;
    private bool m_isOpen;
    private FacilityUIStage m_stage = FacilityUIStage.None;
    private MedicalCondition m_condition = MedicalCondition.HasCandidates;
    private FacilityDialogueContext m_publishedContext = FacilityDialogueContext.None;

    public bool IsOpen => m_isOpen;
    public FacilityDialogueContext CurrentContext => m_stage == FacilityUIStage.None
        ? FacilityDialogueContext.None
        : new FacilityDialogueContext(m_stage, m_condition.ToString());

    public event Action Closed;
    public event Action<FacilityDialogueContext> ContextChanged;

    private void Awake()
    {
        if (m_root == null)
            m_root = gameObject;

        CacheChildViews();
        BindHealButton();
        BindNavigationButtons();
        m_isOpen = m_root != null && m_root.activeSelf;

        if (m_hideOnAwake && !m_isOpening)
            Close();
    }

    private void Reset()
    {
        m_root = gameObject;
        CacheChildViews();
    }

    private void OnDisable()
    {
        UnbindManager();
        UnbindCharacterManager();

        if (!m_isOpening)
            SetOpenState(false);
    }

    private void OnDestroy()
    {
        if (m_healButton != null)
            m_healButton.onClick.RemoveListener(HandleHealClicked);
        if (m_treatmentMenuButton != null)
            m_treatmentMenuButton.onClick.RemoveListener(HandleTreatmentMenuClicked);
        if (m_backButton != null)
            m_backButton.onClick.RemoveListener(HandleBackClicked);
    }

    /// <summary>
    /// 진료 화면이 열려 있으면 Menu로 돌아가고, 아니면 의료실 UI를 닫습니다.
    /// </summary>
    public bool TryHandleEscape()
    {
        if (!m_isOpen)
            return false;

        if (m_menuRoot != null && !m_menuRoot.activeSelf)
        {
            ShowMenu();
            return true;
        }

        Close();
        return true;
    }

    public void Open(MedicalManager manager)
    {
        UnbindManager();

        m_currentManager = manager;
        if (m_currentManager != null)
            m_currentManager.OnPatientSlotsChanged += Refresh;

        BindCharacterManager(CacheCharacterManager());

        m_isOpening = true;
        SetRootActive(true);
        SetOpenState(true);
        m_isOpening = false;

        CacheChildViews();
        BindHealButton();
        BindNavigationButtons();
        m_currentManager?.RestoreRuntimeState();
        // 상황을 먼저 계산해 두어 Menu 단계 알림이 한 번만 나가도록 Refresh를 앞에 둠
        Refresh();
        ShowMenu();
    }

    /// <summary>
    /// 공용 시설 업그레이드 UI가 열리고 닫힐 때 호출됩니다.
    /// 업그레이드 UI가 열려 있는 동안 Menu를 숨기고, 닫히면 Menu로 돌아갑니다.
    /// </summary>
    public void SetMenuVisible(bool visible)
    {
        if (!m_isOpen)
            return;

        if (visible)
        {
            ShowMenu();
        }
        else
        {
            SetViewState(menu: false, treatment: false);
            SetStage(FacilityUIStage.Upgrade);
        }
    }

    public void Close()
    {
        ClearTreatmentCandidateViews();
        UnbindManager();
        UnbindCharacterManager();
        SetRootActive(false);
        SetOpenState(false);
    }

    public void Refresh()
    {
        CacheChildViews();
        RefreshPatientSlots();
        RefreshTreatmentCandidates();

        if (m_healButton != null)
        {
            m_healButton.interactable = m_currentManager != null
                && m_currentManager.CurrentPatientCount > 0;
        }

        m_condition = EvaluateCondition();
        PublishContext();
    }

    // 상황 판단은 이 함수 한 곳에서만. 우선순위: 슬롯 잠김 > 대상 없음 > 대상 있음
    private MedicalCondition EvaluateCondition()
    {
        if (!HasUsablePatientSlot())
            return MedicalCondition.AllSlotsLocked;

        if (m_treatmentCandidates.Count == 0)
            return MedicalCondition.NoCandidates;

        return MedicalCondition.HasCandidates;
    }

    private bool HasUsablePatientSlot()
    {
        if (m_currentManager == null || m_patientSlots == null)
            return false;

        for (int i = 0; i < m_patientSlots.Length; i++)
        {
            if (m_currentManager.IsPatientSlotUnlocked(i)
                && m_currentManager.IsPatientSlotAvailable(i))
            {
                return true;
            }
        }

        return false;
    }

    private void CacheChildViews()
    {
        if (m_patientSlots == null || m_patientSlots.Length == 0)
            m_patientSlots = GetComponentsInChildren<MedicalPatientSlotView>(true);

        if (m_treatmentCandidateViews == null || m_treatmentCandidateViews.Length == 0)
            m_treatmentCandidateViews = GetComponentsInChildren<TestButton>(true);
    }

    private void BindHealButton()
    {
        if (m_healButton == null)
            return;

        m_healButton.onClick.RemoveListener(HandleHealClicked);
        m_healButton.onClick.AddListener(HandleHealClicked);
    }

    private void BindNavigationButtons()
    {
        if (m_treatmentMenuButton != null)
        {
            m_treatmentMenuButton.onClick.RemoveListener(HandleTreatmentMenuClicked);
            m_treatmentMenuButton.onClick.AddListener(HandleTreatmentMenuClicked);
        }

        if (m_backButton != null)
        {
            m_backButton.onClick.RemoveListener(HandleBackClicked);
            m_backButton.onClick.AddListener(HandleBackClicked);
        }
    }

    private void HandleTreatmentMenuClicked()
    {
        SetViewState(menu: false, treatment: true);
        SetStage(FacilityUIStage.Main);
        Refresh();
    }

    /// <summary>
    /// 돌아가기 버튼은 Esc 키와 같은 경로(UIManager)로 처리해 업그레이드 UI 등 최상위 UI부터 한 단계씩 닫습니다.
    /// </summary>
    private void HandleBackClicked()
    {
        if (!m_isOpen)
            return;

        UIManager uiManager = FindFirstObjectByType<UIManager>();
        if (uiManager != null && uiManager.TryHandleEscape())
            return;

        TryHandleEscape();
    }

    private void ShowMenu()
    {
        // Menu가 없는 기존 구성에서는 진료 화면을 그대로 보여줍니다.
        SetViewState(menu: m_menuRoot != null, treatment: m_menuRoot == null);
        SetStage(m_menuRoot != null ? FacilityUIStage.Menu : FacilityUIStage.Main);
    }

    private void SetViewState(bool menu, bool treatment)
    {
        if (m_menuRoot != null && m_menuRoot.activeSelf != menu)
            m_menuRoot.SetActive(menu);
        if (m_treatmentRoot != null && m_treatmentRoot.activeSelf != treatment)
            m_treatmentRoot.SetActive(treatment);
    }

    private void RefreshPatientSlots()
    {
        if (m_patientSlots == null)
            return;

        for (int i = 0; i < m_patientSlots.Length; i++)
        {
            MedicalPatientSlotView slotView = m_patientSlots[i];
            if (slotView == null)
                continue;

            bool isUnlocked = m_currentManager != null
                && m_currentManager.IsPatientSlotUnlocked(i);
            bool isAvailable = m_currentManager != null
                && m_currentManager.IsPatientSlotAvailable(i);
            slotView.Bind(i, isUnlocked, isAvailable, HandlePatientSlotClicked);
            slotView.ClearPatient();
        }

        if (m_currentManager == null)
            return;

        IReadOnlyList<PatientStatus> statuses = m_currentManager.PatientStatuses;
        for (int i = 0; i < statuses.Count; i++)
        {
            PatientStatus status = statuses[i];
            int slotIndex = status.SlotIndex;
            if (status.Patient == null
                || slotIndex < 0
                || slotIndex >= m_patientSlots.Length
                || m_patientSlots[slotIndex] == null)
            {
                continue;
            }

            MedicalPatientSlotView slotView = m_patientSlots[slotIndex];
            slotView.SetPatient(status.Patient.RuntimeId, status.Patient.DefinitionId);
            slotView.ApplyStatus(status, false);
        }
    }

    private void RefreshTreatmentCandidates()
    {
        m_treatmentCandidates.Clear();
        if (m_currentManager != null)
            m_currentManager.FillPatientCandidates(m_treatmentCandidates);

        int visibleCount = Mathf.Min(
            m_treatmentCandidates.Count,
            m_treatmentCandidateViews?.Length ?? 0);

        for (int i = 0; i < visibleCount; i++)
        {
            ShelterMemberRuntimeData character = m_treatmentCandidates[i];
            bool isInteractable = character != null
                && !m_currentManager.IsPatientAssigned(character.RuntimeId);
            m_treatmentCandidateViews[i]?.Bind(
                character,
                isInteractable,
                HandleTreatmentCandidateClicked);
        }

        if (m_treatmentCandidateViews != null)
        {
            for (int i = visibleCount; i < m_treatmentCandidateViews.Length; i++)
                m_treatmentCandidateViews[i]?.Clear();
        }

        if (m_noTreatmentTargetText != null)
            m_noTreatmentTargetText.gameObject.SetActive(m_treatmentCandidates.Count == 0);
    }

    private void ClearTreatmentCandidateViews()
    {
        m_treatmentCandidates.Clear();
        if (m_treatmentCandidateViews == null)
            return;

        for (int i = 0; i < m_treatmentCandidateViews.Length; i++)
            m_treatmentCandidateViews[i]?.Clear();
    }

    private void HandleTreatmentCandidateClicked(string runtimeId)
    {
        if (m_currentManager == null || string.IsNullOrWhiteSpace(runtimeId))
            return;

        m_currentManager.TryAssignPatient(runtimeId);
        Refresh();
    }

    private void HandlePatientSlotClicked(MedicalPatientSlotView slot)
    {
        if (m_currentManager == null || slot == null || !slot.HasPatient)
            return;

        m_currentManager.TryReleasePatientAtSlot(slot.SlotIndex);
        Refresh();
    }

    private void HandleHealClicked()
    {
        if (m_currentManager == null)
            return;

        m_currentManager.TryHealAssignedPatients();
        Refresh();
    }

    private CharacterManager CacheCharacterManager()
    {
        if (m_characterManager == null)
            m_characterManager = CharacterManager.Instance;

        if (m_characterManager == null)
            m_characterManager = FindFirstObjectByType<CharacterManager>();

        return m_characterManager;
    }

    private void BindCharacterManager(CharacterManager manager)
    {
        if (m_boundCharacterManager == manager)
            return;

        UnbindCharacterManager();
        m_boundCharacterManager = manager;
        if (m_boundCharacterManager == null)
            return;

        m_boundCharacterManager.CharacterChanged += HandleCharacterChanged;
        m_boundCharacterManager.CharactersChanged += HandleCharactersChanged;
    }

    private void UnbindCharacterManager()
    {
        if (m_boundCharacterManager != null)
        {
            m_boundCharacterManager.CharacterChanged -= HandleCharacterChanged;
            m_boundCharacterManager.CharactersChanged -= HandleCharactersChanged;
        }

        m_boundCharacterManager = null;
    }

    private void HandleCharacterChanged(ShelterMemberRuntimeData character)
    {
        if (m_isOpen)
            Refresh();
    }

    private void HandleCharactersChanged()
    {
        if (m_isOpen)
            Refresh();
    }

    private void UnbindManager()
    {
        if (m_currentManager != null)
            m_currentManager.OnPatientSlotsChanged -= Refresh;

        m_currentManager = null;
    }

    private void SetRootActive(bool active)
    {
        if (m_root != null && m_root.activeSelf != active)
            m_root.SetActive(active);
    }

    private void SetOpenState(bool isOpen)
    {
        if (m_isOpen == isOpen)
            return;

        m_isOpen = isOpen;
        if (!m_isOpen)
        {
            SetStage(FacilityUIStage.None);
            Closed?.Invoke();
        }
    }

    private void SetStage(FacilityUIStage stage)
    {
        m_stage = stage;
        PublishContext();
    }

    // Refresh는 클릭마다 불리므로 단계나 상황이 실제로 바뀐 경우에만 알림
    private void PublishContext()
    {
        FacilityDialogueContext context = CurrentContext;
        if (context.Equals(m_publishedContext))
            return;

        m_publishedContext = context;
        ContextChanged?.Invoke(context);
    }
}
