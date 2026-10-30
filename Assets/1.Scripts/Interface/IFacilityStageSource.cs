using System;

/// <summary>
/// 시설 UI가 현재 보여 주는 단계입니다. 모든 시설이 같은 단계 구분을 공유합니다.
/// </summary>
public enum FacilityUIStage
{
    /// <summary>시설 UI가 닫혀 있음</summary>
    None,

    /// <summary>시설 UI를 열면 먼저 보이는 선택 화면</summary>
    Menu,

    /// <summary>시설 고유 기능 화면 (제조: 제작, 의료: 진료, Scramble: 출격 확인)</summary>
    Main,

    /// <summary>업그레이드 화면</summary>
    Upgrade
}

/// <summary>
/// 시설 UI 대사 선택 기준. 화면 단계(Stage)와 그 화면 안의 시설별 상황(Condition)을 함께 담습니다.
/// </summary>
/// <remarks>
/// Condition은 시설별 상황 enum(예: <see cref="MedicalCondition"/>)의 이름이며, 빈 값이면 기본 상황입니다.
/// 공통 코드는 Condition의 의미를 모르고 대사 표 조회 키로만 사용합니다.
/// </remarks>
public readonly struct FacilityDialogueContext : IEquatable<FacilityDialogueContext>
{
    public static readonly FacilityDialogueContext None = new FacilityDialogueContext(FacilityUIStage.None);

    public readonly FacilityUIStage Stage;
    public readonly string Condition;

    public FacilityDialogueContext(FacilityUIStage stage, string condition = null)
    {
        Stage = stage;
        Condition = condition ?? string.Empty;
    }

    public bool Equals(FacilityDialogueContext other)
    {
        return Stage == other.Stage
            && string.Equals(Condition ?? string.Empty, other.Condition ?? string.Empty, StringComparison.Ordinal);
    }

    public override bool Equals(object obj) => obj is FacilityDialogueContext other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Stage, Condition ?? string.Empty);

    public override string ToString() => string.IsNullOrEmpty(Condition) ? Stage.ToString() : $"{Stage}/{Condition}";
}

/// <summary>
/// 시설 UI의 단계·상황 전환을 외부(대사, 연출 등)에 알리는 계약입니다.
/// </summary>
/// <remarks>
/// 시설 UI는 단계와 상황이 바뀌었다는 사실만 알리고, 무엇을 보여 줄지는 구독하는 쪽이 정합니다.
/// </remarks>
public interface IFacilityStageSource
{
    /// <summary>현재 단계와 상황입니다.</summary>
    FacilityDialogueContext CurrentContext { get; }

    /// <summary>단계 또는 상황이 바뀔 때 발생합니다.</summary>
    event Action<FacilityDialogueContext> ContextChanged;
}
