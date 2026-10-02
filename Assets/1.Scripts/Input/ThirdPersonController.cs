using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Serialization;
using VInspector;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Starter Assets 기반의 3인칭 캐릭터 이동, 회전, 점프, 중력, 애니메이션 파라미터를 제어하는 컴포넌트입니다.
/// </summary>
/// <remarks>
/// 이 컨트롤러는 <see cref="CharacterController"/>와 <see cref="PlayerInputController"/>를 필수 참조로 사용합니다.
/// 필수 참조는 <c>Awake</c>에서 캐싱하고, 누락 시 컴포넌트를 비활성화하여 런타임 null 참조를 방지합니다.
/// </remarks>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerInputController))]
#if ENABLE_INPUT_SYSTEM
[RequireComponent(typeof(PlayerInput))]
#endif
public class ThirdPersonController : MonoBehaviour, ISharedBalanceReceiver
{
    private const string FootwearFmodParameter = "Footwear";
    private const string SurfaceFmodParameter = "Surface";

    [Tooltip("이 플레이어에 적용할 공용 밸런스 SO입니다. 비어 있으면 Inspector 값을 그대로 씁니다.")]
    [SerializeField] private PlayerCommonBalanceSO m_balanceSO;

    /// <summary>
    /// 멤버 전환 시 이어받을 이동 블렌드 상태입니다.
    /// </summary>
    public struct LocomotionCarryoverState
    {
        /// <summary>이어받을 상태가 실제로 담겨 있는지 여부입니다. <c>false</c>면 나머지 값을 쓰지 않습니다.</summary>
        public bool HasState;

        /// <summary>이어받을 현재 이동 속도(m/s)입니다.</summary>
        public float Speed;

        /// <summary>이어받을 이동 애니메이션 블렌드 값입니다.</summary>
        public float AnimationBlend;

        /// <summary>이어받을 애니메이션 재생 배속입니다.</summary>
        public float MotionSpeed;

        /// <summary>이어받을 목표 회전각(도)입니다.</summary>
        public float TargetRotation;

        /// <summary>이어받을 회전 보간 속도입니다. 전환 직후 회전이 튀지 않게 합니다.</summary>
        public float RotationVelocity;

        /// <summary>이어받을 수직 속도(m/s)입니다. 공중 전환에서 낙하가 초기화되지 않게 합니다.</summary>
        public float VerticalVelocity;

        /// <summary>이어받을 점프 재시도 대기 잔여 시간(초)입니다.</summary>
        public float JumpTimeoutDelta;

        /// <summary>이어받을 낙하 판정 대기 잔여 시간(초)입니다.</summary>
        public float FallTimeoutDelta;

        /// <summary>전환 시점의 접지 여부입니다.</summary>
        public bool Grounded;

        /// <summary>전환 시점의 점프 진행 여부입니다.</summary>
        public bool Jump;

        /// <summary>전환 시점의 자유 낙하 여부입니다.</summary>
        public bool FreeFall;
    }

    /// <summary>
    /// 몸이 어느 방향을 기준으로 도는지를 나타내는 3인칭 시점 모드입니다.
    /// </summary>
    public enum CameraViewMode
    {
        /// <summary>몸이 이동 방향을 바라보는 자유 시점입니다.</summary>
        FreeLook,

        /// <summary>몸이 카메라 정면을 바라보는 백뷰입니다. 옆·뒤 입력은 게걸음/뒷걸음이 됩니다.</summary>
        BackView,
    }

    /// <summary>
    /// 시점 모드를 고르는 기준이 되는 플레이어 상태입니다.
    /// </summary>
    public enum ViewContext
    {
        /// <summary>조준/사격 중이거나 사격 직후 잔류 시간 안입니다.</summary>
        Combat,

        /// <summary>비전투 상태에서 이동 중이거나, 멈춘 지 얼마 되지 않았습니다.</summary>
        NonCombatMove,

        /// <summary>비전투 상태로 충분히 오래 멈춰 있습니다.</summary>
        Idle,
    }


    /// <summary>이동 관련 설정값입니다.</summary>
    [Foldout("Move Options")]
    [Tooltip("캐릭터의 기본 이동 속도입니다. 단위는 m/s입니다.")]
    [FormerlySerializedAs("MoveSpeed")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_moveSpeed = 2.0f;

    [Tooltip("캐릭터의 전력질주 속도입니다. 단위는 m/s입니다.")]
    [FormerlySerializedAs("SprintSpeed")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_sprintSpeed = 5.335f;

    [Tooltip("전력질주를 시작할 수 있는 전방 부채꼴의 반각입니다. 이 각을 벗어난 방향으로는 걷기 속도만 납니다.")]
    [Range(0.0f, 180.0f)]
    [Clamp(Min = 0, Max = 180)]
    [SerializeField] private float m_sprintForwardAngle = 60.0f;

    [Tooltip("전진 입력에 적용할 이동 속도 배율입니다. 1이면 기본 이동 속도와 같습니다.")]
    [Range(0.0f, 2.0f)]
    [SerializeField] private float m_forwardSpeedMultiplier = 1.0f;

    [Tooltip("후진 입력에 적용할 이동 속도 배율입니다. 기본값 0.8은 전진보다 20% 느립니다.")]
    [Range(0.0f, 2.0f)]
    [SerializeField] private float m_backwardSpeedMultiplier = 0.8f;

    [Tooltip("좌·우 입력에 공통 적용할 이동 속도 배율입니다. 1이면 기본 이동 속도와 같습니다.")]
    [Range(0.0f, 2.0f)]
    [SerializeField] private float m_strafeSpeedMultiplier = 1.0f;

    [Tooltip("이미 전력질주 중일 때 이 각도만큼 더 허용합니다. 부채꼴 경계에서 달리기와 걷기가 번갈아 켜지는 것을 막습니다.")]
    [Range(0.0f, 90.0f)]
    [Clamp(Min = 0, Max = 90)]
    [SerializeField] private float m_sprintReleaseMargin = 20.0f;

    [Tooltip("이동 방향(MoveX/MoveZ)을 애니메이터에 넣을 때의 감쇠 시간입니다. 방향이 급히 바뀔 때 발걸음이 튀는 것을 막습니다.")]
    [Clamp(Min = 0)]
    [SerializeField] private float m_moveDirectionDamp = 0.1f;

    [Tooltip("이동 방향이 이 각도(도) 이상 꺾이면 방향 조정이 아니라 방향 교체로 보고 아래의 빠른 전환 시간을 씁니다. 100이면 좌우 반전이나 급선회가 여기에 해당합니다.")]
    [Range(0.0f, 180.0f)]
    [SerializeField] private float m_moveDirectionSnapAngle = 100.0f;

    [Tooltip("방향 교체로 판정됐을 때 180도를 도는 데 걸리는 시간(초)입니다. 짧을수록 좌우 전환이 즉각적이지만 너무 짧으면 툭 끊겨 보이고, 길면 중간 방향 모션이 눈에 띕니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_moveDirectionFlipDuration = 0.06f;

    [Tooltip("정지와 이동 사이를 오가는 데 걸리는 시간(초)입니다. 방향 블렌드 트리의 중심(정지 클립)과 방향 클립 사이를 얼마나 천천히 넘나들지 정합니다. 짧으면 걷기 시작과 멈춤이 뚝 끊겨 보입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_moveStartStopDuration = 0.18f;

    [Tooltip("웅크린 상태의 이동 속도입니다. 단위는 m/s입니다.")]
    [Clamp(Min = 0)]
    [SerializeField] private float m_crouchSpeed = 1.2f;

    [Tooltip("웅크린 상태의 CharacterController 높이입니다. 서기 높이보다 크면 서기 높이로 잘립니다.")]
    [Clamp(Min = 0.1)]
    [SerializeField] private float m_crouchHeight = 1.1f;

    [Tooltip("서기와 웅크리기 사이 높이 전환에 걸리는 시간입니다. 0이면 즉시 바뀝니다. 단위는 초입니다.")]
    [Clamp(Min = 0)]
    [SerializeField] private float m_crouchTransitionDuration = 0.2f;

    [Tooltip("공중에서 웅크리기를 선입력한 뒤 착지할 때 지상 웅크림 블렌드로 넘어가는 시간입니다. 단위는 초입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_crouchLandingTransitionDuration = 0.08f;

    [Tooltip("MoveState 한 단계(웅크림↔걷기, 걷기↔달리기) 전환에 걸리는 시간입니다. 웅크림 높이 전환 시간과 맞추면 자세와 콜라이더가 같이 움직입니다.")]
    [Clamp(Min = 0)]
    [SerializeField] private float m_moveStateBlendDuration = 0.2f;

    [Tooltip("캐릭터가 이동 방향을 바라보도록 회전하는 데 걸리는 보간 시간입니다.")]
    [Range(0.0f, 0.3f)]
    [FormerlySerializedAs("RotationSmoothTime")]
    [BalanceField]
    [Clamp(Min = 0, Max = 0.3)]
    [SerializeField] private float m_rotationSmoothTime = 0.12f;

    [Tooltip("실제 이동 벡터가 새 입력 방향으로 회전하는 최대 각속도입니다. W→WD처럼 입력이 바뀔 때 궤적이 한 프레임에 45° 꺾이지 않게 합니다. 단위는 °/s이며 0이면 즉시 전환합니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_movementDirectionTurnSpeed = 360.0f;

    [Tooltip("일반적인 실제 이동 방향 보간을 적용할 최대 각도 차입니다. WA→WD처럼 서로 이웃한 대각선 입력끼리 바뀌는 경우에는 90°까지 예외적으로 보간합니다. A→D 같은 정반대 전환은 즉시 바뀝니다. 단위는 °입니다.")]
    [Range(0.0f, 180.0f)]
    [SerializeField] private float m_movementDirectionSmoothMaxAngle = 60.0f;

    [Tooltip("가속과 감속 반응 속도입니다.")]
    [FormerlySerializedAs("SpeedChangeRate")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_speedChangeRate = 10.0f;

    [Tooltip("ADS/전투 자세 진입·해제 직후 이동 속도가 목표 속도에 접근하는 반응 속도입니다. 일반 가감속보다 높게 두어 토글감 없이 빠르게 전환합니다.")]
    [Clamp(Min = 0)]
    [SerializeField] private float m_combatSpeedTransitionRate = 16.0f;

    [Tooltip("ADS/전투 자세 속도 전환에 전용 반응 속도를 적용하는 시간(초)입니다.")]
    [Clamp(Min = 0)]
    [SerializeField] private float m_combatSpeedTransitionDuration = 0.18f;



    /// <summary>점프와 중력 관련 설정값입니다.</summary>
    [Foldout("Jump Options")]
    [Tooltip("캐릭터가 점프할 수 있는 높이입니다.")]
    [FormerlySerializedAs("JumpHeight")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_jumpHeight = 1.2f;

    [Tooltip("캐릭터에 적용할 중력 값입니다. Unity 기본 중력은 -9.81입니다.")]
    [FormerlySerializedAs("Gravity")]
    [BalanceField]
    [SerializeField] private float m_gravity = -15.0f;

    [Tooltip("다음 점프가 가능해지기까지 필요한 대기 시간입니다. 0이면 즉시 다시 점프할 수 있습니다.")]
    [FormerlySerializedAs("JumpTimeout")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_jumpTimeout = 0.50f;

    [Tooltip("낙하 상태로 전환되기 전까지의 대기 시간입니다. 계단 이동 같은 작은 단차 처리에 유용합니다.")]
    [FormerlySerializedAs("FallTimeout")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_fallTimeout = 0.15f;



    /// <summary>지면 감지 관련 설정값입니다.</summary>
    [Foldout("Ground Options")]
    [Tooltip("지면 감지 위치의 Y축 오프셋입니다. 울퉁불퉁한 지형에서 보정용으로 사용합니다.")]
    [FormerlySerializedAs("GroundedOffset")]
    [BalanceField]
    [SerializeField] private float m_groundedOffset = -0.14f;

    [Tooltip("지면 감지 구체의 반지름입니다. CharacterController 반지름과 맞추는 것이 좋습니다.")]
    [FormerlySerializedAs("GroundedRadius")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_groundedRadius = 0.28f;

    [Tooltip("지면으로 판정할 레이어 마스크입니다.")]
    [FormerlySerializedAs("GroundLayers")]
    [SerializeField] private LayerMask m_groundLayers;



    /// <summary>카메라 회전 타겟과 상하 회전 제한 설정값입니다.</summary>
    [Foldout("Camera Options")]
    [Tooltip("Cinemachine Virtual Camera가 따라갈 카메라 타겟 오브젝트입니다.")]
    [FormerlySerializedAs("CinemachineCameraTarget")]
    [SerializeField] private GameObject m_cinemachineCameraTarget;

    [Tooltip("카메라를 위로 회전할 수 있는 최대 각도입니다.")]
    [FormerlySerializedAs("TopClamp")]
    [BalanceField]
    [SerializeField] private float m_topClamp = 70.0f;

    [Tooltip("카메라를 아래로 회전할 수 있는 최대 각도입니다.")]
    [FormerlySerializedAs("BottomClamp")]
    [BalanceField]
    [SerializeField] private float m_bottomClamp = -30.0f;

    [Tooltip("카메라 각도에 추가로 적용할 보정 각도입니다. 고정 카메라 튜닝에 사용할 수 있습니다.")]
    [FormerlySerializedAs("CameraAngleOverride")]
    [BalanceField]
    [SerializeField] private float m_cameraAngleOverride = 0.0f;

    [Tooltip("카메라 회전 입력을 잠글지 여부입니다.")]
    [FormerlySerializedAs("LockCameraPosition")]
    [SerializeField] private bool m_lockCameraPosition = false;



    /// <summary>사격 반동(에임을 실제로 밀어 탄착에 영향을 주는 오프셋) 설정값입니다.</summary>
    [Foldout("Recoil Options")]
    [Tooltip("반동 오프셋이 0(원래 조준)으로 복귀하는 속도입니다. 클수록 빠르게 제자리로 돌아옵니다.")]
    [FormerlySerializedAs("m_cameraKickRecoverySpeed")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_recoilRecoverySpeed = 8.0f;

    [Tooltip("발사 입력을 놓은 뒤 반동 회복을 시작하기까지의 유예 시간(초)입니다. 입력을 유지하는 동안에는 발수·누적량과 무관하게 회복하지 않습니다. 0이면 버튼을 놓은 직후부터 복귀합니다.")]
    [FormerlySerializedAs("m_cameraKickRecoveryDelay")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_recoilRecoveryDelay = 0.15f;

    [Tooltip("켜면 발사 순간의 반동 상승을 즉시 계단식이 아니라 보간(앞쪽으로 쏠린 이징 — 빠르게 확 올랐다 정착)으로 넣습니다. 끄면(기본) 기존처럼 즉시 반영합니다.")]
    [SerializeField] private bool m_recoilOnsetInterp = false;

    [Tooltip("반동 온셋 보간 속도입니다. 클수록 더 빠르게(앞쪽으로 더 쏠려) 목표에 도달합니다. Recoil Onset Interp가 켜져 있을 때만 적용됩니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_recoilOnsetSpeed = 35.0f;

    [Tooltip("켜면(기본) 반동 반대 방향으로 넣은 조준 입력이 조준을 움직이기 전에 반동을 먼저 상쇄합니다. 자동회복이 플레이어의 되잡기를 이중으로 걷어가 시점이 과하게 쳐지는(오버 컴펜세이션) 현상을 막습니다. 반동과 같은 방향 입력(의도적 재조준·트래킹)은 그대로 통과합니다.")]
    [SerializeField] private bool m_recoilCompensationAbsorb = true;

    [Tooltip("켜면 세로 반동 회복분을 고정 상한(Recoil Max Pitch)으로 제한합니다. 끄면(기본) 조준 상하 한계까지 쌓여 천장까지 상승 후 회복하며, 그 한계로만 제한됩니다. 대개 꺼두는 걸 권장.")]
    [SerializeField] private bool m_usePitchOffsetCap = false;

    [Tooltip("세로(피치) 반동 회복분 오프셋의 고정 상한 각도(도)입니다. Use Pitch Offset Cap이 켜져 있을 때만 적용됩니다.")]
    [FormerlySerializedAs("m_cameraKickMaxPitch")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_recoilMaxPitch = 4.0f;

    [Tooltip("켜면(기본) 좌우 반동 회복분을 고정 상한(Recoil Max Yaw)으로 제한합니다. yaw는 조준 클램프가 없어(360 자유) 끄면 무제한으로 쌓일 수 있으니 보통 켜둡니다.")]
    [SerializeField] private bool m_useYawOffsetCap = true;

    [Tooltip("좌우(요) 반동 회복분 오프셋의 고정 상한 각도(도)입니다. Use Yaw Offset Cap이 켜져 있을 때만 적용됩니다(회복분에만).")]
    [FormerlySerializedAs("m_cameraKickMaxYaw")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_recoilMaxYaw = 3.0f;

    [Tooltip("세로(pitch) 반동 회복 비율입니다. 1=자동(멈추면 완전 회복), 0=하드(조준에 영구 반영·안 돌아옴 → 상하 조준 한계까지 상승), 중간=부분(일부만 회복). 영구분은 상하 조준 한계로 제한됩니다.")]
    [Range(0.0f, 1.0f)]
    [BalanceField]
    [Clamp(Min = 0, Max = 1)]
    [SerializeField] private float m_pitchRecoveryRatio = 1.0f;

    [Tooltip("좌우(yaw) 반동 회복 비율입니다. 1=자동(완전 회복), 0=하드(영구 반영·안 돌아옴 → 플레이어가 되잡음, Strinova식), 중간=부분. 하드는 Alternate 패턴과 궁합이 좋습니다.")]
    [Range(0.0f, 1.0f)]
    [BalanceField]
    [Clamp(Min = 0, Max = 1)]
    [SerializeField] private float m_yawRecoveryRatio = 1.0f;

    [Tooltip("켜면 세로 반동의 상승과 회복을 곡선 하나로 처리합니다. 발마다 곡선 하나가 시작되고 살아 있는 곡선을 모두 더합니다. 끄면 기존 방식(목표값 누적 + 지연 후 회복)을 씁니다.")]
    [SerializeField] private bool m_usePitchRecoilEnvelope = false;

    [Tooltip("세로 반동 곡선 하나의 길이(초)입니다. 정규화 시간 0~1을 재는 기준이 됩니다. 연사 간격보다 길면 발끼리 겹쳐 누적됩니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_pitchRecoilEnvelopeDuration = 0.32f;

    [Tooltip("세로 반동 곡선입니다. x는 정규화 시간(0~1), y는 반동 세기 배율입니다. y가 가장 큰 x가 피크 위치이고, y를 1보다 크게 두면 목표를 넘어섰다 돌아옵니다.")]
    [SerializeField]
    private AnimationCurve m_pitchRecoilEnvelopeCurve = ImpulseEnvelope.BuildFastAttackConstantReleaseCurve(0.10f, 1.0f, 0.75f);

    [Tooltip("켜면 좌우 반동의 상승과 회복을 곡선 하나로 처리합니다. 상하와 따로 켤 수 있어 한쪽만 바꿔 비교할 수 있습니다.")]
    [SerializeField] private bool m_useYawRecoilEnvelope = false;

    [Tooltip("좌우 반동 곡선 하나의 길이(초)입니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_yawRecoilEnvelopeDuration = 0.30f;

    [Tooltip("좌우 반동 곡선입니다. x는 정규화 시간(0~1), y는 반동 세기 배율입니다.")]
    [SerializeField]
    private AnimationCurve m_yawRecoilEnvelopeCurve = ImpulseEnvelope.BuildFastAttackConstantReleaseCurve(0.12f, 1.0f, 0.85f);

    /// <summary>세로 반동 엔벨로프의 런타임 상태입니다.</summary>
    private readonly ImpulseEnvelope m_pitchRecoilEnvelope = new ImpulseEnvelope();

    /// <summary>좌우 반동 엔벨로프의 런타임 상태입니다.</summary>
    private readonly ImpulseEnvelope m_yawRecoilEnvelope = new ImpulseEnvelope();

    /// <summary>세로 반동에서 되잡기 입력으로 상쇄된 누적량입니다.</summary>
    private float m_pitchRecoilAbsorb;

    /// <summary>좌우 반동에서 되잡기 입력으로 상쇄된 누적량입니다.</summary>
    private float m_yawRecoilAbsorb;



    /// <summary>비전투 상태의 카메라와 시점 모드 설정값입니다.</summary>
    /// <remarks>
    /// 전투는 조준과 탄착이 화면 중앙을 기준으로 하므로 선택 대상이 아닙니다. 코드에서 백뷰로 고정하고,
    /// 전투 카메라는 <see cref="AimController"/>가 소유합니다. 그래서 여기 노출되는 것은 비전투 두 상태뿐입니다.
    ///
    /// 이 묶음은 아직 <c>[BalanceField]</c>를 붙이지 않았습니다. 밸런스 SO에 같은 이름 필드가 없으면
    /// 바인드마다 경고가 남기 때문입니다. SO를 다시 생성할 때 함께 승격하면 됩니다.
    /// </remarks>
    [Foldout("View Options")]
    [Tooltip("비전투 이동 중 사용할 Cinemachine 카메라입니다. 비어 있으면 상시 활성 카메라를 그대로 씁니다.")]
    [SerializeField] private CinemachineCamera m_nonCombatCamera;

    [Tooltip("비전투 이동 중 시점 모드입니다. BackView이면 몸이 카메라 정면을 따라가고 옆·뒤 입력이 게걸음/뒷걸음이 됩니다.")]
    [SerializeField] private CameraViewMode m_nonCombatViewMode = CameraViewMode.BackView;

    [Tooltip("멈춰 있는 동안 사용할 Cinemachine 카메라입니다. 비어 있으면 상시 활성 카메라를 그대로 씁니다.")]
    [SerializeField] private CinemachineCamera m_idleCamera;

    [Tooltip("멈춰 있는 동안의 시점 모드입니다. FreeLook이면 제자리에서 주변을 살필 수 있습니다.")]
    [SerializeField] private CameraViewMode m_idleViewMode = CameraViewMode.FreeLook;

    [Tooltip("이동 입력이 끊긴 뒤 Idle 시점으로 넘어가기까지의 대기 시간입니다. 단위는 초입니다.")]
    [Clamp(Min = 0)]
    [SerializeField] private float m_freeLookIdleDelay = 1.5f;

    [Tooltip("백뷰에서 몸이 카메라 정면을 따라가는 회전 보간 시간입니다. 0이면 즉시 따라붙습니다. 전투와 비전투 백뷰가 이 값을 함께 씁니다.")]
    [Range(0.0f, 0.3f)]
    [Clamp(Min = 0, Max = 0.3)]
    [SerializeField] private float m_backViewRotationSmoothTime = 0.03f;



    /// <summary>애니메이션 이벤트에서 재생할 캐릭터 오디오 설정값입니다.</summary>
    [Foldout("Audio Options")]
    [Tooltip("착지 애니메이션 이벤트에서 재생할 효과음입니다.")]
    [FormerlySerializedAs("LandingAudioClip")]
    [SerializeField] private AudioClip m_landingAudioClip;

    [Tooltip("걸음 애니메이션 이벤트에서 후보 중 하나를 골라 재생할 발소리 목록입니다.")]
    [FormerlySerializedAs("FootstepAudioClips")]
    [SerializeField] private AudioClip[] m_footstepAudioClips;

    [Tooltip("웅크린 걸음 애니메이션 이벤트에서 재생할 FMOD 발소리 이벤트입니다. 지정하면 AudioClip 목록보다 우선합니다.")]
    [SerializeField] private FMODUnity.EventReference m_footstepEvent;

    [Tooltip("서 있는 기본 이동과 전력질주 애니메이션 이벤트에서 재생할 FMOD 달리기 발소리 이벤트입니다.")]
    [SerializeField] private FMODUnity.EventReference m_runFootstepEvent;

    [Tooltip("점프가 실제로 시작되는 순간 재생할 FMOD 도약 효과음 이벤트입니다.")]
    [SerializeField] private FMODUnity.EventReference m_jumpEvent;

    [Tooltip("캐릭터가 실제 지면에 닿는 순간 재생할 FMOD 착지 효과음 이벤트입니다.")]
    [SerializeField] private FMODUnity.EventReference m_landEvent;

    [Tooltip("이 캐릭터가 착용한 신발 종류입니다. FMOD Footwear 파라미터에 전달됩니다.")]
    [SerializeField] private FootwearType m_footwearType = FootwearType.Unknown;

    [Tooltip("발소리와 착지음의 재생 음량입니다.")]
    [Range(0, 1)]
    [FormerlySerializedAs("FootstepAudioVolume")]
    [BalanceField]
    [Clamp(Min = 0, Max = 1)]
    [SerializeField] private float m_footstepAudioVolume = 0.5f;

    /// <summary>누락된 FMOD 이동 효과음 이벤트 경고가 반복 출력되지 않게 합니다.</summary>
    private bool m_loggedMissingMovementFmodEvent;

    /// <summary>현재 지면 접촉에서 착지음을 이미 재생했는지 여부입니다.</summary>
    private bool m_landingSoundPlayedForCurrentGroundContact = true;

    [Foldout("Debug")]
    [Tooltip("이 캐릭터를 선택했을 때 접지 판정 구를 Scene 뷰에 표시합니다. 접지 중이면 초록, 아니면 빨강입니다.")]
    [SerializeField] private bool m_debugDrawGroundCheck = true;

    /// <summary>카메라 회전 보간에 사용하는 현재 yaw 값입니다.</summary>
    private float m_cinemachineTargetYaw;

    /// <summary>카메라 회전 보간에 사용하는 현재 pitch 값입니다.</summary>
    private float m_cinemachineTargetPitch;

    /// <summary>실제 소비되는 세로(피치) 반동 오프셋입니다. 온셋 보간 ON이면 <see cref="m_recoilPitchTarget"/>를 향해 이징하고, OFF면 목표와 동일합니다.</summary>
    private float m_recoilPitchOffset;

    /// <summary>실제 소비되는 좌우(요) 반동 오프셋입니다. 온셋 보간 ON이면 <see cref="m_recoilYawTarget"/>를 향해 이징하고, OFF면 목표와 동일합니다.</summary>
    private float m_recoilYawOffset;

    /// <summary>세로(피치) 반동 목표값입니다. AddRecoil이 즉시 누적하고 지연 후 0으로 회복하며, 소비 오프셋이 이 값을 추종합니다.</summary>
    private float m_recoilPitchTarget;

    /// <summary>좌우(요) 반동 목표값입니다. AddRecoil이 즉시 누적하고 지연 후 0으로 회복하며, 소비 오프셋이 이 값을 추종합니다.</summary>
    private float m_recoilYawTarget;

    /// <summary>마지막으로 반동이 가해진 시각입니다. 발사 입력을 놓은 뒤 회복 시작 유예 판정에 사용합니다.</summary>
    private float m_lastRecoilTime = float.NegativeInfinity;

    /// <summary>현재 프레임 이동에 사용할 수평 속도입니다.</summary>
    private float m_speed;

    /// <summary>Animator의 이동 블렌드 파라미터에 전달할 보간 속도값입니다.</summary>
    private float m_animationBlend;
    /// <summary>캐릭터가 바라봐야 할 목표 Y축 회전각입니다.</summary>
    private float m_targetRotation = 0.0f;

    /// <summary>대각선 입력끼리의 전환을 판정하기 위한 직전 프레임 이동 입력입니다.</summary>
    private Vector2 m_previousMoveInput;

    /// <summary><see cref="Mathf.SmoothDampAngle"/>에서 사용하는 회전 속도 참조값입니다.</summary>
    private float m_rotationVelocity;

    /// <summary>현재 수직 속도입니다. 점프와 중력 계산에 사용합니다.</summary>
    private float m_verticalVelocity;

    /// <summary>수직 낙하 속도의 상한값입니다.</summary>
    private readonly float m_terminalVelocity = 53.0f;

    /// <summary>점프 재입력 제한 타이머입니다.</summary>
    private float m_jumpTimeoutDelta;

    /// <summary>낙하 애니메이션 전환 지연 타이머입니다.</summary>
    private float m_fallTimeoutDelta;

    /// <summary>Animator Speed 파라미터 해시입니다.</summary>
    private int m_animIDSpeed;

    /// <summary>Animator Grounded 파라미터 해시입니다.</summary>
    private int m_animIDGrounded;

    /// <summary>Animator Jump 파라미터 해시입니다.</summary>
    private int m_animIDJump;

    /// <summary>Animator FreeFall 파라미터 해시입니다.</summary>
    private int m_animIDFreeFall;

    /// <summary>애니메이터를 기본 상태로 되돌리는 트리거의 해시입니다.</summary>
    private int m_animIDReset;

    /// <summary>
    /// 전환 직후 밀려난 속도를 무시할 남은 프레임 수입니다.
    /// </summary>
    /// <remarks>
    /// 겹침을 푸는 이동은 조작을 넘겨받은 다음 프레임에 한 번 일어나고, 그 뒤로는
    /// 자기 velocity를 다시 읽으며 스스로 이어집니다. 그 고리를 끊을 만큼만 잡으면 됩니다.
    /// </remarks>
    private int m_switchSettleFrames;

    /// <summary>전환 직후 밀려난 속도를 무시할 프레임 수입니다.</summary>
    private const int SwitchSettleFrameCount = 3;

    /// <summary>Animator MotionSpeed 파라미터 해시입니다.</summary>
    private int m_animIDMotionSpeed;

    /// <summary>Animator MoveX 파라미터 해시입니다. 8방향 이동 블렌드의 좌우 축입니다.</summary>
    private int m_animIDMoveX;

    /// <summary>Animator MoveZ 파라미터 해시입니다. 8방향 이동 블렌드의 앞뒤 축입니다.</summary>
    private int m_animIDMoveZ;

    /// <summary>Animator LookX 파라미터 해시입니다. 카메라와 몸의 좌우 각도 차입니다.</summary>
    private int m_animIDLookX;

    /// <summary>Animator LookY 파라미터 해시입니다. 카메라의 상하 각도입니다.</summary>
    private int m_animIDLookY;

    /// <summary>Animator IsCrouch 파라미터 해시입니다.</summary>
    private int m_animIDCrouch;

    /// <summary>Animator IsMove 파라미터 해시입니다. 정지 상태와 이동 상태를 가릅니다.</summary>
    private int m_animIDIsMove;

    /// <summary>Animator MoveState 파라미터 해시입니다. 웅크림/걷기/달리기 단계 축입니다.</summary>
    private int m_animIDMoveState;

    /// <summary>Animator IsAim 파라미터 해시입니다. 전투 자세 여부와 같습니다.</summary>
    private int m_animIDAim;

    /// <summary>이동 방향을 유효한 방향으로 볼 최소 수평 속력입니다.</summary>
    private const float MoveDirectionThreshold = 0.01f;

    /// <summary>이웃한 대각선 입력끼리 보간할 수 있는 최대 입력 각도입니다.</summary>
    private const float DiagonalTransitionMaxAngle = 90.0f;

    /// <summary>정규화된 입력을 대각선 영역으로 판정할 때 각 축에 필요한 최소 절댓값입니다.</summary>
    private const float DiagonalInputAxisThreshold = 0.5f;

    /// <summary>
    /// 애니메이터에 넣는 몸 기준 이동 방향입니다. 이동 중에는 크기가 1로 유지됩니다.
    /// </summary>
    /// <remarks>
    /// 축별로 따로 감쇠하지 않는 이유는 방향을 반대로 바꿀 때 값이 반드시 원점을 지나기 때문입니다.
    /// 방향 블렌드 트리는 원점에 정지 클립을 두고 있어서, 좌에서 우로 바꾸는 것만으로 그 사이에
    /// 정지 자세가 한 번 끼어듭니다. 실제로 "걷다-멈칫-걷다"로 보였습니다.
    ///
    /// 그래서 이동 중에는 크기를 1로 붙들고 <b>각도만</b> 돌립니다. 그러면 좌에서 우로 갈 때 앞이나 뒤를
    /// 거쳐 원 위를 돌아가므로 정지 자세를 지나지 않습니다. 멈출 때만 크기를 0으로 줄여 원점(정지)으로 갑니다.
    /// </remarks>
    private Vector2 m_moveDirectionSmoothed;

    /// <summary>LookX를 -1~1로 정규화할 때 기준으로 쓰는 최대 좌우 각도(도)입니다.</summary>
    /// <remarks>자유 시점에서는 몸이 카메라 정반대를 볼 수 있으므로 180도를 씁니다.</remarks>
    private const float MaxLookYaw = 180.0f;

    /// <summary>MoveState 축의 웅크림 단계 값입니다.</summary>
    private const float MoveStateCrouch = 0.0f;

    /// <summary>MoveState 축의 걷기 단계 값입니다.</summary>
    private const float MoveStateWalk = 1.0f;

    /// <summary>MoveState 축의 달리기 단계 값입니다.</summary>
    private const float MoveStateRun = 2.0f;

    /// <summary>현재 애니메이터에 넣고 있는 MoveState 값입니다. 목표 단계를 향해 보간합니다.</summary>
    private float m_moveState = MoveStateWalk;

    /// <summary>이번 프레임에 전력질주 방향 조건을 통과했는지입니다. 프레임당 한 번 판정해 캐싱합니다.</summary>
    private bool m_canSprintForward;

    /// <summary>공중 상태를 강제로 끊을 때 되돌아갈 기본 지상 이동 상태입니다.</summary>
    /// <remarks>
    /// <c>Move</c> 서브 스테이트 머신 안의 상태이므로 경로에 머신 이름이 들어갑니다.
    /// 조준 분기는 진입 후 <c>IsAim</c> 전이가 알아서 처리하므로 비조준 쪽을 목표로 삼습니다.
    /// </remarks>
    private const string GroundedLocomotionStatePath = "Base Layer.Move.Default Move Blend";

    /// <summary>공중 상태를 강제로 끊을 때 되돌아갈 기본 지상 이동 상태의 해시입니다.</summary>
    private static readonly int GroundedLocomotionStateHash = Animator.StringToHash(GroundedLocomotionStatePath);

#if ENABLE_INPUT_SYSTEM
    /// <summary>현재 입력 장치 판별에 사용하는 PlayerInput 컴포넌트입니다.</summary>
    private PlayerInput m_playerInput;
#endif
    /// <summary>캐릭터 애니메이션 파라미터를 갱신할 Animator 컴포넌트입니다.</summary>
    private Animator m_animator;

    /// <summary>실제 이동 충돌 처리를 수행하는 CharacterController 컴포넌트입니다.</summary>
    private CharacterController m_controller;

    /// <summary>Starter Assets 입력 상태를 보관하는 입력 컴포넌트입니다.</summary>
    private PlayerInputController m_input;

    /// <summary>MainCamera 태그로 찾은 기준 카메라 오브젝트입니다.</summary>
    private GameObject m_mainCamera;

    /// <summary>입력값을 유효한 입력으로 간주하기 위한 최소 제곱 크기 기준입니다.</summary>
    private const float Threshold = 0.01f;

    /// <summary>동료 AI에게 길을 양보해 달라고 다시 요청할 수 있는 최소 간격입니다.</summary>
    private const float SquadYieldRequestInterval = 0.15f;

    /// <summary>가벼운 접촉만으로 동료를 움직이지 않게 하는 최소 진행 입력입니다.</summary>
    private const float SquadYieldMinimumInput = 0.2f;

    private float m_nextSquadYieldRequestTime;

    /// <summary>Animator 컴포넌트가 존재하는지 여부입니다.</summary>
    private bool m_hasAnimator;

    /// <summary>필수 참조 캐싱과 검증이 완료되었는지 여부입니다.</summary>
    private bool m_hasRequiredReferences;

    /// <summary>현재 캐릭터가 지면에 닿아 있는지 여부입니다.</summary>
    private bool m_grounded = true;

    /// <summary>직전 프레임의 접지 상태입니다. 이번 프레임의 착지 순간을 판정합니다.</summary>
    private bool m_wasGrounded = true;

    /// <summary>직전 프레임의 전력질주 입력 상태입니다. 웅크림을 취소할 새 전력질주 입력만 구분합니다.</summary>
    private bool m_previousSprintInput;

    /// <summary>전투 자세(조준/사격/사격 잔류) 여부입니다. AimController가 통지합니다.</summary>
    /// <remarks>전투 자세는 설정과 무관하게 백뷰로 고정되고, 전력질주가 잠깁니다.</remarks>
    private bool m_isCombatStance;

    /// <summary>ADS/전투 자세 전환 뒤 전용 속도 보간을 유지할 남은 시간입니다.</summary>
    private float m_combatSpeedTransitionTimer;

    /// <summary>이동 입력이 끊긴 뒤 경과한 시간입니다. Idle 시점 전환 판정에 사용합니다.</summary>
    private float m_idleTimer;

    /// <summary>이번 프레임에 적용 중인 시점 모드입니다.</summary>
    private CameraViewMode m_viewMode = CameraViewMode.FreeLook;

    /// <summary>이번 프레임에 판정된 시점 컨텍스트입니다.</summary>
    private ViewContext m_viewContext = ViewContext.Idle;

    /// <summary>서기 상태의 CharacterController 높이입니다. 첫 프레임에 한 번 기억합니다.</summary>
    private float m_standingHeight;

    /// <summary>서기 상태의 CharacterController 중심입니다.</summary>
    private Vector3 m_standingCenter;

    /// <summary>서기 상태의 카메라 타겟 로컬 Y입니다.</summary>
    private float m_standingCameraTargetY;

    /// <summary>웅크림 진행도입니다. 0이면 서기, 1이면 완전히 웅크린 상태입니다.</summary>
    private float m_crouchBlend;

    /// <summary>서기/웅크림 기준값을 기억했는지 여부입니다.</summary>
    private bool m_hasCrouchBaseline;

    /// <summary>재장전 상태 여부입니다. true이면 전력질주 대신 기본 이동 속도를 사용합니다.</summary>
    private bool m_isReload;

    /// <summary>스킬 등 외부 효과가 적용하는 이동 속도 배율입니다.</summary>
    /// <remarks>밸런스 원본은 건드리지 않고 런타임 계산에만 합성합니다.</remarks>
    private float m_movementSpeedMultiplier = 1.0f;

    /// <summary>스킬 등 외부 효과가 적용하는 자세 전환 속도 배율입니다.</summary>
    private float m_actionSpeedMultiplier = 1.0f;

    /// <summary>기본 이동 속도입니다.</summary>
    public float MoveSpeed => m_moveSpeed * m_movementSpeedMultiplier;
    /// <summary>전력질주 이동 속도입니다.</summary>
    public float SprintSpeed => m_sprintSpeed * m_movementSpeedMultiplier;
    /// <summary>전진 입력에 적용하는 이동 속도 배율입니다.</summary>
    public float ForwardSpeedMultiplier => m_forwardSpeedMultiplier;
    /// <summary>후진 입력에 적용하는 이동 속도 배율입니다.</summary>
    public float BackwardSpeedMultiplier => m_backwardSpeedMultiplier;
    /// <summary>좌·우 입력에 공통 적용하는 이동 속도 배율입니다.</summary>
    public float StrafeSpeedMultiplier => m_strafeSpeedMultiplier;
    /// <summary>웅크린 상태의 이동 속도입니다.</summary>
    public float CrouchSpeed => m_crouchSpeed * m_movementSpeedMultiplier;
    /// <summary>현재 앉기 정도입니다. 0이면 선 자세, 1이면 완전히 앉은 자세이고 그 사이는 전환 중입니다.</summary>
    /// <remarks>높이 보간에 쓰는 값을 그대로 내보냅니다. 탄퍼짐처럼 자세에 따라 달라지는 값이 같은 곡선을 타야 합니다.</remarks>
    public float CrouchBlend => m_crouchBlend;

    /// <summary>
    /// 앉기가 지금 실제로 적용 중인지 여부입니다. 공중에서는 입력이 눌려 있어도 <c>false</c>입니다.
    /// </summary>
    /// <remarks>
    /// 공중에서 누른 앉기는 <b>예약</b>일 뿐입니다. 발이 땅에 닿기 전에는 이동 속도·캡슐 높이·애니메이션
    /// 어느 것도 바뀌면 안 됩니다. 입력은 그대로 두므로 착지하는 프레임부터 곧바로 적용됩니다
    /// (착지 모션 연결은 <see cref="UpdateCrouchLandingTransition"/>가 맡습니다).
    ///
    /// 앉기 효과를 쓰는 곳이 이동 속도·애니메이터·무브스테이트·캡슐 높이로 흩어져 있어, 각자
    /// <c>m_input.Crouch</c>를 직접 읽으면 한 곳만 빠뜨렸을 때 공중에서 일부 효과만 걸립니다.
    /// 판단을 여기 하나로 모읍니다.
    ///
    /// 접지 판정(<c>GroundedCheck</c>)이 <c>Update</c>에서 이 값을 읽는 모든 처리보다 먼저 돌아,
    /// 착지·이륙하는 프레임에 한 프레임 밀리지 않습니다.
    /// </remarks>
    public bool IsCrouchActive => m_grounded && m_input != null && m_input.Crouch;
    /// <summary>이동 방향을 바라보는 회전 보간 시간입니다.</summary>
    public float RotationSmoothTime => m_rotationSmoothTime;
    /// <summary>실제 이동 벡터가 새 입력 방향을 향해 회전하는 최대 각속도(°/s)입니다.</summary>
    public float MovementDirectionTurnSpeed => m_movementDirectionTurnSpeed;
    /// <summary>실제 이동 방향 보간을 적용할 최대 각도 차(°)입니다.</summary>
    public float MovementDirectionSmoothMaxAngle => m_movementDirectionSmoothMaxAngle;
    /// <summary>가속과 감속 반응 속도입니다.</summary>
    /// <summary>이동 방향을 애니메이터에 넣을 때의 감쇠 시간(초)입니다.</summary>
    public float MoveDirectionDamp => m_moveDirectionDamp;

    /// <summary>방향 교체로 판정할 각도(도)입니다.</summary>
    public float MoveDirectionSnapAngle => m_moveDirectionSnapAngle;

    /// <summary>방향 교체 시 180도를 도는 데 걸리는 시간(초)입니다.</summary>
    public float MoveDirectionFlipDuration => m_moveDirectionFlipDuration;

    /// <summary>정지와 이동 사이를 오가는 데 걸리는 시간(초)입니다.</summary>
    public float MoveStartStopDuration => m_moveStartStopDuration;

    /// <summary>웅크림·걷기·달리기 단계 사이를 오가는 데 걸리는 시간(초)입니다.</summary>
    public float MoveStateBlendDuration => ScaleActionDuration(m_moveStateBlendDuration);

    /// <summary>현재 외부 이동 속도 배율입니다.</summary>
    public float MovementSpeedMultiplier => m_movementSpeedMultiplier;

    /// <summary>현재 외부 자세 전환 속도 배율입니다.</summary>
    public float ActionSpeedMultiplier => m_actionSpeedMultiplier;

    public float SpeedChangeRate => m_speedChangeRate;

    /// <summary>점프 높이입니다.</summary>
    public float JumpHeight => m_jumpHeight;
    /// <summary>캐릭터에 적용되는 중력 값입니다.</summary>
    public float Gravity => m_gravity;
    /// <summary>점프 재입력 제한 시간입니다.</summary>
    public float JumpTimeout => m_jumpTimeout;
    /// <summary>낙하 상태 전환 지연 시간입니다.</summary>
    public float FallTimeout => m_fallTimeout;

    /// <summary>현재 지면 접촉 상태입니다.</summary>
    public bool Grounded => m_grounded;
    /// <summary>지면 감지 위치의 Y축 오프셋입니다.</summary>
    public float GroundedOffset => m_groundedOffset;
    /// <summary>지면 감지 구체의 반지름입니다.</summary>
    public float GroundedRadius => m_groundedRadius;
    /// <summary>지면으로 판정할 레이어 마스크입니다.</summary>
    public LayerMask GroundLayers => m_groundLayers;

    /// <summary>Cinemachine 카메라가 따라갈 타겟 오브젝트입니다.</summary>
    public GameObject CinemachineCameraTarget => m_cinemachineCameraTarget;
    /// <summary>카메라 상단 회전 제한 각도입니다.</summary>
    public float TopClamp => m_topClamp;
    /// <summary>카메라 하단 회전 제한 각도입니다.</summary>
    public float BottomClamp => m_bottomClamp;
    /// <summary>카메라 회전에 추가 적용할 보정 각도입니다.</summary>
    public float CameraAngleOverride => m_cameraAngleOverride;
    /// <summary>카메라 회전 잠금 여부입니다.</summary>
    public bool LockCameraPosition => m_lockCameraPosition;

    /// <summary>착지 애니메이션 이벤트에서 재생할 오디오 클립입니다.</summary>
    public AudioClip LandingAudioClip => m_landingAudioClip;
    /// <summary>발걸음 애니메이션 이벤트에서 임의 선택할 오디오 클립 배열입니다.</summary>
    public AudioClip[] FootstepAudioClips => m_footstepAudioClips;
    /// <summary>현재 캐릭터가 착용한 신발 종류입니다.</summary>
    public FootwearType Footwear => m_footwearType;
    /// <summary>발걸음과 착지 효과음 재생 볼륨입니다.</summary>
    public float FootstepAudioVolume => m_footstepAudioVolume;

    /// <summary>현재 전투 자세인지 여부입니다.</summary>
    public bool IsCombatStance => m_isCombatStance;
    /// <summary>현재 적용 중인 시점 모드입니다.</summary>
    public CameraViewMode ViewMode => m_viewMode;
    /// <summary>현재 판정된 시점 컨텍스트입니다.</summary>
    public ViewContext CurrentViewContext => m_viewContext;
    /// <summary>현재 재장전 상태인지 여부입니다.</summary>
    public bool IsReload => m_isReload;

    /// <summary>현재 입력 장치가 마우스 기반인지 여부입니다.</summary>
    private bool IsCurrentDeviceMouse
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return m_playerInput != null && m_playerInput.currentControlScheme == "KeyboardMouse";
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// 기본 이동 속도를 설정합니다. 음수는 0으로 보정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetMoveSpeed(float value) => m_moveSpeed = value;
    /// <summary>
    /// 전력질주 속도를 설정합니다. 음수는 0으로 보정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetSprintSpeed(float value) => m_sprintSpeed = value;

    public void SetForwardSpeedMultiplier(float value) => m_forwardSpeedMultiplier = Mathf.Max(0.0f, value);
    public void SetBackwardSpeedMultiplier(float value) => m_backwardSpeedMultiplier = Mathf.Max(0.0f, value);
    public void SetStrafeSpeedMultiplier(float value) => m_strafeSpeedMultiplier = Mathf.Max(0.0f, value);
    /// <summary>
    /// 웅크림 이동 속도를 설정합니다. 음수는 0으로 보정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetCrouchSpeed(float value) => m_crouchSpeed = Mathf.Max(0.0f, value);

    /// <summary>스킬 등 외부 효과가 적용하는 이동 속도 배율을 설정합니다.</summary>
    public void SetMovementSpeedMultiplier(float value)
    {
        m_movementSpeedMultiplier = Mathf.Max(0.01f, value);
    }

    /// <summary>스킬 등 외부 효과가 적용하는 자세 전환 속도 배율을 설정합니다.</summary>
    public void SetActionSpeedMultiplier(float value)
    {
        m_actionSpeedMultiplier = Mathf.Max(0.01f, value);
    }

    private float ScaleActionDuration(float duration)
    {
        return Mathf.Max(0.0f, duration) / m_actionSpeedMultiplier;
    }
    /// <summary>
    /// 회전 보간 시간을 설정합니다. 0에서 0.3 사이로 보정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetRotationSmoothTime(float value) => m_rotationSmoothTime = Mathf.Clamp(value, 0.0f, 0.3f);
    /// <summary>실제 이동 방향의 최대 회전 각속도를 설정합니다.</summary>
    /// <param name="value">새 각속도(°/s)입니다. 음수는 0으로 보정합니다.</param>
    public void SetMovementDirectionTurnSpeed(float value) => m_movementDirectionTurnSpeed = Mathf.Max(0.0f, value);
    /// <summary>실제 이동 방향 보간을 적용할 최대 각도 차를 설정합니다.</summary>
    /// <param name="value">새 임계 각도(°)입니다. 0~180°로 제한합니다.</param>
    public void SetMovementDirectionSmoothMaxAngle(float value) =>
        m_movementDirectionSmoothMaxAngle = Mathf.Clamp(value, 0.0f, 180.0f);
    /// <summary>
    /// 가속과 감속 반응 속도를 설정합니다. 음수는 0으로 보정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    /// <summary>이동 방향 감쇠 시간을 설정합니다.</summary>
    /// <param name="value">새로 적용할 시간(초)입니다.</param>
    public void SetMoveDirectionDamp(float value) => m_moveDirectionDamp = Mathf.Max(0.0f, value);

    /// <summary>방향 교체로 판정할 각도를 설정합니다.</summary>
    /// <param name="value">새로 적용할 각도(도)입니다. 0~180으로 잘립니다.</param>
    public void SetMoveDirectionSnapAngle(float value) => m_moveDirectionSnapAngle = Mathf.Clamp(value, 0.0f, 180.0f);

    /// <summary>방향 교체 시의 회전 시간을 설정합니다.</summary>
    /// <param name="value">새로 적용할 시간(초)입니다.</param>
    public void SetMoveDirectionFlipDuration(float value) => m_moveDirectionFlipDuration = Mathf.Max(0.0f, value);

    /// <summary>정지와 이동 사이의 블렌드 시간을 설정합니다.</summary>
    /// <param name="value">새로 적용할 시간(초)입니다.</param>
    public void SetMoveStartStopDuration(float value) => m_moveStartStopDuration = Mathf.Max(0.0f, value);

    /// <summary>웅크림·걷기·달리기 단계 사이의 블렌드 시간을 설정합니다.</summary>
    /// <param name="value">새로 적용할 시간(초)입니다.</param>
    public void SetMoveStateBlendDuration(float value) => m_moveStateBlendDuration = Mathf.Max(0.0f, value);

    public void SetSpeedChangeRate(float value) => m_speedChangeRate = value;

    /// <summary>
    /// 점프 높이를 설정합니다. 음수는 0으로 보정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetJumpHeight(float value) => m_jumpHeight = value;
    /// <summary>
    /// 중력 값을 설정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetGravity(float value) => m_gravity = value;
    /// <summary>
    /// 점프 재입력 제한 시간을 설정합니다. 음수는 0으로 보정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetJumpTimeout(float value) => m_jumpTimeout = value;
    /// <summary>
    /// 낙하 상태 전환 지연 시간을 설정합니다. 음수는 0으로 보정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetFallTimeout(float value) => m_fallTimeout = value;

    /// <summary>
    /// 지면 감지 위치의 Y축 오프셋을 설정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetGroundedOffset(float value) => m_groundedOffset = value;
    /// <summary>
    /// 지면 감지 구체의 반지름을 설정합니다. 음수는 0으로 보정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetGroundedRadius(float value) => m_groundedRadius = value;
    /// <summary>
    /// 지면으로 판정할 레이어 마스크를 설정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetGroundLayers(LayerMask value) => m_groundLayers = value;

    /// <summary>
    /// Cinemachine 카메라 타겟 오브젝트를 설정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetCinemachineCameraTarget(GameObject value) => m_cinemachineCameraTarget = value;

    /// <summary>
    /// 카메라 타겟의 월드 회전과 내부 yaw/pitch 값을 지정한 회전에 맞춥니다.
    /// </summary>
    /// <param name="worldRotation">적용할 월드 회전입니다.</param>
    public void SyncCameraTargetRotation(Quaternion worldRotation)
    {
        if (m_cinemachineCameraTarget == null)
        {
            return;
        }

        Vector3 eulerAngles = worldRotation.eulerAngles;
        float pitch = eulerAngles.x - m_cameraAngleOverride;

        if (pitch > 180.0f)
        {
            pitch -= 360.0f;
        }

        m_cinemachineTargetYaw = eulerAngles.y;
        m_cinemachineTargetPitch = ClampAngle(pitch, m_bottomClamp, m_topClamp);

        m_cinemachineCameraTarget.transform.rotation = Quaternion.Euler(
            m_cinemachineTargetPitch + m_cameraAngleOverride,
            m_cinemachineTargetYaw,
            0.0f);
    }

    /// <summary>
    /// 저장된 yaw/pitch(입력 누적값)를 바꾸지 않고, 카메라 타겟의 절대 월드 회전만 다시 적용합니다.
    /// </summary>
    /// <remarks>
    /// 이 컴포넌트가 비활성화된 동안(예: 상호작용 락으로 조작이 잠긴 구간)에는 <see cref="CameraRotation"/>이
    /// 돌지 않아 카메라 타겟의 절대 회전이 더 이상 매 프레임 재적용되지 않습니다. 그 상태에서 부모(플레이어 루트)가
    /// 다른 이유로 회전하면(예: 구조 중 대상을 바라보도록 몸을 돌리는 처리) 자식인 카메라 타겟도 같이 돌아간 것처럼
    /// 보입니다(부모-자식 회전 합성). 비활성 상태에서도 매 틱 이 메서드를 호출하면 카메라가 눌러 고정된 것처럼 유지됩니다.
    /// </remarks>
    public void ReapplyCameraRotation()
    {
        if (m_cinemachineCameraTarget == null)
        {
            return;
        }

        m_cinemachineCameraTarget.transform.rotation = Quaternion.Euler(
            RecoilAdjustedPitch + m_cameraAngleOverride,
            m_cinemachineTargetYaw + m_recoilYawOffset,
            0.0f);
    }
    /// <summary>
    /// 카메라 상단 회전 제한 각도를 설정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetTopClamp(float value) => m_topClamp = value;
    /// <summary>
    /// 카메라 하단 회전 제한 각도를 설정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetBottomClamp(float value) => m_bottomClamp = value;
    /// <summary>
    /// 카메라 회전 보정 각도를 설정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetCameraAngleOverride(float value) => m_cameraAngleOverride = value;
    /// <summary>
    /// 카메라 회전 잠금 여부를 설정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetLockCameraPosition(bool value) => m_lockCameraPosition = value;

    /// <summary>
    /// 사격 반동을 누적합니다. 논리 조준(pitch/yaw)에 함께 얹혀 실제 조준이 밀리고 탄착에도 영향을 줍니다.
    /// </summary>
    /// <param name="pitchDegrees">세로(피치) 반동 각도(도)입니다. 양수면 조준이 위로 솟습니다.</param>
    /// <param name="yawDegrees">좌우(요) 반동 각도(도)입니다.</param>
    /// <remarks>
    /// 축별 회복 비율 r(<see cref="m_pitchRecoveryRatio"/>/<see cref="m_yawRecoveryRatio"/>)로 반동을 두 몫으로 나눕니다.
    /// - 회복분(= 반동 × r): 별도 오프셋에 쌓았다가 <see cref="CameraRotation"/>에서 지연 후 0으로 회복(자동). 플레이어 입력을 안 먹도록 오프셋으로 처리.
    /// - 영구분(= 반동 × (1−r)): 실제 조준값(<see cref="m_cinemachineTargetPitch"/>/<see cref="m_cinemachineTargetYaw"/>)에 박아 안 돌아옴 → 플레이어가 되잡음(누적/walking).
    /// r=1이면 전부 회복분(자동), r=0이면 전부 영구분(하드). pitch 영구분은 <see cref="CameraRotation"/>의 상하 한계로 클램프되고, 회복분은 <see cref="RecoilAdjustedPitch"/>로 제한됩니다.
    /// 오프셋이 <see cref="LogicalAimRotation"/>(탄 판정)·렌더 카메라 양쪽에 반영됩니다. 시각 전용 juice(롤·FOV)는 <see cref="AimController"/>가 별도로 처리합니다.
    /// </remarks>
    public void AddRecoil(float pitchDegrees, float yawDegrees)
    {
        float pitchR = Mathf.Clamp01(m_pitchRecoveryRatio);
        float yawR = Mathf.Clamp01(m_yawRecoveryRatio);

        // 회복분(= 반동 × r) → 오프셋(멈추면 0으로 회복).
        // pitch 상한: Use Pitch Offset Cap이 켜지면 고정 캡, 꺼지면(기본) "조준 헤드룸"으로 클램프한다.
        //   헤드룸 = 현재 조준에서 상하 한계까지 남은 거리. 이렇게 하면 소비 pitch가 look 한계를 넘지 않는 선까지만
        //   오프셋이 쌓여 (a) 천장까지 자연스럽게 상승하고, (b) 화면 너머로 과누적돼 멈춘 뒤 회복이 지연되는 현상(dead-lag)을 방지한다.
        // 목표(target)에 즉시 누적한다. 실제 소비 오프셋은 CameraRotation에서 이 목표를 (즉시 or 보간으로) 추종한다.
        // 엔벨로프를 켠 축은 목표값에 쌓지 않고 이번 발의 곡선 하나를 새로 시작합니다.
        // 상한은 목표값이 아니라 매 프레임 합에 걸므로 여기서 클램프하지 않습니다.
        float pitchAdd = pitchDegrees * pitchR;
        if (m_usePitchRecoilEnvelope)
        {
            m_pitchRecoilEnvelope.Add(pitchAdd);
        }
        else if (m_usePitchOffsetCap)
        {
            m_recoilPitchTarget = Mathf.Clamp(m_recoilPitchTarget + pitchAdd, -m_recoilMaxPitch, m_recoilMaxPitch);
        }
        else
        {
            float offsetMin = m_cinemachineTargetPitch - m_topClamp;      // 아래쪽 여유(음수)
            float offsetMax = m_cinemachineTargetPitch - m_bottomClamp;   // 위쪽 여유(반동은 주로 이쪽)
            m_recoilPitchTarget = Mathf.Clamp(m_recoilPitchTarget + pitchAdd, offsetMin, offsetMax);
        }

        // yaw는 조준 클램프가 없어(360 자유) 캡을 켜두는 게 기본. 끄면 무제한 누적.
        float yawAdd = yawDegrees * yawR;
        if (m_useYawRecoilEnvelope)
        {
            m_yawRecoilEnvelope.Add(yawAdd);
        }
        else
        {
            m_recoilYawTarget = m_useYawOffsetCap
                ? Mathf.Clamp(m_recoilYawTarget + yawAdd, -m_recoilMaxYaw, m_recoilMaxYaw)
                : m_recoilYawTarget + yawAdd;
        }

        // 영구분(= 반동 × (1−r), 회복 안 하는 몫) → 실제 조준값에 박음(안 돌아옴). pitch는 위로(빼기 규약), yaw는 더함.
        // pitch 영구분은 CameraRotation의 상하 한계로 클램프되고, yaw는 무제한(좌우로 걸어감).
        m_cinemachineTargetPitch -= pitchDegrees * (1.0f - pitchR);
        m_cinemachineTargetYaw += yawDegrees * (1.0f - yawR);

        m_lastRecoilTime = Time.time;
    }

    /// <summary>
    /// 반동을 적용한 시점 pitch입니다. 단, 기본 마우스 조준과 동일한 상하 한계(<see cref="m_bottomClamp"/>~<see cref="m_topClamp"/>)를 넘지 않도록 클램프합니다.
    /// </summary>
    /// <remarks>
    /// 반동 pitch는 위(작은 값)로 밀지만, 기본 조준으로 올려다볼 수 있는 최대치를 넘어 하늘/바닥으로 튀지 않게 같은 범위로 제한합니다(그대로 두면 −30°를 넘겨 시점이 뒤집혀 기괴해짐).
    /// 좌우(yaw) 반동은 항상 회복되므로 이런 제한이 필요 없어 클램프하지 않습니다. 클램프는 소비되는 시점 값에만 걸고 누적 오프셋 자체는 회복 로직에 맡깁니다.
    /// </remarks>
    private float RecoilAdjustedPitch => Mathf.Clamp(m_cinemachineTargetPitch - m_recoilPitchOffset, m_bottomClamp, m_topClamp);

    /// <summary>
    /// 논리적 "시선(뷰) 방향" 회전입니다. 이름의 Aim은 전투 소유가 아니라 "플레이어가 어디를 보는가"라는 시점 의도를 뜻합니다.
    /// 시각 전용 juice(카메라 롤·FOV 펀치)는 빠지고, 플레이어 시점 입력에 사격 반동 오프셋을 더한 회전입니다.
    /// </summary>
    /// <remarks>
    /// 책임 소재: 이 값은 <b>ThirdPersonController가 소유한 뷰 회전 상태</b>(<see cref="m_cinemachineTargetPitch"/>/<see cref="m_cinemachineTargetYaw"/>,
    /// 상하 클램프, 반동 오프셋, <see cref="m_cameraAngleOverride"/>)만으로 계산되는 순수 파생값이라 여기 둡니다(계산 재료가 여기 있음).
    /// 같은 재료를 <see cref="CameraRotation"/>(렌더)도 씁니다. <see cref="AimController"/>는 이 방향을 <b>소비</b>하는 쪽입니다
    /// — "어디를 보는가"(여기) → "그 방향으로 쏘면 어디 맞나"(AimController의 조준점·히트스캔). 소유를 AimController로 옮기면 뷰 상태를 역참조해야 해 의존이 꼬입니다.
    ///
    /// 동작: 사격 판정(AimPoint 계산)은 이 회전을 기준으로 하며, 반동(<see cref="AddRecoil"/>)은 여기 반영되어 탄착을 실제로 밉니다.
    /// 시각 전용 juice(롤·FOV)는 <see cref="AimController"/>가 조준 카메라 렌즈에만 얹으므로 이 회전에는 들어오지 않습니다.
    /// 피치는 렌더 규약(값이 커질수록 아래)에 맞춰 오프셋을 빼서 "위로 솟는" 반동이 되며, <see cref="RecoilAdjustedPitch"/>로 상하 한계를 넘지 않습니다.
    /// </remarks>
    public Quaternion LogicalAimRotation => Quaternion.Euler(
        RecoilAdjustedPitch + m_cameraAngleOverride,
        m_cinemachineTargetYaw + m_recoilYawOffset,
        0.0f);

    /// <summary>
    /// 논리적 시선(뷰) 전방 방향입니다. 시각 킥이 빠진, 사격 판정에 쓰는 정규화 방향으로, <see cref="AimController"/>가 조준점·발사 계산에 소비합니다.
    /// </summary>
    /// <remarks>소유·책임 근거는 <see cref="LogicalAimRotation"/> 참고(뷰 상태의 순수 파생값이라 ThirdPersonController가 소유, AimController는 소비자).</remarks>
    public Vector3 LogicalAimForward => LogicalAimRotation * Vector3.forward;

    /// <summary>반동 오프셋이 0으로 복귀하는 속도입니다. 시각 킥(AimController)이 회복 속도를 이 값에 맞춰 이질감을 줄일 때 읽습니다.</summary>
    public float RecoilRecoverySpeed => m_recoilRecoverySpeed;

    /// <summary>발사 입력을 놓은 후 논리 반동 회복을 시작하기까지의 유예 시간입니다.</summary>
    public float RecoilRecoveryDelay => m_recoilRecoveryDelay;

    /// <summary>논리 반동 온셋을 보간할지 여부입니다.</summary>
    public bool RecoilOnsetInterpolationEnabled => m_recoilOnsetInterp;

    /// <summary>논리 반동 온셋 보간 속도입니다.</summary>
    public float RecoilOnsetSpeed => m_recoilOnsetSpeed;

    /// <summary>반동 반대 방향 조준 입력으로 회복 오프셋을 우선 상쇄할지 여부입니다.</summary>
    public bool RecoilCompensationAbsorbEnabled => m_recoilCompensationAbsorb;

    /// <summary>회복 가능한 피치 반동 오프셋의 고정 상한 사용 여부입니다.</summary>
    public bool UsePitchOffsetCap => m_usePitchOffsetCap;

    /// <summary>피치 반동 오프셋 고정 상한입니다.</summary>
    public float RecoilMaxPitch => m_recoilMaxPitch;

    /// <summary>회복 가능한 요 반동 오프셋의 고정 상한 사용 여부입니다.</summary>
    public bool UseYawOffsetCap => m_useYawOffsetCap;

    /// <summary>요 반동 오프셋 고정 상한입니다.</summary>
    public float RecoilMaxYaw => m_recoilMaxYaw;

    /// <summary>피치 반동 중 자동 회복할 비율입니다.</summary>
    public float PitchRecoveryRatio => Mathf.Clamp01(m_pitchRecoveryRatio);

    /// <summary>요 반동 중 자동 회복할 비율입니다.</summary>
    public float YawRecoveryRatio => Mathf.Clamp01(m_yawRecoveryRatio);

    /// <summary>회복 중인 현재 피치 반동 오프셋입니다.</summary>
    public float CurrentRecoilPitchOffset => m_recoilPitchOffset;

    /// <summary>회복 중인 현재 요 반동 오프셋입니다.</summary>
    public float CurrentRecoilYawOffset => m_recoilYawOffset;

    /// <summary>반동 오프셋이 0으로 복귀하는 속도를 설정합니다.</summary>
    /// <param name="value">음수는 0으로 보정됩니다.</param>
    public void SetRecoilRecoverySpeed(float value) => m_recoilRecoverySpeed = value;

    /// <summary>발사 입력을 놓은 후 논리 반동 회복을 시작하기까지의 유예 시간을 설정합니다.</summary>
    /// <param name="value">음수는 0으로 보정됩니다.</param>
    public void SetRecoilRecoveryDelay(float value) => m_recoilRecoveryDelay = value;

    /// <summary>논리 반동 온셋 보간 사용 여부를 설정합니다.</summary>
    /// <param name="value">사용하려면 <c>true</c>입니다.</param>
    public void SetRecoilOnsetInterpolationEnabled(bool value) => m_recoilOnsetInterp = value;

    /// <summary>논리 반동 온셋 보간 속도를 설정합니다.</summary>
    /// <param name="value">음수는 0으로 보정됩니다.</param>
    public void SetRecoilOnsetSpeed(float value) => m_recoilOnsetSpeed = value;

    /// <summary>반동 반대 방향 조준 입력으로 회복 오프셋을 우선 상쇄할지 여부를 설정합니다.</summary>
    /// <param name="value">사용하려면 <c>true</c>입니다.</param>
    public void SetRecoilCompensationAbsorbEnabled(bool value) => m_recoilCompensationAbsorb = value;

    /// <summary>피치 반동 오프셋의 고정 상한 사용 여부를 설정합니다.</summary>
    /// <param name="value">사용하려면 <c>true</c>입니다.</param>
    public void SetUsePitchOffsetCap(bool value) => m_usePitchOffsetCap = value;

    /// <summary>피치 반동 오프셋의 고정 상한을 설정합니다.</summary>
    /// <param name="value">음수는 0으로 보정됩니다.</param>
    public void SetRecoilMaxPitch(float value) => m_recoilMaxPitch = value;

    /// <summary>요 반동 오프셋의 고정 상한 사용 여부를 설정합니다.</summary>
    /// <param name="value">사용하려면 <c>true</c>입니다.</param>
    public void SetUseYawOffsetCap(bool value) => m_useYawOffsetCap = value;

    /// <summary>요 반동 오프셋의 고정 상한을 설정합니다.</summary>
    /// <param name="value">음수는 0으로 보정됩니다.</param>
    public void SetRecoilMaxYaw(float value) => m_recoilMaxYaw = value;

    /// <summary>피치 반동 중 자동 회복할 비율을 설정합니다.</summary>
    /// <param name="value">0~1 범위로 보정됩니다.</param>
    public void SetPitchRecoveryRatio(float value) => m_pitchRecoveryRatio = Mathf.Clamp01(value);

    /// <summary>요 반동 중 자동 회복할 비율을 설정합니다.</summary>
    /// <param name="value">0~1 범위로 보정됩니다.</param>
    public void SetYawRecoveryRatio(float value) => m_yawRecoveryRatio = Mathf.Clamp01(value);

    /// <summary>세로 반동을 곡선 엔벨로프로 처리할지 여부입니다.</summary>
    public bool UsePitchRecoilEnvelope => m_usePitchRecoilEnvelope;

    /// <summary>세로 반동 곡선 하나의 길이(초)입니다.</summary>
    public float PitchRecoilEnvelopeDuration => Mathf.Max(0.0f, m_pitchRecoilEnvelopeDuration);

    /// <summary>세로 반동 곡선입니다.</summary>
    public AnimationCurve PitchRecoilEnvelopeCurve => m_pitchRecoilEnvelopeCurve;

    /// <summary>좌우 반동을 곡선 엔벨로프로 처리할지 여부입니다.</summary>
    public bool UseYawRecoilEnvelope => m_useYawRecoilEnvelope;

    /// <summary>좌우 반동 곡선 하나의 길이(초)입니다.</summary>
    public float YawRecoilEnvelopeDuration => Mathf.Max(0.0f, m_yawRecoilEnvelopeDuration);

    /// <summary>좌우 반동 곡선입니다.</summary>
    public AnimationCurve YawRecoilEnvelopeCurve => m_yawRecoilEnvelopeCurve;

    /// <summary>세로 반동 엔벨로프 사용 여부를 설정합니다.</summary>
    /// <remarks>방식을 바꾸면 진행 중이던 상태가 다른 방식에 그대로 남지 않도록 함께 정리합니다.</remarks>
    public void SetUsePitchRecoilEnvelope(bool value)
    {
        if (m_usePitchRecoilEnvelope == value)
        {
            return;
        }

        m_usePitchRecoilEnvelope = value;
        m_pitchRecoilEnvelope.Clear();
        m_pitchRecoilAbsorb = 0.0f;
        m_recoilPitchTarget = 0.0f;
    }

    /// <summary>세로 반동 곡선 길이를 설정합니다.</summary>
    public void SetPitchRecoilEnvelopeDuration(float value) => m_pitchRecoilEnvelopeDuration = Mathf.Max(0.0f, value);

    /// <summary>세로 반동 곡선을 교체합니다.</summary>
    /// <remarks>넘어온 곡선을 복제해 보관합니다. 참조를 그대로 들면 밸런스 SO의 곡선과 같은 인스턴스를 공유합니다.</remarks>
    public void SetPitchRecoilEnvelopeCurve(AnimationCurve value)
    {
        m_pitchRecoilEnvelopeCurve = value == null ? null : new AnimationCurve(value.keys);
    }

    /// <summary>좌우 반동 엔벨로프 사용 여부를 설정합니다.</summary>
    public void SetUseYawRecoilEnvelope(bool value)
    {
        if (m_useYawRecoilEnvelope == value)
        {
            return;
        }

        m_useYawRecoilEnvelope = value;
        m_yawRecoilEnvelope.Clear();
        m_yawRecoilAbsorb = 0.0f;
        m_recoilYawTarget = 0.0f;
    }

    /// <summary>좌우 반동 곡선 길이를 설정합니다.</summary>
    public void SetYawRecoilEnvelopeDuration(float value) => m_yawRecoilEnvelopeDuration = Mathf.Max(0.0f, value);

    /// <summary>좌우 반동 곡선을 교체합니다.</summary>
    public void SetYawRecoilEnvelopeCurve(AnimationCurve value)
    {
        m_yawRecoilEnvelopeCurve = value == null ? null : new AnimationCurve(value.keys);
    }

    /// <summary>현재 회복 중인 논리 반동 오프셋을 즉시 제거합니다. 영구 반동분은 보존됩니다.</summary>
    public void ClearRecoilOffsets()
    {
        m_recoilPitchOffset = 0.0f;
        m_recoilYawOffset = 0.0f;
        m_recoilPitchTarget = 0.0f;
        m_recoilYawTarget = 0.0f;
        m_lastRecoilTime = float.NegativeInfinity;

        // 진행 중인 곡선과 상쇄분도 함께 버립니다. 남기면 초기화 뒤에도 반동이 이어져 보입니다.
        m_pitchRecoilEnvelope.Clear();
        m_yawRecoilEnvelope.Clear();
        m_pitchRecoilAbsorb = 0.0f;
        m_yawRecoilAbsorb = 0.0f;
    }

    /// <summary>
    /// 착지 효과음 클립을 설정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetLandingAudioClip(AudioClip value) => m_landingAudioClip = value;
    /// <summary>
    /// 발걸음 효과음 클립 배열을 설정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetFootstepAudioClips(AudioClip[] value) => m_footstepAudioClips = value;
    /// <summary>
    /// 캐릭터가 착용한 신발 종류를 설정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 신발 종류입니다.</param>
    public void SetFootwearType(FootwearType value) => m_footwearType = value;
    /// <summary>
    /// 효과음 볼륨을 설정합니다. 0에서 1 사이로 보정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetFootstepAudioVolume(float value) => m_footstepAudioVolume = Mathf.Clamp01(value);

    /// <summary>
    /// 전투 자세 여부를 설정합니다. AimController가 전투 자세 진입/이탈 시 호출합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    /// <remarks>
    /// 전투에 들어가면 Idle 타이머를 지웁니다. 남겨 두면 교전이 끝난 첫 프레임에
    /// 이전에 서 있던 시간이 그대로 살아나 곧바로 자유 시점으로 풀립니다.
    /// </remarks>
    public void SetCombatStance(bool value)
    {
        if (m_isCombatStance != value)
        {
            m_combatSpeedTransitionTimer = m_combatSpeedTransitionDuration;
        }

        m_isCombatStance = value;

        if (value)
        {
            m_idleTimer = 0.0f;
        }
    }
    /// <summary>
    /// 재장전 상태를 설정합니다.
    /// </summary>
    /// <param name="value">새로 적용할 값입니다.</param>
    public void SetReload(bool value) => m_isReload = value;

    /// <summary>
    /// 현재 이동 블렌드 상태를 전환 유지용으로 캡처합니다.
    /// </summary>
    /// <returns>이동 블렌드 상태입니다.</returns>
    public LocomotionCarryoverState CaptureLocomotionCarryoverState()
    {
        float motionSpeed = 1.0f;

        if (m_input != null)
        {
            motionSpeed = m_input.analogMovement ? m_input.move.magnitude : 1.0f;
        }

        bool jump = false;
        bool freeFall = false;

        if (m_animator == null)
        {
            m_hasAnimator = TryGetComponent(out m_animator);
        }
        else
        {
            m_hasAnimator = true;
        }

        AssignAnimationIDs();

        if (m_hasAnimator)
        {
            jump = m_animator.GetBool(m_animIDJump);
            freeFall = m_animator.GetBool(m_animIDFreeFall);
        }

        bool grounded = m_grounded && m_verticalVelocity <= 0.0f && !jump && !freeFall;

        return new LocomotionCarryoverState
        {
            HasState = true,
            Speed = m_speed,
            AnimationBlend = m_animationBlend,
            MotionSpeed = motionSpeed,
            TargetRotation = m_targetRotation,
            RotationVelocity = m_rotationVelocity,
            VerticalVelocity = m_verticalVelocity,
            JumpTimeoutDelta = m_jumpTimeoutDelta,
            FallTimeoutDelta = m_fallTimeoutDelta,
            Grounded = grounded,
            Jump = jump,
            FreeFall = freeFall,
        };
    }

    /// <summary>
    /// 전환 직전 캡처한 이동 블렌드 상태를 현재 컨트롤러와 Animator에 적용합니다.
    /// </summary>
    /// <param name="state">적용할 이동 블렌드 상태입니다.</param>
    public void ApplyLocomotionCarryoverState(LocomotionCarryoverState state)
    {
        // 인계 상태가 없더라도 조작을 넘겨받은 것은 사실이므로 겹침 밀림 대비는 켭니다.
        m_switchSettleFrames = SwitchSettleFrameCount;

        // 전환 직후 시점이 갑자기 자유 시점으로 풀리지 않도록 Idle 판정을 처음부터 다시 셉니다.
        m_idleTimer = 0.0f;

        if (!state.HasState)
        {
            return;
        }

        AssignAnimationIDs();

        m_speed = Mathf.Max(0.0f, state.Speed);
        m_animationBlend = Mathf.Max(0.0f, state.AnimationBlend);
        m_targetRotation = state.TargetRotation;
        m_rotationVelocity = state.RotationVelocity;
        m_verticalVelocity = state.VerticalVelocity;
        m_jumpTimeoutDelta = state.JumpTimeoutDelta;
        m_fallTimeoutDelta = state.FallTimeoutDelta;
        m_grounded = state.Grounded;

        if (m_animator == null)
        {
            m_hasAnimator = TryGetComponent(out m_animator);
        }
        else
        {
            m_hasAnimator = true;
        }

        if (m_hasAnimator)
        {
            m_animator.SetFloat(m_animIDSpeed, m_animationBlend);
            m_animator.SetFloat(m_animIDMotionSpeed, state.MotionSpeed);
            m_animator.SetBool(m_animIDGrounded, state.Grounded);
            m_animator.SetBool(m_animIDJump, state.Jump);
            m_animator.SetBool(m_animIDFreeFall, state.FreeFall);
        }
    }

    /// <summary>
    /// AI 제어로 전환될 때 플레이어 조작 중 남은 점프/낙하 상태를 정리합니다.
    /// </summary>
    public void ClearAirborneCarryoverState()
    {
        AssignAnimationIDs();

        m_verticalVelocity = -2.0f;
        m_jumpTimeoutDelta = m_jumpTimeout;
        m_fallTimeoutDelta = m_fallTimeout;
        m_grounded = true;
        m_wasGrounded = true;

        if (m_input != null)
        {
            m_input.jump = false;
            m_previousSprintInput = m_input.sprint;
        }

        if (m_animator == null)
        {
            m_hasAnimator = TryGetComponent(out m_animator);
        }
        else
        {
            m_hasAnimator = true;
        }

        if (m_hasAnimator)
        {
            m_animator.SetFloat(m_animIDSpeed, 0.0f);
            m_animator.SetFloat(m_animIDMotionSpeed, 1.0f);
            m_animator.SetBool(m_animIDGrounded, true);
            m_animator.SetBool(m_animIDJump, false);
            m_animator.SetBool(m_animIDFreeFall, false);
            PlayGroundedLocomotionState(m_animator, this);
            m_animator.Update(0.0f);
        }
    }

    /// <summary>
    /// 지상 이동 상태를 강제로 재생합니다. 상태가 없으면 경고를 남깁니다.
    /// </summary>
    /// <param name="animator">대상 Animator입니다.</param>
    /// <param name="context">경고 로그에 붙일 컨텍스트 오브젝트입니다.</param>
    /// <remarks>
    /// <see cref="Animator.Play(int, int, float)"/>는 없는 상태 해시를 받아도 예외도 로그도 없이 아무 일도 하지 않습니다.
    /// 애니메이터에서 상태 이름이나 서브 스테이트 머신 경로가 바뀌면 공중 강제 종료와 기립 복귀가 조용히 멈추므로
    /// 여기서 한 번 확인해 눈에 보이게 만듭니다.
    /// </remarks>
    public static void PlayGroundedLocomotionState(Animator animator, Object context)
    {
        if (animator == null)
        {
            return;
        }

        if (!animator.HasState(0, GroundedLocomotionStateHash))
        {
            Debug.LogWarning(
                $"[ThirdPersonController] 애니메이터에 '{GroundedLocomotionStatePath}' 상태가 없습니다. " +
                "지상 이동 강제 복귀를 건너뜁니다.",
                context);
            return;
        }

        animator.Play(GroundedLocomotionStateHash, 0, 0.0f);
    }

    /// <summary>
    /// 필수 컴포넌트와 외부 참조를 캐싱하고 누락 여부를 검증합니다.
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

        m_hasRequiredReferences = true;
        BindConfiguredBalance();
    }

    /// <summary>
    /// 카메라 초기 회전값, Animator 파라미터 해시, 점프/낙하 타이머 초기값을 설정합니다.
    /// </summary>
    private void Start()
    {
        if (!m_hasRequiredReferences)
        {
            return;
        }

        if (m_cinemachineCameraTarget != null)
        {
            m_cinemachineTargetYaw = m_cinemachineCameraTarget.transform.rotation.eulerAngles.y;
        }
        else
        {
            Debug.LogWarning("[ThirdPersonController] CinemachineCameraTarget이 비어 있습니다. 카메라 회전 타겟 갱신은 생략됩니다.", this);
        }

        AssignAnimationIDs();
        CacheCrouchBaseline();

        m_jumpTimeoutDelta = m_jumpTimeout;
        m_fallTimeoutDelta = m_fallTimeout;
        m_wasGrounded = m_grounded;
        m_previousSprintInput = m_input.sprint;
    }

    /// <summary>
    /// 서기 상태의 캡슐 높이와 중심, 카메라 타겟 높이를 기억합니다.
    /// </summary>
    /// <remarks>
    /// 웅크림은 이 기준값에서 깎아 내려가는 방식입니다. 기준을 매 프레임 다시 읽으면
    /// 웅크린 높이가 다음 프레임의 서기 높이가 되어 캡슐이 계속 줄어듭니다.
    /// </remarks>
    private void CacheCrouchBaseline()
    {
        if (m_hasCrouchBaseline || m_controller == null)
        {
            return;
        }

        m_standingHeight = m_controller.height;
        m_standingCenter = m_controller.center;

        if (m_cinemachineCameraTarget != null)
        {
            m_standingCameraTargetY = m_cinemachineCameraTarget.transform.localPosition.y;
        }

        m_hasCrouchBaseline = true;
    }

    /// <summary>
    /// 매 프레임 점프/중력, 지면 감지, 이동 처리를 수행합니다.
    /// </summary>
    private void Update()
    {
        if (!m_hasRequiredReferences)
        {
            return;
        }

        m_hasAnimator = TryGetComponent(out m_animator);

        // 접지 판정을 점프/중력보다 먼저 합니다. 순서가 반대면 JumpAndGravity가 직전 프레임의 접지 결과를
        // 읽어, 착지하는 프레임에 IsGrounded와 IsFreeFall이 한 프레임 동안 함께 켜집니다.
        // 그 한 프레임 때문에 착지 직후 재점프가 도약 동작을 건너뛰고 낙하 상태로 새는 일이 있었습니다.
        GroundedCheck();

        if (!m_grounded)
        {
            m_landingSoundPlayedForCurrentGroundContact = false;
        }

        bool landedThisFrame = m_grounded && !m_wasGrounded;

        if (landedThisFrame)
        {
            PlayLandingSound();
        }

        JumpAndGravity();
        // 캡슐 높이를 이동보다 먼저 맞춥니다. 이동 뒤에 바꾸면 이번 프레임 충돌 판정과 높이가 한 프레임 어긋납니다.
        UpdateCrouch();
        UpdateCrouchLandingTransition(landedThisFrame);
        // 시점 판정을 이동보다 먼저 합니다. Move가 이번 프레임의 시점 모드로 몸을 돌리기 때문입니다.
        UpdateViewMode();
        Move();

        m_wasGrounded = m_grounded;
        m_previousSprintInput = m_input.sprint;
    }

    /// <summary>
    /// 웅크림 입력과 천장 여유에 따라 캡슐 높이와 카메라 타겟 높이를 갱신합니다.
    /// </summary>
    private void UpdateCrouch()
    {
        CacheCrouchBaseline();

        if (!m_hasCrouchBaseline)
        {
            return;
        }

        // 앉은 뒤 새로 전력질주를 누르면 일어나 달리기를 시작합니다(캐릭터 행동 시스템 §6).
        // 이미 전력질주를 홀드한 상태에서 공중 웅크리기를 선입력한 경우까지 취소하면, 착지 웅크림이
        // 영영 성립하지 않습니다. 그래서 "현재 홀드 중"이 아니라 "이번 프레임에 새로 눌림"만 소비합니다.
        bool sprintPressedThisFrame = m_input.sprint && !m_previousSprintInput;
        if (m_input.Crouch && sprintPressedThisFrame)
        {
            m_input.CrouchInput(false);
        }

        // 공중에서는 입력이 눌려 있어도 앉지 않습니다. 예약만 남고 착지 프레임부터 적용됩니다.
        bool wantsCrouch = IsCrouchActive;

        // 천장에 막혀 있으면 일어서지 않습니다. 막힌 자리에서 캡슐을 키우면 CharacterController가
        // 겹침을 풀려고 캐릭터를 밀어내기 때문에 지형을 뚫고 튀어 나갑니다.
        if (!wantsCrouch && m_crouchBlend > 0.0f && IsStandBlocked())
        {
            wantsCrouch = true;
        }

        float target = wantsCrouch ? 1.0f : 0.0f;

        float crouchTransitionDuration = ScaleActionDuration(m_crouchTransitionDuration);
        m_crouchBlend = crouchTransitionDuration <= 0.0f
            ? target
            : Mathf.MoveTowards(m_crouchBlend, target, Time.deltaTime / crouchTransitionDuration);

        ApplyCrouchBlend();
    }

    /// <summary>
    /// 공중에서 유지한 웅크리기 입력으로 착지했을 때, 긴 JumpLand 상태를 기다리지 않고 지상 웅크림 블렌드로 부드럽게 넘깁니다.
    /// </summary>
    private void UpdateCrouchLandingTransition(bool landedThisFrame)
    {
        if (!landedThisFrame || !m_input.Crouch || !m_hasAnimator)
        {
            return;
        }

        if (!m_animator.HasState(0, GroundedLocomotionStateHash))
        {
            Debug.LogWarning(
                $"[ThirdPersonController] 애니메이터에 '{GroundedLocomotionStatePath}' 상태가 없습니다. " +
                "착지 웅크림 전환을 건너뜁니다.",
                this);
            return;
        }

        // 이 프레임에 목표 블렌드도 함께 맞춰야 CrossFade 첫 프레임이 걷기 자세로 튀지 않습니다.
        m_moveState = MoveStateCrouch;
        m_animator.SetBool(m_animIDCrouch, true);
        m_animator.SetFloat(m_animIDMoveState, MoveStateCrouch);
        m_animator.CrossFadeInFixedTime(
            GroundedLocomotionStateHash,
            ScaleActionDuration(m_crouchLandingTransitionDuration),
            0);
    }

    /// <summary>
    /// 현재 웅크림 진행도를 캡슐 높이와 중심, 카메라 타겟 높이에 적용합니다.
    /// </summary>
    private void ApplyCrouchBlend()
    {
        float crouchHeight = Mathf.Min(m_crouchHeight, m_standingHeight);
        float height = Mathf.Lerp(m_standingHeight, crouchHeight, m_crouchBlend);
        float drop = m_standingHeight - height;

        m_controller.height = height;

        // 발이 제자리에 남도록 줄어든 만큼의 절반을 중심에서 내립니다.
        // 중심을 그대로 두면 캡슐이 위아래로 같이 줄어들어 발이 땅에서 떠 버립니다.
        Vector3 center = m_standingCenter;
        center.y = m_standingCenter.y - drop * 0.5f;
        m_controller.center = center;

        if (m_cinemachineCameraTarget != null)
        {
            Vector3 local = m_cinemachineCameraTarget.transform.localPosition;
            local.y = m_standingCameraTargetY - drop;
            m_cinemachineCameraTarget.transform.localPosition = local;
        }
    }

    /// <summary>
    /// 지금 자리에서 서기 높이로 되돌릴 여유가 없는지 확인합니다.
    /// </summary>
    /// <returns>머리 위가 막혀 있으면 true입니다.</returns>
    /// <remarks>
    /// 지형 판정에 <see cref="m_groundLayers"/>를 그대로 씁니다. 천장도 같은 레벨 지오메트리라서인데,
    /// 두 마스크가 갈라져야 하는 상황이 오면 천장 전용 마스크를 따로 빼는 것이 맞습니다.
    /// </remarks>
    private bool IsStandBlocked()
    {
        float radius = m_controller.radius;
        float currentTopY = transform.position.y + m_controller.center.y + m_controller.height * 0.5f - radius;
        float standingTopY = transform.position.y + m_standingCenter.y + m_standingHeight * 0.5f - radius;
        float distance = standingTopY - currentTopY;

        if (distance <= 0.0f)
        {
            return false;
        }

        Vector3 origin = new Vector3(transform.position.x, currentTopY, transform.position.z)
                         + new Vector3(m_controller.center.x, 0.0f, m_controller.center.z);

        // 반지름을 살짝 줄입니다. 캡슐과 같은 크기로 쏘면 옆 벽에 붙어 있을 때 늘 막힌 것으로 나옵니다.
        return Physics.SphereCast(
            origin,
            radius * 0.95f,
            Vector3.up,
            out _,
            distance,
            m_groundLayers,
            QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// 모든 Update 처리 이후 카메라 회전을 갱신합니다.
    /// </summary>
    private void LateUpdate()
    {
        if (!m_hasRequiredReferences)
        {
            return;
        }

        CameraRotation();
    }

    /// <summary>
    /// 현재 GameObject와 씬에서 필요한 참조를 캐싱합니다.
    /// </summary>
    private void CacheRequiredReferences()
    {
        if (m_mainCamera == null)
        {
            m_mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
        }

        m_hasAnimator = TryGetComponent(out m_animator);
        m_controller = GetComponent<CharacterController>();
        m_input = GetComponent<PlayerInputController>();
#if ENABLE_INPUT_SYSTEM
        m_playerInput = GetComponent<PlayerInput>();
#else
        Debug.LogError("Starter Assets package is missing dependencies. Please use Tools/Starter Assets/Reinstall Dependencies to fix it", this);
#endif
    }

    /// <summary>
    /// 캐싱된 필수 참조가 모두 유효한지 검사합니다.
    /// </summary>
    /// <returns>필수 참조가 모두 존재하면 true, 하나라도 누락되면 false입니다.</returns>
    private bool ValidateRequiredReferences()
    {
        bool isValid = true;

        if (m_mainCamera == null)
        {
            Debug.LogError("[ThirdPersonController] MainCamera 태그를 가진 카메라를 찾지 못했습니다. 씬의 메인 카메라에 MainCamera 태그를 지정하세요.", this);
            isValid = false;
        }

        if (m_controller == null)
        {
            Debug.LogError("[ThirdPersonController] CharacterController 컴포넌트가 없습니다.", this);
            isValid = false;
        }

        if (m_input == null)
        {
            Debug.LogError("[ThirdPersonController] PlayerInputController 컴포넌트가 없습니다. 같은 GameObject에 추가하세요.", this);
            isValid = false;
        }
#if ENABLE_INPUT_SYSTEM
        if (m_playerInput == null)
        {
            Debug.LogError("[ThirdPersonController] PlayerInput 컴포넌트가 없습니다.", this);
            isValid = false;
        }
#endif

        return isValid;
    }

    /// <summary>
    /// Animator 파라미터 이름을 해시값으로 변환해 캐싱합니다.
    /// </summary>
    private void AssignAnimationIDs()
    {
        m_animIDSpeed = Animator.StringToHash("MoveSpeed");
        m_animIDGrounded = Animator.StringToHash("IsGrounded");
        m_animIDJump = Animator.StringToHash("IsJump");
        m_animIDFreeFall = Animator.StringToHash("IsFreeFall");
        m_animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
        m_animIDReset = Animator.StringToHash("DoReset");
        m_animIDMoveX = Animator.StringToHash("MoveX");
        m_animIDMoveZ = Animator.StringToHash("MoveZ");
        m_animIDLookX = Animator.StringToHash("LookX");
        m_animIDLookY = Animator.StringToHash("LookY");
        m_animIDCrouch = Animator.StringToHash("IsCrouch");
        m_animIDAim = Animator.StringToHash("IsAim");
        m_animIDIsMove = Animator.StringToHash("IsMove");
        m_animIDMoveState = Animator.StringToHash("MoveState");
    }

    /// <summary>
    /// 이 컴포넌트가 소유한 이동 애니메이터 파라미터를 기본값으로 되돌리고 <c>DoReset</c>을 발동합니다.
    /// </summary>
    /// <remarks>
    /// 어떤 상태에 걸려 빠져나오지 못할 때 쓰는 탈출구입니다. 변이체 쪽에서 같은 방식을 쓰고 있어
    /// 플레이어에도 같은 이름으로 둡니다. 인게임 호출부는 없고, 디버그와 복구 용도로 남겨 둡니다.
    ///
    /// 여기서는 이동 계열만 건드립니다. 다운·상호작용·전투 파라미터는 각 소유 시스템이 정리해야 하므로,
    /// 유닛 전체를 기본형으로 되돌리려면 <see cref="SquadMemberController.ResetAnimatorToBase"/>를 부릅니다.
    /// 그쪽이 이 함수를 포함해 소유자별 정리를 모아서 실행합니다.
    ///
    /// 트리거만으로 끝내지 않고 지상 이동 상태를 직접 재생하는 이유는, 애니메이터에서 <c>DoReset</c>
    /// 전이가 빠지면 탈출구가 조용히 무력해지기 때문입니다. 전이가 있으면 결과가 같고, 없으면 이쪽이 받칩니다.
    /// </remarks>
    public void ResetAnimation()
    {
        AssignAnimationIDs();

        if (m_animator == null)
        {
            m_hasAnimator = TryGetComponent(out m_animator);
        }
        else
        {
            m_hasAnimator = true;
        }

        if (!m_hasAnimator)
        {
            return;
        }

        m_speed = 0.0f;
        m_animationBlend = 0.0f;

        // 보간 중간값이 남아 있으면 다음 프레임에 그 값에서 이어져 리셋이 한 프레임만 보입니다.
        m_moveState = MoveStateWalk;

        m_animator.SetFloat(m_animIDSpeed, 0.0f);
        m_animator.SetFloat(m_animIDMotionSpeed, 1.0f);
        m_animator.SetFloat(m_animIDMoveX, 0.0f);
        m_animator.SetFloat(m_animIDMoveZ, 0.0f);
        m_animator.SetFloat(m_animIDMoveState, MoveStateWalk);
        m_animator.SetFloat(m_animIDLookX, 0.0f);
        m_animator.SetFloat(m_animIDLookY, 0.0f);

        m_animator.SetBool(m_animIDGrounded, true);
        m_animator.SetBool(m_animIDJump, false);
        m_animator.SetBool(m_animIDFreeFall, false);
        m_animator.SetBool(m_animIDAim, false);
        m_animator.SetBool(m_animIDCrouch, false);
        m_animator.SetBool(m_animIDIsMove, false);

        m_animator.SetTrigger(m_animIDReset);
        PlayGroundedLocomotionState(m_animator, this);
    }

    /// <summary>
    /// 캐릭터 발밑에 지면이 있는지 구체 검사로 확인하고 Animator에 반영합니다.
    /// </summary>
    private void GroundedCheck()
    {
        Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - m_groundedOffset,
            transform.position.z);
        m_grounded = Physics.CheckSphere(spherePosition, m_groundedRadius, m_groundLayers,
            QueryTriggerInteraction.Ignore);

        if (m_hasAnimator)
        {
            m_animator.SetBool(m_animIDGrounded, m_grounded);
        }
    }

    /// <summary>
    /// 반동 반대 방향 조준 입력을 반동 목표에서 먼저 상쇄하고, 남은 입력만 반환합니다(컴펜세이션 흡수).
    /// </summary>
    /// <param name="lookDelta">이번 프레임 조준 입력 변화량입니다.</param>
    /// <param name="recoilTarget">해당 축의 반동 목표값입니다. 상쇄한 만큼 크기가 줄어듭니다(ref).</param>
    /// <param name="sameSignOpposes">입력과 목표의 부호가 같을 때 "반동 반대"인지 여부입니다. pitch(view=target−offset)=true, yaw(view=target+offset)=false.</param>
    /// <returns>반동을 상쇄하고 남은, 실제로 조준을 움직일 입력량입니다.</returns>
    /// <remarks>플레이어가 반동을 되잡는 입력을 조준 이동이 아니라 반동 해소에 먼저 쓰게 해, 자동회복 시 시점이 과하게 쳐지는 오버 컴펜세이션을 막습니다. 반동과 같은 방향(의도적 재조준·트래킹)은 그대로 통과시킵니다.</remarks>
    /// <summary>
    /// 반동 오프셋을 이번 프레임 값으로 갱신합니다.
    /// </summary>
    /// <remarks>
    /// 축마다 두 방식 중 하나로 처리합니다.
    ///
    /// 엔벨로프를 켠 축은 발 하나가 곡선 하나가 되고, 살아 있는 곡선을 모두 더한 값이 그대로 오프셋입니다.
    /// 회복이 곡선 뒷부분에 들어 있어 별도의 회복 속도나 지연이 관여하지 않습니다.
    ///
    /// 끈 축은 기존 방식입니다. 목표값을 지연 후 0으로 되돌리고, 소비 오프셋이 그 목표를 즉시 또는 보간으로 추종합니다.
    /// 두 방식을 축별로 섞을 수 있게 둔 이유는, 상하만 곡선으로 바꿔 보고 좌우는 그대로 두는 비교가 필요하기 때문입니다.
    /// </remarks>
    private void UpdateRecoilOffsets()
    {
        bool legacyPitch = !m_usePitchRecoilEnvelope;
        bool legacyYaw = !m_useYawRecoilEnvelope;

        // 발사 입력을 유지하는 순간부터 회복을 막습니다. 따라서 초반 연사 구간도 발사 간격/프레임 순서에
        // 따라 오르내리지 않습니다. 클릭은 입력을 놓는 즉시 일반적인 마지막 발 기준 유예·복귀 경로를 탑니다.
        bool shootHeld = m_input != null && m_input.Shoot;
        if ((legacyPitch || legacyYaw) && !shootHeld && Time.time - m_lastRecoilTime > m_recoilRecoveryDelay)
        {
            // 회복은 항상 같은 각속도로 진행합니다. Lerp로 처리하면 초반에는 빠르고 끝에서 느려져,
            // 연사 중 상한에 멈춘 반동이 풀릴 때 감속·버벅임처럼 보입니다.
            float recoveryStep = Mathf.Max(0.0f, m_recoilRecoverySpeed) * Time.deltaTime;

            if (legacyPitch)
            {
                m_recoilPitchTarget = Mathf.MoveTowards(m_recoilPitchTarget, 0.0f, recoveryStep);
            }

            if (legacyYaw)
            {
                m_recoilYawTarget = Mathf.MoveTowards(m_recoilYawTarget, 0.0f, recoveryStep);
            }
        }

        // 온셋 보간: 소비 오프셋이 목표를 향해 지수 이징(앞쪽으로 쏠려 빠르게 붙었다 정착). 끄면 즉시 목표와 동일(기존 계단식).
        float onsetFactor = Mathf.Clamp01(Time.deltaTime * Mathf.Max(0.0f, m_recoilOnsetSpeed));

        if (m_usePitchRecoilEnvelope)
        {
            float sum = EvaluateEnvelopeOffset(
                m_pitchRecoilEnvelope,
                m_pitchRecoilEnvelopeDuration,
                m_pitchRecoilEnvelopeCurve,
                ref m_pitchRecoilAbsorb);

            m_recoilPitchOffset = ClampPitchRecoilOffset(sum);
        }
        else if (m_recoilOnsetInterp)
        {
            m_recoilPitchOffset = Mathf.Lerp(m_recoilPitchOffset, m_recoilPitchTarget, onsetFactor);
        }
        else
        {
            m_recoilPitchOffset = m_recoilPitchTarget;
        }

        if (m_useYawRecoilEnvelope)
        {
            float sum = EvaluateEnvelopeOffset(
                m_yawRecoilEnvelope,
                m_yawRecoilEnvelopeDuration,
                m_yawRecoilEnvelopeCurve,
                ref m_yawRecoilAbsorb);

            m_recoilYawOffset = m_useYawOffsetCap
                ? Mathf.Clamp(sum, -m_recoilMaxYaw, m_recoilMaxYaw)
                : sum;
        }
        else if (m_recoilOnsetInterp)
        {
            m_recoilYawOffset = Mathf.Lerp(m_recoilYawOffset, m_recoilYawTarget, onsetFactor);
        }
        else
        {
            m_recoilYawOffset = m_recoilYawTarget;
        }
    }

    /// <summary>
    /// 살아 있는 엔벨로프를 진행시키고 되잡기 상쇄분을 뺀 값을 돌려줍니다.
    /// </summary>
    /// <param name="envelope">진행시킬 엔벨로프 집합입니다.</param>
    /// <param name="duration">엔벨로프 하나의 길이(초)입니다.</param>
    /// <param name="curve">정규화 시간을 세기 배율로 바꾸는 곡선입니다.</param>
    /// <param name="absorb">되잡기 입력으로 상쇄된 누적량입니다. 이 함수가 함께 정리합니다.</param>
    /// <returns>이번 프레임에 적용할 반동 오프셋입니다.</returns>
    /// <remarks>
    /// 기존 방식은 되잡기 입력이 목표값을 직접 깎았지만, 엔벨로프에는 깎을 목표값이 없습니다.
    /// 그래서 상쇄된 양을 따로 들고 있다가 합에서 뺍니다.
    ///
    /// 상쇄분이 합보다 커지면 부호가 뒤집혀 반동과 반대 방향으로 시점을 밀게 되므로 합에 맞춰 잘라 둡니다.
    /// 자극이 모두 끝나면 상쇄분도 함께 버립니다. 남겨 두면 다음 발의 초반을 이유 없이 깎습니다.
    /// </remarks>
    private static float EvaluateEnvelopeOffset(
        ImpulseEnvelope envelope,
        float duration,
        AnimationCurve curve,
        ref float absorb)
    {
        float sum = envelope.Evaluate(duration, curve, Time.deltaTime);

        if (envelope.ActiveCount == 0)
        {
            absorb = 0.0f;
            return 0.0f;
        }

        bool absorbExceedsSum = Mathf.Abs(absorb) > Mathf.Abs(sum);
        bool absorbFacesWrongWay = absorb != 0.0f && !Mathf.Approximately(Mathf.Sign(absorb), Mathf.Sign(sum));

        if (absorbExceedsSum || absorbFacesWrongWay)
        {
            absorb = sum;
        }

        return sum - absorb;
    }

    /// <summary>
    /// 세로 반동 오프셋을 상한 규칙에 맞게 제한합니다.
    /// </summary>
    /// <remarks>
    /// 기존 방식은 <see cref="AddRecoil"/>에서 목표값을 쌓을 때 이 제한을 걸었습니다.
    /// 엔벨로프는 쌓아 두는 목표값이 없어 합을 낼 때마다 같은 규칙을 적용해야 합니다.
    /// </remarks>
    private float ClampPitchRecoilOffset(float offset)
    {
        if (m_usePitchOffsetCap)
        {
            return Mathf.Clamp(offset, -m_recoilMaxPitch, m_recoilMaxPitch);
        }

        float offsetMin = m_cinemachineTargetPitch - m_topClamp;      // 아래쪽 여유(음수)
        float offsetMax = m_cinemachineTargetPitch - m_bottomClamp;   // 위쪽 여유(반동은 주로 이쪽)
        return Mathf.Clamp(offset, offsetMin, offsetMax);
    }

    /// <summary>
    /// 엔벨로프 방식에서 되잡기 입력을 상쇄분으로 흡수합니다.
    /// </summary>
    /// <param name="lookDelta">이번 프레임의 조준 입력입니다.</param>
    /// <param name="currentOffset">지금 적용 중인 반동 오프셋입니다.</param>
    /// <param name="absorb">상쇄 누적량입니다. 흡수한 만큼 늘립니다.</param>
    /// <param name="sameSignOpposes">입력과 오프셋의 부호가 같을 때 되잡기인지 여부입니다.</param>
    /// <returns>반동을 상쇄하고 남은 조준 입력입니다.</returns>
    private static float AbsorbEnvelopeRecoil(
        float lookDelta,
        float currentOffset,
        ref float absorb,
        bool sameSignOpposes)
    {
        if (lookDelta == 0.0f || currentOffset == 0.0f)
        {
            return lookDelta;
        }

        bool opposes = sameSignOpposes
            ? Mathf.Approximately(Mathf.Sign(lookDelta), Mathf.Sign(currentOffset))
            : !Mathf.Approximately(Mathf.Sign(lookDelta), Mathf.Sign(currentOffset));

        if (!opposes)
        {
            return lookDelta;
        }

        float absorbed = Mathf.Min(Mathf.Abs(lookDelta), Mathf.Abs(currentOffset));
        absorb += absorbed * Mathf.Sign(currentOffset);
        return lookDelta - absorbed * Mathf.Sign(lookDelta);
    }

    private static float AbsorbRecoil(float lookDelta, ref float recoilTarget, bool sameSignOpposes)
    {
        if (lookDelta == 0.0f || recoilTarget == 0.0f)
        {
            return lookDelta;
        }

        bool opposes = sameSignOpposes
            ? Mathf.Sign(lookDelta) == Mathf.Sign(recoilTarget)
            : Mathf.Sign(lookDelta) != Mathf.Sign(recoilTarget);

        if (!opposes)
        {
            return lookDelta;
        }

        float absorbed = Mathf.Min(Mathf.Abs(lookDelta), Mathf.Abs(recoilTarget));
        recoilTarget -= absorbed * Mathf.Sign(recoilTarget);
        return lookDelta - absorbed * Mathf.Sign(lookDelta);
    }

    /// <summary>
    /// 입력값을 바탕으로 카메라 타겟의 yaw/pitch 회전을 갱신합니다.
    /// </summary>
    private void CameraRotation()
    {
        if (m_input.look.sqrMagnitude >= Threshold && !m_lockCameraPosition)
        {
            float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;
            float lookSensitivity = IsCurrentDeviceMouse && GameSettingManager.Instance != null
                ? GameSettingManager.Instance.MouseSensitivity
                : 1.0f;

            float yawDelta = m_input.look.x * deltaTimeMultiplier * lookSensitivity;
            float pitchDelta = m_input.look.y * deltaTimeMultiplier * lookSensitivity;

            if (m_recoilCompensationAbsorb)
            {
                // 반동 반대 방향 조준 입력은 조준을 움직이기 전에 반동 목표부터 상쇄한다(되잡기 흡수 → 오버 컴펜세이션 방지).
                // pitch: view = target − offset → 반동과 "같은 부호" 입력이 반동 반대(아래로). yaw: view = target + offset → "반대 부호"가 반동 반대.
                // 엔벨로프 축은 깎을 목표값이 없어 상쇄분을 따로 누적합니다.
                pitchDelta = m_usePitchRecoilEnvelope
                    ? AbsorbEnvelopeRecoil(pitchDelta, m_recoilPitchOffset, ref m_pitchRecoilAbsorb, sameSignOpposes: true)
                    : AbsorbRecoil(pitchDelta, ref m_recoilPitchTarget, sameSignOpposes: true);

                yawDelta = m_useYawRecoilEnvelope
                    ? AbsorbEnvelopeRecoil(yawDelta, m_recoilYawOffset, ref m_yawRecoilAbsorb, sameSignOpposes: false)
                    : AbsorbRecoil(yawDelta, ref m_recoilYawTarget, sameSignOpposes: false);
            }

            m_cinemachineTargetYaw += yawDelta;
            m_cinemachineTargetPitch += pitchDelta;
        }

        m_cinemachineTargetYaw = ClampAngle(m_cinemachineTargetYaw, float.MinValue, float.MaxValue);
        m_cinemachineTargetPitch = ClampAngle(m_cinemachineTargetPitch, m_bottomClamp, m_topClamp);

        UpdateRecoilOffsets();

        if (m_cinemachineCameraTarget != null)
        {
            // 렌더 카메라도 논리 조준과 동일한 반동 오프셋을 얹어 화면과 탄착이 함께 움직입니다.
            // 피치는 위로 솟는 느낌이 되도록 뺍니다(StarterAssets pitch 규약: 값이 커질수록 아래를 봄).
            // RecoilAdjustedPitch로 기본 조준과 같은 상하 한계에 물려, 반동이 하늘/바닥을 넘어 시점이 뒤집히지 않게 합니다.
            m_cinemachineCameraTarget.transform.rotation = Quaternion.Euler(
                RecoilAdjustedPitch + m_cameraAngleOverride,
                m_cinemachineTargetYaw + m_recoilYawOffset,
                0.0f);
        }
    }

    /// <summary>
    /// 입력 상태, 전력질주 상태, 전투 자세를 반영해 캐릭터를 이동 및 회전시킵니다.
    /// </summary>
    private void Move()
    {
        Vector3 inputDirection = new Vector3(m_input.move.x, 0.0f, m_input.move.y).normalized;

        float currentHorizontalSpeed = new Vector3(
            m_controller.velocity.x,
            0.0f,
            m_controller.velocity.z).magnitude;

        // 정지 출발은 입력 방향을 즉시 잡고, 이미 이동 중일 때만 최대 각속도로 회전합니다.
        // 그렇지 않으면 오래 멈춘 뒤 다른 방향으로 출발할 때도 예전 헤딩에서 휘어 나갑니다.
        // m_targetRotation이 실제 이동 헤딩을 소유하므로 몸 회전, 전력질주 판정, CharacterController 이동이
        // 동일한 보간 결과를 소비합니다.
        if (m_input.move != Vector2.zero)
        {
            float desiredRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg +
                                    m_mainCamera.transform.eulerAngles.y;

            bool alreadyMoving = currentHorizontalSpeed > MoveDirectionThreshold ||
                                 m_speed > MoveDirectionThreshold;

            float directionDelta = Mathf.Abs(Mathf.DeltaAngle(m_targetRotation, desiredRotation));
            bool diagonalToDiagonal = IsNeighboringDiagonalTransition(m_previousMoveInput, m_input.move);
            bool smoothDirectionChange = alreadyMoving &&
                                         m_movementDirectionTurnSpeed > 0.0f &&
                                         (directionDelta <= m_movementDirectionSmoothMaxAngle ||
                                          diagonalToDiagonal);

            m_targetRotation = smoothDirectionChange
                ? Mathf.MoveTowardsAngle(
                    m_targetRotation,
                    desiredRotation,
                    m_movementDirectionTurnSpeed * Time.deltaTime)
                : desiredRotation;
        }

        m_previousMoveInput = m_input.move;

        // 전력질주 방향 판정은 프레임당 한 번만 합니다. 이 판정이 히스테리시스 상태를 들고 있어
        // 여러 번 부르면 같은 프레임 안에서 경계가 두 번 움직입니다.
        m_canSprintForward = EvaluateSprintForward();

        float targetSpeed = m_input.sprint && m_canSprintForward ? SprintSpeed : MoveSpeed;

        // 속도 제약은 시점이 아니라 상태에 걸립니다. 비전투 백뷰에서는 전력질주가 그대로 살아 있습니다.
        //
        // 재장전은 여기에 넣지 않습니다. 달리면서 재장전하는 것이 지금의 조작 방침이라,
        // 재장전이 속도를 걷기로 끌어내리면 달리기가 사실상 끊깁니다. 상체는 Action Layer(상체 마스크)의
        // 재장전 클립이, 하체는 Base Layer 조준 분기의 달리기 티어가 각각 담당하므로 겹쳐도 자세가 성립합니다.
        //
        // 기획 편차: 캐릭터 행동 시스템 §6 「달리기 중에는 재장전을 시작할 수 없다」, §10.2 「달리기와
        // 구조는 재장전을 보류할 수 있다」와 어긋납니다. 사용자 지시로 코드를 먼저 바꾼 상태이며,
        // 문서 개정 전까지 이 편차를 문서 쪽 실수로 오해해 되돌리지 말 것.
        if (m_isCombatStance)
        {
            targetSpeed = MoveSpeed;
        }

        // 웅크리기는 전투 자세보다 뒤에서 덮습니다. 조준하며 웅크려도 웅크림 속도가 유지되어야 하고,
        // 이 순서 덕분에 웅크린 동안은 전력질주가 따로 막지 않아도 자연히 잠깁니다.
        if (IsCrouchActive)
        {
            targetSpeed = CrouchSpeed;
        }

        if (m_input.move == Vector2.zero)
        {
            targetSpeed = 0.0f;
        }
        else
        {
            targetSpeed *= ResolveDirectionalSpeedMultiplier(m_input.move);
        }

        // 전환 직후 몇 프레임은 밀려난 속도를 이동 입력으로 오해하지 않도록 잘라 냅니다.
        // CharacterController를 다른 콜라이더와 겹친 자리에서 켜면 유니티가 겹침을 푸느라 크게 밀어내는데,
        // 그 이동이 velocity에 잡히고 아래 보간이 현재 속력을 출발점으로 삼기 때문에
        // 걷는 속도의 몇 배가 다음 프레임의 이동 속도가 되어 앞으로 튀어 나갑니다.
        // 실측에서 한 프레임 0.58m(캡슐 반지름 두 개와 표면 두께의 합)를 밀린 뒤 2m 넘게 미끄러졌습니다.
        //
        // 상시로 자르지 않는 이유는 밀려나는 것 자체가 잘못이 아니기 때문입니다.
        // 넉백처럼 외부에서 미는 이동을 넣으면 그때는 최대치를 넘는 것이 정상이고,
        // 상시 클램프는 그것을 조용히 먹어 버립니다. 그래서 전환이 원인일 때만 적용합니다.
        if (m_switchSettleFrames > 0)
        {
            m_switchSettleFrames--;

            float maxSelfSpeed = Mathf.Max(MoveSpeed, SprintSpeed);
            currentHorizontalSpeed = Mathf.Min(currentHorizontalSpeed, maxSelfSpeed);
        }

        float speedOffset = 0.1f;
        float inputMagnitude = m_input.analogMovement ? m_input.move.magnitude : 1f;
        float speedChangeRate = m_combatSpeedTransitionTimer > 0.0f
            ? m_combatSpeedTransitionRate
            : m_speedChangeRate;
        m_combatSpeedTransitionTimer = Mathf.Max(0.0f, m_combatSpeedTransitionTimer - Time.deltaTime);

        if (currentHorizontalSpeed < targetSpeed - speedOffset ||
            currentHorizontalSpeed > targetSpeed + speedOffset)
        {
            m_speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude,
                Time.deltaTime * speedChangeRate);

            m_speed = Mathf.Round(m_speed * 1000f) / 1000f;
        }
        else
        {
            m_speed = targetSpeed;
        }

        m_animationBlend = Mathf.Lerp(m_animationBlend, targetSpeed, Time.deltaTime * speedChangeRate);

        if (m_animationBlend < 0.01f)
        {
            m_animationBlend = 0f;
        }

        ApplyBodyRotation();

        // 이동 방향은 시점 모드와 무관하게 항상 카메라 기준 입력 방향입니다.
        // 백뷰에서 몸이 카메라를 보는 동안에도 이 값이 그대로 쓰이기 때문에 옆·뒤 입력이 게걸음/뒷걸음이 됩니다.
        Vector3 targetDirection = Quaternion.Euler(0.0f, m_targetRotation, 0.0f) * Vector3.forward;

        m_controller.Move(targetDirection.normalized * (m_speed * Time.deltaTime) +
                          new Vector3(0.0f, m_verticalVelocity, 0.0f) * Time.deltaTime);

        if (m_hasAnimator)
        {
            m_animator.SetFloat(m_animIDSpeed, m_animationBlend);
            m_animator.SetFloat(m_animIDMotionSpeed, inputMagnitude);
        }

        UpdateLocomotionAnimator();
    }

    /// <summary>
    /// 직접 조작 중인 멤버가 AI 동료와 진행 방향으로 부딪히면, 동료에게 NavMesh 기반 길 양보를 요청합니다.
    /// </summary>
    /// <remarks>
    /// CharacterController는 Rigidbody 질량을 밀어 주지 않으므로, 충돌 처리에서 힘을 더하지 않습니다.
    /// 동료가 유효한 NavMesh 후보를 찾았을 때만 자신의 AI가 이동하게 하며 벽/다운 동료에는 적용하지 않습니다.
    /// </remarks>
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (m_input == null || m_input.move.magnitude < SquadYieldMinimumInput ||
            Time.time < m_nextSquadYieldRequestTime || hit.collider == null)
        {
            return;
        }

        SquadMemberController member = hit.collider.GetComponentInParent<SquadMemberController>();
        if (member == null || member.gameObject == gameObject)
        {
            return;
        }

        Vector3 moveDirection = hit.moveDirection;
        moveDirection.y = 0.0f;
        if (moveDirection.sqrMagnitude <= Threshold * Threshold)
        {
            return;
        }

        // 진행 반대편 면에 실제로 밀고 있을 때만 요청합니다. 옆면을 스치거나 뒤로 물러날 때는 양보시키지 않습니다.
        if (Vector3.Dot(moveDirection.normalized, -hit.normal) < 0.25f)
        {
            return;
        }

        if (!member.CanYieldToPlayer)
        {
            return;
        }

        member.TryBeginPlayerYield(transform.position, moveDirection);
        m_nextSquadYieldRequestTime = Time.time + SquadYieldRequestInterval;
    }

    /// <summary>
    /// WA→WD처럼 서로 이웃한 대각선 입력끼리의 전환인지 판정합니다.
    /// </summary>
    /// <remarks>
    /// 양쪽 입력이 모두 대각선이어도 WA→SD처럼 정반대인 경우는 제외합니다. 이 예외를 현재 헤딩이 아니라
    /// 원시 입력으로 판정해야 90° 회전의 중간 지점에서도 보간이 끊기지 않습니다.
    /// </remarks>
    private static bool IsNeighboringDiagonalTransition(Vector2 previousInput, Vector2 currentInput)
    {
        if (!IsDiagonalMoveInput(previousInput) || !IsDiagonalMoveInput(currentInput))
        {
            return false;
        }

        float previousAngle = Mathf.Atan2(previousInput.x, previousInput.y) * Mathf.Rad2Deg;
        float currentAngle = Mathf.Atan2(currentInput.x, currentInput.y) * Mathf.Rad2Deg;
        return Mathf.Abs(Mathf.DeltaAngle(previousAngle, currentAngle)) <= DiagonalTransitionMaxAngle;
    }

    /// <summary>이동 입력이 8방향 기준의 대각선 영역에 있는지 판정합니다.</summary>
    private static bool IsDiagonalMoveInput(Vector2 input)
    {
        if (input.sqrMagnitude <= MoveDirectionThreshold * MoveDirectionThreshold)
        {
            return false;
        }

        Vector2 direction = input.normalized;
        return Mathf.Abs(direction.x) >= DiagonalInputAxisThreshold &&
               Mathf.Abs(direction.y) >= DiagonalInputAxisThreshold;
    }

    /// <summary>
    /// 지금 이동 방향으로 전력질주할 수 있는지 판정합니다.
    /// </summary>
    /// <returns>이동 방향이 몸 기준 전방 부채꼴 안이면 true입니다.</returns>
    /// <remarks>
    /// 달리기 클립이 전방 세 방향(정면, 좌우 45도)뿐입니다. 뒤나 옆으로 달리는 동작은 기획상 없는 것이라
    /// 없는 방향으로 달릴 수 있게 두면 정면 달리기 모션인 채로 옆으로 미끄러집니다.
    ///
    /// 시작 각도와 유지 각도를 다르게 두는 이유는 대각선 입력이 정확히 경계에 놓이기 때문입니다.
    /// 45도 하나로 판정하면 대각선 달리기가 늘 경계에 걸려, 몸이 카메라를 따라 미세하게 흔들릴 때마다
    /// 달리기와 걷기가 번갈아 켜집니다. 실측에서 대각선 달리기의 끊김으로 드러났습니다.
    ///
    /// 판정 기준을 실제 속도가 아니라 입력 방향으로 잡습니다. 속도를 보면 이 판정이 속도를 정하고
    /// 그 속도가 다시 판정에 들어오는 되먹임이 생깁니다.
    ///
    /// 자유 시점에서는 몸이 이동 방향으로 돌아가므로 각도 차가 곧 0이 되어 사실상 항상 허용됩니다.
    /// 제한이 실제로 걸리는 것은 몸이 카메라에 묶여 있는 백뷰입니다.
    /// </remarks>
    private bool EvaluateSprintForward()
    {
        if (m_input.move == Vector2.zero)
        {
            return false;
        }

        float angle = Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, m_targetRotation));
        float limit = m_canSprintForward
            ? m_sprintForwardAngle + m_sprintReleaseMargin
            : m_sprintForwardAngle;

        return angle <= limit;
    }

    /// <summary>
    /// 8방향 이동 블렌드와 조준/웅크림 분기에 필요한 애니메이터 파라미터를 갱신합니다.
    /// </summary>
    /// <remarks>
    /// 이동 방향은 실제 속도를 몸 기준으로 바꿔 넣습니다. 감염체(<see cref="EnemyController"/>)와 같은 규약입니다.
    /// 월드 방향을 그대로 쓰면 몸이 어디를 보든 같은 값이 되어 옆걸음과 앞걸음을 구분하지 못합니다.
    /// 입력이 아니라 속도를 쓰기 때문에 벽에 막히면 값이 0이 되어 제자리걸음이 멈춥니다.
    ///
    /// 크기는 버리고 방향만 넣습니다. 걷기/달리기 구분은 <c>MoveSpeed</c>가 담당합니다.
    /// 블렌드 트리가 반지름으로 속도를 구분하는 형태라면 여기서 정규화하지 말고 속력을 함께 실어야 합니다.
    /// </remarks>
    /// <summary>
    /// 애니메이터에 넣을 이동 방향을 갱신합니다. 이동 중에는 각도만 돌리고 크기는 1로 유지합니다.
    /// </summary>
    /// <param name="desired">몸 기준 목표 이동 방향입니다. 정지 중이면 값이 의미 없습니다.</param>
    /// <param name="moving">지금 실제로 이동 중인지 여부입니다.</param>
    private void UpdateSmoothedMoveDirection(Vector2 desired, bool moving)
    {
        // 방향(각도)과 세기(크기)를 나눠서 다룹니다. 각도는 어느 클립을 쓸지, 크기는 정지 클립과
        // 그 방향 클립을 얼마나 섞을지를 정합니다. 둘을 한 벡터로 뭉쳐 보간하면 방향을 바꾸는 것만으로
        // 크기가 함께 줄었다 늘어 정지 자세가 끼어듭니다.
        float magnitude = m_moveDirectionSmoothed.magnitude;
        Vector2 direction = magnitude > 0.0001f
            ? m_moveDirectionSmoothed / magnitude
            : Vector2.zero;

        float magnitudeStep = m_moveStartStopDuration <= 0.0f
            ? 1.0f
            : Time.deltaTime / m_moveStartStopDuration;

        magnitude = Mathf.MoveTowards(magnitude, moving ? 1.0f : 0.0f, magnitudeStep);

        if (!moving)
        {
            // 멈출 때는 마지막 방향을 유지한 채 세기만 줄여 정지 자세로 잦아듭니다.
            m_moveDirectionSmoothed = direction * magnitude;
            return;
        }

        if (direction == Vector2.zero)
        {
            // 정지에서 막 출발하는 프레임에는 돌릴 기준 방향이 없습니다. 방향은 바로 잡되
            // 세기는 위에서 이미 0부터 오르고 있으므로 정지 자세에서 서서히 걸어 나갑니다.
            m_moveDirectionSmoothed = desired.normalized * magnitude;
            return;
        }

        float angle = Vector2.SignedAngle(direction, desired);

        // 크게 꺾는 것은 방향 조정이 아니라 방향 교체입니다. 좌에서 우로 바꾸는 것이 그렇습니다.
        // 이때 천천히 돌리면 앞이나 뒤 걷기를 거쳐 가느라 중간에 엉뚱한 모션이 눈에 띄고,
        // 한 프레임에 끊어 붙이면 툭 끊겨 보입니다. 그래서 값을 갈아끼우지 않고 아주 빠르게 돌립니다.
        // 몇 프레임이라도 블렌드가 남아 있어야 발이 이어져 보입니다.
        float turnDuration = Mathf.Abs(angle) >= m_moveDirectionSnapAngle
            ? m_moveDirectionFlipDuration
            : m_moveDirectionDamp;

        float turnStep = turnDuration <= 0.0f ? 1.0f : Time.deltaTime / turnDuration;

        float maxDegrees = 180.0f * turnStep;
        float delta = Mathf.Clamp(angle, -maxDegrees, maxDegrees);

        float radians = delta * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);

        Vector2 rotated = new Vector2(
            direction.x * cos - direction.y * sin,
            direction.x * sin + direction.y * cos);

        m_moveDirectionSmoothed = rotated.normalized * magnitude;
    }

    private void UpdateLocomotionAnimator()
    {
        if (!m_hasAnimator)
        {
            return;
        }

        Vector3 velocity = m_controller.velocity;
        velocity.y = 0.0f;

        float horizontalSpeed = velocity.magnitude;
        bool moving = horizontalSpeed > MoveDirectionThreshold;

        // 방향은 감쇠해서 넣습니다. 입력이 바뀌는 순간 몸 기준 방향이 45도씩 튀는데,
        // 그대로 넣으면 블렌드 가중치가 한 프레임에 갈아타면서 발걸음이 끊깁니다.
        Vector3 local = moving
            ? transform.InverseTransformDirection(velocity / horizontalSpeed)
            : Vector3.zero;

        UpdateSmoothedMoveDirection(new Vector2(local.x, local.z), moving);

        m_animator.SetFloat(m_animIDMoveX, m_moveDirectionSmoothed.x);
        m_animator.SetFloat(m_animIDMoveZ, m_moveDirectionSmoothed.y);

        m_animator.SetBool(m_animIDIsMove, moving);
        m_animator.SetFloat(m_animIDMoveState, UpdateMoveState());
        m_animator.SetBool(m_animIDCrouch, IsCrouchActive);

        // IsAim은 ADS만이 아니라 전투 자세 전체입니다. 힙파이어로 쏘는 동안 상체가 총을 내리고 있으면
        // 총알은 나가는데 조준 포즈가 없는 구간이 생깁니다. 백뷰/전력질주 잠금과도 같은 기준입니다.
        //
        // 재장전도 포함하는 이유는 하체 때문입니다. 재장전은 전투 자세를 벗어나므로 이 값이 내려가는데,
        // 그러면 Base Layer가 조준 이동 블렌드에서 비조준 이동 블렌드로 통째로 갈아탑니다. 게걸음 클립이
        // 재장전을 시작하고 끝낼 때마다 바뀌어 걸음걸이가 흔들려 보였습니다.
        // 상체는 어차피 재장전 클립이 덮으므로, 하체만 조준 보행을 유지시킵니다.
        m_animator.SetBool(m_animIDAim, m_isCombatStance || m_isReload);

        UpdateLookAnimator();
    }

    /// <summary>
    /// 이동 단계(웅크림/걷기/달리기)를 목표 값으로 보간하고 현재 값을 돌려줍니다.
    /// </summary>
    /// <returns>이번 프레임에 애니메이터에 넣을 MoveState 값입니다.</returns>
    /// <remarks>
    /// 값을 즉시 바꾸지 않고 보간하는 이유는 걷기와 달리기 사이가 한 단계 차이라 중간값이
    /// 두 동작을 섞은 자연스러운 자세가 되기 때문입니다. 즉시 바꾸면 발걸음이 튑니다.
    ///
    /// 축의 순서를 웅크림 0, 걷기 1, 달리기 2로 잡은 것도 같은 이유입니다. 웅크림과 달리기를 양 끝에 두면
    /// 그 둘이 직접 섞이는 일이 없고, 웅크린 채 달리기를 눌러도 걷기를 지나며 이어집니다.
    ///
    /// 전력질주 판정에 <see cref="CanSprintTowardInput"/>을 함께 보는 이유는 실제 속도와 자세를 맞추기
    /// 위해서입니다. 부채꼴을 벗어난 방향은 걷기 속도로 움직이므로 자세도 걷기여야 합니다.
    ///
    /// 같은 이유로 재장전은 이 판정에서 빠져 있습니다. <see cref="Move"/>가 재장전 중에도 달리기 속도를
    /// 그대로 내주므로, 여기서 걷기로 묶으면 달리는 속도에 걷는 하체가 붙어 발이 미끄러집니다.
    /// </remarks>
    private float UpdateMoveState()
    {
        float target = MoveStateWalk;

        if (IsCrouchActive)
        {
            target = MoveStateCrouch;
        }
        else if (m_input.sprint && !m_isCombatStance && m_canSprintForward)
        {
            target = MoveStateRun;
        }

        float moveStateBlendDuration = MoveStateBlendDuration;
        m_moveState = moveStateBlendDuration <= 0.0f
            ? target
            : Mathf.MoveTowards(m_moveState, target, Time.deltaTime / moveStateBlendDuration);

        return m_moveState;
    }

    /// <summary>
    /// 카메라와 몸의 각도 차를 -1~1로 정규화해 상체 조준 오프셋 파라미터에 넣습니다.
    /// </summary>
    /// <remarks>
    /// 마우스 이동량이 아니라 "몸이 카메라를 얼마나 못 따라갔는지"입니다. 백뷰에서는 몸이 카메라를
    /// 곧바로 따라가므로 0에 가깝고, 자유 시점에서는 크게 벌어집니다.
    ///
    /// 규약: <c>LookX</c>는 -1이 카메라가 몸의 왼쪽, +1이 오른쪽입니다.
    /// <c>LookY</c>는 -1이 최대 아래, +1이 최대 위입니다. StarterAssets 피치는 값이 커질수록 아래를 보므로
    /// 부호를 뒤집습니다. 위아래 한계가 서로 다르므로 각 방향을 자기 한계로 나눠 양 끝이 정확히 ±1이 되게 합니다.
    ///
    /// 반동 오프셋은 넣지 않습니다. 조준 의도만 담아야 발사 중에 상체가 파라미터를 따라 떨지 않습니다.
    /// </remarks>
    private void UpdateLookAnimator()
    {
        float lookX = 0.0f;

        if (m_mainCamera != null)
        {
            float yawDelta = Mathf.DeltaAngle(transform.eulerAngles.y, m_mainCamera.transform.eulerAngles.y);
            lookX = Mathf.Clamp(yawDelta / MaxLookYaw, -1.0f, 1.0f);
        }

        float pitchLimit = Mathf.Abs(m_cinemachineTargetPitch >= 0.0f ? m_topClamp : m_bottomClamp);
        float lookY = pitchLimit <= Mathf.Epsilon
            ? 0.0f
            : -Mathf.Clamp(m_cinemachineTargetPitch / pitchLimit, -1.0f, 1.0f);

        m_animator.SetFloat(m_animIDLookX, lookX);
        m_animator.SetFloat(m_animIDLookY, lookY);
    }

    /// <summary>
    /// 전투 자세와 이동 입력, 정지 경과 시간으로 이번 프레임의 시점 컨텍스트와 시점 모드를 판정합니다.
    /// </summary>
    private void UpdateViewMode()
    {
        bool moving = m_input.move != Vector2.zero;

        if (m_isCombatStance)
        {
            m_idleTimer = 0.0f;
            m_viewContext = ViewContext.Combat;
        }
        else if (moving)
        {
            m_idleTimer = 0.0f;
            m_viewContext = ViewContext.NonCombatMove;
        }
        else
        {
            m_idleTimer += Time.deltaTime;

            // 멈춘 즉시 Idle로 넘기지 않는 이유는, 걷다 서다를 반복할 때 시점이 프레임 단위로 딸깍거리기 때문입니다.
            m_viewContext = m_idleTimer >= m_freeLookIdleDelay
                ? ViewContext.Idle
                : ViewContext.NonCombatMove;
        }

        m_viewMode = ResolveViewMode(m_viewContext);
        ApplyStateCamera();
    }

    /// <summary>
    /// 시점 컨텍스트에 대응하는 시점 모드를 결정합니다.
    /// </summary>
    /// <param name="context">이번 프레임에 판정된 시점 컨텍스트입니다.</param>
    /// <returns>적용할 시점 모드입니다.</returns>
    private CameraViewMode ResolveViewMode(ViewContext context)
    {
        // 전투는 조준과 탄착이 화면 중앙을 기준으로 하므로 설정과 무관하게 백뷰 고정입니다.
        if (context == ViewContext.Combat)
        {
            return CameraViewMode.BackView;
        }

        return context == ViewContext.Idle ? m_idleViewMode : m_nonCombatViewMode;
    }

    /// <summary>
    /// 현재 시점 컨텍스트에 배정된 카메라만 활성으로 남깁니다.
    /// </summary>
    /// <remarks>
    /// 전투 카메라는 <see cref="AimController"/>가 소유하므로 여기서는 건드리지 않습니다.
    /// 한 카메라에 주인이 둘이면 활성 전환과 FOV 제어가 서로 다른 프레임에 갈라집니다.
    ///
    /// 슬롯이 비어 있으면 아무것도 켜지 않습니다. 슬롯에 없는 상시 활성 카메라는 끄지 않으므로
    /// 그 카메라가 자연히 폴백이 됩니다. 두 슬롯에 같은 카메라를 물려도 결과는 같습니다.
    /// </remarks>
    private void ApplyStateCamera()
    {
        CinemachineCamera desired = null;

        if (m_viewContext == ViewContext.Idle)
        {
            desired = m_idleCamera;
        }
        else if (m_viewContext != ViewContext.Combat)
        {
            desired = m_nonCombatCamera;
        }

        SyncCameraActive(m_idleCamera, desired);
        SyncCameraActive(m_nonCombatCamera, desired);
    }

    /// <summary>
    /// 슬롯 카메라의 활성 상태를 이번 프레임의 목표 카메라와 일치시킵니다.
    /// </summary>
    /// <param name="camera">확인할 슬롯 카메라입니다.</param>
    /// <param name="desired">이번 프레임에 활성이어야 할 카메라입니다.</param>
    private static void SyncCameraActive(CinemachineCamera camera, CinemachineCamera desired)
    {
        if (camera == null)
        {
            return;
        }

        bool active = camera == desired;

        if (camera.gameObject.activeSelf != active)
        {
            camera.gameObject.SetActive(active);
        }
    }

    /// <summary>
    /// Returns the speed multiplier for the current local input direction.
    /// Diagonal input continuously blends its forward/backward contribution with the shared strafe value.
    /// </summary>
    private float ResolveDirectionalSpeedMultiplier(Vector2 input)
    {
        Vector2 direction = input.sqrMagnitude > 1.0f ? input.normalized : input;
        float verticalWeight = Mathf.Abs(direction.y);
        float strafeWeight = Mathf.Abs(direction.x);
        float totalWeight = verticalWeight + strafeWeight;
        if (totalWeight <= 0.0001f)
        {
            return 1.0f;
        }

        float verticalMultiplier = direction.y >= 0.0f
            ? m_forwardSpeedMultiplier
            : m_backwardSpeedMultiplier;
        return (verticalMultiplier * verticalWeight + m_strafeSpeedMultiplier * strafeWeight) / totalWeight;
    }

    /// <summary>
    /// 현재 시점 모드에 맞는 목표 yaw로 몸을 회전시킵니다.
    /// </summary>
    /// <remarks>
    /// 자유 시점은 이동 방향(카메라 기준 입력)을, 백뷰는 카메라 정면을 목표로 삼습니다.
    /// 목표만 다르고 보간은 <see cref="Mathf.SmoothDampAngle"/> 하나로 통일했습니다.
    /// 예전에는 백뷰만 <c>Vector3.Lerp(..., Time.deltaTime * 50f)</c>를 썼는데,
    /// 그 형태는 프레임레이트에 따라 수렴 속도가 달라지고 모드가 바뀌는 프레임에 회전 속도가 튑니다.
    ///
    /// 감쇠 시간도 전투 여부가 아니라 시점 모드로 갈립니다. 비전투 백뷰의 카메라 거동이 힙파이어와
    /// 같아야 하므로, 백뷰인 동안은 전투든 아니든 같은 값을 씁니다.
    /// SmoothDamp는 회전 속도를 상태로 들고 있어서 보간 도중 감쇠 시간이 바뀌어도
    /// 각도가 끊기지 않고 가속도만 달라집니다.
    /// </remarks>
    private void ApplyBodyRotation()
    {
        bool backView = m_viewMode == CameraViewMode.BackView;

        // 자유 시점은 이동 입력이 있을 때만 몸을 돌립니다. 백뷰는 제자리에서도 카메라를 따라갑니다.
        if (!backView && m_input.move == Vector2.zero)
        {
            return;
        }

        float goalRotation = backView && m_mainCamera != null
            ? m_mainCamera.transform.eulerAngles.y
            : m_targetRotation;

        float smoothTime = backView ? m_backViewRotationSmoothTime : m_rotationSmoothTime;

        float rotation = Mathf.SmoothDampAngle(
            transform.eulerAngles.y,
            goalRotation,
            ref m_rotationVelocity,
            smoothTime);

        transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
    }

    /// <summary>
    /// 지면 상태에 따라 점프, 낙하, 중력, 관련 애니메이션 파라미터를 갱신합니다.
    /// </summary>
    private void JumpAndGravity()
    {
        if (m_grounded)
        {
            m_fallTimeoutDelta = m_fallTimeout;

            if (m_hasAnimator)
            {
                m_animator.SetBool(m_animIDJump, false);
                m_animator.SetBool(m_animIDFreeFall, false);
            }

            if (m_verticalVelocity < 0.0f)
            {
                m_verticalVelocity = -2f;
            }

            if (m_input.jump && m_jumpTimeoutDelta <= 0.0f)
            {
                SurfaceMaterialType takeoffSurface = ResolveFootstepSurface();
                m_verticalVelocity = Mathf.Sqrt(m_jumpHeight * -2f * m_gravity);
                TryPlaySurfaceFmod(m_jumpEvent, takeoffSurface);

                // 점프하면 앉기를 풉니다. 몸을 띄우는 동작이라 웅크린 자세가 그대로 남을 수 없습니다.
                // 입력을 내려 두므로 공중에서 다시 누르면 착지 웅크림은 그대로 성립합니다
                // (<see cref="UpdateCrouchLandingTransition"/>). 전력질주 취소와 같은 방식입니다.
                m_input.CrouchInput(false);

                if (m_hasAnimator)
                {
                    m_animator.SetBool(m_animIDJump, true);
                }
            }

            if (m_jumpTimeoutDelta >= 0.0f)
            {
                m_jumpTimeoutDelta -= Time.deltaTime;
            }
        }
        else
        {
            m_jumpTimeoutDelta = m_jumpTimeout;

            if (m_fallTimeoutDelta >= 0.0f)
            {
                m_fallTimeoutDelta -= Time.deltaTime;
            }
            else
            {
                if (m_hasAnimator)
                {
                    m_animator.SetBool(m_animIDFreeFall, true);
                }
            }

            m_input.jump = false;
        }

        if (m_verticalVelocity < m_terminalVelocity)
        {
            m_verticalVelocity += m_gravity * Time.deltaTime;
        }
    }

    /// <summary>
    /// 각도를 -360도에서 360도 범위로 정규화한 뒤 지정 범위로 제한합니다.
    /// </summary>
    /// <param name="lfAngle">제한할 각도입니다.</param>
    /// <param name="lfMin">최소 각도입니다.</param>
    /// <param name="lfMax">최대 각도입니다.</param>
    /// <returns>지정 범위로 제한된 각도입니다.</returns>
    private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
    {
        if (lfAngle < -360f) lfAngle += 360f;
        if (lfAngle > 360f) lfAngle -= 360f;
        return Mathf.Clamp(lfAngle, lfMin, lfMax);
    }

    /// <summary>
    /// 에디터에서 선택되었을 때 지면 감지 구체를 기즈모로 표시합니다.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!m_debugDrawGroundCheck)
        {
            return;
        }

        Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
        Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

        Gizmos.color = m_grounded ? transparentGreen : transparentRed;

        Gizmos.DrawSphere(
            new Vector3(transform.position.x, transform.position.y - m_groundedOffset, transform.position.z),
            m_groundedRadius);
    }

    /// <summary>
    /// 발걸음 애니메이션 이벤트에서 FMOD 발소리를 우선 재생합니다.
    /// </summary>
    /// <param name="animationEvent">애니메이션 이벤트 정보입니다.</param>
    private void OnFootstep(AnimationEvent animationEvent)
    {
        if (animationEvent.animatorClipInfo.weight <= 0.5f)
        {
            return;
        }

        if (TryPlayFootstepFmod())
        {
            return;
        }

        if (m_footstepAudioClips != null && m_footstepAudioClips.Length > 0)
        {
            int index = Random.Range(0, m_footstepAudioClips.Length);
            AudioSource.PlayClipAtPoint(m_footstepAudioClips[index], transform.TransformPoint(m_controller.center), m_footstepAudioVolume);
        }
    }

    /// <summary>현재 자세에 맞는 FMOD 발소리 one-shot을 재생합니다.</summary>
    private bool TryPlayFootstepFmod()
    {
        FMODUnity.EventReference selectedEvent = IsCrouchActive
            ? m_footstepEvent
            : m_runFootstepEvent;

        return TryPlaySurfaceFmod(selectedEvent, ResolveFootstepSurface());
    }

    /// <summary>지정한 FMOD 이벤트에 현재 신발과 표면을 전달해 one-shot으로 재생합니다.</summary>
    /// <param name="eventReference">재생할 FMOD 이벤트입니다.</param>
    /// <param name="surfaceType">이 동작이 발생한 표면 재질입니다.</param>
    private bool TryPlaySurfaceFmod(
        FMODUnity.EventReference eventReference,
        SurfaceMaterialType surfaceType)
    {
        if (eventReference.IsNull || !FMODUnity.RuntimeManager.IsInitialized)
        {
            return false;
        }

        try
        {
            FMOD.Studio.EventInstance instance = FMODUnity.RuntimeManager.CreateInstance(eventReference);
            if (!instance.isValid())
            {
                return false;
            }

            FMODUnity.RuntimeManager.AttachInstanceToGameObject(instance, gameObject);
            instance.setParameterByNameWithLabel(FootwearFmodParameter, m_footwearType.ToString());
            instance.setParameterByNameWithLabel(SurfaceFmodParameter, surfaceType.ToString());
            instance.setVolume(m_footstepAudioVolume);
            instance.start();
            instance.release();
            return true;
        }
        catch (FMODUnity.EventNotFoundException exception)
        {
            if (!m_loggedMissingMovementFmodEvent)
            {
                Debug.LogWarning($"[ThirdPersonController] FMOD 이동 효과음 이벤트를 찾지 못했습니다: {exception.Message}", this);
                m_loggedMissingMovementFmodEvent = true;
            }

            return false;
        }
    }

    /// <summary>캐릭터 중심 아래에서 현재 발밑 표면 재질을 찾습니다.</summary>
    /// <returns>표면 태그를 찾지 못하면 <see cref="SurfaceMaterialType.Unknown"/>입니다.</returns>
    private SurfaceMaterialType ResolveFootstepSurface()
    {
        Vector3 groundedCenter = new Vector3(
            transform.position.x,
            transform.position.y - m_groundedOffset,
            transform.position.z);
        Vector3 origin = groundedCenter + Vector3.up * (m_groundedRadius + 0.1f);
        float distance = m_groundedRadius * 2.0f + 0.2f;

        if (!Physics.Raycast(
                origin,
                Vector3.down,
                out RaycastHit hit,
                distance,
                m_groundLayers,
                QueryTriggerInteraction.Ignore))
        {
            return SurfaceMaterialType.Unknown;
        }

        return SurfaceMaterialTag.Resolve(hit.collider);
    }

    /// <summary>
    /// 착지 애니메이션 이벤트에서 착지 효과음을 재생합니다.
    /// </summary>
    /// <param name="animationEvent">애니메이션 이벤트 정보입니다.</param>
    private void OnLand(AnimationEvent animationEvent)
    {
        if (animationEvent.animatorClipInfo.weight > 0.5f && m_grounded)
        {
            PlayLandingSound();
        }
    }

    /// <summary>한 번의 지면 접촉마다 FMOD 착지음 또는 기존 AudioClip을 한 번만 재생합니다.</summary>
    private void PlayLandingSound()
    {
        if (m_landingSoundPlayedForCurrentGroundContact)
        {
            return;
        }

        m_landingSoundPlayedForCurrentGroundContact = true;

        if (TryPlaySurfaceFmod(m_landEvent, ResolveFootstepSurface()))
        {
            return;
        }

        if (m_landingAudioClip != null)
        {
            AudioSource.PlayClipAtPoint(
                m_landingAudioClip,
                transform.TransformPoint(m_controller.center),
                m_footstepAudioVolume);
        }
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
