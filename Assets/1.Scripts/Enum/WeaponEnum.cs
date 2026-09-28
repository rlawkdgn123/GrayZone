using UnityEngine;

public enum ModifierOp
{
    Add,
    Multiply,
    Minus,
    Division
}
public enum PartType
{
    Muzzle,                     // 총구: 소음기, 소염기, 컴펜세이
    Barrel,                     // 총열: 사거리나 이동 속도에 영향을 주는 다양한 길이의 배럴
    Laser,                      // 레이저: 지향 사격 정확도나 조준 속도를 높여주는 레이저 사이트
    Optic,                      // 조준경: 도트 사이트, 홀로그래픽, 고배율 스코프
    Stock,                      // 개머리판: 반동 제어나 기동성을 결정하는 부위
    Underbarrel,                // 하부 부착물: 수직/각진 손잡이, 유탄 발사기, 양각대
    Magazine,                   // 탄창: 대용량 탄창이나 특정 구역용 탄종 변경
    RearGrip,                   // 후방 그립: 손잡이 표면 처리를 통해 조준 안정성 개선
    Ammunition,                 // 탄약: 철갑탄, 저지력 탄환 등 탄의 속성 변경
}
public enum StatType
{
    Damage,            //공격력
    FireRate,          //연사속도
    ReloadSpeed,       //재장전속도
    BulletSpray,       //탄퍼짐
    Recoil,            //반동수치
    Ammo,              //탄창 용량
    ADSSpeed,          //조준속도
    NoiseLevel,        //소음량
    Mobility,          //기동성
    BulletSpeed,       //탄환속도
}
public enum WeaponType
{
    Pistol,             // 권총
    SubmachineGun,      // 기관단총 (SMG)
    AssaultRifle,       // 돌격소총 (AR)
    Shotgun,            // 산탄총
    SniperRifle,        // 저격소총 (SR)
    LightMachineGun,    // 경기관총 (LMG)
    Melee,              // 근접 무기
    Grenade             // 투척물
}

/// <summary>
/// 총기의 탄약을 재장전하는 방식입니다.
/// </summary>
public enum ReloadMode
{
    /// <summary>재장전 시간이 끝날 때 부족한 탄약을 한 번에 채웁니다.</summary>
    Magazine,

    /// <summary>총 재장전 시간을 장탄량으로 나눈 간격마다 탄약을 1발씩 채웁니다.</summary>
    IndividualRounds,
}

/// <summary>
/// 플레이어 입력을 한 발의 사격 요청으로 해석하는 방식입니다.
/// </summary>
/// <remarks>
/// 기존 총기 프리팹에 직렬화 값이 없어도 현재 동작을 유지하도록 완전자동을 0으로 둡니다.
/// 실제 발사 간격과 재사격 가능 여부는 이 값과 별개로 Gun의 Shoot Delay가 계속 제한합니다.
/// </remarks>
public enum GunFireMode
{
    /// <summary>발사 입력을 누르고 있는 동안 Shoot Delay 간격으로 계속 사격합니다.</summary>
    FullAuto = 0,

    /// <summary>발사 입력을 새로 누른 순간에 한 발만 사격합니다.</summary>
    SemiAuto = 1,
}

// 아래 두 enum은 Gun(런타임)와 밸런스 SO가 같은 타입을 공유하기 위해
// 각 클래스 중첩 정의에서 이곳으로 옮겼습니다. 두 곳에 따로 정의하면 멤버 이름이
// 어긋났을 때 BindManager의 이름 기반 변환이 조용히 실패합니다.

/// <summary>
/// 탄퍼짐 콘 안에서 발사 방향을 흩뜨리는 분포 방식입니다.
/// </summary>
/// <remarks>
/// 탄퍼짐 콘 "안에서의 분포 모양"만 결정합니다. 콘의 각도 크기는 min/max spread가, 조준선에 어느 반경까지
/// 표시할지는 크로스헤어(CrosshairController)의 표시 기준이 담당합니다. 크로스헤어가 표시 배율을 계산할 때 이 값을 읽습니다.
/// </remarks>
public enum SpreadDistribution
{
    /// <summary>원판 전체에 고르게(면적 균일) 흩뜨립니다.</summary>
    Uniform,

    /// <summary>중심에 가중된 정규분포로 흩뜨립니다(기본).</summary>
    Gaussian,
}

/// <summary>
/// 좌우 반동(Yaw)과 시각 롤(Dutch)의 좌우 방향 패턴입니다. 세로 반동(Pitch)에는 영향이 없습니다.
/// </summary>
public enum KickSidePattern
{
    /// <summary>매 발 무작위 방향·크기(±범위 내).</summary>
    Random,

    /// <summary>좌·우 번갈아, 첫 발이 왼쪽입니다. 설정 크기를 그대로 좌우로 씁니다.</summary>
    AlternateLeftFirst,

    /// <summary>우·좌 번갈아, 첫 발이 오른쪽입니다. 설정 크기를 그대로 좌우로 씁니다.</summary>
    AlternateRightFirst,
}
