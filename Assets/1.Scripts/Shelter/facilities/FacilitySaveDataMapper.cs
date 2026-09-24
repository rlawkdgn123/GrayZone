using UnityEngine;

/// <summary>
/// 시설 정의/런타임 상태와 저장 DTO 사이의 변환을 담당
/// </summary>
public static class FacilitySaveDataMapper
{
    /// <summary>
    /// 시설 정의의 기본값으로 저장 DTO를 생성
    /// </summary>
    /// <param name="definition">저장 DTO의 기준이 되는 시설 정의</param>
    /// <returns>유효한 시설 ID가 있으면 저장 DTO, 없으면 <c>null</c></returns>
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

    /// <summary>
    /// 런타임 시설 상태를 저장 DTO로 변환
    /// </summary>
    /// <param name="runtimeState">저장할 런타임 시설 상태</param>
    /// <returns>유효한 시설 ID가 있으면 저장 DTO, 없으면 <c>null</c></returns>
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

    /// <summary>
    /// 저장 DTO를 런타임 시설 상태로 복원
    /// </summary>
    /// <param name="saveData">복원할 저장 DTO</param>
    /// <returns>유효한 저장 데이터이면 런타임 상태, 아니면 <c>null</c></returns>
    public static FacilityRuntimeState ToRuntime(SaveData.FacilitySaveData saveData)
    {
        if (saveData == null || string.IsNullOrWhiteSpace(saveData.facilityId))
        {
            return null;
        }

        return new FacilityRuntimeState(
            saveData.facilityId,
            saveData.isUnlocked,
            saveData.upgradeLevel);
    }

    private static string ResolveFacilityId(FacilityDefinition definition)
    {
        return definition.FacilityId?.Trim() ?? string.Empty;
    }
}
