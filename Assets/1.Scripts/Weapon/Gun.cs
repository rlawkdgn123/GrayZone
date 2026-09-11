using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using VInspector;

/// <summary>
/// 무기의 탄약, 사격, 재장전, 탄피/탄창/머즐 이펙트 생성, 탄약 UI 갱신을 담당하는 컨트롤러입니다.
/// </summary>
/// <remarks>
/// 실제 탄환, 탄피, 탄창 오브젝트 생성은 <c>PoolManager</c>를 통해 수행합니다.
/// 사격 입력 자체는 외부 컨트롤러에서 판단하고, 이 컴포넌트는 <see cref="TryShoot"/> 호출을 통해 사격 가능 여부와 발사 처리를 담당합니다.
/// </remarks>
public class Gun : MonoBehaviour, IBalancePostProcess, ISharedBalanceReceiver
{
    /// <summary>탄약 수가 바뀔 때 (현재 탄약, 최대 탄창) 순서로 알립니다.</summary>
    /// <remarks>UI 갱신용입니다. 사격·재장전·밸런스 재적용 모두 이 이벤트를 거칩니다.</remarks>
    public event System.Action<int, int> OnBulletChanged;

    [Foldout("Balance Data")]
    [Tooltip("이 무기에 적용할 순수 수치형 밸런스 SO입니다. 비어 있으면 기존 Inspector 값을 사용합니다.")]
    [FormerlySerializedAs("m_balance")]
    [SerializeField] private GunBalanceSO m_balanceSO;

    // 피드백 SO 슬롯은 WeaponFeedbackEmitter가 소유합니다.
    // 재생을 담당하는 쪽이 재생할 리소스를 가져야 같은 타입의 리소스 여러 개를 필드 이름으로 구분할 수 있습니다.

    /// <summary>
    /// 조준 중 계산된 총구 기준 히트스캔 사격 정보를 담습니다.
    /// </summary>
    /// <remarks>
    /// <see cref="AimController"/>가 조준 프레임에서 한 번 계산하고, 마커 표시와 사격 처리에서 같은 값을 사용합니다.
    /// </remarks>
    public struct HitscanShotInfo
    {
        /// <summary>이 정보가 계산되어 사용할 수 있는 상태인지 여부입니다.</summary>
        public bool IsValid;

        /// <summary>사거리 안에서 무언가를 맞혔는지 여부입니다.</summary>
        public bool HasHit;

        /// <summary>총구와 조준점 사이가 막혀 조준점까지 탄이 가지 못하는 상태인지 여부입니다.</summary>
        public bool IsObstructed;

        /// <summary>탄이 출발하는 총구 위치입니다.</summary>
        public Vector3 Origin;

        /// <summary>총구에서 나가는 실제 발사 방향입니다.</summary>
        public Vector3 Direction;

        /// <summary>카메라 기준으로 플레이어가 겨눈 지점입니다. 총구 기준 탄착점과 다를 수 있습니다.</summary>
        public Vector3 AimPoint;

        /// <summary>탄이 실제로 도달한 지점입니다. 아무것도 맞히지 않으면 사거리 끝입니다.</summary>
        public Vector3 EndPoint;

        /// <summary>맞힌 대상의 레이캐스트 결과입니다. <see cref="HasHit"/>가 <c>true</c>일 때만 의미가 있습니다.</summary>
        public RaycastHit Hit;

        /// <summary>이 정보를 계산한 프레임 번호입니다. 같은 프레임에서 다시 계산하지 않기 위한 기준입니다.</summary>
        public int FrameCount;
    }

    // SpreadDistribution, KickSidePattern은 밸런스 SO와 같은 타입을 공유해야 하므로
    // Assets/1.Scripts/Enum/WeaponEnum.cs로 옮겼습니다.

    [Foldout("Bullet Options")]
    [Tooltip("현재 탄약 수입니다.")]
    [FormerlySerializedAs("currentBullet")]
    [SerializeField] private int m_currentBullet = 30;

    [Tooltip("최대 탄약 수입니다.")]
    [FormerlySerializedAs("maxBullet")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private int m_maxBullet = 30;

    [Tooltip("사격 후 다음 사격이 가능해질 때까지의 지연 시간입니다.")]
    [FormerlySerializedAs("shootDelay")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_shootDelay = 0.12f;

    [Tooltip("재장전에 걸리는 시간(초)입니다. 이 값이 정본이며 탄약 충전·조준선 게이지·재장전 애니메이션 배속이 모두 여기에 맞춰집니다. 애니메이션은 완료 이벤트가 이 시간에 오도록 자동으로 배속됩니다(예: 1배속 클립이 2.67초면 1.33을 넣으면 2배속). 줄이면 빨라지고 늘리면 느려집니다.")]
    [FormerlySerializedAs("reloadTime")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_reloadTime = 1.5f;

    [Tooltip("탄약이 최대치일 때도 재장전을 허용할지 여부입니다. 기본값은 false로, 풀 탄창에서는 재장전이 막히고 빈 장전 사운드만 재생됩니다. 디버그 용도로만 true로 켭니다.")]
    [SerializeField] private bool m_allowFullMagReload = false;

    [Foldout("Spawn Points")]
    [Tooltip("탄환이 생성될 위치입니다.")]
    [FormerlySerializedAs("firePos")]
    [SerializeField] private Transform m_firePos;

    [Tooltip("탄피가 생성될 위치입니다.")]
    [FormerlySerializedAs("shellPos")]
    [SerializeField] private Transform m_shellPos;

    [Tooltip("탄창이 떨어질 위치입니다.")]
    [FormerlySerializedAs("clipPos")]
    [SerializeField] private Transform m_clipPos;

    [Tooltip("머즐 플래시 위치입니다. 파티클 방식 사용 시 보조 참조로 사용합니다.")]
    [FormerlySerializedAs("muzzleFlashPos")]
    [SerializeField] private Transform m_muzzleFlashPos;

    [Foldout("Pool Index")]
    [Tooltip("탄환 오브젝트 풀 인덱스입니다.")]
    [FormerlySerializedAs("bulletPoolIndex")]
    [SerializeField] private int m_bulletPoolIndex = 0;

    [Tooltip("탄피 오브젝트 풀 인덱스입니다.")]
    [FormerlySerializedAs("shellPoolIndex")]
    [SerializeField] private int m_shellPoolIndex = 1;

    [Tooltip("탄창 오브젝트 풀 인덱스입니다.")]
    [FormerlySerializedAs("clipPoolIndex")]
    [SerializeField] private int m_clipPoolIndex = 2;

    [Tooltip("머즐 플래시 오브젝트 풀 인덱스입니다. 현재 파티클 방식에서는 사용하지 않습니다.")]
    [FormerlySerializedAs("muzzleFlashPoolIndex")]
    [SerializeField] private int m_muzzleFlashPoolIndex = 3;

    [Foldout("UI Options")]
    [Tooltip("현재 탄약 수를 표시할 UI 텍스트입니다.")]
    [FormerlySerializedAs("bulletText")]
    [SerializeField] private Text m_bulletText;

    [Foldout("Audio Options")]
    [Tooltip("무기 효과음을 재생할 AudioSource입니다. 비어 있으면 같은 GameObject에서 자동 탐색합니다.")]
    [FormerlySerializedAs("audioSource")]
    [SerializeField] private AudioSource m_audioSource;

    [Tooltip("사격 효과음입니다.")]
    [FormerlySerializedAs("shootClip")]
    [SerializeField] private AudioClip m_shootClip;

    [Tooltip("재장전 효과음입니다.")]
    [FormerlySerializedAs("reloadClip")]
    [SerializeField] private AudioClip m_reloadClip;

    [Tooltip("풀 탄창 등으로 재장전이 막힐 때 재생할 빈 장전(드라이) 효과음입니다. 비워두면 무음입니다.")]
    [HideIf("m_allowFullMagReload")]
    [SerializeField] private AudioClip m_emptyReloadClip;

    [Foldout("Effect Options")]
    [EndIf]
    [Tooltip("사격 시 재생할 머즐 플래시 파티클입니다.")]
    [FormerlySerializedAs("muzzleFlashParticle")]
    [SerializeField] private ParticleSystem m_muzzleFlashParticle;

    [Foldout("Hitscan Options")]
    [Tooltip("명중 한 발이 주는 기본 피해량입니다. 약점 배율과 거리 감쇠는 여기에 곱해집니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private int m_hitscanDamage = 1;

    [Tooltip("이 무기가 약점 판정을 사용하는지 여부입니다. 끄면 약점 부위를 맞혀도 일반 피해로 처리하고 약점 표시도 뜨지 않습니다.")]
    [BalanceField]
    [SerializeField] private bool m_allowHeadshot = true;

    [Tooltip("약점 부위를 맞혔을 때 곱하는 피해 배율입니다. 약점 판정이 꺼져 있으면 사용하지 않습니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_headshotDamageMultiplier = 2.0f;

    [Tooltip("탄이 도달하는 최대 사거리(m)입니다. 이 거리를 넘어가면 아무것도 맞히지 않습니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_hitscanRange = 100.0f;

    [Tooltip("명중 시 대상을 밀어내는 넉백 충격량(N·s)입니다. 사망한 대상은 래그돌이 이 힘을 받습니다. 0이면 피격 대상이 정한 최소치만 적용됩니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_knockbackImpulse = 0.0f;

    [Tooltip("명중 시 적의 경직력 누적에 더하는 저지력입니다. 피해·넉백과는 별개이며, 적의 경직 한계치에 닿으면 경직이 발동합니다. 경직이 없는 대상에게는 무시됩니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_stoppingPower = 0.0f;

    [Tooltip("사격 판정이 걸릴 레이어입니다. 기본값은 전부이며, 여기서 제외한 레이어는 탄이 그대로 통과합니다.")]
    [SerializeField] private LayerMask m_hitscanLayerMask = ~0;

    [Tooltip("부위 히트박스를 켤 대상을 찾는 1차 탐지 레이어입니다. HitDetectVolume의 EnemyHitDetect 레이어를 지정합니다. 비우면 부위 히트박스를 열 수 없어 해당 대상은 사격 피해를 받지 않습니다.")]
    [FormerlySerializedAs("m_bodyDetectLayerMask")]
    [SerializeField] private LayerMask m_hitDetectLayerMask = 0;

    [Tooltip("탄이 아군 유닛의 몸을 통과할지 여부입니다. 끄면 앞을 막고 선 팀원이 탄을 막습니다.")]
    [SerializeField] private bool m_allyBulletPassThrough = true;

    [Foldout("Spread Options")]
    [Header("Hipfire")]
    [Tooltip("힙파이어(비조준) 시 최소 방사각(도).")]
    [FormerlySerializedAs("m_hipfireBaseSpread")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_hipfireMinSpread = 4.0f;

    [Tooltip("힙파이어(비조준) 시 최대 방사각(도). 연사 누적값은 이 값을 넘지 않습니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_hipfireMaxSpread = 10.0f;

    [Tooltip("현재 적용 중인 힙파이어 방사각(도)입니다. 런타임 관찰용이며 직접 편집하는 값이 아닙니다.")]
    [ReadOnly][SerializeField] private float m_hipfireCurrentSpread;

    [Tooltip("힙파이어에서 이 발수까지는 최소 방사각을 유지하고 연사 증가값을 누적하지 않습니다.")]
    [FormerlySerializedAs("m_accurateShotCount")]
    [FormerlySerializedAs("m_minSpreadShotCount")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private int m_hipfireMinSpreadShotCount = 3;

    [Tooltip("힙파이어 발사마다 누적되는 방사각 증가량(도).")]
    [FormerlySerializedAs("m_bloomPerShot")]
    [FormerlySerializedAs("m_spreadIncreasePerShot")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_hipfireSpreadIncreasePerShot = 1.0f;

    [Tooltip("힙파이어 사격을 멈춘 뒤 초당 회복(감소)하는 방사각(도/초).")]
    [FormerlySerializedAs("m_bloomRecovery")]
    [FormerlySerializedAs("m_spreadRecoveryPerSecond")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_hipfireSpreadRecoveryPerSecond = 8.0f;

    [Tooltip("발사 입력을 놓은 뒤 이 시간(초)이 지나면 힙파이어 탄퍼짐 회복을 시작하고 연사 발수 카운트를 리셋합니다. 입력을 유지하는 동안에는 회복하지 않습니다.")]
    [FormerlySerializedAs("m_spreadResetTime")]
    [FormerlySerializedAs("m_spreadRecoveryDelay")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_hipfireSpreadRecoveryDelay = 0.3f;

    [Header("ADS")]
    [Tooltip("ADS(조준) 시 최소 방사각(도). 0이면 정밀 사격입니다.")]
    [FormerlySerializedAs("m_adsBaseSpread")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_adsMinSpread = 0.0f;

    [Tooltip("ADS(조준) 시 최대 방사각(도). 연사 누적값은 이 값을 넘지 않습니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_adsMaxSpread = 6.0f;

    [Tooltip("현재 적용 중인 ADS 방사각(도)입니다. 런타임 관찰용이며 직접 편집하는 값이 아닙니다.")]
    [ReadOnly][SerializeField] private float m_adsCurrentSpread;

    [Tooltip("ADS에서 이 발수까지는 최소 방사각을 유지하고 연사 증가값을 누적하지 않습니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private int m_adsMinSpreadShotCount = 3;

    [Tooltip("ADS 발사마다 누적되는 방사각 증가량(도).")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_adsSpreadIncreasePerShot = 1.0f;

    [Tooltip("ADS 사격을 멈춘 뒤 초당 회복(감소)하는 방사각(도/초).")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_adsSpreadRecoveryPerSecond = 8.0f;

    [Tooltip("발사 입력을 놓은 뒤 이 시간(초)이 지나면 ADS 탄퍼짐 회복을 시작하고 연사 발수 카운트를 리셋합니다. 입력을 유지하는 동안에는 회복하지 않습니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_adsSpreadRecoveryDelay = 0.3f;

    [Header("Distribution")]
    [Tooltip("탄퍼짐 분포 방식입니다. Uniform=원판 전체 균일, Gaussian=중심 가중 정규분포(기본). 샷건(콘 3분할) 등은 추후 추가 예정입니다.")]
    [BalanceField]
    [SerializeField] private SpreadDistribution m_spreadDistribution = SpreadDistribution.Gaussian;

    [Tooltip("Gaussian 분포의 중심 집중도입니다. 콘 반각(최대 방사각)을 몇 σ로 볼지 정합니다. 값이 클수록 탄이 중심에 더 몰립니다(기본 3 = 약 99%가 콘 안, 평균 편향은 반각의 약 0.42배). 낮출수록 가장자리로 퍼집니다. Uniform에는 영향이 없습니다.")]
    [BalanceField]
    [Clamp(Min = 1)]
    [SerializeField] private float m_spreadConcentration = 3.0f;

    [Header("Noise")]
    [Tooltip("사격 1회의 기본 소음량입니다. 가청 여부가 아니라, 변이체가 여러 소음 중 어느 것을 추적할지 비교할 때만 쓰입니다. 기획 미확정 - 임시값입니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_shotNoiseLevel = 1.0f;

    [Tooltip("사격 소음이 들리는 거리(m)입니다. 이 거리 안이면 들리고 밖이면 들리지 않습니다. 벽이나 엄폐물에 의한 감쇠는 적용하지 않습니다. 변이체 시야(12m)보다 훨씬 커야 소음 유인 전술이 성립합니다. 기획 미확정 - 임시값입니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_shotNoiseRange = 40.0f;

    [Foldout("Recoil Options")]
    [Header("Aim Recoil (탄착에 영향)")]
    [Tooltip("발사 1회당 세로(피치) 반동 각도(도)입니다. 양수면 조준이 위로 솟습니다(머즐 클라임). 실제 조준을 밀어 탄착에도 영향을 주며(LogicalAim), 사격을 멈추면 자동 회복됩니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_recoilPitchKick = 0.6f;

    [Tooltip("발사 1회당 좌우(요) 반동 각도(도)의 크기입니다. 실제 조준을 밀어 탄착에도 영향을 줍니다. 0이면 좌우 반동이 없습니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_recoilYawKick = 0.2f;

    [Tooltip("좌우 반동(Yaw)의 방향 패턴입니다. Random=매 발 ±범위 무작위, AlternateLeftFirst=좌·우 번갈아(첫 발 왼쪽), AlternateRightFirst=우·좌 번갈아(첫 발 오른쪽). Alternate는 위 크기를 그대로 좌우로 씁니다.")]
    [BalanceField]
    [SerializeField] private KickSidePattern m_yawKickPattern = KickSidePattern.Random;

    [Header("Visual Kick (에임 무영향, juice)")]
    [Tooltip("발사 1회당 카메라 롤(Dutch) 크기(도)입니다. 화면만 살짝 기울입니다. 조준/탄착에는 영향이 없습니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_recoilRoll = 0.5f;

    [Tooltip("카메라 롤(Dutch)의 방향 패턴입니다. Random=매 발 ±범위 무작위, AlternateLeftFirst=좌·우 번갈아(첫 발 왼쪽), AlternateRightFirst=우·좌 번갈아(첫 발 오른쪽). Yaw 반동과 독립적으로 설정됩니다.")]
    [BalanceField]
    [SerializeField] private KickSidePattern m_rollKickPattern = KickSidePattern.Random;

    [Tooltip("발사 1회당 카메라 FOV 펀치(도)입니다. 순간적으로 시야가 벌어졌다 회복되는 시각 반동 연출입니다. 조준/탄착에는 영향이 없습니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_recoilFovPunch = 1.0f;

    [Tooltip("조준(ADS) 중 발사 1회당 카메라 FOV 펀치(도)입니다. 조준 중에는 화면이 확대돼 같은 값도 더 크게 보이므로 힙파이어와 따로 둡니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_recoilFovPunchAds = 1.0f;

    [Tooltip("거리에 따른 피해 배율 구간표입니다. 구간을 두지 않으면 거리와 무관하게 기본 피해가 들어갑니다.")]
    [BalanceField]
    [SerializeField] private DamageFalloffTable m_damageFalloff = new DamageFalloffTable();

    [Tooltip("한 발이 유닛을 몇 번 꿰뚫는지와 꿰뚫을 때마다의 피해 감쇠 구간표입니다. 기본값은 관통 없음입니다. 벽과 지형은 관통하지 않습니다.")]
    [BalanceField]
    [SerializeField] private PenetrationTable m_penetration = new PenetrationTable();

#if UNITY_EDITOR
    [Foldout("Debug")]
    [Tooltip("Editor-only SpreadDebug console log. Calls are stripped from Player builds.")]
    [SerializeField] private bool m_debugLogSpread = false;
#endif

    [Foldout("Debug")]
    [Tooltip("이 무기를 선택했을 때 총구 전방에 거리별 피해 감쇠 구간을 Scene 뷰에 표시합니다. 구간 경계마다 고리를 그리고 구간별로 색이 달라집니다.")]
    [SerializeField] private bool m_debugDrawDamageFalloff = false;

    [Tooltip("감쇠 구간 경계에 그리는 고리의 반지름(m)입니다. 표시 크기일 뿐 판정과는 무관합니다.")]
    [Clamp(Min = 0.01f)]
    [SerializeField] private float m_debugFalloffRingRadius = 0.35f;

    [Tooltip("이 무기를 선택했을 때 사격 소음의 도달 반경을 Scene 뷰에 원으로 표시합니다. 이 원 안의 변이체가 총성을 듣습니다.")]
    [SerializeField] private bool m_debugDrawShotNoiseRange = false;

    [Tooltip("2단계 사격 판정의 각 단계를 콘솔에 남깁니다. 1차(HitDetectVolume 감지) / 2차(부위 히트박스 켬) / 3차(레이 재발사와 결과) 순으로 찍힙니다. 판정이 어디서 끊기는지 확인할 때만 켭니다.")]
    [SerializeField] private bool m_debugLogTwoStage = false;

    // 무한 장탄수는 플레이테스트 트레이너에서도 쓰기 위해 빌드에도 컴파일하고,
    // 실제 효과는 런타임 트레이너가 활성화된 Editor/Development Build에서만 동작합니다.
    [Foldout("Debug")]
    [Tooltip("켜면 사격해도 현재 장전된 탄약이 줄지 않습니다(무한 장탄수). 개발 모드에서만 효과가 있습니다.")]
    [SerializeField] private bool m_debugInfiniteMagazine = false;

    /// <summary>무한 장탄수 디버그 플래그입니다. 디버그 트레이너에서 사용하며 개발 모드에서만 효과가 있습니다.</summary>
    public bool DebugInfiniteMagazine
    {
        get => m_debugInfiniteMagazine;
        set => m_debugInfiniteMagazine = value;
    }

    /// <summary>무한 장탄수가 실제로 적용되는 상태인지 여부입니다. 개발 모드에서 플래그가 켜졌을 때만 <c>true</c>입니다.</summary>
    private bool IsInfiniteMagazineDebugActive => m_debugInfiniteMagazine && GameDevMode.DebugFeaturesEnabled;

    /// <summary>총구 기준 탄착점이 조준점과 같다고 볼 허용 오차(m)입니다.</summary>
    /// <remarks>
    /// 총구와 카메라의 위치가 다르므로 두 지점은 완전히 일치하지 않습니다.
    /// 이 오차 안이면 "겨눈 곳에 맞는다"고 보고 조준 보조 표시를 바꾸지 않습니다.
    /// </remarks>
    public const float HitscanAimTolerance = 0.05f;

    private bool m_canShoot = true;
    private bool m_isReloading;
    private float m_reloadStartTime;
    private float m_nextDryFireTime;
    private bool m_hasRequiredReferences;
    private Faction m_ownerFaction = Faction.Player;

    /// <summary>사격 트레이스 결과를 재사용하는 버퍼입니다.</summary>
    /// <remarks>
    /// 아군 통과 판정은 경로 위의 충돌을 여러 개 봐야 하는데, 매번 배열을 새로 만들면 자동 사격 중에
    /// 발사 간격마다 쓰레기가 쌓입니다. 무기마다 하나씩 들고 재사용합니다.
    ///
    /// 크기는 "한 사격선 위에 겹칠 수 있는 콜라이더 수"를 넉넉히 잡은 값입니다. 팀원 3인의 히트박스가
    /// 여러 개씩 겹쳐도 남도록 두었고, 넘치면 <see cref="TryResolveNearestBlocking"/>이 경고를 남깁니다.
    /// </remarks>
    private readonly RaycastHit[] m_traceBuffer = new RaycastHit[TraceBufferSize];

    /// <summary>사격 트레이스 버퍼 크기입니다.</summary>
    private const int TraceBufferSize = 24;

    /// <summary>탄을 막는 충돌만 거리 오름차순으로 모아 두는 목록입니다. 재사용합니다.</summary>
    private readonly List<RaycastHit> m_blockingHits = new List<RaycastHit>(TraceBufferSize);

    /// <summary>이번 사격이 피해를 줄 대상을 가까운 순서대로 담은 목록입니다. 재사용합니다.</summary>
    private readonly List<RaycastHit> m_shotPath = new List<RaycastHit>(TraceBufferSize);

    /// <summary>탄이 최종적으로 박힌 지형 충돌입니다. 지형에서 멈추지 않았으면 유효하지 않습니다.</summary>
    /// <remarks>
    /// 표면 피드백(탄흔)은 첫 충돌이 아니라 여기에 남겨야 합니다. 관통이 켜지면 첫 충돌은 꿰뚫고 지나간
    /// 적이고, 탄이 실제로 멈추는 곳은 그 뒤의 벽입니다.
    /// </remarks>
    private RaycastHit m_surfaceImpact;

    /// <summary>이번 사격이 지형에서 멈췄는지 여부입니다.</summary>
    private bool m_hasSurfaceImpact;

    /// <summary>이 무기를 소유한 유닛입니다. 피격자가 반격 대상을 알 수 있도록 피해와 함께 전달합니다.</summary>
    private GameObject m_ownerObject;

    /// <summary>사격 소음을 발신할 캐릭터 쪽 컴포넌트입니다. 없으면 소음을 내지 않습니다.</summary>
    private CharacterNoiseEmitter m_noiseEmitter;

    /// <summary>무기 사운드와 시각 피드백의 출력 컴포넌트입니다.</summary>
    private WeaponFeedbackEmitter m_feedbackEmitter;
    private float m_hipfireCurrentSpreadAdd;
    private float m_adsCurrentSpreadAdd;

    /// <summary>발사 입력 홀드로 spread 회복을 막을 마지막 프레임입니다. AimController가 매 프레임 연장합니다.</summary>
    private int m_spreadRecoveryBlockUntilFrame = -1;
    private int m_hipfireShotsInBurst;
    private int m_adsShotsInBurst;
    private float m_hipfireLastShotTime;
    private float m_adsLastShotTime;

    // 시트 값이 뒤집혔을 때 되돌릴 기준입니다. 프리팹마다 튜닝이 달라 클래스 공용 상수를 쓸 수 없으므로,
    // 첫 바인딩 직전에 이 프리팹이 저작한 값을 그대로 보관합니다.
    private bool m_fallbackRangesCaptured;
    private float m_fallbackHipfireMinSpread;
    private float m_fallbackHipfireMaxSpread;
    private float m_fallbackAdsMinSpread;
    private float m_fallbackAdsMaxSpread;

    /// <summary>현재 탄약 수입니다.</summary>
    public int CurrentBullet => m_currentBullet;

    /// <summary>현재 이 무기에 지정된 순수 수치형 밸런스 SO입니다.</summary>
    public GunBalanceSO Balance => m_balanceSO;

    /// <summary>이 무기의 표현 피드백을 담당하는 컴포넌트입니다. 붙어 있지 않으면 <c>null</c>입니다.</summary>
    public WeaponFeedbackEmitter FeedbackEmitter => ResolveFeedbackEmitter();

    /// <summary>최대 탄약 수입니다.</summary>
    public int MaxBullet => m_maxBullet;

    /// <summary>현재 사격 가능한 상태인지 여부입니다.</summary>
    public bool CanShoot => m_canShoot;

    /// <summary>
    /// 히트스캔 피격이 확정되어 피해가 적용됐을 때 발생합니다. 인자는 헤드샷·킬 여부를 담은 피드백입니다.
    /// </summary>
    /// <remarks>조준선 히트마커/킬 표시가 구독합니다.</remarks>
    public event System.Action<CombatDamage.HitFeedback> OnHitFeedback;

    /// <summary>
    /// 재장전이 완료돼 탄약이 채워졌을 때 발생합니다.
    /// </summary>
    /// <remarks>
    /// 재장전 종료를 알리는 정규 경로는 재장전 클립의 애니메이션 이벤트(<see cref="AimController.Reload"/>)입니다.
    /// 다만 애니메이션 이벤트는 꺼진 컴포넌트에는 오지 않습니다. 장전 도중 다른 대원으로 전환하면
    /// <see cref="SquadMemberController"/>가 그 대원의 <see cref="AimController"/>를 끄므로 이벤트가 유실되고,
    /// 애니메이터의 <c>IsReload</c>가 내려가지 않아 재장전 스테이트에서 영영 빠져나오지 못합니다.
    /// 탄약 충전을 실제로 확정하는 것은 이쪽 타이머이므로, 그 시점을 알려 비주얼 정리의 대체 경로를 둡니다.
    /// </remarks>
    public event System.Action OnReloadCompleted;

    /// <summary>현재 재장전 중인지 여부입니다.</summary>
    public bool IsReloading => m_isReloading;

    /// <summary>현재 재장전 진행도(0~1)입니다. 재장전 중이 아니면 0입니다.</summary>
    public float ReloadProgress => !m_isReloading ? 0.0f
        : m_reloadTime <= 0.0f ? 1.0f
        : Mathf.Clamp01((Time.time - m_reloadStartTime) / m_reloadTime);

    /// <summary>
    /// 지금 재장전을 시작할 수 있는 상태인지 여부입니다.
    /// </summary>
    /// <remarks>필수 참조를 갖추고, 재장전 중이 아니며, 탄약이 최대치 미만일 때만 <c>true</c>입니다. 풀 탄창 재장전 진입을 막는 데 사용합니다. <see cref="AllowFullMagReload"/>가 켜져 있으면 풀 탄창에서도 <c>true</c>입니다.</remarks>
    public bool CanReload => m_hasRequiredReferences && !m_isReloading && (m_allowFullMagReload || m_currentBullet < m_maxBullet);

    /// <summary>탄약이 최대치일 때도 재장전을 허용할지 여부입니다. 기본값은 <c>false</c>이며 디버그 용도입니다.</summary>
    public bool AllowFullMagReload => m_allowFullMagReload;

    /// <summary>사격 지연 시간입니다.</summary>
    public float ShootDelay => m_shootDelay;

    /// <summary>재장전 시간입니다.</summary>
    public float ReloadTime => m_reloadTime;

    /// <summary>탄환 생성 위치입니다.</summary>
    public Transform FirePos => m_firePos;

    /// <summary>탄피 생성 위치입니다.</summary>
    public Transform ShellPos => m_shellPos;

    /// <summary>탄창 생성 위치입니다.</summary>
    public Transform ClipPos => m_clipPos;

    /// <summary>머즐 플래시 위치입니다.</summary>
    public Transform MuzzleFlashPos => m_muzzleFlashPos;

    /// <summary>탄약 UI 텍스트입니다.</summary>
    public Text BulletText => m_bulletText;

    /// <summary>히트스캔 판정에 사용할 최대 사거리입니다.</summary>
    public float HitscanRange => m_hitscanRange;

    /// <summary>히트스캔 사격이 적에게 적용할 피해량입니다.</summary>
    public int HitscanDamage => m_hitscanDamage;

    /// <summary>이 무기가 약점 판정을 사용하는지 여부입니다.</summary>
    public bool AllowHeadshot => m_allowHeadshot;

    /// <summary>헤드샷 히트박스에 명중했을 때 이 무기가 적용할 피해 배율입니다.</summary>
    public float HeadshotDamageMultiplier => m_headshotDamageMultiplier;

    /// <summary>히트스캔 레이캐스트가 충돌 검사할 레이어 마스크입니다.</summary>
    public LayerMask HitscanLayerMask => m_hitscanLayerMask;

    /// <summary>부위별 히트박스를 열 후보를 찾는 1차 감지 레이어 마스크입니다.</summary>
    public LayerMask HitDetectLayerMask => m_hitDetectLayerMask;

    /// <summary>탄이 아군 유닛의 몸을 통과하는지 여부입니다.</summary>
    public bool AllyBulletPassThrough => m_allyBulletPassThrough;

    /// <summary>이 무기를 든 유닛의 진영입니다.</summary>
    public Faction OwnerFaction => m_ownerFaction;

    /// <summary>발사 1회당 세로(피치) 반동 각도(도)입니다. 실제 조준을 밀어 탄착에도 영향을 줍니다.</summary>
    public float RecoilPitchKick => m_recoilPitchKick;

    /// <summary>발사 1회당 좌우(요) 반동 각도(도)의 최대 크기입니다. 실제 조준을 밀어 탄착에도 영향을 줍니다.</summary>
    public float RecoilYawKick => m_recoilYawKick;

    /// <summary>발사 1회당 카메라 롤(Dutch) 최대 크기(도)입니다. 시각 전용 juice이며 조준/탄착에는 영향이 없습니다.</summary>
    public float RecoilRoll => m_recoilRoll;

    /// <summary>발사 1회당 카메라 FOV 펀치(도)입니다. 시각 전용 juice이며 조준/탄착에는 영향이 없습니다.</summary>
    public float RecoilFovPunch => m_recoilFovPunch;

    /// <summary>조준 중 발사 1회당 카메라 FOV 펀치(도)입니다.</summary>
    public float RecoilFovPunchAds => m_recoilFovPunchAds;

    /// <summary>거리에 따른 피해 배율 구간표입니다.</summary>
    public DamageFalloffTable DamageFalloff => m_damageFalloff;

    /// <summary>좌우 반동(Yaw)의 좌우 방향 패턴입니다.</summary>
    public KickSidePattern YawKickPattern => m_yawKickPattern;

    /// <summary>시각 롤(Dutch)의 좌우 방향 패턴입니다. Yaw 반동과 독립입니다.</summary>
    public KickSidePattern RollKickPattern => m_rollKickPattern;

    /// <summary>
    /// 현재 모드의 표시용 탄퍼짐 방사각(도)을 반환합니다.
    /// </summary>
    /// <param name="isAds">ADS면 true, 힙파이어면 false입니다.</param>
    /// <returns>사격 상태를 변경하지 않고 계산한 현재 탄퍼짐 방사각(도)입니다.</returns>
    public float GetCurrentSpread(bool isAds)
    {
        return isAds
            ? GetCurrentSpread(m_adsMinSpread, m_adsMaxSpread, m_adsCurrentSpreadAdd)
            : GetCurrentSpread(m_hipfireMinSpread, m_hipfireMaxSpread, m_hipfireCurrentSpreadAdd);
    }

    /// <summary>현재 사격 자세의 방사각 최소·최대값을 함께 반환합니다.</summary>
    /// <param name="isAds">ADS면 <c>true</c>, 힙파이어면 <c>false</c>입니다.</param>
    /// <param name="minSpread">해당 자세의 최소 방사각(도)입니다.</param>
    /// <param name="maxSpread">해당 자세의 최대 방사각(도)입니다. 최소값보다 작아지지 않습니다.</param>
    /// <remarks>크로스헤어가 벌어짐 폭을 그릴 때 두 값이 함께 필요해 한 번에 돌려줍니다.</remarks>
    public void GetSpreadRange(bool isAds, out float minSpread, out float maxSpread)
    {
        minSpread = Mathf.Max(0.0f, isAds ? m_adsMinSpread : m_hipfireMinSpread);
        maxSpread = Mathf.Max(minSpread, isAds ? m_adsMaxSpread : m_hipfireMaxSpread);
    }

    /// <summary>탄퍼짐 콘 안에서의 분포 방식입니다. 크로스헤어가 표시 배율을 계산할 때 읽습니다(읽기 전용).</summary>
    public SpreadDistribution Distribution => m_spreadDistribution;

    /// <summary>Gaussian 분포의 중심 집중도(σ=1/이 값)입니다. 크로스헤어가 표시 배율을 계산할 때 읽습니다(읽기 전용, 최소 1).</summary>
    public float SpreadConcentration => m_spreadConcentration;

    /// <summary>힙파이어에서 최소 탄퍼짐을 유지하는 연속 발사 수입니다.</summary>
    public int HipfireMinSpreadShotCount => m_hipfireMinSpreadShotCount;

    /// <summary>힙파이어 연사 시 발마다 누적하는 탄퍼짐 각도입니다.</summary>
    public float HipfireSpreadIncreasePerShot => m_hipfireSpreadIncreasePerShot;

    /// <summary>발사 입력을 놓은 뒤 힙파이어 탄퍼짐 회복을 시작하기까지의 지연 시간입니다.</summary>
    public float HipfireSpreadRecoveryDelay => m_hipfireSpreadRecoveryDelay;

    /// <summary>힙파이어 탄퍼짐의 초당 회복량입니다.</summary>
    public float HipfireSpreadRecoveryPerSecond => m_hipfireSpreadRecoveryPerSecond;

    /// <summary>ADS에서 최소 탄퍼짐을 유지하는 연속 발사 수입니다.</summary>
    public int AdsMinSpreadShotCount => m_adsMinSpreadShotCount;

    /// <summary>ADS 연사 시 발마다 누적하는 탄퍼짐 각도입니다.</summary>
    public float AdsSpreadIncreasePerShot => m_adsSpreadIncreasePerShot;

    /// <summary>발사 입력을 놓은 뒤 ADS 탄퍼짐 회복을 시작하기까지의 지연 시간입니다.</summary>
    public float AdsSpreadRecoveryDelay => m_adsSpreadRecoveryDelay;

    /// <summary>ADS 탄퍼짐의 초당 회복량입니다.</summary>
    public float AdsSpreadRecoveryPerSecond => m_adsSpreadRecoveryPerSecond;

    /// <summary>
    /// 현재 무기의 spread 회복을 발사 입력 홀드로 막을지 통지합니다.
    /// </summary>
    /// <remarks>
    /// 입력은 무기가 아닌 <see cref="AimController"/>가 소유합니다. 호출 순서가 Gun.Update보다 늦어도
    /// 다음 프레임까지 막도록 한 프레임의 여유를 둡니다. 짧은 클릭은 버튼을 놓은 직후 이 차단이 해제되어
    /// 기존 유예 시간 뒤 일반 회복으로 이어집니다.
    /// </remarks>
    /// <param name="fireInputHeld">발사 입력을 계속 누르고 있으면 <c>true</c>입니다.</param>
    public void SetSpreadRecoveryBlockedByHeldFireInput(bool fireInputHeld)
    {
        m_spreadRecoveryBlockUntilFrame = fireInputHeld ? Time.frameCount + 1 : -1;
    }

    /// <summary>
    /// 컴포넌트 참조를 캐싱하고 필수 참조를 검증합니다.
    /// </summary>
    private void Awake()
    {
        CacheReferences();

        if (!ValidateRequiredReferences())
        {
            enabled = false;
            return;
        }

        m_hasRequiredReferences = true;
        BindConfiguredBalance();
    }

    /// <summary>
    /// 초기 탄약 UI를 갱신합니다.
    /// </summary>
    private void Start()
    {
        if (!m_hasRequiredReferences)
        {
            return;
        }

        ClampBulletValues();
        UpdateCurrentSpreadInspectorFields();
        UpdateBulletUI();
    }

    /// <summary>
    /// 비활성화 중 취소된 예약 때문에 굳어버린 사격·재장전 상태를 되돌립니다.
    /// </summary>
    /// <remarks>
    /// <see cref="OnDisable"/>이 <see cref="ResetShoot"/>·<see cref="CompleteReload"/> 예약을 취소하는데,
    /// 두 플래그를 되돌리는 곳은 그 예약뿐입니다. 그래서 복구가 없으면 <b>사격 직후 비활성화된 총은
    /// <c>m_canShoot=false</c>로 굳어 다시 켜도 영영 발사되지 않습니다</b>(재장전 중이었다면 같은 이유로
    /// <c>m_isReloading=true</c>에 갇힙니다). Play Mode에서 실제로 발생한 상태이므로 가정이 아닙니다.
    /// 재장전은 완료 예약이 사라졌으니 중단으로 처리합니다. 탄약은 채우지 않으며 다시 재장전해야 합니다.
    /// </remarks>
    private void OnEnable()
    {
        m_canShoot = true;
        m_isReloading = false;

        if (m_hasRequiredReferences)
        {
            UpdateBulletUI();
        }
    }

    /// <summary>
    /// 예약된 사격 쿨다운 호출을 정리합니다.
    /// </summary>
    /// <remarks>여기서 취소한 예약은 <see cref="OnEnable"/>이 상태를 되돌려 보상합니다. 둘은 짝입니다.</remarks>
    private void OnDisable()
    {
        CancelInvoke(nameof(ResetShoot));
        CancelInvoke(nameof(CompleteReload));
    }

    /// <summary>
    /// 사격을 멈춘 뒤 일정 시간이 지나면 누적된 탄퍼짐 증가값을 회복합니다.
    /// </summary>
    private void Update()
    {
        // AimController가 발사 입력을 유지하는 프레임을 통지하면 spread 회복을 건너뜁니다.
        // 한 프레임 앞까지 유지하는 것은 Gun.Update와 AimController.Update 실행 순서가 바뀌어도 홀드 중
        // 한 프레임만 회복됐다 다시 벌어지는 현상을 막기 위함입니다.
        if (Time.frameCount > m_spreadRecoveryBlockUntilFrame)
        {
            RecoverSpread(ref m_hipfireCurrentSpreadAdd, m_hipfireLastShotTime, m_hipfireSpreadRecoveryDelay, m_hipfireSpreadRecoveryPerSecond);
            RecoverSpread(ref m_adsCurrentSpreadAdd, m_adsLastShotTime, m_adsSpreadRecoveryDelay, m_adsSpreadRecoveryPerSecond);
        }
        UpdateCurrentSpreadInspectorFields();
    }

    /// <summary>
    /// 자동으로 찾을 수 있는 내부 참조를 캐싱합니다.
    /// </summary>
    private void CacheReferences()
    {
        if (m_audioSource == null)
        {
            m_audioSource = GetComponent<AudioSource>();
        }

        // 무기를 소유한 유닛의 진영을 사격 주체 진영으로 사용합니다.
        // (스쿼드 멤버 자식에 부착되어 부모의 HealthSystemBase를 찾습니다. 없으면 Player로 가정.)
        HealthSystemBase ownerHealth = GetComponentInParent<HealthSystemBase>();
        m_ownerFaction = ownerHealth != null ? ownerHealth.Faction : Faction.Player;
        m_ownerObject = ownerHealth != null ? ownerHealth.gameObject : null;

        // 소음은 총기가 아니라 총을 든 캐릭터가 내는 것으로 다룹니다.
        // 같은 캐릭터의 사격과 발소리가 같은 소음원이어야 변이체의 추적 목적지가 그 캐릭터를 따라 갱신됩니다.
        if (m_noiseEmitter == null)
        {
            m_noiseEmitter = GetComponentInParent<CharacterNoiseEmitter>();
        }

        ResolveFeedbackEmitter();
    }

    /// <summary>
    /// 필수 참조가 올바르게 설정되어 있는지 확인합니다.
    /// </summary>
    /// <returns>필수 참조가 모두 유효하면 <c>true</c>, 하나라도 누락되면 <c>false</c>입니다.</returns>
    private bool ValidateRequiredReferences()
    {
        bool isValid = true;

        if (m_firePos == null)
        {
            Debug.LogError("[Gun] FirePos가 할당되지 않았습니다.", this);
            isValid = false;
        }

        if (m_shellPos == null)
        {
            Debug.LogWarning("[Gun] ShellPos가 할당되지 않았습니다. 탄피 생성은 생략됩니다.", this);
        }

        if (m_clipPos == null)
        {
            Debug.LogWarning("[Gun] ClipPos가 할당되지 않았습니다. 탄창 드롭은 생략됩니다.", this);
        }

        if (m_audioSource == null && ResolveFeedbackEmitter() == null)
        {
            Debug.LogWarning("[Gun] AudioSource도 WeaponFeedbackEmitter도 없습니다. 무기 효과음은 재생되지 않습니다.", this);
        }
        /*
        if (PoolManager.instance == null)
        {
            Debug.LogError("[Gun] PoolManager 인스턴스를 찾지 못했습니다.", this);
            isValid = false;
        }*/

        return isValid;
    }

    /// <summary>
    /// 새로운 무기 밸런스 SO로 교체하고 공용 ID 바인딩을 즉시 다시 수행합니다.
    /// </summary>
    public BalanceBindResult SetBalance(GunBalanceSO balance)
    {
        m_balanceSO = balance;
        return BindConfiguredBalance();
    }

    /// <summary>개별 밸런스 SO를 직접 물고 있는지 여부입니다.</summary>
    /// <remarks><c>true</c>면 <see cref="SOBinder"/>가 통합 SO 주입을 건너뜁니다.</remarks>
    public bool HasOwnBalance => m_balanceSO != null;

    /// <summary>
    /// 엔티티 통합 밸런스 SO의 값을 적용합니다.
    /// </summary>
    /// <param name="balance">통합 밸런스 SO입니다.</param>
    /// <returns>이번 바인딩의 집계 결과입니다.</returns>
    /// <remarks>
    /// 개별 SO 슬롯은 비운 채로 둡니다. 통합 SO는 <see cref="GunBalanceSO"/> 타입이 아니라 담을 수 없고,
    /// 슬롯이 비어 있다는 것 자체가 "개별 지정 없음"을 뜻하기 때문입니다.
    /// </remarks>
    public BalanceBindResult BindSharedBalance(ScriptableObject balance)
    {
        return BindFrom(balance);
    }

    /// <summary>
    /// 지정된 SO가 있을 때 공용 BindManager를 통해 같은 ID의 필드 값을 적용합니다.
    /// </summary>
    private BalanceBindResult BindConfiguredBalance()
    {
        return BindFrom(m_balanceSO);
    }

    /// <summary>
    /// 주어진 원본 SO에서 밸런스 값을 대입하고 무기 고유의 후처리를 수행합니다.
    /// </summary>
    /// <param name="balance">값을 읽어올 밸런스 SO입니다. 개별 SO일 수도, 엔티티 통합 SO일 수도 있습니다.</param>
    /// <returns>이번 바인딩의 집계 결과입니다. 원본이 없으면 기본값입니다.</returns>
    /// <remarks>
    /// 개별 경로와 통합 경로가 같은 본문을 쓰게 해서, 어느 쪽으로 들어와도 방사각 기준값 갈무리와
    /// 표 복제가 빠지지 않게 합니다.
    /// </remarks>
    private BalanceBindResult BindFrom(ScriptableObject balance)
    {
        if (balance == null)
        {
            return default;
        }

        // 대입이 값을 덮어쓰기 전에 프리팹 저작값을 확보해 둡니다.
        CaptureFallbackSpreadRanges();
        BalanceBindResult result = BindManager.Instance.Bind(balance, this, this);

        // 참조형 밸런스 값은 그대로 대입되어 SO와 같은 인스턴스를 공유합니다.
        // 복제하지 않으면 런타임에 구간표를 고칠 때 프로젝트 자산인 SO가 함께 바뀝니다.
        // 정렬도 여기서 한 번만 합니다. 사격 경로에 정렬 비용을 얹지 않기 위해서입니다.
        m_damageFalloff = m_damageFalloff != null ? m_damageFalloff.Clone() : new DamageFalloffTable();
        m_penetration = m_penetration != null ? m_penetration.Clone() : new PenetrationTable();

        return result;
    }

    /// <summary>
    /// 프리팹이 저작한 방사각 범위를 최초 한 번만 보관합니다.
    /// </summary>
    /// <remarks>
    /// 첫 <see cref="BindConfiguredBalance"/> 호출 시점에는 아직 SO 값이 대입되지 않아 필드에 프리팹 값이 남아 있습니다.
    /// 런타임에 SO를 교체해도 기준은 항상 이 최초 프리팹 값입니다.
    /// </remarks>
    private void CaptureFallbackSpreadRanges()
    {
        if (m_fallbackRangesCaptured)
        {
            return;
        }

        m_fallbackHipfireMinSpread = m_hipfireMinSpread;
        m_fallbackHipfireMaxSpread = m_hipfireMaxSpread;
        m_fallbackAdsMinSpread = m_adsMinSpread;
        m_fallbackAdsMaxSpread = m_adsMaxSpread;
        m_fallbackRangesCaptured = true;
    }

    /// <summary>
    /// 최소·최대 방사각이 뒤집혀 들어온 경우 프리팹 저작값으로 되돌리고 경고합니다.
    /// </summary>
    /// <remarks>
    /// 한 필드의 Min/Max 선언으로는 "다른 필드보다 커야 한다"를 표현할 수 없어, 모든 값이 대입된 뒤에 검사합니다.
    /// 시트에 min=5, max=2처럼 잘못 적힌 경우가 여기서 걸립니다.
    /// </remarks>
    private void RestoreInvertedSpreadRanges()
    {
        if (!m_fallbackRangesCaptured)
        {
            return;
        }

        if (m_hipfireMaxSpread < m_hipfireMinSpread)
        {
            Debug.LogWarning(
                $"[Gun] 비조준 방사각 범위가 뒤집혔습니다(min={m_hipfireMinSpread}, max={m_hipfireMaxSpread}). " +
                $"프리팹 값(min={m_fallbackHipfireMinSpread}, max={m_fallbackHipfireMaxSpread})으로 되돌립니다.",
                this);
            m_hipfireMinSpread = m_fallbackHipfireMinSpread;
            m_hipfireMaxSpread = m_fallbackHipfireMaxSpread;
        }

        if (m_adsMaxSpread < m_adsMinSpread)
        {
            Debug.LogWarning(
                $"[Gun] 조준 방사각 범위가 뒤집혔습니다(min={m_adsMinSpread}, max={m_adsMaxSpread}). " +
                $"프리팹 값(min={m_fallbackAdsMinSpread}, max={m_fallbackAdsMaxSpread})으로 되돌립니다.",
                this);
            m_adsMinSpread = m_fallbackAdsMinSpread;
            m_adsMaxSpread = m_fallbackAdsMaxSpread;
        }
    }

    /// <summary>
    /// 공용 대입 이후 런타임 상태와 변수 간 관계를 정리합니다.
    /// </summary>
    public void OnBalanceApplied()
    {
        // 뒤집힌 범위를 먼저 되돌린 뒤 나머지 보정을 적용합니다.
        RestoreInvertedSpreadRanges();
        ClampBulletValues();
        UpdateCurrentSpreadInspectorFields();

        if (m_hasRequiredReferences)
        {
            UpdateBulletUI();
        }
    }

    /// <summary>
    /// 탄약 관련 수치가 유효 범위를 벗어나지 않도록 보정합니다.
    /// </summary>
    /// <remarks>
    /// 단일 필드 경계는 <see cref="ClampAttribute"/>가 담당하므로 여기 두지 않습니다.
    /// 여기 남은 둘은 다른 필드가 경계라서 선언으로 표현할 수 없는 것들입니다.
    /// 현재 탄약은 최대 탄창을 넘을 수 없고, 최대 탄퍼짐은 최소 탄퍼짐보다 작을 수 없습니다.
    /// 뒤집힌 탄퍼짐 범위는 시트에서 값이 잘못 들어와도 조준이 무너지지 않도록 여기서 되돌립니다.
    /// </remarks>
    private void ClampBulletValues()
    {
        m_currentBullet = Mathf.Clamp(m_currentBullet, 0, m_maxBullet);
        m_adsMaxSpread = Mathf.Max(m_adsMinSpread, m_adsMaxSpread);
        m_hipfireMaxSpread = Mathf.Max(m_hipfireMinSpread, m_hipfireMaxSpread);
    }

    /// <remarks>
    /// 인스펙터에서 값을 만졌을 때도 Bind 직후와 같은 규칙을 적용합니다. 규칙 본문은 한 곳에만 둡니다.
    /// </remarks>
    private void OnValidate()
    {
        OnBalanceApplied();
    }

    /// <summary>
    /// 지정한 목표 위치를 향해 사격을 시도합니다.
    /// </summary>
    /// <param name="targetPosition">탄환이 향할 월드 좌표입니다.</param>
    /// <returns>사격에 성공하면 <c>true</c>, 사격 불가능 상태면 <c>false</c>입니다.</returns>
    public bool TryShoot(Vector3 targetPosition)
    {
        if (!m_hasRequiredReferences)
        {
            return false;
        }

        bool isOutOfAmmo = m_currentBullet <= 0 && !IsInfiniteMagazineDebugActive;
        if (!m_canShoot || m_isReloading || isOutOfAmmo)
        {
            if (isOutOfAmmo && !m_isReloading)
            {
                PlayDryFireWithCooldown();
            }

            return false;
        }

        if (PoolManager.instance == null)
        {
            Debug.LogWarning("[Gun] PoolManager 인스턴스가 없어 사격을 처리할 수 없습니다.", this);
            return false;
        }

        if (!IsInfiniteMagazineDebugActive)
        {
            m_currentBullet--;
        }
        m_canShoot = false;
        // 오브젝트 풀링
        SpawnBullet(targetPosition);
        PlaySuccessfulShotFeedback(m_firePos.position, targetPosition);
        UpdateBulletUI();

        Invoke(nameof(ResetShoot), m_shootDelay);
        return true;
    }

    /// <summary>
    /// 조준 컨트롤러가 계산한 조준 정보에 탄퍼짐을 적용해 사격을 시도합니다.
    /// </summary>
    /// <param name="shotInfo">총구 원점·조준 방향·조준점을 담은 조준 정보입니다(탄퍼짐 미적용 정밀 값).</param>
    /// <param name="isAds">조준(ADS) 상태면 <c>true</c>, 힙파이어면 <c>false</c>입니다. 기본 방사각 선택에 사용합니다.</param>
    /// <param name="firedShot">탄퍼짐이 적용된 실제 발사 방향과 탄착 정보입니다. 마커/이펙트가 이 값을 씁니다.</param>
    /// <returns>사격에 성공하면 <c>true</c>, 필수 참조 누락·무효 정보·쿨다운·재장전·탄약 부족이면 <c>false</c>입니다.</returns>
    public bool TryLayShoot(HitscanShotInfo shotInfo, bool isAds, out HitscanShotInfo firedShot)
    {
        firedShot = shotInfo;

        if (!m_hasRequiredReferences)
        {
            return false;
        }

        if (!shotInfo.IsValid)
        {
            return false;
        }

        bool isOutOfAmmo = m_currentBullet <= 0 && !IsInfiniteMagazineDebugActive;
        if (!m_canShoot || m_isReloading || isOutOfAmmo)
        {
            if (isOutOfAmmo && !m_isReloading)
            {
                PlayDryFireWithCooldown();
            }

            return false;
        }

        if (!IsInfiniteMagazineDebugActive)
        {
            m_currentBullet--;
        }
        m_canShoot = false;

        firedShot = BuildFiredShot(shotInfo, isAds);
        LayShoot(firedShot);
        PlaySuccessfulShotFeedback(firedShot.Origin, firedShot.EndPoint);
        EmitShotNoise();
        UpdateBulletUI();

        Invoke(nameof(ResetShoot), m_shootDelay);
        return true;
    }

    /// <summary>
    /// 현재 방사각으로 발사 방향을 흩뜨려 실제 발사 사격 정보를 구성합니다.
    /// </summary>
    /// <param name="aimShot">탄퍼짐 미적용 정밀 조준 정보입니다.</param>
    /// <param name="isAds">조준(ADS) 상태 여부입니다.</param>
    /// <returns>탄퍼짐이 적용된 방향으로 재레이캐스트한 발사 사격 정보입니다.</returns>
    private HitscanShotInfo BuildFiredShot(HitscanShotInfo aimShot, bool isAds)
    {
        float spread = ResolveShotSpread(isAds);
        Vector3 direction = ApplySpread(aimShot.Direction, spread);

        // 사격 순간 방사각/편향 진단 로그(에디터 전용, 빌드에서 호출 스트립).
        LogSpreadDebug(isAds, spread, aimShot.Direction, direction);

        HitscanShotInfo fired = new()
        {
            IsValid = true,
            AimPoint = aimShot.AimPoint,
            Origin = aimShot.Origin,
            Direction = direction,
            FrameCount = aimShot.FrameCount,
            EndPoint = aimShot.Origin + direction * m_hitscanRange,
        };

        ResolveShotPath(aimShot.Origin, direction, m_hitscanRange, ref fired);

        return fired;
    }

    /// <summary>
    /// 사격 경로를 훑어 피해를 줄 대상 목록과 탄이 멈추는 지점을 정합니다.
    /// </summary>
    /// <param name="origin">추적 시작 위치입니다.</param>
    /// <param name="direction">추적 방향입니다.</param>
    /// <param name="distance">추적 거리입니다.</param>
    /// <param name="fired">결과를 채울 사격 정보입니다.</param>
    /// <remarks>
    /// 결과는 <see cref="m_shotPath"/>에 남습니다. 같은 프레임의 <see cref="ApplyHitscanDamage"/>가 이어서
    /// 소비합니다. 사격 한 번의 흐름(BuildFiredShot -> LayShoot -> ApplyHitscanDamage) 안에서만 유효합니다.
    ///
    /// 관통은 <b>유닛만</b> 뚫습니다. 지형을 만나면 그 자리에서 멈춥니다. 아군과 시체는
    /// <see cref="CombatDamage.BlocksShot"/>이 이미 걸러 내므로 관통 횟수를 쓰지 않습니다.
    /// 팀원을 지나갔다고 관통 횟수가 닳으면, 같은 조준이 팀 배치에 따라 다른 결과를 냅니다.
    /// </remarks>
    private void ResolveShotPath(Vector3 origin, Vector3 direction, float distance, ref HitscanShotInfo fired)
    {
        m_shotPath.Clear();
        m_hasSurfaceImpact = false;

        // 1차: HitDetectVolume으로 이 레이가 지나갈 유닛을 찾아 그들의 부위 히트박스만 켭니다.
        // 켠 뒤에는 반드시 EnableHitboxesAlongShot 안에서 물리 씬을 동기화합니다.
        EnableHitboxesAlongShot(origin, direction, distance);

        try
        {
            // 2차: 방금 연 대상들의 EnemyHitbox만 포함하는 실제 사격 마스크로 같은 탄도를 다시 검사합니다.
            int count = Physics.RaycastNonAlloc(
                origin, direction, m_traceBuffer, distance, m_hitscanLayerMask, QueryTriggerInteraction.Collide);

            CollectBlockingHits(m_traceBuffer, count, m_ownerFaction, m_allyBulletPassThrough, m_blockingHits);

            // 3차: 켜진 부위를 상대로 같은 탄도를 다시 쏜 결과. 여기서 맞은 부위가 실제 판정 부위입니다.
            LogTwoStageTrace(
                $"3차 재발사 | 명중 {m_blockingHits.Count}개"
                + (m_blockingHits.Count > 0
                    ? $" 부위={m_blockingHits[0].collider.name}"
                    : " (빗나감 - 켠 부위 사이를 통과했습니다)"));

            int penetrationsUsed = 0;

            for (int i = 0; i < m_blockingHits.Count; i++)
            {
                RaycastHit current = m_blockingHits[i];

                if (!fired.HasHit)
                {
                    fired.HasHit = true;
                    fired.Hit = current;
                }

                fired.EndPoint = current.point;

                bool isUnit = current.collider.GetComponentInParent<IDamageable>() != null;

                if (!isUnit)
                {
                    // 지형입니다. 여기서 멈추고, 탄흔은 이 자리에 남습니다.
                    m_surfaceImpact = current;
                    m_hasSurfaceImpact = true;
                    return;
                }

                m_shotPath.Add(current);

                if (!m_penetration.CanPenetrate(penetrationsUsed))
                {
                    return;
                }

                penetrationsUsed++;
            }
        }
        finally
        {
            // RaycastHit은 Collider 참조와 명중 데이터를 보존하므로, 피해 적용 전이라도 즉시 닫아도 됩니다.
            // 성공·실패·예외 어느 경로에서도 다음 프레임까지 부위 히트박스가 남지 않게 합니다.
            DisableOpenedHitboxes();
        }
    }

    /// <summary>이번 사격에서 부위 히트박스를 켠 유닛들입니다. 판정이 끝나면 다시 끕니다.</summary>
    private readonly List<HitboxGroup> m_openedHitboxGroups = new List<HitboxGroup>();

    /// <summary>
    /// 사격 경로가 지나갈 유닛을 HitDetectVolume으로 찾아 그들의 부위 히트박스만 켭니다.
    /// </summary>
    /// <param name="origin">사격 시작점입니다.</param>
    /// <param name="direction">사격 방향입니다.</param>
    /// <param name="distance">사거리입니다.</param>
    /// <param name="logStages">단계별 진단 로그를 남길지 여부입니다. 조준점 트레이스처럼 매 프레임 도는 경로는 끕니다.</param>
    /// <remarks>
    /// <b>왜 두 단계인가</b>: 부위 히트박스는 애니메이션 뼈에 붙어 매 프레임 위치가 바뀝니다. 항상 켜 두면
    /// 개체 하나당 십수 개가 매 프레임 브로드페이즈를 갱신하고 그 비용이 개체 수만큼 곱해집니다.
    /// 실제로 필요한 순간은 레이가 그 개체를 지나갈 때뿐이므로, 그때만 켭니다.
    ///
    /// <b>1차 마스크는 탐지 전용입니다.</b> EnemyHitDetect 외의 지형이나 이동용 콜라이더를 섞지 않습니다.
    /// 벽과 실제 부위 판정은 2차 사격 마스크가 처리하므로, 1차 레이는 후보를 여는 책임만 가집니다.
    ///
    /// <b>물리 씬 동기화가 필수입니다.</b> 이 프로젝트는 <c>Physics.autoSyncTransforms</c>가 꺼져 있어,
    /// 방금 켠 콜라이더는 애니메이션이 옮겨 놓은 최신 위치가 아니라 이전에 동기화된 위치에 있습니다.
    /// 그대로 2차 레이를 쏘면 맞아야 할 것이 빗나가고, 그 빗나감은 재현이 어렵습니다.
    /// </remarks>
    private void EnableHitboxesAlongShot(Vector3 origin, Vector3 direction, float distance, bool logStages = true)
    {
        DisableOpenedHitboxes();

        if (m_hitDetectLayerMask == 0)
        {
            // 탐지 마스크가 없으면 후보를 열지 않습니다. 평상시 히트박스가 꺼진 표준 대상은 맞지 않습니다.
            return;
        }

        int count = Physics.RaycastNonAlloc(
            origin, direction, m_traceBuffer, distance, m_hitDetectLayerMask, QueryTriggerInteraction.Collide);

        CollectBlockingHits(m_traceBuffer, count, m_ownerFaction, m_allyBulletPassThrough, m_blockingHits);

        for (int i = 0; i < m_blockingHits.Count; i++)
        {
            Collider collider = m_blockingHits[i].collider;

            // EnemyHitDetect 레이어에는 IDamageable의 자식 HitDetectVolume만 있어야 합니다.
            // 잘못 배치된 지형 콜라이더가 들어왔으면 그 충돌은 후보로 쓰지 않습니다.
            if (collider.GetComponentInParent<IDamageable>() == null)
            {
                continue;
            }

            HitboxGroup group = collider.GetComponentInParent<HitboxGroup>();
            if (group == null || !group.IsUsable)
            {
                continue;
            }

            group.SetHitboxesEnabled(true);
            m_openedHitboxGroups.Add(group);
        }

        if (logStages)
        {
            // 1차: 사격 레이가 HitDetectVolume을 지났는지. 여기서 0이면 그 대상은 아예 후보가 아닙니다.
            LogTwoStageTrace(
                $"1차 감지 | HitDetectVolume {m_blockingHits.Count}개 통과 (레이 원시 히트 {count}개)"
                + (m_blockingHits.Count > 0 ? $" 최근접={m_blockingHits[0].collider.name}" : string.Empty));

            // 2차: 그 결과로 어떤 대상의 부위 히트박스를 켰는지.
            LogTwoStageTrace(
                $"2차 히트박스 켬 | 대상 {m_openedHitboxGroups.Count}개"
                + (m_openedHitboxGroups.Count > 0 ? $" [{DescribeOpenedGroups()}]" : " (없음 - 3차는 아무것도 못 맞힙니다)"));
        }

        if (m_openedHitboxGroups.Count > 0)
        {
            // 켠 콜라이더를 최신 뼈 위치로 옮겨 놓습니다. 이 호출이 빠지면 2차 레이가 옛 위치를 때립니다.
            Physics.SyncTransforms();
        }
    }

    /// <summary>이번 사격에서 켰던 부위 히트박스를 모두 되돌립니다.</summary>
    /// <remarks>
    /// 판정이 끝나면 곧바로 끕니다. 프레임 끝까지 두면 연사 중 다음 발이 이전 발의 상태를 물려받아,
    /// 어떤 개체가 켜져 있는지가 사격 순서에 따라 달라집니다.
    /// </remarks>
    private void DisableOpenedHitboxes()
    {
        for (int i = 0; i < m_openedHitboxGroups.Count; i++)
        {
            m_openedHitboxGroups[i]?.SetHitboxesEnabled(false);
        }

        m_openedHitboxGroups.Clear();
    }

    /// <summary>
    /// 조준점을 정하는 트레이스를 사격 판정과 동일한 2단계 규칙으로 수행합니다.
    /// </summary>
    /// <param name="origin">트레이스 시작 위치입니다. 보통 카메라 위치입니다.</param>
    /// <param name="direction">트레이스 방향입니다.</param>
    /// <param name="distance">트레이스 거리입니다.</param>
    /// <param name="hitscanMask">부위 히트박스를 볼 수 있는 실제 판정 마스크입니다. 자기 레이어 제외처럼 호출부만 아는 규칙은 호출부가 반영해 넘깁니다.</param>
    /// <param name="hit">가장 가까운, 탄을 막는 대상입니다.</param>
    /// <returns>막는 대상을 찾았으면 <c>true</c>입니다.</returns>
    /// <remarks>
    /// <b>조준점은 반드시 목표의 표면에 찍혀야 합니다.</b> 총알은 카메라가 아니라 총구에서 나가고 방향은
    /// <c>(조준점 - 총구)</c>로 정해집니다. 카메라선과 총구선은 <b>조준점 한 점에서만 만나는 두 직선</b>이므로,
    /// 조준점이 목표보다 앞에 찍히면 그 점을 지난 뒤 목표 깊이에 닿기까지 두 선이 다시 벌어집니다.
    /// 벌어지는 양은 <c>(총구가 카메라 조준축에서 벗어난 거리) / (총구에서 조준점까지의 거리)</c>에 남은 거리를
    /// 곱한 값입니다. 3인칭이라 총구가 카메라보다 약 5m 앞·1m 옆에 있어 이 비율이 크고, 감지 볼륨 앞면
    /// (몸보다 약 1m 앞)을 조준점으로 쓰면 실측 수십 cm가 어긋납니다.
    ///
    /// 그 오차가 반지름 6~9cm인 손·전완·정강이·발보다 커서 사지가 통째로 빗나가는데, 감지 볼륨은 2m라
    /// 1차는 그대로 통과합니다. 그래서 "감지는 되는데 부위만 안 맞는" 모양이 되고, 몸통·머리(반지름 13~15cm)만
    /// 우연히 살아남습니다. 조준점을 부위 표면에 두면 총구선이 그 점을 반드시 지나므로 거리·각도·부위
    /// 크기와 무관하게 성립합니다.
    ///
    /// 후보를 여는 규칙을 <see cref="ResolveShotPath"/>와 공유하는 것이 요점입니다. 두 곳이 다른 규칙을 쓰면
    /// 조준점과 탄착이 다시 갈립니다. 단계별 로그는 끕니다 - 이 경로는 전투 자세 동안 매 프레임 돌아
    /// 켜 두면 콘솔이 프레임마다 두 줄씩 쌓입니다.
    ///
    /// 1차가 아무것도 찾지 못하면 히트박스를 켜지 않고 <see cref="Physics.SyncTransforms"/>도 부르지 않으므로,
    /// 조준선에 유닛이 없는 평상시 추가 비용은 레이캐스트 한 번입니다.
    /// </remarks>
    public bool TryTraceAimPoint(
        Vector3 origin, Vector3 direction, float distance, int hitscanMask, out RaycastHit hit)
    {
        EnableHitboxesAlongShot(origin, direction, distance, false);

        try
        {
            int count = Physics.RaycastNonAlloc(
                origin, direction, m_traceBuffer, distance, hitscanMask, QueryTriggerInteraction.Collide);

            return TryResolveNearestBlocking(
                m_traceBuffer, count, m_ownerFaction, m_allyBulletPassThrough, out hit);
        }
        finally
        {
            // 판정이 아니라 조준점 계산이므로, 같은 프레임의 실제 사격이 자기 1차를 다시 돌릴 수 있도록
            // 반드시 원상태로 되돌립니다. 남겨 두면 사격 순서에 따라 켜진 대상이 달라집니다.
            DisableOpenedHitboxes();
        }
    }

    /// <summary>2단계 사격 판정이 끊기는 지점을 찾기 위한 임시 에디터 로그입니다.</summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void LogTwoStageTrace(string message)
    {
        if (!m_debugLogTwoStage)
        {
            return;
        }

        string ownerName = m_ownerObject != null ? m_ownerObject.name : "none";
        Debug.Log($"[사격판정] f{Time.frameCount} {ownerName}/{name} {message}", this);
    }

    /// <summary>이번 사격에서 부위 히트박스를 켠 대상들의 이름을 나열합니다.</summary>
    /// <remarks>
    /// 어떤 개체가 후보로 잡혔는지 로그로 확인하기 위한 것입니다. 개수만으로는 엉뚱한 대상이 열렸는지 알 수 없습니다.
    /// 로그가 꺼져 있으면 호출되지 않으므로 평상시 문자열 조립 비용이 없습니다.
    /// </remarks>
    private string DescribeOpenedGroups()
    {
        if (m_openedHitboxGroups.Count == 0)
        {
            return string.Empty;
        }

        System.Text.StringBuilder builder = new System.Text.StringBuilder();

        for (int i = 0; i < m_openedHitboxGroups.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            HitboxGroup group = m_openedHitboxGroups[i];
            if (group == null)
            {
                builder.Append("(사라짐)");
                continue;
            }

            // 그룹 이름만으로는 "켰다고 기록됐는데 실제 콜라이더는 꺼져 있는" 상태를 가릴 수 없습니다.
            // 켜진 수/수집된 수를 함께 적어 3차가 빗나갈 때 원인이 기하 문제인지 배선 문제인지 나눕니다.
            builder.Append(group.gameObject.name);
            builder.Append(" 부위");
            builder.Append(group.EnabledHitboxCount);
            builder.Append('/');
            builder.Append(group.HitboxCount);
        }

        return builder.ToString();
    }

    /// <summary>
    /// 트레이스 결과에서 탄을 막는 것만 골라 거리 오름차순으로 담습니다.
    /// </summary>
    /// <param name="buffer">트레이스 결과 버퍼입니다.</param>
    /// <param name="count">버퍼에 채워진 개수입니다.</param>
    /// <param name="attacker">사격자의 진영입니다.</param>
    /// <param name="allyPassThrough">아군의 몸을 통과시킬지 여부입니다.</param>
    /// <param name="result">결과를 담을 목록입니다. 호출 시 비웁니다.</param>
    /// <remarks>
    /// 관통은 "가장 가까운 하나"가 아니라 순서 전체가 필요하므로 여기서는 정렬합니다.
    /// 비교자는 정적으로 캐싱해 호출마다 델리게이트를 만들지 않습니다.
    ///
    /// 버퍼가 가득 차면 유니티는 나머지를 조용히 버립니다. 그러면 뒤쪽 대상이 통째로 사라지므로 경고를 남깁니다.
    /// </remarks>
    internal static void CollectBlockingHits(
        RaycastHit[] buffer, int count, Faction attacker, bool allyPassThrough, List<RaycastHit> result)
    {
        result.Clear();

        if (count >= buffer.Length)
        {
            Debug.LogWarning(
                $"[Gun] 사격 트레이스 버퍼({buffer.Length})가 가득 찼습니다. 더 먼 충돌은 버려졌을 수 있습니다.");
        }

        for (int i = 0; i < count; i++)
        {
            if (CombatDamage.BlocksShot(buffer[i].collider, attacker, allyPassThrough))
            {
                result.Add(buffer[i]);
            }
        }

        result.Sort(DistanceComparison);
    }

    /// <summary>거리 오름차순 비교자입니다. 호출마다 델리게이트를 만들지 않도록 캐싱합니다.</summary>
    private static readonly System.Comparison<RaycastHit> DistanceComparison =
        static (a, b) => a.distance.CompareTo(b.distance);

    /// <summary>
    /// 사격 경로를 추적합니다. 설정에 따라 아군의 몸은 통과합니다.
    /// </summary>
    /// <param name="origin">추적 시작 위치입니다.</param>
    /// <param name="direction">추적 방향입니다.</param>
    /// <param name="distance">추적 거리입니다.</param>
    /// <param name="hit">가장 먼저 막은 대상입니다.</param>
    /// <returns>무언가에 막혔으면 true입니다.</returns>
    /// <remarks>
    /// 피격 히트박스는 물리로 밀치지 않도록 trigger로 두므로, 전역 설정과 무관하게 trigger를 맞히도록 못 박습니다.
    /// <c>UseGlobal</c>로 두면 <see cref="Physics.queriesHitTriggers"/>를 끄는 순간 사격이 통째로 먹히지 않습니다.
    ///
    /// 아군 통과가 켜져 있으면 경로 위의 대상을 모두 받아 아군 몸을 건너뜁니다. 단발
    /// <see cref="Physics.Raycast"/>는 가장 앞의 것만 주므로, 팀원이 앞을 막으면 그 뒤의 적을 볼 방법이 없습니다.
    ///
    /// <see cref="Physics.RaycastNonAlloc"/>를 쓰는 이유는 호출마다 배열을 새로 만들지 않기 위해서입니다.
    /// 자동 사격은 발사 간격마다 이 경로를 지나므로 <see cref="Physics.RaycastAll"/>이면 그때마다 쓰레기가 쌓입니다.
    ///
    /// 결과 순서는 두 API 모두 정의되어 있지 않습니다(유니티 문서: "the order of the results is undefined").
    /// 그래서 가장 가까운 유효 대상을 직접 골라야 합니다. 정렬하지 않으면 팀원 뒤의 적이나 벽 너머를 먼저 집어
    /// 관통 판정이 통째로 어긋납니다.
    ///
    /// 정렬 대신 한 번 훑으며 최솟값을 고릅니다. 필요한 것은 "막는 것 중 가장 가까운 하나"뿐이라 전체 순서는
    /// 필요 없고, 정렬은 O(n log n)에 비교자 호출까지 붙습니다. 대상 수가 적어 차이는 작지만 굳이 낼 비용이 아닙니다.
    ///
    /// 아군 판정은 <see cref="CombatDamage.IsFriendlyBody"/>가 담당합니다. 피해 판정과 같은 기준을 써야
    /// 피해는 안 들어가는데 탄만 막히는 어긋남이 생기지 않습니다.
    /// </remarks>
    private bool TryTraceShot(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit)
    {
        int count = Physics.RaycastNonAlloc(
            origin, direction, m_traceBuffer, distance, m_hitscanLayerMask, QueryTriggerInteraction.Collide);

        return TryResolveNearestBlocking(
            m_traceBuffer, count, m_ownerFaction, m_allyBulletPassThrough, out hit);
    }

    /// <summary>
    /// 트레이스 결과에서 탄을 막는 가장 가까운 대상을 고릅니다.
    /// </summary>
    /// <param name="buffer">트레이스 결과 버퍼입니다.</param>
    /// <param name="count">버퍼에 채워진 개수입니다.</param>
    /// <param name="attacker">사격자의 진영입니다.</param>
    /// <param name="hit">가장 가까운 막는 대상입니다.</param>
    /// <returns>막는 대상을 찾았으면 true입니다.</returns>
    /// <remarks>
    /// 버퍼가 가득 차면 유니티는 나머지를 조용히 버립니다. 그래서 앞을 가린 아군이 버퍼를 다 채우면
    /// 뒤의 적을 못 보는 일이 생길 수 있어, 가득 찬 경우를 경고로 남깁니다.
    /// </remarks>
    internal static bool TryResolveNearestBlocking(
        RaycastHit[] buffer, int count, Faction attacker, bool allyPassThrough, out RaycastHit hit)
    {
        hit = default;

        if (count >= buffer.Length)
        {
            Debug.LogWarning(
                $"[Gun] 사격 트레이스 버퍼({buffer.Length})가 가득 찼습니다. 더 먼 충돌은 버려졌을 수 있습니다.");
        }

        float nearest = float.PositiveInfinity;
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            if (buffer[i].distance >= nearest)
            {
                continue;
            }

            if (!CombatDamage.BlocksShot(buffer[i].collider, attacker, allyPassThrough))
            {
                continue;
            }

            nearest = buffer[i].distance;
            hit = buffer[i];
            found = true;
        }

        return found;
    }

    /// <summary>
    /// 이번 사격에 적용할 방사각(도)을 계산하고 연사 탄퍼짐 상태를 갱신합니다.
    /// </summary>
    /// <param name="isAds">조준(ADS) 상태 여부입니다. 모드별 탄퍼짐 파라미터와 누적 상태 선택에 사용합니다.</param>
    /// <returns>최소 방사각 + 누적 증가값으로 계산한 이번 사격의 방사각(도)입니다.</returns>
    private float ResolveShotSpread(bool isAds)
    {
        return isAds
            ? ResolveShotSpread(
                m_adsMinSpread,
                m_adsMaxSpread,
                m_adsMinSpreadShotCount,
                m_adsSpreadIncreasePerShot,
                m_adsSpreadRecoveryDelay,
                ref m_adsCurrentSpreadAdd,
                ref m_adsShotsInBurst,
                ref m_adsLastShotTime)
            : ResolveShotSpread(
                m_hipfireMinSpread,
                m_hipfireMaxSpread,
                m_hipfireMinSpreadShotCount,
                m_hipfireSpreadIncreasePerShot,
                m_hipfireSpreadRecoveryDelay,
                ref m_hipfireCurrentSpreadAdd,
                ref m_hipfireShotsInBurst,
                ref m_hipfireLastShotTime);
    }

    /// <summary>
    /// 모드(ADS/힙파이어)별 탄퍼짐 파라미터와 연사 누적 상태로 이번 사격의 방사각(도)을 계산합니다.
    /// </summary>
    /// <param name="minSpread">이 모드의 최소 방사각(도)입니다. 연사 누적이 없을 때의 기본 방사각이며, 0이면 정밀 사격입니다.</param>
    /// <param name="maxSpread">이 모드의 최대 방사각(도)입니다. 연사 누적값을 더해도 이 값을 넘지 않습니다. <paramref name="minSpread"/>보다 작으면 minSpread로 보정됩니다.</param>
    /// <param name="minSpreadShotCount">이 발수까지는 최소 방사각을 유지하고 누적 증가를 시작하지 않는, 첫 정밀 구간 발수입니다.</param>
    /// <param name="spreadIncreasePerShot">정밀 구간을 넘긴 뒤 발사할 때마다 누적되는 방사각 증가량(도)입니다.</param>
    /// <param name="spreadRecoveryDelay">마지막 사격 이후 이 시간(초)이 지나면 연사로 간주하지 않고 발수 카운트를 리셋합니다.</param>
    /// <param name="currentSpreadAdd">현재까지 누적된 방사각 증가값(도)입니다. 이 메서드에서 정밀 구간 이후 증가시키며, 회복은 <see cref="RecoverSpread"/>가 담당합니다.</param>
    /// <param name="shotsInBurst">현재 연사에서 발사한 누적 발수입니다. 사격 간격이 <paramref name="spreadRecoveryDelay"/>를 넘으면 0으로 리셋됩니다.</param>
    /// <param name="lastShotTime">마지막 사격 시각입니다. 연사 판정과 회복 지연에 사용하며, 이 메서드에서 현재 시각으로 갱신합니다.</param>
    /// <returns>최소 방사각 + 누적 증가값을 <paramref name="maxSpread"/>로 제한한, 이번 사격에 적용할 방사각(도)입니다.</returns>
    /// <remarks>
    /// 이번 발에 적용할 방사각은 "현재까지 누적된 값" 기준으로 먼저 정하고, 그 뒤에 다음 발을 위한 누적값을 증가시킵니다.
    /// 따라서 정밀 구간(<paramref name="minSpreadShotCount"/>) 직후 첫 발은 아직 최소 방사각이고, 그 다음 발부터 증가가 반영됩니다.
    /// </remarks>
    private static float ResolveShotSpread(
        float minSpread,
        float maxSpread,
        int minSpreadShotCount,
        float spreadIncreasePerShot,
        float spreadRecoveryDelay,
        ref float currentSpreadAdd,
        ref int shotsInBurst,
        ref float lastShotTime)
    {
        // 마지막 사격 이후 충분히 시간이 지났으면 새 연사로 보고 발수 카운트를 리셋합니다.
        // (누적값 currentSpreadAdd 자체의 회복은 RecoverSpread가 별도로 처리합니다.)
        if (Time.time - lastShotTime > spreadRecoveryDelay)
        {
            shotsInBurst = 0;
        }

        // maxSpread가 minSpread보다 작게 설정된 경우를 보정합니다(Clamp의 min>max 방지).
        maxSpread = Mathf.Max(minSpread, maxSpread);

        // 이번 발의 방사각 = 최소 방사각 + 현재까지 누적된 증가값, 단 [min, max] 범위로 제한.
        // min=max=0이면 누적이 있어도 항상 0이 되어 정밀 사격이 됩니다.
        float spread = Mathf.Clamp(minSpread + currentSpreadAdd, minSpread, maxSpread);

        // 이번 발을 카운트하고, 정밀 구간을 넘긴 뒤부터 다음 발을 위한 누적값을 키웁니다.
        // 누적값은 (max - min)을 상한으로 하여 최종 방사각이 maxSpread를 넘지 않게 합니다.
        shotsInBurst++;
        if (shotsInBurst > minSpreadShotCount)
        {
            float maxSpreadAdd = Mathf.Max(0.0f, maxSpread - minSpread);
            currentSpreadAdd = Mathf.Min(maxSpreadAdd, currentSpreadAdd + spreadIncreasePerShot);
        }

        // 연사 판정과 회복 지연 기준점이 되도록 마지막 사격 시각을 갱신합니다.
        lastShotTime = Time.time;
        return spread;
    }

    /// <summary>
    /// 지정한 모드의 누적 탄퍼짐 증가값을 회복합니다.
    /// </summary>
    private static void RecoverSpread(ref float currentSpreadAdd, float lastShotTime, float recoveryDelay, float recoveryPerSecond)
    {
        if (currentSpreadAdd > 0.0f && Time.time - lastShotTime > recoveryDelay)
        {
            currentSpreadAdd = Mathf.Max(0.0f, currentSpreadAdd - recoveryPerSecond * Time.deltaTime);
        }
    }

    /// <summary>
    /// 지정한 모드의 현재 탄퍼짐 방사각을 상태 변경 없이 계산합니다.
    /// </summary>
    private static float GetCurrentSpread(float minSpread, float maxSpread, float currentSpreadAdd)
    {
        maxSpread = Mathf.Max(minSpread, maxSpread);
        return Mathf.Clamp(minSpread + currentSpreadAdd, minSpread, maxSpread);
    }

    private void UpdateCurrentSpreadInspectorFields()
    {
        m_hipfireCurrentSpread = GetCurrentSpread(m_hipfireMinSpread, m_hipfireMaxSpread, m_hipfireCurrentSpreadAdd);
        m_adsCurrentSpread = GetCurrentSpread(m_adsMinSpread, m_adsMaxSpread, m_adsCurrentSpreadAdd);
    }

    /// <summary>
    /// 발사 방향을 총구 기준 콘(cone) 안에서 무작위로 흩뜨립니다.
    /// </summary>
    /// <param name="direction">탄퍼짐 미적용 발사 방향입니다.</param>
    /// <param name="spreadDegrees">콘의 최대 편향 각도(도)입니다. 0 이하면 그대로 반환합니다.</param>
    /// <returns>콘 안에서 무작위로 편향된 정규화 방향입니다. 빗나감 거리는 사거리에 비례합니다.</returns>
    /// <remarks>
    /// 편향 = 단위오프셋 × tan(<paramref name="spreadDegrees"/>). 즉 콘의 "각도 크기"(min/max spread)와 콘 "안에서의 분포 모양"
    /// (<see cref="m_spreadDistribution"/>·<see cref="m_spreadConcentration"/>)은 서로 직교하며 곱으로 합성됩니다. 서로 상쇄되지 않고,
    /// 하나가 크기·이상치 사거리(하드 캡)를, 다른 하나가 중심 몰림 정도(코어 조임)를 담당합니다.
    /// 콘 안에서의 편향 분포는 <see cref="m_spreadDistribution"/>가 결정합니다. 기본값 Gaussian은 콘 반각(<paramref name="spreadDegrees"/>)을
    /// <see cref="m_spreadConcentration"/> σ로 보고 중심 가중 정규분포로 샘플링한 뒤 콘 경계로 클램프하므로, 탄이 대부분 중심 근처에 몰립니다.
    /// 어느 분포든 콘 반각은 최대 편향(하드 캡)으로 유지됩니다. (spreadDegrees=0이면 편향 0이라 분포/집중도는 작용할 대상이 없습니다.)
    /// </remarks>
    private Vector3 ApplySpread(Vector3 direction, float spreadDegrees)
    {
        if (spreadDegrees <= 0.0f)
        {
            return direction.normalized;
        }

        Vector3 forward = direction.normalized;
        Vector3 right = Vector3.Cross(forward, Vector3.up);
        if (right.sqrMagnitude < 0.0001f)
        {
            right = Vector3.Cross(forward, Vector3.forward);
        }

        right.Normalize();
        Vector3 up = Vector3.Cross(right, forward);

        Vector2 unitOffset = m_spreadDistribution switch
        {
            SpreadDistribution.Uniform => Random.insideUnitCircle,
            _ => SampleGaussianUnitOffset(m_spreadConcentration),
        };
        Vector2 offset = unitOffset * Mathf.Tan(spreadDegrees * Mathf.Deg2Rad);
        return (forward + right * offset.x + up * offset.y).normalized;
    }

    /// <summary>
    /// 단위 원판 안에서 중심 가중(가우시안) 2D 오프셋을 샘플링합니다.
    /// </summary>
    /// <param name="concentration">콘 경계를 몇 σ로 볼지 정하는 집중도입니다. 클수록 중심에 더 몰립니다.</param>
    /// <returns>크기가 [0, 1]로 클램프된, 중심에 밀집한 2D 오프셋입니다.</returns>
    /// <remarks>Box-Muller 변환으로 회전 대칭 표준정규 2D 벡터를 만든 뒤 σ = 1/concentration 비율로 축소하고, 드물게 단위 원 밖으로 나가는 표본은 경계로 클램프해 최대 편향(하드 캡)을 유지합니다.</remarks>
    private static Vector2 SampleGaussianUnitOffset(float concentration)
    {
        // Box-Muller: 균일 난수 2개 -> 회전 대칭 표준정규 2D 벡터.
        float u1 = Mathf.Max(1e-6f, 1.0f - Random.value);
        float u2 = 1.0f - Random.value;
        float radius = Mathf.Sqrt(-2.0f * Mathf.Log(u1));
        float angle = 2.0f * Mathf.PI * u2;
        Vector2 standardNormal = new(radius * Mathf.Cos(angle), radius * Mathf.Sin(angle));

        // 콘 경계 = concentration σ가 되도록 축소(σ = 1/concentration).
        float sigma = 1.0f / Mathf.Max(1.0f, concentration);
        Vector2 offset = standardNormal * sigma;

        // 드물게 경계를 넘는 표본은 콘 경계로 클램프합니다.
        return offset.sqrMagnitude > 1.0f ? offset.normalized : offset;
    }

    /// <summary>
    /// 재장전을 시작합니다.
    /// </summary>
    public void StartReload()
    {
        if (m_isReloading || (!m_allowFullMagReload && m_currentBullet >= m_maxBullet))
        {
            return;
        }

        m_isReloading = true;
        m_reloadStartTime = Time.time;
        PlayReloadSound();
        CancelInvoke(nameof(CompleteReload));
        Invoke(nameof(CompleteReload), m_reloadTime);
    }

    /// <summary>
    /// 재장전을 완료하고 탄약을 최대치로 채웁니다.
    /// </summary>
    public void CompleteReload()
    {
        CancelInvoke(nameof(CompleteReload));
        m_currentBullet = m_maxBullet;
        m_isReloading = false;
        UpdateBulletUI();
        OnReloadCompleted?.Invoke();
    }

    /// <summary>
    /// 진행 중인 재장전을 취소합니다.
    /// </summary>
    public void CancelReload()
    {
        CancelInvoke(nameof(CompleteReload));
        m_isReloading = false;
        UpdateBulletUI();
    }

    /// <summary>
    /// 계산된 히트스캔 사격 정보를 소비해 즉시 사격 결과를 처리합니다.
    /// </summary>
    /// <param name="shotInfo">조준 프레임에서 계산된 히트스캔 사격 정보입니다.</param>
    /// <remarks>Enemy 레이어에 맞은 경우 부모에서 <see cref="EnemyHealth"/>를 찾아 피해를 적용합니다.</remarks>
    private void LayShoot(HitscanShotInfo shotInfo)
    {
        if (!shotInfo.IsValid)
        {
            return;
        }

        if (shotInfo.HasHit)
        {
            DrawShotDebugRay(shotInfo, Color.red);

            // 피격 피드백은 대상마다 ApplyHitscanDamage 안에서 냅니다. 관통이면 꿰뚫린 적도 각자 피가 튀어야 합니다.
            ApplyHitscanDamage(shotInfo);

            // 탄흔은 탄이 실제로 멈춘 지형에 남깁니다. 첫 충돌을 쓰면 적을 꿰뚫고 벽에 박혀도 벽이 깨끗합니다.
            if (m_hasSurfaceImpact)
            {
                ResolveFeedbackEmitter()?.PlayImpact(m_surfaceImpact);
                FieldManager.Instance?.EffectManager?.PlaySurfaceResponse(m_surfaceImpact);
            }

            return;
        }

        DrawShotDebugRay(shotInfo, Color.yellow);
    }

    /// <summary>
    /// (에디터 전용) 사격 히트스캔 경로를 Scene 뷰 디버그 레이로 그립니다. 빌드에서는 호출이 스트립됩니다.
    /// </summary>
    /// <param name="shotInfo">그릴 사격 정보입니다.</param>
    /// <param name="color">레이 색상입니다.</param>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private static void DrawShotDebugRay(HitscanShotInfo shotInfo, Color color)
    {
        Debug.DrawLine(shotInfo.Origin, shotInfo.EndPoint, color, 1.0f, false);
    }

    /// <summary>
    /// (에디터 전용) 사격 순간 방사각·편향 진단 로그를 출력합니다. 빌드에서는 호출이 스트립됩니다.
    /// </summary>
    /// <param name="isAds">조준(ADS) 상태 여부입니다.</param>
    /// <param name="spread">이번 사격에 적용된 방사각(도)입니다.</param>
    /// <param name="aimDirection">탄퍼짐 미적용 조준 방향입니다.</param>
    /// <param name="firedDirection">탄퍼짐 적용 후 실제 발사 방향입니다.</param>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void LogSpreadDebug(bool isAds, float spread, Vector3 aimDirection, Vector3 firedDirection)
    {
#if UNITY_EDITOR
        if (!m_debugLogSpread)
        {
            return;
        }

        float deviationDeg = Vector3.Angle(aimDirection, firedDirection);
        Debug.Log($"[SpreadDebug] isAds={isAds} spread={spread:F3}° deviation={deviationDeg:F3}° " +
                  $"(ads[{m_adsMinSpread:F2}~{m_adsMaxSpread:F2}] add={m_adsCurrentSpreadAdd:F2} / " +
                  $"hip[{m_hipfireMinSpread:F2}~{m_hipfireMaxSpread:F2}] add={m_hipfireCurrentSpreadAdd:F2})", this);
#endif
    }

    /// <summary>
    /// 히트스캔 충돌 대상이 적대 진영이면 공용 피해 경로로 피해를 전달합니다.
    /// </summary>
    /// <param name="shotInfo">사격으로 발생한 히트스캔 충돌 정보입니다.</param>
    /// <remarks>
    /// 대상 구체 타입을 모른 채 <see cref="CombatDamage"/>가 진영·부위 판정 후 적용하고,
    /// 피격 확정 시 <see cref="OnHitFeedback"/>를 발생시킵니다.
    ///
    /// 관통이 켜져 있으면 <see cref="ResolveShotPath"/>가 남긴 대상 목록을 가까운 순서대로 훑습니다.
    /// 돌려주는 값은 <b>첫 대상</b>의 결과입니다. 조준선 피드백과 처치 표시가 "내가 겨눈 것"을 기준으로
    /// 움직여야 하기 때문입니다. 뒤쪽 대상의 피격 피드백은 <see cref="OnHitFeedback"/>로 각각 나갑니다.
    /// </remarks>
    private CombatDamage.HitFeedback ApplyHitscanDamage(HitscanShotInfo shotInfo)
    {
        if (m_hitscanDamage <= 0 || m_shotPath.Count == 0)
        {
            return CombatDamage.HitFeedback.None;
        }

        CombatDamage.HitFeedback first = CombatDamage.HitFeedback.None;

        for (int i = 0; i < m_shotPath.Count; i++)
        {
            RaycastHit target = m_shotPath[i];

            // 거리 감쇠는 무기가 소유합니다. 총이 쏜 거리는 총이 아는 정보이고,
            // 공용 피해 경로(CombatDamage)에 거리 개념을 넣으면 근접 공격이 쓰지 않는 인자가 생깁니다.
            int damage = ResolveDistanceAdjustedDamage(target.distance);

            // 관통 감쇠는 거리 감쇠 위에 얹습니다. 두 감쇠는 서로 다른 이유로 걸리므로 함께 적용됩니다.
            damage = m_penetration.ResolveDamage(i, damage);

            if (damage <= 0)
            {
                continue;
            }

            // 저지력은 거리·관통 감쇠를 받지 않습니다. 감쇠는 "얼마나 아픈가"의 규칙이고
            // 경직은 "얼마나 휘청이는가"라서, 관통한 두 번째 대상도 같은 충격을 받는 편이 맞습니다.
            CombatDamage.HitFeedback feedback = CombatDamage.ResolveHit(
                target.collider,
                m_ownerFaction,
                damage,
                m_headshotDamageMultiplier,
                m_allowHeadshot,
                m_ownerObject,
                m_stoppingPower);

            if (feedback.Applied)
            {
                LogTwoStageTrace($"4차 피해 적용 | 부위={target.collider.name} 피해={damage}");
                OnHitFeedback?.Invoke(feedback);
                ApplyHitscanKnockback(target, shotInfo);

                EnemyController enemy = target.collider.GetComponentInParent<EnemyController>();
                enemy?.PlayHitFeedback(target.point, target.normal, target.collider.transform);
            }

            if (i == 0)
            {
                first = feedback;
            }
        }

        return first;
    }

    /// <summary>
    /// 명중한 대상에 넉백 충격량을 전달합니다.
    /// </summary>
    /// <param name="shotInfo">이번 사격의 히트스캔 충돌 정보입니다.</param>
    /// <remarks>
    /// <b>피해 적용 뒤에 부릅니다.</b> 사망 처리가 같은 프레임에 동기로 끝나므로, 죽은 대상이면 이 시점에
    /// 래그돌이 이미 켜져 있어 물리 충격을 받을 수 있습니다. 살아 있으면 대상이 이동 변위로 처리합니다.
    /// 어느 쪽인지는 <see cref="IKnockbackReceiver"/> 구현이 자기 상태를 보고 정하므로 총기는 몰라도 됩니다.
    ///
    /// 방향은 <b>사격 지점에서 피격 지점으로 향하는 벡터</b>입니다. 히트스캔에서는 탄환 진행 방향과 거의
    /// 같으면서 계산이 단순하고, 근접 공격이 같은 규칙을 쓸 때도 그대로 성립합니다. 표면 법선을 쓰면
    /// 벽 각도에 따라 옆이나 뒤로 튀어 "맞아서 밀렸다"는 인상이 깨집니다.
    ///
    /// 공용 피해 경로(<see cref="CombatDamage"/>)를 거치지 않고 여기서 직접 처리합니다. 방향과 부위를
    /// 실어 보내려면 <c>TakeDamage</c>와 사망 이벤트 시그니처를 열어야 하고, 그러면 플레이어와 스쿼드까지
    /// 영향을 받습니다. 넉백은 쏜 쪽이 아는 정보로 끝낼 수 있으므로 호출부에 둡니다.
    /// </remarks>
    /// <param name="hit">넉백을 받을 충돌입니다. 관통이면 대상마다 따로 부릅니다.</param>
    /// <param name="shotInfo">이번 사격의 발사 지점과 방향을 읽습니다.</param>
    private void ApplyHitscanKnockback(in RaycastHit hit, in HitscanShotInfo shotInfo)
    {
        Collider hitCollider = hit.collider;
        if (hitCollider == null)
        {
            return;
        }

        IKnockbackReceiver receiver = hitCollider.GetComponentInParent<IKnockbackReceiver>();
        if (receiver == null)
        {
            return;
        }

        Vector3 direction = hit.point - shotInfo.Origin;
        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            // 사격 지점과 피격 지점이 겹치는 밀착 사격입니다. 조준 방향을 그대로 씁니다.
            direction = shotInfo.Direction;
        }

        receiver.ApplyKnockback(
            direction,
            hit.point,
            m_knockbackImpulse,
            hit.rigidbody);
    }

    /// <summary>
    /// 거리 감쇠를 반영한 피해량을 돌려줍니다.
    /// </summary>
    /// <param name="distance">사격 지점에서 명중 지점까지의 거리(m)입니다.</param>
    /// <returns>구간 배율을 곱해 반올림한 피해량입니다. 최소 1을 보장합니다.</returns>
    /// <remarks>
    /// 약점 배율은 여기서 곱하지 않습니다. <see cref="CombatDamage.ResolveHit"/>가 부위를 판정한 뒤 곱하며,
    /// 거리 감쇠를 먼저 적용해 두면 원거리 약점 사격이 "감쇠된 피해의 배수"가 되어 의도와 맞습니다.
    ///
    /// 최소 1을 보장하는 것은 <see cref="CombatDamage.ResolveHit"/>와 같은 규약입니다.
    /// 맞았는데 0이 들어가면 피격 표시만 뜨고 아무 일도 일어나지 않아 버그로 보입니다.
    /// </remarks>
    private int ResolveDistanceAdjustedDamage(float distance)
    {
        if (m_damageFalloff == null)
        {
            return m_hitscanDamage;
        }

        return m_damageFalloff.ResolveDamage(distance, m_hitscanDamage);
    }

    /// <summary>
    /// 선택했을 때 총구 전방에 거리별 피해 감쇠 구간을 그립니다.
    /// </summary>
    /// <remarks>
    /// 인스펙터의 감쇠 트랙(<c>DamageFalloffTableDrawer</c>)은 구간 값을 편집하기에는 좋지만 2D UI라서
    /// "이 총을 든 채로 저 적까지가 몇 번째 구간인지"를 알 수 없습니다. 실제 교전 거리는 레벨이 정하므로
    /// 씬 안에서 봐야 판단이 됩니다.
    ///
    /// 구간 경계마다 고리를 그리고 구간 사이는 선으로 잇습니다. 구간이 멀수록 색을 어둡게 해서
    /// 피해가 줄어드는 방향을 색으로도 읽을 수 있게 했습니다.
    ///
    /// 총구가 없으면 이 컴포넌트의 위치와 정면을 씁니다. 프리팹 상태처럼 총구 참조가 아직 비어 있어도
    /// 대략의 거리감은 볼 수 있어야 하기 때문입니다.
    /// </remarks>
    private void OnDrawGizmosSelected()
    {
        if (m_debugDrawShotNoiseRange)
        {
            // 총성은 벽을 통과합니다. 이 원 안이면 듣는 쪽의 청각 배수에 따라 인지 게이지가 찹니다.
            Gizmos.color = new Color(1.0f, 0.3f, 0.55f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, m_shotNoiseRange);
        }

        if (!m_debugDrawDamageFalloff || m_damageFalloff == null || m_damageFalloff.IsEmpty)
        {
            return;
        }

        Transform origin = m_firePos != null ? m_firePos : transform;
        Vector3 start = origin.position;
        Vector3 forward = origin.forward;

        float previousDistance = 0.0f;
        int stepCount = m_damageFalloff.Steps.Count;

        for (int i = 0; i < stepCount; i++)
        {
            DamageFalloffStep step = m_damageFalloff.Steps[i];

            // 사거리 밖은 애초에 명중 판정이 성립하지 않으므로 그 너머는 그리지 않습니다.
            float distance = Mathf.Min(step.MaxDistance, m_hitscanRange);

            // 뒤 구간이 사거리 안쪽으로 잘렸다면 남은 구간도 볼 것이 없습니다.
            if (distance <= previousDistance)
            {
                break;
            }

            // 먼 구간일수록 어둡게. 피해가 줄어드는 방향을 색으로도 읽히게 합니다.
            float brightness = Mathf.Lerp(1.0f, 0.35f, stepCount > 1 ? (float)i / (stepCount - 1) : 0.0f);
            Gizmos.color = new Color(brightness, brightness * 0.85f, 0.2f, 0.9f);

            Gizmos.DrawLine(start + forward * previousDistance, start + forward * distance);
            DrawFalloffRing(start + forward * distance, forward, m_debugFalloffRingRadius);

            previousDistance = distance;
        }
    }

    /// <summary>감쇠 구간 경계를 표시하는 고리를 그립니다.</summary>
    /// <param name="center">고리의 중심입니다.</param>
    /// <param name="normal">고리가 향할 축입니다. 사격 방향을 넘깁니다.</param>
    /// <param name="radius">고리의 반지름(m)입니다.</param>
    /// <remarks>
    /// <c>Gizmos</c>에는 원을 그리는 기능이 없어 선분으로 근사합니다. 같은 이유로 <see cref="AimController"/>도
    /// 디버그 구를 선분으로 그립니다. <c>Handles</c>는 Editor 전용 어셈블리라 런타임 스크립트에서 쓸 수 없습니다.
    /// </remarks>
    private static void DrawFalloffRing(Vector3 center, Vector3 normal, float radius)
    {
        const int Segments = 16;

        if (normal.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Quaternion rotation = Quaternion.LookRotation(normal);
        Vector3 previous = center + rotation * new Vector3(radius, 0.0f, 0.0f);

        for (int i = 1; i <= Segments; i++)
        {
            float angle = i / (float)Segments * Mathf.PI * 2.0f;
            Vector3 point = center + rotation * new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0.0f);

            Gizmos.DrawLine(previous, point);
            previous = point;
        }
    }

    /// <summary>
    /// 이번 사격의 소음을 변이체 쪽에 알립니다.
    /// </summary>
    /// <remarks>
    /// 소음량과 도달 거리는 총기가 정하고, 발신 자체는 캐릭터가 합니다.
    /// 소음기 같은 부착물이 값을 바꾸는 것은 총기 쪽 관심사이므로 수치를 여기 두었습니다.
    ///
    /// 실제 사격이 성립한 뒤에만 부릅니다. 탄약이 없거나 재장전 중이면 소리가 나지 않아야 합니다.
    /// 소음이 실제 발사와 어긋나면 플레이어가 소리를 예측할 수 없어 잠입 판단이 불가능해집니다.
    /// </remarks>
    private void EmitShotNoise()
    {
        if (m_noiseEmitter == null)
        {
            return;
        }

        m_noiseEmitter.EmitGunshot(m_shotNoiseLevel, m_shotNoiseRange);
    }

    /// <summary>
    /// 사격 쿨다운을 종료하고 다시 사격 가능한 상태로 전환합니다.
    /// </summary>
    private void ResetShoot()
    {
        m_canShoot = true;
    }

    /// <summary>
    /// 탄환 오브젝트를 풀에서 꺼내 목표 방향으로 생성합니다.
    /// </summary>
    /// <param name="targetPosition">탄환이 향할 월드 좌표입니다.</param>
    private void SpawnBullet(Vector3 targetPosition)
    {
        if (m_firePos == null || PoolManager.instance == null)
        {
            return;
        }

        Vector3 shootDirection = targetPosition - m_firePos.position;

        if (shootDirection.sqrMagnitude < 0.0001f)
        {
            shootDirection = m_firePos.forward;
        }
        else
        {
            shootDirection.Normalize();
        }

        Quaternion bulletRotation = Quaternion.LookRotation(shootDirection);

        PoolManager.instance.GetObject(
            m_bulletPoolIndex,
            m_firePos.position,
            bulletRotation);
    }

    /// <summary>
    /// 탄피 오브젝트를 풀에서 생성합니다.
    /// </summary>
    private void SpawnShell()
    {
        if (m_shellPos == null || PoolManager.instance == null)
        {
            return;
        }

        PoolManager.instance.GetObject(
            m_shellPoolIndex,
            m_shellPos.position,
            m_shellPos.rotation);
    }

    /// <summary>
    /// 탄창 오브젝트를 풀에서 생성합니다.
    /// </summary>
    private void SpawnClip()
    {
        if (m_clipPos == null || PoolManager.instance == null)
        {
            return;
        }

        PoolManager.instance.GetObject(
            m_clipPoolIndex,
            m_clipPos.position,
            m_clipPos.rotation);
    }

    /// <summary>
    /// 머즐 플래시 파티클을 재생합니다.
    /// </summary>
    private void SpawnMuzzleFlash()
    {
        if (m_muzzleFlashParticle == null)
        {
            return;
        }

        m_muzzleFlashParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        m_muzzleFlashParticle.Play();
    }

    /// <summary>
    /// 재장전 애니메이션에서 탄창이 빠지는 타이밍에 호출되는 이벤트입니다.
    /// </summary>
    public void OnReloadMagOut()
    {
        SpawnClip();
    }

    /// <summary>
    /// 사격 효과음을 재생합니다.
    /// </summary>
    private void PlayShootSound()
    {
        if (m_audioSource == null || m_shootClip == null)
        {
            return;
        }

        m_audioSource.PlayOneShot(m_shootClip);
    }

    /// <summary>
    /// 재장전 효과음을 재생합니다.
    /// </summary>
    private void PlayReloadSound()
    {
        if (TryUseFeedbackEmitter(out WeaponFeedbackEmitter emitter))
        {
            emitter.PlayReload();
            return;
        }

        if (m_audioSource == null || m_reloadClip == null)
        {
            return;
        }

        m_audioSource.PlayOneShot(m_reloadClip);
    }

    /// <summary>
    /// 풀 탄창 등으로 재장전이 막혔을 때 빈 장전(드라이) 효과음을 재생합니다.
    /// </summary>
    /// <remarks>효과음 클립이 비어 있으면 아무 소리도 내지 않습니다. 사운드 배선 전이라도 호출 틀은 유지됩니다.</remarks>
    public void PlayEmptyReloadSound()
    {
        if (TryUseFeedbackEmitter(out WeaponFeedbackEmitter emitter))
        {
            emitter.PlayDryFire();
            return;
        }

        if (m_audioSource == null || m_emptyReloadClip == null)
        {
            return;
        }

        m_audioSource.PlayOneShot(m_emptyReloadClip);
    }

    /// <summary>사격 입력을 누르고 있어도 드라이 사운드가 프레임마다 중첩되지 않도록 짧은 간격을 둡니다.</summary>
    private void PlayDryFireWithCooldown()
    {
        if (Time.unscaledTime < m_nextDryFireTime)
        {
            return;
        }

        m_nextDryFireTime = Time.unscaledTime + 0.2f;
        PlayEmptyReloadSound();
    }

    /// <summary>피드백 이미터가 리소스를 가지고 있으면 그쪽으로 출력하고, 없으면 기존 개별 배선 경로를 유지합니다.</summary>
    private void PlaySuccessfulShotFeedback(Vector3 tracerStart, Vector3 tracerEnd)
    {
        if (TryUseFeedbackEmitter(out WeaponFeedbackEmitter emitter))
        {
            Transform muzzleSocket = m_muzzleFlashPos != null ? m_muzzleFlashPos : m_firePos;
            emitter.PlayShot(
                muzzleSocket,
                m_shellPos,
                tracerStart,
                tracerEnd);
            return;
        }

        SpawnShell();
        SpawnMuzzleFlash();
        PlayShootSound();
    }

    /// <summary>같은 총기 오브젝트에 붙은 Feedback emitter를 찾습니다.</summary>
    /// <returns>붙어 있지 않으면 <c>null</c>입니다.</returns>
    /// <remarks>
    /// 예전에는 없으면 런타임에 <c>AddComponent</c>로 보강했지만 지금은 하지 않습니다.
    /// 이미터가 피드백 SO를 주입받아야 하는데, 런타임에 생기는 컴포넌트에는 <see cref="SOBinder"/>가 주입할 시점이 없습니다.
    /// 빈 이미터를 붙여 봐야 재생할 리소스도 없으므로, 없으면 예전 개별 배선 경로로 넘어가는 편이 정직합니다.
    /// </remarks>
    private WeaponFeedbackEmitter ResolveFeedbackEmitter()
    {
        if (m_feedbackEmitter == null)
        {
            TryGetComponent(out m_feedbackEmitter);
        }

        return m_feedbackEmitter;
    }

    /// <summary>새 피드백 경로를 쓸 수 있는지 확인합니다.</summary>
    /// <param name="emitter">사용할 이미터입니다. 쓸 수 없으면 <c>null</c>입니다.</param>
    /// <returns>이미터가 있고 재생할 리소스를 가지고 있으면 <c>true</c>입니다.</returns>
    /// <remarks>리소스가 하나도 없으면 소리 없이 넘어가는 대신 예전 개별 배선 경로를 씁니다.</remarks>
    private bool TryUseFeedbackEmitter(out WeaponFeedbackEmitter emitter)
    {
        emitter = ResolveFeedbackEmitter();
        return emitter != null && emitter.HasFeedback;
    }

    /// <summary>
    /// 현재 탄약 UI를 갱신합니다.
    /// </summary>
    public void UpdateBulletUI()
    {
        if (m_bulletText != null)
        {
            m_bulletText.text = m_currentBullet.ToString();
        }

        OnBulletChanged?.Invoke(m_currentBullet, m_maxBullet);
    }

    /// <summary>
    /// 탄약 표시용 UI 텍스트를 교체하고 즉시 갱신합니다.
    /// </summary>
    /// <param name="targetUI">새로 사용할 탄약 UI 텍스트입니다.</param>
    public void SetBulletUI(Text targetUI)
    {
        m_bulletText = targetUI;
        UpdateBulletUI();
    }

    /// <summary>
    /// 현재 탄약 수를 설정합니다.
    /// </summary>
    /// <param name="value">새 현재 탄약 수입니다.</param>
    public void SetCurrentBullet(int value)
    {
        m_currentBullet = Mathf.Clamp(value, 0, m_maxBullet);
        UpdateBulletUI();
    }

    /// <summary>
    /// 최대 탄약 수를 설정합니다.
    /// </summary>
    /// <param name="value">새 최대 탄약 수입니다.</param>
    public void SetMaxBullet(int value)
    {
        m_maxBullet = Mathf.Max(0, value);
        m_currentBullet = Mathf.Clamp(m_currentBullet, 0, m_maxBullet);
        UpdateBulletUI();
    }

    /// <summary>
    /// 사격 지연 시간을 설정합니다.
    /// </summary>
    /// <param name="value">새 사격 지연 시간입니다.</param>
    public void SetShootDelay(float value)
    {
        m_shootDelay = Mathf.Max(0.0f, value);
    }

    /// <summary>
    /// 재장전 시간을 설정합니다.
    /// </summary>
    /// <param name="value">새 재장전 시간입니다.</param>
    public void SetReloadTime(float value)
    {
        m_reloadTime = Mathf.Max(0.0f, value);
    }

    /// <summary>
    /// 탄약이 최대치일 때도 재장전을 허용할지 여부를 설정합니다.
    /// </summary>
    /// <param name="value">풀 탄창 재장전을 허용하면 <c>true</c>입니다. 기본값은 <c>false</c>이며 디버그 용도입니다.</param>
    public void SetAllowFullMagReload(bool value)
    {
        m_allowFullMagReload = value;
    }

    // ─────────────────────────────────────────────────────────────
    // 플레이테스트 트레이너용 런타임 스탯 setter. 값 보정 규칙은 ClampBulletValues와 일치시킵니다.
    // ─────────────────────────────────────────────────────────────

    /// <summary>이번 무기의 최소 힙파이어 방사각(도)입니다.</summary>
    public float HipfireMinSpread => m_hipfireMinSpread;

    /// <summary>이번 무기의 최대 힙파이어 방사각(도)입니다.</summary>
    public float HipfireMaxSpread => m_hipfireMaxSpread;

    /// <summary>이번 무기의 최소 ADS 방사각(도)입니다.</summary>
    public float AdsMinSpread => m_adsMinSpread;

    /// <summary>이번 무기의 최대 ADS 방사각(도)입니다.</summary>
    public float AdsMaxSpread => m_adsMaxSpread;

    /// <summary>히트스캔 사격 피해량을 설정합니다. 음수는 0으로 보정합니다.</summary>
    /// <param name="value">새 피해량입니다.</param>
    public void SetHitscanDamage(int value) => m_hitscanDamage = value;

    /// <summary>이 무기의 약점 판정 사용 여부를 설정합니다.</summary>
    /// <param name="value">약점 판정을 쓰면 true입니다.</param>
    public void SetAllowHeadshot(bool value) => m_allowHeadshot = value;

    /// <summary>헤드샷 피해 배율을 설정합니다. 음수는 0으로 보정합니다.</summary>
    /// <param name="value">새 배율입니다.</param>
    public void SetHeadshotDamageMultiplier(float value) => m_headshotDamageMultiplier = value;

    /// <summary>히트스캔 사거리를 설정합니다. 음수는 0으로 보정합니다.</summary>
    /// <param name="value">새 사거리입니다.</param>
    public void SetHitscanRange(float value) => m_hitscanRange = value;

    /// <summary>세로(피치) 반동 각도(도)를 설정합니다.</summary>
    /// <param name="value">새 반동 각도입니다.</param>
    public void SetRecoilPitchKick(float value) => m_recoilPitchKick = value;

    /// <summary>좌우(요) 반동 각도(도)를 설정합니다. 음수는 0으로 보정합니다.</summary>
    /// <param name="value">새 반동 각도입니다.</param>
    public void SetRecoilYawKick(float value) => m_recoilYawKick = value;

    /// <summary>카메라 롤(Dutch) 시각 킥 크기(도)를 설정합니다. 음수는 0으로 보정합니다.</summary>
    /// <param name="value">새 시각 킥 크기입니다.</param>
    public void SetRecoilRoll(float value) => m_recoilRoll = value;

    /// <summary>카메라 FOV 펀치 시각 킥 크기(도)를 설정합니다. 음수는 0으로 보정합니다.</summary>
    /// <param name="value">새 FOV 펀치 크기입니다.</param>
    public void SetRecoilFovPunch(float value) => m_recoilFovPunch = value;

    /// <summary>조준 중 FOV 펀치 크기를 설정합니다.</summary>
    public void SetRecoilFovPunchAds(float value) => m_recoilFovPunchAds = Mathf.Max(0.0f, value);

    /// <summary>좌우 반동 방향 패턴을 설정합니다.</summary>
    /// <param name="value">새 반동 방향 패턴입니다.</param>
    public void SetYawKickPattern(KickSidePattern value) => m_yawKickPattern = value;

    /// <summary>카메라 롤 방향 패턴을 설정합니다.</summary>
    /// <param name="value">새 시각 킥 방향 패턴입니다.</param>
    public void SetRollKickPattern(KickSidePattern value) => m_rollKickPattern = value;

    /// <summary>히트스캔 충돌 판정 레이어 마스크를 설정합니다.</summary>
    /// <param name="value">새 충돌 판정 레이어 마스크입니다.</param>
    public void SetHitscanLayerMask(LayerMask value) => m_hitscanLayerMask = value;

    /// <summary>탄퍼짐 콘 내부의 분포 방식을 설정합니다.</summary>
    /// <param name="value">새 분포 방식입니다.</param>
    public void SetSpreadDistribution(SpreadDistribution value) => m_spreadDistribution = value;

    /// <summary>Gaussian 탄퍼짐 분포의 중심 집중도를 설정합니다.</summary>
    /// <param name="value">1보다 작은 값은 1로 보정됩니다.</param>
    public void SetSpreadConcentration(float value) => m_spreadConcentration = value;

    /// <summary>힙파이어 최소/최대 방사각(도)을 설정합니다. max는 min 이상으로 보정합니다.</summary>
    /// <param name="minSpread">새 최소 방사각입니다.</param>
    /// <param name="maxSpread">새 최대 방사각입니다.</param>
    public void SetHipfireSpread(float minSpread, float maxSpread)
    {
        m_hipfireMinSpread = Mathf.Max(0.0f, minSpread);
        m_hipfireMaxSpread = Mathf.Max(m_hipfireMinSpread, maxSpread);
    }

    /// <summary>ADS 최소/최대 방사각(도)을 설정합니다. max는 min 이상으로 보정합니다.</summary>
    /// <param name="minSpread">새 최소 방사각입니다.</param>
    /// <param name="maxSpread">새 최대 방사각입니다.</param>
    public void SetAdsSpread(float minSpread, float maxSpread)
    {
        m_adsMinSpread = Mathf.Max(0.0f, minSpread);
        m_adsMaxSpread = Mathf.Max(m_adsMinSpread, maxSpread);
    }

    /// <summary>힙파이어 연사 탄퍼짐 누적과 회복 규칙을 설정합니다.</summary>
    /// <param name="minSpreadShotCount">최소 탄퍼짐을 유지할 첫 연속 발사 수입니다.</param>
    /// <param name="spreadIncreasePerShot">그 이후 발마다 누적할 방사각입니다.</param>
    /// <param name="recoveryDelay">사격 중단 후 회복 시작 지연 시간입니다.</param>
    /// <param name="recoveryPerSecond">초당 회복 방사각입니다.</param>
    public void SetHipfireBurstSpread(
        int minSpreadShotCount,
        float spreadIncreasePerShot,
        float recoveryDelay,
        float recoveryPerSecond)
    {
        m_hipfireMinSpreadShotCount = Mathf.Max(0, minSpreadShotCount);
        m_hipfireSpreadIncreasePerShot = Mathf.Max(0.0f, spreadIncreasePerShot);
        m_hipfireSpreadRecoveryDelay = Mathf.Max(0.0f, recoveryDelay);
        m_hipfireSpreadRecoveryPerSecond = Mathf.Max(0.0f, recoveryPerSecond);
    }

    /// <summary>ADS 연사 탄퍼짐 누적과 회복 규칙을 설정합니다.</summary>
    /// <param name="minSpreadShotCount">최소 탄퍼짐을 유지할 첫 연속 발사 수입니다.</param>
    /// <param name="spreadIncreasePerShot">그 이후 발마다 누적할 방사각입니다.</param>
    /// <param name="recoveryDelay">사격 중단 후 회복 시작 지연 시간입니다.</param>
    /// <param name="recoveryPerSecond">초당 회복 방사각입니다.</param>
    public void SetAdsBurstSpread(
        int minSpreadShotCount,
        float spreadIncreasePerShot,
        float recoveryDelay,
        float recoveryPerSecond)
    {
        m_adsMinSpreadShotCount = Mathf.Max(0, minSpreadShotCount);
        m_adsSpreadIncreasePerShot = Mathf.Max(0.0f, spreadIncreasePerShot);
        m_adsSpreadRecoveryDelay = Mathf.Max(0.0f, recoveryDelay);
        m_adsSpreadRecoveryPerSecond = Mathf.Max(0.0f, recoveryPerSecond);
    }

#if UNITY_EDITOR
    /// <summary>
    /// 지금 인스펙터에 들어 있는 값을 이 컴포넌트가 물고 있는 밸런스 SO와 CSV로 되돌려 씁니다.
    /// </summary>
    /// <remarks>
    /// 플레이테스트로 잡은 값을 정본으로 승격시키는 용도입니다.
    /// 이 작업을 하지 않으면 인스펙터에서 만진 값은 다음 실행의 Awake에서 SO 값에 덮여 사라집니다.
    /// 에디터 전용입니다. SO와 CSV는 프로젝트 자산이라 빌드에서는 쓸 수 없습니다.
    /// </remarks>
    [ContextMenu("밸런스: 현재 인스펙터 → SO + CSV 갱신")]
    private void ReverseSyncBalanceToAsset()
    {
        UnityEngine.Debug.Log($"[BalanceReverseSync] {name}: {BalanceReverseSyncHook.Run(this)}", this);
    }
#endif
}
