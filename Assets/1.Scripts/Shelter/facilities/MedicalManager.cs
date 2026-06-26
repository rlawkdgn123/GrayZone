using UnityEngine;
using System.Collections.Generic;

public class MedicalManager : MonoBehaviour, IFacilityUpgradeable
{
    private const int BasePatientCapacity = 2;
    private const int FirstUpgradePatientCapacity = 4;
    private const int FullPatientCapacity = 9;
    private const int MaxPatientCapacityLevel = 2;

    [Header("Facility")]
    [SerializeField] private FacilityDefinition definition;
    [SerializeField] private string fallbackFacilityId = "medical_center";
    [SerializeField] private string roomId = "medical_room";

    [Header("Character Access")]
    [SerializeField] private CharacterManager characterManager;

    [Header("Staff")]
    [SerializeField] private int maxStaff = 1;

    [Header("Recovery")]
    //ToDo: Configure recovery time by player injury state.
    [SerializeField] private int healDays = 5;
    [SerializeField] private StaffHealBonus[] staffHealBonuses = new StaffHealBonus[]
    {
        new StaffHealBonus { type = NPCType.Tanker,   daysReduction = 1 },
        new StaffHealBonus { type = NPCType.Healer,   daysReduction = 2 },
        new StaffHealBonus { type = NPCType.Dealer,   daysReduction = 1 }
    };

    private readonly List<MedicalTreatment> patientTreatments = new List<MedicalTreatment>(FullPatientCapacity);
    private readonly List<PatientStatus> patientStatuses = new List<PatientStatus>(FullPatientCapacity);
    private readonly List<NPCRuntimeData> assignedStaff = new List<NPCRuntimeData>();
    private StaffAssignment staffSlots;
    private int patientCapacityLevel = 0;

    public event System.Action<NPCRuntimeData> OnStaffAssigned;
    public event System.Action<NPCRuntimeData> OnStaffReleased;
    public event System.Action<NPCRuntimeData> OnPatientHealed;
    public event System.Action OnPatientSlotsChanged;

    public IReadOnlyList<PatientStatus> PatientStatuses
    {
        get
        {
            RefreshPatientStatuses();
            return patientStatuses;
        }
    }
    public int CurrentPatientCount => patientTreatments.Count;
    public int MaxPatientCount => PatientCapacity;
    public int PatientCapacity => GetPatientCapacity();
    public int MaxPatientCapacity => FullPatientCapacity;
    public int UnlockedPatientSlotCount => PatientCapacity;
    public int LockedPatientSlotCount => FullPatientCapacity - PatientCapacity;
    public int CurrentStaffCount => assignedStaff.Count;
    public int MaxStaffCount => staffSlots != null ? staffSlots.MaxPeople : maxStaff;
    public int PatientUpgrade => patientCapacityLevel;
    public int UpgradeLevel => patientCapacityLevel;
    public int MaxUpgradeLevel => GetMaxPatientCapacityLevel();

    public string FacilityId
    {
        get
        {
            if (definition != null && !string.IsNullOrWhiteSpace(definition.FacilityId))
                return definition.FacilityId;
            return fallbackFacilityId;
        }
    }

    private void Awake()
    {
        staffSlots = new StaffAssignment(maxStaff);
        CacheCharacterManager();
    }

    private void OnValidate()
    {
        patientCapacityLevel = Mathf.Clamp(patientCapacityLevel, 0, GetMaxPatientCapacityLevel());
        maxStaff = Mathf.Max(0, maxStaff);
        healDays = Mathf.Max(1, healDays);
    }

    private void Start()
    {
        if (GameDateManager.Instance != null)
            GameDateManager.Instance.DayAdvanced += OnDayAdvanced;
    }

    private void OnDestroy()
    {
        if (GameDateManager.Instance != null)
            GameDateManager.Instance.DayAdvanced -= OnDayAdvanced;
    }

    public void LoadPatientUpgrade(int saved)
    {
        int previousCapacity = PatientCapacity;
        patientCapacityLevel = Mathf.Clamp(saved, 0, GetMaxPatientCapacityLevel());

        if (PatientCapacity != previousCapacity)
            NotifyPatientSlotsChanged();
    }

    public void ApplyUpgradeLevel(int level)
    {
        LoadPatientUpgrade(level);
    }

    public bool TryAssignPatient(NPCRuntimeData target)
    {
        return target != null && TryAssignPatient(target.RuntimeId);
    }

    public bool TryAssignPatient(string runtimeId)
    {
        if (string.IsNullOrWhiteSpace(runtimeId)) return false;
        if (FindPatientSlotIndex(runtimeId) >= 0) return true;

        if (patientTreatments.Count >= PatientCapacity) return false;
        if (!TryGetCharacterManager(out CharacterManager manager)) return false;

        if (!manager.TryAssignToFacility(
                runtimeId,
                FacilityId,
                roomId,
                CharacterAssignmentFilter.AvailableInjured,
                out NPCRuntimeData target,
                out _))
        {
            return false;
        }

        patientTreatments.Add(new MedicalTreatment(target, GetEffectiveHealDays()));
        NotifyPatientSlotsChanged();
        return true;
    }

    public bool TryReleasePatient(NPCRuntimeData target)
    {
        return target != null && TryReleasePatient(target.RuntimeId);
    }

    public bool TryReleasePatient(string runtimeId)
    {
        int slotIndex = FindPatientSlotIndex(runtimeId);
        if (slotIndex < 0) return false;
        if (!TryGetCharacterManager(out CharacterManager manager)) return false;

        MedicalTreatment treatment = patientTreatments[slotIndex];
        if (!manager.TryReleaseFromFacility(treatment.Patient.RuntimeId, out _))
            return false;

        patientTreatments.RemoveAt(slotIndex);
        NotifyPatientSlotsChanged();
        return true;
    }

    public bool UpgradePatientCapacity(int amount)
    {
        if (amount <= 0) return false;

        int previousCapacity = PatientCapacity;
        patientCapacityLevel = Mathf.Clamp(patientCapacityLevel + amount, 0, GetMaxPatientCapacityLevel());
        bool upgraded = PatientCapacity > previousCapacity;
        if (upgraded)
            NotifyPatientSlotsChanged();

        return upgraded;
    }

    public int GetPatientHealDaysRemaining(NPCRuntimeData patient)
    {
        int slotIndex = FindPatientSlotIndex(patient);
        return slotIndex >= 0 ? patientTreatments[slotIndex].RemainingDays : 0;
    }

    public bool TryAssignStaff(NPCRuntimeData staff)
    {
        if (staff == null) return false;
        if (assignedStaff.Contains(staff)) return true;
        if (staffSlots == null) staffSlots = new StaffAssignment(maxStaff);
        if (!staffSlots.CanAssign(assignedStaff.Count, 1)) return false;

        assignedStaff.Add(staff);
        ApplyHealDayDelta(staff.Type, subtract: true);
        OnStaffAssigned?.Invoke(staff);
        NotifyPatientSlotsChanged();
        return true;
    }

    public bool TryReleaseStaff(NPCRuntimeData staff)
    {
        if (staff == null || !assignedStaff.Remove(staff)) return false;
        ApplyHealDayDelta(staff.Type, subtract: false);
        OnStaffReleased?.Invoke(staff);
        NotifyPatientSlotsChanged();
        return true;
    }

    private void OnDayAdvanced(int prev, int next)
    {
        int elapsedDays = Mathf.Max(1, next - prev);
        bool changed = false;
        for (int i = patientTreatments.Count - 1; i >= 0; i--)
        {
            MedicalTreatment treatment = patientTreatments[i];
            treatment.ReduceRemainingDays(elapsedDays);
            changed = true;

            if (treatment.RemainingDays <= 0)
                CompleteHealing(i);
        }

        if (changed)
            NotifyPatientSlotsChanged();
    }

    private void CompleteHealing(int slotIndex)
    {
        MedicalTreatment treatment = patientTreatments[slotIndex];
        NPCRuntimeData patient = treatment.Patient;

        if (!TryGetCharacterManager(out CharacterManager manager))
            return;

        if (!manager.TryCompleteRecovery(patient.RuntimeId, out _))
            return;

        patientTreatments.RemoveAt(slotIndex);
        OnPatientHealed?.Invoke(patient);
    }

    private int GetEffectiveHealDays()
    {
        int reduction = 0;
        foreach (var staff in assignedStaff)
            reduction += GetHealBonus(staff.Type);
        return Mathf.Max(1, healDays - reduction);
    }

    private int GetHealBonus(NPCType type)
    {
        foreach (var bonus in staffHealBonuses)
            if (bonus.type == type) return bonus.daysReduction;
        return 0;
    }

    private void ApplyHealDayDelta(NPCType type, bool subtract)
    {
        int bonus = GetHealBonus(type);
        if (bonus <= 0) return;

        int delta = subtract ? -bonus : bonus;
        for (int i = 0; i < patientTreatments.Count; i++)
        {
            patientTreatments[i].AdjustRemainingDays(delta);
        }
    }

    private int FindPatientSlotIndex(NPCRuntimeData patient)
    {
        if (patient == null)
            return -1;

        return FindPatientSlotIndex(patient.RuntimeId);
    }

    private int FindPatientSlotIndex(string runtimeId)
    {
        if (string.IsNullOrWhiteSpace(runtimeId))
            return -1;

        string normalizedRuntimeId = runtimeId.Trim();
        for (int i = 0; i < patientTreatments.Count; i++)
        {
            MedicalTreatment treatment = patientTreatments[i];
            if (treatment.Patient != null && treatment.Patient.RuntimeId == normalizedRuntimeId)
                return i;
        }

        return -1;
    }

    private bool TryGetCharacterManager(out CharacterManager manager)
    {
        manager = CacheCharacterManager();
        if (manager != null)
            return true;

        Debug.LogWarning("[MedicalManager] CharacterManager is not available.", this);
        return false;
    }

    private CharacterManager CacheCharacterManager()
    {
        if (characterManager == null)
            characterManager = CharacterManager.Instance;

        if (characterManager == null)
            characterManager = FindFirstObjectByType<CharacterManager>();

        return characterManager;
    }

    private int GetPatientCapacity()
    {
        switch (patientCapacityLevel)
        {
            case 0:
                return BasePatientCapacity;
            case 1:
                return FirstUpgradePatientCapacity;
            default:
                return FullPatientCapacity;
        }
    }

    private int GetMaxPatientCapacityLevel()
    {
        return MaxPatientCapacityLevel;
    }

    private void NotifyPatientSlotsChanged()
    {
        RefreshPatientStatuses();
        OnPatientSlotsChanged?.Invoke();
    }

    private void RefreshPatientStatuses()
    {
        patientStatuses.Clear();
        for (int i = 0; i < patientTreatments.Count; i++)
        {
            MedicalTreatment treatment = patientTreatments[i];
            patientStatuses.Add(new PatientStatus(treatment.Patient, treatment.RemainingDays));
        }
    }

    private sealed class MedicalTreatment
    {
        public NPCRuntimeData Patient { get; }
        public int RemainingDays { get; private set; }

        public MedicalTreatment(NPCRuntimeData patient, int remainingDays)
        {
            Patient = patient;
            RemainingDays = Mathf.Max(1, remainingDays);
        }

        public void ReduceRemainingDays(int days)
        {
            RemainingDays = Mathf.Max(0, RemainingDays - Mathf.Max(1, days));
        }

        public void AdjustRemainingDays(int delta)
        {
            RemainingDays = Mathf.Max(1, RemainingDays + delta);
        }
    }
}


public readonly struct PatientStatus
{
    public PatientStatus(NPCRuntimeData patient, int remainingDays)
    {
        Patient = patient;
        RemainingDays = remainingDays;
    }

    public NPCRuntimeData Patient { get; }
    public int RemainingDays { get; }
}


//Treatment logic
[System.Serializable]
public struct StaffHealBonus
{
    public NPCType type;
    public int daysReduction;
}
