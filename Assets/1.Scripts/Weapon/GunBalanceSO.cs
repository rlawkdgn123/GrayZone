using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Gun에 주입할 순수 수치 밸런스 데이터를 보관합니다.
/// </summary>
/// <remarks>
/// 이 파일은 SO CSV 도구가 위 스크립트들의 [BalanceField] 필드에서 생성했습니다.
/// 필드 이름은 "{스크립트 이름}_{필드 이름}" 규칙을 따릅니다. 통합 SO 하나가 여러 컴포넌트 값을 함께 담아도
/// 이름이 겹치지 않게 하기 위해서이며, BindManager가 같은 규칙으로 짝을 찾습니다.
/// [BalanceField(Shared = true)]로 선언한 필드만 접두사 없이 생성되어 여러 컴포넌트가 같은 값을 받습니다.
/// 값의 정본은 SO이며 CSV는 내보낸 스냅샷입니다. 허용 범위는 스크립트 쪽 [Clamp]에 선언됩니다.
/// 미디어·런타임 참조는 담지 않습니다. 표현 리소스는 Feedback SO가 소유합니다.
/// </remarks>
[CreateAssetMenu(fileName = "GunBalanceSO", menuName = "GrayZone/Balance/GunBalanceSO")]
public sealed class GunBalanceSO : ScriptableObject, IBalanceTableData
{
    // ───────────── Gun ─────────────

    [Header("Gun")]
    [Tooltip("Shotgun 한 번의 사격에서 발사할 펠릿 수입니다. 탄약·반동·총성·탄피는 펠릿 수와 무관하게 한 번만 처리하며, Shotgun이 아닌 총기는 항상 1발로 처리합니다. (1 이상)")]
    [SerializeField] private int Gun_m_pelletCount = 1;

    [Tooltip("최대 탄약 수입니다. (0 이상)")]
    [FormerlySerializedAs("m_maxBullet")]
    [SerializeField] private int Gun_m_maxBullet = 30;

    [Tooltip("켜면 크로스헤어 장탄 아크를 최대 장탄수만큼 끊어 한 칸이 한 발을 나타내게 합니다.")]
    [SerializeField] private bool Gun_m_segmentAmmoGaugeByRound;

    [Tooltip("사격 후 다음 사격이 가능해질 때까지의 지연 시간입니다. (0 이상)")]
    [FormerlySerializedAs("m_shootDelay")]
    [SerializeField] private float Gun_m_shootDelay = 0.12f;

    [Tooltip("발사 입력 방식입니다. Full Auto는 홀드 연사, Semi Auto는 클릭 한 번에 한 발만 발사합니다.")]
    [SerializeField] private GunFireMode Gun_m_fireMode = GunFireMode.FullAuto;

    [Tooltip("재장전에 걸리는 시간(초)입니다. 이 값이 정본이며 탄약 충전·조준선 게이지·재장전 애니메이션 배속이 모두 여기에 맞춰집니다. 애니메이션은 완료 이벤트가 이 시간에 오도록 자동으로 배속됩니다(예: 1배속 클립이 2.67초면 1.33을 넣으면 2배속). (0 이상)")]
    [FormerlySerializedAs("m_reloadTime")]
    [SerializeField] private float Gun_m_reloadTime = 1.5f;

    [Tooltip("재장전 방식입니다. Magazine은 시간이 끝날 때 부족한 탄약을 한 번에 채우고, IndividualRounds는 총 재장전 시간 ÷ 장탄량 간격마다 1발씩 채웁니다. 개별 장전은 중간에 취소해도 이미 들어간 탄약이 유지됩니다.")]
    [SerializeField] private ReloadMode Gun_m_reloadMode = ReloadMode.Magazine;

    [Tooltip("명중 한 발이 주는 기본 피해량입니다. Shotgun은 펠릿 하나당 이 피해를 각각 적용하며, 약점 배율과 거리 감쇠는 펠릿별 피해에 곱해집니다. (0 이상)")]
    [FormerlySerializedAs("m_hitscanDamage")]
    [SerializeField] private int Gun_m_hitscanDamage = 1;

    [Tooltip("이 무기가 약점 판정을 사용하는지 여부입니다. 끄면 약점 부위를 맞혀도 일반 피해로 처리하고 약점 표시도 뜨지 않습니다.")]
    [FormerlySerializedAs("m_allowHeadshot")]
    [SerializeField] private bool Gun_m_allowHeadshot = true;

    [Tooltip("약점 부위를 맞혔을 때 곱하는 피해 배율입니다. 약점 판정이 꺼져 있으면 사용하지 않습니다. (0 이상)")]
    [FormerlySerializedAs("m_headshotDamageMultiplier")]
    [SerializeField] private float Gun_m_headshotDamageMultiplier = 2f;

    [Tooltip("탄이 도달하는 최대 사거리(m)입니다. 이 거리를 넘어가면 아무것도 맞히지 않습니다. (0 이상)")]
    [FormerlySerializedAs("m_hitscanRange")]
    [SerializeField] private float Gun_m_hitscanRange = 100f;

    [Tooltip("명중 시 대상을 밀어내는 넉백 충격량(N·s)입니다. 사망한 대상은 래그돌이 이 힘을 받습니다. 0이면 피격 대상이 정한 최소치만 적용됩니다. (0 이상)")]
    [FormerlySerializedAs("m_knockbackImpulse")]
    [SerializeField] private float Gun_m_knockbackImpulse = 0f;

    [Tooltip("힙파이어·서기 자세의 최소 방사각(도). (0 이상)")]
    [FormerlySerializedAs("m_hipfireMinSpread")]
    [FormerlySerializedAs("Gun_m_hipfireMinSpread")]
    [SerializeField] private float Gun_m_hipfireStandingMinSpread = 2f;

    [Tooltip("힙파이어·서기 자세의 최대 방사각(도). 연사 누적값은 이 값을 넘지 않습니다. (0 이상)")]
    [FormerlySerializedAs("m_hipfireMaxSpread")]
    [FormerlySerializedAs("Gun_m_hipfireMaxSpread")]
    [SerializeField] private float Gun_m_hipfireStandingMaxSpread = 7f;

    [Tooltip("힙파이어·앉기 자세의 최소 방사각(도). 앉는 도중에는 서기 값에서 이 값으로 보간됩니다. (0 이상)")]
    [SerializeField] private float Gun_m_hipfireCrouchMinSpread = 1.2f;

    [Tooltip("힙파이어·앉기 자세의 최대 방사각(도). 연사 누적값은 이 값을 넘지 않습니다. (0 이상)")]
    [SerializeField] private float Gun_m_hipfireCrouchMaxSpread = 4.2f;

    [Tooltip("힙파이어·공중 자세의 최소 방사각(도). 공중에서는 앉기 여부와 무관하게 이 범위를 사용합니다. (0 이상)")]
    [SerializeField] private float Gun_m_hipfireAirborneMinSpread = 4f;

    [Tooltip("힙파이어·공중 자세의 최대 방사각(도). 연사 누적값은 이 값을 넘지 않습니다. (0 이상)")]
    [SerializeField] private float Gun_m_hipfireAirborneMaxSpread = 9f;

    [Tooltip("힙파이어에서 이 발수까지는 최소 방사각을 유지하고 연사 증가값을 누적하지 않습니다. (0 이상)")]
    [FormerlySerializedAs("m_hipfireMinSpreadShotCount")]
    [SerializeField] private int Gun_m_hipfireMinSpreadShotCount = 3;

    [Tooltip("힙파이어 발사마다 누적되는 방사각 증가량(도). (0 이상)")]
    [FormerlySerializedAs("m_hipfireSpreadIncreasePerShot")]
    [SerializeField] private float Gun_m_hipfireSpreadIncreasePerShot = 0.5f;

    [Tooltip("힙파이어 사격을 멈춘 뒤 초당 회복(감소)하는 방사각(도/초). (0 이상)")]
    [FormerlySerializedAs("m_hipfireSpreadRecoveryPerSecond")]
    [SerializeField] private float Gun_m_hipfireSpreadRecoveryPerSecond = 8f;

    [Tooltip("발사 입력을 놓은 뒤 이 시간(초)이 지나면 힙파이어 탄퍼짐 회복을 시작하고 연사 발수 카운트를 리셋합니다. 입력을 유지하는 동안에는 회복하지 않습니다. (0 이상)")]
    [FormerlySerializedAs("m_hipfireSpreadRecoveryDelay")]
    [SerializeField] private float Gun_m_hipfireSpreadRecoveryDelay = 0.3f;

    [Tooltip("ADS·서기 자세의 최소 방사각(도). 0이면 정밀 사격입니다. (0 이상)")]
    [FormerlySerializedAs("m_adsMinSpread")]
    [FormerlySerializedAs("Gun_m_adsMinSpread")]
    [SerializeField] private float Gun_m_adsStandingMinSpread = 0f;

    [Tooltip("ADS·서기 자세의 최대 방사각(도). 연사 누적값은 이 값을 넘지 않습니다. (0 이상)")]
    [FormerlySerializedAs("m_adsMaxSpread")]
    [FormerlySerializedAs("Gun_m_adsMaxSpread")]
    [SerializeField] private float Gun_m_adsStandingMaxSpread = 3f;

    [Tooltip("ADS·앉기 자세의 최소 방사각(도). 앉는 도중에는 서기 값에서 이 값으로 보간됩니다. (0 이상)")]
    [SerializeField] private float Gun_m_adsCrouchMinSpread = 0f;

    [Tooltip("ADS·앉기 자세의 최대 방사각(도). 연사 누적값은 이 값을 넘지 않습니다. (0 이상)")]
    [SerializeField] private float Gun_m_adsCrouchMaxSpread = 1.8f;

    [Tooltip("ADS·공중 자세의 최소 방사각(도). 공중에서는 앉기 여부와 무관하게 이 범위를 사용합니다. (0 이상)")]
    [SerializeField] private float Gun_m_adsAirborneMinSpread = 2f;

    [Tooltip("ADS·공중 자세의 최대 방사각(도). 연사 누적값은 이 값을 넘지 않습니다. (0 이상)")]
    [SerializeField] private float Gun_m_adsAirborneMaxSpread = 5f;

    [Tooltip("ADS에서 이 발수까지는 최소 방사각을 유지하고 연사 증가값을 누적하지 않습니다. (0 이상)")]
    [FormerlySerializedAs("m_adsMinSpreadShotCount")]
    [SerializeField] private int Gun_m_adsMinSpreadShotCount = 3;

    [Tooltip("ADS 발사마다 누적되는 방사각 증가량(도). (0 이상)")]
    [FormerlySerializedAs("m_adsSpreadIncreasePerShot")]
    [SerializeField] private float Gun_m_adsSpreadIncreasePerShot = 0.4f;

    [Tooltip("ADS 사격을 멈춘 뒤 초당 회복(감소)하는 방사각(도/초). (0 이상)")]
    [FormerlySerializedAs("m_adsSpreadRecoveryPerSecond")]
    [SerializeField] private float Gun_m_adsSpreadRecoveryPerSecond = 8f;

    [Tooltip("발사 입력을 놓은 뒤 이 시간(초)이 지나면 ADS 탄퍼짐 회복을 시작하고 연사 발수 카운트를 리셋합니다. 입력을 유지하는 동안에는 회복하지 않습니다. (0 이상)")]
    [FormerlySerializedAs("m_adsSpreadRecoveryDelay")]
    [SerializeField] private float Gun_m_adsSpreadRecoveryDelay = 0.3f;

    [Tooltip("탄퍼짐 분포 방식입니다. Uniform=원판 전체 균일, Gaussian=중심 가중 정규분포(기본). Shotgun 펠릿도 이 분포를 그대로 사용합니다.")]
    [FormerlySerializedAs("m_spreadDistribution")]
    [SerializeField] private SpreadDistribution Gun_m_spreadDistribution = SpreadDistribution.Gaussian;

    [Tooltip("Gaussian 분포의 중심 집중도입니다. 콘 반각(최대 방사각)을 몇 σ로 볼지 정합니다. 값이 클수록 탄이 중심에 더 몰립니다(기본 3 = 약 99%가 콘 안, 평균 편향은 반각의 약 0.42배). 낮출수록 가장자리로 퍼집니다. Uniform에는 영향이 없습니다. (1 이상)")]
    [FormerlySerializedAs("m_spreadConcentration")]
    [SerializeField] private float Gun_m_spreadConcentration = 3f;

    [Tooltip("사격 1회의 기본 소음량입니다. 가청 여부가 아니라, 변이체가 여러 소음 중 어느 것을 추적할지 비교할 때만 쓰입니다. 기획 미확정 - 임시값입니다. (0 이상)")]
    [FormerlySerializedAs("m_shotNoiseLevel")]
    [SerializeField] private float Gun_m_shotNoiseLevel = 1f;

    [Tooltip("사격 소음이 들리는 거리(m)입니다. 이 거리 안이면 들리고 밖이면 들리지 않습니다. 벽이나 엄폐물에 의한 감쇠는 적용하지 않습니다. 변이체 시야(12m)보다 훨씬 커야 소음 유인 전술이 성립합니다. 기획 미확정 - 임시값입니다. (0 이상)")]
    [FormerlySerializedAs("m_shotNoiseRange")]
    [SerializeField] private float Gun_m_shotNoiseRange = 40f;

    [Tooltip("발사 1회당 세로(피치) 반동 각도(도)입니다. 양수면 조준이 위로 솟습니다(머즐 클라임). 실제 조준을 밀어 탄착에도 영향을 주며(LogicalAim), 사격을 멈추면 자동 회복됩니다. (0 이상)")]
    [FormerlySerializedAs("m_recoilPitchKick")]
    [SerializeField] private float Gun_m_recoilPitchKick = 0.6f;

    [Tooltip("발사 1회당 좌우(요) 반동 각도(도)의 크기입니다. 실제 조준을 밀어 탄착에도 영향을 줍니다. 0이면 좌우 반동이 없습니다. (0 이상)")]
    [FormerlySerializedAs("m_recoilYawKick")]
    [SerializeField] private float Gun_m_recoilYawKick = 0.2f;

    [Tooltip("좌우 반동(Yaw)의 방향 패턴입니다. Random=매 발 ±범위 무작위, AlternateLeftFirst=좌·우 번갈아(첫 발 왼쪽), AlternateRightFirst=우·좌 번갈아(첫 발 오른쪽). Alternate는 위 크기를 그대로 좌우로 씁니다.")]
    [FormerlySerializedAs("m_yawKickPattern")]
    [SerializeField] private KickSidePattern Gun_m_yawKickPattern = KickSidePattern.Random;

    [Tooltip("발사 1회당 카메라 롤(Dutch) 크기(도)입니다. 화면만 살짝 기울입니다. 조준/탄착에는 영향이 없습니다. (0 이상)")]
    [FormerlySerializedAs("m_recoilRoll")]
    [SerializeField] private float Gun_m_recoilRoll = 0.5f;

    [Tooltip("카메라 롤(Dutch)의 방향 패턴입니다. Random=매 발 ±범위 무작위, AlternateLeftFirst=좌·우 번갈아(첫 발 왼쪽), AlternateRightFirst=우·좌 번갈아(첫 발 오른쪽). Yaw 반동과 독립적으로 설정됩니다.")]
    [FormerlySerializedAs("m_rollKickPattern")]
    [SerializeField] private KickSidePattern Gun_m_rollKickPattern = KickSidePattern.Random;

    [Tooltip("발사 1회당 카메라 FOV 펀치(도)입니다. 순간적으로 시야가 벌어졌다 회복되는 시각 반동 연출입니다. 조준/탄착에는 영향이 없습니다. (0 이상)")]
    [FormerlySerializedAs("m_recoilFovPunch")]
    [SerializeField] private float Gun_m_recoilFovPunch = 1f;

    [Tooltip("조준(ADS) 중 발사 1회당 카메라 FOV 펀치(도)입니다. 조준 중에는 화면이 확대돼 같은 값도 더 크게 보이므로 힙파이어와 따로 둡니다. (0 이상)")]
    [FormerlySerializedAs("m_recoilFovPunchAds")]
    [SerializeField] private float Gun_m_recoilFovPunchAds = 1f;

    [Tooltip("거리에 따른 피해 배율 구간표입니다. 구간을 두지 않으면 거리와 무관하게 기본 피해가 들어갑니다.")]
    [FormerlySerializedAs("m_damageFalloff")]
    [SerializeField] private DamageFalloffTable Gun_m_damageFalloff;

    [Tooltip("한 발이 유닛을 몇 번 꿰뚫는지와 꿰뚫을 때마다의 피해 감쇠 구간표입니다. 기본값은 관통 없음입니다. 벽과 지형은 관통하지 않습니다.")]
    [FormerlySerializedAs("m_penetration")]
    [SerializeField] private PenetrationTable Gun_m_penetration;

    // ───────────── SO 메타데이터 ─────────────
    // 생성기가 만들지 않는 필드입니다. 재생성해도 지워지지 않게 여기 유지합니다.
    // m_weaponType은 Gun의 Shared BalanceField와 이름으로 연결되어 런타임 발사 방식도 결정합니다.

    [Tooltip("테이블과 향후 런타임 어댑터에서 사용할 고정 무기 ID입니다.")]
    [SerializeField] private string m_weaponId = "weapon.rifle.01";

    [Tooltip("무기 분류입니다.")]
    [SerializeField] private WeaponType m_weaponType = WeaponType.AssaultRifle;

    [Tooltip("기획 시트와 Inspector에 표시할 무기 이름입니다.")]
    [SerializeField] private string m_weaponName = "Rifle 1";

    [Tooltip("명중 시 적의 경직력 누적에 더하는 저지력입니다. 피해·넉백과는 별개이며, 적의 경직 한계치에 닿으면 경직이 발동합니다. 경직이 없는 대상에게는 무시됩니다.")]
    [SerializeField] private float m_stoppingPower;
}
