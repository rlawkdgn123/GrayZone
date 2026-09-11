using System;
using System.Collections.Generic;
using UnityEngine;

public enum ManufacturingStartJobFailureReason
{
    None = 0,
    InvalidSlot = 1,
    SlotLocked = 2,
    InvalidQuantity = 3,
    RecipeNotFound = 4,
    RecipeLocked = 5,
    InvalidResultItem = 6,
    ContextUnavailable = 7,
    SlotOccupied = 8,
    CostOverflow = 9,
    ResultQuantityOverflow = 10,
    InsufficientResources = 11,
    InvalidJobData = 12,
    RuntimeDataRejected = 13,
    ResourceRollbackFailed = 14
}

public readonly struct ManufacturingMaterialQuoteLine
{
    public string ResourceId { get; }
    public int RequiredAmount { get; }
    public int OwnedAmount { get; }
    public bool IsEnough => OwnedAmount >= RequiredAmount;

    public ManufacturingMaterialQuoteLine(
        string resourceId,
        int requiredAmount,
        int ownedAmount)
    {
        ResourceId = ResourceIds.Normalize(resourceId);
        RequiredAmount = Math.Max(0, requiredAmount);
        OwnedAmount = Math.Max(0, ownedAmount);
    }
}

/// <summary>
/// 제조 시설의 공용 해금/업그레이드 연결과 제조 전용 작업 흐름을 담당합니다.
/// </summary>
/// <remarks>
/// 시설 해금과 레벨의 진실원천은 <see cref="FacilityManager"/>입니다.
/// 제조 작업과 창고 상태는 각각 <see cref="ShelterSceneDataManager.Manufacturing"/>과
/// <see cref="ShelterSceneDataManager.Storage"/>를 직접 사용하며 이 컴포넌트가 복제본을 소유하지 않습니다.
/// </remarks>
public sealed class ManufacturingManager : MonoBehaviour, IFacilityUpgradeable, IFuelShortageAffected
{
    private const int MaxLevelIndex = 3;
    private const int TotalCraftingSlotCount = ManufacturingRuntimeData.SlotCount;
    private const int TotalHelperSlotCount = 2;

    private static readonly ManufacturingJobRuntimeData[] EmptyJobs =
        Array.Empty<ManufacturingJobRuntimeData>();

    [Header("Facility")]
    [SerializeField] private FacilityDefinition definition;
    [SerializeField] private string fallbackFacilityId = "manufacturing_center";
    [SerializeField] private string roomId = "manufacturing_room";

    [Header("Scene Dependencies")]
    [SerializeField] private ShelterSceneDataManager shelterDataManager;
    [SerializeField] private CharacterManager characterManager;

    [Header("Level Visuals")]
    [SerializeField] private FacilityLevelVisuals levelVisuals;

    [Header("Upgrade Cost (레벨 i → i+1)")]
    [SerializeField] private UpgradeCostTier[] upgradeCosts;

    [Header("Recipes")]
    [SerializeField] private ManufacturingRecipeDefinition[] recipes =
        Array.Empty<ManufacturingRecipeDefinition>();
    [Min(1)]
    [SerializeField] private int maxOrderQuantity = 99;

    [Header("Productivity")]
    [Min(1)]
    [SerializeField] private int baseProductivity = 5;
    [Min(0)]
    [SerializeField] private int level3ProductivityBonus = 5;
    [Min(0)]
    [SerializeField] private int fuelShortageEfficiencyDecrease = 2;
    [SerializeField] private HelperProductivityBonus[] helperProductivityBonuses =
    {
        new HelperProductivityBonus { type = NPCType.Tanker, bonus = 2 },
        new HelperProductivityBonus { type = NPCType.Healer, bonus = 1 },
        new HelperProductivityBonus { type = NPCType.Dealer, bonus = 1 }
    };

    [Header("Helper Slots")]
    [Tooltip("두 번째 헬퍼 슬롯이 열리는 플레이어 표시 레벨입니다.")]
    [Range(1, 4)]
    [SerializeField] private int secondHelperSlotUnlockLevel = 3;

    private bool m_isUnlocked = true;
    private bool m_isFuelShortageActive;
    // 임시 빌드 전용: 슬롯별 1회 사용 상태. 세이브하지 않으며 방어전 귀환 연결점에서 재충전한다.
    private readonly bool[] m_craftingSlotAvailable = new bool[TotalCraftingSlotCount];

    /// <summary>작업 생성, 진행, 취소 또는 시설 상태가 바뀌었을 때 발생합니다.</summary>
    public event Action StateChanged;

    /// <summary>제조 작업 목록 또는 작업 진행량이 바뀌었을 때 발생합니다.</summary>
    public event Action JobsChanged;

    /// <summary>헬퍼 배치 상태 또는 최종 제작력이 바뀌었을 때 발생합니다.</summary>
    public event Action HelpersChanged;

    public string FacilityId
    {
        get
        {
            if (definition != null && !string.IsNullOrWhiteSpace(definition.FacilityId))
                return definition.FacilityId;

            return fallbackFacilityId;
        }
    }

    /// <summary>내부 0 기반 시설 레벨입니다. UI 표시 레벨은 <see cref="DisplayLevel"/>을 사용합니다.</summary>
    public int UpgradeLevel => CurrentLevel;
    public int MaxUpgradeLevel => MaxLevelIndex;
    public int DisplayLevel => CurrentLevel + 1;
    public bool IsUnlocked => m_isUnlocked;
    public int CraftingSlotCount => TotalCraftingSlotCount;
    public int HelperSlotCount => TotalHelperSlotCount;
    public int UnlockedCraftingSlotCount => Mathf.Clamp(DisplayLevel, 1, TotalCraftingSlotCount);
    public int HelperCapacity => HelperSlotsForLevel(CurrentLevel);
    public int CurrentHelperCount => CountAssignedHelpers();
    public int MaxOrderQuantity => maxOrderQuantity;
    public int FinalProductivity => CalculateFinalProductivity();
    public IReadOnlyList<ManufacturingRecipeDefinition> Recipes =>
        recipes ?? Array.Empty<ManufacturingRecipeDefinition>();
    public IReadOnlyList<ManufacturingJobRuntimeData> Jobs =>
        TryGetManufacturingData(out ManufacturingRuntimeData runtimeData)
            ? runtimeData.Jobs
            : EmptyJobs;

    private int CurrentLevel =>
        FacilityManager.Instance != null
            ? FacilityManager.Instance.GetUpgradeLevel(FacilityId)
            : 0;

    private void Awake()
    {
        CacheDependencies();
        RechargeAllCraftingSlots();
    }

    private void OnValidate()
    {
        fallbackFacilityId = fallbackFacilityId?.Trim() ?? string.Empty;
        roomId = roomId?.Trim() ?? string.Empty;
        maxOrderQuantity = Mathf.Max(1, maxOrderQuantity);
        baseProductivity = Mathf.Max(1, baseProductivity);
        level3ProductivityBonus = Mathf.Max(0, level3ProductivityBonus);
        fuelShortageEfficiencyDecrease = Mathf.Max(0, fuelShortageEfficiencyDecrease);
        secondHelperSlotUnlockLevel = Mathf.Clamp(secondHelperSlotUnlockLevel, 1, 4);
        recipes ??= Array.Empty<ManufacturingRecipeDefinition>();
        helperProductivityBonuses ??= Array.Empty<HelperProductivityBonus>();
    }

    private void Start()
    {
        CacheDependencies();

        // 방어전을 위한 로직 변경
        /*
        if (GameDateManager.Instance != null)
            GameDateManager.Instance.DayAdvanced += OnDayAdvanced;
        */

        if (characterManager != null)
            characterManager.CharactersChanged += OnCharactersChanged;

        // 등록 즉시 FacilityManager가 셸터 런타임의 해금/레벨 상태를 적용합니다.
        FacilityManager.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        // 방어전을 위한 로직 변경
        /*
        if (GameDateManager.Instance != null)
            GameDateManager.Instance.DayAdvanced -= OnDayAdvanced;
        */

        if (characterManager != null)
            characterManager.CharactersChanged -= OnCharactersChanged;

        FacilityManager.Instance?.Unregister(this);
    }

    public void ApplyUpgradeLevel(int level)
    {
        RefreshLevelVisuals();
        NotifyStateChanged();
    }

    public void ApplyUnlockState(bool isUnlocked)
    {
        m_isUnlocked = isUnlocked;
        RefreshLevelVisuals();
        NotifyStateChanged();
    }

    public void ApplyFuelShortageState(bool isActive)
    {
        if (m_isFuelShortageActive == isActive)
            return;

        m_isFuelShortageActive = isActive;
        HelpersChanged?.Invoke();
        StateChanged?.Invoke();
    }

    public CostBundle GetUpgradeCost(int currentLevel)
    {
        if (upgradeCosts == null || currentLevel < 0 || currentLevel >= upgradeCosts.Length)
            return new CostBundle();

        UpgradeCostTier tier = upgradeCosts[currentLevel];
        UpgradeCostEntry[] entries = tier?.entries;
        if (entries == null || entries.Length == 0)
            return new CostBundle();

        ResourceCost[] costs = new ResourceCost[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            UpgradeCostEntry entry = entries[i];
            costs[i] = entry != null
                ? new ResourceCost(entry.resourceId, entry.amount)
                : new ResourceCost(string.Empty, 0);
        }

        return new CostBundle(costs);
    }

    public bool AreUpgradeRequirementsMet(int currentLevel) => true;

    public IReadOnlyList<FacilityFeatureLine> GetUpgradeFeatureLines(int currentLevel)
    {
        List<FacilityFeatureLine> lines = new();
        int nextLevel = currentLevel + 1;
        if (nextLevel > MaxLevelIndex)
            return lines;

        int currentCraftingSlots = CraftingSlotsForLevel(currentLevel);
        int nextCraftingSlots = CraftingSlotsForLevel(nextLevel);
        if (currentCraftingSlots != nextCraftingSlots)
        {
            lines.Add(new FacilityFeatureLine(
                "제조 슬롯",
                $"{currentCraftingSlots} → {nextCraftingSlots}"));
        }

        int currentHelperSlots = HelperSlotsForLevel(currentLevel);
        int nextHelperSlots = HelperSlotsForLevel(nextLevel);
        if (currentHelperSlots != nextHelperSlots)
        {
            lines.Add(new FacilityFeatureLine(
                "헬퍼 슬롯",
                $"{currentHelperSlots} → {nextHelperSlots}"));
        }

        int currentLevelBonus = LevelProductivityBonus(currentLevel);
        int nextLevelBonus = LevelProductivityBonus(nextLevel);
        if (currentLevelBonus != nextLevelBonus)
        {
            lines.Add(new FacilityFeatureLine(
                "기본 제작력",
                $"{baseProductivity + currentLevelBonus} → {baseProductivity + nextLevelBonus}"));
        }

        return lines;
    }

    /// <summary>제조 슬롯이 현재 시설 레벨에서 사용 가능한지 반환합니다.</summary>
    public bool IsCraftingSlotUnlocked(int slotIndex)
    {
        return m_isUnlocked
            && slotIndex >= 0
            && slotIndex < CraftingSlotsForLevel(CurrentLevel);
    }

    /// <summary>헬퍼 슬롯이 현재 시설 레벨에서 사용 가능한지 반환합니다.</summary>
    public bool IsHelperSlotUnlocked(int slotIndex)
    {
        return m_isUnlocked
            && slotIndex >= 0
            && slotIndex < HelperSlotsForLevel(CurrentLevel);
    }

    /// <summary>레시피가 현재 시설 레벨에서 사용 가능한지 반환합니다.</summary>
    public bool IsRecipeUnlocked(ManufacturingRecipeDefinition recipe)
    {
        return m_isUnlocked
            && recipe != null
            && recipe.RequiredFacilityLevel <= DisplayLevel;
    }

    public bool TryGetRecipe(string recipeId, out ManufacturingRecipeDefinition recipe)
    {
        recipe = null;
        string normalizedId = NormalizeId(recipeId);
        if (string.IsNullOrEmpty(normalizedId) || recipes == null)
            return false;

        for (int i = 0; i < recipes.Length; i++)
        {
            ManufacturingRecipeDefinition candidate = recipes[i];
            if (candidate != null
                && string.Equals(candidate.RecipeId, normalizedId, StringComparison.Ordinal))
            {
                recipe = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 레시피 실행 횟수에 필요한 전체 재료와 현재 보유 수량을 호출자 목록에 채웁니다.
    /// UI는 이 읽기 전용 견적을 표시하며 실제 차감은 <see cref="TryStartJob(int,string,int)"/>이 다시 검증합니다.
    /// </summary>
    public bool TryFillMaterialQuote(
        string recipeId,
        int requestedBatchCount,
        List<ManufacturingMaterialQuoteLine> results,
        out bool canAfford)
    {
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        results.Clear();
        canAfford = false;

        if (requestedBatchCount < 1
            || requestedBatchCount > maxOrderQuantity
            || !TryGetRecipe(recipeId, out ManufacturingRecipeDefinition recipe)
            || !TryGetContext(
                out _,
                out StorageFacility storage,
                out _)
            || !TryBuildQuantityCost(
                recipe.BuildUnitCost(),
                requestedBatchCount,
                out CostBundle totalCost))
        {
            return false;
        }

        canAfford = true;
        foreach (ResourceCost cost in totalCost.Costs)
        {
            int ownedAmount =
                storage.GetResourceAmount(cost.ResourceId);
            ManufacturingMaterialQuoteLine line = new(
                cost.ResourceId,
                cost.Amount,
                ownedAmount);
            results.Add(line);
            if (!line.IsEnough)
                canAfford = false;
        }

        return true;
    }

    /// <summary>
    /// 전체 재료를 선차감하고 비어 있는 슬롯에 제조 작업을 생성합니다.
    /// </summary>
    public bool TryStartJob(int slotIndex, string recipeId, int quantity)
    {
        return TryStartJob(slotIndex, recipeId, quantity, out _);
    }

    /// <summary>
    /// 전체 재료를 선차감하고 비어 있는 슬롯에 제조 작업을 생성하며 실패 원인을 반환합니다.
    /// </summary>
    public bool TryStartJob(
        int slotIndex,
        string recipeId,
        int requestedBatchCount,
        out ManufacturingStartJobFailureReason failureReason)
    {
        failureReason = ManufacturingStartJobFailureReason.None;

        if (slotIndex < 0 || slotIndex >= TotalCraftingSlotCount)
        {
            failureReason = ManufacturingStartJobFailureReason.InvalidSlot;
            return false;
        }

        if (!IsCraftingSlotUnlocked(slotIndex))
        {
            failureReason = ManufacturingStartJobFailureReason.SlotLocked;
            return false;
        }

        if (requestedBatchCount < 1 || requestedBatchCount > maxOrderQuantity)
        {
            failureReason = ManufacturingStartJobFailureReason.InvalidQuantity;
            return false;
        }

        if (!TryGetRecipe(recipeId, out ManufacturingRecipeDefinition recipe))
        {
            failureReason = ManufacturingStartJobFailureReason.RecipeNotFound;
            return false;
        }

        if (!IsRecipeUnlocked(recipe))
        {
            failureReason = ManufacturingStartJobFailureReason.RecipeLocked;
            return false;
        }

        if (!recipe.HasValidResult)
        {
            failureReason = ManufacturingStartJobFailureReason.InvalidResultItem;
            return false;
        }

        if (!TryGetContext(
                out ShelterSceneDataManager dataManager,
                out StorageFacility storage,
                out ManufacturingRuntimeData runtimeData))
        {
            failureReason = ManufacturingStartJobFailureReason.ContextUnavailable;
            return false;
        }

        if (runtimeData.TryGetJob(slotIndex, out _))
        {
            failureReason = ManufacturingStartJobFailureReason.SlotOccupied;
            return false;
        }

        CostBundle unitCost = recipe.BuildUnitCost();
        if (!TryBuildQuantityCost(unitCost, requestedBatchCount, out CostBundle totalCost))
        {
            failureReason = ManufacturingStartJobFailureReason.CostOverflow;
            return false;
        }

        ManufacturingJobRuntimeData job = new(
            slotIndex,
            recipe.RecipeId,
            recipe.ResultKind,
            recipe.ResultDefinitionId,
            requestedBatchCount,
            recipe.ResultQuantityPerBatch,
            recipe.UnitWork,
            unitCost.Costs);

        if (!job.IsValid)
        {
            failureReason = ManufacturingStartJobFailureReason.InvalidJobData;
            return false;
        }

        if (!TryCalculateResultQuantity(
                job.RequestedBatchCount,
                job.ResultQuantityPerBatchSnapshot,
                out int totalResultQuantity)
            || !CanStoreResultQuantity(
                storage,
                job.ResultKind,
                job.ResultDefinitionId,
                totalResultQuantity))
        {
            failureReason = ManufacturingStartJobFailureReason.ResultQuantityOverflow;
            return false;
        }

        if (!storage.TrySpendResources(totalCost))
        {
            failureReason = ManufacturingStartJobFailureReason.InsufficientResources;
            return false;
        }

        if (!runtimeData.TryAddJob(job))
        {
            if (!storage.TryAddResources(totalCost))
            {
                failureReason = ManufacturingStartJobFailureReason.ResourceRollbackFailed;
                Debug.LogError(
                    "[ManufacturingManager] Failed to rollback resources after job creation failed.",
                    this);
                return false;
            }

            failureReason = ManufacturingStartJobFailureReason.RuntimeDataRejected;
            return false;
        }

        dataManager.MarkDirty();
        NotifyJobsChanged();
        return true;
    }

    /// <summary>임시 빌드 전용: 지정 제작 슬롯의 1회 사용 가능 상태를 반환합니다.</summary>
    public bool IsCraftingSlotAvailable(int slotIndex)
    {
        return slotIndex >= 0
            && slotIndex < m_craftingSlotAvailable.Length
            && m_craftingSlotAvailable[slotIndex];
    }

    /// <summary>
    /// 임시 빌드 전용: 기존 레시피 재료 비용을 즉시 차감하고 결과물을 바로 창고에 입고합니다.
    /// 날짜 작업, 진행 게이지, 취소 및 환불 데이터는 만들지 않습니다.
    /// </summary>
    public bool TryCraftImmediately(
        int slotIndex,
        string recipeId,
        int requestedBatchCount,
        out ManufacturingStartJobFailureReason failureReason)
    {
        failureReason = ManufacturingStartJobFailureReason.None;

        if (slotIndex < 0 || slotIndex >= TotalCraftingSlotCount)
        {
            failureReason = ManufacturingStartJobFailureReason.InvalidSlot;
            return false;
        }

        if (!IsCraftingSlotUnlocked(slotIndex))
        {
            failureReason = ManufacturingStartJobFailureReason.SlotLocked;
            return false;
        }

        if (!IsCraftingSlotAvailable(slotIndex))
        {
            failureReason = ManufacturingStartJobFailureReason.SlotOccupied;
            return false;
        }

        if (requestedBatchCount < 1 || requestedBatchCount > maxOrderQuantity)
        {
            failureReason = ManufacturingStartJobFailureReason.InvalidQuantity;
            return false;
        }

        if (!TryGetRecipe(recipeId, out ManufacturingRecipeDefinition recipe))
        {
            failureReason = ManufacturingStartJobFailureReason.RecipeNotFound;
            return false;
        }

        if (!IsRecipeUnlocked(recipe))
        {
            failureReason = ManufacturingStartJobFailureReason.RecipeLocked;
            return false;
        }

        if (!recipe.HasValidResult)
        {
            failureReason = ManufacturingStartJobFailureReason.InvalidResultItem;
            return false;
        }

        if (!TryGetContext(
                out ShelterSceneDataManager dataManager,
                out StorageFacility storage,
                out ManufacturingRuntimeData runtimeData))
        {
            failureReason = ManufacturingStartJobFailureReason.ContextUnavailable;
            return false;
        }

        if (runtimeData.TryGetJob(slotIndex, out _))
        {
            failureReason = ManufacturingStartJobFailureReason.SlotOccupied;
            return false;
        }

        CostBundle unitCost = recipe.BuildUnitCost();
        if (!TryBuildQuantityCost(unitCost, requestedBatchCount, out CostBundle totalCost))
        {
            failureReason = ManufacturingStartJobFailureReason.CostOverflow;
            return false;
        }

        if (!TryCalculateResultQuantity(
                requestedBatchCount,
                recipe.ResultQuantityPerBatch,
                out int totalResultQuantity)
            || !CanStoreResultQuantity(
                storage,
                recipe.ResultKind,
                recipe.ResultDefinitionId,
                totalResultQuantity))
        {
            failureReason = ManufacturingStartJobFailureReason.ResultQuantityOverflow;
            return false;
        }

        if (!storage.TrySpendResources(totalCost))
        {
            failureReason = ManufacturingStartJobFailureReason.InsufficientResources;
            return false;
        }

        if (!TryStoreCompletedResult(
                storage,
                recipe.ResultKind,
                recipe.ResultDefinitionId,
                totalResultQuantity))
        {
            failureReason = ManufacturingStartJobFailureReason.RuntimeDataRejected;
            return false;
        }

        m_craftingSlotAvailable[slotIndex] = false;
        dataManager.MarkDirty();
        NotifyJobsChanged();
        return true;
    }

    /// <summary>
    /// 임시 빌드 전용 방어전 귀환 연결점입니다. 방어전 결과 확정 후 셸터 진입 시 호출합니다.
    /// </summary>
    public void RechargeAllCraftingSlots()
    {
        for (int i = 0; i < m_craftingSlotAvailable.Length; i++)
            m_craftingSlotAvailable[i] = true;

        NotifyJobsChanged();
    }

    /// <summary>
    /// 진행 중인 작업을 취소하고 아직 완성되지 않은 수량분의 시작 당시 비용을 환불합니다.
    /// </summary>
    public bool TryCancelJob(int slotIndex)
    {
        if (!TryGetContext(
                out ShelterSceneDataManager dataManager,
                out StorageFacility storage,
                out ManufacturingRuntimeData runtimeData)
            || !runtimeData.TryGetJob(slotIndex, out ManufacturingJobRuntimeData job))
        {
            return false;
        }

        if (!TryBuildRefundCost(job, out CostBundle refund)
            || !storage.TryAddResources(refund)
            || !runtimeData.RemoveJob(slotIndex))
        {
            return false;
        }

        dataManager.MarkDirty();
        NotifyJobsChanged();
        return true;
    }

    /// <summary>현재 제조 헬퍼 후보를 호출자가 제공한 목록에 채웁니다.</summary>
    public void FillHelperCandidates(List<ShelterMemberRuntimeData> results)
    {
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        results.Clear();
        if (!TryGetCharacterManager(out CharacterManager manager))
            return;

        foreach (ShelterMemberRuntimeData character in manager.Characters)
        {
            if (CanAssignHelper(character))
                results.Add(character);
        }
    }

    /// <summary>현재 제조실에 배치된 헬퍼를 호출자가 제공한 목록에 채웁니다.</summary>
    public void FillAssignedHelpers(List<ShelterMemberRuntimeData> results)
    {
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        results.Clear();
        if (!TryGetCharacterManager(out CharacterManager manager))
            return;

        foreach (ShelterMemberRuntimeData character in manager.Characters)
        {
            if (IsAssignedHelper(character))
                results.Add(character);
        }
    }

    public bool CanAssignHelper(ShelterMemberRuntimeData character)
    {
        if (!m_isUnlocked
            || character == null
            || CurrentHelperCount >= HelperCapacity
            || character.IsDead
            || character.IsAssignedToFacility)
        {
            return false;
        }

        return character.InjuryState == CharacterInjuryState.Normal
            || character.InjuryState == CharacterInjuryState.Minor;
    }

    public bool TryAssignHelper(string runtimeId)
    {
        if (!TryGetCharacterManager(out CharacterManager manager)
            || !manager.TryGetCharacter(runtimeId, out ShelterMemberRuntimeData character))
        {
            return false;
        }

        if (IsAssignedHelper(character))
            return true;

        if (!CanAssignHelper(character))
            return false;

        return manager.TryAssignToFacility(
            runtimeId,
            FacilityId,
            roomId,
            CharacterAssignmentFilter.AvailableAlive,
            FacilityAssignmentKind.Staff,
            out _);
    }

    public bool TryReleaseHelper(string runtimeId)
    {
        if (!TryGetCharacterManager(out CharacterManager manager)
            || !manager.TryGetCharacter(runtimeId, out ShelterMemberRuntimeData character)
            || !IsAssignedHelper(character))
        {
            return false;
        }

        return manager.TryReleaseFromFacility(runtimeId, out _);
    }

    // 방어전을 위한 로직 변경
    /*
    private void OnDayAdvanced(int previousDay, int nextDay)
    {
        if (!m_isUnlocked
            || !TryGetContext(
                out ShelterSceneDataManager dataManager,
                out StorageFacility storage,
                out ManufacturingRuntimeData runtimeData))
        {
            return;
        }

        int productivity = FinalProductivity;
        IReadOnlyList<ManufacturingJobRuntimeData> jobs = runtimeData.Jobs;
        bool changed = false;

        for (int i = jobs.Count - 1; i >= 0; i--)
        {
            ManufacturingJobRuntimeData job = jobs[i];
            if (job.IsComplete)
            {
                runtimeData.RemoveJob(job.SlotIndex);
                changed = true;
                continue;
            }

            int newCompletedBatchCount = PredictNewCompletedBatchCount(job, productivity);
            if (newCompletedBatchCount > 0
                && (!TryCalculateResultQuantity(
                        newCompletedBatchCount,
                        job.ResultQuantityPerBatchSnapshot,
                        out int predictedResultQuantity)
                    || !CanStoreResultQuantity(
                        storage,
                        job.ResultKind,
                        job.ResultDefinitionId,
                        predictedResultQuantity)))
            {
                Debug.LogError(
                    $"[ManufacturingManager] Result quantity overflow for "
                    + $"'{job.ResultDefinitionId}' ({job.ResultKind}).",
                    this);
                continue;
            }

            int processedWorkBefore = job.ProcessedWork;
            int actualCompletedBatchCount = job.ApplyWork(productivity);
            if (job.ProcessedWork == processedWorkBefore)
                continue;

            changed = true;
            if (actualCompletedBatchCount <= 0 && !job.IsComplete)
                continue;

            if (actualCompletedBatchCount > 0
                && (!TryCalculateResultQuantity(
                        actualCompletedBatchCount,
                        job.ResultQuantityPerBatchSnapshot,
                        out int resultQuantity)
                    || !TryStoreCompletedResult(
                        storage,
                        job.ResultKind,
                        job.ResultDefinitionId,
                        resultQuantity)))
            {
                Debug.LogError(
                    $"[ManufacturingManager] Failed to store completed result "
                    + $"'{job.ResultDefinitionId}' ({job.ResultKind}).",
                    this);
                continue;
            }

            if (job.IsComplete)
                runtimeData.RemoveJob(job.SlotIndex);
        }

        if (!changed)
            return;

        dataManager.MarkDirty();
        NotifyJobsChanged();
    }
    */

    private void OnCharactersChanged()
    {
        HelpersChanged?.Invoke();
        StateChanged?.Invoke();
    }

    private int CalculateFinalProductivity()
    {
        int total = baseProductivity + LevelProductivityBonus(CurrentLevel);
        if (TryGetCharacterManager(out CharacterManager manager))
        {
            foreach (ShelterMemberRuntimeData character in manager.Characters)
            {
                if (IsAssignedHelper(character))
                    total += GetHelperProductivityBonus(character.Type);
            }
        }

        if (m_isFuelShortageActive)
            total -= fuelShortageEfficiencyDecrease;

        return Mathf.Max(1, total);
    }

    private int CountAssignedHelpers()
    {
        if (!TryGetCharacterManager(out CharacterManager manager))
            return 0;

        int count = 0;
        foreach (ShelterMemberRuntimeData character in manager.Characters)
        {
            if (IsAssignedHelper(character))
                count++;
        }

        return count;
    }

    private bool IsAssignedHelper(ShelterMemberRuntimeData character)
    {
        return character != null
            && character.AssignmentKind == FacilityAssignmentKind.Staff
            && string.Equals(
                character.AssignedFacilityId,
                FacilityId,
                StringComparison.Ordinal);
    }

    private int GetHelperProductivityBonus(NPCType type)
    {
        if (helperProductivityBonuses == null)
            return 0;

        for (int i = 0; i < helperProductivityBonuses.Length; i++)
        {
            if (helperProductivityBonuses[i].type == type)
                return Mathf.Max(0, helperProductivityBonuses[i].bonus);
        }

        return 0;
    }

    private int CraftingSlotsForLevel(int level)
    {
        return Mathf.Clamp(level + 1, 1, TotalCraftingSlotCount);
    }

    private int HelperSlotsForLevel(int level)
    {
        int displayLevel = Mathf.Clamp(level + 1, 1, 4);
        return displayLevel >= secondHelperSlotUnlockLevel ? 2 : 1;
    }

    private int LevelProductivityBonus(int level)
    {
        return level >= 2 ? level3ProductivityBonus : 0;
    }

    private static int PredictNewCompletedBatchCount(
        ManufacturingJobRuntimeData job,
        int workAmount)
    {
        if (job == null || !job.IsValid || workAmount <= 0 || job.IsComplete)
            return 0;

        int completedBefore = job.CompletedBatchCount;
        int appliedWork = Math.Min(workAmount, job.RemainingWork);
        int completedAfter = Math.Min(
            job.RequestedBatchCount,
            (job.ProcessedWork + appliedWork) / job.UnitWorkSnapshot);
        return completedAfter - completedBefore;
    }

    private static bool TryBuildQuantityCost(
        CostBundle unitCost,
        int quantity,
        out CostBundle totalCost)
    {
        totalCost = new CostBundle();
        if (quantity <= 0)
            return false;

        if (unitCost == null || unitCost.IsFree)
            return true;

        Dictionary<string, int> totals =
            new(StringComparer.Ordinal);
        List<string> orderedResourceIds = new();
        foreach (ResourceCost cost in unitCost.Costs)
        {
            if (cost.IsValid
                && !totals.ContainsKey(cost.ResourceId))
            {
                orderedResourceIds.Add(cost.ResourceId);
            }

            if (!TryAccumulateCost(
                    totals,
                    cost.ResourceId,
                    cost.Amount,
                    quantity))
                return false;
        }

        totalCost = CreateCostBundle(totals, orderedResourceIds);
        return true;
    }

    private static bool TryBuildRefundCost(
        ManufacturingJobRuntimeData job,
        out CostBundle refund)
    {
        refund = new CostBundle();
        if (job == null || !job.IsValid)
            return false;

        Dictionary<string, int> totals =
            new(StringComparer.Ordinal);
        foreach (ManufacturingMaterialCostSnapshot cost in job.UnitCostSnapshots)
        {
            if (cost != null
                && !TryAccumulateCost(
                    totals,
                    cost.ResourceId,
                    cost.UnitAmount,
                    job.RemainingBatchCount))
            {
                return false;
            }
        }

        refund = CreateCostBundle(totals);
        return true;
    }

    private static bool TryAccumulateCost(
        Dictionary<string, int> totals,
        string resourceId,
        int unitAmount,
        int quantity)
    {
        if (unitAmount <= 0 || quantity <= 0)
            return true;

        string id = ResourceIds.Normalize(resourceId);
        if (string.IsNullOrEmpty(id))
            return false;

        long added = (long)unitAmount * quantity;
        totals.TryGetValue(id, out int current);
        long total = current + added;
        if (total > int.MaxValue)
            return false;

        totals[id] = (int)total;
        return true;
    }

    private static bool TryCalculateResultQuantity(
        int completedBatchCount,
        int resultQuantityPerBatch,
        out int resultQuantity)
    {
        resultQuantity = 0;
        if (completedBatchCount <= 0 || resultQuantityPerBatch <= 0)
            return false;

        long calculated = (long)completedBatchCount * resultQuantityPerBatch;
        if (calculated > int.MaxValue)
            return false;

        resultQuantity = (int)calculated;
        return true;
    }

    private static bool CanStoreResultQuantity(
        StorageFacility storage,
        ManufacturingResultKind resultKind,
        string resultDefinitionId,
        int amount)
    {
        if (storage == null || amount <= 0)
            return false;

        int current = resultKind == ManufacturingResultKind.Resource
            ? storage.GetResourceAmount(resultDefinitionId)
            : storage.GetItemQuantity(resultDefinitionId);
        return current <= int.MaxValue - amount;
    }

    private static bool TryStoreCompletedResult(
        StorageFacility storage,
        ManufacturingResultKind resultKind,
        string resultDefinitionId,
        int amount)
    {
        if (!CanStoreResultQuantity(
                storage,
                resultKind,
                resultDefinitionId,
                amount))
        {
            return false;
        }

        return resultKind == ManufacturingResultKind.Resource
            ? storage.TryAddResource(resultDefinitionId, amount)
            : storage.TryAddItem(resultDefinitionId, amount);
    }

    private static CostBundle CreateCostBundle(
        Dictionary<string, int> totals)
    {
        ResourceCost[] costs = new ResourceCost[totals.Count];
        int index = 0;
        foreach (KeyValuePair<string, int> total in totals)
        {
            costs[index++] =
                new ResourceCost(total.Key, total.Value);
        }

        return new CostBundle(costs);
    }

    private static CostBundle CreateCostBundle(
        Dictionary<string, int> totals,
        IReadOnlyList<string> orderedResourceIds)
    {
        ResourceCost[] costs =
            new ResourceCost[orderedResourceIds.Count];
        for (int i = 0; i < orderedResourceIds.Count; i++)
        {
            string resourceId = orderedResourceIds[i];
            costs[i] = new ResourceCost(
                resourceId,
                totals[resourceId]);
        }

        return new CostBundle(costs);
    }

    private void RefreshLevelVisuals()
    {
        if (levelVisuals == null)
            return;

        if (m_isUnlocked)
            levelVisuals.ShowLevel(CurrentLevel);
        else
            levelVisuals.HideAll();
    }

    private void NotifyJobsChanged()
    {
        JobsChanged?.Invoke();
        StateChanged?.Invoke();
    }

    private void NotifyStateChanged()
    {
        HelpersChanged?.Invoke();
        StateChanged?.Invoke();
    }

    private bool TryGetContext(
        out ShelterSceneDataManager dataManager,
        out StorageFacility storage,
        out ManufacturingRuntimeData runtimeData)
    {
        dataManager = CacheShelterDataManager();
        storage = dataManager?.Storage;
        runtimeData = dataManager?.Manufacturing;
        if (dataManager != null && storage != null && runtimeData != null)
            return true;

        Debug.LogWarning("[ManufacturingManager] Shelter manufacturing context is not available.", this);
        return false;
    }

    private bool TryGetManufacturingData(out ManufacturingRuntimeData runtimeData)
    {
        ShelterSceneDataManager dataManager = CacheShelterDataManager();
        runtimeData = dataManager?.Manufacturing;
        return runtimeData != null;
    }

    private bool TryGetCharacterManager(out CharacterManager manager)
    {
        manager = CacheCharacterManager();
        return manager != null;
    }

    private void CacheDependencies()
    {
        CacheShelterDataManager();
        CacheCharacterManager();
    }

    private ShelterSceneDataManager CacheShelterDataManager()
    {
        if (shelterDataManager == null)
            shelterDataManager = ShelterSceneDataManager.Instance;

        if (shelterDataManager == null)
            shelterDataManager = FindFirstObjectByType<ShelterSceneDataManager>();

        return shelterDataManager;
    }

    private CharacterManager CacheCharacterManager()
    {
        if (characterManager == null)
            characterManager = CharacterManager.Instance;

        if (characterManager == null)
            characterManager = FindFirstObjectByType<CharacterManager>();

        return characterManager;
    }

    private static string NormalizeId(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    [Serializable]
    private sealed class UpgradeCostEntry
    {
        public string resourceId = string.Empty;
        [Min(0)] public int amount = 0;
    }

    [Serializable]
    private sealed class UpgradeCostTier
    {
        public UpgradeCostEntry[] entries = Array.Empty<UpgradeCostEntry>();
    }

    [Serializable]
    private struct HelperProductivityBonus
    {
        public NPCType type;
        [Min(0)] public int bonus;
    }
}
