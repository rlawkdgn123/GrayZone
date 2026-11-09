using UnityEngine;

/// <summary>
/// 상호작용 안내 HUD(<see cref="TrapInfoHud"/>)에 이름·설명·비용을 보여 줄 수 있는 상호작용 대상입니다.
/// </summary>
/// <remarks>
/// 함정 설치와 정문 수리처럼 "자원을 내고 F를 꾹 누르는" 상호작용이 같은 안내 패널을 쓰도록 둡니다.
/// 대상 판정과 실행 조건은 그대로 <see cref="IInteractable"/>·<see cref="IInteractionRequirement"/>가 정하고,
/// 이 인터페이스는 표시할 값만 제공합니다.
/// </remarks>
public interface IInteractionHudInfo
{
    /// <summary>안내 패널 제목입니다.</summary>
    string HudDisplayName { get; }

    /// <summary>안내 패널 설명입니다.</summary>
    string HudDescription { get; }

    /// <summary>비용 줄 맨 앞(아이콘 왼쪽)에 붙일 동작 이름입니다(예: "설치", "수리"). 비어 있으면 표시하지 않습니다.</summary>
    string HudActionLabel { get; }

    /// <summary>한 번 실행할 때 소모하는 자원입니다. 비용이 없으면 유효하지 않은 값입니다.</summary>
    ResourceCost HudCost { get; }

    /// <summary>지금 비용을 낼 수 있는지 여부입니다. 거짓이면 비용 옆에 자원 부족 문구를 붙입니다.</summary>
    bool CanAffordHudCost { get; }

    /// <summary>
    /// 자원과 관계없이 지금 실행할 수 없는 이유입니다(예: "체력이 충분합니다"). 막혀 있지 않으면 null이나 빈 문자열입니다.
    /// </summary>
    /// <remarks>값이 있으면 HUD는 자원 부족 문구 대신 이 문구를 비용 옆에 붙입니다. 실제로 막는 것은 <see cref="IInteractionRequirement"/>입니다.</remarks>
    string HudBlockedMessage { get; }

    /// <summary>디버그 등으로 비용을 받지 않는 중인지 여부입니다.</summary>
    bool IsHudCostWaived { get; }

    /// <summary>비용 아이콘으로 쓸 이미지입니다. null이면 HUD에 배치된 기본 아이콘을 씁니다.</summary>
    Sprite HudCostIcon { get; }

    /// <summary>안내가 뜬 동안 외곽선을 그릴 오브젝트입니다. null이면 외곽선을 그리지 않습니다.</summary>
    GameObject HudOutlineTarget { get; }
}
