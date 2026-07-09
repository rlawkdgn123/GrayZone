using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class MedicalUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject m_root;
    [SerializeField] private bool m_hideOnAwake = true;

    [Header("Patient Slots")]
    [SerializeField] private MedicalPatientSlotView[] m_patientSlots;

    [Header("Treatment Candidates")]
    [SerializeField] private CharacterManager m_characterManager;
    [SerializeField] private NPCListScript m_npcListScript;

    private readonly List<NPCRuntimeData> m_treatmentCandidates = new List<NPCRuntimeData>();

    private MedicalManager m_currentManager;
    private CharacterManager m_boundCharacterManager;
    private MedicalPatientSlotView m_pendingSlot;   // 배치 대상으로 클릭해 둔 빈 슬롯
    private bool m_isOpening;
    private bool m_isOpen;
    private bool m_isTreatmentCandidateListOpen;
    private bool m_helperMode;   // 후보 목록 모드: false=환자, true=헬퍼

    public bool IsOpen => m_isOpen;

    public event Action Closed;

    private void Awake()
    {
        if (m_root == null)
            m_root = gameObject;

        CacheChildViews();
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


    private void Update()
    {
        if (Keyboard.current != null &&
                Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Close();
        }
    }


    public void Open(MedicalManager manager)
    {
        UnbindManager();

        m_currentManager = manager;
        if (m_currentManager != null)
        {
            m_currentManager.OnPatientSlotsChanged += Refresh;
            m_currentManager.OnPatientHealed += HandlePatientHealed;
        }

        BindCharacterManager(CacheCharacterManager());

        m_isOpening = true;
        SetRootActive(true);
        SetOpenState(true);
        m_isOpening = false;

        CacheChildViews();
        HideTreatmentCandidateList();
        SyncSlotsFromManager();
        Refresh();
    }

    public void Close()
    {
        HideTreatmentCandidateList();
        UnbindManager();
        UnbindCharacterManager();
        SetRootActive(false);
        SetOpenState(false);
    }

    public void Refresh()
    {
        CacheChildViews();
        RefreshPatientSlots();
        ApplyPatientStatusesToSlots();

        if (m_isTreatmentCandidateListOpen)
            RefreshTreatmentCandidates();
        else
            ClearTreatmentCandidateList();
    }

    private void CacheChildViews()
    {
        if (m_patientSlots == null || m_patientSlots.Length == 0)
            m_patientSlots = GetComponentsInChildren<MedicalPatientSlotView>(true);

        if (m_npcListScript == null)
            m_npcListScript = GetComponentInChildren<NPCListScript>(true);
    }

    private void RefreshPatientSlots()
    {
        if (m_patientSlots == null)
            return;

        int unlockedSlotCount = m_currentManager != null
            ? m_currentManager.PatientCapacity
            : 0;

        for (int i = 0; i < m_patientSlots.Length; i++)
        {
            MedicalPatientSlotView slotView = m_patientSlots[i];
            if (slotView == null)
                continue;

            bool isUnlocked = i < unlockedSlotCount;
            slotView.Bind(isUnlocked, HandleSlotClicked);
        }
    }

    // 각 점유 슬롯에 게이지/부상상태/남은일수 반영. 게이지 바는 레벨2+(UpgradeLevel>=1)에서만 표시.
    private void ApplyPatientStatusesToSlots()
    {
        if (m_patientSlots == null || m_currentManager == null)
            return;

        IReadOnlyList<PatientStatus> statuses = m_currentManager.PatientStatuses;
        //Todo : 1로 바꾸기
        bool showGauge = m_currentManager.UpgradeLevel >= 0;

        for (int i = 0; i < m_patientSlots.Length; i++)
        {
            MedicalPatientSlotView slot = m_patientSlots[i];
            if (slot == null || !slot.HasPatient)
                continue;

            for (int j = 0; j < statuses.Count; j++)
            {
                PatientStatus status = statuses[j];
                if (status.Patient != null && status.Patient.DefinitionId == slot.PatientId)
                {
                    slot.ApplyStatus(status, showGauge);
                    break;
                }
            }
        }
    }

    // 헬퍼 배치 진입점 — 환자 슬롯 클릭과 별개의 버튼에서 호출.
    public void BeginHelperAssignment()
    {
        if (m_currentManager == null)
            return;

        m_helperMode = true;
        m_pendingSlot = null;
        ShowTreatmentCandidateList();
        RefreshTreatmentCandidates();
    }

    // UI를 열 때 1회: 현재 치료 중인 환자를 슬롯에 초기 배치한다.
    // 이후의 위치는 배치/취소/완치 이벤트로 증분 유지되며, Refresh는 점유를 재배치하지 않는다.
    private void SyncSlotsFromManager()
    {
        if (m_patientSlots == null)
            return;

        for (int i = 0; i < m_patientSlots.Length; i++)
            if (m_patientSlots[i] != null)
                m_patientSlots[i].ClearPatient();

        if (m_currentManager == null)
            return;

        // 환자 수 <= 수용량이므로 앞 슬롯부터 채우면 모두 해금 슬롯 범위 안에 들어간다.
        IReadOnlyList<PatientStatus> statuses = m_currentManager.PatientStatuses;
        for (int i = 0; i < statuses.Count && i < m_patientSlots.Length; i++)
        {
            NPCRuntimeData patient = statuses[i].Patient;
            if (patient != null && m_patientSlots[i] != null)
                m_patientSlots[i].SetPatient(patient.DefinitionId);
        }
    }

    private void HandlePatientHealed(NPCRuntimeData patient)
    {
        if (patient != null)
            ClearSlotByPatientId(patient.DefinitionId);
    }

    private void ClearSlotByPatientId(string definitionId)
    {
        if (m_patientSlots == null || string.IsNullOrWhiteSpace(definitionId))
            return;

        for (int i = 0; i < m_patientSlots.Length; i++)
        {
            MedicalPatientSlotView slot = m_patientSlots[i];
            if (slot != null && slot.HasPatient && slot.PatientId == definitionId)
            {
                slot.ClearPatient();
                return;
            }
        }
    }

    private void RefreshTreatmentCandidates()
    {
        ClearTreatmentCandidateList();

        if (m_currentManager == null || m_npcListScript == null)
            return;

        if (m_helperMode)
        {
            if (m_currentManager.CurrentHelperCount >= m_currentManager.HelperCapacity)
                return;

            m_currentManager.FillHelperCandidates(m_treatmentCandidates);
        }
        else
        {
            if (m_currentManager.CurrentPatientCount >= m_currentManager.PatientCapacity)
                return;

            m_currentManager.FillPatientCandidates(m_treatmentCandidates);
        }

        m_npcListScript.Bind(m_treatmentCandidates, HandleTreatmentCandidateClicked);
    }

    private void ClearTreatmentCandidateList()
    {
        if (m_npcListScript != null)
            m_npcListScript.Clear();

        m_treatmentCandidates.Clear();
    }

    private void HandleSlotClicked(MedicalPatientSlotView slot)
    {
        if (slot == null || m_currentManager == null)
            return;

        if (slot.HasPatient)
        {
            // 점유 슬롯 → 배치 취소
            if (m_currentManager.TryReleasePatient(slot.PatientId))
            {
                slot.ClearPatient();
                HideTreatmentCandidateList();
            }
            return;
        }

        // 빈 슬롯 → 환자 후보 목록 열기 (이 슬롯을 배치 대상으로 기억)
        m_helperMode = false;
        m_pendingSlot = slot;
        ShowTreatmentCandidateList();
        RefreshTreatmentCandidates();
    }

    private void HandleTreatmentCandidateClicked(string definitionId)
    {
        if (m_currentManager == null)
            return;

        bool wasCandidateListOpen = m_isTreatmentCandidateListOpen;
        m_isTreatmentCandidateListOpen = false;

        bool assigned = m_helperMode
            ? m_currentManager.TryAssignHelper(definitionId)
            : m_currentManager.TryAssignPatient(definitionId);

        if (assigned)
        {
            // 환자 모드에서만 클릭해 둔 빈 슬롯에 고정 배치. 헬퍼 전용 슬롯 UI는 에디터 배선 필요.
            if (!m_helperMode && m_pendingSlot != null)
                m_pendingSlot.SetPatient(definitionId);
            m_pendingSlot = null;

            HideTreatmentCandidateList();
            RefreshPatientSlots();
            ApplyPatientStatusesToSlots();
        }
        else
        {
            m_isTreatmentCandidateListOpen = wasCandidateListOpen;

            if (m_isTreatmentCandidateListOpen)
                RefreshTreatmentCandidates();
        }
    }

    private void ShowTreatmentCandidateList()
    {
        m_isTreatmentCandidateListOpen = true;

        if (m_npcListScript != null)
            m_npcListScript.gameObject.SetActive(true);
    }

    private void HideTreatmentCandidateList()
    {
        m_isTreatmentCandidateListOpen = false;
        m_helperMode = false;
        m_pendingSlot = null;
        ClearTreatmentCandidateList();

        if (m_npcListScript != null)
            m_npcListScript.gameObject.SetActive(false);
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
        m_boundCharacterManager.RosterChanged += HandleRosterChanged;
    }

    private void UnbindCharacterManager()
    {
        if (m_boundCharacterManager != null)
        {
            m_boundCharacterManager.CharacterChanged -= HandleCharacterChanged;
            m_boundCharacterManager.RosterChanged -= HandleRosterChanged;
        }

        m_boundCharacterManager = null;
    }

    private void HandleCharacterChanged(NPCRuntimeData character)
    {
        if (m_isOpen)
            Refresh();
    }

    private void HandleRosterChanged()
    {
        if (m_isOpen)
            Refresh();
    }

    private void UnbindManager()
    {
        if (m_currentManager != null)
        {
            m_currentManager.OnPatientSlotsChanged -= Refresh;
            m_currentManager.OnPatientHealed -= HandlePatientHealed;
        }

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
            Closed?.Invoke();
    }
}
