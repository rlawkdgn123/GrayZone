using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Serialization;
using VInspector;

/// <summary>
/// 플레이어의 조준 카메라, 조준 UI, 조준 방향 회전, IK 리그, 사격 및 재장전 입력을 제어하는 컴포넌트입니다.
/// </summary>
/// <remarks>
/// 이 컴포넌트는 <see cref="PlayerInputController"/>, <see cref="ThirdPersonController"/>,
/// <see cref="Animator"/>, <see cref="AudioSource"/>를 같은 GameObject의 필수 참조로 사용합니다.
/// 필수 참조는 <c>Awake</c>에서 캐싱하고, 누락 시 컴포넌트를 비활성화하여 런타임 null 참조를 방지합니다.
/// </remarks>
[RequireComponent(typeof(PlayerInputController))]
[RequireComponent(typeof(ThirdPersonController))]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(AudioSource))]
public class AimController : MonoBehaviour, ISharedBalanceReceiver
{
    [Tooltip("이 플레이어에 적용할 공용 밸런스 SO입니다. 비어 있으면 Inspector 값을 그대로 씁니다.")]
    [SerializeField] private PlayerCommonBalanceSO m_balanceSO;

    private const int WeaponLayerIndex = 1;

    /// <summary>
    /// 사격 반동을 더하는 Additive 레이어의 인덱스입니다.
    /// </summary>
    /// <remarks>
    /// 반동을 무기(Action) 레이어와 나눈 이유는 두 요구가 정반대이기 때문입니다.
    /// 재장전은 상체를 통째로 <b>교체</b>해야 하므로 Override여야 하고, 반동은 지금 자세(웅크림·보행)를
    /// <b>보존한 채</b> 얹혀야 하므로 Additive여야 합니다. 한 레이어로 두면 한쪽을 맞출 때 다른 쪽이 깨집니다.
    /// </remarks>
    private const int RecoilLayerIndex = 2;

    /// <summary>
    /// 공중 조준 자세를 담당하는 애니메이터 레이어 인덱스입니다.
    /// </summary>
    /// <remarks>
    /// 지상에서는 Base Layer의 조준 트리가 상하체가 붙은 전신 조준 클립을 갖고 있어 별도 레이어가 필요 없습니다.
    /// 공중에는 그 클립이 없고 Base Layer가 점프 클립을 재생해야 하므로, 다리는 Base Layer에 두고 상체만
    /// 이 레이어로 덮어 조준 자세를 만듭니다. 마스크는 몸통을 포함하고 다리를 제외합니다.
    ///
    /// 사격 자세는 이 레이어에 두지 않습니다. 사격 중에도 상체는 조준 자세를 유지하고 반동은
    /// <see cref="RecoilLayerIndex"/>의 Additive 레이어가 얹습니다. 지상과 같은 역할 분담이며,
    /// 같은 클립을 Override와 Additive 양쪽에 걸어 이중 적용되는 것을 막습니다.
    /// </remarks>
    private const int AirActionLayerIndex = 3;

    // 이 시간(초) 이상 사격이 끊기면 좌우 킥 번갈이 패턴을 첫 발부터 다시 시작합니다.
    private const float KickPatternResetGap = 0.25f;

    private static readonly int AnimIDShoot = Animator.StringToHash("IsShoot");
    private static readonly int AnimIDReload = Animator.StringToHash("DoReload");

    /// <summary>
    /// Base Layer의 점프 계열 상태들입니다. 상체를 공중용으로 덮어야 하는 구간을 판정하는 데 씁니다.
    /// </summary>
    /// <remarks>
    /// 접지 판정만으로는 부족합니다. 착지하는 순간 <c>Grounded</c>는 곧바로 참이 되지만 Base Layer는
    /// <c>JumpLand</c>로 들어가 클립의 70% 지점까지 머문 뒤에야 지상 이동 블렌드로 넘어갑니다.
    /// 그 사이 Base Layer가 재생하는 것은 팔을 내린 착지 클립이라, 접지 판정만 보고 상체 레이어를 내리면
    /// 조준이나 사격을 유지하고 있어도 팔이 한 번 내려갔다 올라옵니다.
    /// 이륙 쪽도 같은 이유로 <c>JumpStart</c>를 함께 봅니다.
    /// </remarks>
    private static readonly int[] JumpMotionStateHashes =
    {
        Animator.StringToHash("Base Layer.JumpStart"),
        Animator.StringToHash("Base Layer.InAir"),
        Animator.StringToHash("Base Layer.JumpLand"),
    };

    /// <summary>
    /// 재장전 중인지 여부입니다. 애니메이터가 재장전 스테이트에 들어가고 나오는 조건입니다.
    /// </summary>
    /// <remarks>
    /// 트리거만으로는 부족합니다. 애니메이터의 재장전 진입 조건이 <c>DoReload AND IsReload</c>이고
    /// 이탈 조건이 <c>IfNot IsReload</c>이므로, 이 값을 세우지 않으면 재장전 스테이트에 아예 들어가지 못합니다.
    ///
    /// 들어가지 못하면 클립의 재장전 완료 이벤트도 오지 않고, 그 이벤트가 <see cref="Reload"/>를 통해
    /// 재장전 상태를 내리는 <b>유일한 경로</b>여서 재장전이 영구히 끝나지 않습니다. 그러면 사격도 막힙니다.
    /// </remarks>
    private static readonly int AnimIDIsReload = Animator.StringToHash("IsReload");

    /// <summary>
    /// 재장전 스테이트의 재생 배속 파라미터(Speed Multiplier)입니다.
    /// </summary>
    /// <remarks>
    /// 재장전 소요 시간의 정본은 <see cref="Gun.ReloadTime"/>이고, 애니메이션이 그 시간에 맞춰 배속됩니다.
    /// 애니메이터 컨트롤러의 재장전 스테이트가 이 파라미터를 Speed Multiplier로 물고 있어야 하며,
    /// 물려 있지 않으면 배속이 적용되지 않고 애니메이션만 1배속으로 남습니다(게이지는 여전히 정본을 따릅니다).
    /// </remarks>
    private static readonly int AnimIDReloadSpeed = Animator.StringToHash("ReloadSpeed");

    /// <summary>1배속 클립에서 재장전 완료 이벤트가 오는 시점(초)입니다. 아직 조회하지 않았으면 음수입니다.</summary>
    private float m_reloadEventTimeAtUnitSpeed = -1.0f;

    /// <summary>연사 명중음이 겹치지 않도록 재사용하는 플레이어용 히트 확인음 인스턴스입니다.</summary>
    private FMOD.Studio.EventInstance m_hitConfirmInstance;

    /// <summary>히트 확인음 이벤트 누락 경고를 한 번만 출력했는지 여부입니다.</summary>
    private bool m_loggedMissingHitConfirmEvent;

    /// <summary>FMOD 히트 확인음 이벤트에서 일반 명중과 헤드샷을 구분하는 로컬 파라미터입니다.</summary>
    private const string HitConfirmHeadshotParameter = "Headshot";

    /// <summary>
    /// 총을 드는 동안 사격을 막는 구간이 끝나는 시각(<see cref="Time.time"/> 기준)입니다.
    /// </summary>
    /// <remarks>
    /// 자유 시점에서 전투 자세로 들어갈 때만 갱신합니다. 전투 자세를 유지한 채 힙파이어와 ADS를 오갈 때는
    /// 총이 이미 올라와 있으므로 건드리지 않습니다.
    /// </remarks>
    private float m_weaponRaiseReadyTime;

    /// <summary>전투 시점 상태입니다. 조준선 디버그 캡처를 이 상태의 전환 시점에만 수행합니다.</summary>
    private enum CombatStance
    {
        /// <summary>비전투 자유 TPS 시점입니다.</summary>
        Free,

        /// <summary>힙파이어(비조준 사격) 백뷰입니다.</summary>
        Hipfire,

        /// <summary>ADS(조준) 백뷰입니다.</summary>
        Ads,
    }

    /// <summary>시각 킥(롤·FOV 펀치)의 회복 방식입니다. 실제 탄착에는 영향이 없습니다.</summary>
    public enum VisualKickRecoveryMode
    {
        /// <summary>반동과 같은 회복 속도를 공유합니다. 연사 중에는 0으로 안 꺼지고 밴드로 누적됩니다.</summary>
        MatchRecoil,

        /// <summary>발사 간격(ShootDelay) 기준 N발 안에 거의 회복합니다. 다음 발 전에 대부분 리셋되어 발당 펀치가 또렷합니다.</summary>
        PerShotReset,
    }

    /// <summary>캐릭터가 소유한 조준선의 탄퍼짐 표시 방식입니다.</summary>
    public enum CrosshairSpreadMode
    {
        /// <summary>현재 무기의 실제 탄퍼짐 방사각을 매 프레임 표시합니다.</summary>
        CurrentSpread,

        /// <summary>현재 자세에서 가능한 무기 최대 탄퍼짐 방사각을 고정으로 표시합니다.</summary>
        WeaponMaxSpread,
    }

    [Foldout("Aim Options")]
    [Tooltip("조준 중 활성화할 Cinemachine 카메라입니다.")]
    [FormerlySerializedAs("aimCam")]
    [SerializeField] private CinemachineCamera m_aimCamera;

    [Tooltip("조준선으로 사용할 UI 오브젝트입니다.")]
    [FormerlySerializedAs("aimImage")]
    [SerializeField] private GameObject m_aimImage;

    [Tooltip("켜면 조준/힙파이어 상태가 아니어도 조준선을 항상 표시합니다.")]
    [SerializeField] private bool m_showAimImageAlways = true;

    [Tooltip("탄퍼짐 조준선 UI 컨트롤러입니다. 비워두면 Aim Image 하위 또는 자기 하위에서 자동으로 찾습니다.")]
    [SerializeField] private CrosshairController m_crosshairController;

    [Foldout("Crosshair Presentation")]
    [Tooltip("이 캐릭터가 직접 조작될 때 공용 HUD에 적용할 보조 조준선 모양입니다. 표시값은 캐릭터가 소유하고 HUD는 그 값을 그리기만 합니다.")]
    [SerializeField] private CrosshairController.SubShape m_crosshairSubShape = CrosshairController.SubShape.RoundedCross;

    [Tooltip("이 캐릭터의 조준선이 탄퍼짐 콘에서 표시할 반경 기준입니다. ConeEdge면 실제 최대 탄퍼짐 경계를 표시합니다.")]
    [SerializeField] private CrosshairController.SpreadDisplayBasis m_crosshairSpreadDisplayBasis = CrosshairController.SpreadDisplayBasis.MostShots;

    [Tooltip("0 이상이면 이 캐릭터 조준선의 탄퍼짐 표시 배율을 고정합니다. -1이면 무기 분포에서 계산합니다. 실제 탄착은 바꾸지 않으며, 무기 밸런스를 바꿔도 의도적으로 유지할 UI 기준점에만 사용합니다.")]
    [SerializeField] private float m_crosshairSpreadDisplayFactorOverride = -1.0f;

    [Tooltip("현재 퍼짐을 따라갈지, 현재 자세에서 가능한 최대 퍼짐을 고정 원으로 표시할지 정합니다.")]
    [SerializeField] private CrosshairSpreadMode m_crosshairSpreadMode = CrosshairSpreadMode.CurrentSpread;

    [Tooltip("이 캐릭터 조준선의 중심 기본 여백(픽셀)입니다. 최대 퍼짐 경계 링을 정확히 맞출 때는 0으로 둡니다.")]
    [Clamp(Min = 0)]
    [SerializeField] private float m_crosshairCenterSpacePixels = 1.0f;

    [Tooltip("이 캐릭터 조준선이 Ring일 때의 기본 지름(픽셀)입니다. 최대 퍼짐 경계 링을 정확히 맞출 때는 0으로 둡니다.")]
    [Clamp(Min = 0)]
    [SerializeField] private float m_crosshairSubRingBaseDiameterPixels = 16.0f;

    [Tooltip("이 캐릭터가 직접 조작될 때 발사 펄스로 조준선을 추가로 벌릴지 여부입니다. 최대 퍼짐 고정 링은 false여야 실제 경계와 일치합니다.")]
    [SerializeField] private bool m_crosshairEnableShotRecoilPulse = true;

    [Tooltip("지향점(LookPoint)을 표시하거나 상체 회전 IK 타겟으로 사용할 오브젝트입니다. 캐릭터가 항상 바라보는 먼 지점을 따라갑니다.")]
    [FormerlySerializedAs("m_aimTarget")]
    [FormerlySerializedAs("aimObj")]
    [SerializeField] private GameObject m_lookTarget;

    [Tooltip("지향점(LookPoint)을 카메라 전방 이 거리에 항상 둡니다. 레이캐스트와 무관하게 늘 먼 지점을 바라보며, 무기 히트스캔 사거리보다 작으면 사거리만큼으로 보정됩니다.")]
    [FormerlySerializedAs("m_aimTargetDistance")]
    [FormerlySerializedAs("aimObjDis")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_lookDistance = 100.0f;

    [Tooltip("조준점(카메라 트레이스) 및 탄착점(총구 히트스캔) 판정에 사용할 레이어입니다. 비어 있으면 무기 히트스캔 레이어 또는 전체를 사용합니다.")]
    [FormerlySerializedAs("targetLayer")]
    [SerializeField] private LayerMask m_targetLayer;

    [Foldout("Hipfire Options")]
    [Tooltip("힙파이어 사격 후 전투 자세를 유지하는 시간(초)입니다. 0이면 사격을 멈추는 즉시 해제합니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_hipfireHoldDuration = 0.0f;

    [Foldout("Combat Zoom Options")]
    [Tooltip("ADS(조준) 시 백뷰 카메라 FOV입니다. 값이 작을수록 더 확대됩니다.")]
    [BalanceField]
    [Clamp(Min = 1)]
    [SerializeField] private float m_adsFov = 20.0f;

    [Tooltip("힙파이어(비조준) 시 백뷰 카메라 FOV입니다. 줌 없는 기본 시야 값(기본 30)입니다.")]
    [BalanceField]
    [Clamp(Min = 1)]
    [SerializeField] private float m_hipfireFov = 30.0f;

    [Tooltip("ADS↔힙파이어 전환 시 FOV 보간 속도입니다. 매우 크게 두면 즉시 전환에 가까워집니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_zoomLerpSpeed = 10.0f;

    [Foldout("Recoil Visual Kick Options")]
    [Tooltip("사격 중 캐릭터 상체에 얹을 반동 애니메이션의 세기입니다. 0이면 반동 동작이 없고 1이면 클립 그대로입니다. 조준·탄착에는 영향을 주지 않으며, 보이는 동작 크기만 바꿉니다.")]
    [Clamp(Min = 0, Max = 1)]
    [SerializeField] private float m_recoilAnimationWeight = 1.0f;

    [Tooltip("켜면 발사마다 실제 조준과 탄착에 영향을 주는 피치/요 반동을 적용합니다. 플레이테스트 트레이너에서 즉시 켜고 끌 수 있습니다.")]
    [SerializeField] private bool m_enableAimRecoil = true;

    [Tooltip("켜면 발사마다 카메라 롤과 FOV 펀치 시각 킥을 적용합니다. 실제 조준과 탄착에는 영향을 주지 않습니다.")]
    [SerializeField] private bool m_enableVisualKick = true;

    [Tooltip("시각 킥 회복 방식입니다. MatchRecoil=반동과 같은 속도(연사 중 밴드로 누적), PerShotReset=발사 간격 기준 N발 안에 회복(발당 리셋). Play Mode에서 바꿔가며 체감을 비교할 수 있습니다.")]
    [SerializeField] private VisualKickRecoveryMode m_visualKickRecoveryMode = VisualKickRecoveryMode.MatchRecoil;

    [Tooltip("PerShotReset일 때, 시각 킥이 거의(~95%) 회복되는 데 걸리는 발수(무기 ShootDelay 기준)입니다. 1이면 다음 발 전에 거의 리셋됩니다.")]
    [ShowIf(nameof(m_visualKickRecoveryMode), VisualKickRecoveryMode.PerShotReset)]
    [BalanceField]
    [Clamp(Min = 0.01)]
    [SerializeField] private float m_visualKickRecoverShots = 1.0f;

    [EndIf]
    [Tooltip("누적될 수 있는 카메라 롤(Dutch) 상한(도)입니다. 유지 없이 발당 순간 펀치 후 회복합니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_visualKickMaxRoll = 3.0f;

    [Tooltip("누적될 수 있는 FOV 펀치 상한(도)입니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_visualKickMaxFovPunch = 5.0f;

    [Tooltip("켜면 카메라 롤 킥의 상승과 회복을 곡선 하나로 처리합니다. 끄면 발사 순간 즉시 더하고 회복만 보간합니다.")]
    [SerializeField] private bool m_useRollKickEnvelope = false;

    [Tooltip("카메라 롤 킥 곡선 하나의 길이(초)입니다. 정규화 시간 0~1을 재는 기준입니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_rollKickEnvelopeDuration = 0.25f;

    [Tooltip("카메라 롤 킥 곡선입니다. x는 정규화 시간(0~1), y는 세기 배율입니다. y가 가장 큰 x가 피크 위치입니다.")]
    [SerializeField]
    private AnimationCurve m_rollKickEnvelopeCurve = ImpulseEnvelope.BuildCurve(0.2f, 1.0f, 1.0f);

    [Tooltip("켜면 FOV 펀치의 상승과 회복을 곡선으로 처리합니다. 힙파이어와 ADS를 따로 둡니다.")]
    [SerializeField] private bool m_useFovPunchEnvelope = false;

    [Tooltip("힙파이어 FOV 펀치 곡선 하나의 길이(초)입니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_hipfireFovPunchEnvelopeDuration = 0.2f;

    [Tooltip("힙파이어 FOV 펀치 곡선입니다.")]
    [SerializeField]
    private AnimationCurve m_hipfireFovPunchEnvelopeCurve = ImpulseEnvelope.BuildCurve(0.2f, 1.0f, 1.0f);

    [Tooltip("ADS FOV 펀치 곡선 하나의 길이(초)입니다. 조준 중에는 화면이 확대돼 같은 펀치도 더 크게 보이므로 따로 둡니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_adsFovPunchEnvelopeDuration = 0.2f;

    [Tooltip("ADS FOV 펀치 곡선입니다.")]
    [SerializeField]
    private AnimationCurve m_adsFovPunchEnvelopeCurve = ImpulseEnvelope.BuildCurve(0.2f, 1.0f, 1.0f);

    [Tooltip("켜면 ADS 확대·축소를 지속시간과 곡선으로 처리합니다. 끄면 기존처럼 FOV 전환 속도 하나로 양쪽을 함께 보간합니다.")]
    [SerializeField] private bool m_useZoomEnvelope = false;

    [Tooltip("ADS 진입(확대)에 걸리는 시간(초)입니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_zoomInDuration = 0.15f;

    [Tooltip("ADS 진입 곡선입니다. x는 진행률(0~1), y는 목표 FOV까지의 비율입니다. 0에서 시작해 1로 끝나야 합니다.")]
    [SerializeField]
    private AnimationCurve m_zoomInCurve = AnimationCurve.EaseInOut(0.0f, 0.0f, 1.0f, 1.0f);

    [Tooltip("ADS 해제(축소)에 걸리는 시간(초)입니다. 진입과 따로 둘 수 있어 빠르게 들어가고 느리게 나오는 식이 가능합니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_zoomOutDuration = 0.2f;

    [Tooltip("ADS 해제 곡선입니다. x는 진행률(0~1), y는 목표 FOV까지의 비율입니다.")]
    [SerializeField]
    private AnimationCurve m_zoomOutCurve = AnimationCurve.EaseInOut(0.0f, 0.0f, 1.0f, 1.0f);

    /// <summary>카메라 롤 킥 엔벨로프의 런타임 상태입니다.</summary>
    private readonly ImpulseEnvelope m_rollKickEnvelope = new ImpulseEnvelope();

    /// <summary>힙파이어에서 발생한 FOV 펀치 엔벨로프입니다.</summary>
    /// <remarks>
    /// ADS와 나눠 두는 이유는, 힙파이어에서 쏜 뒤 곧바로 조준하면 그 발의 곡선이
    /// ADS 지속시간으로 갈아타 도중에 모양이 바뀌기 때문입니다.
    /// </remarks>
    private readonly ImpulseEnvelope m_hipfireFovPunchEnvelope = new ImpulseEnvelope();

    /// <summary>ADS에서 발생한 FOV 펀치 엔벨로프입니다.</summary>
    private readonly ImpulseEnvelope m_adsFovPunchEnvelope = new ImpulseEnvelope();

    /// <summary>지금 진행 중인 FOV 전환의 시작 값입니다.</summary>
    private float m_zoomFromFov;

    /// <summary>지금 진행 중인 FOV 전환의 경과 시간(초)입니다.</summary>
    private float m_zoomElapsed;

    /// <summary>지난 프레임의 조준 상태입니다. 바뀐 프레임에 전환을 새로 시작하기 위한 것입니다.</summary>
    private bool m_zoomWasAds;

    /// <summary>
    /// 이 대원을 지금 플레이어가 직접 조작하고 있는지 여부입니다.
    /// </summary>
    /// <remarks>
    /// 예전에는 이 컴포넌트의 활성 여부가 그 표시를 겸했습니다. 조작하지 않는 대원은 컴포넌트를 껐습니다.
    /// 그런데 이 컴포넌트는 조작 전용(입력·조준 카메라·조준선)과 캐릭터 전용(리그 weight·애니메이터 레이어)을
    /// 함께 들고 있어서, 조작 전용을 끄려고 컴포넌트를 끄면 캐릭터 쪽 몫까지 같이 죽었습니다. 꺼진 동안의
    /// 몫을 AI 경로가 따로 구현하면서 같은 규칙이 두 벌이 됐고, 꺼진 컴포넌트에는 애니메이션 이벤트도
    /// 배달되지 않아 재장전 종료 신호가 유실됐습니다.
    ///
    /// 그래서 컴포넌트는 항상 켜 두고 조작 여부만 이 값으로 표시합니다.
    /// <see cref="SquadMemberController"/>가 <see cref="SetPlayerControlled"/>로 설정합니다.
    /// </remarks>
    private bool m_isPlayerControlled;

    // 화면 HUD 자체는 하나지만, 마지막으로 어떤 캐릭터의 표시값을 적용했는지는 명시적으로 기억해야
    // 대원 전환 시 이전 대원의 모양/기준이 남지 않습니다.
    private static AimController s_crosshairPresentationOwner;
    private static CrosshairController s_crosshairPresentationTarget;
    private static CrosshairController.SubShape s_crosshairPresentationSubShape;
    private static CrosshairController.SpreadDisplayBasis s_crosshairPresentationSpreadBasis;
    private static float s_crosshairPresentationDisplayFactorOverride;
    private static float s_crosshairPresentationCenterSpacePixels;
    private static float s_crosshairPresentationSubRingBaseDiameterPixels;
    private static bool s_crosshairPresentationShotRecoilPulse;

    /// <summary>상체 조준(허리) 리그 weight의 목표값입니다.</summary>
    private float m_rigWeightTarget;

    /// <summary>스킬 등 외부 효과가 적용하는 재장전·전투 자세 전환 속도 배율입니다.</summary>
    private float m_actionSpeedMultiplier = 1.0f;

    /// <summary>지금 적용 중인 상체 조준(허리) 리그 weight입니다.</summary>
    private float m_rigWeight;

    /// <summary>손 IK 리그 weight의 목표값입니다.</summary>
    /// <remarks>
    /// 허리와 따로 두는 이유는 재장전 때 둘이 반대가 되기 때문입니다. 허리는 조준 방향을 계속 바라봐야 하고,
    /// 손은 총을 겨눈 자리에서 풀려 탄창을 다뤄야 합니다.
    /// </remarks>
    private float m_handRigWeightTarget;

    /// <summary>지금 적용 중인 손 IK 리그 weight입니다.</summary>
    private float m_handRigWeight;

    /// <summary>상체(무기) 레이어 weight의 목표값입니다.</summary>
    private float m_weaponLayerTarget;

    /// <summary>지금 적용 중인 상체(무기) 레이어 weight입니다.</summary>
    private float m_weaponLayerWeight;

    /// <summary>지금 적용 중인 공중 조준 레이어 weight입니다. 목표는 <see cref="ResolveAirActionLayerTarget"/>가 정합니다.</summary>
    private float m_airActionLayerWeight;

    /// <summary>
    /// 이번 프레임에 공중 자세 보정이 필요한지입니다. <see cref="RefreshAirMotionState"/>가 프레임마다 한 번 갱신합니다.
    /// </summary>
    /// <remarks>
    /// 애니메이터 상태 조회가 들어가므로 weight를 정하는 함수들이 각자 부르지 않고 이 값을 나눠 씁니다.
    /// </remarks>
    private bool m_airMotionActive;

    /// <summary>반동(Additive) 레이어 weight의 목표값입니다.</summary>
    private float m_recoilLayerTarget;

    /// <summary>지금 적용 중인 반동(Additive) 레이어 weight입니다.</summary>
    private float m_recoilLayerWeight;

    /// <summary>지난 프레임의 전력질주 입력입니다. 누름 시점을 잡기 위한 것입니다.</summary>
    private bool m_sprintWasHeld;

    /// <summary>지난 프레임의 조준/사격 입력입니다. 누름 시점을 잡기 위한 것입니다.</summary>
    private bool m_combatWasHeld;

    /// <summary>전력질주가 조준/사격보다 최근 입력이어서 우선하는 상태인지입니다.</summary>
    private bool m_sprintOverridesCombat;

    /// <summary>조준 트레이스 결과를 재사용하는 버퍼입니다. 전투 자세 동안 매 프레임 도는 경로라 할당을 피합니다.</summary>
    private readonly RaycastHit[] m_aimTraceBuffer = new RaycastHit[AimTraceBufferSize];

    /// <summary>조준 트레이스 버퍼 크기입니다.</summary>
    private const int AimTraceBufferSize = 24;


    [Foldout("IK Options")]
    [Tooltip("켜면 공중에서도 조준·사격 시 상체 조준 리그와 손 IK를 지상과 같은 방식으로 올립니다. 끄면 공중에 있는 동안만 두 리그를 0으로 내립니다. 어느 쪽이든 즉시 바뀌지 않고 자세 전환 시간으로 보간합니다.")]
    [SerializeField] private bool m_enableCombatRigInAir = true;

    [Tooltip("켜면 공중에서 전투 자세일 때 상체(Action) 레이어를 올려 조준 자세를 냅니다. 지상은 Base Layer의 조준 트리가 그 자세를 갖고 있지만 점프·낙하 상태에는 없어서, 켜지 않으면 공중에서 상체가 점프 자세로 남습니다.")]
    [SerializeField] private bool m_useWeaponLayerAimPoseInAir = true;

    [Tooltip("손 위치 보정에 사용할 Rig입니다.")]
    [FormerlySerializedAs("handRig")]
    [SerializeField] private Rig m_handRig;

    [Tooltip("조준 자세 보정에 사용할 Rig입니다.")]
    [FormerlySerializedAs("aimRig")]
    [SerializeField] private Rig m_aimRig;

    [Tooltip("기존 상체 IK 처리 후 총구와 실제 조준 방향의 각도 오차를 보정하는 최대 각도입니다. 0이면 보정하지 않습니다. 가까운 상체 뼈 하나만 돌려 자세가 과도하게 꺾이지 않도록 제한합니다.")]
    [Range(0.0f, 45.0f)]
    [SerializeField] private float m_muzzleAlignmentMaxAngle = 30.0f;

    private Transform m_muzzleAlignmentBone;
    private Quaternion m_preAlignmentLocalRotation;
    private Quaternion m_muzzleAlignmentOffset = Quaternion.identity;
    private bool m_hasMuzzleAlignment;
    private bool m_combatShotPending;
    private bool m_fireRequested;
    private readonly System.Collections.Generic.List<Gun.HitscanShotInfo> m_firedPelletBuffer = new(8);
    private bool m_wasShootPressed;

    /// <summary>
    [Tooltip("전투 자세 진입/이탈 시 상체 레이어와 IK 리그 weight가 오르내리는 데 걸리는 시간입니다. 0이면 즉시 바뀝니다.")]
    [Clamp(Min = 0)]
    [SerializeField] private float m_stanceBlendDuration = 0.15f;

    [Tooltip("공중 상체 레이어가 0에서 1로 올라오는 데 걸리는 시간입니다. 내려갈 때는 이 값이 아니라 자세 전환 시간을 씁니다. 애니메이터가 지상 조준 블렌드에서 JumpStart로 넘어가는 전이가 0.07초라, 이 값이 그보다 길면 상체가 점프 자세로 바뀐 뒤에도 레이어가 덜 올라와 조준이 한 번 풀립니다.")]
    [Clamp(Min = 0)]
    [SerializeField] private float m_airActionLayerRiseDuration = 0.06f;

    [Foldout("Audio Options")]
    [Tooltip("사격 사운드입니다. 실제 사격 사운드를 Gun가 처리한다면 비워둘 수 있습니다.")]
    [FormerlySerializedAs("shootingSound")]
    [SerializeField] private AudioClip m_shootingSound;

    [Tooltip("직접 조작 중인 플레이어가 적을 맞혔을 때 재생할 2D FMOD 히트 확인음입니다.")]
    [SerializeField] private FMODUnity.EventReference m_hitConfirmEvent;

    [Tooltip("재장전 애니메이션 이벤트에서 사용할 사운드 배열입니다. 0: 탄창 제거, 1: 탄창 삽입, 2: 재장전 완료.")]
    [FormerlySerializedAs("reloadSound")]
    [SerializeField] private AudioClip[] m_reloadSounds;

    [Foldout("Debug")]
    [Tooltip("조준 중 총구→탄착점 히트스캔 레이를 그립니다.")]
    [SerializeField] private bool m_drawHitscanDebugRay = true;

    [Tooltip("카메라에서 조준점까지의 트레이스 선을 그립니다(캠→조준점). 총구 기준 탄착점 레이와 얼마나 벌어지는지 확인용입니다.")]
    [SerializeField] private bool m_drawAimTraceLine = false;

    [Tooltip("지향점(논리 조준, 킥 제거: green) 레이와 단순 카메라 forward(렌더 방향, 킥 포함: blue) 레이를 함께 그립니다. 사격 시 두 선이 벌어지면 카메라 킥이 에임과 분리된 것이고, 안 벌어지면 킥이 활성 카메라에 안 닿은 것입니다.")]
    [SerializeField] private bool m_drawCameraForwardRay = false;

    [Tooltip("지향점(캐릭터가 항상 바라보는 먼 지점)에 디버그 스피어를 그립니다.")]
    [SerializeField] private bool m_drawLookPointSphere = false;

    [Tooltip("조준점(카메라 트레이스가 잡은 실제 사격 목표)에 디버그 스피어를 그립니다.")]
    [SerializeField] private bool m_drawAimPointSphere = false;

    [Tooltip("탄착점(총구 히트스캔이 실제로 끝나는 지점)에 디버그 스피어를 그립니다.")]
    [SerializeField] private bool m_drawImpactPointSphere = false;

    [Tooltip("디버그 스피어의 반지름입니다.")]
    [SerializeField] private float m_debugSphereRadius = 0.15f;

    [Tooltip("사격이 실제로 발사될 때 탄착점에 디버그 마커 오브젝트를 생성합니다.")]
    [SerializeField] private bool m_spawnImpactMarkerOnShot = false;

    [Tooltip("사격 시 탄착점에 생성할 디버그 오브젝트(스피어 등)입니다. 비어 있으면 생성을 생략합니다.")]
    [SerializeField] private GameObject m_impactMarkerPrefab;

    [Tooltip("임팩트 마커를 유지할 시간(초)입니다. 프리팹이 비어 있으면 런타임 디버그 스피어에도 적용됩니다.")]
    [Min(0.05f)]
    [SerializeField] private float m_impactMarkerLifetime = 1.5f;

    [Tooltip("프리팹이 비어 있을 때 생성하는 런타임 디버그 스피어의 지름(월드 단위)입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_impactMarkerSize = 0.16f;

    private PlayerInputController m_input;
    private ThirdPersonController m_controller;
    private Animator m_animator;
    private AudioSource m_weaponAudioSource;
    private Gun m_weaponController;
    private ChungSolDragonBreathSkill m_dragonBreathSkill;
    private Camera m_mainCamera;
    private EnemyController m_currentAimEnemy;
    private Vector3 m_currentAimPoint;

    /// <summary>
    /// 카메라 트레이스가 조준점으로 잡은 대상의 체력 소유자입니다. 대상이 없으면 <c>null</c>입니다.
    /// </summary>
    /// <remarks>
    /// 차단 마커 판정에만 씁니다. 총구와 카메라는 위치가 달라, 같은 대상을 겨눠도 총구 레이가 그 대상의
    /// 다른 부위(어깨·팔·히트박스)를 조준점보다 앞에서 스칩니다. 대상을 모른 채 거리만 비교하면 그것이
    /// "중간 장애물"로 잡혀 겨눈 적 위에 차단 마커가 뜹니다. 겨눈 대상 본인은 장애물이 아니므로 제외합니다.
    ///
    /// 루트 Transform이 아니라 체력 소유자로 비교합니다. 적이 스폰 풀 자식으로 들어가면 루트가 스폰 포인트라
    /// 범위가 통째로 넓어집니다. 체력 소유자는 부위 히트박스와 몸통이 같은 하나로 모이는 자연스러운 경계입니다.
    /// </remarks>
    private HealthSystemBase m_currentAimTargetHealth;
    private bool m_hasRequiredReferences;
    private bool m_inCombatStance;
    private bool m_isAds;
    private float m_hipfireTimer;
    private CombatStance m_lastCombatStance = CombatStance.Free;

    /// <summary>시각 킥(FOV 펀치)을 얹기 전의 기준 전투 FOV입니다. ADS/힙파이어 목표로 보간됩니다.</summary>
    private float m_baseFov = 60.0f;

    /// <summary>현재 카메라 롤(Dutch) 시각 킥 오프셋(도)입니다. 0으로 회복합니다. 에임/탄 무영향.</summary>
    private float m_visualKickRoll;

    /// <summary>현재 FOV 펀치 시각 킥 오프셋(도)입니다. 0으로 회복합니다. 에임/탄 무영향.</summary>
    private float m_visualKickFovPunch;

    /// <summary>좌우 킥 번갈이 패턴의 발 인덱스입니다. 버스트 간격이 벌어지면 리셋됩니다.</summary>
    private int m_kickShotIndex;

    /// <summary>마지막 킥 시각입니다. 버스트 사이 간격이 벌어지면 좌우 패턴을 첫 발부터 다시 시작합니다.</summary>
    private float m_lastKickTime = float.NegativeInfinity;

    /// <summary>조준 카메라 참조입니다.</summary>
    public CinemachineCamera AimCamera => m_aimCamera;

    /// <summary>조준 UI 오브젝트 참조입니다.</summary>
    public GameObject AimImage => m_aimImage;

    /// <summary>지향점(LookPoint)을 따라가는 상체 회전 IK 타겟 오브젝트 참조입니다.</summary>
    public GameObject LookTarget => m_lookTarget;

    /// <summary>지향점(LookPoint)을 둘 카메라 전방 거리입니다.</summary>
    public float LookDistance => m_lookDistance;

    /// <summary>조준점/탄착점 판정 레이어입니다.</summary>
    public LayerMask TargetLayer => m_targetLayer;

    /// <summary>탄퍼짐·피격 피드백 표시를 담당하는 선택형 조준선 컨트롤러입니다.</summary>
    public CrosshairController CrosshairController => m_crosshairController;

    /// <summary>조준/힙파이어 상태가 아니어도 조준선을 항상 표시할지 여부입니다.</summary>
    public bool ShowAimImageAlways => m_showAimImageAlways;

    /// <summary>힙파이어 사격 후 전투 자세를 유지하는 시간입니다.</summary>
    public float HipfireHoldDuration => m_hipfireHoldDuration;

    /// <summary>ADS 카메라 기본 FOV입니다.</summary>
    public float AdsFov => m_adsFov;

    /// <summary>힙파이어 카메라 기본 FOV입니다.</summary>
    public float HipfireFov => m_hipfireFov;

    /// <summary>ADS와 힙파이어 FOV 전환 보간 속도입니다.</summary>
    public float ZoomLerpSpeed => m_zoomLerpSpeed;

    /// <summary>현재 히트스캔이 조준 중인 적입니다.</summary>
    public EnemyController CurrentAimEnemy => m_currentAimEnemy;

    /// <summary>카메라 크로스헤어가 가리키는 현재 월드 조준점입니다.</summary>
    public Vector3 CurrentAimPoint => m_currentAimPoint;

    /// <summary>사격 시 재생할 효과음입니다. 지정하지 않았으면 <c>null</c>입니다.</summary>
    /// <remarks>무기별 사운드는 <see cref="WeaponFeedbackEmitter"/>가 담당하고, 이 값은 캐릭터 쪽 보조 배선입니다.</remarks>
    public AudioClip ShootingSound => m_shootingSound;

    /// <summary>재장전 사운드 클립 배열입니다.</summary>
    public AudioClip[] ReloadSounds => m_reloadSounds;

    /// <summary>실제 조준과 탄착에 영향을 주는 반동 적용 여부입니다.</summary>
    /// <summary>사격 중 상체에 얹는 반동 애니메이션의 세기입니다. 조준·탄착과는 무관합니다.</summary>
    public float RecoilAnimationWeight => m_recoilAnimationWeight;

    public bool AimRecoilEnabled => m_enableAimRecoil;

    /// <summary>
    /// 총을 드는 중이라 사격이 막혀 있는지 여부입니다.
    /// </summary>
    /// <remarks>
    /// 자유 시점에서 전투 자세로 들어간 직후 ADS 줌인 시간만큼 <c>true</c>입니다. 이 동안에는 발사도,
    /// 탄약·탄퍼짐 누적·발수 카운트도 진행되지 않습니다. 전투 자세 안에서의 힙파이어↔ADS 전환은 해당하지 않습니다.
    /// </remarks>
    public bool IsRaisingWeapon => Time.time < m_weaponRaiseReadyTime;

    /// <summary>
    /// 완전히 내려간 상태에서 총을 다 들 때까지 걸리는 시간(초)입니다. 사격 차단 구간의 최대 길이와 같습니다.
    /// </summary>
    /// <remarks>줌 방식(곡선/속도)에 따라 근거가 달라지므로 계산된 결과를 그대로 노출합니다. 디버그 표시용입니다.</remarks>
    public float WeaponRaiseDuration => ResolveWeaponRaiseDuration();

    /// <summary>
    /// 현재 재장전 시간에 맞춰 계산된 재장전 애니메이션 배속입니다.
    /// </summary>
    /// <remarks>
    /// 1이면 클립 원래 속도입니다. 재장전 시간을 바꾸면 이 값이 따라 움직이므로, 조정 결과를 눈으로 확인하는 데 씁니다.
    /// 완료 이벤트를 찾지 못했거나 재장전 시간이 0이면 1을 반환합니다.
    /// </remarks>
    public float ReloadAnimationSpeed
    {
        get
        {
            float reloadTime = m_weaponController != null ? m_weaponController.ActiveReloadDuration : 0.0f;
            float eventTime = ResolveReloadEventTimeAtUnitSpeed();
            return reloadTime > 0.0f && eventTime > 0.0f ? eventTime / reloadTime : 1.0f;
        }
    }

    /// <summary>카메라 롤과 FOV 펀치로 구성된 시각 킥 적용 여부입니다.</summary>
    public bool VisualKickEnabled => m_enableVisualKick;

    private bool IsVisualKickAllowed => m_enableVisualKick
        && (GameSettingManager.Instance == null || GameSettingManager.Instance.CameraKickEnabled);

    /// <summary>시각 킥 회복 방식입니다.</summary>
    public VisualKickRecoveryMode CurrentVisualKickRecoveryMode => m_visualKickRecoveryMode;

    /// <summary>PerShotReset 회복에 사용할 발수입니다.</summary>
    public float VisualKickRecoverShots => m_visualKickRecoverShots;

    /// <summary>누적 가능한 카메라 롤 상한(도)입니다.</summary>
    /// <summary>상체 레이어와 IK 리그 weight가 오르내리는 데 걸리는 시간(초)입니다.</summary>
    /// <remarks>재장전이 들고 날 때의 페이드 길이가 이 값입니다. 짧으면 툭 끊기고 길면 늘어집니다.</remarks>
    public float StanceBlendDuration => ScaleActionDuration(m_stanceBlendDuration);

    /// <summary>현재 외부 행동 속도 배율입니다.</summary>
    public float ActionSpeedMultiplier => m_actionSpeedMultiplier;

    public float VisualKickMaxRoll => m_visualKickMaxRoll;

    /// <summary>누적 가능한 FOV 펀치 상한(도)입니다.</summary>
    public float VisualKickMaxFovPunch => m_visualKickMaxFovPunch;

    /// <summary>발사 시 탄착점에 임팩트 마커를 생성할지 여부입니다.</summary>
    public bool ImpactMarkerEnabled => m_spawnImpactMarkerOnShot;

    /// <summary>임팩트 마커 유지 시간(초)입니다.</summary>
    public float ImpactMarkerLifetime => m_impactMarkerLifetime;

    /// <summary>프리팹이 없을 때 생성하는 임팩트 마커 지름입니다.</summary>
    public float ImpactMarkerSize => m_impactMarkerSize;

    /// <summary>총구 기준 히트스캔 디버그 레이 표시 여부입니다.</summary>
    public bool HitscanDebugRayEnabled => m_drawHitscanDebugRay;

    /// <summary>카메라 조준점 트레이스 디버그 선 표시 여부입니다.</summary>
    public bool DrawAimTraceLine => m_drawAimTraceLine;

    /// <summary>논리 조준과 렌더 카메라 전방 비교 레이 표시 여부입니다.</summary>
    public bool DrawCameraForwardRay => m_drawCameraForwardRay;

    /// <summary>지향점 디버그 스피어 표시 여부입니다.</summary>
    public bool DrawLookPointSphere => m_drawLookPointSphere;

    /// <summary>카메라 조준점 디버그 스피어 표시 여부입니다.</summary>
    public bool DrawAimPointSphere => m_drawAimPointSphere;

    /// <summary>총구 기준 탄착점 디버그 스피어 표시 여부입니다.</summary>
    public bool DrawImpactPointSphere => m_drawImpactPointSphere;

    /// <summary>조준 디버그 스피어 반지름입니다.</summary>
    public float DebugSphereRadius => Mathf.Max(0.0f, m_debugSphereRadius);

    /// <summary>
    /// 조준 카메라 참조를 설정합니다.
    /// </summary>
    /// <param name="value">새 조준 카메라입니다.</param>
    public void SetAimCamera(CinemachineCamera value) => m_aimCamera = value;

    /// <summary>
    /// 조준 UI 오브젝트 참조를 설정합니다.
    /// </summary>
    /// <param name="value">새 조준 UI 오브젝트입니다.</param>
    public void SetAimImage(GameObject value) => m_aimImage = value;

    /// <summary>
    /// 지향점 IK 타겟 오브젝트 참조를 설정합니다.
    /// </summary>
    /// <param name="value">새 지향점 타겟 오브젝트입니다.</param>
    public void SetLookTarget(GameObject value) => m_lookTarget = value;

    /// <summary>
    /// 지향점(LookPoint)을 둘 카메라 전방 거리를 설정합니다.
    /// </summary>
    /// <param name="value">새 지향점 거리입니다.</param>
    public void SetLookDistance(float value) => m_lookDistance = value;

    /// <summary>
    /// 조준 Raycast 대상 레이어를 설정합니다.
    /// </summary>
    /// <param name="value">새 대상 레이어 마스크입니다.</param>
    public void SetTargetLayer(LayerMask value) => m_targetLayer = value;

    /// <summary>조준/힙파이어 상태가 아니어도 조준선을 항상 표시할지 여부를 설정합니다.</summary>
    /// <param name="value">항상 표시하려면 <c>true</c>입니다.</param>
    public void SetShowAimImageAlways(bool value)
    {
        m_showAimImageAlways = value;
        if (m_aimImage != null)
        {
            m_aimImage.SetActive(m_isAds || m_showAimImageAlways);
        }
    }

    /// <summary>힙파이어 사격 후 전투 자세를 유지하는 시간을 설정합니다.</summary>
    /// <param name="value">음수는 0으로 보정됩니다.</param>
    public void SetHipfireHoldDuration(float value) => m_hipfireHoldDuration = value;

    /// <summary>ADS 카메라 기본 FOV를 설정합니다.</summary>
    /// <param name="value">1보다 작은 값은 1로 보정됩니다.</param>
    public void SetAdsFov(float value) => m_adsFov = value;

    /// <summary>힙파이어 카메라 기본 FOV를 설정합니다.</summary>
    /// <param name="value">1보다 작은 값은 1로 보정됩니다.</param>
    public void SetHipfireFov(float value) => m_hipfireFov = value;

    /// <summary>ADS와 힙파이어 FOV 전환 보간 속도를 설정합니다.</summary>
    /// <param name="value">음수는 0으로 보정됩니다.</param>
    public void SetZoomLerpSpeed(float value) => m_zoomLerpSpeed = value;

    /// <summary>
    /// 조준 중 총구 기준 히트스캔 디버그 레이 표시 여부를 설정합니다.
    /// </summary>
    /// <param name="value">표시하려면 <c>true</c>, 숨기려면 <c>false</c>입니다.</param>
    public void SetDrawHitscanDebugRay(bool value) => m_drawHitscanDebugRay = value;

    /// <summary>카메라 조준점 트레이스 디버그 선 표시 여부를 설정합니다.</summary>
    /// <param name="value">표시하려면 <c>true</c>입니다.</param>
    public void SetDrawAimTraceLine(bool value) => m_drawAimTraceLine = value;

    /// <summary>논리 조준과 렌더 카메라 전방 비교 레이 표시 여부를 설정합니다.</summary>
    /// <param name="value">표시하려면 <c>true</c>입니다.</param>
    public void SetDrawCameraForwardRay(bool value) => m_drawCameraForwardRay = value;

    /// <summary>지향점 디버그 스피어 표시 여부를 설정합니다.</summary>
    /// <param name="value">표시하려면 <c>true</c>입니다.</param>
    public void SetDrawLookPointSphere(bool value) => m_drawLookPointSphere = value;

    /// <summary>카메라 조준점 디버그 스피어 표시 여부를 설정합니다.</summary>
    /// <param name="value">표시하려면 <c>true</c>입니다.</param>
    public void SetDrawAimPointSphere(bool value) => m_drawAimPointSphere = value;

    /// <summary>총구 기준 탄착점 디버그 스피어 표시 여부를 설정합니다.</summary>
    /// <param name="value">표시하려면 <c>true</c>입니다.</param>
    public void SetDrawImpactPointSphere(bool value) => m_drawImpactPointSphere = value;

    /// <summary>조준 디버그 스피어 반지름을 설정합니다.</summary>
    /// <param name="value">음수는 0으로 보정됩니다.</param>
    public void SetDebugSphereRadius(float value) => m_debugSphereRadius = Mathf.Max(0.0f, value);

    /// <summary>
    /// 사격 사운드 클립을 설정합니다.
    /// </summary>
    /// <param name="value">새 사격 사운드 클립입니다.</param>
    public void SetShootingSound(AudioClip value) => m_shootingSound = value;

    /// <summary>
    /// 재장전 사운드 배열을 설정합니다.
    /// </summary>
    /// <param name="value">새 재장전 사운드 배열입니다.</param>
    public void SetReloadSounds(AudioClip[] value) => m_reloadSounds = value;

    /// <summary>실제 조준과 탄착에 영향을 주는 반동 적용 여부를 설정합니다.</summary>
    /// <param name="value">반동을 적용하려면 <c>true</c>입니다.</param>
    /// <summary>반동 애니메이션 세기를 설정합니다. 0이면 반동 동작이 없고 1이면 클립 그대로입니다.</summary>
    /// <param name="value">새로 적용할 세기입니다. 0~1로 잘립니다.</param>
    public void SetRecoilAnimationWeight(float value) => m_recoilAnimationWeight = Mathf.Clamp01(value);

    public void SetAimRecoilEnabled(bool value) => m_enableAimRecoil = value;

    /// <summary>카메라 롤과 FOV 펀치 시각 킥 적용 여부를 설정합니다.</summary>
    /// <param name="value">시각 킥을 적용하려면 <c>true</c>입니다.</param>
    public void SetVisualKickEnabled(bool value)
    {
        m_enableVisualKick = value;

        if (!m_enableVisualKick)
        {
            m_visualKickRoll = 0.0f;
            m_visualKickFovPunch = 0.0f;
            m_rollKickEnvelope.Clear();
            m_hipfireFovPunchEnvelope.Clear();
            m_adsFovPunchEnvelope.Clear();
        }
    }

    /// <summary>카메라 롤 킥을 곡선 엔벨로프로 처리할지 여부입니다.</summary>
    public bool UseRollKickEnvelope => m_useRollKickEnvelope;

    /// <summary>카메라 롤 킥 곡선 하나의 길이(초)입니다.</summary>
    public float RollKickEnvelopeDuration => Mathf.Max(0.0f, m_rollKickEnvelopeDuration);

    /// <summary>카메라 롤 킥 곡선입니다.</summary>
    public AnimationCurve RollKickEnvelopeCurve => m_rollKickEnvelopeCurve;

    /// <summary>FOV 펀치를 곡선 엔벨로프로 처리할지 여부입니다.</summary>
    public bool UseFovPunchEnvelope => m_useFovPunchEnvelope;

    /// <summary>힙파이어 FOV 펀치 곡선 하나의 길이(초)입니다.</summary>
    public float HipfireFovPunchEnvelopeDuration => Mathf.Max(0.0f, m_hipfireFovPunchEnvelopeDuration);

    /// <summary>힙파이어 FOV 펀치 곡선입니다.</summary>
    public AnimationCurve HipfireFovPunchEnvelopeCurve => m_hipfireFovPunchEnvelopeCurve;

    /// <summary>ADS FOV 펀치 곡선 하나의 길이(초)입니다.</summary>
    public float AdsFovPunchEnvelopeDuration => Mathf.Max(0.0f, m_adsFovPunchEnvelopeDuration);

    /// <summary>ADS FOV 펀치 곡선입니다.</summary>
    public AnimationCurve AdsFovPunchEnvelopeCurve => m_adsFovPunchEnvelopeCurve;

    /// <summary>ADS 확대·축소를 곡선으로 처리할지 여부입니다.</summary>
    public bool UseZoomEnvelope => m_useZoomEnvelope;

    /// <summary>ADS 진입(확대)에 걸리는 시간(초)입니다.</summary>
    public float ZoomInDuration => ScaleActionDuration(m_zoomInDuration);

    /// <summary>ADS 진입 곡선입니다.</summary>
    public AnimationCurve ZoomInCurve => m_zoomInCurve;

    /// <summary>ADS 해제(축소)에 걸리는 시간(초)입니다.</summary>
    public float ZoomOutDuration => ScaleActionDuration(m_zoomOutDuration);

    /// <summary>ADS 해제 곡선입니다.</summary>
    public AnimationCurve ZoomOutCurve => m_zoomOutCurve;

    /// <summary>카메라 롤 킥 엔벨로프 사용 여부를 설정합니다.</summary>
    /// <remarks>방식을 바꿀 때 진행 중이던 값이 다른 방식에 남지 않도록 함께 정리합니다.</remarks>
    public void SetUseRollKickEnvelope(bool value)
    {
        if (m_useRollKickEnvelope == value)
        {
            return;
        }

        m_useRollKickEnvelope = value;
        m_rollKickEnvelope.Clear();
        m_visualKickRoll = 0.0f;
    }

    /// <summary>카메라 롤 킥 곡선 길이를 설정합니다.</summary>
    public void SetRollKickEnvelopeDuration(float value) => m_rollKickEnvelopeDuration = Mathf.Max(0.0f, value);

    /// <summary>카메라 롤 킥 곡선을 교체합니다.</summary>
    /// <remarks>복제해서 보관합니다. 참조를 그대로 들면 밸런스 SO의 곡선과 같은 인스턴스를 공유합니다.</remarks>
    public void SetRollKickEnvelopeCurve(AnimationCurve value)
    {
        m_rollKickEnvelopeCurve = value == null ? null : new AnimationCurve(value.keys);
    }

    /// <summary>FOV 펀치 엔벨로프 사용 여부를 설정합니다.</summary>
    public void SetUseFovPunchEnvelope(bool value)
    {
        if (m_useFovPunchEnvelope == value)
        {
            return;
        }

        m_useFovPunchEnvelope = value;
        m_hipfireFovPunchEnvelope.Clear();
        m_adsFovPunchEnvelope.Clear();
        m_visualKickFovPunch = 0.0f;
    }

    /// <summary>힙파이어 FOV 펀치 곡선 길이를 설정합니다.</summary>
    public void SetHipfireFovPunchEnvelopeDuration(float value)
        => m_hipfireFovPunchEnvelopeDuration = Mathf.Max(0.0f, value);

    /// <summary>힙파이어 FOV 펀치 곡선을 교체합니다.</summary>
    public void SetHipfireFovPunchEnvelopeCurve(AnimationCurve value)
    {
        m_hipfireFovPunchEnvelopeCurve = value == null ? null : new AnimationCurve(value.keys);
    }

    /// <summary>ADS FOV 펀치 곡선 길이를 설정합니다.</summary>
    public void SetAdsFovPunchEnvelopeDuration(float value)
        => m_adsFovPunchEnvelopeDuration = Mathf.Max(0.0f, value);

    /// <summary>ADS FOV 펀치 곡선을 교체합니다.</summary>
    public void SetAdsFovPunchEnvelopeCurve(AnimationCurve value)
    {
        m_adsFovPunchEnvelopeCurve = value == null ? null : new AnimationCurve(value.keys);
    }

    /// <summary>ADS 확대·축소 곡선 사용 여부를 설정합니다.</summary>
    /// <remarks>방식을 바꾸면 진행률을 초기화해 지금 FOV에서 새 전환이 시작되게 합니다.</remarks>
    public void SetUseZoomEnvelope(bool value)
    {
        if (m_useZoomEnvelope == value)
        {
            return;
        }

        m_useZoomEnvelope = value;
        m_zoomFromFov = m_baseFov;
        m_zoomElapsed = 0.0f;
    }

    /// <summary>ADS 진입 시간을 설정합니다.</summary>
    public void SetZoomInDuration(float value) => m_zoomInDuration = Mathf.Max(0.0f, value);

    /// <summary>ADS 해제 시간을 설정합니다.</summary>
    public void SetZoomOutDuration(float value) => m_zoomOutDuration = Mathf.Max(0.0f, value);

    /// <summary>스킬 등 외부 효과가 적용하는 재장전·전투 자세 전환 속도 배율을 설정합니다.</summary>
    public void SetActionSpeedMultiplier(float value)
    {
        m_actionSpeedMultiplier = Mathf.Max(0.01f, value);

        if (m_controller != null && m_controller.IsReload)
        {
            ApplyReloadAnimationSpeed();
        }
    }

    private float ScaleActionDuration(float duration)
    {
        return Mathf.Max(0.0f, duration) / m_actionSpeedMultiplier;
    }

    /// <summary>ADS 진입 곡선을 교체합니다.</summary>
    public void SetZoomInCurve(AnimationCurve value)
    {
        m_zoomInCurve = value == null ? null : new AnimationCurve(value.keys);
    }

    /// <summary>ADS 해제 곡선을 교체합니다.</summary>
    public void SetZoomOutCurve(AnimationCurve value)
    {
        m_zoomOutCurve = value == null ? null : new AnimationCurve(value.keys);
    }

    /// <summary>시각 킥 회복 방식을 설정합니다.</summary>
    /// <param name="value">새 회복 방식입니다.</param>
    public void SetVisualKickRecoveryMode(VisualKickRecoveryMode value) => m_visualKickRecoveryMode = value;

    /// <summary>PerShotReset 회복에 사용할 발수를 설정합니다.</summary>
    /// <param name="value">0보다 작은 값은 0.01로 보정됩니다.</param>
    public void SetVisualKickRecoverShots(float value) => m_visualKickRecoverShots = value;

    /// <summary>누적 가능한 카메라 롤 상한을 설정합니다.</summary>
    /// <param name="value">음수는 0으로 보정됩니다.</param>
    /// <summary>상체 레이어·IK 리그 weight의 보간 시간을 설정합니다.</summary>
    /// <param name="value">새로 적용할 시간(초)입니다. 음수는 0으로 잘립니다.</param>
    public void SetStanceBlendDuration(float value) => m_stanceBlendDuration = Mathf.Max(0.0f, value);

    /// <summary>공중에서도 조준·사격 시 상체 조준 리그와 손 IK를 지상과 같게 올릴지 여부입니다.</summary>
    public bool EnableCombatRigInAir => m_enableCombatRigInAir;

    /// <summary>공중 전투 리그 사용 여부를 설정합니다.</summary>
    public void SetEnableCombatRigInAir(bool value) => m_enableCombatRigInAir = value;

    /// <summary>공중에서 상체(Action) 레이어로 조준 자세를 낼지 여부입니다.</summary>
    public bool UseWeaponLayerAimPoseInAir => m_useWeaponLayerAimPoseInAir;

    /// <summary>공중 상체 조준 자세 사용 여부를 설정합니다.</summary>
    public void SetUseWeaponLayerAimPoseInAir(bool value) => m_useWeaponLayerAimPoseInAir = value;

    public void SetVisualKickMaxRoll(float value) => m_visualKickMaxRoll = value;

    /// <summary>누적 가능한 FOV 펀치 상한을 설정합니다.</summary>
    /// <param name="value">음수는 0으로 보정됩니다.</param>
    public void SetVisualKickMaxFovPunch(float value) => m_visualKickMaxFovPunch = value;

    /// <summary>발사 시 탄착점 임팩트 마커 생성 여부를 설정합니다.</summary>
    /// <param name="value">생성하려면 <c>true</c>입니다.</param>
    public void SetImpactMarkerEnabled(bool value) => m_spawnImpactMarkerOnShot = value;

    /// <summary>임팩트 마커 유지 시간을 설정합니다.</summary>
    /// <param name="value">0.05초보다 작은 값은 0.05초로 보정됩니다.</param>
    public void SetImpactMarkerLifetime(float value) => m_impactMarkerLifetime = Mathf.Max(0.05f, value);

    /// <summary>프리팹이 없을 때 생성하는 임팩트 마커 지름을 설정합니다.</summary>
    /// <param name="value">0.01보다 작은 값은 0.01로 보정됩니다.</param>
    public void SetImpactMarkerSize(float value) => m_impactMarkerSize = Mathf.Max(0.01f, value);

    /// <summary>
    /// Unity 생명주기 초기화 함수입니다.
    /// 필수 참조를 캐싱하고 누락 여부를 검증합니다.
    /// </summary>
    /// <summary>
    /// 지정된 SO가 있을 때 공용 BindManager로 같은 이름의 필드 값을 적용합니다.
    /// </summary>
    /// <returns>이번 바인딩의 집계 결과입니다. SO가 없으면 기본값입니다.</returns>
    /// <remarks>
    /// 같은 SO를 이 오브젝트의 다른 컴포넌트도 각자 바인드합니다. 대상이 요구한 필드만 가져가므로
    /// 서로 간섭하지 않고, 컴포넌트 간 Awake 실행 순서에도 의존하지 않습니다.
    /// </remarks>
    private BalanceBindResult BindConfiguredBalance()
    {
        return BindFrom(m_balanceSO);
    }

    /// <summary>개별 밸런스 SO를 직접 물고 있는지 여부입니다.</summary>
    /// <remarks><c>true</c>면 <see cref="SOBinder"/>가 통합 SO 주입을 건너뜁니다.</remarks>
    public bool HasOwnBalance => m_balanceSO != null;

    /// <summary>엔티티 통합 밸런스 SO의 값을 적용합니다.</summary>
    /// <param name="balance">통합 밸런스 SO입니다.</param>
    /// <returns>이번 바인딩의 집계 결과입니다.</returns>
    /// <remarks>개별 SO 슬롯은 비운 채로 둡니다. 비어 있다는 것 자체가 "개별 지정 없음"을 뜻합니다.</remarks>
    public BalanceBindResult BindSharedBalance(ScriptableObject balance)
    {
        return BindFrom(balance);
    }

    /// <summary>주어진 원본 SO에서 밸런스 값을 대입합니다.</summary>
    /// <param name="balance">값을 읽어올 밸런스 SO입니다. 개별 SO일 수도, 엔티티 통합 SO일 수도 있습니다.</param>
    /// <returns>이번 바인딩의 집계 결과입니다. 원본이 없으면 기본값입니다.</returns>
    private BalanceBindResult BindFrom(ScriptableObject balance)
    {
        if (balance == null)
        {
            return default;
        }

        return BindManager.Instance.Bind(balance, this, this);
    }

    private void Awake()
    {
        CacheRequiredReferences();

        if (!ValidateRequiredReferences())
        {
            enabled = false;
            return;
        }

        CacheOptionalCrosshairController();
        m_hasRequiredReferences = true;
        BindConfiguredBalance();
        ApplyCombatStanceState(false, false, 0.0f);
        SnapStanceWeights();

        if (m_weaponController != null)
        {
            m_weaponController.OnHitFeedback += OnWeaponHitFeedback;
            m_weaponController.OnReloadCompleted += OnWeaponReloadCompleted;
        }
    }

    /// <summary>
    /// Unity 생명주기 종료 함수입니다. 구독한 무기 피드백 이벤트를 해제합니다.
    /// </summary>
    private void OnDestroy()
    {
        if (m_weaponController != null)
        {
            m_weaponController.OnHitFeedback -= OnWeaponHitFeedback;
            m_weaponController.OnReloadCompleted -= OnWeaponReloadCompleted;
        }

        if (m_hitConfirmInstance.isValid())
        {
            m_hitConfirmInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            m_hitConfirmInstance.release();
            m_hitConfirmInstance.clearHandle();
        }
    }

    /// <summary>
    /// 무기 쪽 재장전 타이머가 끝났을 때 재장전 비주얼 상태를 대신 정리합니다.
    /// </summary>
    /// <remarks>
    /// 무기 타이머가 끝나는 순간을 <see cref="ReconcileReloadState"/>에 알리는 세 번째 진입점입니다.
    /// 매 프레임 도는 두 경로(조작 멤버의 <c>Update</c>, AI의 <see cref="ApplyAiCombatStance"/>)가 어느
    /// 쪽도 돌지 않는 상태 - 조작 멤버도 AI도 아닌 대원 - 를 메웁니다. C# 이벤트는 컴포넌트를 꺼도
    /// 끊기지 않으므로 이 경로만은 어느 경우에나 살아 있습니다.
    ///
    /// 실제 판단과 정리는 전부 <see cref="ReconcileReloadState"/>가 합니다. 여기서 따로 처리하면
    /// 같은 규칙이 두 벌이 되고, 그렇게 갈라진 재장전 처리가 이번 버그들의 원인이었습니다.
    /// </remarks>
    private void OnWeaponReloadCompleted()
    {
        if (!m_hasRequiredReferences)
        {
            return;
        }

        ReconcileReloadState();
    }

    /// <summary>
    /// 히트스캔 피격 피드백을 조준선 UI로 전달합니다(히트마커 색상 구분 + 킬 시 해골 표시).
    /// </summary>
    /// <param name="feedback">헤드샷·킬 여부와 최종 피해량을 담은 피격 피드백입니다.</param>
    /// <remarks>
    /// 직접 조작 중인 대원의 사격만 조준선에 반영합니다. 조준선은 스쿼드 전체가 <b>한 개를 공유</b>하므로,
    /// 막지 않으면 AI가 모는 팀원이 적을 맞힐 때마다 플레이어 화면에 히트마커와 처치 표시가 떠서,
    /// 내가 맞힌 것처럼 보입니다.
    /// </remarks>
    private void OnWeaponHitFeedback(CombatDamage.HitFeedback feedback)
    {
        if (!m_isPlayerControlled)
        {
            return;
        }

        PlayHitConfirmSound(feedback.Headshot);
        LogHitMarkerFeedback();

        if (m_crosshairController == null)
        {
            return;
        }

        // 피해량을 함께 넘겨 히트마커 길이가 타격 크기를 반영하게 합니다.
        m_crosshairController.ShowHitMarker(feedback.Headshot, feedback.Damage);

        if (feedback.Killed)
        {
            m_crosshairController.ShowKill();
        }
    }

    /// <summary>
    /// 플레이어용 히트 확인음을 재생합니다. 같은 인스턴스를 다시 시작해 연사 중에도 소리가 겹쳐 커지지 않게 합니다.
    /// </summary>
    private void PlayHitConfirmSound(bool headshot)
    {
        if (m_hitConfirmEvent.IsNull)
        {
            return;
        }

        try
        {
            if (!m_hitConfirmInstance.isValid())
            {
                m_hitConfirmInstance = FMODUnity.RuntimeManager.CreateInstance(m_hitConfirmEvent);
            }

            m_hitConfirmInstance.setParameterByName(
                HitConfirmHeadshotParameter,
                headshot ? 1.0f : 0.0f);
            m_hitConfirmInstance.start();
        }
        catch (FMODUnity.EventNotFoundException)
        {
            if (m_loggedMissingHitConfirmEvent)
            {
                return;
            }

            m_loggedMissingHitConfirmEvent = true;
            Debug.LogWarning(
                $"[AimController] FMOD 히트 확인음 이벤트를 찾지 못했습니다: {m_hitConfirmEvent.Path}",
                this);
        }
    }

    /// <summary>피해 이벤트가 플레이어 조준선까지 전달되는지 확인하는 임시 에디터 로그입니다.</summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void LogHitMarkerFeedback()
    {
        Debug.Log(
            $"[DEBUG-HITMARKER] receiver={name} " +
            $"crosshair={(m_crosshairController != null ? m_crosshairController.name : "none")} " +
            $"enabled={(m_crosshairController != null && m_crosshairController.HitMarkerEnabled)}",
            this);
    }

    /// <summary>
    /// 매 프레임 조준, 사격, 재장전 입력을 처리합니다.
    /// </summary>
    private void Update()
    {
        RestoreMuzzleAlignment();
        m_combatShotPending = false;
        m_fireRequested = false;

        if (!m_hasRequiredReferences)
        {
            return;
        }

        // 사격 차단을 푸는 것이 이 프레임의 조준·사격 처리보다 먼저입니다.
        ReconcileReloadState();

        // 무기는 이동 상태의 소유자가 아니라 접지 여부를 스스로 알 수 없습니다. 공중 추가 탄퍼짐이
        // 이 값을 보고 걸리므로, 조작 여부와 무관하게 매 프레임 넘깁니다.
        if (m_weaponController != null)
        {
            m_weaponController.SetAirborne(IsAirborne);

            // 앉기도 같은 이유로 무기가 스스로 알 수 없습니다. 자세가 바뀌는 동안 탄퍼짐이 같은 곡선을
            // 타도록 켜짐/꺼짐이 아니라 보간값을 그대로 넘깁니다.
            m_weaponController.SetCrouchBlend(m_controller != null ? m_controller.CrouchBlend : 0.0f);
        }

        if (m_isPlayerControlled)
        {
            UpdatePlayerControlledFrame();
        }

        // 리그·레이어 보간은 조작 여부와 무관하게 돌아야 합니다. AI가 모는 대원도 자세가 바뀌고,
        // 여기서 멈추면 그 대원의 가중치가 중간값에 얼어붙습니다.
        UpdateStanceWeights();
    }

    /// <summary>애니메이터와 상체 IK가 끝난 총구를 보정한 뒤, 그 위치에서 표시와 사격을 함께 계산합니다.</summary>
    private void LateUpdate()
    {
        if (!m_inCombatStance || IsReloadInProgress)
        {
            m_muzzleAlignmentOffset = Quaternion.identity;
            return;
        }

        // AI has an individual LookTarget supplied by SquadAIController. Apply the same
        // post-rig upper-body/muzzle correction, while leaving its firing to SquadAIController.
        if (!m_isPlayerControlled)
        {
            AlignMuzzleToAimPoint();
            return;
        }

        if (!m_combatShotPending)
        {
            m_muzzleAlignmentOffset = Quaternion.identity;
            return;
        }

        AlignMuzzleToAimPoint();
        Gun.HitscanShotInfo shotInfo = EvaluateHitscanShot(m_currentAimPoint, m_currentAimTargetHealth);
        UpdateCurrentAimEnemy(shotInfo);
        DrawHitscanDebugRay(shotInfo);
        DrawAimTraceDebugLine(shotInfo);
        DrawCameraForwardDebugRay(m_lookTarget != null ? m_lookTarget.transform.position : m_currentAimPoint);
        DrawAimDebugSpheres(m_lookTarget != null ? m_lookTarget.transform.position : m_currentAimPoint, shotInfo);
        UpdateHitscanBlockMarker(shotInfo);

        if (m_fireRequested && m_weaponController != null
            && m_weaponController.TryLayShoot(shotInfo, m_isAds, out _, m_firedPelletBuffer))
        {
            for (int i = 0; i < m_firedPelletBuffer.Count; i++)
            {
                SpawnImpactMarker(m_firedPelletBuffer[i]);
            }

            ApplyRecoilAndVisualKick();
        }
    }

    /// <summary>양팔과 총을 함께 가진 상체 뼈를 제한적으로 회전시켜 총구를 실제 조준 방향에 맞춥니다.</summary>
    private void AlignMuzzleToAimPoint()
    {
        if (m_muzzleAlignmentMaxAngle <= 0.0f || m_aimRig == null || m_handRig == null
            || m_weaponController == null || m_weaponController.FirePos == null
            || (m_input != null && m_input.ThrowMode) || m_animator == null || !m_animator.isHuman)
        {
            m_muzzleAlignmentOffset = Quaternion.identity;
            return;
        }

        if (m_muzzleAlignmentBone == null)
        {
            m_muzzleAlignmentBone = m_animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (m_muzzleAlignmentBone == null)
            {
                m_muzzleAlignmentBone = m_animator.GetBoneTransform(HumanBodyBones.Chest);
            }
        }

        Transform muzzle = m_weaponController.FirePos;
        if (m_muzzleAlignmentBone == null || !muzzle.IsChildOf(m_muzzleAlignmentBone))
        {
            return;
        }

        float weight = Mathf.Min(m_aimRig.weight, m_handRig.weight);
        m_preAlignmentLocalRotation = m_muzzleAlignmentBone.localRotation;
        Quaternion originalRotation = m_muzzleAlignmentBone.rotation;

        // 카메라와 총구 사이의 시차 때문에 근접 조준점에서는 그 점에 수렴하고,
        // 먼 조준점에서는 카메라 조준축과 평행에 가깝게 유지합니다.
        Vector3 forward = GetAimForward();
        bool converge = Vector3.Dot(m_currentAimPoint - m_muzzleAlignmentBone.position, forward) > 1.0f;
        for (int i = 0; i < 3; i++)
        {
            Vector3 direction = converge ? m_currentAimPoint - muzzle.position : forward;
            if (direction.sqrMagnitude < 0.0001f)
            {
                break;
            }

            Quaternion desired = Quaternion.FromToRotation(muzzle.forward, direction)
                * m_muzzleAlignmentBone.rotation;
            m_muzzleAlignmentBone.rotation = Quaternion.RotateTowards(
                originalRotation,
                desired,
                m_muzzleAlignmentMaxAngle);
        }

        Quaternion offset = Quaternion.Inverse(m_preAlignmentLocalRotation)
            * m_muzzleAlignmentBone.localRotation;
        // 반동·앉기 전환은 상체의 기준 자세를 한 프레임 안에서도 크게 바꿉니다. 사격 중에 이전
        // 자세에서 계산한 로컬 오프셋을 계속 보간하면 총구가 현재 탄착 방향을 한두 프레임 늦게
        // 따라가므로, 실제 발사 프레임은 방금 계산한 보정을 즉시 사용합니다. 조준만 하는 동안에는
        // 기존 보간을 유지해 총을 들 때 상체가 갑자기 꺾이지 않게 합니다.
        if (m_isPlayerControlled && m_fireRequested)
        {
            m_muzzleAlignmentOffset = offset;
        }
        else
        {
            m_muzzleAlignmentOffset = Quaternion.Slerp(
                m_muzzleAlignmentOffset,
                offset,
                1.0f - Mathf.Exp(-18.0f * Time.deltaTime));
        }
        m_muzzleAlignmentBone.localRotation = m_preAlignmentLocalRotation
            * Quaternion.Slerp(Quaternion.identity, m_muzzleAlignmentOffset, weight);
        m_hasMuzzleAlignment = true;
    }

    /// <summary>이 프레임의 후처리가 다음 애니메이션 리그 입력에 누적되지 않도록 원래 자세를 복원합니다.</summary>
    private void RestoreMuzzleAlignment()
    {
        if (m_hasMuzzleAlignment && m_muzzleAlignmentBone != null)
        {
            m_muzzleAlignmentBone.localRotation = m_preAlignmentLocalRotation;
        }

        m_hasMuzzleAlignment = false;
    }

    private void OnDisable()
    {
        RestoreMuzzleAlignment();
        m_muzzleAlignmentOffset = Quaternion.identity;
        m_combatShotPending = false;
        m_fireRequested = false;
        m_wasShootPressed = false;
    }

    /// <summary>
    /// 직접 조작 중인 대원에서만 도는 입력·조준·조준선 처리입니다.
    /// </summary>
    /// <remarks>
    /// 여기 있는 것들은 전부 플레이어가 조작할 때만 의미가 있습니다. 입력을 읽거나, 스쿼드가 하나만
    /// 공유하는 조준 카메라·조준선을 건드리는 것들입니다. AI가 모는 대원에서 돌면 세 대원이 같은
    /// 카메라와 조준선을 두고 다툽니다.
    ///
    /// 반대로 리그 weight와 애니메이터 레이어는 누가 몰든 그 대원에게 계속 필요하므로 <see cref="Update"/>
    /// 쪽에 둡니다. 이 컴포넌트가 두 종류를 함께 들고 있어서, 예전에는 조작 전용을 끄려고 컴포넌트를
    /// 통째로 꺼야 했고 그때마다 캐릭터 쪽 몫이 같이 죽었습니다.
    /// </remarks>
    private void UpdatePlayerControlledFrame()
    {
        // 반자동은 눌림 에지를 한 번만 소비합니다. 전투 자세 밖에서 버튼을 놓은 경우에도 다음 클릭을
        // 새 입력으로 받을 수 있도록 릴리스 상태는 플레이어 입력 프레임 전체에서 추적합니다.
        if (m_input == null || !m_input.Shoot)
        {
            m_wasShootPressed = false;
        }

        // Gun은 입력 소유자가 아니므로, 조준 컨트롤러가 홀드 여부를 전달해 실제 탄퍼짐/크로스헤어 회복도
        // 논리 반동과 같은 입력 기준으로 멈춥니다.
        if (m_weaponController != null)
        {
            m_weaponController.SetSpreadRecoveryBlockedByHeldFireInput(IsContinuousFireHeld);
        }

        if (m_crosshairController != null && !UsesWeaponMaxSpreadCrosshair)
        {
            m_crosshairController.SetShotRecoilPulseHoldByFireInput(IsContinuousFireHeld);
        }

        UpdateStanceArbitration();
        UpdateAimAndWeapon();

        // 전투 자세를 나가면 UpdateCombat이 돌지 않아 조준선 갱신이 멈춥니다. 최소 방사각은 자유 시점에도
        // 유효하므로, 그 상태의 기준 벌어짐은 여기서 따로 유지합니다.
        UpdateRestingCrosshair();

        UpdateCrosshairDebugOnStanceChange();
        UpdateReloadCrosshair();
    }

    /// <summary>
    /// 무기 재장전 상태와 탄약 게이지 채움 비율을 조준선 UI에 전달합니다(재장전 중 크로스헤어↔탄약 아이콘 스왑 + 아크 게이지).
    /// </summary>
    private void UpdateReloadCrosshair()
    {
        if (m_crosshairController == null)
        {
            return;
        }

        bool reloading = m_weaponController != null && m_weaponController.IsReloading;
        m_crosshairController.SetReloading(reloading);

        if (m_weaponController != null)
        {
            bool segmentedByRound = m_weaponController.SegmentAmmoGaugeByRound;
            m_crosshairController.SetAmmoGaugeSegmentation(
                segmentedByRound,
                m_weaponController.MaxBullet);

            int specialRounds = m_dragonBreathSkill != null
                ? Mathf.Min(m_dragonBreathSkill.SpecialRoundsRemaining, m_weaponController.CurrentBullet)
                : 0;
            m_crosshairController.SetSpecialAmmoGaugeRounds(specialRounds);

            float magazineFill = m_weaponController.MaxBullet > 0
                ? (float)m_weaponController.CurrentBullet / m_weaponController.MaxBullet
                : 0.0f;

            // 발 단위 게이지는 재장전 중에도 실제 장탄수를 표시합니다. 탄이 삽입될 때만 한 칸씩 증가합니다.
            // 연속 게이지를 사용하는 무기는 기존처럼 재장전 진행도를 부드럽게 표시합니다.
            float fill = segmentedByRound
                ? magazineFill
                : reloading
                    ? m_weaponController.ReloadProgress
                    : magazineFill;
            m_crosshairController.SetAmmoGaugeFill(fill);
        }
    }

    /// <summary>
    /// 같은 GameObject 또는 자식 오브젝트에서 필요한 참조를 캐싱합니다.
    /// </summary>
    private void CacheRequiredReferences()
    {
        m_input = GetComponent<PlayerInputController>();
        m_controller = GetComponent<ThirdPersonController>();
        m_animator = GetComponent<Animator>();
        m_weaponAudioSource = GetComponent<AudioSource>();
        m_weaponController = GetComponentInChildren<Gun>();
        m_dragonBreathSkill = GetComponent<ChungSolDragonBreathSkill>();
        m_mainCamera = Camera.main;
    }

    /// <summary>
    /// Aim Image 또는 플레이어 하위에 배치된 선택형 조준선 컨트롤러를 캐싱합니다.
    /// </summary>
    private void CacheOptionalCrosshairController()
    {
        if (m_crosshairController != null)
        {
            return;
        }

        if (m_aimImage != null)
        {
            m_crosshairController = m_aimImage.GetComponentInChildren<CrosshairController>(true);
        }

        if (m_crosshairController == null)
        {
            m_crosshairController = GetComponentInChildren<CrosshairController>(true);
        }
    }

    /// <summary>
    /// 필수 참조가 정상적으로 준비되었는지 검증합니다.
    /// </summary>
    /// <returns>필수 참조가 모두 유효하면 true입니다.</returns>
    private bool ValidateRequiredReferences()
    {
        bool isValid = true;

        if (m_input == null)
        {
            Debug.LogError("[AimController] PlayerInputController 컴포넌트가 없습니다. 같은 GameObject에 추가하세요.", this);
            isValid = false;
        }

        if (m_controller == null)
        {
            Debug.LogError("[AimController] ThirdPersonController 컴포넌트가 없습니다. 같은 GameObject에 추가하세요.", this);
            isValid = false;
        }

        if (m_animator == null)
        {
            Debug.LogError("[AimController] Animator 컴포넌트가 없습니다. 같은 GameObject에 추가하세요.", this);
            isValid = false;
        }

        if (m_weaponAudioSource == null)
        {
            Debug.LogError("[AimController] AudioSource 컴포넌트가 없습니다. 같은 GameObject에 추가하세요.", this);
            isValid = false;
        }

        if (m_mainCamera == null)
        {
            Debug.LogError("[AimController] MainCamera 태그를 가진 카메라를 찾지 못했습니다.", this);
            isValid = false;
        }

        if (m_aimCamera == null)
        {
            Debug.LogError("[AimController] Aim Camera가 Inspector에 할당되지 않았습니다.", this);
            isValid = false;
        }

        if (m_aimImage == null)
        {
            Debug.LogError("[AimController] Aim Image가 Inspector에 할당되지 않았습니다.", this);
            isValid = false;
        }

        if (m_lookTarget == null)
        {
            Debug.LogError("[AimController] Look Target이 Inspector에 할당되지 않았습니다.", this);
            isValid = false;
        }

        if (m_handRig == null)
        {
            Debug.LogError("[AimController] Hand Rig가 Inspector에 할당되지 않았습니다.", this);
            isValid = false;
        }

        if (m_aimRig == null)
        {
            Debug.LogError("[AimController] Aim Rig가 Inspector에 할당되지 않았습니다.", this);
            isValid = false;
        }

        if (m_weaponController == null)
        {
            Debug.LogWarning("[AimController] Gun를 자식 오브젝트에서 찾지 못했습니다. 사격과 재장전 무기 처리는 생략됩니다.", this);
        }

        return isValid;
    }

    /// <summary>
    /// 달리기와 조준·사격 중 어느 쪽이 더 최근 요청인지 판정합니다.
    /// </summary>
    /// <remarks>
    /// 설계 근거: 캐릭터 행동 시스템 §2 「같은 상태 축의 행동이 충돌하면 별도 예외가 없는 한 가장 최근의
    /// 유효한 행동 요청을 우선한다」, §9 「달리기는 조준 또는 사격과 동시에 유지할 수 없다」.
    ///
    /// 눌린 순간을 기준으로 삼습니다. 누르고 있는 상태만 보면 둘 다 유지 중일 때 어느 쪽이 나중에
    /// 들어왔는지 알 수 없어, 고정 우선순위로 돌아가 버립니다. 예전 코드가 조준을 늘 앞세워
    /// 달리다 조준하면 조준이 되지만 조준한 채 달리기를 눌러도 아무 일이 없었습니다.
    ///
    /// 달리기를 놓으면 유지 중인 조준·사격이 곧바로 이어받습니다. 그러려면 놓는 순간 우선권을
    /// 넘겨야 하고, 그래서 새 입력을 다시 요구하지 않습니다(§6).
    /// </remarks>
    private void UpdateStanceArbitration()
    {
        bool sprint = m_input.Sprint;
        bool combat = m_input.Aim || m_input.Shoot || m_input.ThrowMode;

        if (sprint && !m_sprintWasHeld)
        {
            m_sprintOverridesCombat = true;
        }

        if (combat && !m_combatWasHeld)
        {
            m_sprintOverridesCombat = false;
        }

        if (!sprint)
        {
            m_sprintOverridesCombat = false;
        }

        m_sprintWasHeld = sprint;
        m_combatWasHeld = combat;
    }

    /// <summary>
    /// 재장전 입력과 조준 입력을 순서대로 처리합니다.
    /// </summary>
    private void UpdateAimAndWeapon()
    {
        // 상체를 따로 쓰는 행동(투척 등)이 들어오면 재장전을 접고 그쪽을 먼저 보냅니다.
        CancelReloadForUpperBodyAction();

        if (HandleReloadInput())
        {
            return;
        }

        // 탄창이 비면 입력 없이 바로 재장전합니다. 위에서 접힌 경우에도, 그 행동이 끝나 조건이 풀리는
        // 프레임에 여기서 다시 걸립니다. 별도의 "복귀" 상태를 두지 않는 이유입니다.
        TryAutoReloadWhenEmpty();

        if (m_controller.IsReload)
        {
            ExitCombatStance();

            // 전투 자세를 나가면 UpdateCombat이 돌지 않아 조준점 갱신이 멈춥니다. 그 상태로 허리 리그만
            // 켜두면 재장전 시작 순간의 지점을 계속 바라보며 굳습니다. 그래서 여기서 조준점만 따로 갱신합니다.
            KeepAimingWhileReloading();
            return;
        }

        // 달리기와 조준·사격은 함께 유지할 수 없고(캐릭터 행동 시스템 §9), 충돌은 가장 최근의 유효한
        // 요청이 이깁니다(§2). 어느 쪽이 최근인지는 UpdateStanceArbitration이 판정합니다.
        if (m_input.Sprint && m_sprintOverridesCombat)
        {
            ExitCombatStance();
            return;
        }

        // 투척 대기: 힙파이어 백뷰와 조준점 계산은 유지하지만 총기 발사는 입력 계층에서 차단합니다.
        if (m_input.ThrowMode)
        {
            EnterCombatStance(false);
            UpdateCombat();
            return;
        }

        // 조준(ADS): 백뷰 + 줌.
        if (m_input.Aim)
        {
            EnterCombatStance(true);
            UpdateCombat();
            return;
        }

        // 비조준 사격(힙파이어): Shoot 입력 시 백뷰 진입/유지하고 복귀 타이머를 리셋합니다.
        if (m_input.Shoot)
        {
            EnterCombatStance(false);
            m_hipfireTimer = m_hipfireHoldDuration;
            UpdateCombat();
            return;
        }

        // 힙파이어 잔류: 마지막 사격 후 유지 시간 동안 백뷰를 유지하고, 끝나면 자유 시점으로 복귀합니다.
        // 잔류는 타이머 기준이라, 우클릭(ADS)에 잠깐 다녀와도 잔류 시간이 남아 있으면 힙파이어로 복귀해 취소되지 않습니다.
        // (순수 ADS 후 해제는 타이머가 0이라 이 분기를 건너뛰고 즉시 복귀합니다.)
        if (m_inCombatStance && m_hipfireTimer > 0.0f)
        {
            m_isAds = false;
            m_hipfireTimer -= Time.deltaTime;

            if (m_hipfireTimer > 0.0f)
            {
                UpdateCombat();
                return;
            }
        }

        ExitCombatStance();
    }

    /// <summary>
    /// 재장전 중에도 허리가 조준 방향을 계속 따라가게 합니다.
    /// </summary>
    /// <remarks>
    /// <see cref="UpdateCombat"/>를 통째로 부르지 않는 이유는 그 안에 히트스캔 평가, 사격 처리, 크로스헤어,
    /// 줌 갱신이 함께 들어 있기 때문입니다. 재장전 중에는 겨냥 방향만 있으면 되므로 지향점만 갱신합니다.
    ///
    /// <see cref="ExitCombatStance"/>가 리그 weight를 함께 0으로 내리므로, 그 뒤에 허리만 다시 올립니다.
    /// 손은 0으로 남겨 탄창을 다루는 동작이 IK에 끌려가지 않게 합니다.
    /// </remarks>
    private void KeepAimingWhileReloading()
    {
        ApplyLookTarget(ResolveReloadLookPoint());
        SetRigWeights(1.0f, 0.0f);
    }

    /// <summary>
    /// 재장전 중 허리가 바라볼 지점입니다. 상하 각도만 카메라를 따르고 좌우는 몸 기준으로 고정합니다.
    /// </summary>
    /// <remarks>
    /// 재장전은 <see cref="ExitCombatStance"/>를 거치므로 몸이 더 이상 카메라 좌우를 따라 돌지 않습니다.
    /// 그 상태에서 <see cref="ResolveLookPoint"/>(카메라 전방)를 그대로 쓰면 허리가 그 좌우 차이를 혼자
    /// 비틀어 메꿔, 걸을 때 상체가 흔들려 보였습니다.
    ///
    /// 백뷰라 좌우 조준은 몸 회전이 이미 담당합니다. 여기서 필요한 것은 위아래로 꺾이는 것뿐이라
    /// 카메라 방향에서 상하 성분만 가져오고 좌우는 몸 정면으로 대체합니다.
    /// </remarks>
    private Vector3 ResolveReloadLookPoint()
    {
        float lookDistance = m_lookDistance;
        if (m_weaponController != null)
        {
            lookDistance = Mathf.Max(lookDistance, m_weaponController.HitscanRange);
        }

        Vector3 aimForward = GetAimForward();

        Vector3 bodyForward = transform.forward;
        bodyForward.y = 0.0f;

        if (bodyForward.sqrMagnitude < 0.0001f)
        {
            return m_mainCamera.transform.position + aimForward * lookDistance;
        }

        bodyForward.Normalize();

        // 카메라 방향의 상하 성분(y)만 남기고, 수평 성분의 크기는 그대로 둔 채 방향만 몸 정면으로 바꿉니다.
        float vertical = Mathf.Clamp(aimForward.y, -1.0f, 1.0f);
        float horizontal = Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - vertical * vertical));

        Vector3 direction = bodyForward * horizontal + Vector3.up * vertical;

        return m_mainCamera.transform.position + direction * lookDistance;
    }

    /// <summary>
    /// 재장전 입력이 들어온 경우 재장전 상태와 애니메이션을 시작합니다.
    /// </summary>
    /// <returns>재장전 입력을 처리했으면 true입니다.</returns>
    private bool HandleReloadInput()
    {
        if (!m_input.Reload)
        {
            return false;
        }

        m_input.ReloadInput(false);

        if (m_controller.IsReload)
        {
            return true;
        }

        // 풀 탄창(또는 이미 재장전 중)이면 재장전 상태(IsReload)와 애니메이션을 아예 세우지 않습니다.
        // 무기측 StartReload는 풀 탄창을 무시하므로, 여기서 막지 않으면 조작 잠금만 걸려 헛장전/데드락이 됩니다.
        if (m_weaponController != null && !m_weaponController.CanReload)
        {
            // 풀 탄창 등으로 장전이 막힌 경우 빈 장전(드라이) 피드백만 재생합니다(클립이 없으면 무음).
            m_weaponController.PlayEmptyReloadSound();
            return true;
        }

        BeginReload();

        return true;
    }

    /// <summary>
    /// 재장전 시작 시 한 번만 필요한 조준 해제, 애니메이션, 무기 상태를 적용합니다.
    /// </summary>
    private void BeginReload()
    {
        ApplyReloadPresentation();

        if (m_weaponController != null)
        {
            m_weaponController.StartReload();
        }
    }

    /// <summary>스킬이 요청한 탄창 전체 장전을 전용 속도 배율과 함께 시작합니다.</summary>
    /// <param name="reloadSpeedMultiplier">일반 재장전 대비 스킬 재장전 속도 배율입니다.</param>
    public bool BeginSkillReload(float reloadSpeedMultiplier)
    {
        if (!m_hasRequiredReferences
            || m_controller == null
            || m_weaponController == null
            || m_controller.IsReload
            || m_weaponController.IsReloading)
        {
            return false;
        }

        if (!m_weaponController.StartSkillReload(reloadSpeedMultiplier))
        {
            return false;
        }

        // 0초 장전은 무기에서 즉시 완료되므로 불필요한 재장전 비주얼을 걸지 않습니다.
        if (m_weaponController.IsReloading)
        {
            ApplyReloadPresentation();
        }

        return true;
    }

    /// <summary>플레이어·AI·스킬 재장전이 공유하는 조준 해제 및 애니메이터 상태를 적용합니다.</summary>
    private void ApplyReloadPresentation()
    {
        m_inCombatStance = false;
        m_isAds = false;
        m_hipfireTimer = 0.0f;
        SetAimState(false);
        HideHitscanBlockMarker();

        // 허리는 계속 조준 방향을 바라보고, 손만 풀어 탄창을 다루게 합니다.
        // 둘을 함께 0으로 내리면 재장전 내내 상체가 정면으로 굳어 조준하던 방향을 잃습니다.
        SetRigWeights(1.0f, 0.0f);

        m_animator.SetBool(AnimIDShoot, false);
        SetWeaponLayerWeight(1.0f);

        // 애니메이션 배속은 재장전 시간에서 역산합니다. 스테이트에 들어가기 전에 세워야 첫 프레임부터 적용됩니다.
        ApplyReloadAnimationSpeed();

        // 트리거와 bool을 함께 세웁니다. 애니메이터 진입 조건이 둘의 AND입니다.
        m_animator.SetBool(AnimIDIsReload, true);
        m_animator.SetTrigger(AnimIDReload);
        m_controller.SetReload(true);
    }

    /// <summary>
    /// AI 대원이 플레이어와 같은 재장전 비주얼과 무기 타이머를 함께 시작합니다.
    /// </summary>
    /// <returns>재장전을 새로 시작했으면 <c>true</c>입니다.</returns>
    /// <remarks>
    /// 스쿼드 AI는 입력을 거치지 않으므로 <see cref="HandleReloadInput"/>를 호출할 수 없습니다.
    /// 그렇다고 <see cref="Gun.StartReload"/>만 직접 부르면 탄약 타이머만 돌고 애니메이터의
    /// <c>IsReload</c>/<c>DoReload</c> 상태가 빠집니다. 플레이어와 같은 <see cref="BeginReload"/>
    /// 경로로 묶어, 애니메이션 완료 시점과 무기 타이머 완료 시점을 하나의 재장전으로 관리합니다.
    /// </remarks>
    public bool BeginAiReload()
    {
        if (!m_hasRequiredReferences
            || m_isPlayerControlled
            || m_controller == null
            || m_weaponController == null
            || m_controller.IsReload
            || !m_weaponController.CanReload)
        {
            return false;
        }

        BeginReload();
        return true;
    }

    /// <summary>
    /// 총을 드는 동안 사격을 막는 구간을 시작합니다.
    /// </summary>
    /// <remarks>
    /// 길이는 ADS 줌인이 완전히 끝나는 시간과 같게 잡습니다. 총을 드는 연출과 조준 확대가 같은 타이밍에
    /// 마무리되어야 "다 들고 나서 쏜다"가 화면과 일치하기 때문입니다.
    /// <para>
    /// 다만 남은 거상량에 비례해 줄입니다. <see cref="m_hipfireHoldDuration"/>이 0이면 사격을 뗄 때마다 전투
    /// 자세를 나가므로, 고정 길이로 걸면 탭 사격의 매 발이 지연됩니다. 총이 아직 내려가지 않았다면
    /// (<see cref="m_rigWeight"/>가 1에 가까움) 남은 시간이 0에 수렴해 연속 탭이 그대로 유지됩니다.
    /// </para>
    /// </remarks>
    private void BeginWeaponRaiseGate()
    {
        float remaining = ResolveWeaponRaiseDuration() * Mathf.Clamp01(1.0f - m_rigWeight);
        m_weaponRaiseReadyTime = Time.time + remaining;
    }

    /// <summary>
    /// 완전히 내려간 상태에서 총을 다 들 때까지 걸리는 시간(초)입니다.
    /// </summary>
    /// <returns>ADS 줌인 시간입니다. 줌 엔벨로프를 쓰지 않으면 지수 보간이 약 95%에 도달하는 시간으로 환산합니다.</returns>
    private float ResolveWeaponRaiseDuration()
    {
        if (m_useZoomEnvelope)
        {
            return ZoomInDuration;
        }

        // 지수 보간(Lerp with dt·speed)에는 고정 길이가 없어, e^(-speed·T)=0.05가 되는 T=3/speed로 환산합니다.
        return m_zoomLerpSpeed > 0.0f ? 3.0f / m_zoomLerpSpeed : 0.0f;
    }

    /// <summary>
    /// 재장전 애니메이션 배속을 무기의 재장전 시간에 맞춰 계산해 애니메이터에 전달합니다.
    /// </summary>
    /// <remarks>
    /// 정본은 <see cref="Gun.ReloadTime"/>입니다. 게이지(<see cref="Gun.ReloadProgress"/>)와 탄약 충전 예약이
    /// 이미 그 값을 쓰므로, 애니메이션도 같은 값에 맞추면 세 가지가 한 숫자로 묶입니다. 예전에는 애니메이션이
    /// 항상 1배속이고 재장전 시간만 따로 적혀 있어, 게이지가 꽉 찬 뒤에도 조작이 잠긴 구간이 생겼습니다.
    /// <para>
    /// 기준 시점은 클립에 하드코딩된 숫자가 아니라 <see cref="Reload"/> 이벤트의 실제 시각에서 읽습니다.
    /// 애니메이션이 교체되거나 이벤트가 옮겨져도 배속이 따라오게 하려는 것입니다.
    /// </para>
    /// </remarks>
    private void ApplyReloadAnimationSpeed()
    {
        if (m_animator == null)
        {
            return;
        }

        float reloadTime = m_weaponController != null ? m_weaponController.ActiveReloadDuration : 0.0f;
        float eventTime = ResolveReloadEventTimeAtUnitSpeed();

        // 어느 한쪽이라도 알 수 없으면 배속을 건드리지 않습니다(1배속 유지).
        float speed = reloadTime > 0.0f && eventTime > 0.0f ? eventTime / reloadTime : 1.0f;
        m_animator.SetFloat(AnimIDReloadSpeed, speed);
    }

    /// <summary>
    /// 1배속 클립에서 재장전 완료 이벤트가 오는 시점(초)을 반환합니다. 최초 1회만 조회하고 캐싱합니다.
    /// </summary>
    /// <returns>완료 이벤트 시각(초)입니다. 이벤트를 찾지 못하면 0입니다.</returns>
    /// <remarks>
    /// 클립 이름이 아니라 <see cref="Reload"/> 이벤트를 담고 있는 클립을 찾습니다. 재장전을 끝내는 유일한 경로가
    /// 그 이벤트이므로, 이름 규칙이 바뀌어도 기준을 놓치지 않습니다.
    /// </remarks>
    private float ResolveReloadEventTimeAtUnitSpeed()
    {
        if (m_reloadEventTimeAtUnitSpeed >= 0.0f)
        {
            return m_reloadEventTimeAtUnitSpeed;
        }

        // 못 찾은 경우에도 0으로 캐싱해 매 재장전마다 전체 클립을 훑지 않게 합니다.
        m_reloadEventTimeAtUnitSpeed = 0.0f;

        RuntimeAnimatorController controller = m_animator != null ? m_animator.runtimeAnimatorController : null;
        if (controller == null)
        {
            return m_reloadEventTimeAtUnitSpeed;
        }

        foreach (AnimationClip clip in controller.animationClips)
        {
            if (clip == null)
            {
                continue;
            }

            foreach (AnimationEvent animationEvent in clip.events)
            {
                if (animationEvent.functionName == nameof(Reload))
                {
                    m_reloadEventTimeAtUnitSpeed = animationEvent.time;
                    return m_reloadEventTimeAtUnitSpeed;
                }
            }
        }

        Debug.LogWarning(
            $"[AimController] 재장전 완료 이벤트({nameof(Reload)})를 애니메이터 클립에서 찾지 못했습니다. " +
            "재장전 애니메이션 배속을 1로 유지합니다.",
            this);

        return m_reloadEventTimeAtUnitSpeed;
    }

    /// <summary>
    /// 전투 자세(백뷰)에 진입합니다. 조준(ADS)과 힙파이어가 공유하며, ads로 줌 여부만 구분합니다.
    /// </summary>
    /// <param name="ads">조준(ADS)이면 true, 힙파이어면 false입니다.</param>
    private void EnterCombatStance(bool ads)
    {
        m_isAds = ads;

        if (!m_inCombatStance)
        {
            // 총을 다 들기 전에는 사격을 막습니다. 자유 시점에서 들어오는 이 분기에서만 걸고,
            // 전투 자세 안에서 힙파이어와 ADS를 오갈 때는 총이 이미 올라와 있으므로 걸지 않습니다.
            BeginWeaponRaiseGate();

            // 새 교전 진입이므로 좌우 킥 번갈이 패턴을 첫 발부터 시작합니다.
            m_kickShotIndex = 0;

            // 조준만으로는 상체 레이어를 올리지 않습니다. 자세별 조준 포즈는 Base Layer가 이미 갖고 있고,
            // 여기서 서 있는 조준 클립을 덮어씌우면 웅크림 조준이 서 있는 자세로 바뀝니다.
            ApplyCombatStanceState(true, false, 0.0f);
            // 자유 카메라에서 백뷰로 막 진입한 프레임은 목표 FOV로 즉시 스냅(줌 점프 방지).
            ApplyCombatZoom(true);

            // 조준선은 스냅하지 않습니다. 자유 시점에서도 최소 방사각만큼 벌어져 있고(UpdateRestingCrosshair)
            // 자세별 기준 벌어짐 차이는 FOV 차이에서만 오므로, 진입·해제 모두 보간으로 이어지는 편이 자연스럽습니다.
            // ADS 진입/해제 중에는 ApplyCombatZoom이 m_baseFov를 줌 곡선으로 옮기고 조준선이 그 값을 매 프레임
            // 읽으므로, 줌 전환도 같은 보간에 실립니다.
            UpdateCrosshair(false);
        }
    }

    /// <summary>
    /// 전투 자세(조준/힙파이어) 중 매 프레임 지향점/조준점/탄착점, 회전, 마커, 사격 입력을 처리합니다.
    /// </summary>
    private void UpdateCombat()
    {
        // 지향점: 레이캐스트와 무관하게 항상 카메라 전방 먼 고정점. 캐릭터(몸통/상체 IK)가 일관되게 이 지점을 바라봅니다.
        // 몸통 수평 회전은 ThirdPersonController가 시점 모드에 맞춰 처리하므로 여기서는 IK 지향점만 갱신합니다.
        Vector3 lookPoint = ResolveLookPoint();
        ApplyLookTarget(lookPoint);

        // 조준점: 카메라 트레이스가 잡은 실제 사격 목표. 총알이 겨누는 지점입니다.
        Vector3 aimPoint = ResolveAimPoint(lookPoint, out m_currentAimTargetHealth);
        m_currentAimPoint = aimPoint;

        // Update에서는 애니메이션 입력까지만 준비합니다. 실제 탄착 계산과 발사는 IK가 끝난 LateUpdate에서
        // 총구 보정을 적용한 뒤 한 번만 수행해 표시와 사격이 같은 총구 위치를 사용하게 합니다.
        m_combatShotPending = true;
        UpdateShootState();
        ApplyCombatZoom(false);
        UpdateCrosshair(false);
    }

    /// <summary>
    /// 전투 자세 카메라(백뷰)의 FOV와 시각 킥(롤·FOV 펀치)을 상태에 맞춰 적용합니다. ADS는 확대(작은 FOV), 힙파이어는 기본 FOV입니다.
    /// </summary>
    /// <param name="snap"><c>true</c>면 목표 FOV로 즉시 설정하고 시각 킥을 초기화합니다. <c>false</c>면 보간하고 시각 킥을 회복시킵니다.</param>
    /// <remarks>
    /// 기준 FOV(<see cref="m_baseFov"/>) 위에 FOV 펀치를 얹고, 롤(Dutch)도 조준 카메라 렌즈에만 적용합니다.
    /// 롤·FOV 펀치는 시각 전용 juice라 조준값(<see cref="ThirdPersonController.LogicalAimRotation"/>)이나 탄착에는 영향이 없습니다.
    /// </remarks>
    private void ApplyCombatZoom(bool snap)
    {
        if (m_aimCamera == null)
        {
            return;
        }

        float targetFov = m_isAds ? m_adsFov : m_hipfireFov;
        UpdateBaseFov(targetFov, snap);

        if (!IsVisualKickAllowed)
        {
            m_visualKickRoll = 0.0f;
            m_visualKickFovPunch = 0.0f;
            m_rollKickEnvelope.Clear();
            m_hipfireFovPunchEnvelope.Clear();
            m_adsFovPunchEnvelope.Clear();
            m_aimCamera.Lens.FieldOfView = m_baseFov;
            m_aimCamera.Lens.Dutch = 0.0f;
            return;
        }

        if (snap)
        {
            // 전투 자세 진입 등 스냅 시엔 시각 킥도 초기화(재진입 시 롤/펀치 잔상 방지).
            m_visualKickRoll = 0.0f;
            m_visualKickFovPunch = 0.0f;
            m_rollKickEnvelope.Clear();
            m_hipfireFovPunchEnvelope.Clear();
            m_adsFovPunchEnvelope.Clear();
        }
        else
        {
            // 유지(hold) 없이 발당 순간 펀치 후 회복시켜 지속 틸트/멀미를 피합니다. 회복 속도는 모드에 따라 결정합니다.
            float recover = Mathf.Clamp01(Time.deltaTime * GetVisualKickRecoverySpeed());

            if (!m_useRollKickEnvelope)
            {
                m_visualKickRoll = Mathf.Lerp(m_visualKickRoll, 0.0f, recover);
            }

            if (!m_useFovPunchEnvelope)
            {
                m_visualKickFovPunch = Mathf.Lerp(m_visualKickFovPunch, 0.0f, recover);
            }
        }

        // 엔벨로프를 켠 축은 살아 있는 곡선의 합이 그대로 현재 값입니다. 상한은 합에 걸어 둡니다.
        float roll = m_useRollKickEnvelope
            ? Mathf.Clamp(
                m_rollKickEnvelope.Evaluate(m_rollKickEnvelopeDuration, m_rollKickEnvelopeCurve, Time.deltaTime),
                -m_visualKickMaxRoll,
                m_visualKickMaxRoll)
            : m_visualKickRoll;

        float fovPunch = m_visualKickFovPunch;
        if (m_useFovPunchEnvelope)
        {
            float hipfireSum = m_hipfireFovPunchEnvelope.Evaluate(
                m_hipfireFovPunchEnvelopeDuration, m_hipfireFovPunchEnvelopeCurve, Time.deltaTime);
            float adsSum = m_adsFovPunchEnvelope.Evaluate(
                m_adsFovPunchEnvelopeDuration, m_adsFovPunchEnvelopeCurve, Time.deltaTime);

            fovPunch = Mathf.Clamp(hipfireSum + adsSum, 0.0f, m_visualKickMaxFovPunch);
        }

        // 기준 FOV 위에 펀치를 얹고, 롤은 렌즈에만 반영(에임/탄 무영향).
        m_aimCamera.Lens.FieldOfView = m_baseFov + fovPunch;
        m_aimCamera.Lens.Dutch = roll;
    }

    /// <summary>
    /// 기준 FOV를 목표 값으로 옮깁니다.
    /// </summary>
    /// <param name="targetFov">이번 자세의 목표 FOV입니다.</param>
    /// <param name="snap">즉시 맞출지 여부입니다.</param>
    /// <remarks>
    /// 엔벨로프를 끄면 기존처럼 속도 하나로 지수 보간합니다. 이 방식은 목표에 점근하기만 해서
    /// 지속시간 개념이 없고, 확대와 축소가 같은 속도를 씁니다.
    ///
    /// 켜면 자세가 바뀐 프레임에 그때의 FOV를 시작점으로 기억하고 진행률을 0부터 셉니다.
    /// 그래서 진입과 해제에 서로 다른 시간과 곡선을 줄 수 있고, 전환 도중에 자세를 되돌려도
    /// 현재 FOV에서 새 전환이 시작돼 튀지 않습니다.
    /// </remarks>
    private void UpdateBaseFov(float targetFov, bool snap)
    {
        if (snap)
        {
            m_baseFov = targetFov;
            m_zoomFromFov = targetFov;
            m_zoomElapsed = 0.0f;
            m_zoomWasAds = m_isAds;
            return;
        }

        if (!m_useZoomEnvelope)
        {
            m_baseFov = Mathf.Lerp(m_baseFov, targetFov, Time.deltaTime * m_zoomLerpSpeed);
            m_zoomWasAds = m_isAds;
            return;
        }

        if (m_zoomWasAds != m_isAds)
        {
            m_zoomWasAds = m_isAds;
            m_zoomFromFov = m_baseFov;
            m_zoomElapsed = 0.0f;
        }

        float duration = m_isAds ? ZoomInDuration : ZoomOutDuration;
        if (duration <= 0.0f)
        {
            m_baseFov = targetFov;
            return;
        }

        m_zoomElapsed = Mathf.Min(m_zoomElapsed + Time.deltaTime, duration);
        float progress = m_zoomElapsed / duration;
        AnimationCurve curve = m_isAds ? m_zoomInCurve : m_zoomOutCurve;
        float weight = curve != null && curve.length > 0 ? curve.Evaluate(progress) : progress;

        m_baseFov = Mathf.LerpUnclamped(m_zoomFromFov, targetFov, weight);
    }

    /// <summary>
    /// 현재 모드에 따른 시각 킥 회복 속도(초당)를 반환합니다.
    /// </summary>
    /// <returns>
    /// MatchRecoil이면 반동 회복 속도(<see cref="ThirdPersonController.RecoilRecoverySpeed"/>)를 공유해 연사 중 밴드로 누적합니다.
    /// PerShotReset이면 발사 간격(ShootDelay)의 <see cref="m_visualKickRecoverShots"/>배 안에 ~95% 회복되도록 역산해(e^(-speed·T)=0.05 → speed=3/T) 발당 리셋에 가깝게 만듭니다.
    /// </returns>
    private float GetVisualKickRecoverySpeed()
    {
        if (m_visualKickRecoveryMode == VisualKickRecoveryMode.PerShotReset && m_weaponController != null)
        {
            float shots = Mathf.Max(0.01f, m_visualKickRecoverShots);
            float interval = Mathf.Max(0.0001f, m_weaponController.ShootDelay * shots);
            return 3.0f / interval;
        }

        return m_controller != null ? m_controller.RecoilRecoverySpeed : 8.0f;
    }

    /// <summary>
    /// 현재 무기 탄퍼짐 방사각과 전투 카메라 FOV를 조준선 UI 컨트롤러에 전달합니다.
    /// </summary>
    /// <param name="snap"><c>true</c>면 조준선 위치를 즉시 반영합니다.</param>
    private void UpdateCrosshair(bool snap)
    {
        if (m_crosshairController == null)
        {
            return;
        }

        float spreadDegrees = m_weaponController != null ? m_weaponController.GetCurrentSpread(m_isAds) : 0.0f;

        // 시각 FOV 펀치가 아니라 기준 FOV를 써서, 크로스헤어가 발사 juice에 따라 숨쉬지 않게 합니다.
        PushCrosshairSpread(spreadDegrees, m_baseFov, m_isAds, snap);
    }

    /// <summary>
    /// 전투 자세가 아닐 때 조준선을 현재 무기의 최소 방사각 상태로 유지합니다.
    /// </summary>
    /// <remarks>
    /// 전투 자세에서는 <see cref="UpdateCombat"/>이 매 프레임 <see cref="UpdateCrosshair"/>를 호출하지만
    /// 자유 시점에서는 그 경로가 돌지 않습니다. 예전에는 자세를 나갈 때 조준선을 방사각 0으로 되돌려,
    /// 무기에 최소 방사각이 있어도 완전히 닫힌 조준선이 보였습니다. 그래서 첫 발에 최소 방사각만큼의
    /// 벌어짐이 한꺼번에 나타났습니다. 최소 방사각은 조준하지 않아도 무기가 항상 갖는 값이므로
    /// 휴지 상태에도 그만큼 벌어져 있어야 합니다.
    /// <para>
    /// 매 프레임 통지하므로 무기 교체나 런타임 수치 변경도 자동으로 따라갑니다. 자유 시점은 전투 카메라가
    /// 꺼져 있어 화면이 메인 카메라 FOV이므로, 각도를 픽셀로 옮길 때도 그 FOV를 씁니다.
    /// </para>
    /// </remarks>
    private void UpdateRestingCrosshair()
    {
        if (m_inCombatStance || m_crosshairController == null)
        {
            return;
        }

        float fovDegrees = m_mainCamera != null ? m_mainCamera.fieldOfView : 60.0f;
        PushCrosshairSpread(ResolveRestingSpreadDegrees(), fovDegrees, false, false);
    }

    /// <summary>
    /// 전투 자세가 아닐 때 조준선이 유지할 기준 방사각(도)을 반환합니다.
    /// </summary>
    /// <returns>현재 무기의 힙파이어 최소 방사각입니다. 무기가 없으면 0입니다.</returns>
    /// <remarks>자유 시점은 조준 상태가 아니므로 ADS가 아니라 힙파이어 범위를 기준으로 삼습니다.</remarks>
    private float ResolveRestingSpreadDegrees()
    {
        if (m_weaponController == null)
        {
            return 0.0f;
        }

        m_weaponController.GetSpreadRange(false, out float minSpread, out _);
        return minSpread;
    }

    /// <summary>
    /// 조준선에 이번 프레임의 방사각·분포·FOV와 발당 펄스 기여 비율을 함께 전달합니다.
    /// </summary>
    /// <param name="spreadDegrees">조준선에 표시할 방사각(도)입니다.</param>
    /// <param name="fovDegrees">각도를 화면 픽셀로 투영할 때 쓸 세로 FOV(도)입니다.</param>
    /// <param name="snap"><c>true</c>면 조준선 간격을 즉시 반영합니다.</param>
    /// <remarks>
    /// 전투 중(<see cref="UpdateCrosshair"/>)과 휴지 중(<see cref="UpdateRestingCrosshair"/>)이 같은 본문을
    /// 쓰게 해서, 두 경로에서 분포·집중도나 펄스 비율 통지가 빠지는 일이 없게 합니다.
    /// </remarks>
    private void PushCrosshairSpread(float spreadDegrees, float fovDegrees, bool isAds, bool snap)
    {
        ApplyCrosshairPresentationIfNeeded();

        if (UsesWeaponMaxSpreadCrosshair && m_weaponController != null)
        {
            m_weaponController.GetSpreadRange(isAds, out _, out spreadDegrees);
            // 최대 경계 링은 발사 직후의 UI 펄스까지 얹으면 실제 산탄 콘보다 커집니다.
            snap = true;
        }

        SpreadDistribution distribution = m_weaponController != null
            ? m_weaponController.Distribution
            : SpreadDistribution.Gaussian;
        float concentration = m_weaponController != null ? m_weaponController.SpreadConcentration : 3.0f;

        m_crosshairController.SetSpread(spreadDegrees, distribution, concentration, fovDegrees, snap);

        // 탄퍼짐이 상한에 가까워질수록 발당 펄스 기여를 같은 비율로 줄입니다. 상한 gap의 들썩임을 막는
        // 목적은 예전 상한 판정과 같지만, 한 프레임에 펄스를 버리지 않으므로 총 gap이 도중에 줄어들지 않습니다.
        m_crosshairController.SetShotRecoilPulseSpreadScale(
            UsesWeaponMaxSpreadCrosshair ? 0.0f : 1.0f - GetCurrentSpreadProgress01());
    }

    /// <summary>이 캐릭터가 직접 조작될 때만, 자신이 소유한 조준선 표시값을 공용 HUD에 적용합니다.</summary>
    private void ApplyCrosshairPresentationIfNeeded()
    {
        // 투척 모드 동안에는 ExplosiveProjectileShooter가 같은 공용 HUD의 프리셋을 소유합니다.
        // 여기서 일반 캐릭터 프로필을 다시 쓰면 두 시스템 값이 섞여 수류탄 링이 남습니다.
        if (m_crosshairController == null || (m_input != null && m_input.ThrowMode) || HasAppliedCrosshairPresentation())
        {
            return;
        }

        m_crosshairController.CurrentSubShape = m_crosshairSubShape;
        m_crosshairController.SetSpreadDisplayBasis(m_crosshairSpreadDisplayBasis);
        m_crosshairController.SetSpreadDisplayFactorOverride(m_crosshairSpreadDisplayFactorOverride);
        m_crosshairController.CenterSpacePixels = m_crosshairCenterSpacePixels;
        m_crosshairController.SubRingSizePixels = m_crosshairSubRingBaseDiameterPixels;
        m_crosshairController.SetShotRecoilPulseEnabled(m_crosshairEnableShotRecoilPulse);
        m_crosshairController.ClearShotRecoilPulse();

        s_crosshairPresentationOwner = this;
        s_crosshairPresentationTarget = m_crosshairController;
        s_crosshairPresentationSubShape = m_crosshairSubShape;
        s_crosshairPresentationSpreadBasis = m_crosshairSpreadDisplayBasis;
        s_crosshairPresentationDisplayFactorOverride = m_crosshairSpreadDisplayFactorOverride;
        s_crosshairPresentationCenterSpacePixels = m_crosshairCenterSpacePixels;
        s_crosshairPresentationSubRingBaseDiameterPixels = m_crosshairSubRingBaseDiameterPixels;
        s_crosshairPresentationShotRecoilPulse = m_crosshairEnableShotRecoilPulse;
    }

    /// <summary>현재 캐릭터 프로필이 HUD에 적용된 값과 같은지 비교합니다. Inspector 실시간 수정을 놓치지 않기 위해 모든 표시값을 확인합니다.</summary>
    private bool HasAppliedCrosshairPresentation()
    {
        return s_crosshairPresentationOwner == this
            && s_crosshairPresentationTarget == m_crosshairController
            && s_crosshairPresentationSubShape == m_crosshairSubShape
            && s_crosshairPresentationSpreadBasis == m_crosshairSpreadDisplayBasis
            && Mathf.Approximately(s_crosshairPresentationDisplayFactorOverride, m_crosshairSpreadDisplayFactorOverride)
            && Mathf.Approximately(s_crosshairPresentationCenterSpacePixels, m_crosshairCenterSpacePixels)
            && Mathf.Approximately(s_crosshairPresentationSubRingBaseDiameterPixels, m_crosshairSubRingBaseDiameterPixels)
            && s_crosshairPresentationShotRecoilPulse == m_crosshairEnableShotRecoilPulse
            && m_crosshairController.CurrentSubShape == m_crosshairSubShape
            && m_crosshairController.CurrentSpreadDisplayBasis == m_crosshairSpreadDisplayBasis
            && Mathf.Approximately(m_crosshairController.SpreadDisplayFactorOverride, m_crosshairSpreadDisplayFactorOverride)
            && Mathf.Approximately(m_crosshairController.CenterSpacePixels, m_crosshairCenterSpacePixels)
            && Mathf.Approximately(m_crosshairController.SubRingSizePixels, m_crosshairSubRingBaseDiameterPixels)
            && m_crosshairController.ShotRecoilPulseEnabled == m_crosshairEnableShotRecoilPulse;
    }

    private bool UsesWeaponMaxSpreadCrosshair => m_crosshairSpreadMode == CrosshairSpreadMode.WeaponMaxSpread;

    /// <summary>
    /// 전투 스탠스(자유시점/힙파이어/ADS)가 바뀐 프레임에만 조준선 디버그 스냅샷을 한 번 캡처합니다.
    /// </summary>
    /// <remarks>
    /// 매 프레임 디버그 갱신을 피하고, 전환 시점에 상태별 목표 FOV(ADS/힙파이어)와 자유시점 카메라 FOV를 크로스헤어로 전달합니다.
    /// 시각 표시(<see cref="UpdateCrosshair"/>)는 매 프레임 그대로 갱신되며, 이 캡처는 디버그 값에만 영향을 줍니다.
    /// </remarks>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void UpdateCrosshairDebugOnStanceChange()
    {
        if (m_crosshairController == null)
        {
            return;
        }

        CombatStance stance = !m_inCombatStance
            ? CombatStance.Free
            : (m_isAds ? CombatStance.Ads : CombatStance.Hipfire);

        if (stance == m_lastCombatStance)
        {
            return;
        }

        m_lastCombatStance = stance;

        // 값 복사가 아니라 라이브 소스 포인터를 연결만 한다. 이후 디버그 표시는 이 포인터로 현재값을 읽는다.
        switch (stance)
        {
            case CombatStance.Ads:
                m_crosshairController.BindSpreadDebug(
                    "Ads",
                    () => m_weaponController != null ? m_weaponController.GetCurrentSpread(true) : 0.0f,
                    () => m_adsFov);
                break;

            case CombatStance.Hipfire:
                m_crosshairController.BindSpreadDebug(
                    "Hipfire",
                    () => m_weaponController != null ? m_weaponController.GetCurrentSpread(false) : 0.0f,
                    () => m_hipfireFov);
                break;

            default:
                // 자유 시점도 힙파이어 최소 방사각을 유지하므로(UpdateRestingCrosshair) 0이 아니라 그 값을 읽습니다.
                m_crosshairController.BindSpreadDebug(
                    "Free",
                    () => ResolveRestingSpreadDegrees(),
                    () => m_mainCamera != null ? m_mainCamera.fieldOfView : 60.0f);
                break;
        }
    }

    /// <summary>
    /// 전투 자세를 나갈 때 조준선의 발사 피드백만 즉시 정리합니다.
    /// </summary>
    /// <remarks>
    /// 벌어짐 자체는 0으로 되돌리지 않습니다. 무기의 최소 방사각은 자유 시점에서도 유효하므로
    /// <see cref="UpdateRestingCrosshair"/>가 그 값을 목표로 잡고 조준선의 보간이 부드럽게 접근합니다.
    /// 여기서 지우는 것은 그 프레임까지 남아 있던 발당 반동 펄스뿐입니다.
    /// </remarks>
    private void ResetCrosshair()
    {
        if (m_crosshairController != null)
        {
            m_crosshairController.ClearShotRecoilPulse();
        }
    }

    /// <summary>
    /// 전투 자세를 해제하고 자유 TPS 시점으로 복귀합니다.
    /// </summary>
    private void ExitCombatStance()
    {
        if (!m_inCombatStance)
        {
            return;
        }

        m_isAds = false;
        m_hipfireTimer = 0.0f;
        ApplyCombatStanceState(false, false, 0.0f);
    }

    /// <summary>
    /// 레이캐스트와 무관하게 카메라 전방 먼 고정점을 지향점(LookPoint)으로 계산합니다.
    /// </summary>
    /// <returns>카메라 전방 지향 거리(무기 히트스캔 사거리 이상)에 위치한 월드 지향점입니다.</returns>
    /// <remarks>항상 먼 지점을 바라보므로 가까운 장애물이 끼어도 몸통/상체 회전이 급변하지 않습니다.</remarks>
    private Vector3 ResolveLookPoint()
    {
        Transform cameraTransform = m_mainCamera.transform;

        // 지향점은 항상 먼 지점이어야 하므로, 무기 히트스캔 사거리보다 짧지 않게 보정합니다.
        float lookDistance = m_lookDistance;
        if (m_weaponController != null)
        {
            lookDistance = Mathf.Max(lookDistance, m_weaponController.HitscanRange);
        }

        return cameraTransform.position + GetAimForward() * lookDistance;
    }

    /// <summary>
    /// 조준 계산에 사용할 전방 방향을 반환합니다. 카메라 킥(시각 흔들림)이 빠진 논리 조준 방향이라 에임이 흔들림과 독립됩니다.
    /// </summary>
    /// <returns>카메라 킥이 빠진 논리 조준의 정규화 전방 방향입니다.</returns>
    /// <remarks>
    /// PO 핸드오프 기준(2026-06-30 카메라 킥/AimPoint): AimPoint는 카메라 킥이 반영된 렌더 방향이 아니라 논리 조준 방향으로 계산한다.
    /// 화면(뷰)은 카메라 킥으로 흔들리되 지향점/조준점/탄착점은 킥의 영향을 받지 않는다. 명중 영향은 탄퍼짐(과 추후 총기 반동)이 담당한다.
    /// </remarks>
    private Vector3 GetAimForward()
    {
        if (m_controller != null)
        {
            return m_controller.LogicalAimForward;
        }

        return m_mainCamera.transform.forward;
    }

    /// <summary>
    /// 카메라 트레이스로 조준점(AimPoint, 실제 사격 목표)을 계산합니다.
    /// </summary>
    /// <param name="lookPoint">이번 프레임의 지향점입니다. 카메라 트레이스가 아무것도 못 맞히면 이 먼 지점을 조준점으로 사용합니다.</param>
    /// <returns>카메라가 크로스헤어로 가리키는 실제 월드 지점(미충돌 시 지향점)입니다.</returns>
    /// <remarks>총알은 총구→이 지점으로 향하므로, 가까운 적도 시차 없이 정확히 겨눕니다.</remarks>
    private Vector3 ResolveAimPoint(Vector3 lookPoint, out HealthSystemBase aimTarget)
    {
        aimTarget = null;
        Transform cameraTransform = m_mainCamera.transform;
        Vector3 direction = GetAimForward().normalized;
        Vector3 origin = ResolveAimTraceOrigin(cameraTransform.position, direction);
        float aimDistance = Mathf.Max(0.0f, Vector3.Dot(lookPoint - origin, direction));

        // 레이어가 지정돼 있으면 그것을, 아니면 무기 히트스캔 레이어(없으면 전체)에서 소유(본인) 레이어를 제외해
        // 카메라 트레이스가 자기 콜라이더를 조준점으로 잡지 않게 합니다.
        int mask;
        if (m_targetLayer.value != 0)
        {
            mask = m_targetLayer.value;
        }
        else
        {
            int baseMask = m_weaponController != null ? m_weaponController.HitscanLayerMask.value : ~0;
            mask = baseMask & ~(1 << gameObject.layer);
        }

        // 부위 히트박스는 평소 꺼져 있어 카메라 트레이스가 직접 볼 수 없습니다. 그래서 무기가 있으면
        // 사격과 같은 2단계 규칙(감지 볼륨으로 후보를 찾고 그 부위만 잠깐 켠 뒤 같은 레이를 다시 쏘기)으로
        // **부위 표면**을 조준점으로 받습니다. 감지 볼륨 표면을 조준점으로 쓰면 안 되는 이유는
        // Gun.TryTraceAimPoint 주석에 있습니다 - 요약하면 총구선과 카메라선이 조준점에서만 만나므로,
        // 조준점이 몸보다 앞에 있으면 그 차이가 총구 오프셋 비율로 증폭되어 팔·손·정강이·발이 통째로 빗나갑니다.
        //
        // 아군의 몸은 조준점으로 잡지 않습니다(무기의 아군 통과 규칙을 그대로 씁니다). 팀원이 앞을 지나가는
        // 순간 조준점이 그 등판으로 당겨지면 화면 중앙의 적을 겨누고 있는데도 조준 거리와 차단 표시가 함께 어긋납니다.
        if (m_weaponController != null)
        {
            if (!m_weaponController.TryTraceAimPoint(
                    origin, direction, aimDistance, mask, out RaycastHit staged))
            {
                return lookPoint;
            }

            aimTarget = ResolveHealthOwner(staged.collider);
            return staged.point;
        }

        // 무기가 없으면 켤 부위도, 마스크를 소유한 주체도 없으므로 단발 트레이스로 돌아갑니다.
        if (TryTraceAim(origin, direction, aimDistance, mask, out RaycastHit hit))
        {
            aimTarget = ResolveHealthOwner(hit.collider);
            return hit.point;
        }

        return lookPoint;
    }

    /// <summary>화면 중앙 레이를 유지하면서 캐릭터와 총구 뒤의 구간을 조준 검색에서 제외합니다.</summary>
    private Vector3 ResolveAimTraceOrigin(Vector3 cameraPosition, Vector3 direction)
    {
        // 거리 자체가 아니라 레이 방향으로 투영한 깊이를 사용해야 숄더 카메라/상하 조준에서도
        // 같은 화면 중앙선을 유지합니다. 총구보다 뒤의 목표를 잡아 역방향으로 발사하는 것도 막습니다.
        float depth = Vector3.Dot(transform.position - cameraPosition, direction);
        if (m_weaponController != null && m_weaponController.FirePos != null)
        {
            depth = Mathf.Max(depth, Vector3.Dot(m_weaponController.FirePos.position - cameraPosition, direction));
        }

        // 이 제외 구간은 카메라 조준에만 적용합니다. 총구에서 나가는 실제 탄착 검사는 그대로 둡니다.
        return cameraPosition + direction * Mathf.Max(0.0f, depth + 0.01f);
    }

    /// <summary>
    /// 조준 트레이스를 수행합니다. 무기 설정이 아군 통과이면 아군의 몸을 건너뜁니다.
    /// </summary>
    /// <param name="origin">추적 시작 위치입니다.</param>
    /// <param name="direction">추적 방향입니다.</param>
    /// <param name="distance">추적 거리입니다.</param>
    /// <param name="mask">사용할 레이어 마스크입니다.</param>
    /// <param name="hit">가장 먼저 막은 대상입니다.</param>
    /// <returns>무언가에 막혔으면 true입니다.</returns>
    /// <remarks>
    /// 판정 기준을 무기(<see cref="Gun.AllyBulletPassThrough"/>)에서 가져오는 이유는, 조준점과 실제 탄착이
    /// 같은 규칙을 써야 하기 때문입니다. 한쪽만 아군을 통과하면 조준선과 탄착이 갈립니다.
    ///
    /// 이 트레이스는 사격이 없어도 전투 자세 동안 매 프레임 돕니다. 그래서 할당이 없는
    /// <see cref="Physics.RaycastNonAlloc"/>와 재사용 버퍼를 씁니다. 가장 가까운 유효 대상을 고르는 규칙은
    /// <see cref="Gun.TryResolveNearestBlocking"/>가 무기 쪽과 공유합니다.
    /// </remarks>
    private bool TryTraceAim(Vector3 origin, Vector3 direction, float distance, int mask, out RaycastHit hit)
    {
        // 무기가 없으면 아군 통과 규칙도 없지만, 시체(래그돌) 무시는 무기와 무관하게 적용해야 하므로
        // 단발 Raycast로 돌아가지 않고 같은 경로를 씁니다.
        bool passAllies = m_weaponController != null && m_weaponController.AllyBulletPassThrough;
        Faction faction = m_weaponController != null ? m_weaponController.OwnerFaction : Faction.Player;

        int count = Physics.RaycastNonAlloc(
            origin, direction, m_aimTraceBuffer, distance, mask, QueryTriggerInteraction.Collide);

        return Gun.TryResolveNearestBlocking(m_aimTraceBuffer, count, faction, passAllies, out hit);
    }

    /// <summary>
    /// 히트스캔 충돌 결과로부터 현재 조준 중인 적을 갱신합니다.
    /// </summary>
    /// <param name="shotInfo">현재 조준 프레임에서 계산된 히트스캔 사격 정보입니다.</param>
    /// <remarks>조준 대상 적 판정은 실제 탄착 경로(총구 히트스캔)를 기준으로 합니다.</remarks>
    private void UpdateCurrentAimEnemy(Gun.HitscanShotInfo shotInfo)
    {
        m_currentAimEnemy = shotInfo.HasHit && shotInfo.Hit.collider != null
            ? shotInfo.Hit.collider.GetComponentInParent<EnemyController>()
            : null;
    }

    /// <summary>
    /// 상체 회전 IK가 바라보는 지향점 타겟 오브젝트의 위치를 지향점으로 갱신합니다.
    /// </summary>
    /// <param name="lookPoint">이번 프레임의 지향점(먼 지점)입니다.</param>
    /// <remarks>이 오브젝트는 MultiAimConstraint(상체 회전)의 source이며, 손목 IK는 별도 타겟을 사용해 영향을 받지 않습니다.</remarks>
    private void ApplyLookTarget(Vector3 lookPoint)
    {
        if (m_lookTarget == null)
        {
            return;
        }

        m_lookTarget.transform.position = lookPoint;
    }

    /// <summary>
    /// 조준점(카메라 트레이스 목표)과 무기 총구를 기준으로 현재 프레임의 히트스캔 사격 정보를 계산합니다.
    /// </summary>
    /// <param name="targetPosition">카메라 트레이스로 계산한 조준점(AimPoint)입니다. 총구가 이 지점을 향해 발사합니다.</param>
    /// <returns>총구 원점, 발사 방향, 탄착점(EndPoint), 충돌 및 중간 장애물 여부를 포함한 사격 정보입니다.</returns>
    /// <remarks>이 결과는 조준 마커 표시와 실제 히트스캔 사격 처리에서 동일하게 사용됩니다.</remarks>
    private Gun.HitscanShotInfo EvaluateHitscanShot(Vector3 targetPosition, HealthSystemBase aimTarget)
    {
        Gun.HitscanShotInfo shotInfo = new()
        {
            AimPoint = targetPosition,
            EndPoint = targetPosition,
            FrameCount = Time.frameCount,
        };

        if (m_weaponController == null || m_weaponController.FirePos == null)
        {
            return shotInfo;
        }

        Transform firePos = m_weaponController.FirePos;
        Vector3 origin = firePos.position;
        Vector3 aimVector = targetPosition - origin;
        float aimDistance = aimVector.magnitude;
        Vector3 direction = aimDistance <= 0.0001f
            ? firePos.forward
            : aimVector / aimDistance;

        float hitscanRange = Mathf.Max(0.0f, m_weaponController.HitscanRange);
        float rayDistance = aimDistance <= 0.0001f
            ? hitscanRange
            : Mathf.Min(hitscanRange, aimDistance);

        shotInfo.IsValid = rayDistance > Gun.HitscanAimTolerance;
        shotInfo.Origin = origin;
        shotInfo.Direction = direction;
        shotInfo.EndPoint = origin + direction * rayDistance;

        if (!shotInfo.IsValid)
        {
            return shotInfo;
        }

        if (!m_weaponController.TryTraceAimPoint(
                origin,
                direction,
                rayDistance,
                m_weaponController.HitscanLayerMask,
                out RaycastHit hit))
        {
            return shotInfo;
        }

        shotInfo.HasHit = true;
        shotInfo.Hit = hit;
        shotInfo.EndPoint = hit.point;

        // 겨눈 대상 본인은 장애물이 아닙니다. 총구와 카메라의 위치 차이 때문에 같은 적의 다른 부위가
        // 조준점보다 앞에서 맞는 일이 흔하고, 거리만 비교하면 그것이 차단으로 잡힙니다.
        bool hitIsAimTarget = aimTarget != null && ResolveHealthOwner(hit.collider) == aimTarget;

        shotInfo.IsObstructed = !hitIsAimTarget
            && aimDistance > 0.0001f
            && hit.distance < aimDistance - Gun.HitscanAimTolerance;

        return shotInfo;
    }

    /// <summary>
    /// 히트스캔 사격 정보에 중간 장애물이 있으면 탄착점 화면 위치에 차단 마커 UI를 표시합니다.
    /// </summary>
    /// <param name="shotInfo">현재 조준 프레임에서 계산된 히트스캔 사격 정보입니다.</param>
    /// <remarks>
    /// 표시는 <see cref="CrosshairController"/>가 소유합니다. 여기서는 "막혔는지"와 "어느 월드 지점인지"만 넘깁니다.
    /// <para>
    /// 예전에는 월드 평면(Plane) 오브젝트를 표면 법선에 맞춰 눕히고 오프셋으로 띄웠습니다. 화면 UI로 바꾸면서
    /// 벽면 스내핑과 법선 오프셋을 없앴습니다. 마커가 벽 기울기에 따라 찌그러지지 않고, 표면과 겹쳐 생기는
    /// z-파이팅과 얇은 벽 뒷면으로의 관통 문제도 함께 사라집니다.
    /// </para>
    /// </remarks>
    private void UpdateHitscanBlockMarker(Gun.HitscanShotInfo shotInfo)
    {
        if (m_crosshairController == null || m_weaponController == null)
        {
            return;
        }

        if (!shotInfo.IsValid || !shotInfo.IsObstructed)
        {
            HideHitscanBlockMarker();
            return;
        }

        m_crosshairController.ShowBlockMarker(shotInfo.EndPoint, m_mainCamera);
    }

    /// <summary>
    /// 히트스캔 장애물 차단 마커 UI를 숨깁니다.
    /// </summary>
    private void HideHitscanBlockMarker()
    {
        if (m_crosshairController != null)
        {
            m_crosshairController.HideBlockMarker();
        }
    }

    /// <summary>
    /// 조준 중 계산된 히트스캔 사격 정보를 Scene 뷰 디버그 레이로 표시합니다.
    /// </summary>
    /// <param name="shotInfo">현재 조준 프레임에서 계산된 히트스캔 사격 정보입니다.</param>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void DrawHitscanDebugRay(Gun.HitscanShotInfo shotInfo)
    {
        if (!m_drawHitscanDebugRay || !shotInfo.IsValid)
        {
            return;
        }

        Debug.DrawLine(
            shotInfo.Origin,
            shotInfo.EndPoint,
            shotInfo.IsObstructed ? Color.red : Color.yellow,
            0.0f,
            false);
    }

    /// <summary>
    /// 카메라에서 조준점까지의 트레이스 선을 그립니다(캠→조준점). 총구 기준 탄착점 레이와의 벌어짐 확인용입니다.
    /// </summary>
    /// <param name="shotInfo">현재 조준 프레임에서 계산된 히트스캔 사격 정보입니다. <see cref="Gun.HitscanShotInfo.AimPoint"/>가 조준점입니다.</param>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void DrawAimTraceDebugLine(Gun.HitscanShotInfo shotInfo)
    {
        if (!m_drawAimTraceLine)
        {
            return;
        }

        Debug.DrawLine(ResolveAimTraceOrigin(m_mainCamera.transform.position, GetAimForward().normalized),
            shotInfo.AimPoint, Color.cyan, 0.0f, false);
    }

    /// <summary>
    /// 지향점(논리 조준, 킥 제거) 레이와 단순 카메라 forward(렌더 방향, 킥 포함) 레이를 함께 그려 카메라 킥의 에임 분리 여부를 확인합니다.
    /// </summary>
    /// <param name="lookPoint">이번 프레임의 지향점(논리 조준 먼 지점)입니다.</param>
    /// <remarks>
    /// green = 지향점 레이(`GetAimForward`, 킥 제거 / 실제 사격 방향), blue = 카메라 forward 레이(`Camera.main.forward`, 킥 포함 / 렌더 방향).
    /// 사격 시 두 선이 벌어지면 킥이 카메라에만 적용되고 에임에는 분리된 것이며, 안 벌어지면 킥이 활성 카메라에 닿지 않은 것입니다.
    /// </remarks>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void DrawCameraForwardDebugRay(Vector3 lookPoint)
    {
        if (!m_drawCameraForwardRay)
        {
            return;
        }

        Transform cameraTransform = m_mainCamera.transform;
        float rayLength = Vector3.Distance(cameraTransform.position, lookPoint);

        // 지향점(논리 조준, 킥 제거) 레이.
        Debug.DrawLine(cameraTransform.position, lookPoint, Color.green, 0.0f, false);

        // 단순 카메라 forward(렌더 방향, 킥 포함) 레이.
        Debug.DrawLine(
            cameraTransform.position,
            cameraTransform.position + cameraTransform.forward * rayLength,
            Color.blue,
            0.0f,
            false);
    }

    /// <summary>
    /// 지향점(green)/조준점(cyan)/탄착점(magenta)에 디버그 스피어를 그립니다.
    /// </summary>
    /// <param name="lookPoint">이번 프레임의 지향점(캐릭터가 바라보는 먼 지점)입니다.</param>
    /// <param name="shotInfo">현재 조준 프레임에서 계산된 히트스캔 사격 정보입니다.</param>
    /// <remarks>조준점은 <see cref="Gun.HitscanShotInfo.AimPoint"/>(카메라 트레이스 목표), 탄착점은 <see cref="Gun.HitscanShotInfo.EndPoint"/>(총구 히트스캔 최종 지점)입니다.</remarks>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void DrawAimDebugSpheres(Vector3 lookPoint, Gun.HitscanShotInfo shotInfo)
    {
        if (m_drawLookPointSphere)
        {
            DrawDebugSphere(lookPoint, m_debugSphereRadius, Color.green);
        }

        if (m_drawAimPointSphere)
        {
            DrawDebugSphere(shotInfo.AimPoint, m_debugSphereRadius, Color.cyan);
        }

        if (m_drawImpactPointSphere)
        {
            DrawDebugSphere(shotInfo.EndPoint, m_debugSphereRadius, Color.magenta);
        }
    }

    /// <summary>
    /// 세 직교 평면의 원으로 와이어 스피어를 한 프레임 동안 그립니다.
    /// </summary>
    /// <param name="center">스피어 중심 월드 좌표입니다.</param>
    /// <param name="radius">스피어 반지름입니다.</param>
    /// <param name="color">스피어 색상입니다.</param>
    /// <remarks><see cref="Debug.DrawLine"/> 기반이라 Scene 뷰, 그리고 Gizmos가 켜진 Game 뷰에서 표시됩니다.</remarks>
    private static void DrawDebugSphere(Vector3 center, float radius, Color color)
    {
        const int segments = 16;
        float step = 2.0f * Mathf.PI / segments;

        for (int i = 0; i < segments; i++)
        {
            float a = i * step;
            float b = (i + 1) * step;
            float ca = Mathf.Cos(a);
            float sa = Mathf.Sin(a);
            float cb = Mathf.Cos(b);
            float sb = Mathf.Sin(b);

            Debug.DrawLine(center + new Vector3(ca, sa, 0.0f) * radius, center + new Vector3(cb, sb, 0.0f) * radius, color, 0.0f, false);
            Debug.DrawLine(center + new Vector3(ca, 0.0f, sa) * radius, center + new Vector3(cb, 0.0f, sb) * radius, color, 0.0f, false);
            Debug.DrawLine(center + new Vector3(0.0f, ca, sa) * radius, center + new Vector3(0.0f, cb, sb) * radius, color, 0.0f, false);
        }
    }

    /// <summary>
    /// 사격이 발사된 프레임에 탄착점에 디버그 마커 오브젝트를 생성합니다.
    /// </summary>
    /// <param name="shotInfo">발사된 사격의 히트스캔 정보입니다.</param>
    /// <remarks>
    /// 토글이 켜진 개발 모드에서만 생성합니다. 마커 프리팹이 비어 있으면 플레이테스트에서도 바로 확인할 수 있도록
    /// 작은 마젠타 스피어를 임시로 만들고, 충돌 표면이 있으면 법선 방향으로 정렬합니다.
    /// </remarks>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void SpawnImpactMarker(Gun.HitscanShotInfo shotInfo)
    {
        if (!GameDevMode.DebugFeaturesEnabled || !m_spawnImpactMarkerOnShot)
        {
            return;
        }

        Quaternion rotation = shotInfo.HasHit && shotInfo.Hit.normal.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(shotInfo.Hit.normal)
            : Quaternion.identity;

        GameObject marker;
        if (m_impactMarkerPrefab != null)
        {
            marker = Instantiate(m_impactMarkerPrefab, shotInfo.EndPoint, rotation);
        }
        else
        {
            marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "ImpactMarker(Trainer)";
            marker.transform.SetPositionAndRotation(shotInfo.EndPoint, rotation);
            marker.transform.localScale = Vector3.one * m_impactMarkerSize;

            Collider markerCollider = marker.GetComponent<Collider>();
            if (markerCollider != null)
            {
                Destroy(markerCollider);
            }

            Renderer markerRenderer = marker.GetComponent<Renderer>();
            if (markerRenderer != null)
            {
                markerRenderer.material.color = Color.magenta;
            }
        }

        Destroy(marker, m_impactMarkerLifetime);
    }


    /// <summary>
    /// 사격 입력 상태를 애니메이터와 무기 컨트롤러에 반영합니다.
    /// </summary>
    private void UpdateShootState()
    {
        // 상체 레이어를 사격·재장전에만 올립니다. 조준 포즈는 Base Layer 몫이라 여기서 관여하지 않습니다.
        RefreshWeaponLayerWeight();

        // 총을 다 들기 전에는 발사도, 사격 포즈도 내보내지 않습니다. 포즈만 먼저 나가면 총을 드는 도중에
        // 사격 자세로 튀어 "다 들고 나서 쏜다"가 무너집니다.
        bool shootPressed = m_input.Shoot;

        if (shootPressed && IsRaisingWeapon)
        {
            m_animator.SetBool(AnimIDShoot, false);
            return;
        }

        bool shouldRequestShot = shootPressed
            && (m_weaponController == null
                || m_weaponController.FireMode == GunFireMode.FullAuto
                || !m_wasShootPressed);

        m_wasShootPressed = shootPressed;

        if (shouldRequestShot)
        {
            m_animator.SetBool(AnimIDShoot, true);

            m_fireRequested = true;

            return;
        }

        m_animator.SetBool(AnimIDShoot, false);
    }

    /// <summary>
    /// 홀드 입력 자체가 연속 사격을 뜻하는 총기인지 확인합니다.
    /// </summary>
    /// <remarks>
    /// 반자동은 첫 클릭 한 번만 소비하므로 버튼을 계속 누르고 있어도 탄퍼짐과 조준선 반동 회복을
    /// 막지 않습니다. 실제 사격 직후의 회복 지연은 Gun이 마지막 사격 시각으로 별도 처리합니다.
    /// </remarks>
    private bool IsContinuousFireHeld => m_input != null
        && m_input.Shoot
        && m_weaponController != null
        && m_weaponController.FireMode == GunFireMode.FullAuto;

    /// <summary>
    /// 상체를 따로 쓰는 행동이 시작되면 진행 중인 재장전을 접습니다.
    /// </summary>
    /// <remarks>
    /// 투척은 재장전과 같은 상체를 쓰므로 둘을 겹칠 수 없습니다. 재장전이 자동으로 걸리게 되면서
    /// 수류탄을 꺼내려는 순간마다 재장전이 먼저 잡고 있을 수 있어, 들어온 쪽을 우선합니다.
    ///
    /// 무기 타이머도 함께 끊습니다. 비주얼만 접고 타이머를 두면 아무 동작 없이 탄이 채워집니다.
    /// 접힌 재장전은 조건이 풀리는 프레임에 <see cref="TryAutoReloadWhenEmpty"/>가 다시 시작합니다.
    /// </remarks>
    private void CancelReloadForUpperBodyAction()
    {
        if (m_controller == null || !m_controller.IsReload || !IsUpperBodyActionRequested)
        {
            return;
        }

        if (m_weaponController != null)
        {
            m_weaponController.CancelReload();
        }

        FinishReloadVisualState(false);
    }

    /// <summary>
    /// 상체를 따로 쓰는 행동이 요청된 상태인지 여부입니다.
    /// </summary>
    /// <remarks>
    /// 지금은 투척 모드뿐입니다. 같은 성격의 행동(근접, 아이템 사용 등)이 생기면 여기에 더하면
    /// 재장전 취소와 자동 재장전 보류가 함께 따라옵니다. 두 곳에서 따로 판단하면 한쪽만 빠집니다.
    /// </remarks>
    private bool IsUpperBodyActionRequested => m_input != null && m_input.ThrowMode;

    /// <summary>
    /// 탄창이 비어 있으면 입력 없이 재장전을 시작합니다.
    /// </summary>
    /// <remarks>
    /// 상체를 따로 쓰는 행동 중에는 걸지 않습니다. 그 행동이 끝나면 이 메서드가 매 프레임 다시
    /// 조건을 보므로 자동으로 이어집니다.
    /// </remarks>
    private void TryAutoReloadWhenEmpty()
    {
        if (IsUpperBodyActionRequested)
        {
            return;
        }

        TryReloadOnEmptyFire();
    }

    /// <summary>
    /// 탄창이 비어 있으면 재장전을 시작합니다.
    /// </summary>
    /// <returns>재장전을 시작했으면 <c>true</c>입니다.</returns>
    /// <remarks>
    /// R키 경로(<see cref="HandleReloadInput"/>)와 같은 게이트를 씁니다. 조건을 따로 만들면 한쪽만 바뀌어
    /// 두 경로의 재장전 가능 여부가 갈립니다.
    /// </remarks>
    private bool TryReloadOnEmptyFire()
    {
        if (m_weaponController == null || m_weaponController.CurrentBullet > 0)
        {
            return false;
        }

        if (m_controller != null && m_controller.IsReload)
        {
            return false;
        }

        if (!m_weaponController.CanReload)
        {
            return false;
        }

        BeginReload();
        return true;
    }

    /// <summary>
    /// 발사가 성사된 프레임에 무기별 수치를 읽어 (1) 에임에 영향을 주는 반동과 (2) 에임 무영향 시각 킥을 함께 가합니다.
    /// </summary>
    /// <remarks>
    /// 반동(에임): 좌우(요)는 매 발 <c>-RecoilYawKick ~ +RecoilYawKick</c> 무작위. <see cref="ThirdPersonController.AddRecoil"/>가
    /// 논리 조준에 얹어 탄착까지 밀며, 사격을 멈추면 자동 회복합니다.
    /// 시각 킥(juice): 카메라 롤(Dutch)과 FOV 펀치를 누적하며, 조준/탄착에는 영향이 없습니다(<see cref="ApplyCombatZoom"/>에서 회복·적용).
    /// </remarks>
    private void ApplyRecoilAndVisualKick()
    {
        if (m_controller == null || m_weaponController == null)
        {
            return;
        }

        // 버스트 사이 간격이 벌어졌으면 좌우 번갈이 패턴을 첫 발부터 다시 시작합니다.
        if (Time.time - m_lastKickTime > KickPatternResetGap)
        {
            m_kickShotIndex = 0;
        }
        m_lastKickTime = Time.time;

        // 좌우 패턴에 따라 이번 발의 Yaw 반동·롤 부호(및 크기)를 각각 독립적으로 결정합니다(같은 발 인덱스 공유).
        float yawSigned = ResolveKickValue(m_weaponController.YawKickPattern, m_weaponController.RecoilYawKick, m_kickShotIndex);
        float rollSigned = ResolveKickValue(m_weaponController.RollKickPattern, m_weaponController.RecoilRoll, m_kickShotIndex);
        m_kickShotIndex++;

        // (1) 반동 — 실제 조준을 밀어 탄착에도 영향(세로 pitch + 좌우 yaw).
        if (m_enableAimRecoil)
        {
            m_controller.AddRecoil(m_weaponController.RecoilPitchKick, yawSigned);
        }

        // 실제 탄퍼짐은 초반 정밀탄에서 0일 수 있으므로, 발사 성공 자체를 기준으로 UI 반동 펄스를 별도로 준다.
        // 상한 근처에서의 들썩임 억제는 펄스를 버리는 대신 UpdateCrosshair가 매 프레임 통지하는
        // 비례 감쇠(SetShotRecoilPulseSpreadScale)가 담당한다. 그래서 여기서는 분기 없이 항상 펄스를 준다.
        if (m_crosshairController != null && !UsesWeaponMaxSpreadCrosshair)
        {
            m_crosshairController.TriggerShotRecoilPulse();
        }

        // (2) 시각 킥 — 롤·FOV 펀치 누적(조준/탄 무영향, 상한 클램프).
        if (IsVisualKickAllowed)
        {
            // 엔벨로프를 켠 축은 이번 발의 곡선 하나를 시작만 하고, 값은 매 프레임 합에서 나옵니다.
            if (m_useRollKickEnvelope)
            {
                m_rollKickEnvelope.Add(rollSigned);
            }
            else
            {
                m_visualKickRoll = Mathf.Clamp(m_visualKickRoll + rollSigned, -m_visualKickMaxRoll, m_visualKickMaxRoll);
            }

            // 조준 중에는 화면이 확대돼 같은 펀치도 더 크게 보이므로 크기를 자세별로 나눠 씁니다.
            float fovPunch = m_isAds ? m_weaponController.RecoilFovPunchAds : m_weaponController.RecoilFovPunch;

            if (m_useFovPunchEnvelope)
            {
                // 쏜 시점의 자세에 해당하는 엔벨로프에 넣습니다. 쏜 뒤 자세를 바꿔도 그 발의 길이와 모양이 유지됩니다.
                if (m_isAds)
                {
                    m_adsFovPunchEnvelope.Add(fovPunch);
                }
                else
                {
                    m_hipfireFovPunchEnvelope.Add(fovPunch);
                }
            }
            else
            {
                m_visualKickFovPunch = Mathf.Clamp(m_visualKickFovPunch + fovPunch, 0.0f, m_visualKickMaxFovPunch);
            }
        }
    }

    /// <summary>
    /// 현재 자세의 무기 탄퍼짐이 최소값에서 상한까지 얼마나 진행했는지 0~1로 반환합니다.
    /// </summary>
    /// <returns>최소 방사각에서 0, 상한에서 1입니다. 무기가 없거나 동적 벌어짐이 없으면 0입니다.</returns>
    /// <remarks>
    /// 발당 UI 펄스의 기여 비율을 정하는 데 씁니다. 상한에서 1이 되어 펄스 기여가 0으로 맞물리므로,
    /// 예전 상한 판정처럼 최대 gap에서의 들썩임을 막으면서도 벌어짐의 단조증가가 유지됩니다.
    /// 최소값과 최대값이 같은 정밀 무기는 동적 벌어짐 자체가 없으므로 0을 돌려주어 발당 UI 피드백을 온전히 보존합니다.
    /// </remarks>
    private float GetCurrentSpreadProgress01()
    {
        if (m_weaponController == null)
        {
            return 0.0f;
        }

        m_weaponController.GetSpreadRange(m_isAds, out float minSpread, out float maxSpread);
        float range = maxSpread - minSpread;
        if (range <= 0.001f)
        {
            return 0.0f;
        }

        return Mathf.Clamp01((m_weaponController.GetCurrentSpread(m_isAds) - minSpread) / range);
    }

    /// <summary>
    /// 좌우 킥 패턴에 따라 이번 발의 부호 있는 킥 값(도)을 계산합니다. 왼쪽을 음수로 둡니다.
    /// </summary>
    /// <param name="pattern">적용할 좌우 킥 패턴입니다.</param>
    /// <param name="magnitude">킥 크기(도)입니다. 0 이하이면 0을 반환합니다.</param>
    /// <param name="shotIndex">현재 발 인덱스입니다. 번갈이 패턴의 짝/홀 판정에 씁니다.</param>
    /// <returns>Random이면 ±범위 무작위, Alternate이면 발 인덱스로 좌우 교대한 부호 있는 크기입니다.</returns>
    /// <remarks>Yaw 반동과 시각 롤이 같은 발 인덱스를 공유하되 각자 자기 패턴으로 독립 계산됩니다.</remarks>
    private static float ResolveKickValue(KickSidePattern pattern, float magnitude, int shotIndex)
    {
        if (magnitude <= 0.0f)
        {
            return 0.0f;
        }

        if (pattern == KickSidePattern.Random)
        {
            return Random.Range(-magnitude, magnitude);
        }

        // 번갈이: 발 인덱스 짝/홀로 좌우 교대. 왼쪽 = 음수.
        bool even = (shotIndex % 2) == 0;
        bool leftFirst = pattern == KickSidePattern.AlternateLeftFirst;
        float sign = even == leftFirst ? -1.0f : 1.0f;
        return magnitude * sign;
    }

    /// <summary>
    /// 조준 카메라, 조준 UI를 갱신하고 이동 컨트롤러에 전투 자세 여부를 통지합니다.
    /// </summary>
    /// <param name="isAiming">전투 자세(조준/힙파이어)이면 true입니다.</param>
    private void SetAimState(bool isAiming)
    {
        bool showAimImage = isAiming || m_showAimImageAlways;

        if (m_aimCamera != null)
        {
            m_aimCamera.gameObject.SetActive(isAiming);
        }

        if (m_aimImage != null)
        {
            m_aimImage.SetActive(showAimImage);
        }

        if (m_crosshairController != null)
        {
            m_crosshairController.SetVisible(showAimImage);
        }

        if (!isAiming)
        {
            ResetCrosshair();
        }

        if (m_controller != null)
        {
            m_controller.SetCombatStance(isAiming);
        }
    }

    /// <summary>
    /// 재장전 완료 애니메이션 이벤트에서 호출합니다.
    /// </summary>
    public void Reload()
    {
        if (!m_hasRequiredReferences)
        {
            return;
        }

        FinishReloadVisualState(true);
        PlayWeaponSound(GetReloadSound(2));
    }

    /// <summary>
    /// 무기 쪽 재장전이 아직 진행 중인지 여부입니다.
    /// </summary>
    /// <remarks>
    /// 조작 컨트롤러의 <c>IsReload</c>만으로는 부족합니다. 그 값은 재장전 비주얼이 걸려 있다는 표시이고,
    /// 실제로 탄약이 채워지는 시점을 쥔 것은 무기 쪽 타이머입니다. 둘이 모두 서 있을 때만 "재장전 중"입니다.
    /// </remarks>
    private bool IsReloadInProgress => m_controller != null
                                    && m_controller.IsReload
                                    && m_weaponController != null
                                    && m_weaponController.IsReloading;

    /// <summary>
    /// 재장전 비주얼 래치를 무기 쪽 진행 상태에 맞춥니다.
    /// </summary>
    /// <remarks>
    /// 재장전이 어디까지 갔는지를 실제로 쥐고 있는 것은 <see cref="Gun"/>의 타이머 하나뿐입니다. 그쪽은
    /// 조작권이 오가거나 컴포넌트가 꺼져도 계속 돌아 반드시 끝납니다. 반면 <c>IsReload</c>는 그 진행도를
    /// 따로 복제한 것이 아니라 "재장전 비주얼이 걸려 있다"는 표시일 뿐인데, 이 값을 내리는 경로가
    /// 재장전 클립의 애니메이션 이벤트 하나뿐이었습니다.
    ///
    /// 문제는 이 값이 조준·사격 처리 전체를 막는 자리에도 쓰인다는 점입니다. 그래서 애니메이션 쪽 사정으로
    /// 이벤트를 한 번 놓치면(컴포넌트 비활성, 해당 레이어 weight 0) 탄약은 채워졌는데 사격만 영영 막히는,
    /// 복구 수단이 없는 상태가 됐습니다. 리그 weight 같은 표시용 값이 입력 가능 여부를 잠그면 안 됩니다.
    ///
    /// 그래서 주인을 무기 쪽으로 두고 래치가 그보다 오래 살아남지 못하게 매 프레임 맞춥니다. 이 경로가
    /// 있으면 이벤트는 더 이상 사격 재개의 유일한 조건이 아니며, 놓치더라도 다음 프레임에 풀립니다.
    /// </remarks>
    private void ReconcileReloadState()
    {
        if (m_controller == null || !m_controller.IsReload)
        {
            return;
        }

        if (m_weaponController == null || m_weaponController.IsReloading)
        {
            return;
        }

        FinishReloadVisualState(false);
    }

    /// <summary>
    /// 진행 중인 재장전의 상체 레이어와 리그 weight를 다시 세웁니다.
    /// </summary>
    /// <remarks>
    /// 값은 <see cref="BeginReload"/>가 세우는 것과 같습니다. 허리는 계속 조준 방향을 보고, 손만 풀어
    /// 탄창을 다루게 합니다. 손까지 총 그립에 묶으면 탄창 교체 동작이 그립에 붙어 깨집니다.
    ///
    /// 재장전이 시작된 뒤 조작권이 오가면 그때마다 전투 자세를 적용하는 경로들이 이 값을 덮어씁니다.
    /// 상체 레이어가 0으로 내려가면 남은 재장전 모션이 보이지 않을 뿐 아니라, 그 레이어의 애니메이션
    /// 이벤트도 발생하지 않아 <c>IsReload</c>를 내릴 정규 경로까지 끊깁니다.
    /// </remarks>
    private void ApplyReloadVisualState()
    {
        m_inCombatStance = false;
        SetRigWeights(1.0f, 0.0f);
        SetWeaponLayerWeight(1.0f);
        m_recoilLayerTarget = 0.0f;

        if (m_animator != null)
        {
            m_animator.SetBool(AnimIDShoot, false);
        }
    }

    /// <summary>
    /// 재장전 완료 후 조작 컨트롤러와 조준 보정 상태를 정리합니다.
    /// </summary>
    /// <param name="completeWeaponReload">무기 탄약도 완료 처리할지 여부입니다.</param>
    private void FinishReloadVisualState(bool completeWeaponReload)
    {
        m_controller.SetReload(false);

        // 애니메이터가 재장전 스테이트에서 나오는 조건입니다. 내리지 않으면 자세가 남습니다.
        if (m_animator != null)
        {
            m_animator.SetBool(AnimIDIsReload, false);
        }
        m_inCombatStance = false;
        m_isAds = false;
        m_hipfireTimer = 0.0f;
        SetAimState(false);
        HideHitscanBlockMarker();
        // 재장전이 끝나면 조준 허리만 풀고, 손은 다시 총기 그립에 고정합니다.
        SetRigWeights(0.0f, 1.0f);
        SetWeaponLayerWeight(0.0f);
        m_animator.SetBool(AnimIDShoot, false);

        if (completeWeaponReload && m_weaponController != null)
        {
            m_weaponController.CompleteReload();
        }
    }

    /// <summary>
    /// 탄창 제거 애니메이션 이벤트에서 호출합니다.
    /// </summary>
    public void ReloadWeaponClip()
    {
        if (m_weaponController != null)
        {
            m_weaponController.OnReloadMagOut();
        }

        PlayWeaponSound(GetReloadSound(0));
    }

    /// <summary>
    /// 탄창 삽입 애니메이션 이벤트에서 호출합니다.
    /// </summary>
    public void ReloadInsertClip()
    {
        PlayWeaponSound(GetReloadSound(1));
    }

    /// <summary>
    /// 스쿼드 멤버 전환 직후 얕은 조준/사격 입력 상태를 현재 멤버에 반영합니다.
    /// </summary>
    /// <param name="isAiming">조준 입력을 유지할지 여부입니다.</param>
    /// <param name="isShooting">사격 입력을 유지할지 여부입니다.</param>
    public void ApplySwitchCarryoverState(bool isAiming, bool isShooting)
    {
        if (!m_hasRequiredReferences)
        {
            return;
        }

        // 재장전 도중에 조작권을 돌려받은 경우입니다. 넘겨받은 전투 자세를 그대로 적용하면 상체 레이어가
        // 0으로 내려가 남은 재장전 모션이 통째로 사라집니다. 진행 중인 재장전이 전투 자세보다 우선입니다.
        if (IsReloadInProgress)
        {
            ApplyReloadVisualState();
            return;
        }

        // ADS(isAiming)이거나 힙파이어(사격 중)면 전투 자세를 유지한 채 전환합니다.
        bool inCombat = isAiming || isShooting;

        m_isAds = isAiming;
        // 힙파이어 carryover면 잔류 타이머를 부여해, 전환 직후 사격을 멈춰도 백뷰가 곧장 풀리지 않습니다.
        m_hipfireTimer = inCombat && !isAiming ? m_hipfireHoldDuration : 0.0f;

        // 상체 레이어는 사격 중일 때만 올립니다. 조준 자세 자체는 Base Layer가 자세별로 갖고 있습니다.
        ApplyCombatStanceState(inCombat, isShooting, 0.0f);

        if (inCombat)
        {
            ApplyCombatZoom(true);
            UpdateCrosshair(true);
        }
    }

    /// <summary>
    /// 외부 상태 전환에 의해 조준을 강제로 해제합니다.
    /// </summary>
    public void ForceStopAim()
    {
        bool keepReloadAnimation = m_controller != null
                                && m_controller.IsReload
                                && m_weaponController != null
                                && m_weaponController.IsReloading;

        ForceStopAim(keepReloadAnimation, releaseHandRig: false);
    }

    /// <summary>
    /// 다운/사망 등으로 전투 비주얼(조준·손 IK 리그, 무기 상체 레이어)을 조건 없이 완전히 해제합니다.
    /// </summary>
    /// <remarks>
    /// 재장전 여부와 무관하게 무기 레이어 weight까지 0으로 내려, 다운/사망 모션이 상체 IK나 무기 레이어에
    /// 의해 깨지지 않도록 합니다.
    /// </remarks>
    public void ReleaseCombatVisuals()
    {
        ForceStopAim(false, releaseHandRig: true);
    }

    /// <summary>
    /// 전투 애니메이터 파라미터를 기본값으로 되돌립니다.
    /// </summary>
    /// <remarks>
    /// <see cref="SquadMemberController.ResetAnimatorToBase"/>가 부르는 탈출구의 전투 담당 몫입니다.
    /// <see cref="ReleaseCombatVisuals"/>가 조준·리그·무기 레이어와 <c>IsShoot</c>까지 내리므로 여기서는
    /// 재장전 쪽만 더 정리합니다. <c>DoReload</c>는 트리거라 소비되지 않은 채 남아 있으면 리셋 직후
    /// 재장전 모션이 한 번 튀어나오므로 함께 지웁니다.
    /// </remarks>
    public void ResetCombatAnimation()
    {
        ReleaseCombatVisuals();

        if (m_animator == null)
        {
            return;
        }

        m_animator.SetBool(AnimIDIsReload, false);
        m_animator.ResetTrigger(AnimIDReload);
    }

    /// <summary>
    /// 조준을 강제로 해제합니다.
    /// </summary>
    /// <param name="keepReloadAnimation">true이면 재장전 상체 애니메이션과 Aim Rig를 유지합니다.</param>
    /// <param name="releaseHandRig">true이면 다운/사망 전신 모션을 위해 Hand Rig도 해제합니다.</param>
    private void ForceStopAim(bool keepReloadAnimation, bool releaseHandRig)
    {
        m_inCombatStance = false;
        m_isAds = false;
        m_hipfireTimer = 0.0f;
        SetAimState(false);
        HideHitscanBlockMarker();
        // 일반적인 조준 해제는 Idle 총기 그립을 유지합니다. 재장전 또는 다운/사망처럼
        // 손 애니메이션을 온전히 써야 하는 상태에서만 Hand Rig를 함께 내립니다.
        float aimWeight = keepReloadAnimation ? 1.0f : 0.0f;
        float handWeight = keepReloadAnimation || releaseHandRig ? 0.0f : 1.0f;
        SetRigWeights(aimWeight, handWeight);
        SetWeaponLayerWeight(keepReloadAnimation ? 1.0f : 0.0f);

        // 사격이 끝난 경로이므로 반동은 항상 내립니다. 남으면 다운·사망 모션 위에 반동이 더해집니다.
        m_recoilLayerTarget = 0.0f;

        if (m_animator != null)
        {
            m_animator.SetBool(AnimIDShoot, false);
        }

        // 다운·사망 경로라 보간하지 않습니다. 상체 레이어나 IK가 조금이라도 남으면 전신 모션이 깨집니다.
        SnapStanceWeights();
    }

    /// <summary>
    /// 전투 자세 진입/해제 전환에서 한 번만 적용할 카메라, UI, 이동, 리그, 애니메이션 상태를 모읍니다.
    /// </summary>
    /// <param name="active">전환 후 전투 자세(백뷰) 활성 상태입니다.</param>
    /// <param name="keepShooting">전환 직후 사격 애니메이션을 유지할지 여부입니다.</param>
    /// <param name="weaponLayerWeight">무기 레이어에 적용할 weight입니다.</param>
    private void ApplyCombatStanceState(bool active, bool keepShooting, float weaponLayerWeight)
    {
        m_inCombatStance = active;
        SetAimState(active);
        // 비전투 상태에서도 손은 총기 그립에 고정하고, Aim Rig만 전투 자세에 따라 전환합니다.
        SetRigWeights(active ? 1.0f : 0.0f, 1.0f);
        SetWeaponLayerWeight(weaponLayerWeight);

        // 반동은 사격을 이어받을 때만 남깁니다. 전투 자세를 나가면 반드시 0입니다.
        m_recoilLayerTarget = active && keepShooting ? m_recoilAnimationWeight : 0.0f;

        if (m_animator != null)
        {
            m_animator.SetBool(AnimIDShoot, active && keepShooting);
        }

        if (!active)
        {
            HideHitscanBlockMarker();
        }
    }

    /// <summary>
    /// 허리 조준 리그와 손 IK 리그의 목표 weight를 따로 설정합니다.
    /// </summary>
    /// <param name="aimWeight">허리(상체 조준) 리그 목표 weight입니다.</param>
    /// <param name="handWeight">손 IK 리그 목표 weight입니다.</param>
    /// <remarks>
    /// 재장전(Aim 1/Hand 0)이나 평상시(Aim 0/Hand 1)처럼 둘의 목표가 다른 구간에 씁니다.
    /// </remarks>
    private void SetRigWeights(float aimWeight, float handWeight)
    {
        m_rigWeightTarget = aimWeight;
        m_handRigWeightTarget = handWeight;
    }

    /// <summary>
    /// 상체(무기) 레이어 weight의 목표값을 설정합니다.
    /// </summary>
    /// <param name="weight">목표 레이어 weight입니다.</param>
    private void SetWeaponLayerWeight(float weight)
    {
        m_weaponLayerTarget = weight;
    }

    /// <summary>
    /// 현재 사격·재장전 여부만 보고 상체 레이어 weight 목표를 다시 계산합니다.
    /// </summary>
    /// <remarks>
    /// <b>조준은 여기에 포함하지 않습니다.</b> 자세별 조준 포즈(서기/웅크림 × 방향 × 정지/이동)는
    /// Base Layer의 <c>Aim Rifle *</c> 트리가 이미 전부 갖고 있습니다. 조준만으로 상체 레이어를 올리면
    /// 그 위에 서 있는 조준 클립 하나를 덮어씌우게 되어, 웅크린 채 조준해도 서 있는 자세로 보였습니다.
    ///
    /// 상체 레이어가 유일하게 보태는 것은 Base Layer에 없는 사격 반동과 재장전 동작입니다.
    /// 그래서 그 둘일 때만 올립니다.
    ///
    /// 사격 반동은 <see cref="RecoilLayerIndex"/>의 Additive 레이어가 담당합니다. 그쪽은 지금 자세 위에
    /// 차이만 더하므로 웅크림이나 보행이 유지됩니다. 그래서 여기(Override)는 재장전만 봅니다.
    /// </remarks>
    private void RefreshWeaponLayerWeight()
    {
        bool shooting = m_inCombatStance && m_input != null && m_input.Shoot;
        bool reloading = m_weaponController != null && m_weaponController.IsReloading;

        // Override 레이어: 상체를 통째로 교체해야 하는 재장전에만 씁니다.
        SetWeaponLayerWeight(reloading ? 1.0f : 0.0f);

        // Additive 레이어: 사격 중에만 반동을 얹습니다.
        // 재장전 중에는 상체가 이미 교체되어 있으므로 반동을 더하지 않습니다.
        m_recoilLayerTarget = shooting && !reloading ? m_recoilAnimationWeight : 0.0f;
    }

    /// <summary>
    /// IK 리그와 상체 레이어 weight를 목표값을 향해 옮기고 적용합니다.
    /// </summary>
    /// <remarks>
    /// 두 값을 따로 두는 이유는 재장전 중에 서로 반대가 되기 때문입니다. 그때 상체 레이어는 1이어야
    /// 재장전 동작이 보이고, IK 리그는 0이어야 손이 조준 위치에 붙지 않고 탄창을 다룹니다.
    /// </remarks>
    private void UpdateStanceWeights()
    {
        RefreshAirMotionState();

        float stanceBlendDuration = StanceBlendDuration;
        float step = stanceBlendDuration <= 0.0f
            ? 1.0f
            : Time.deltaTime / stanceBlendDuration;

        // 공중 처리는 목표값만 바꿉니다. 현재값을 직접 건드리면 뜨고 내리는 순간 자세가 툭 끊깁니다.
        float aimTarget = ResolveAirborneAdjustedRigTarget(m_rigWeightTarget);
        float handTarget = ResolveAirborneAdjustedRigTarget(m_handRigWeightTarget);

        // 수류탄 전용 전신 모션 위에 총기 조준/손 IK가 다시 적용되면 손이 총기 그립으로 끌려간다.
        // ThrowMode 동안에는 두 리그를 함께 내려 Animator의 수류탄 포즈를 그대로 사용한다.
        if (m_input != null && m_input.ThrowMode)
        {
            aimTarget = 0.0f;
            handTarget = 0.0f;
        }

        m_rigWeight = Mathf.MoveTowards(m_rigWeight, aimTarget, step);
        m_handRigWeight = Mathf.MoveTowards(m_handRigWeight, handTarget, step);
        m_weaponLayerWeight = Mathf.MoveTowards(m_weaponLayerWeight, m_weaponLayerTarget, step);

        // 공중 상체 레이어만 오를 때와 내릴 때의 시간을 다르게 씁니다. 이유는 아래 함수 주석에 적어 두었습니다.
        float airTarget = ResolveAirActionLayerTarget();
        m_airActionLayerWeight = Mathf.MoveTowards(
            m_airActionLayerWeight,
            airTarget,
            ResolveAirActionLayerStep(airTarget, step));

        // 반동은 자세 전환보다 빨라야 첫 발이 밋밋하지 않습니다. 그래서 자세 블렌드 시간을 쓰지 않고 즉시 올립니다.
        m_recoilLayerWeight = m_recoilLayerTarget;

        ApplyStanceWeights();
    }

    /// <summary>
    /// 공중 상체 레이어 weight가 이번 프레임에 움직일 양을 돌려줍니다.
    /// </summary>
    /// <param name="target">이번 프레임의 목표 weight입니다.</param>
    /// <param name="stanceStep">자세 전환 시간으로 계산한 기본 이동량입니다.</param>
    /// <remarks>
    /// 오를 때와 내릴 때 쓰는 시간이 다릅니다.
    ///
    /// <b>오를 때</b>는 애니메이터를 따라가야 합니다. 점프하면 Base Layer가 지상 조준 블렌드에서
    /// <c>JumpStart</c>로 0.07초에 걸쳐 넘어갑니다. 그 시간이 지나면 상체는 이미 점프 자세인데,
    /// 자세 전환 시간(0.15초)으로 올리면 그 시점에 레이어가 절반도 못 올라옵니다. 실측으로 0.46이었고,
    /// 그 차이만큼 조준이 한 번 풀렸다 돌아옵니다. 그래서 <see cref="m_airActionLayerRiseDuration"/>을
    /// 따로 두고 애니메이터 전이보다 짧게 잡습니다.
    ///
    /// <b>내릴 때</b>는 그럴 필요가 없습니다. 착지는 <c>JumpLand</c>가 끝나고 Base Layer가 지상 조준
    /// 클립에 도착한 뒤에야 목표가 0이 되므로 서두를 이유가 없고, 공중에서 조준을 푸는 경우에는
    /// 오히려 자세 전환 시간으로 천천히 내려가는 편이 자연스럽습니다.
    /// </remarks>
    private float ResolveAirActionLayerStep(float target, float stanceStep)
    {
        if (target <= m_airActionLayerWeight)
        {
            return stanceStep;
        }

        return m_airActionLayerRiseDuration <= 0.0f
            ? 1.0f
            : Time.deltaTime / m_airActionLayerRiseDuration;
    }

    /// <summary>이 대원이 지금 공중에 떠 있는지 여부입니다.</summary>
    private bool IsAirborne => m_controller != null && !m_controller.Grounded;

    /// <summary>
    /// 이번 프레임의 <see cref="m_airMotionActive"/>를 다시 구합니다.
    /// </summary>
    /// <remarks>
    /// 접지 판정과 Base Layer의 점프 모션 재생 중 하나라도 해당하면 공중 구간으로 봅니다.
    /// 둘을 함께 보는 이유는 <see cref="JumpMotionStateHashes"/> 주석에 적어 두었습니다.
    /// </remarks>
    private void RefreshAirMotionState()
    {
        m_airMotionActive = IsAirborne || IsPlayingJumpMotion();
    }

    /// <summary>Base Layer가 점프·낙하·착지 클립을 재생 중인지 여부입니다.</summary>
    /// <remarks>
    /// 전이 중이면 다음 상태도 함께 봅니다. 이륙할 때 지상 이동 블렌드에서 <c>JumpStart</c>로 넘어가는
    /// 구간이 여기에 해당하고, 착지할 때 <c>JumpLand</c>에서 지상 이동 블렌드로 돌아가는 구간은
    /// 현재 상태 쪽이 <c>JumpLand</c>라 전이가 끝날 때까지 참으로 남습니다. 그래서 상체 레이어가
    /// Base Layer의 조준 클립이 다 올라온 뒤에 내려가고, 중간에 비는 구간이 없습니다.
    /// </remarks>
    private bool IsPlayingJumpMotion()
    {
        if (m_animator == null || m_animator.layerCount == 0)
        {
            return false;
        }

        if (IsJumpMotionState(m_animator.GetCurrentAnimatorStateInfo(0).fullPathHash))
        {
            return true;
        }

        return m_animator.IsInTransition(0)
            && IsJumpMotionState(m_animator.GetNextAnimatorStateInfo(0).fullPathHash);
    }

    /// <summary>주어진 Base Layer 상태가 점프 계열인지 여부입니다.</summary>
    /// <param name="fullPathHash">검사할 상태의 전체 경로 해시입니다.</param>
    private static bool IsJumpMotionState(int fullPathHash)
    {
        for (int i = 0; i < JumpMotionStateHashes.Length; i++)
        {
            if (JumpMotionStateHashes[i] == fullPathHash)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 공중 설정을 반영한 리그 weight 목표값을 돌려줍니다.
    /// </summary>
    /// <param name="target">지상 기준으로 정해진 원래 목표 weight입니다.</param>
    /// <remarks>
    /// <see cref="m_enableCombatRigInAir"/>가 켜져 있으면 원래 목표를 그대로 씁니다. 즉 공중에서도 조준·사격 시
    /// 지상과 똑같이 1까지 올라갑니다. 실측상 이것이 현재 동작이기도 해서, 기본값을 켜 두면 지금과 달라지는 것이
    /// 없습니다. 이 함수는 동작을 바꾸려고 넣은 것이 아니라 <b>기획이 바뀌면 끌 수 있는 자리</b>를 만들려고 둔 것입니다.
    ///
    /// 꺼져 있으면 공중에 있는 동안만 목표를 0으로 내립니다. 현재값이 아니라 목표만 바꾸므로 뜨고 내리는 전환도
    /// <see cref="m_stanceBlendDuration"/>으로 보간되고, 착지하면 원래 목표로 다시 올라갑니다.
    /// </remarks>
    private float ResolveAirborneAdjustedRigTarget(float target)
    {
        if (m_enableCombatRigInAir || !m_airMotionActive)
        {
            return target;
        }

        return 0.0f;
    }

    /// <summary>
    /// 공중 조준 레이어(<see cref="AirActionLayerIndex"/>)의 목표 weight를 돌려줍니다.
    /// </summary>
    /// <remarks>
    /// 공중이면서 전투 자세일 때만 1입니다. 지상에서는 Base Layer의 전신 조준 클립이 그 역할을 하므로 0입니다.
    ///
    /// 재장전 중에는 올리지 않습니다. 재장전은 Action 레이어가 팔을 맡는데, 그 위에 이 레이어가 몸통까지
    /// 조준 자세로 덮으면 탄창을 다루는 동작이 어그러집니다.
    ///
    /// <see cref="m_enableCombatRigInAir"/>와는 별개 스위치입니다. 저쪽은 리그(손 IK·허리) weight를,
    /// 이쪽은 애니메이터 레이어를 다룹니다. 공중 전투 연출을 통째로 끄려면 둘 다 꺼야 합니다.
    /// </remarks>
    private float ResolveAirActionLayerTarget()
    {
        if (!m_useWeaponLayerAimPoseInAir || !m_airMotionActive || !m_inCombatStance || IsReloadInProgress)
        {
            return 0.0f;
        }

        return 1.0f;
    }

    /// <summary>
    /// 목표값을 기다리지 않고 지금 즉시 적용합니다.
    /// </summary>
    /// <remarks>
    /// 다운과 사망에 씁니다. 그 모션은 전신을 쓰므로 상체 레이어나 IK가 조금이라도 남아 있으면 자세가 깨집니다.
    /// </remarks>
    private void SnapStanceWeights()
    {
        RefreshAirMotionState();

        m_rigWeight = m_rigWeightTarget;
        m_handRigWeight = m_handRigWeightTarget;
        m_weaponLayerWeight = m_weaponLayerTarget;
        m_recoilLayerWeight = m_recoilLayerTarget;
        m_airActionLayerWeight = ResolveAirActionLayerTarget();

        ApplyStanceWeights();
    }

    /// <summary>
    /// AI 조작 캐릭터의 전투 자세를 적용합니다. 컴포넌트가 꺼져 있어도 외부에서 부를 수 있습니다.
    /// </summary>
    /// <param name="inCombat">전투 자세(조준)를 잡을지 여부입니다.</param>
    /// <param name="shooting">지금 사격 중인지 여부입니다. 반동 레이어와 사격 애니메이션에 씁니다.</param>
    /// <remarks>
    /// <b>이 컴포넌트를 AI에서 켜지 않는 이유</b>: <see cref="Update"/> 경로는 <c>m_input</c>(사람 입력)에
    /// 의존하고 조준 카메라·크로스헤어·FOV를 함께 건드립니다. AI 멤버에서 그대로 돌리면 플레이어의
    /// 화면과 카메라를 빼앗습니다. 그래서 컴포넌트는 꺼 둔 채 이 진입점만 매 프레임 부르게 했습니다.
    /// <para>
    /// <b><see cref="SetAimState"/>를 부르지 않습니다.</b> 그 함수가 조준 카메라·조준 이미지·크로스헤어를
    /// 켜는데, 그것들은 플레이어 한 명의 화면에 속한 자원입니다. 여기서는 몸에 붙은 것(상체 조준 리그,
    /// 손 IK, 무기 레이어, 반동 레이어)만 다룹니다.
    /// </para>
    /// <para>
    /// 컴포넌트가 꺼져 있어 <see cref="Update"/>의 보간이 돌지 않으므로 여기서 직접 보간합니다.
    /// 매 프레임 부르지 않으면 자세가 중간값에서 멈춥니다.
    /// </para>
    /// <para>
    /// Animator의 <c>IsAim</c>은 여기서 건드리지 않습니다. 그 파라미터는
    /// <see cref="ThirdPersonController"/>가 소유하며, AI 멤버에서는 그 컴포넌트도 꺼져 있으므로
    /// <see cref="SquadAIController"/>가 다른 이동 파라미터와 함께 직접 채웁니다.
    /// </para>
    /// </remarks>
    /// <summary>
    /// 이 대원을 플레이어가 직접 조작하는지 설정합니다.
    /// </summary>
    /// <param name="value">직접 조작 중이면 true입니다.</param>
    /// <remarks>
    /// <see cref="SquadMemberController"/>가 조작권을 옮길 때 부릅니다. 컴포넌트를 끄는 대신 이 값만
    /// 내려야 합니다. 끄면 <see cref="Update"/>가 멈춰 리그 weight 보간이 그 자리에 얼어붙고,
    /// 애니메이션 이벤트도 배달되지 않습니다.
    /// </remarks>
    public void SetPlayerControlled(bool value)
    {
        if (m_isPlayerControlled && !value)
        {
            // 투척 프리셋은 공용 HUD 전체 값을 임시로 덮습니다. 다음 대원의 프로필을 적용하기 전에
            // 반드시 현재 대원이 복원해야 LateUpdate 순서와 무관하게 이전 값이 남지 않습니다.
            ExplosiveProjectileShooter projectileShooter = GetComponent<ExplosiveProjectileShooter>();
            if (projectileShooter != null)
            {
                projectileShooter.ReleaseCrosshairOverrideIfOwned();
            }
        }

        m_isPlayerControlled = value;

        if (value)
        {
            CacheOptionalCrosshairController();
            ApplyCrosshairPresentationIfNeeded();
        }
    }

    /// <summary>
    /// AI가 모는 대원의 상체 조준 목표 지점을 갱신합니다.
    /// </summary>
    /// <param name="point">이 대원이 겨눌 월드 지점입니다.</param>
    /// <remarks>
    /// 조작 멤버는 <see cref="UpdateCombat"/>가 카메라 트레이스 결과로 이 지점을 씁니다. AI 대원은 그 경로를
    /// 타지 않으므로 <see cref="SquadAIController"/>가 자기 조준점을 직접 넘깁니다.
    ///
    /// 대원마다 조준 목표가 따로 있어야 하는 이유는 상체 조준 IK가 이 지점을 바라보기 때문입니다.
    /// 하나를 공유하면 봇 상체가 플레이어 조준을 따라가고, 반대로 AI가 지점을 옮기면 플레이어 상체까지
    /// 같이 꺾입니다.
    /// </remarks>
    public void ApplyAiLookPoint(Vector3 point)
    {
        m_currentAimPoint = point;

        // AI 경로는 카메라 트레이스를 타지 않아 조준 대상을 모릅니다. 조작 멤버였을 때의 값이 남아 있으면
        // 엉뚱한 적을 "겨눈 대상"으로 보고 차단 판정이 어긋나므로 비웁니다.
        m_currentAimTargetHealth = null;
        ApplyLookTarget(point);
    }

    /// <summary>
    /// 콜라이더가 속한 체력 소유자를 찾습니다. 없으면 <c>null</c>입니다.
    /// </summary>
    /// <remarks>
    /// 부위 히트박스는 자식 콜라이더라 자기 자신에는 체력이 없습니다. 부모로 올라가야 몸통과 같은 하나로 모입니다.
    /// </remarks>
    private static HealthSystemBase ResolveHealthOwner(Collider collider)
    {
        return collider != null ? collider.GetComponentInParent<HealthSystemBase>() : null;
    }

    /// <summary>지금 플레이어가 직접 조작 중인지 여부입니다.</summary>
    public bool IsPlayerControlled => m_isPlayerControlled;

    public void ApplyAiCombatStance(bool inCombat, bool shooting)
    {
        if (m_animator == null)
        {
            m_animator = GetComponent<Animator>();
        }

        // AI가 몰고 있는 동안에도 래치가 무기 상태보다 오래 남지 않게 합니다. 이 컴포넌트는 꺼져 있어
        // Update가 돌지 않으므로, 매 프레임 들어오는 이 경로가 조작 멤버의 Update 자리를 대신합니다.
        ReconcileReloadState();

        // 재장전 중에는 재장전 비주얼이 전투 자세보다 우선입니다. 재장전 모션은 상체 레이어에 있는데
        // 이 함수는 AI가 매 프레임 부르므로, 거르지 않으면 전환 직후부터 상체 레이어를 0으로 눌러
        // 재장전 모션이 통째로 보이지 않습니다. 조작 멤버 쪽에서 같은 이유로
        // <see cref="ForceStopAim(bool, bool)"/>가 재장전 중에만 상체 레이어를 유지하는 것과 같은 처리입니다.
        //
        // 리그는 <see cref="BeginReload"/>와 같은 조합을 씁니다. 허리는 계속 조준 방향을 보고, 손만
        // 풀어 탄창을 다루게 합니다. 손까지 총 그립에 붙여 두면 탄창 교체 동작이 그립에 묶여 깨집니다.
        if (IsReloadInProgress)
        {
            ApplyReloadVisualState();
            return;
        }

        m_inCombatStance = inCombat;

        // 애니메이션에 관한 한 봇은 조작 멤버와 같아야 합니다. 그래서 아래 두 줄은
        // <see cref="ApplyCombatStanceState"/>가 조작 멤버에 적용하는 것과 같은 값을 씁니다.
        // 다른 것은 조준 카메라·조준선처럼 스쿼드가 공유하는 UI뿐이고, 그쪽은 여기서 건드리지 않습니다.
        //
        // 상체 조준 리그(m_aimRig)를 함께 올려도 되는 것은 지금 그 리그의 Spine IK 제약 weight가 0이기
        // 때문입니다. 제약을 다시 켜려면 먼저 LookTarget을 멤버별로 나눠야 합니다. 지금은 씬에 하나뿐인
        // 오브젝트를 셋이 공유해서, 켜는 순간 봇 상체가 플레이어 마우스를 따라 꺾이고 반대로 AI가 타겟을
        // 옮기면 플레이어 상체까지 같이 꺾입니다.
        // AI도 비전투 상태에서 총기 그립은 유지하고, 상체 조준만 전투 여부에 따라 전환합니다.
        SetRigWeights(inCombat ? 1.0f : 0.0f, 1.0f);

        // 상체 레이어는 조준만으로 올리지 않습니다. Base Layer의 조준 트리가 이미 자세를 갖고 있어
        // 여기서 덮으면 웅크린 채 조준해도 서 있는 자세로 바뀝니다. 봇만 사격 중에 이 레이어를 올리던
        // 것이 사격할 때 총 IK와 고개가 틀어져 보이던 원인입니다.
        SetWeaponLayerWeight(0.0f);

        m_recoilLayerTarget = inCombat && shooting ? m_recoilAnimationWeight : 0.0f;

        if (m_animator != null)
        {
            m_animator.SetBool(AnimIDShoot, inCombat && shooting);
        }
    }

    /// <summary>지금 값을 리그와 애니메이터 레이어에 반영합니다.</summary>
    private void ApplyStanceWeights()
    {
        if (m_aimRig != null)
        {
            m_aimRig.weight = m_rigWeight;
        }

        if (m_handRig != null)
        {
            m_handRig.weight = m_handRigWeight;
        }

        if (m_animator != null)
        {
            m_animator.SetLayerWeight(WeaponLayerIndex, m_weaponLayerWeight);

            // 레이어가 없는 애니메이터(다른 캐릭터 컨트롤러)에서도 안전하도록 개수를 확인합니다.
            if (m_animator.layerCount > RecoilLayerIndex)
            {
                m_animator.SetLayerWeight(RecoilLayerIndex, m_recoilLayerWeight);
            }

            if (m_animator.layerCount > AirActionLayerIndex)
            {
                m_animator.SetLayerWeight(AirActionLayerIndex, m_airActionLayerWeight);
            }
        }
    }

    /// <summary>
    /// 재장전 사운드 배열에서 지정한 인덱스의 클립을 가져옵니다.
    /// </summary>
    /// <param name="index">가져올 재장전 사운드 인덱스입니다.</param>
    /// <returns>유효한 인덱스이면 해당 AudioClip, 아니면 null입니다.</returns>
    private AudioClip GetReloadSound(int index)
    {
        if (m_reloadSounds == null || index < 0 || index >= m_reloadSounds.Length)
        {
            Debug.LogWarning($"[AimController] ReloadSounds[{index}]가 없습니다.", this);
            return null;
        }

        return m_reloadSounds[index];
    }

    /// <summary>
    /// 무기 사운드를 AudioSource로 재생합니다.
    /// </summary>
    /// <param name="sound">재생할 사운드 클립입니다.</param>
    private void PlayWeaponSound(AudioClip sound)
    {
        if (m_weaponAudioSource == null || sound == null)
        {
            return;
        }

        m_weaponAudioSource.clip = sound;
        m_weaponAudioSource.Play();
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
