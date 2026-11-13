using System;

/// <summary>
/// <see cref="PlayerInputController.SetInputLock"/>으로 닫을 수 있는 플레이어 입력 종류입니다.
/// </summary>
/// <remarks>
/// 여러 개를 함께 고를 수 있습니다. 값은 비트로 저장되므로 기존 항목의 번호를 바꾸지 말고 끝에 추가하세요.
/// </remarks>
[Flags]
public enum PlayerInputLock
{
    None = 0,
    Move = 1 << 0,
    Look = 1 << 1,
    Jump = 1 << 2,
    Sprint = 1 << 3,
    Aim = 1 << 4,
    Shoot = 1 << 5,
    ThrowMode = 1 << 6,
    Reload = 1 << 7,
    Crouch = 1 << 8,
    Interact = 1 << 9,
    Inventory = 1 << 10,

    /// <summary>캐릭터 변경(1·2·3, Tab)입니다. <see cref="SquadManager"/>가 읽습니다.</summary>
    SwitchMember = 1 << 11,

    /// <summary>스킬(C)입니다. <see cref="SquadManager"/>가 읽습니다.</summary>
    Skill = 1 << 12,

    /// <summary>팀원 위치 사수 명령(Q/E)입니다. <see cref="SquadManager"/>가 읽습니다.</summary>
    SquadOrder = 1 << 13,

    /// <summary>팀원 전체 복귀(CapsLock)입니다. <see cref="SquadManager"/>가 읽습니다.</summary>
    Recall = 1 << 14,

    /// <summary>회복약(X)입니다. <see cref="PotionManager"/>가 읽습니다.</summary>
    Potion = 1 << 15,

    /// <summary>AI 팀원의 사격입니다. 키 입력은 아니지만 튜토리얼 챕터 잠금으로 함께 막으려고 둡니다. <see cref="SquadAIController"/>가 읽습니다.</summary>
    SquadAiFire = 1 << 16,
}
