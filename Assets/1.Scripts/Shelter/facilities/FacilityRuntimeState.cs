/// <summary>
/// 저장과 런타임에서 공유하는 시설의 해금/업그레이드 상태
/// </summary>
[System.Serializable]
public sealed class FacilityRuntimeState
{
    /// <summary>시설 정의와 매칭되는 고정 식별자</summary>
    public string facilityId = string.Empty;

    /// <summary>시설이 현재 해금되어 있는지 여부</summary>
    public bool isUnlocked;

    /// <summary>현재 시설 업그레이드 레벨</summary>
    public int upgradeLevel;

    /// <summary>
    /// Unity 직렬화를 위한 기본 생성자
    /// </summary>
    public FacilityRuntimeState()
    {
    }

    /// <summary>
    /// 시설 런타임 상태를 생성
    /// </summary>
    /// <param name="facilityId">시설 고정 식별자</param>
    /// <param name="isUnlocked">초기 해금 여부</param>
    /// <param name="upgradeLevel">초기 업그레이드 레벨</param>
    public FacilityRuntimeState(
        string facilityId,
        bool isUnlocked,
        int upgradeLevel = 0)
    {
        this.facilityId = NormalizeFacilityId(facilityId);
        this.isUnlocked = isUnlocked;
        this.upgradeLevel = System.Math.Max(0, upgradeLevel);
    }

    /// <summary>
    /// 비어 있는 식별자와 음수 레벨을 보정
    /// </summary>
    /// <param name="fallbackFacilityId">식별자가 비어 있을 때 사용할 대체 시설 ID</param>
    public void EnsureValid(string fallbackFacilityId = "")
    {
        if (string.IsNullOrWhiteSpace(facilityId))
            facilityId = NormalizeFacilityId(fallbackFacilityId);

        upgradeLevel = System.Math.Max(0, upgradeLevel);
    }

    private static string NormalizeFacilityId(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
