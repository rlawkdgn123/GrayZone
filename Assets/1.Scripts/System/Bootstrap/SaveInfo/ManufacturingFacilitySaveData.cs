using System;
using System.Collections.Generic;

/// <summary>제조 시설의 전체 슬롯 저장 데이터입니다.</summary>
[Serializable]
public sealed class ManufacturingFacilitySaveData
{
    public List<ManufacturingSlotSaveData> slots = CreateEmptySlots();

    private static List<ManufacturingSlotSaveData> CreateEmptySlots()
    {
        List<ManufacturingSlotSaveData> results =
            new List<ManufacturingSlotSaveData>(ManufacturingRuntimeData.SlotCount);
        for (int slotIndex = 0; slotIndex < ManufacturingRuntimeData.SlotCount; slotIndex++)
        {
            results.Add(new ManufacturingSlotSaveData
            {
                slotIndex = slotIndex
            });
        }

        return results;
    }
}

/// <summary>제조 슬롯 하나의 점유 여부와 활성 작업 스냅샷입니다.</summary>
[Serializable]
public sealed class ManufacturingSlotSaveData
{
    public int slotIndex;
    public bool hasActiveJob;
    public string recipeId = string.Empty;
    public ManufacturingResultKind resultKind = ManufacturingResultKind.Item;
    public string resultDefinitionId = string.Empty;
    public int requestedBatchCount;
    public int resultQuantityPerBatchSnapshot = 1;
    public int unitWorkSnapshot;
    public int processedWork;
    public List<ManufacturingMaterialCostSaveData> unitCosts =
        new List<ManufacturingMaterialCostSaveData>();
}

/// <summary>제조 작업 시작 시 고정된 배치당 재료 비용 저장 데이터입니다.</summary>
[Serializable]
public sealed class ManufacturingMaterialCostSaveData
{
    public string resourceId = string.Empty;
    public int unitAmount;
}

/// <summary>제조 런타임 작업과 저장 슬롯 데이터 사이를 변환합니다.</summary>
public static class ManufacturingFacilitySaveDataMapper
{
    public static ManufacturingFacilitySaveData FromRuntime(ManufacturingRuntimeData runtimeData)
    {
        ManufacturingFacilitySaveData saveData = new ManufacturingFacilitySaveData();
        if (runtimeData == null)
        {
            return saveData;
        }

        IReadOnlyList<ManufacturingJobRuntimeData> jobs = runtimeData.Jobs;
        for (int i = 0; i < jobs.Count; i++)
        {
            ManufacturingJobRuntimeData job = jobs[i];
            if (job == null
                || !job.IsValid
                || job.SlotIndex < 0
                || job.SlotIndex >= saveData.slots.Count)
            {
                continue;
            }

            ManufacturingSlotSaveData slot = saveData.slots[job.SlotIndex];
            slot.hasActiveJob = true;
            slot.recipeId = job.RecipeId;
            slot.resultKind = job.ResultKind;
            slot.resultDefinitionId = job.ResultDefinitionId;
            slot.requestedBatchCount = job.RequestedBatchCount;
            slot.resultQuantityPerBatchSnapshot = job.ResultQuantityPerBatchSnapshot;
            slot.unitWorkSnapshot = job.UnitWorkSnapshot;
            slot.processedWork = job.ProcessedWork;

            IReadOnlyList<ManufacturingMaterialCostSnapshot> costs = job.UnitCostSnapshots;
            for (int costIndex = 0; costIndex < costs.Count; costIndex++)
            {
                ManufacturingMaterialCostSnapshot cost = costs[costIndex];
                if (cost != null && cost.IsValid)
                {
                    slot.unitCosts.Add(new ManufacturingMaterialCostSaveData
                    {
                        resourceId = cost.ResourceId,
                        unitAmount = cost.UnitAmount
                    });
                }
            }
        }

        return saveData;
    }

    public static ManufacturingRuntimeData ToRuntime(ManufacturingFacilitySaveData saveData)
    {
        ManufacturingRuntimeData runtimeData = new ManufacturingRuntimeData();
        if (saveData?.slots == null)
        {
            return runtimeData;
        }

        HashSet<int> restoredSlots = new HashSet<int>();
        for (int i = 0; i < saveData.slots.Count; i++)
        {
            ManufacturingSlotSaveData slot = saveData.slots[i];
            if (slot == null
                || !slot.hasActiveJob
                || slot.slotIndex < 0
                || slot.slotIndex >= ManufacturingRuntimeData.SlotCount
                || !restoredSlots.Add(slot.slotIndex))
            {
                continue;
            }

            List<ResourceCost> unitCosts = new List<ResourceCost>();
            if (slot.unitCosts != null)
            {
                for (int costIndex = 0; costIndex < slot.unitCosts.Count; costIndex++)
                {
                    ManufacturingMaterialCostSaveData cost = slot.unitCosts[costIndex];
                    if (cost != null)
                    {
                        ResourceCost resourceCost = new ResourceCost(cost.resourceId, cost.unitAmount);
                        if (resourceCost.IsValid)
                        {
                            unitCosts.Add(resourceCost);
                        }
                    }
                }
            }

            ManufacturingJobRuntimeData job = new ManufacturingJobRuntimeData(
                slot.slotIndex,
                slot.recipeId,
                slot.resultKind,
                slot.resultDefinitionId,
                slot.requestedBatchCount,
                slot.resultQuantityPerBatchSnapshot,
                slot.unitWorkSnapshot,
                unitCosts);
            job.ApplyWork(slot.processedWork);
            runtimeData.TryAddJob(job);
        }

        runtimeData.EnsureValid();
        return runtimeData;
    }
}
