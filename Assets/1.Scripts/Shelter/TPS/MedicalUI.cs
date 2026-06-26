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
    [SerializeField] private NpcPortraitCatalog m_portraitCatalog;
    [SerializeField] private TestButton[] m_testButtons;

    private readonly List<NPCRuntimeData> m_treatmentCandidates = new List<NPCRuntimeData>();

    private MedicalManager m_currentManager;
    private CharacterManager m_boundCharacterManager;
    private bool m_isOpening;
    private bool m_isOpen;

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
            m_currentManager.OnPatientSlotsChanged += Refresh;

        BindCharacterManager(CacheCharacterManager());

        m_isOpening = true;
        SetRootActive(true);
        SetOpenState(true);
        m_isOpening = false;

        Refresh();
    }

    public void Close()
    {
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
    }

    private void CacheChildViews()
    {
        if (m_patientSlots == null || m_patientSlots.Length == 0)
            m_patientSlots = GetComponentsInChildren<MedicalPatientSlotView>(true);

        if (m_testButtons == null || m_testButtons.Length == 0)
            m_testButtons = GetComponentsInChildren<TestButton>(true);
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
            slotView.Bind(i, isUnlocked, HandlePatientSlotClicked);
        }
    }

    private void RefreshTreatmentCandidates()
    {
        ClearTreatmentCandidateButtons();

        if (m_currentManager == null || m_boundCharacterManager == null || m_testButtons == null)
            return;

        if (m_currentManager.CurrentPatientCount >= m_currentManager.PatientCapacity)
            return;

        m_boundCharacterManager.FillTreatmentCandidates(m_treatmentCandidates);

        int buttonCount = Mathf.Min(m_testButtons.Length, m_treatmentCandidates.Count);
        for (int i = 0; i < buttonCount; i++)
        {
            TestButton button = m_testButtons[i];
            if (button == null)
                continue;

            NPCRuntimeData character = m_treatmentCandidates[i];
            button.Bind(character, GetPortrait(character), HandleTreatmentCandidateClicked);
        }
    }

    private void ClearTreatmentCandidateButtons()
    {
        if (m_testButtons == null)
            return;

        for (int i = 0; i < m_testButtons.Length; i++)
        {
            if (m_testButtons[i] != null)
                m_testButtons[i].Clear();
        }

        m_treatmentCandidates.Clear();
    }

    private void HandlePatientSlotClicked(int slotIndex)
    {
        RefreshTreatmentCandidates();
    }

    private void HandleTreatmentCandidateClicked(string runtimeId)
    {
        if (m_currentManager == null)
            return;

        if (m_currentManager.TryAssignPatient(runtimeId))
            Refresh();
        else
            RefreshTreatmentCandidates();
    }

    private Sprite GetPortrait(NPCRuntimeData character)
    {
        if (character == null || m_portraitCatalog == null)
            return null;

        return m_portraitCatalog.GetPortrait(character.DefinitionId);
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
            Closed?.Invoke();
    }
}
