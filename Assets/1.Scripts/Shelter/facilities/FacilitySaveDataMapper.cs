using UnityEngine;

public static class FacilitySaveDataMapper
{
    public static SaveData.FacilitySaveData FromDefinition(FacilityDefinition definition)
    {
        if (definition == null)
        {
            return null;
        }

        string facilityId = ResolveFacilityId(definition);
        if (string.IsNullOrWhiteSpace(facilityId))
        {
            return null;
        }

        return new SaveData.FacilitySaveData
        {
            facilityId = facilityId,
            isUnlocked = definition.UnlockedByDefault,
            upgradeLevel = 0
        };
    }

    public static SaveData.FacilitySaveData FromRuntime(FacilityRuntimeState runtimeState)
    {
        if (runtimeState == null)
        {
            return null;
        }

        runtimeState.EnsureValid();
        if (string.IsNullOrWhiteSpace(runtimeState.facilityId))
        {
            return null;
        }

        return new SaveData.FacilitySaveData
        {
            facilityId = runtimeState.facilityId,
            isUnlocked = runtimeState.isUnlocked,
            upgradeLevel = Mathf.Max(0, runtimeState.upgradeLevel)
        };
    }

    public static FacilityRuntimeState ToRuntime(SaveData.FacilitySaveData saveData)
    {
        if (saveData == null || string.IsNullOrWhiteSpace(saveData.facilityId))
        {
            return null;
        }

        return new FacilityRuntimeState(saveData.facilityId, saveData.isUnlocked, saveData.upgradeLevel);
    }

    private static string ResolveFacilityId(FacilityDefinition definition)
    {
        return definition.FacilityId?.Trim() ?? string.Empty;
    }
}
