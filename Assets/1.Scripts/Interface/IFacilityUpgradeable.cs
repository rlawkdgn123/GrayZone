public interface IFacilityUpgradeable
{
    string FacilityId { get; }
    int UpgradeLevel { get; }
    int MaxUpgradeLevel { get; }

    void ApplyUpgradeLevel(int level);

    // FacilityManager가 세이브에서 복원한 해금 상태를 시설에 반영한다.
    void ApplyUnlockState(bool isUnlocked);

    // 현재 레벨(currentLevel)에서 다음 레벨로 올리는 데 필요한 비용(시설이 자기 비용을 소유).
    CostBundle GetUpgradeCost(int currentLevel);

    // 자원 이외의 업그레이드 조건(선행 시설·일차·플래그 등). 조건 없으면 true. — 확장 자리(seam).
    bool AreUpgradeRequirementsMet(int currentLevel);
}
