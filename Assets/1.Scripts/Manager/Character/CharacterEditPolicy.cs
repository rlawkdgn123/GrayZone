/// <summary>
/// 캐릭터 편집 허용 여부(정책) 판정. 데이터 원천 유무 → 읽기전용 → 능력 플래그 순으로 검사한다.
/// 상태는 보유하지 않고 파사드가 주입한 컨텍스트(읽기전용/능력값/데이터원천)를 참조한다.
/// </summary>
public sealed class CharacterEditPolicy
{
    private const CharacterEditCapability LegacyAllCapabilities =
        CharacterEditCapability.FacilityAssignment |
        CharacterEditCapability.EquipmentChange |
        CharacterEditCapability.SkillChange |
        CharacterEditCapability.EquipmentUpgrade;

    private readonly ICharacterDataContext context;

    public CharacterEditPolicy(ICharacterDataContext context)
    {
        this.context = context;
    }

    /// <summary>
    /// 지정한 능력을 지금 사용할 수 있는지 판정한다. 불가 시 사유를 failure로 돌려준다.
    /// </summary>
    public bool CanUse(CharacterEditCapability capability, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (context.DataSource == null)
        {
            failure = CharacterActionFailure.DataSourceUnavailable;
            return false;
        }

        if (context.IsReadOnly)
        {
            failure = CharacterActionFailure.ReadOnlyMode;
            return false;
        }

        if ((context.EnabledCapabilities & capability) != capability)
        {
            failure = CharacterActionFailure.CapabilityDisabled;
            return false;
        }

        return true;
    }

    /// <summary>
    /// 레거시 저장값(개별 플래그 합) 정규화. 예전 "All에 해당하는 합"이면 현행 All로 승격한다.
    /// 직렬화 필드 자체는 파사드가 소유하므로, 파사드가 이 결과로 필드를 갱신한다.
    /// </summary>
    public static CharacterEditCapability NormalizeCapabilities(CharacterEditCapability value)
    {
        return value == LegacyAllCapabilities ? CharacterEditCapability.All : value;
    }
}
