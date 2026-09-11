using System.Collections.Generic;

/// <summary>
/// 셸터 시설이 해금·업그레이드 흐름에 참여하기 위해 구현하는 계약입니다.
/// </summary>
/// <remarks>
/// 업그레이드 비용과 조건은 <b>시설이 스스로 소유</b>합니다. 관리자 쪽에 표를 두면
/// 시설을 추가할 때마다 두 곳을 고쳐야 하고, 한쪽만 고친 상태를 알아채기 어렵습니다.
/// <para>
/// 여기 선언된 것은 전부 "시설이 답해 주는" 질문이고, 실제 자원 차감과 세이브 반영은 관리자가 합니다.
/// </para>
/// </remarks>
public interface IFacilityUpgradeable
{
    /// <summary>세이브와 조회에 사용하는 고정 시설 ID입니다.</summary>
    string FacilityId { get; }

    /// <summary>현재 업그레이드 단계입니다.</summary>
    int UpgradeLevel { get; }

    /// <summary>이 시설이 올라갈 수 있는 최대 단계입니다.</summary>
    int MaxUpgradeLevel { get; }

    /// <summary>지정한 단계의 효과를 시설에 반영합니다.</summary>
    /// <param name="level">적용할 업그레이드 단계입니다.</param>
    void ApplyUpgradeLevel(int level);

    /// <summary>세이브에서 복원한 해금 상태를 시설에 반영합니다.</summary>
    /// <param name="isUnlocked">해금되어 있으면 <c>true</c>입니다.</param>
    /// <remarks>FacilityManager가 세이브를 읽은 뒤 호출합니다.</remarks>
    void ApplyUnlockState(bool isUnlocked);

    /// <summary>현재 단계에서 다음 단계로 올리는 데 필요한 비용입니다.</summary>
    /// <param name="currentLevel">기준이 되는 현재 단계입니다.</param>
    /// <returns>다음 단계 상승에 드는 자원 묶음입니다.</returns>
    /// <remarks>비용표는 시설이 소유합니다. 관리자는 값을 알지 못하고 물어보기만 합니다.</remarks>
    CostBundle GetUpgradeCost(int currentLevel);

    /// <summary>자원 이외의 업그레이드 조건을 만족하는지 확인합니다.</summary>
    /// <param name="currentLevel">기준이 되는 현재 단계입니다.</param>
    /// <returns>조건을 만족하거나 조건 자체가 없으면 <c>true</c>입니다.</returns>
    /// <remarks>선행 시설·일차·플래그 같은 조건을 위한 확장 자리(seam)입니다.</remarks>
    bool AreUpgradeRequirementsMet(int currentLevel);

    /// <summary>다음 단계가 제공하는 기능을 표시용 줄 목록으로 돌려줍니다.</summary>
    /// <param name="currentLevel">기준이 되는 현재 단계입니다.</param>
    /// <returns>표시할 줄 목록입니다. 최대 단계이면 빈 목록입니다.</returns>
    /// <remarks>표시 전용이며 로직에 영향을 주지 않습니다.</remarks>
    IReadOnlyList<FacilityFeatureLine> GetUpgradeFeatureLines(int currentLevel);
}

/// <summary>연료 부족으로 효율 저하가 적용되는 시설의 런타임 계약입니다.</summary>
public interface IFuelShortageAffected
{
    void ApplyFuelShortageState(bool isActive);
}
