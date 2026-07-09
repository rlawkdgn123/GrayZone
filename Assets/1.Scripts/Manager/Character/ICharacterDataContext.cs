using System.Collections.Generic;

/// <summary>
/// CharacterManager(파사드)가 순수 로직 클래스(Policy/Query/Editor)에게 제공하는 접근 통로.
/// 직렬화 필드·이벤트·Unity 수명주기는 파사드가 소유하고, 순수 클래스는 이 인터페이스로만 접근한다.
/// </summary>
public interface ICharacterDataContext
{
    /// <summary>실제 캐릭터 데이터 원천(런타임에 늦게 붙을 수 있어 매번 조회).</summary>
    ShelterDataManager DataSource { get; }

    /// <summary>읽기 전용 모드 여부.</summary>
    bool IsReadOnly { get; }

    /// <summary>현재 활성화된 편집 능력 플래그.</summary>
    CharacterEditCapability EnabledCapabilities { get; }

    /// <summary>
    /// 캐릭터가 변경되었음을 알린다. 파사드가 DataSource.MarkDirty + CharacterChanged/RosterChanged 이벤트를 발행한다.
    /// </summary>
    void NotifyCharacterChanged(NPCRuntimeData character);
}
