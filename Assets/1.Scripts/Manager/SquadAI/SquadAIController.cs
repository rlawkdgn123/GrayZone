using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;
using VInspector;

/// <summary>
/// AI가 조작하는 스쿼드 멤버의 행동을 판단하는 컴포넌트입니다.
/// </summary>
/// <remarks>
/// <see cref="PlayerInputController"/>의 대칭 짝입니다. 사람 입력이 그쪽으로 들어오듯 AI 판단이 이쪽으로 들어오며,
/// 조종 주체만 다르고 아래 계층(행동 판정과 실행)은 같은 것을 씁니다.
/// 어느 쪽이 활성인지는 <see cref="SquadMemberController"/>가 정합니다.
/// <para>
/// <b>목표 경계</b>: 이 컴포넌트는 "무엇을 할지"만 정하고 캐릭터를 직접 움직이지 않습니다.
/// 실제 이동·회전·애니메이션 실행은 <see cref="ThirdPersonController"/>가 담당합니다.
/// </para>
/// <para>
/// <b>현재는 예외</b>: 아직 이동 실행 통합 전이라 여기서 <see cref="NavMeshAgent"/>로 직접 이동하고
/// 회전과 애니메이터 파라미터까지 갱신합니다. 이 부분은 이동 통합 트랙에서 걷어낼 임시 코드이며,
/// 그때 NavMesh는 경로 계산 전용으로 남고 실행은 <see cref="ThirdPersonController"/>로 넘어갑니다.
/// </para>
/// 기획 용어로는 `추종`이 아니라 `동행`과 `합류`를 씁니다(공용 문서 `스쿼드 AI 시스템` §5, §12).
/// </remarks>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(SquadMemberController))]
public class SquadAIController : MonoBehaviour
{
    /// <summary>
    /// 멤버 전환 시 이어받을 AI 추종 상태입니다.
    /// </summary>
    public struct FollowCarryoverState
    {
        public bool HasState;
        public float NextUpdateDelay;
        public bool AgentEnabled;
        public bool IsStopped;
        public bool HasPath;
        public Vector3 Position;
        public Vector3 Destination;
        public Vector3 Velocity;

        /// <summary>이 자리의 AI가 받아 둔 이동·사수 명령(Q/E)이 있는지입니다.</summary>
        /// <remarks>전환하면 위치를 맞바꾸므로, 그 자리를 이어받는 멤버가 명령도 이어받아야 지키던 자리가 풀리지 않습니다.</remarks>
        public bool HasMoveOrder;

        /// <summary>이어받을 사수 위치입니다.</summary>
        public Vector3 MoveOrderPosition;
    }

    [Foldout("Follow Options")]
    [Tooltip("플레이어까지 경로상 거리가 이 값을 넘으면 합류를 시작하고 달립니다. 완료 거리와 충분히 벌려야 걷는 구간이 생깁니다.")]
    [FormerlySerializedAs("followDistance")]
    [SerializeField] private float m_joinStartDistance = 7.0f;

    [Tooltip("플레이어까지 경로상 거리가 이 값 이하로 들어오면 합류를 끝내고 달리기를 멈춥니다. 합류 목적지도 이 반경 안에서 고릅니다.")]
    [FormerlySerializedAs("stopDistance")]
    [SerializeField] private float m_joinCompleteDistance = 2.0f;

    [Tooltip("합류가 아닌 평상시 동행 속도를 플레이어 걷기 속도의 몇 배로 할지입니다. 1보다 커야 걷는 플레이어를 따라잡아 거리가 유지됩니다.")]
    [SerializeField] private float m_followSpeedMultiplier = 1.15f;

    [Tooltip("목표 위치를 다시 계산하는 주기입니다.")]
    [FormerlySerializedAs("updateInterval")]
    [SerializeField] private float m_updateInterval = 0.15f;

    [Tooltip("합류 목적지 후보를 플레이어 주위 몇 방향에서 뽑을지입니다. 고정 자리가 아니라 매번 가장 가까운 빈 자리를 고르기 위한 표본 수입니다.")]
    [SerializeField] private int m_destinationCandidateCount = 8;

    [Tooltip("합류 중 이 시간(초) 동안 경로가 없거나 이동 진척이 없으면 경로를 다시 탐색합니다. 재장전이나 구조처럼 정상적으로 이동하지 못하는 시간은 세지 않습니다. 기획 미확정 - 임시값입니다.")]
    [SerializeField] private float m_joinStallTime = 1.5f;

    [Tooltip("합류 중 이 거리(m)만큼도 스스로 움직이지 못했으면 이동 진척이 없다고 봅니다. 너무 작게 두면 제자리 흔들림을 진척으로 오인합니다. 기획 미확정 - 임시값입니다.")]
    [SerializeField] private float m_joinProgressEpsilon = 0.5f;

    [Tooltip("합류 실패를 확정하기 전에 경로를 다시 탐색할 횟수입니다. 이 횟수를 넘겨도 회복되지 않으면 재배치로 넘어갑니다. 기획 미확정 - 임시값입니다.")]
    [SerializeField] private int m_joinRepathLimit = 3;

    // AI 팀원끼리 유지할 간격은 SquadManager.AiMemberSpacing이 소유합니다. 스쿼드 전체에 걸리는
    // 편성 규칙이라 멤버마다 사본을 두면 값이 어긋나고, 나중에 합류한 멤버만 옛 값을 씁니다.

    [Tooltip("동행 AI의 회피 우선순위 기준값입니다. 멤버 순번을 더해 서로 다른 값을 씁니다. 낮을수록 우선합니다.")]
    [SerializeField] private int m_baseAvoidancePriority = 50;

    [Foldout("Follow Options")]
    [Tooltip("직접 조작 중인 플레이어가 길을 막고 있는 동료 AI에 닿았을 때, 동료가 옆 또는 뒤로 비켜날 거리(m)입니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_playerYieldDistance = 0.9f;

    [Tooltip("플레이어에게 길을 양보하는 상태를 유지할 최대 시간(초)입니다. 목적지에 먼저 닿으면 즉시 원래 AI 로직으로 돌아갑니다.")]
    [Min(0.05f)]
    [SerializeField] private float m_playerYieldDuration = 0.55f;

    [Tooltip("플레이어 충돌로 길 양보를 다시 요청할 수 있는 최소 간격(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_playerYieldRequestCooldown = 0.2f;

    [Tooltip("길 양보 후보 위치를 NavMesh로 보정할 때 허용하는 반경(m)입니다.")]
    [Min(0.05f)]
    [SerializeField] private float m_playerYieldSampleRadius = 0.45f;

    [Tooltip("길을 양보할 때 사용하는 최소 이동 속도(m/s)입니다. 원래 추종 속도가 더 빠르면 그 값을 유지합니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_playerYieldSpeed = 3.5f;

    [Foldout("Order Options")]
    [Tooltip("플레이어가 지정한 사수 위치까지 이 거리(m) 안에 들어오면 도착한 것으로 보고 멈춥니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_moveOrderArriveDistance = 0.6f;

    [Foldout("Sight Options")]
    [Tooltip("이 AI가 적을 직접 확인할 수 있는 최대 거리입니다.")]
    [SerializeField] private float m_sightRange = 20.0f;

    [Tooltip("전방을 기준으로 하는 전체 시야각입니다. 변이체 쪽 시야각과 같은 방식으로 씁니다.")]
    [SerializeField] private float m_sightAngle = 120.0f;

    [Tooltip("시야 판정의 시작점입니다. 비어 있으면 발밑에서 눈 높이만큼 올린 지점을 씁니다.")]
    [SerializeField] private Transform m_eyePoint;

    [Tooltip("시야 판정 시작점의 기본 높이입니다. Eye Point가 비어 있을 때만 씁니다.")]
    [SerializeField] private float m_eyeHeight = 1.6f;

    [Foldout("Targeting Options")]
    [Tooltip("후보 전체를 다시 비교하는 주기입니다. 짧으면 대상이 자주 바뀌고, 길면 상황 변화에 늦게 반응합니다.")]
    [SerializeField] private float m_reevaluateInterval = 0.5f;

    [Tooltip("적이 안 보이게 돼도 현재 대상으로 붙잡아 두는 시간입니다. 이 동안은 마지막 확인 위치를 겨누되 쏘지는 않습니다.")]
    [SerializeField] private float m_targetHoldGrace = 5.0f;

    [Tooltip("거리 위협도의 최대값입니다. 적이 붙어 있을수록 이 값에 가까워집니다.")]
    [SerializeField] private float m_distanceThreatWeight = 10.0f;

    [Tooltip("거리 위협도가 0이 되는 거리입니다. 이보다 멀면 거리로는 점수를 얻지 못합니다.")]
    [SerializeField] private float m_distanceThreatFalloff = 30.0f;

    [Tooltip("현재 대상에게만 얹는 유지 보정입니다. 클수록 대상을 잘 바꾸지 않습니다.")]
    [SerializeField] private float m_currentTargetBonus = 3.0f;

    [Tooltip("받은 피해 1당 위협도 환산값입니다. 나를 때린 적을 우선하게 만드는 값입니다.")]
    [SerializeField] private float m_damageThreatPerPoint = 0.5f;

    [Tooltip("피해 위협도가 초당 줄어드는 양입니다. 맞은 지 오래되면 다시 거리 위주로 판단합니다.")]
    [SerializeField] private float m_damageThreatDecayPerSecond = 1.0f;

    [Tooltip("사격선 가림이 이 시간을 넘으면 지속적인 가림으로 보고 후보군을 낮춥니다. 짧으면 동료가 스칠 때마다 대상이 바뀝니다.")]
    [SerializeField] private float m_sustainedBlockDuration = 1.0f;

    [Tooltip("아군이 사격선에서 이 거리 안에 있으면 가린 것으로 봅니다. 크면 동료 근처만 가도 사격을 멈춥니다.")]
    [SerializeField] private float m_allyBlockRadius = 0.6f;

    [Tooltip("새 대상을 잡고 실제로 쏘기까지 기다리는 시간입니다. AI가 즉발로 반응하지 않게 만드는 값입니다.")]
    [SerializeField] private float m_reactionTime = 0.35f;

    [Foldout("Aim Options")]
    [Tooltip("대상을 향해 몸을 돌리는 속도입니다. 이동 방향 회전 속도와 따로 둡니다.")]
    [SerializeField] private float m_aimRotationSpeed = 12.0f;

    [Tooltip("대상 방향과 몸 방향의 각도 차가 이 값 이하이면 조준이 맞았다고 봅니다.")]
    [SerializeField] private float m_aimToleranceAngle = 8.0f;

    [Foldout("Fire Options")]
    [Tooltip("한 번 사격을 시작하면 이 시간 동안 계속 쏩니다. 무기의 점사 모드가 아니라 AI의 사격 제어입니다.")]
    [SerializeField] private float m_burstDuration = 0.8f;

    [Tooltip("사격 구간이 끝난 뒤 쉬는 시간입니다. 이 동안 조준은 유지하고 발사만 멈춥니다.")]
    [SerializeField] private float m_burstRestDuration = 0.5f;

    [Tooltip("겨눈 지점에서 좌우로 벌어지는 조준 오차입니다. AI가 기계처럼 정확하지 않게 만드는 값입니다.")]
    [SerializeField] private float m_aimErrorRadius = 0.35f;

    [Tooltip("탄창 잔탄이 이 값 이하일 때 전술 재장전을 검토합니다. 0이면 탄창이 완전히 빌 때만 재장전합니다.")]
    [SerializeField] private int m_tacticalReloadThreshold = 0;

    [Foldout("Combat Position Options")]
    [Tooltip("현재 대상이 이 거리 안으로 붙으면 뒤로 물러나며 사격합니다. 물러날 자리가 없으면 제자리에서 쏩니다.")]
    [SerializeField] private float m_keepAwayDistance = 3.0f;

    [Tooltip("한 번 물러날 때 목표로 삼는 거리입니다. 너무 크면 플레이어에게서 떨어져 곧 합류로 넘어갑니다.")]
    [SerializeField] private float m_retreatStep = 2.5f;

    [Tooltip("전투 위치를 다시 찾는 주기입니다. 짧으면 사격선이 잠깐 막힐 때마다 자리를 옮겨 산만해집니다.")]
    [SerializeField] private float m_combatPositionInterval = 0.4f;

    [Foldout("Rescue Options")]
    [Tooltip("자동 구조를 시작할 최대 거리입니다. 이보다 먼 다운 아군은 검토하지 않습니다. 0 이하면 거리 제한이 없습니다.")]
    [SerializeField] private float m_rescueSearchRange = 25.0f;

    [Tooltip("구조 대상에게 이 거리 안으로 들어오면 멈춰 서서 기립시킵니다. 상호작용 사거리와 맞춰야 합니다.")]
    [SerializeField] private float m_rescueReachDistance = 1.8f;

    [Tooltip("접근 경로가 없어 구조가 취소되면 이 시간 동안 같은 대상을 다시 고르지 않습니다. 밸런스 영역입니다(§19).")]
    [SerializeField] private float m_rescueRetryInterval = 5.0f;

    [Foldout("Move Options")]
    [Tooltip("이동 방향으로 회전하는 속도입니다.")]
    [FormerlySerializedAs("rotationSpeed")]
    [SerializeField] private float m_rotationSpeed = 10f;

    [Tooltip("걷기와 달리기 자세를 오가는 데 걸리는 시간입니다. ThirdPersonController의 같은 값과 맞춰야 조작 전환 시 자세가 튀지 않습니다.")]
    [SerializeField] private float m_moveStateBlendDuration = 0.2f;

    // 발사 허용 여부는 SquadManager.AiFiringAllowed가 소유합니다. 스쿼드 전체 설정이라 조작 캐릭터를
    // 전환해도 유지돼야 하고, 멤버마다 사본을 두면 전환·구조 복귀 때 누가 옛 값을 쥐는지가 갈립니다.

    [Foldout("Debug")]
    [Tooltip("이 동행 AI를 선택했을 때 합류 시작 거리와 합류 완료 거리를 리더 기준 원 두 개로 표시합니다. 리더가 없으면 그리지 않습니다.")]
    [SerializeField] private bool m_debugDrawJoinDistances = false;

    [Tooltip("합류 실패가 확정되면 플레이어 근처 유효 위치로 즉시 재배치합니다. 연출이 없어 순간이동으로 보이므로 끌 수 있게 두었습니다. 끄면 판정과 경고만 남고 위치는 보정하지 않아, 경로가 끊긴 팀원이 그 자리에 그대로 남습니다.")]
    [SerializeField] private bool m_repositionOnJoinFailure = true;

    [Tooltip("플레이어까지 NavMesh 경로가 없을 때, 플레이어 발밑이 이 레이어(기본 Environment, Prop)면 재배치하지 않습니다. 플레이어가 차량·상자 같은 지형 오브젝트나 소품 위에 올라가 있어 경로가 끊긴 경우입니다. 그동안은 경로만 다시 찾고, 플레이어가 내려오면 평소처럼 따라갑니다.")]
    [SerializeField] private LayerMask m_noRepositionLeaderGroundLayers = (1 << 6) | (1 << 7);

    /// <summary>플레이어 발밑 지면을 찾을 때 발 위에서 시작하는 높이(m)입니다.</summary>
    private const float LeaderGroundProbeUp = 0.3f;

    /// <summary>플레이어 발밑 지면을 찾을 때 아래로 보는 거리(m)입니다.</summary>
    private const float LeaderGroundProbeDistance = 0.8f;

    private SquadMemberController m_memberController;
    private NavMeshAgent m_agent;
    private SquadManager m_squadManager;
    private Animator m_animator;

    private float m_nextUpdateTime;
    private bool m_hasRequiredReferences;

    // §17 합류 실패 판정 상태입니다.
    //
    // 진척을 <b>자기가 실제로 움직인 거리</b>로 잽니다. 기준 위치에서 m_joinProgressEpsilon 이상
    // 벗어나면 진척입니다.
    //
    // 처음에는 "플레이어까지의 경로상 거리가 줄었는가"로 뒀다가 뒤집었습니다. 그 방식은
    // <b>플레이어가 달려서 멀어지는 동안 경로 거리가 줄지 않으므로, 정상적으로 따라가는 중인 AI를
    // 정체로 셉니다.</b> 그대로 두면 플레이어가 계속 앞서 달릴 때 팀원이 순간이동으로 따라붙는데,
    // 그것이 바로 §17이 "합류 실패를 복구하는 예외 처리로만 사용한다"며 금지한 빠른 재집결입니다.
    //
    // 뒤집기 전 근거였던 "속도로 보면 벽에 비비는 동안에도 진척으로 오인한다"는 이 방식에는
    // 걸리지 않습니다. 보는 것이 속도가 아니라 <b>기준점 대비 실제 변위</b>라서, 제자리에서 비비면
    // 속도는 남아도 변위가 쌓이지 않기 때문입니다.
    private float m_joinStallTimer;
    private Vector3 m_joinProgressPosition;
    private bool m_hasJoinProgressPosition;

    // 누적 간격을 재기 위한 직전 확인 시각입니다. UpdateJoinRecovery는 매 프레임이 아니라
    // UpdateJoinState 안에서 m_updateInterval 주기로만 돌므로 Time.deltaTime을 더하면 안 됩니다.
    // 0.15초에 한 번 호출되면서 한 프레임분(60fps면 약 0.0167초)만 더하면 약 9배 느리게 쌓여,
    // m_joinStallTime 1.5초가 실제로는 13초가 넘습니다.
    private float m_lastJoinCheckTime = -1.0f;

    private int m_joinRepathCount;
    private bool m_joinFailed;

    // 합류 중인지입니다. 시작 거리에서 켜고 완료 거리에서 끄는 히스테리시스의 한쪽입니다(§6.2).
    // 하나의 거리로 판정하면 경계에서 합류 시작과 종료가 매 갱신마다 뒤집힙니다.
    private bool m_isJoining;

    // 마지막으로 확정한 동행 위치입니다. 갱신 주기 사이에도 도착 판정에 쓰므로 들고 있습니다.
    private Vector3 m_destination;
    private bool m_hasDestination;

    // 플레이어까지의 경로상 거리입니다. 직선거리를 쓰면 벽 하나 사이에 두고 "가깝다"고 오판합니다(§6.2).
    private float m_pathDistanceToLeader = -1.0f;

    /// <summary>
    /// NavMesh 조회에 쓰는 필터입니다. 이 멤버 NavMeshAgent의 에이전트 종류로 조회합니다.
    /// </summary>
    /// <remarks>
    /// 영역 값만 넘기는 조회는 에이전트 종류를 가리지 않아, 경로 안내용 PathGuide NavMesh가 씬에 같이 있으면
    /// 그쪽 메시에서 경로를 찾다가 PathPartial이 나옵니다. 그러면 플레이어 바로 옆에서도 경로가 없다고 보고
    /// 합류 실패로 쌓여 약 6초마다 재배치(순간이동)됩니다(실측).
    /// </remarks>
    private NavMeshQueryFilter NavFilter => new NavMeshQueryFilter
    {
        agentTypeID = m_agent != null ? m_agent.agentTypeID : 0,
        areaMask = NavMesh.AllAreas,
    };

    // 경로 계산 버퍼입니다. 매번 새로 만들면 갱신마다 할당이 생깁니다.
    private NavMeshPath m_pathBuffer;

    // 리더가 바뀔 때만 다시 찾습니다. 속도 기준을 리더에게서 읽기 위한 캐시입니다.
    private Transform m_cachedLeaderTransform;
    private ThirdPersonController m_cachedLeaderController;

    // 지금 달려서 따라잡는 중인지입니다. 이동 속도와 자세(MoveState)가 같은 값을 봐야 어긋나지 않습니다.
    private bool m_isSprinting;

    // 애니메이터에 넣고 있는 MoveState 값입니다. 목표 단계로 보간합니다.
    private float m_moveState = MoveStateJog;

    // 현재 대상 판단입니다. 이동과 별개 축이라 따로 둡니다(§9).
    private readonly SquadAITargeting m_targeting = new SquadAITargeting();

    // 이번 프레임에 무엇을 요청할지 정하는 판정입니다(§18.1). 실행 함수들은 이 결과만 봅니다.
    private readonly SquadAIDecision m_decision = new SquadAIDecision();

    // 마지막으로 확정한 행동 요청입니다. 실행과 진단이 같은 값을 봐야 어긋나지 않습니다.
    private SquadAIDecision.Result m_currentDecision;

    // 플레이어가 내린 이동·사수 명령입니다. 켜져 있으면 합류·동행·전투 위치 조정 대신 이 자리로 가서 지킵니다.
    private bool m_hasMoveOrder;
    private Vector3 m_moveOrderPosition;

    // 지금 구조하려는 대상과 그 상호작용 지점입니다(§16). 선점은 SquadManager가 중재합니다.
    private SquadMemberController m_rescueTarget;
    private DownedAllyInteractable m_rescueInteractable;

    // 구조 홀드 진행 시간입니다. 홀드 진행도는 부르는 쪽이 소유합니다(InteractionController와 같은 방식).
    private float m_rescueHoldTimer;
    private bool m_rescueHoldStarted;

    // 경로 실패로 취소한 대상과 다시 시도할 수 있는 시각입니다(§16 재시도 간격).
    private SquadMemberController m_rescueRetryBlockedTarget;
    private float m_rescueRetryAllowedTime;

    // 구조 중 유효한 피격을 받았는지입니다(§16 취소 조건). 피해 알림에서 세우고 판단이 소비합니다.
    private bool m_rescueHitWhileRescuing;

    // 이번 프레임에 겨눌 지점입니다. 유예 중에는 마지막 확인 위치입니다(§8.5).
    private Vector3 m_aimPoint;
    private bool m_hasAimPoint;

    // 자동 사격의 유지·휴지 구간 상태입니다(§11.2). 무기의 점사 모드가 아니라 AI 사격 제어입니다.
    private bool m_isBursting;
    private float m_burstPhaseEndTime;

    // 이번 사격 구간에 쓸 조준 오차입니다. 발마다 흔들면 탄퍼짐과 겹쳐 과해집니다.
    private Vector3 m_aimErrorOffset;

    // 마지막으로 실제 발사한 시각입니다. 연사 중 반동 레이어가 발 사이마다 끊기지 않게 하는 데 씁니다.
    private float m_lastShotTime = -999.0f;

    // 사격 자세를 유지할 시간입니다. 발사 간격보다 넉넉해야 연사 중 반동이 이어집니다.
    private const float ShootingVisualHold = 0.25f;

    // LookY를 -1~1로 정규화할 때 쓰는 최대 상하 각도입니다. ThirdPersonController의 카메라 피치 범위와 맞춥니다.
    private const float DefaultMaxLookUpPitch = 70.0f;
    private const float DefaultMaxLookDownPitch = 30.0f;

    // 상체 조준 오프셋 파라미터의 감쇠 시간입니다. 즉시 바꾸면 상체가 튑니다.
    private const float LookDamp = 0.1f;

    // 상체 조준 리그를 소유한 컴포넌트입니다. AI에서는 꺼져 있고 공개 진입점만 부릅니다.
    private AimController m_aimController;
    private ThirdPersonController m_thirdPersonController;

    // 전투 위치 상태입니다. 주기 사이에 자리가 흔들리지 않도록 들고 있습니다(§10.2).
    private Vector3 m_combatPosition;
    private bool m_hasCombatPosition;
    private float m_nextCombatPositionTime;

    // 직접 조작 멤버에게 길을 비켜 주는 동안에는 일반 추종/전투 목적지가 이 목적지를 덮어쓰지 않게 합니다.
    private bool m_isYieldingToPlayer;
    private Vector3 m_playerYieldDestination;
    private float m_playerYieldEndTime;
    private float m_nextPlayerYieldRequestTime;
    private float m_preYieldStoppingDistance;
    private float m_preYieldSpeed;
    private int m_preYieldAvoidancePriority;

    /// <summary>전투 위치 후보를 몇 방향에서 뽑을지입니다.</summary>
    private const int CombatPositionCandidateCount = 7;

    /// <summary>후퇴 후보를 좌우로 벌리는 각도 간격입니다.</summary>
    private const float RetreatAngleStep = 30.0f;

    // 사격선 판정에 쓸 무기입니다. 사거리를 여기서 읽습니다.
    private Gun m_weapon;

    private static readonly int SpeedHash = Animator.StringToHash("MoveSpeed");
    private static readonly int MotionSpeedHash = Animator.StringToHash("MotionSpeed");

    // 아래 파라미터는 ThirdPersonController가 조작 중인 멤버에게 채우는 것과 같은 묶음입니다.
    // AI 멤버는 ThirdPersonController가 꺼져 있어(SquadMemberController가 직접 조작 멤버만 켭니다)
    // 여기서 채우지 않으면 값이 초기값에 머물러 애니메이션이 엉킵니다.
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveZHash = Animator.StringToHash("MoveZ");
    private static readonly int MoveStateHash = Animator.StringToHash("MoveState");
    private static readonly int IsMoveHash = Animator.StringToHash("IsMove");
    private static readonly int GroundedHash = Animator.StringToHash("IsGrounded");
    private static readonly int JumpHash = Animator.StringToHash("IsJump");
    private static readonly int FreeFallHash = Animator.StringToHash("IsFreeFall");

    // 조준 자세 파라미터입니다. 조작 멤버는 ThirdPersonController가 채우지만 AI 멤버는 그것도 꺼져 있어
    // 여기서 채워야 조준 포즈가 나옵니다.
    private static readonly int AimHash = Animator.StringToHash("IsAim");

    // 상체 조준 오프셋 파라미터입니다. 조작 멤버는 ThirdPersonController가 카메라 각도로 채웁니다.
    // AI 멤버에서 비워 두면 조작하던 시절의 플레이어 값이 그대로 남아 상체가 엉뚱하게 꺾입니다.
    private static readonly int LookXHash = Animator.StringToHash("LookX");
    private static readonly int LookYHash = Animator.StringToHash("LookY");

    /// <summary>이동 방향을 유효한 방향으로 볼 최소 수평 속력입니다.</summary>
    private const float MoveDirectionThreshold = 0.01f;

    /// <summary>MoveState 축의 조깅 단계 값입니다. 평소 이동은 이 단계입니다.</summary>
    private const float MoveStateJog = 2.0f;

    /// <summary>MoveState 축의 달리기 단계 값입니다.</summary>
    /// <remarks><see cref="ThirdPersonController"/>와 같은 축을 씁니다(0=웅크림, 1=걷기(대문 입장 컷신만 씀), 2=조깅, 3=달리기).</remarks>
    private const float MoveStateRun = 3.0f;

    /// <summary>이동 방향 파라미터의 감쇠 시간입니다.</summary>
    private const float MoveDirectionDamp = 0.1f;

    /// <summary>목적지 후보를 NavMesh 위로 끌어당길 때 허용하는 반경입니다.</summary>
    private const float DestinationSampleRadius = 1.5f;

    /// <summary>목적지 도착 판정의 최소 거리입니다.</summary>
    /// <remarks>실제 판정 거리는 <see cref="ResolveArrivalDistance"/>가 Agent 정지 거리와 비교해 정합니다.</remarks>
    private const float DestinationArrivalEpsilon = 0.6f;

    /// <summary>Agent 정지 거리 위에 더 얹는 도착 판정 여유입니다.</summary>
    /// <remarks>정지 직후 미세하게 남는 관성과 위치 오차를 흡수합니다.</remarks>
    private const float ArrivalStopDistanceMargin = 0.35f;

    /// <summary>후보 고리의 반지름을 합류 완료 거리의 몇 배로 잡을지입니다.</summary>
    /// <remarks>
    /// 완료 거리와 같게 두면 후보가 경계에 걸려 도착하자마자 다시 벗어난 것으로 판정될 수 있습니다.
    /// 안쪽으로 조금 당겨 두면 도착 후 판정이 안정됩니다.
    /// </remarks>
    private const float DestinationRadiusRatio = 0.75f;

    /// <summary>합류를 시작하는 경로상 거리입니다.</summary>
    public float JoinStartDistance => m_joinStartDistance;

    /// <summary>합류를 끝내는 경로상 거리입니다.</summary>
    public float JoinCompleteDistance => m_joinCompleteDistance;

    /// <summary>목표 위치 갱신 주기입니다.</summary>
    public float UpdateInterval => m_updateInterval;

    /// <summary>이동 방향으로 회전하는 속도입니다.</summary>
    public float RotationSpeed => m_rotationSpeed;

    /// <summary>지금 합류 중인지 여부입니다.</summary>
    /// <remarks>
    /// 진단용입니다. 합류는 <see cref="JoinStartDistance"/>에서 켜지고 <see cref="JoinCompleteDistance"/>에서
    /// 꺼집니다. 이 값이 자주 뒤집히면 두 거리가 너무 가깝다는 뜻입니다(공용 문서 §6.2).
    /// </remarks>
    public bool IsJoining => m_isJoining;

    /// <summary>현재 직접 조작 중인 플레이어에게 길을 양보하기 위해 이동 중인지 여부입니다.</summary>
    public bool IsYieldingToPlayer => m_isYieldingToPlayer;

    /// <summary>현재 상태에서 직접 조작 중인 플레이어에게 길을 양보할 수 있는지 여부입니다.</summary>
    /// <remarks>구조를 수행 중이거나 NavMesh 밖인 멤버는 플레이어 접촉으로 이동시키지 않습니다.</remarks>
    public bool CanYieldToPlayer
    {
        get
        {
            if (!EnsureRequiredReferences())
            {
                return false;
            }

            return m_memberController.IsAlive && !m_memberController.IsDown &&
                   m_agent.enabled && m_agent.isOnNavMesh &&
                   m_rescueTarget == null && !m_rescueHoldStarted;
        }
    }

    /// <summary>합류 실패가 확정된 상태인지입니다(§17).</summary>
    /// <remarks>
    /// 진단용입니다. 재배치가 성공하면 즉시 내려가므로, 이 값이 계속 켜져 있으면 <b>재배치할 유효 위치를
    /// 못 찾고 있다</b>는 뜻입니다(§17 "유효한 재배치 위치 없음 -> 강제 재배치 없이 경로 재탐색 계속").
    /// </remarks>
    public bool IsJoinFailed => m_joinFailed;

    /// <summary>합류 실패를 확정하기까지 경로를 다시 탐색한 횟수입니다(§17).</summary>
    /// <remarks>진단용입니다. 진척이 회복되면 0으로 돌아갑니다.</remarks>
    public int JoinRepathCount => m_joinRepathCount;

    /// <summary>이동 진척 없이 지난 시간입니다(§17).</summary>
    /// <remarks>진단용입니다. 정상적으로 이동하지 못하는 행동 중에는 늘지 않습니다.</remarks>
    public float JoinStallTimer => m_joinStallTimer;

    /// <summary>마지막으로 확정한 합류 목적지입니다. 확정 전에는 자기 위치를 돌려줍니다.</summary>
    public Vector3 CurrentDestination => m_hasDestination ? m_destination : transform.position;

    /// <summary>플레이어까지 마지막으로 측정한 경로상 거리입니다. 경로가 없으면 <c>-1</c>입니다.</summary>
    public float PathDistanceToLeader => m_pathDistanceToLeader;

    /// <summary>이 AI의 현재 대상 판단입니다(§9).</summary>
    /// <remarks>
    /// 무엇을 아는지는 스쿼드가 공유하지만(<see cref="SquadEnemyIntel"/>), 그중 누구를 칠지는
    /// AI마다 따로 정합니다. 컴포넌트가 아니라 이 컨트롤러가 소유하는 일반 객체입니다.
    /// </remarks>
    public SquadAITargeting Targeting => m_targeting;

    /// <summary>이번 프레임에 확정된 행동 요청입니다(§18.1).</summary>
    /// <remarks>
    /// 실행 함수들이 보는 것과 같은 값입니다. 어떤 우선순위 단계가 이 행동을 정했는지는
    /// <see cref="SquadAIDecision.Result.Step"/>에 문서 번호로 들어 있습니다.
    /// </remarks>
    public SquadAIDecision.Result CurrentDecision => m_currentDecision;

    /// <summary>플레이어의 이동·사수 명령을 수행 중인지입니다.</summary>
    public bool HasMoveOrder => m_hasMoveOrder;

    /// <summary>플레이어가 지정한 사수 위치입니다. <see cref="HasMoveOrder"/>가 false면 의미가 없습니다.</summary>
    public Vector3 MoveOrderPosition => m_moveOrderPosition;

    /// <summary>
    /// 지정한 위치로 이동해 그 자리를 지키라는 명령을 내립니다.
    /// </summary>
    /// <param name="position">NavMesh 위로 보정된 사수 위치입니다.</param>
    /// <remarks>
    /// 명령 중에는 합류(§18.1 4)와 전투 위치 조정(5)을 하지 않고, 자동 구조(2)도 시작하지 않습니다.
    /// 조준·사격·재장전은 그대로 합니다. 플레이어가 멀리 가도 따라가지 않는 것이 명령의 목적이기 때문입니다.
    /// 다시 부르면 위치만 바꿉니다.
    /// </remarks>
    public void IssueMoveOrder(Vector3 position)
    {
        m_hasMoveOrder = true;
        m_moveOrderPosition = position;

        // 합류 판단을 접습니다. 명령 중에는 UpdateJoinState가 돌지 않으므로 여기서 내리지 않으면
        // 합류 상태와 복구 누적이 명령이 끝날 때까지 얼어붙습니다.
        m_isJoining = false;
        ResetJoinRecovery();
        m_hasJoinProgressPosition = false;
        m_lastJoinCheckTime = -1.0f;

        // 다음 프레임에 바로 출발하도록 이동 갱신 주기를 당깁니다.
        m_nextUpdateTime = 0.0f;
    }

    /// <summary>
    /// 이동·사수 명령을 취소하고 평소 동행으로 돌아갑니다.
    /// </summary>
    public void CancelMoveOrder()
    {
        if (!m_hasMoveOrder)
        {
            return;
        }

        m_hasMoveOrder = false;
        ClearFollowDestination();
        m_nextUpdateTime = 0.0f;
    }

    /// <summary>지금 자동 구조하려는 대상입니다. 없으면 null입니다(§16).</summary>
    public SquadMemberController RescueTarget => m_rescueTarget;

    /// <summary>자동 구조 기립 홀드의 진행도(0~1)입니다. 홀드 중이 아니면 0입니다.</summary>
    public float RescueHoldProgress01
    {
        get
        {
            if (!m_rescueHoldStarted || m_rescueInteractable == null)
            {
                return 0.0f;
            }

            float duration = Mathf.Max(0.0f, m_rescueInteractable.HoldDuration);
            return duration <= 0.0f ? 1.0f : Mathf.Clamp01(m_rescueHoldTimer / duration);
        }
    }

    /// <summary>이번 프레임에 겨누고 있는 지점입니다. 대상이 없으면 false입니다.</summary>
    /// <param name="point">겨누는 지점입니다. 유예 중에는 마지막 확인 위치입니다.</param>
    /// <returns>겨누는 중이면 true입니다.</returns>
    public bool TryGetCurrentAimPoint(out Vector3 point)
    {
        point = m_aimPoint;
        return m_hasAimPoint;
    }

    /// <summary>대상 방향과 몸 방향의 각도 차입니다. 겨누는 중이 아니면 -1입니다.</summary>
    /// <remarks>조준이 실제로 맞았는지 확인하는 진단값입니다.</remarks>
    public float AimAngleError
    {
        get
        {
            if (!m_hasAimPoint)
            {
                return -1.0f;
            }

            Vector3 delta = m_aimPoint - transform.position;
            delta.y = 0.0f;
            return delta.sqrMagnitude < 0.0001f ? 0.0f : Vector3.Angle(transform.forward, delta.normalized);
        }
    }

    /// <summary>조준이 허용 오차 안에 들어왔는지 여부입니다.</summary>
    public bool IsAimAligned => m_hasAimPoint && AimAngleError <= m_aimToleranceAngle;

    /// <summary>지금 사격 유지 구간인지 여부입니다. 휴지 구간이면 false입니다(§11.2).</summary>
    public bool IsBursting => m_isBursting;

    /// <summary>현재 대상이 이 거리 안으로 붙으면 물러납니다(§10.3).</summary>
    public float KeepAwayDistance => m_keepAwayDistance;

    /// <summary>지금 전투 위치로 이동 중인지 여부입니다.</summary>
    /// <remarks>진단용입니다. 거리 벌리기나 사격선 확보로 자리를 옮기는 중이면 true입니다.</remarks>
    public bool IsRepositioning => m_hasCombatPosition;

    /// <summary>팀 AI의 발사 허용 여부입니다.</summary>
    /// <remarks>
    /// 끄면 발사와 재장전만 멈추고 대상 선정·조준은 그대로 돕니다.
    /// <para>
    /// <b>값은 <see cref="SquadManager"/>가 소유하는 스쿼드 전체 설정입니다.</b> 이 속성은 그 값을
    /// 그대로 보여 주고 넘겨줄 뿐이며, 여기에 쓰면 스쿼드 전원에 반영됩니다. 멤버마다 사본을 두면
    /// 조작 캐릭터를 전환할 때 누가 옛 값을 쥐고 있는지가 갈립니다.
    /// </para>
    /// </remarks>
    public bool AllowFiring
    {
        get => m_squadManager == null || m_squadManager.AiFiringAllowed;
        set
        {
            if (m_squadManager != null)
            {
                m_squadManager.AiFiringAllowed = value;
            }
        }
    }

    /// <summary>AI 팀원끼리 유지할 간격입니다. <see cref="SquadManager"/>가 소유합니다.</summary>
    private float DestinationClearance => m_squadManager != null ? m_squadManager.AiMemberSpacing : 0.0f;

    /// <summary>이 AI가 적을 직접 확인할 수 있는 최대 거리입니다.</summary>
    public float SightRange => m_sightRange;

    /// <summary>이 AI의 전체 시야각입니다.</summary>
    public float SightAngle => m_sightAngle;

    /// <summary>
    /// 이 AI가 조작하는 캐릭터의 시야로 해당 지점을 볼 수 있는지 판정합니다(§8.3).
    /// </summary>
    /// <param name="point">확인할 지점입니다.</param>
    /// <param name="obstacleMask">시야를 가로막는 고정 환경 장애물 레이어입니다.</param>
    /// <returns>거리·시야각·차폐를 모두 통과하면 true입니다.</returns>
    /// <remarks>
    /// 판정 주체가 여기인 이유는 문서가 "AI는 <b>자신이 현재 조작하는 캐릭터</b>의 시점, 시야각과
    /// 시야 거리를 기준으로 확인한다"고 규정하기 때문입니다. 스쿼드 공용 정보는 결과만 받습니다.
    /// <para>
    /// 싼 검사부터 합니다. Raycast는 거리와 각도를 통과한 뒤에만 나갑니다.
    /// 변이체 쪽 <c>EnemyTargetSensor.CanSee</c>와 같은 순서·같은 방식이며, 전용 인식 콜라이더 대신
    /// 한 점을 겨누는 것도 동일합니다(기획 확정).
    /// </para>
    /// <para>
    /// 무엇을 볼지는 부르는 쪽이 정합니다. 이 함수는 "교전 중인 적만" 같은 §8.1 제약을 알지 못하므로
    /// 후보를 걸러 넘겨야 합니다.
    /// </para>
    /// </remarks>
    public bool CanSeePoint(Vector3 point, int obstacleMask)
    {
        Vector3 origin = m_eyePoint != null
            ? m_eyePoint.position
            : transform.position + Vector3.up * m_eyeHeight;

        Vector3 delta = point - origin;

        if (delta.sqrMagnitude > m_sightRange * m_sightRange)
        {
            return false;
        }

        float distance = delta.magnitude;
        if (distance <= 0.0001f)
        {
            return true;
        }

        Vector3 direction = delta / distance;
        if (Vector3.Angle(transform.forward, direction) > m_sightAngle * 0.5f)
        {
            return false;
        }

        return !Physics.Raycast(origin, direction, distance, obstacleMask, QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// 현재 AI 추종 상태를 전환 유지용으로 캡처합니다.
    /// </summary>
    /// <returns>AI 추종 상태입니다.</returns>
    public FollowCarryoverState CaptureFollowCarryoverState()
    {
        if (m_agent == null)
        {
            return default;
        }

        bool canReadAgentPath = m_agent.enabled && m_agent.isOnNavMesh;

        return new FollowCarryoverState
        {
            HasState = true,
            NextUpdateDelay = Mathf.Max(0.0f, m_nextUpdateTime - Time.time),
            AgentEnabled = m_agent.enabled,
            IsStopped = !m_agent.enabled || m_agent.isStopped,
            HasPath = canReadAgentPath && m_agent.hasPath,
            Position = transform.position,
            Destination = canReadAgentPath ? m_agent.destination : transform.position,
            Velocity = m_agent.enabled ? m_agent.velocity : Vector3.zero,
            HasMoveOrder = m_hasMoveOrder,
            MoveOrderPosition = m_moveOrderPosition,
        };
    }

    /// <summary>
    /// 전환 직전 캡처한 AI 추종 상태를 현재 멤버에 적용합니다.
    /// </summary>
    /// <param name="state">적용할 AI 추종 상태입니다.</param>
    public void ApplyFollowCarryoverState(FollowCarryoverState state)
    {
        if (!state.HasState)
        {
            return;
        }

        // 이 자리에 걸려 있던 이동·사수 명령을 이어받습니다. 명령은 길찾기 상태와 무관하게 남겨야
        // 에이전트가 잠시 꺼져 있어도 다음 갱신에서 그 자리로 갑니다.
        if (state.HasMoveOrder)
        {
            IssueMoveOrder(state.MoveOrderPosition);
        }

        if (m_agent == null || !m_agent.enabled)
        {
            return;
        }

        m_nextUpdateTime = Time.time + state.NextUpdateDelay;

        if (state.IsStopped || !state.AgentEnabled)
        {
            StopAgent();
            return;
        }

        // 아래 블록과 같은 이유로 NavMesh 위에 있을 때만 만집니다. 전환 직후에는 지면 보정이
        // 실패해 아직 NavMesh를 벗어나 있을 수 있습니다.
        if (m_agent.isOnNavMesh)
        {
            m_agent.isStopped = false;
        }

        if (state.HasPath && m_agent.isOnNavMesh)
        {
            Vector3 destination = state.Destination;
            Vector3 velocity = state.Velocity;
            velocity.y = 0.0f;

            if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 2.0f, NavFilter))
            {
                destination = hit.position;
            }

            m_agent.SetDestination(destination);
            m_agent.velocity = velocity;
        }
    }

    /// <summary>
    /// 필요한 참조를 캐싱하고 NavMeshAgent 회전 갱신 방식을 초기화합니다.
    /// </summary>
    private void Awake()
    {
        CacheRequiredReferences();

        if (!ValidateRequiredReferences())
        {
            enabled = false;
            return;
        }

        m_agent.updateRotation = false;
        ApplyTargetingSettings();
        m_hasRequiredReferences = m_squadManager != null;
    }

    /// <summary>
    /// Inspector 값을 대상 판단에 반영합니다.
    /// </summary>
    /// <remarks>
    /// 판단 규칙과 수치를 분리해 두었으므로 값만 옮겨 담습니다. 인스펙터에서 바꾼 값을 Play Mode에서
    /// 바로 보려면 <see cref="OnValidate"/>가 다시 부릅니다.
    /// </remarks>
    private void ApplyTargetingSettings()
    {
        m_targeting.ReevaluateInterval = m_reevaluateInterval;
        m_targeting.TargetHoldGrace = m_targetHoldGrace;
        m_targeting.DistanceThreatWeight = m_distanceThreatWeight;
        m_targeting.DistanceThreatFalloff = m_distanceThreatFalloff;
        m_targeting.CurrentTargetBonus = m_currentTargetBonus;
        m_targeting.DamageThreatPerPoint = m_damageThreatPerPoint;
        m_targeting.DamageThreatDecayPerSecond = m_damageThreatDecayPerSecond;
        m_targeting.SustainedBlockDuration = m_sustainedBlockDuration;
        m_targeting.AllyBlockRadius = m_allyBlockRadius;
        m_targeting.ReactionTime = m_reactionTime;
    }

    private void OnValidate()
    {
        ApplyTargetingSettings();
    }

    /// <summary>
    /// 이 AI가 받은 피해를 공격자별 위협도에 누적합니다(§4.3).
    /// </summary>
    /// <param name="attacker">피해를 준 적입니다.</param>
    /// <param name="damage">실제로 적용된 피해량입니다.</param>
    public void NotifyDamagedBy(EnemyController attacker, int damage)
    {
        m_targeting.NotifyDamagedBy(attacker, damage);

        // 구조 중 유효한 피격은 구조를 취소합니다(§16). 여기서는 표시만 하고 판단은 UpdateRescue가 합니다.
        // 피해 0인 알림까지 취소로 치면 스치는 것만으로 구조가 끊깁니다.
        if (damage > 0)
        {
            m_rescueHitWhileRescuing = true;
        }
    }

    /// <summary>
    /// 현재 대상 판단을 갱신합니다.
    /// </summary>
    /// <remarks>
    /// 합류 중에는 대상 판단을 멈춥니다. §9.5가 "합류가 시작되면 현재 대상 정보는 유지하되 조준과
    /// 사격 요청을 종료한다"고 규정하므로, 판단은 남기고 실행만 끊는 것이 맞습니다. 여기서는
    /// <see cref="SquadAITargeting.CanFireAtCurrentTarget"/>이 자연히 false가 되도록 갱신만 건너뜁니다.
    /// </remarks>
    private void UpdateTargeting()
    {
        SquadManager manager = m_squadManager;
        if (manager == null)
        {
            return;
        }

        var context = new SquadAITargeting.Context
        {
            Intel = manager.EnemyIntel,
            EyePosition = m_eyePoint != null ? m_eyePoint.position : transform.position + Vector3.up * m_eyeHeight,
            BodyPosition = transform.position,
            WeaponRange = m_weapon != null ? m_weapon.HitscanRange : m_sightRange,
            ObstacleMask = manager.IntelObstacleMask,
            Squad = manager.SquadMembers,
            Self = m_memberController,
            CanEngage = m_memberController != null
                        && m_memberController.IsAlive
                        && !m_memberController.IsDown,
            IsJoining = m_isJoining,
        };

        m_targeting.Tick(in context);

        // 합류 중에는 대상 정보를 남긴 채 조준만 끕니다(§18.2 "합류 시작 중 조준 또는 연속 사격 ->
        // 실행 요청 즉시 종료 후 합류"). 조준을 남기면 몸이 적을 보느라 합류 방향으로 돌지 않습니다.
        m_hasAimPoint = !m_isJoining
                        && m_targeting.CurrentTarget != null
                        && m_targeting.TryGetAimPoint(manager.EnemyIntel, out m_aimPoint);
    }

    /// <summary>
    /// 대상을 향해 몸을 돌립니다(§11.2).
    /// </summary>
    /// <returns>조준을 수행했으면 true입니다. 그러면 이동 방향 회전을 건너뜁니다.</returns>
    /// <remarks>
    /// <b>유예 중에도 돕니다.</b> §8.5가 "마지막 확인 위치를 향한 조준과 방향 유지는 가능하지만
    /// 개인 사격선이 없으면 발사하지 않는다"고 규정하므로, 겨누는 것과 쏘는 것은 다른 판정입니다.
    /// 발사 가부는 <see cref="SquadAITargeting.CanFireAtCurrentTarget"/>이 따로 들고 있습니다.
    /// <para>
    /// 조준이 이동 방향 회전을 이깁니다. 둘 다 몸을 돌리므로 한쪽만 이겨야 하고, 전투 중에는 적을
    /// 보는 쪽이 맞습니다. §10.3의 "이동 중에도 조준과 사격을 계속한다"와도 맞습니다.
    /// </para>
    /// <para>
    /// <b>현재는 몸통 회전까지만입니다.</b> 조준 자세(IsAim)와 상체 리그는 <see cref="AimController"/>가
    /// 담당하는데 AI 멤버에서는 꺼져 있습니다. 이동 실행 통합에서 그 컴포넌트를 켜게 되면 그때 연결합니다.
    /// </para>
    /// </remarks>
    private bool HandleAimRotation()
    {
        if (!m_currentDecision.Aim || !m_hasAimPoint)
        {
            return false;
        }

        Vector3 delta = m_aimPoint - transform.position;
        delta.y = 0.0f;

        if (delta.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        Quaternion targetRotation = Quaternion.LookRotation(delta.normalized);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            Time.deltaTime * m_aimRotationSpeed);

        return true;
    }

    /// <summary>
    /// 사격 가능하면 실제로 무기를 쏩니다(§11.2).
    /// </summary>
    /// <remarks>
    /// <b>플레이어와 같은 경로를 씁니다.</b> <see cref="Gun.TryLayShoot"/>가 히트스캔·탄퍼짐·피해·피드백과
    /// 탄약 소비를 모두 처리하므로, AI만 다른 경로로 쏘면 §11.1의 "동일한 무기는 조작 주체와 관계없이
    /// 실제 성능을 동일하게 적용한다"가 깨집니다.
    /// <para>
    /// <b>유지·휴지 구간</b>: 조건이 맞아도 계속 쏘지 않고 <see cref="m_burstDuration"/> 만큼 쏜 뒤
    /// <see cref="m_burstRestDuration"/> 만큼 쉽니다. 문서가 이것을 무기의 점사 모드가 아니라
    /// AI 사격 제어로 규정합니다. 쉬는 동안에도 조준은 유지합니다.
    /// </para>
    /// <para>
    /// <b>조준 오차</b>는 사격 구간이 시작될 때 한 번 뽑아 그 구간 내내 씁니다. 발마다 새로 흔들면
    /// 무기 자체의 탄퍼짐과 겹쳐 과하게 흩어집니다. AI와 플레이어의 차이는 여기서 만듭니다(§11.1).
    /// </para>
    /// </remarks>
    private void HandleFiring()
    {
        if (m_weapon == null)
        {
            return;
        }

        // 발사 요청은 판정이 이미 끝냈습니다(§18.1 5). 사격을 막아도 대상 선정과 조준은 그대로 두는데,
        // 겨누기와 쏘기는 다른 판정이며(§8.5) 조준까지 끄면 동료가 적을 등지고 서 있어
        // 무엇을 노리는지 볼 수 없기 때문입니다.
        //
        // 조준 정렬만 여기서 봅니다. 이번 프레임의 몸 회전 결과에 의존하므로 판정 시점에는 아직
        // 확정되지 않은 값입니다. 사격선이 막히거나 조준이 어긋나면 즉시 멈춥니다(§10.1, §11.2).
        // 튜토리얼이 팀원 사격을 막아 둔 동안에는 조준만 하고 쏘지 않습니다.
        if (!m_currentDecision.Fire || !IsAimAligned || PlayerInputController.IsInputLocked(PlayerInputLock.SquadAiFire))
        {
            m_isBursting = false;
            return;
        }

        UpdateBurstPhase();

        if (!m_isBursting)
        {
            return;
        }

        Vector3 muzzle = m_weapon.FirePos != null
            ? m_weapon.FirePos.position
            : transform.position + Vector3.up * m_eyeHeight;

        Vector3 aim = m_aimPoint + Vector3.up + m_aimErrorOffset;
        Vector3 delta = aim - muzzle;

        if (delta.sqrMagnitude < 0.0001f)
        {
            return;
        }

        float distance = delta.magnitude;
        Vector3 direction = delta / distance;

        var shot = new Gun.HitscanShotInfo
        {
            IsValid = true,
            HasHit = false,
            IsObstructed = false,
            Origin = muzzle,
            Direction = direction,
            AimPoint = aim,
            EndPoint = muzzle + direction * Mathf.Min(distance, m_weapon.HitscanRange),
            FrameCount = Time.frameCount,
        };

        // AI는 ADS를 쓰지 않으므로 힙파이어 기준 탄퍼짐으로 넘깁니다.
        if (m_weapon.TryLayShoot(shot, false, out _))
        {
            m_lastShotTime = Time.time;
        }
    }

    /// <summary>
    /// 전투 자세(조준 포즈와 상체 리그)를 적용합니다.
    /// </summary>
    /// <remarks>
    /// 두 곳을 함께 건드려야 조준처럼 보입니다. <c>IsAim</c>은 Base Layer의 조준 트리를 고르고,
    /// <see cref="AimController.ApplyAiCombatStance"/>가 상체 조준 리그와 손 IK를 올립니다.
    /// 둘 다 원래는 조작 멤버에서 <see cref="ThirdPersonController"/>와 <see cref="AimController"/>가
    /// 하는 일인데, AI 멤버에서는 두 컴포넌트가 모두 꺼져 있어 여기서 대신 부릅니다.
    /// <para>
    /// 사격 직후 잠깐은 사격 중으로 봅니다. 발사 간격 사이마다 반동 레이어가 0으로 떨어지면 연사 중에
    /// 반동이 끊겨 보이기 때문입니다.
    /// </para>
    /// </remarks>
    private void HandleCombatStance()
    {
        bool inCombat = m_hasAimPoint;
        bool shooting = inCombat && Time.time - m_lastShotTime <= ShootingVisualHold;

        if (m_animator != null)
        {
            m_animator.SetBool(AimHash, inCombat);

            // 상체 조준 오프셋을 AI 기준으로 채웁니다. 이 값을 건드리지 않으면 조작하던 시절
            // 플레이어의 카메라 각도가 그대로 남아 상체가 엉뚱한 쪽으로 꺾입니다.
            // AI는 몸 전체를 대상 쪽으로 돌리므로 좌우 오프셋은 0이고, 상하만 높이 차로 냅니다.
            float lookY = 0.0f;
            if (inCombat)
            {
                Vector3 eye = m_eyePoint != null ? m_eyePoint.position : transform.position + Vector3.up * m_eyeHeight;
                Vector3 delta = m_aimPoint + Vector3.up - eye;
                float flat = new Vector2(delta.x, delta.z).magnitude;
                if (flat > 0.01f)
                {
                    lookY = NormalizeLookPitch(Mathf.Atan2(delta.y, flat) * Mathf.Rad2Deg);
                }
            }

            m_animator.SetFloat(LookXHash, 0.0f, LookDamp, Time.deltaTime);
            m_animator.SetFloat(LookYHash, lookY, LookDamp, Time.deltaTime);
        }

        if (m_aimController != null)
        {
            // 겨눌 지점을 함께 넘겨 상체 IK 타겟이 AI 기준으로 움직이게 합니다.
            if (inCombat)
            {
                m_aimController.ApplyAiLookPoint(m_aimPoint + Vector3.up);
            }

            m_aimController.ApplyAiCombatStance(inCombat, shooting);
        }
    }

    /// <summary>
    /// Maps AI target pitch to the same asymmetric LookY convention as ThirdPersonController:
    /// +1 is the configured upward clamp and -1 is the configured downward clamp.
    /// </summary>
    private float NormalizeLookPitch(float pitchDegrees)
    {
        float maxUp = m_thirdPersonController != null
            ? Mathf.Max(0.01f, m_thirdPersonController.TopClamp)
            : DefaultMaxLookUpPitch;
        float maxDown = m_thirdPersonController != null
            ? Mathf.Max(0.01f, Mathf.Abs(m_thirdPersonController.BottomClamp))
            : DefaultMaxLookDownPitch;

        return pitchDegrees >= 0.0f
            ? Mathf.Clamp01(pitchDegrees / maxUp)
            : -Mathf.Clamp01(-pitchDegrees / maxDown);
    }

    /// <summary>사격 유지 구간과 휴지 구간을 번갈아 갱신합니다(§11.2).</summary>
    private void UpdateBurstPhase()
    {
        if (Time.time < m_burstPhaseEndTime)
        {
            return;
        }

        if (m_isBursting)
        {
            m_isBursting = false;
            m_burstPhaseEndTime = Time.time + Mathf.Max(0.0f, m_burstRestDuration);
            return;
        }

        m_isBursting = true;
        m_burstPhaseEndTime = Time.time + Mathf.Max(0.05f, m_burstDuration);
        m_aimErrorOffset = ResolveAimError();
    }

    /// <summary>
    /// 확정된 재장전 요청을 시작합니다(§12.1).
    /// </summary>
    /// <remarks>
    /// 빈 탄창인지 전술 재장전인지, 지금 해도 되는지는 <see cref="SquadAIDecision"/>이 §18.1 순서로
    /// 이미 정했습니다(빈 탄창은 3번, 전술은 6번). 여기서는 그 요청을 수행만 합니다.
    /// <para>
    /// 시작한 재장전은 중간에 끊지 않습니다(§12.2 "새로운 적이 사격 가능한 상태가 되어도 완료한다").
    /// <see cref="Gun"/>이 <c>Invoke</c>로 완료를 예약하므로 여기서 아무것도 하지 않으면 그대로 완료됩니다.
    /// </para>
    /// </remarks>
    private void HandleReload()
    {
        // 무엇을 재장전할지는 판정이 이미 끝냈습니다(§18.1 3과 6). 여기서는 요청만 수행합니다.
        // 진행 중인 재장전은 판정이 None을 돌려주므로 자연히 끊기지 않습니다(§12.2).
        if (m_currentDecision.Reload == SquadAIReloadIntent.None || m_weapon == null)
        {
            return;
        }

        // AI는 입력을 거치지 않으므로 Gun만 직접 시작하면 탄약 타이머만 돌고 재장전 모션이 빠집니다.
        // AimController의 공용 재장전 경로로 들어가면 플레이어와 같은 IsReload/DoReload 상태,
        // 레이어 가중치, 완료 정리까지 함께 적용됩니다.
        if (m_aimController != null && m_aimController.BeginAiReload())
        {
            return;
        }

        // 리그/애니메이터 참조가 없는 예외 프리팹은 기존처럼 논리 재장전만 유지합니다.
        m_weapon.StartReload();
    }

    /// <summary>이번 사격 구간에 쓸 조준 오차를 뽑습니다.</summary>
    /// <returns>겨눈 지점에 더할 오프셋입니다.</returns>
    private Vector3 ResolveAimError()
    {
        if (m_aimErrorRadius <= 0.0f)
        {
            return Vector3.zero;
        }

        Vector2 circle = Random.insideUnitCircle * m_aimErrorRadius;
        return transform.right * circle.x + Vector3.up * circle.y;
    }

    /// <summary>
    /// 비활성화될 때 남아 있는 경로와 이동 애니메이션 값을 정리합니다.
    /// </summary>
    private void OnDisable()
    {
        EndPlayerYield();
        StopAgent();
        UpdateMoveAnimation(Vector3.zero);
        ClearFollowState();
    }

    /// <summary>
    /// 동행 위치와 정지 상태를 지웁니다.
    /// </summary>
    /// <remarks>
    /// 조작권을 넘겨받거나 다시 AI가 될 때 옛 위치를 들고 있으면, 그 사이 리더가 움직였는데도
    /// "이미 도착했다"고 판단해 제자리에 서 있게 됩니다.
    /// </remarks>
    private void ClearFollowState()
    {
        m_isJoining = false;

        // 이동·사수 명령도 내립니다. 이 멤버를 직접 조작하게 되면 명령은 의미가 없고,
        // 남겨 두면 다시 AI가 됐을 때 예전 자리로 혼자 돌아갑니다.
        m_hasMoveOrder = false;

        // 찜도 함께 놓습니다. AI에서 벗어난 멤버의 찜이 남아 있으면 남은 AI가 그 자리를 못 씁니다.
        ClearFollowDestination();

        m_hasCombatPosition = false;
        m_pathDistanceToLeader = -1.0f;
        m_isSprinting = false;
        m_moveState = MoveStateJog;
        m_cachedLeaderTransform = null;
        m_cachedLeaderController = null;

        // 합류 실패 누적도 지웁니다(§17). 남겨 두면 조작 캐릭터로 갔다가 다시 AI가 됐을 때 그 사이
        // 흐른 시간이 한꺼번에 정체로 얹혀 첫 갱신에 곧바로 재배치합니다. 진척 기준도 옛 자리가
        // 되므로 시각과 기준을 함께 무효화합니다.
        ResetJoinRecovery();
        m_hasJoinProgressPosition = false;
        m_lastJoinCheckTime = -1.0f;

        // 대상 판단도 함께 지웁니다. 이 컴포넌트가 꺼지면 Tick이 멈추므로, 그냥 두면 마지막 판단이
        // 그대로 얼어붙습니다. 다시 AI가 됐을 때 옛 대상을 이미 조준 중인 것처럼 보이고,
        // 그 사이 대상이 죽거나 교전이 끝났어도 "쏠 수 있다"가 남습니다(실측으로 발견).
        // 문서도 AI 슬롯이 비활성화되면 판단 정보를 제거하라고 규정합니다(§9.5).
        m_targeting.Clear();

        // 마지막 행동 요청도 비웁니다. 남겨 두면 다시 AI가 된 첫 프레임에 옛 요청이 한 번 실행됩니다.
        m_currentDecision = default;

        // 진행 중인 구조도 접습니다(§16 "구조자가 다운되거나 ... 구조를 취소한다").
        // 시작한 홀드를 놓지 않으면 대상의 다운 타이머가 멈춘 채 남습니다.
        ClearRescue(false);

        // 전투 자세도 내립니다. 그러지 않으면 조준 리그가 올라간 채로 남아, 조작 멤버가 됐을 때
        // AimController가 자기 상태에서 시작하지 못하고 어긋난 자세를 물려받습니다.
        m_hasAimPoint = false;
        m_isBursting = false;
        if (m_aimController != null)
        {
            m_aimController.ApplyAiCombatStance(false, false);
        }

        if (m_animator != null)
        {
            m_animator.SetBool(AimHash, false);

            // 상체 오프셋도 0으로 되돌립니다. 남겨 두면 조작 멤버가 됐을 때 AimController가
            // 이 값에서 시작해 첫 프레임에 상체가 튑니다.
            m_animator.SetFloat(LookXHash, 0.0f);
            m_animator.SetFloat(LookYHash, 0.0f);
        }
    }

    /// <summary>
    /// 정보를 갱신하고, 이번 프레임의 행동 요청을 정한 뒤, 그 요청을 실행합니다.
    /// </summary>
    /// <remarks>
    /// 세 구간이 이 순서로 고정입니다.
    /// <para>
    /// 1. <b>정보 갱신</b> - 대상 판단(§9)과 이동 축 상태(경로상 거리, 합류 히스테리시스).
    ///    무엇을 할지는 아직 정하지 않습니다.
    /// 2. <b>판정</b> - <see cref="SquadAIDecision"/>이 §18.1의 우선순위 7단계를 위에서부터 확인해
    ///    이번 프레임의 요청을 확정합니다. 우선순위는 오직 그 안에만 있습니다.
    /// 3. <b>실행</b> - 아래 함수들은 요청을 수행만 하고 "할지 말지"를 다시 판단하지 않습니다.
    /// </para>
    /// <para>
    /// 이동만 갱신 주기를 따릅니다. 목적지를 매 프레임 다시 고르면 경로 계산 비용이 크고 발이 떨립니다
    /// (§6.3). 조준·사격·재장전은 매 프레임 돕니다.
    /// </para>
    /// </remarks>
    private void Update()
    {
        if (!EnsureRequiredReferences())
        {
            return;
        }

        // --- 1. 정보 갱신 ---

        // 대상 판단은 이동과 별개 축이라 이동을 못 하는 상황에서도 계속 돕니다.
        // 다만 다운·전투 이탈은 판단 자체를 멈춰야 하므로 Context.CanEngage가 걸러 냅니다(§9.5).
        UpdateTargeting();

        bool canFollow = CanFollow(out Transform leader);

        // 자기 자신이 리더인 경우는 갈 곳이 없는 것이지 행동 제한이 아니지만, 결과가 같으므로
        // 같은 분기로 접습니다. 다만 이쪽은 명시적으로 세워야 합니다.
        bool selfIsLeader = canFollow && leader == transform;
        bool canAct = canFollow && !selfIsLeader;

        // 구조 판단은 합류·전투보다 위라(§18.1 2) 그 둘보다 먼저 갱신합니다.
        // 이동·사수 명령 중에는 자리를 지켜야 하므로 자동 구조를 시작하지 않고, 하던 구조도 접습니다.
        UpdateRescue(canAct && !m_hasMoveOrder);

        // 양보는 이동만 선점합니다. 구조/행동 제한이 생기면 즉시 원래 이동 설정을 복원하고,
        // 양보 중에도 아래 타겟·사격·재장전 처리는 매 프레임 계속합니다.
        if (m_isYieldingToPlayer && (!canAct || !CanYieldToPlayer))
        {
            EndPlayerYield();
        }
        bool yielding = UpdatePlayerYield();
        bool movementDue = canAct && !yielding && Time.time >= m_nextUpdateTime;
        if (movementDue)
        {
            m_nextUpdateTime = Time.time + m_updateInterval;

            // 명령 중에는 플레이어와의 거리를 보지 않습니다. 합류 판단이 돌면 멀어졌다고 다시 달려옵니다.
            movementDue = m_hasMoveOrder || UpdateJoinState(leader);
        }

        // --- 2. 판정 ---

        m_currentDecision = m_decision.Resolve(BuildDecisionContext(canAct, selfIsLeader));

        // --- 3. 실행 ---

        if (m_currentDecision.Kind == SquadAIActionKind.Restricted)
        {
            EndPlayerYield();
            if (m_currentDecision.HoldPosition)
            {
                StopAgent();
            }

            UpdateMoveAnimation(Vector3.zero);
            return;
        }

        if (movementDue)
        {
            ExecuteMovement(leader);
        }

        // 조준이 이동 방향 회전을 이깁니다. 둘 다 몸을 돌리므로 한쪽만 이겨야 하고,
        // 전투 중에는 적을 보는 쪽이 맞습니다(§10.3 "이동 중에도 조준과 사격을 계속한다").
        if (!HandleAimRotation())
        {
            HandleRotation();
        }

        HandleRescueHold();
        HandleFiring();
        HandleReload();
        HandleCombatStance();
        HandleAnimation();
    }

    /// <summary>
    /// 자동 구조를 시작할지, 유지할지, 접을지를 갱신합니다(§16).
    /// </summary>
    /// <param name="canAct">지금 행동할 수 있는 상태인지입니다.</param>
    /// <remarks>
    /// <b>비전투에서만 시작합니다</b>("AI는 스쿼드가 비전투 상태일 때만 자동 구조를 시작한다").
    /// 전투 중 위험을 감수하는 구조는 플레이어가 직접 결정할 몫입니다. 대신 별도의 안전 대기시간은
    /// 두지 않습니다 - 문서가 "비전투 상태가 확인되면 별도의 추가 안전 대기시간 없이 검토한다"고
    /// 못박습니다.
    /// <para>
    /// <b>선점은 스쿼드가 중재합니다</b>. 여기서는 후보를 고르고 <see cref="SquadManager.TryClaimAutoRescue"/>에
    /// 물어보기만 합니다. 각자 판단하면 여럿이 같은 대상으로 달려가 그동안 동행이 비게 됩니다.
    /// </para>
    /// <para>
    /// 취소 조건은 문서의 다섯 가지입니다 - 새 전투, 구조자 피격, 구조자 다운, 대상 무효화, 경로 상실.
    /// 그중 <b>경로 상실만</b> 재시도 간격이 붙습니다. 나머지는 조건이 풀리면 바로 다시 검토해도 됩니다.
    /// </para>
    /// </remarks>
    private void UpdateRescue(bool canAct)
    {
        SquadManager manager = m_squadManager;
        if (manager == null || m_memberController == null)
        {
            ClearRescue(false);
            return;
        }

        bool inCombat = manager.Engagement != null && manager.Engagement.IsInCombat;

        // 취소 조건 - 새 전투 시작, 구조자 피격, 구조자 다운·사망.
        // 피격은 여기서 소비합니다. 구조를 하고 있지 않을 때 쌓인 표시는 의미가 없습니다.
        bool hitWhileRescuing = m_rescueHitWhileRescuing;
        m_rescueHitWhileRescuing = false;

        if (!canAct || inCombat || hitWhileRescuing)
        {
            ClearRescue(false);
            return;
        }

        // 이미 맡은 대상이 있으면 그것을 유지합니다. 매번 다시 고르면 같은 거리의 두 대상 사이에서 떨립니다.
        if (m_rescueTarget != null && !IsRescueTargetUsable(m_rescueTarget, m_rescueInteractable))
        {
            ClearRescue(false);
        }

        if (m_rescueTarget == null)
        {
            if (!TrySelectRescueTarget(manager, out SquadMemberController target, out DownedAllyInteractable interactable))
            {
                return;
            }

            if (!manager.TryClaimAutoRescue(m_memberController, target))
            {
                // 다른 AI가 이미 맡았습니다. 나는 평소 행동을 계속합니다.
                return;
            }

            m_rescueTarget = target;
            m_rescueInteractable = interactable;
            m_rescueHoldTimer = 0.0f;
            m_rescueHoldStarted = false;
        }

        // 경로가 끊기면 취소하고 재시도 간격을 겁니다(§16).
        if (CalculatePathDistance(transform.position, m_rescueTarget.transform.position) < 0.0f)
        {
            ClearRescue(true);
        }
    }

    /// <summary>
    /// 구조할 다운 아군을 고릅니다(§16).
    /// </summary>
    /// <param name="manager">스쿼드 매니저입니다.</param>
    /// <param name="target">고른 대상입니다.</param>
    /// <param name="interactable">그 대상의 구조 상호작용입니다.</param>
    /// <returns>고를 대상이 있으면 true입니다.</returns>
    /// <remarks>
    /// 이미 누군가 붙어 있는 대상은 건너뜁니다("플레이어가 이미 구조 중이면 AI는 같은 구조를 시도하지
    /// 않는다"). 홀드 여부만이 아니라 <b>누가 붙었는지</b>를 보는 이유는 내 홀드도 홀드이기 때문입니다.
    /// <para>
    /// 접근 경로가 없는 대상도 후보에서 뺍니다("유효한 접근 경로가 없으면 자동 구조를 시작하지 않는다").
    /// 직선거리로 고르면 벽 너머 아군을 골라 놓고 벽에 붙어 비빕니다.
    /// </para>
    /// </remarks>
    private bool TrySelectRescueTarget(SquadManager manager, out SquadMemberController target, out DownedAllyInteractable interactable)
    {
        target = null;
        interactable = null;

        IReadOnlyList<SquadMemberController> members = manager.SquadMembers;
        if (members == null)
        {
            return false;
        }

        float bestPathDistance = float.MaxValue;

        for (int i = 0; i < members.Count; i++)
        {
            SquadMemberController candidate = members[i];
            if (candidate == null || candidate == m_memberController)
            {
                continue;
            }

            // 경로 실패로 접었던 대상은 재시도 간격이 지나기 전까지 다시 고르지 않습니다.
            if (candidate == m_rescueRetryBlockedTarget && Time.time < m_rescueRetryAllowedTime)
            {
                continue;
            }

            DownedAllyInteractable candidateInteractable = candidate.GetComponent<DownedAllyInteractable>();
            if (!IsRescueTargetUsable(candidate, candidateInteractable))
            {
                continue;
            }

            float flat = FlatDistance(transform.position, candidate.transform.position);
            if (m_rescueSearchRange > 0.0f && flat > m_rescueSearchRange)
            {
                continue;
            }

            float pathDistance = CalculatePathDistance(transform.position, candidate.transform.position);
            if (pathDistance < 0.0f || pathDistance >= bestPathDistance)
            {
                continue;
            }

            bestPathDistance = pathDistance;
            target = candidate;
            interactable = candidateInteractable;
        }

        return target != null;
    }

    /// <summary>
    /// 그 대상을 지금 구조할 수 있는지 확인합니다(§16).
    /// </summary>
    /// <param name="candidate">확인할 멤버입니다.</param>
    /// <param name="interactable">그 멤버의 구조 상호작용입니다.</param>
    /// <returns>구조 대상으로 쓸 수 있으면 true입니다.</returns>
    private bool IsRescueTargetUsable(SquadMemberController candidate, DownedAllyInteractable interactable)
    {
        if (candidate == null || interactable == null)
        {
            return false;
        }

        // 다운이 풀렸거나 죽었으면 대상이 무효화된 것입니다.
        if (!candidate.IsAlive || !candidate.IsDown)
        {
            return false;
        }

        // 나 말고 다른 누군가가 이미 붙어 있으면 끼어들지 않습니다.
        SquadMemberController activeInteractor = interactable.ActiveInteractorMember;
        return activeInteractor == null || activeInteractor == m_memberController;
    }

    /// <summary>
    /// 진행 중인 구조를 접습니다.
    /// </summary>
    /// <param name="blockRetry">경로 실패로 접는 것이면 true입니다. 재시도 간격을 겁니다(§16).</param>
    /// <remarks>
    /// 시작한 홀드는 반드시 취소해 줘야 합니다. <see cref="DownedAllyInteractable"/>가 홀드 중에
    /// 대상의 다운 타이머를 멈추고 구조자 쪽 애니메이터·상호작용 잠금을 걸어 두므로,
    /// 그냥 손을 놓으면 그 상태가 남습니다.
    /// </remarks>
    private void ClearRescue(bool blockRetry)
    {
        if (m_rescueTarget == null)
        {
            m_rescueHoldStarted = false;
            m_rescueHoldTimer = 0.0f;
            return;
        }

        if (m_rescueHoldStarted && m_rescueInteractable != null)
        {
            m_rescueInteractable.CancelHold(gameObject);
        }

        if (blockRetry)
        {
            m_rescueRetryBlockedTarget = m_rescueTarget;
            m_rescueRetryAllowedTime = Time.time + Mathf.Max(0.0f, m_rescueRetryInterval);
        }

        if (m_squadManager != null)
        {
            m_squadManager.ReleaseAutoRescue(m_memberController);
        }

        m_rescueTarget = null;
        m_rescueInteractable = null;
        m_rescueHoldStarted = false;
        m_rescueHoldTimer = 0.0f;
    }

    /// <summary>
    /// 구조 대상에 닿았으면 기립 홀드를 진행합니다(§16).
    /// </summary>
    /// <remarks>
    /// 홀드 진행도는 <b>부르는 쪽이 소유</b>합니다. <see cref="InteractionController"/>도 같은 방식으로
    /// <c>m_holdTimer</c>를 굴려 넘기므로, 사람과 AI가 같은 구조 구현을 그대로 씁니다. 실제 기립·애니메이터·
    /// 다운 타이머 처리는 전부 <see cref="DownedAllyInteractable"/> 안에 있습니다.
    /// </remarks>
    private void HandleRescueHold()
    {
        if (m_currentDecision.Kind != SquadAIActionKind.Rescue || m_rescueInteractable == null)
        {
            return;
        }

        if (!m_currentDecision.HoldPosition)
        {
            return;
        }

        if (!m_rescueHoldStarted)
        {
            m_rescueInteractable.BeginHold(gameObject);
            m_rescueHoldStarted = true;
            m_rescueHoldTimer = 0.0f;
        }

        float holdDuration = Mathf.Max(0.0f, m_rescueInteractable.HoldDuration);
        m_rescueHoldTimer += Time.deltaTime;

        float progress = holdDuration <= 0.0f
            ? 1.0f
            : Mathf.Clamp01(m_rescueHoldTimer / holdDuration);

        m_rescueInteractable.UpdateHold(gameObject, progress);

        if (m_rescueHoldTimer < holdDuration)
        {
            return;
        }

        // 홀드를 채웠습니다. 실제 기립은 상호작용 구현체가 합니다.
        m_rescueInteractable.Interact(gameObject);
        m_rescueInteractable.CompleteHold(gameObject);

        // 완료 후 상태를 다시 평가합니다(§16). 대상이 일어났으므로 선점을 놓으면
        // 다음 프레임 판정이 남은 다운 아군이나 일반 행동을 새로 고릅니다.
        m_rescueHoldStarted = false;
        ClearRescue(false);
    }

    /// <summary>
    /// 판정에 넘길 현재 사정을 모읍니다.
    /// </summary>
    /// <param name="canAct">지금 행동 요청을 낼 수 있는 상태인지입니다.</param>
    /// <param name="holdPosition">활동 제한 상태에서 이동을 멈춰 세워야 하는지입니다.</param>
    /// <returns>판정에 넘길 사정입니다.</returns>
    /// <remarks>
    /// 컴포넌트를 뒤지는 일은 여기서 끝냅니다. <see cref="SquadAIDecision"/>은 규칙만 들고 있어야
    /// 우선순위가 한눈에 읽힙니다. <see cref="SquadAITargeting"/>과 같은 방식입니다.
    /// </remarks>
    private SquadAIDecision.Context BuildDecisionContext(bool canAct, bool holdPosition)
    {
        return new SquadAIDecision.Context
        {
            CanAct = canAct,
            HoldPosition = holdPosition,
            HasMoveOrder = m_hasMoveOrder,

            // 대상 선정과 선점 중재는 UpdateRescue가 이미 끝냈습니다(§16).
            RescueRequested = m_rescueTarget != null,
            RescueInReach = m_rescueTarget != null
                            && FlatDistance(transform.position, m_rescueTarget.transform.position) <= m_rescueReachDistance,

            HasWeapon = m_weapon != null,
            IsReloading = m_weapon != null && m_weapon.IsReloading,
            CanStartReload = m_weapon != null && m_weapon.CanReload,
            CurrentBullet = m_weapon != null ? m_weapon.CurrentBullet : 0,
            TacticalReloadThreshold = m_tacticalReloadThreshold,
            FiringEnabled = AllowFiring,

            IsJoining = m_isJoining,
            HasTarget = m_targeting.CurrentTarget != null,
            HasAimPoint = m_hasAimPoint,
            CanFireAtTarget = m_targeting.CanFireAtCurrentTarget,
        };
    }

    /// <summary>
    /// 필요한 컴포넌트와 매니저 참조를 캐싱합니다.
    /// </summary>
    private void CacheRequiredReferences()
    {
        m_memberController = GetComponent<SquadMemberController>();
        m_agent = GetComponent<NavMeshAgent>();
        // FindFirstObjectByType는 Awake 순서에 따라 곧 제거될 중복 SquadManager를 잡을 수 있습니다.
        // 싱글턴 접근자를 사용하면 중복 정리가 끝난 뒤에도 현재 유효한 매니저로 다시 연결됩니다.
        m_squadManager = SquadManager.Instance;
        m_animator = GetComponent<Animator>();
        m_weapon = GetComponentInChildren<Gun>();
        m_aimController = GetComponent<AimController>();
        m_thirdPersonController = GetComponent<ThirdPersonController>();
    }

    /// <summary>
    /// 씬 초기화 순서나 중복 매니저 정리로 무효화된 런타임 참조를 다시 연결합니다.
    /// </summary>
    /// <returns>현재 AI 갱신에 필요한 멤버, Agent, SquadManager가 모두 유효하면 true입니다.</returns>
    /// <remarks>
    /// 플레이어 프리팹의 Awake가 SquadManager보다 먼저 실행되거나, 싱글턴 중복 정리로 처음 찾은 매니저가
    /// 파괴될 수 있습니다. 한 번 실패했다고 컴포넌트를 영구 비활성화하지 않고 다음 갱신/접촉에서 재탐색합니다.
    /// </remarks>
    private bool EnsureRequiredReferences()
    {
        if (m_memberController == null || m_agent == null || m_squadManager == null)
        {
            CacheRequiredReferences();
        }

        m_hasRequiredReferences = m_memberController != null && m_agent != null && m_squadManager != null;
        if (m_hasRequiredReferences)
        {
            m_agent.updateRotation = false;
        }

        return m_hasRequiredReferences;
    }

    /// <summary>
    /// 필수 참조가 누락되었는지 확인합니다.
    /// </summary>
    /// <returns>필수 참조가 모두 유효하면 true입니다.</returns>
    private bool ValidateRequiredReferences()
    {
        bool isValid = true;

        if (m_memberController == null)
        {
            Debug.LogError("[SquadAIController] SquadMemberController 컴포넌트가 없습니다.", this);
            isValid = false;
        }

        if (m_agent == null)
        {
            Debug.LogError("[SquadAIController] NavMeshAgent 컴포넌트가 없습니다.", this);
            isValid = false;
        }

        return isValid;
    }

    /// <summary>
    /// 현재 추종 가능한 상태인지 확인하고 리더 Transform을 반환합니다.
    /// </summary>
    /// <param name="leader">현재 조작 중인 리더 Transform입니다.</param>
    /// <returns>추종 가능한 상태이면 true입니다.</returns>
    private bool CanFollow(out Transform leader)
    {
        leader = null;

        if (m_memberController == null || !m_memberController.IsAlive || m_memberController.IsDown)
        {
            return false;
        }

        if (m_squadManager == null || m_squadManager.PlayerSquadMember == null)
        {
            return false;
        }

        if (m_agent == null || !m_agent.enabled)
        {
            return false;
        }

        leader = m_squadManager.PlayerSquadMember.transform;
        return leader != null;
    }

    /// <summary>
    /// 플레이어까지의 경로상 거리를 재고 합류 여부를 갱신합니다. 이동은 실행하지 않습니다.
    /// </summary>
    /// <param name="leader">따라갈 플레이어 조작 캐릭터의 Transform입니다.</param>
    /// <returns>이동할 수 있는 상태이면 true입니다. 경로가 없으면 false입니다.</returns>
    /// <remarks>
    /// 공용 문서 `스쿼드 AI 시스템` v0.2 §6 기준입니다. <b>직선거리를 쓰지 않습니다</b>(§6.2) - 벽 하나를
    /// 사이에 두면 직선으로는 가까워도 실제로는 멀리 돌아야 하므로 판정에 경로상 거리를 씁니다.
    /// <para>
    /// 여기서 갱신한 합류 여부를 <see cref="SquadAIDecision"/>이 §18.1 4번 입력으로 읽습니다. 그래서
    /// 이 함수는 판정보다 먼저 돌아야 하고, 판정과 실행 사이에 끼어들면 안 됩니다.
    /// </para>
    /// </remarks>
    private bool UpdateJoinState(Transform leader)
    {
        ApplyAvoidancePriority(ResolveMemberOrder());

        m_pathDistanceToLeader = CalculatePathDistance(transform.position, leader.position);

        // §17 경로 복구 판정을 먼저 돌립니다. 경로가 없는 경우도 여기서 함께 셉니다 - 문서가
        // "유효한 이동 경로를 찾지 못하거나 경로가 있는데도 진척이 없으면"을 한 조건으로 묶기 때문입니다.
        UpdateJoinRecovery(leader);

        // 경로가 아예 없으면 갈 방법이 없으므로 제자리를 지킵니다. 직선으로 밀어붙이면 벽에 붙어 비빕니다.
        if (m_pathDistanceToLeader < 0.0f)
        {
            StopAgent();
            m_isJoining = false;

            // 갈 수 없게 됐으므로 찜도 놓습니다. 남겨 두면 닿지도 못하는 자리를 계속 막습니다.
            ClearFollowDestination();

            // 합류를 접었으므로 달리기도 내립니다. 이 시점에는 이번 프레임 판정이 아직 없어
            // 직전 값을 쓰면 달리기 속도가 남습니다.
            ApplyFollowSpeed(leader, false);
            return false;
        }

        // 합류는 시작 거리에서 켜고 완료 거리에서 끕니다(§6.2). 한 거리로 판정하면 경계에서 뒤집힙니다.
        //
        // 완료 조건에 "목적지에 도착했는가"를 함께 보는 이유(실측): 목적지 반경은 직선거리로 잡고
        // 완료 판정은 경로상 거리로 하기 때문에, 목적지에 이미 서 있는데도 경로상으로는 완료 거리를
        // 살짝 넘어 합류가 안 풀리는 구간이 생깁니다. §6.1의 "합류 완료 거리 안의 유효 위치에 있으면
        // 위치 조정을 멈춘다"가 이 경우의 기준이므로 도착 여부를 함께 봅니다.
        bool arrivedAtDestination = m_hasDestination
                                    && FlatDistance(transform.position, m_destination) <= ResolveArrivalDistance();

        if (m_isJoining)
        {
            if (m_pathDistanceToLeader <= m_joinCompleteDistance || arrivedAtDestination)
            {
                m_isJoining = false;
            }
        }
        else if (m_pathDistanceToLeader > m_joinStartDistance)
        {
            m_isJoining = true;
        }

        return true;
    }

    /// <summary>
    /// 합류가 막혔는지 판정하고, 확정되면 재배치로 복구합니다(§17).
    /// </summary>
    /// <param name="leader">따라갈 플레이어 조작 캐릭터의 Transform입니다.</param>
    /// <remarks>
    /// 공용 문서 `스쿼드 AI 시스템` §17과 §18.2의 "경로 단절로 합류 불가능 -> 재탐색 후 지속 실패 시
    /// 유효 위치로 재배치"가 정본입니다. §18.1에서는 4번(합류와 경로 복구) 안에 들어가므로 별도
    /// <see cref="SquadAIActionKind"/> 값을 만들지 않습니다. 재배치는 이동 요청이 아니라 한 순간의
    /// 복구 동작이어서 이동 축의 배타 선택에 넣을 것이 없습니다.
    ///
    /// <para>
    /// <b>거리만으로 실패를 확정하지 않습니다</b>(§17 두 번째 규칙). 멀다는 것과 갈 수 없다는 것은 다릅니다.
    /// 그래서 판정 기준은 두 가지뿐입니다 - 경로가 없거나, 경로가 있는데도 진척이 없는 것.
    /// </para>
    ///
    /// <para>
    /// <b>재장전과 구조 중에는 시간을 세지 않습니다</b>(§17). §18.2가 "재장전 중 합류에 달리기 필요 ->
    /// 재장전 완료 후 달리기 재평가"로 규정하므로 그 구간은 원래 느립니다. 그 느림을 실패로 세면
    /// 재장전할 때마다 재배치가 터집니다.
    /// </para>
    ///
    /// <para>
    /// 합류 중이 아닐 때는 판정을 돌리지 않고 상태를 비웁니다. 완료 거리 안에서 제자리를 지키는 것은
    /// 진척이 없는 정상 상태이며(§6.4 5번), 그것까지 세면 가만히 있는 팀원이 재배치됩니다.
    /// </para>
    /// </remarks>
    private void UpdateJoinRecovery(Transform leader)
    {
        // 간격은 호출 시각의 차이로 잽니다(m_lastJoinCheckTime 주석 참조). 갱신은 어느 분기로 빠지든
        // 먼저 해야 합니다 - 이르게 return하는 경로에서 빼먹으면 그 사이 흐른 시간이 다음 호출에
        // 한꺼번에 얹힙니다.
        float now = Time.time;
        float elapsed = m_lastJoinCheckTime < 0.0f ? 0.0f : Mathf.Max(0.0f, now - m_lastJoinCheckTime);
        m_lastJoinCheckTime = now;

        bool pathMissing = m_pathDistanceToLeader < 0.0f;

        // 합류할 이유가 없고 경로도 정상이면 판정 대상이 아닙니다.
        if (!m_isJoining && !pathMissing)
        {
            ResetJoinRecovery();
            return;
        }

        // 정상적으로 이동하지 못하는 행동 중인 시간은 실패 판정에서 제외합니다(§17).
        // 기준 위치도 갱신하지 않습니다. 갱신하면 그 구간에 움직인 만큼이 다음 비교의 기준이 되어
        // 재장전이 끝난 직후 한 번은 무조건 진척으로 잡힙니다.
        bool cannotMoveNormally = m_currentDecision.Kind == SquadAIActionKind.Rescue
                                  || (m_weapon != null && m_weapon.IsReloading);

        if (cannotMoveNormally)
        {
            return;
        }

        // 기준 위치가 없으면 지금 자리로 세웁니다. 이 상태는 ClearFollowState를 거친 직후에만
        // 나오고 그때는 m_lastJoinCheckTime도 함께 무효화되어 elapsed가 0이므로, 여기서 따로
        // 빠져나가지 않아도 이번 회차가 정체로 세어지지 않습니다.
        if (!m_hasJoinProgressPosition)
        {
            m_joinProgressPosition = transform.position;
            m_hasJoinProgressPosition = true;
        }

        // 진척은 "스스로 이만큼 움직였는가"로 봅니다(m_joinProgressPosition 주석 참조).
        // 경로가 없으면 움직였더라도 목적지에 다가가는 것이 아니므로 진척으로 치지 않습니다.
        bool improved = !pathMissing
                        && FlatDistance(transform.position, m_joinProgressPosition) >= m_joinProgressEpsilon;

        if (improved)
        {
            ResetJoinRecovery();
            return;
        }

        m_joinStallTimer += elapsed;

        if (m_joinStallTimer < Mathf.Max(0.0f, m_joinStallTime))
        {
            return;
        }

        // 정해진 시간을 넘겼으므로 한 번의 재탐색으로 셉니다. 기준 위치를 지금 자리로 다시 잡아
        // 다음 구간을 새로 재게 합니다.
        m_joinStallTimer = 0.0f;
        m_joinRepathCount++;
        m_joinProgressPosition = transform.position;

        if (m_joinRepathCount <= Mathf.Max(0, m_joinRepathLimit))
        {
            // 아직 재탐색 여유가 있습니다. 다음 갱신에서 경로를 다시 계산하므로 여기서 할 일은 없습니다.
            return;
        }

        m_joinFailed = true;

        if (!m_repositionOnJoinFailure)
        {
            // 토글이 꺼져 있으면 위치를 건드리지 않습니다. 이 경우 팀원은 그 자리에 남습니다.
            Debug.LogWarning(
                $"[SquadAIController] {name}: 합류 실패를 확정했지만 재배치가 꺼져 있어 위치를 보정하지 않았습니다. " +
                $"재탐색 {m_joinRepathCount}회, 경로상 거리 {(pathMissing ? "없음" : m_pathDistanceToLeader.ToString("F2"))}.",
                this);
            return;
        }

        // 경로가 없는 이유가 플레이어가 지형 오브젝트 위에 올라가 있어서라면 옮기지 않습니다.
        // 유효 위치가 없을 때와 같이 실패 상태를 유지한 채 다음 주기에 다시 봅니다.
        if (pathMissing && IsLeaderOnNoRepositionGround(leader))
        {
            m_joinRepathCount = Mathf.Max(0, m_joinRepathLimit);
            return;
        }

        if (TryRepositionNearLeader(leader))
        {
            // §17: 재배치 후 거리와 전투 상태를 다시 평가한다. 다음 갱신이 경로상 거리를 새로 재므로
            // 여기서는 판정 상태만 비웁니다.
            ResetJoinRecovery();
            m_joinFailed = false;
            return;
        }

        // §17, §18.2: 유효한 재배치 위치가 없으면 강제로 옮기지 않고 경로 재탐색을 계속한다.
        // 그래서 실패 상태는 유지한 채 재탐색 횟수만 되돌려 다음 주기에 다시 시도하게 합니다.
        m_joinRepathCount = Mathf.Max(0, m_joinRepathLimit);
    }

    /// <summary>
    /// 플레이어가 재배치하지 않을 레이어(<see cref="m_noRepositionLeaderGroundLayers"/>) 위에 서 있는지 봅니다.
    /// </summary>
    /// <param name="leader">따라갈 플레이어 조작 캐릭터의 Transform입니다.</param>
    /// <returns>발밑 가장 가까운 지면이 그 레이어면 true입니다.</returns>
    /// <remarks>
    /// 가장 가까운 지면 하나만 봅니다. 지정 레이어만 골라 쏘면, 지면 아래 깔린 다른 오브젝트에 맞아도 위에 서 있다고 잘못 판정합니다.
    /// 플레이어 자신의 콜라이더는 건너뜁니다.
    /// </remarks>
    private bool IsLeaderOnNoRepositionGround(Transform leader)
    {
        if (leader == null || m_noRepositionLeaderGroundLayers.value == 0)
        {
            return false;
        }

        RaycastHit[] hits = Physics.RaycastAll(
            leader.position + Vector3.up * LeaderGroundProbeUp,
            Vector3.down,
            LeaderGroundProbeUp + LeaderGroundProbeDistance,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);

        float nearest = float.MaxValue;
        int nearestLayer = -1;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(leader))
            {
                continue;
            }

            if (hit.distance < nearest)
            {
                nearest = hit.distance;
                nearestLayer = hit.collider.gameObject.layer;
            }
        }

        return nearestLayer >= 0 && (m_noRepositionLeaderGroundLayers.value & (1 << nearestLayer)) != 0;
    }

    /// <summary>합류 실패 판정 상태를 비웁니다.</summary>
    private void ResetJoinRecovery()
    {
        m_joinStallTimer = 0.0f;
        m_joinRepathCount = 0;
        m_joinFailed = false;

        // 진척 기준을 지금 자리로 당겨 둡니다. 옛 자리를 남겨 두면 다음 회차에 그 사이 움직인
        // 거리가 진척으로 잡혀, 실제로는 막혀 있는데 한 번 통과하게 됩니다.
        m_joinProgressPosition = transform.position;
        m_hasJoinProgressPosition = true;
    }

    /// <summary>
    /// 플레이어 근처의 유효 위치를 찾아 이 멤버를 재배치합니다(§17).
    /// </summary>
    /// <param name="leader">따라갈 플레이어 조작 캐릭터의 Transform입니다.</param>
    /// <returns>재배치했으면 true입니다. 유효 위치가 없거나 이동할 수 없는 상태면 false입니다.</returns>
    /// <remarks>
    /// 실제로 위치를 옮기는 절차는 <see cref="SquadMemberController.TryRepositionTo"/>가 합니다.
    /// §4.2가 공간 이동 상태를 캐릭터 소유로 두기 때문이며, 여기서는 <b>어디로</b>만 정합니다.
    /// </remarks>
    private bool TryRepositionNearLeader(Transform leader)
    {
        if (m_memberController == null || leader == null)
        {
            return false;
        }

        if (!TrySelectRepositionPosition(leader, out Vector3 position))
        {
            return false;
        }

        if (!m_memberController.TryRepositionTo(position))
        {
            return false;
        }

        // 옮긴 자리를 그대로 목적지로 두면 다음 갱신까지 옛 목적지로 향합니다. 찜도 함께 놓습니다.
        ClearFollowDestination();
        m_isJoining = false;

        Debug.LogWarning(
            $"[SquadAIController] {name}: 합류 경로를 복구하지 못해 플레이어 근처로 재배치했습니다(§17). " +
            $"재탐색 {m_joinRepathCount}회.",
            this);

        return true;
    }

    /// <summary>
    /// 플레이어 주변에서 재배치할 유효 위치를 고릅니다(§17).
    /// </summary>
    /// <param name="leader">따라갈 플레이어 조작 캐릭터의 Transform입니다.</param>
    /// <param name="position">고른 자리입니다.</param>
    /// <returns>유효 위치를 찾았으면 true입니다.</returns>
    /// <remarks>
    /// <b>합류 목적지 탐색(<see cref="TrySelectNearestFreePosition"/>)을 재사용할 수 없습니다.</b>
    /// 그쪽은 후보를 "지금 위치에서 걸어갈 수 있는가"로 걸러내는데, 재배치가 필요한 상황은 정의상
    /// 걸어갈 수 있는 자리가 없는 상황입니다. 같은 필터를 쓰면 후보가 전부 탈락해 영원히 재배치되지 않습니다.
    ///
    /// <para>
    /// 그래서 남기는 조건은 §17이 정한 두 가지입니다 - 이동 가능한 지면인가(NavMesh 표본), 그리고
    /// 캐릭터와 겹치지 않는가(<see cref="IsPositionClear"/>). 플레이어 카메라 가시성은 조건으로 쓰지
    /// 않습니다(§17 명시).
    /// </para>
    ///
    /// <para>
    /// 플레이어에게 가까운 자리를 먼저 고릅니다. 재배치는 전술 행동이 아니라 예외 복구이므로(§17)
    /// 좋은 자리를 찾는 것이 아니라 합류 상태로 되돌리는 것이 목적입니다.
    /// </para>
    /// </remarks>
    private bool TrySelectRepositionPosition(Transform leader, out Vector3 position)
    {
        position = leader.position;

        int candidateCount = Mathf.Max(1, m_destinationCandidateCount);
        float radius = Mathf.Max(0.1f, m_joinCompleteDistance * DestinationRadiusRatio);
        float angleStep = 360.0f / candidateCount;

        // 후보 고리를 멤버 순번으로 엇갈리게 돌립니다. 두 팀원이 동시에 실패하면 같은 자리를 고릅니다.
        float angleOffset = angleStep * (ResolveMemberOrder() / (float)Mathf.Max(1, ResolveAiMemberCount()));

        bool found = false;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < candidateCount; i++)
        {
            float angle = angleOffset + angleStep * i;
            Vector3 offset = Quaternion.Euler(0.0f, angle, 0.0f) * (Vector3.forward * radius);
            Vector3 candidate = leader.position + offset;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, DestinationSampleRadius, NavFilter))
            {
                continue;
            }

            if (!IsPositionClear(hit.position))
            {
                continue;
            }

            float distance = FlatDistance(hit.position, leader.position);
            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            position = hit.position;
            found = true;
        }

        return found;
    }

    /// <summary>
    /// 확정된 행동 요청에 맞춰 이동만 실행합니다.
    /// </summary>
    /// <param name="leader">따라갈 플레이어 조작 캐릭터의 Transform입니다.</param>
    /// <remarks>
    /// 여기서는 "어느 행동인가"를 다시 판단하지 않습니다. 그것은 <see cref="SquadAIDecision"/>이 §18.1
    /// 순서대로 이미 정했습니다. 이 함수가 하는 판단은 <b>그 행동을 이룰 자리가 실제로 있는가</b>뿐입니다.
    /// <para>
    /// 전투 위치를 찾지 못하면 동행 경로로 흘러내립니다. §10.2가 "조건을 충족하는 위치가 없으면 무리하게
    /// 이동하지 않고 동행 행동을 유지한다"고 규정하므로, 자리가 없을 때 제자리에서 쏘는 것이 맞습니다.
    /// </para>
    /// </remarks>
    private void ExecuteMovement(Transform leader)
    {
        SquadAIActionKind kind = m_currentDecision.Kind;

        // 구조는 다른 무엇보다 우선이므로 리더 쪽 목적지를 아예 보지 않습니다(§16 "접근부터 구조 완료까지
        // 동행, 합류, 재장전, 자세 동조와 위치 조정보다 구조를 우선한다"). 플레이어가 합류 시작 거리 밖으로
        // 나가도 구조를 끝낸 뒤에 합류합니다.
        if (kind == SquadAIActionKind.Rescue)
        {
            if (m_currentDecision.HoldPosition || m_rescueTarget == null)
            {
                // 닿았으면 멈춰 서서 기립시킵니다. 접근 중 이동은 걷기입니다.
                StopAgent();
                ClearFollowDestination();
                ApplyFollowSpeed(leader, false);
                return;
            }

            SetFollowDestination(m_rescueTarget.transform.position);

            ApplyFollowSpeed(leader, false);
            m_agent.isStopped = false;
            m_agent.SetDestination(m_destination);
            return;
        }

        // 이동·사수 명령은 지정한 자리만 봅니다. 도착하면 멈춰 서고, 밀려나면 다음 갱신에 되돌아갑니다.
        if (kind == SquadAIActionKind.MoveOrder)
        {
            if (FlatDistance(transform.position, m_moveOrderPosition) <= m_moveOrderArriveDistance)
            {
                StopAgent();
                ClearFollowDestination();
                ApplyFollowSpeed(leader, false);
                return;
            }

            SetFollowDestination(m_moveOrderPosition);
            ApplyFollowSpeed(leader, m_currentDecision.Sprint);
            m_agent.isStopped = false;
            m_agent.SetDestination(m_moveOrderPosition);
            return;
        }

        if (kind == SquadAIActionKind.Combat && TryResolveCombatPosition(leader, out Vector3 combatPosition))
        {
            SetFollowDestination(combatPosition);

            // 전투 중 이동은 걷기입니다. 달리면 조준이 흔들리고, §10.3도 개인적인 회피를 위한
            // 달리기 반복을 금지합니다. 판정이 Combat에는 달리기를 얹지 않으므로 그대로 따릅니다.
            ApplyFollowSpeed(leader, m_currentDecision.Sprint);

            m_agent.isStopped = false;
            m_agent.SetDestination(combatPosition);
            return;
        }

        // 완료 반경 안에 이미 서 있고 자리가 유효하면 위치를 조정하지 않습니다(§6.1).
        // 합류 중에는 아직 도착하지 않은 것이므로 이 분기를 타지 않습니다.
        if (kind != SquadAIActionKind.Join
            && m_pathDistanceToLeader <= m_joinCompleteDistance
            && IsPositionClear(transform.position))
        {
            StopAgent();
            ClearFollowDestination();
            ApplyFollowSpeed(leader, m_currentDecision.Sprint);
            return;
        }

        if (!TryResolveDestination(leader, out Vector3 destination))
        {
            // 갈 자리를 못 찾았으면 찜도 놓습니다. 들고 있으면 못 가는 자리를 계속 막습니다.
            StopAgent();
            ClearFollowDestination();
            ApplyFollowSpeed(leader, m_currentDecision.Sprint);
            return;
        }

        SetFollowDestination(destination);

        ApplyFollowSpeed(leader, m_currentDecision.Sprint);

        m_agent.isStopped = false;
        m_agent.SetDestination(destination);
    }

    /// <summary>
    /// 전투 중 자리를 옮겨야 하는지 판단하고, 옮긴다면 목적지를 정합니다(§10.2, §10.3).
    /// </summary>
    /// <param name="leader">기준이 되는 플레이어 조작 캐릭터입니다.</param>
    /// <param name="destination">확정된 전투 위치입니다.</param>
    /// <returns>자리를 옮겨야 하면 true입니다.</returns>
    /// <remarks>
    /// <b>기본은 안 움직이는 것입니다</b>(§10.2 "현재 위치에서 사격할 수 있으면 불필요하게 이동하지 않는다").
    /// 두 경우에만 움직입니다.
    /// <para>
    /// 1. <b>너무 붙었을 때</b>(§10.3) — 근접 변이체가 <see cref="m_keepAwayDistance"/> 안으로 들어오면
    ///    적 반대 방향으로 물러납니다. 물러나면서도 조준과 사격은 계속합니다.
    /// 2. <b>사격선이 지속적으로 막혔을 때</b>(§10.2) — 뚫리는 자리를 찾습니다. 일시적인 가림으로는
    ///    움직이지 않습니다. 동료가 잠깐 스칠 때마다 자리를 옮기면 산만해집니다.
    /// </para>
    /// <para>
    /// <b>자리가 없으면 그냥 서서 쏩니다</b>(§10.2 "조건을 충족하는 위치가 없으면 무리하게 이동하지 않고
    /// 동행 행동을 유지한다"). 뒤가 벽이면 벽에 비비지 않고 제자리에서 사격하는 것이 맞습니다.
    /// </para>
    /// <para>
    /// 합류가 필요한 상황은 호출자가 먼저 걸러 냅니다. 합류가 전투 위치 조정보다 우선입니다(§18.1).
    /// </para>
    /// </remarks>
    private bool TryResolveCombatPosition(Transform leader, out Vector3 destination)
    {
        destination = transform.position;

        EnemyController target = m_targeting.CurrentTarget;
        if (target == null || !m_hasAimPoint)
        {
            return false;
        }

        if (Time.time < m_nextCombatPositionTime)
        {
            // 주기 사이에는 앞서 고른 자리를 유지합니다. 매번 다시 고르면 목적지가 미세하게 흔들립니다.
            if (m_hasCombatPosition)
            {
                destination = m_combatPosition;
                return true;
            }

            return false;
        }

        m_nextCombatPositionTime = Time.time + Mathf.Max(0.05f, m_combatPositionInterval);

        Vector3 enemyPos = target.transform.position;
        float distanceToEnemy = FlatDistance(transform.position, enemyPos);

        bool tooClose = distanceToEnemy < m_keepAwayDistance;
        bool blockedTooLong = !m_targeting.CanFireAtCurrentTarget && m_targeting.IsFiringLineBlockedLong;

        if (!tooClose && !blockedTooLong)
        {
            m_hasCombatPosition = false;
            return false;
        }

        if (TrySelectCombatPosition(leader, target, tooClose, out Vector3 picked))
        {
            m_combatPosition = picked;
            m_hasCombatPosition = true;
            destination = picked;
            return true;
        }

        // 갈 자리가 없습니다. 억지로 움직이지 않고 제자리에서 쏩니다.
        m_hasCombatPosition = false;
        return false;
    }

    /// <summary>
    /// 전투 위치 후보 중 조건을 만족하는 가장 가까운 자리를 고릅니다(§10.2).
    /// </summary>
    /// <param name="leader">플레이어 조작 캐릭터입니다. 합류 완료 반경의 기준입니다.</param>
    /// <param name="target">현재 대상입니다.</param>
    /// <param name="tooClose">거리를 벌려야 하는 상황인지 여부입니다.</param>
    /// <param name="destination">선택된 자리입니다.</param>
    /// <returns>쓸 만한 자리를 찾았으면 true입니다.</returns>
    /// <remarks>
    /// 후보는 <b>플레이어 주변 합류 완료 반경 안</b>으로 제한합니다(§10.2). 그래야 적을 피하다가
    /// 플레이어에게서 떨어져 곧바로 합류로 넘어가는 일이 없습니다.
    /// 거리를 벌리는 경우에는 적에게서 더 멀어지는 자리만 후보로 인정합니다.
    /// </remarks>
    private bool TrySelectCombatPosition(Transform leader, EnemyController target, bool tooClose, out Vector3 destination)
    {
        destination = transform.position;

        Vector3 enemyPos = target.transform.position;
        Vector3 away = transform.position - enemyPos;
        away.y = 0.0f;

        if (away.sqrMagnitude < 0.0001f)
        {
            away = -transform.forward;
        }

        away.Normalize();

        float currentDistance = FlatDistance(transform.position, enemyPos);
        float leaderRadius = Mathf.Max(0.1f, m_joinCompleteDistance);

        bool found = false;
        float bestPathDistance = float.MaxValue;

        // 정면 후퇴부터 좌우로 벌려 가며 훑습니다. 뒤가 막혔으면 비스듬히라도 빠지는 편이 자연스럽습니다.
        for (int i = 0; i < CombatPositionCandidateCount; i++)
        {
            float angle = ResolveRetreatAngle(i);
            Vector3 dir = Quaternion.Euler(0.0f, angle, 0.0f) * away;
            Vector3 candidate = transform.position + dir * m_retreatStep;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, DestinationSampleRadius, NavFilter))
            {
                continue;
            }

            // 플레이어에게서 너무 떨어지면 전투 위치가 아니라 이탈입니다.
            if (FlatDistance(hit.position, leader.position) > leaderRadius)
            {
                continue;
            }

            if (!IsPositionClear(hit.position))
            {
                continue;
            }

            if (tooClose && FlatDistance(hit.position, enemyPos) <= currentDistance)
            {
                // 물러나려는 것이므로 더 가까워지는 자리는 의미가 없습니다.
                continue; 
            }

            if (!HasFiringLineFrom(hit.position, target))
            {
                continue;
            }

            float pathDistance = CalculatePathDistance(transform.position, hit.position);
            if (pathDistance < 0.0f || pathDistance >= bestPathDistance)
            {
                continue;
            }

            bestPathDistance = pathDistance;
            destination = hit.position;
            found = true;
        }

        return found;
    }

    /// <summary>후퇴 후보 각도를 정면부터 좌우로 번갈아 벌려 돌려줍니다.</summary>
    /// <param name="index">후보 순번입니다.</param>
    /// <returns>후퇴 방향에 더할 각도입니다.</returns>
    private static float ResolveRetreatAngle(int index)
    {
        int step = (index + 1) / 2;
        float sign = index % 2 == 0 ? 1.0f : -1.0f;
        return sign * step * RetreatAngleStep;
    }

    /// <summary>
    /// 지정한 자리에서 대상에게 사격선이 열리는지 확인합니다(§10.2).
    /// </summary>
    /// <param name="from">확인할 자리입니다.</param>
    /// <param name="target">현재 대상입니다.</param>
    /// <returns>환경 장애물에 막히지 않으면 true입니다.</returns>
    /// <remarks>
    /// 아군 가림은 보지 않습니다. 아군도 함께 움직이므로 이동이 끝날 때쯤이면 판정이 이미 달라져 있고,
    /// 그것까지 맞추려 들면 서로를 피해 다니느라 자리가 정해지지 않습니다.
    /// </remarks>
    private bool HasFiringLineFrom(Vector3 from, EnemyController target)
    {
        Vector3 eye = from + Vector3.up * m_eyeHeight;
        Vector3 delta = target.transform.position + Vector3.up - eye;
        float distance = delta.magnitude;

        if (distance <= 0.0001f)
        {
            return true;
        }

        int mask = m_squadManager != null ? m_squadManager.IntelObstacleMask : 0;
        return !Physics.Raycast(eye, delta / distance, distance, mask, QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// 합류 목적지를 고릅니다. 기존 목적지가 아직 쓸 만하면 그대로 유지합니다.
    /// </summary>
    /// <param name="leader">기준이 되는 플레이어 조작 캐릭터입니다.</param>
    /// <param name="destination">확정된 목적지입니다.</param>
    /// <returns>쓸 수 있는 목적지를 찾았으면 true입니다.</returns>
    /// <remarks>
    /// §6.3: "기존 목적지가 계속 합류 완료 거리 안에 있고 접근 가능하면 다시 선택하지 않는다."
    /// 이 유지 규칙이 고정 자리 없이도 목적지가 안정되게 만드는 부분입니다.
    /// </remarks>
    private bool TryResolveDestination(Transform leader, out Vector3 destination)
    {
        if (m_hasDestination && IsDestinationStillUsable(leader, m_destination))
        {
            destination = m_destination;
            return true;
        }

        return TrySelectNearestFreePosition(leader, out destination);
    }

    /// <summary>기존 목적지를 계속 쓸 수 있는지 확인합니다.</summary>
    /// <param name="leader">기준이 되는 플레이어 조작 캐릭터입니다.</param>
    /// <param name="destination">확인할 목적지입니다.</param>
    /// <returns>완료 반경 안이고 접근 가능하며 비어 있으면 true입니다.</returns>
    private bool IsDestinationStillUsable(Transform leader, Vector3 destination)
    {
        Vector3 toLeader = destination - leader.position;
        toLeader.y = 0.0f;
        if (toLeader.sqrMagnitude > m_joinCompleteDistance * m_joinCompleteDistance)
        {
            return false;
        }

        if (!IsPositionClear(destination))
        {
            return false;
        }

        return CalculatePathDistance(transform.position, destination) >= 0.0f;
    }

    /// <summary>
    /// 플레이어 주변 완료 반경 안에서 가장 짧게 갈 수 있는 빈 자리를 고릅니다(§6.3).
    /// </summary>
    /// <param name="leader">기준이 되는 플레이어 조작 캐릭터입니다.</param>
    /// <param name="destination">선택된 목적지입니다.</param>
    /// <returns>후보를 찾았으면 true입니다.</returns>
    /// <remarks>
    /// 후보 고리를 멤버 순번으로 <b>서로 엇갈리게</b> 돌려 둡니다. 같은 고리를 쓰면 두 멤버가 같은 후보
    /// 집합에서 "경로상 가장 가까운 자리"를 고르게 되고, 둘이 같은 방향에서 오면 같은 자리를 고릅니다.
    /// 고정 자리를 주는 것이 아니라 후보 위상만 어긋나게 하는 것이라 §6.1의 `슬롯별 고정 위치` 금지에
    /// 걸리지 않습니다.
    /// <para>
    /// <b>나누는 값은 AI 인원수입니다</b>(후보 수가 아닙니다). 후보 수로 나누면 8후보 기준 5.6도밖에
    /// 벌어지지 않아 1.5m 고리에서 0.15m 차이가 됩니다. 즉 두 고리가 사실상 겹칩니다(실측으로 확인한
    /// 결함). 인원수로 나누면 2인 기준 22.5도가 되어 후보가 서로의 사이사이에 놓입니다.
    /// </para>
    /// </remarks>
    private bool TrySelectNearestFreePosition(Transform leader, out Vector3 destination)
    {
        destination = leader.position;

        int candidateCount = Mathf.Max(1, m_destinationCandidateCount);
        float radius = Mathf.Max(0.1f, m_joinCompleteDistance * DestinationRadiusRatio);
        float angleStep = 360.0f / candidateCount;
        float angleOffset = angleStep * (ResolveMemberOrder() / (float)Mathf.Max(1, ResolveAiMemberCount()));

        bool found = false;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < candidateCount; i++)
        {
            float angle = angleOffset + angleStep * i;
            Vector3 offset = Quaternion.Euler(0.0f, angle, 0.0f) * (Vector3.forward * radius);
            Vector3 candidate = leader.position + offset;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, DestinationSampleRadius, NavFilter))
            {
                continue;
            }

            if (!IsPositionClear(hit.position))
            {
                continue;
            }

            float pathDistance = CalculatePathDistance(transform.position, hit.position);
            if (pathDistance < 0.0f || pathDistance >= bestDistance)
            {
                continue;
            }

            bestDistance = pathDistance;
            destination = hit.position;
            found = true;
        }

        return found;
    }

    /// <summary>
    /// 직접 조작 중인 플레이어가 통로를 지나갈 수 있도록, 이 AI를 짧게 옆 또는 뒤의 빈 NavMesh 위치로 이동시킵니다.
    /// </summary>
    /// <param name="playerPosition">충돌을 일으킨 직접 조작 멤버의 현재 위치입니다.</param>
    /// <param name="playerMoveDirection">직접 조작 멤버가 통로를 지나려는 수평 이동 방향입니다.</param>
    /// <returns>양보 상태를 시작했거나 이미 양보 중이면 true입니다.</returns>
    /// <remarks>
    /// 물리 힘이나 Transform 강제 이동을 쓰지 않습니다. NavMesh 경로, 현재 멤버 간 간격, 다른 AI의 목적지 점유를
    /// 모두 통과한 후보만 선택하므로 문턱/코너에서 동료를 벽 안으로 밀어 넣지 않습니다.
    /// </remarks>
    public bool TryBeginPlayerYield(Vector3 playerPosition, Vector3 playerMoveDirection)
    {
        if (!CanYieldToPlayer)
        {
            return false;
        }

        if (m_isYieldingToPlayer)
        {
            return true;
        }

        if (Time.time < m_nextPlayerYieldRequestTime)
        {
            return false;
        }

        m_nextPlayerYieldRequestTime = Time.time + Mathf.Max(0.0f, m_playerYieldRequestCooldown);

        playerMoveDirection.y = 0.0f;
        if (playerMoveDirection.sqrMagnitude <= MoveDirectionThreshold * MoveDirectionThreshold)
        {
            return false;
        }

        if (!TrySelectPlayerYieldDestination(playerPosition, playerMoveDirection.normalized, out Vector3 destination))
        {
            return false;
        }

        // 기존 추종 목적지의 점유를 먼저 풀어야 새 양보 후보와 다른 AI 목적지가 불필요하게 서로 막히지 않습니다.
        ClearFollowDestination();

        m_playerYieldDestination = destination;
        m_playerYieldEndTime = Time.time + Mathf.Max(0.05f, m_playerYieldDuration);
        m_preYieldStoppingDistance = m_agent.stoppingDistance;
        m_preYieldSpeed = m_agent.speed;
        m_preYieldAvoidancePriority = m_agent.avoidancePriority;
        m_isYieldingToPlayer = true;

        // 큰 숫자는 낮은 우선순위입니다. 지나가는 플레이어가 계속 움직일 수 있도록 회피 우선권을 양보합니다.
        m_agent.avoidancePriority = 99;
        // 추종용 정지 반경(예: 1.5m)을 그대로 쓰면 0.9m 옆 목적지는 이미 도착한 것으로 처리됩니다.
        m_agent.stoppingDistance = Mathf.Max(0.05f, m_agent.radius * 0.25f);
        m_agent.speed = Mathf.Max(m_agent.speed, m_playerYieldSpeed);
        m_agent.isStopped = false;
        if (!m_agent.SetDestination(m_playerYieldDestination))
        {
            EndPlayerYield();
            return false;
        }
        return true;
    }

    /// <summary>플레이어 진행 방향의 좌우, 마지막으로 뒤쪽 순서로 안전한 양보 위치를 고릅니다.</summary>
    private bool TrySelectPlayerYieldDestination(Vector3 playerPosition, Vector3 playerMoveDirection, out Vector3 destination)
    {
        destination = transform.position;

        float distance = Mathf.Max(0.1f, m_playerYieldDistance);
        Vector3 side = Vector3.Cross(Vector3.up, playerMoveDirection).normalized;
        Vector3[] offsets =
        {
            side * distance,
            -side * distance,
            -playerMoveDirection * distance
        };

        bool found = false;
        float bestPathDistance = float.MaxValue;
        float minimumPlayerClearance = Mathf.Max(0.2f, m_agent.radius * 2.0f);

        for (int i = 0; i < offsets.Length; i++)
        {
            // 좌우 후보가 하나라도 있으면 뒤로 물러나는 후보는 보지 않습니다. 플레이어와 마주친 AI가
            // 복도 진행축을 따라 후퇴하기보다 통로 옆으로 빠지는 동작을 우선해야 합니다.
            if (i == 2 && found)
            {
                break;
            }

            Vector3 offset = offsets[i];
            Vector3 candidate = transform.position + offset;
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, m_playerYieldSampleRadius, NavFilter))
            {
                continue;
            }

            Vector3 fromPlayer = hit.position - playerPosition;
            fromPlayer.y = 0.0f;
            Vector3 actualOffset = hit.position - transform.position;
            actualOffset.y = 0.0f;

            // SamplePosition이 현재 자리 쪽으로 되감긴 후보는 실제로 비켜나지 않으므로 제외합니다.
            float requiredDisplacement = distance * 0.5f;
            float usefulDisplacement = i < 2
                ? Mathf.Abs(Vector3.Dot(actualOffset, side))
                : -Vector3.Dot(actualOffset, playerMoveDirection);
            if (usefulDisplacement < requiredDisplacement ||
                fromPlayer.sqrMagnitude < minimumPlayerClearance * minimumPlayerClearance ||
                !IsPositionClear(hit.position))
            {
                continue;
            }

            float pathDistance = CalculatePathDistance(transform.position, hit.position);
            if (pathDistance < 0.0f || pathDistance >= bestPathDistance)
            {
                continue;
            }

            bestPathDistance = pathDistance;
            destination = hit.position;
            found = true;
        }

        return found;
    }

    /// <summary>진행 중인 플레이어 양보 이동을 갱신하고, 끝나면 일반 AI 이동을 즉시 재개할 수 있게 정리합니다.</summary>
    /// <returns>이번 프레임에 양보 이동이 일반 AI 이동을 대신해야 하면 true입니다.</returns>
    private bool UpdatePlayerYield()
    {
        if (!m_isYieldingToPlayer)
        {
            return false;
        }

        bool agentUsable = m_agent != null && m_agent.enabled && m_agent.isOnNavMesh;
        // SetDestination 직후 remainingDistance는 경로 계산 전에 0으로 보일 수 있습니다. 실제 Transform이
        // 양보 목적지에 가까워졌을 때만 도착으로 인정해야 첫 프레임에 이동이 취소되지 않습니다.
        float arrivalDistance = agentUsable ? Mathf.Max(0.1f, m_agent.radius * 0.5f) : 0.1f;
        bool arrived = agentUsable && !m_agent.pathPending &&
                       FlatDistance(transform.position, m_playerYieldDestination) <= arrivalDistance;
        if (!agentUsable || Time.time >= m_playerYieldEndTime || arrived)
        {
            EndPlayerYield();
            return false;
        }

        m_agent.avoidancePriority = 99;
        m_agent.isStopped = false;
        UpdateMoveAnimation(m_agent.velocity);

        return true;
    }

    /// <summary>플레이어 양보 상태를 끝내고, 다음 프레임이 아니라 같은 프레임에 일반 추종 계산을 재개합니다.</summary>
    private void EndPlayerYield()
    {
        if (!m_isYieldingToPlayer)
        {
            return;
        }

        m_isYieldingToPlayer = false;
        if (m_agent != null)
        {
            m_agent.stoppingDistance = m_preYieldStoppingDistance;
            m_agent.speed = m_preYieldSpeed;
            m_agent.avoidancePriority = m_preYieldAvoidancePriority;
        }
        StopAgent();
        m_nextUpdateTime = Time.time;
    }

    /// <summary>
    /// 목적지에 도착했다고 볼 거리를 구합니다.
    /// </summary>
    /// <returns>도착 판정에 쓸 수평 거리입니다.</returns>
    /// <remarks>
    /// <b>Agent의 <c>stoppingDistance</c>를 반드시 넘겨야 합니다(실측).</b> Agent는 목적지보다
    /// <c>stoppingDistance</c>만큼 앞에서 멈추므로, 그보다 작은 값으로 도착을 판정하면 영원히 도착하지
    /// 않습니다. 그러면 합류가 풀리지 않아 동료가 목적지 근처에서 계속 달리는 상태로 남습니다.
    /// 합류 완료 거리를 좁게 잡을수록 이 함정에 걸리기 쉬워 고정값 대신 Agent 설정에서 끌어옵니다.
    /// </remarks>
    private float ResolveArrivalDistance()
    {
        float agentStopDistance = m_agent != null ? m_agent.stoppingDistance : 0.0f;
        return Mathf.Max(DestinationArrivalEpsilon, agentStopDistance + ArrivalStopDistanceMargin);
    }

    /// <summary>높이를 무시한 두 지점 사이의 거리입니다.</summary>
    /// <param name="a">첫 번째 지점입니다.</param>
    /// <param name="b">두 번째 지점입니다.</param>
    /// <returns>수평 거리입니다.</returns>
    /// <remarks>
    /// 캐릭터 발밑과 NavMesh 표본 높이가 조금씩 다르므로, 도착 판정에 y를 넣으면 그 차이만큼
    /// 항상 멀다고 나옵니다.
    /// </remarks>
    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        Vector3 delta = a - b;
        delta.y = 0.0f;
        return delta.magnitude;
    }

    /// <summary>이동 목적지를 확정하고 스쿼드에 찜해 둡니다.</summary>
    /// <param name="destination">가려는 자리입니다.</param>
    /// <remarks>
    /// 찜을 함께 걸어야 다른 AI가 같은 자리를 고르지 않습니다. 목적지를 정하는 곳이 여러 갈래
    /// (동행·합류·전투 위치·구조 접근)라 한 곳으로 모아 두지 않으면 한 갈래에서 빠뜨리게 됩니다.
    /// </remarks>
    private void SetFollowDestination(Vector3 destination)
    {
        m_destination = destination;
        m_hasDestination = true;
        m_squadManager.ClaimDestination(m_memberController, destination);
    }

    /// <summary>이동 목적지를 비우고 찜도 놓습니다.</summary>
    /// <remarks>
    /// 제자리를 지키기로 했으면 찜을 놓아야 합니다. 그러지 않으면 내가 서 있지도 않은 자리를 계속
    /// 막고 있어 다른 AI가 그 근처를 못 씁니다. 내 현재 위치는 어차피 따로 검사됩니다.
    /// </remarks>
    private void ClearFollowDestination()
    {
        m_hasDestination = false;

        if (m_squadManager != null)
        {
            m_squadManager.ReleaseDestination(m_memberController);
        }
    }

    /// <summary>그 자리가 다른 캐릭터와 겹치지 않는지 확인합니다.</summary>
    /// <param name="position">확인할 위치입니다.</param>
    /// <returns>비어 있으면 true입니다.</returns>
    /// <remarks>
    /// 자기 자신과 플레이어 조작 캐릭터는 제외합니다. 플레이어를 넣으면 완료 반경 안 후보가 전부
    /// 막혀 버립니다. 목적지 선택에만 쓰며, 이동 중 실제 충돌은 Agent 회피가 처리합니다.
    /// </remarks>
    private bool IsPositionClear(Vector3 position)
    {
        var members = m_squadManager.SquadMembers;
        if (members == null)
        {
            return true;
        }

        float clearanceSqr = DestinationClearance * DestinationClearance;
        for (int i = 0; i < members.Count; i++)
        {
            SquadMemberController member = members[i];
            if (member == null || member == m_memberController || member.IsPlayerSquadMember)
            {
                continue;
            }

            if (!member.IsAlive || member.IsDown)
            {
                continue;
            }

            Vector3 delta = member.transform.position - position;
            delta.y = 0.0f;
            if (delta.sqrMagnitude < clearanceSqr)
            {
                return false;
            }
        }

        // 다른 AI가 이미 그 자리를 찜했는지도 봅니다. 현재 위치만 보면 둘 다 멀리서 오는 중일 때
        // 그 자리가 비어 있어 양쪽 다 통과하고, 도착해서야 겹칩니다(실측: 목적지 간격 0.31m).
        return !m_squadManager.IsDestinationClaimedByOther(m_memberController, position, DestinationClearance);
    }

    /// <summary>
    /// 두 지점 사이의 실제 이동 경로 길이를 잽니다.
    /// </summary>
    /// <param name="from">출발 지점입니다.</param>
    /// <param name="to">도착 지점입니다.</param>
    /// <returns>경로상 거리이며, 완전한 경로가 없으면 <c>-1</c>입니다.</returns>
    /// <remarks>
    /// <c>PathComplete</c>가 아닌 경로는 목적지에 닿지 못한다는 뜻이므로 거리로 인정하지 않습니다.
    /// 이것이 §6의 길막 처리이기도 합니다. 닿을 수 없는 자리는 후보에서 빠지고 다른 자리가 선택됩니다.
    /// 갱신 주기마다 후보 수만큼 계산하므로 주기를 너무 짧게 두지 마십시오.
    /// </remarks>
    private float CalculatePathDistance(Vector3 from, Vector3 to)
    {
        m_pathBuffer ??= new NavMeshPath();

        if (!NavMesh.CalculatePath(from, to, NavFilter, m_pathBuffer))
        {
            return -1.0f;
        }

        if (m_pathBuffer.status != NavMeshPathStatus.PathComplete)
        {
            return -1.0f;
        }

        Vector3[] corners = m_pathBuffer.corners;
        if (corners.Length < 2)
        {
            return 0.0f;
        }

        float total = 0.0f;
        for (int i = 1; i < corners.Length; i++)
        {
            total += Vector3.Distance(corners[i - 1], corners[i]);
        }

        return total;
    }

    /// <summary>
    /// 스쿼드 목록에서 이 멤버가 몇 번째 AI인지 셉니다.
    /// </summary>
    /// <returns>0부터 시작하는 순번입니다.</returns>
    /// <remarks>
    /// 회피 우선순위와 후보 탐색 시작 각도를 서로 어긋나게 하는 데만 씁니다. 이 순번으로 자리를
    /// 고정하지는 않습니다. 플레이어 조작 멤버를 건너뛰고 세므로 조작권이 바뀌어도 순번이 이어집니다.
    /// </remarks>
    /// <summary>지금 살아 있는 AI 조작 멤버가 몇 명인지 셉니다.</summary>
    /// <returns>AI 멤버 수입니다. 최소 1을 돌려줍니다.</returns>
    /// <remarks>
    /// 후보 고리를 몇 등분해 엇갈리게 할지 정하는 데 씁니다. 다운·사망한 멤버를 세면 남은 인원이
    /// 실제보다 촘촘하게 갈라져 자리가 낭비됩니다.
    /// </remarks>
    private int ResolveAiMemberCount()
    {
        var members = m_squadManager.SquadMembers;
        if (members == null)
        {
            return 1;
        }

        int count = 0;
        for (int i = 0; i < members.Count; i++)
        {
            SquadMemberController member = members[i];
            if (member == null || member.IsPlayerSquadMember || !member.IsAlive || member.IsDown)
            {
                continue;
            }

            count++;
        }

        return Mathf.Max(1, count);
    }

    private int ResolveMemberOrder()
    {
        var members = m_squadManager.SquadMembers;
        if (members == null)
        {
            return 0;
        }

        int order = 0;
        for (int i = 0; i < members.Count; i++)
        {
            SquadMemberController member = members[i];
            if (member == null || member.IsPlayerSquadMember)
            {
                continue;
            }

            if (member == m_memberController)
            {
                return order;
            }

            order++;
        }

        return 0;
    }

    /// <summary>
    /// 멤버 순번에 따라 서로 다른 회피 우선순위를 부여합니다.
    /// </summary>
    /// <param name="order">이 멤버의 AI 순번입니다.</param>
    /// <remarks>
    /// <b>실측으로 확인한 문제</b>: 동행 AI 두 명의 <c>avoidancePriority</c>가 기본값 50으로 같으면
    /// <c>HighQualityObstacleAvoidance</c>가 서로를 대칭으로 피하려다 교착에 빠집니다.
    /// 속도를 6으로 올려도 실제 속력이 0과 2.7 사이를 오가며 4초에 4m밖에 못 갔습니다.
    /// 우선순위를 갈라 놓으면 한쪽이 양보해 교착이 풀립니다. Unity는 값이 낮을수록 우선합니다.
    /// </remarks>
    private void ApplyAvoidancePriority(int order)
    {
        int priority = Mathf.Clamp(m_baseAvoidancePriority + order, 0, 99);
        if (m_agent.avoidancePriority != priority)
        {
            m_agent.avoidancePriority = priority;
        }
    }

    /// <summary>
    /// 확정된 달리기 요청에 맞춰 Agent 이동 속도를 고릅니다.
    /// </summary>
    /// <param name="leader">속도 기준을 읽어올 플레이어 조작 캐릭터입니다.</param>
    /// <param name="sprint">달려야 하면 true입니다.</param>
    /// <remarks>
    /// §6.1은 "플레이어의 달리기 상태를 그대로 복사하지 않는다", §6.2는 "합류 시작 조건을 충족하면
    /// 달리기를 사용한다"고 규정합니다. 그래서 리더가 달리는지는 보지 않습니다. 달릴지 말지는
    /// <see cref="SquadAIDecision"/>이 정하고(§13의 재장전 중 걷기 포함) 여기서는 속도만 고릅니다.
    /// 속도값 자체는 리더의 <see cref="ThirdPersonController"/>에서 읽어 플레이어와 어긋나지 않게 합니다.
    /// <para>
    /// 멈추는 경로에서도 이 함수를 부릅니다(실측). 그러지 않으면 합류를 끝낸 뒤에도 Agent에 달리기 속도가
    /// 남아, 다음에 다시 움직이기 시작하는 첫 프레임이 달리기 속도로 출발합니다.
    /// </para>
    /// </remarks>
    private void ApplyFollowSpeed(Transform leader, bool sprint)
    {
        m_isSprinting = sprint;

        ThirdPersonController leaderController = ResolveLeaderController(leader);
        if (leaderController == null)
        {
            // 리더에서 속도를 읽지 못하면 프리팹에 설정된 Agent 속도를 그대로 씁니다.
            return;
        }

        // 평상시 동행은 플레이어 걷기보다 조금 빠릅니다. 같은 속도로 두면 플레이어가 걷기만 해도
        // 격차가 계속 벌어져 합류(달리기)가 상시로 켜지고, 결과적으로 동료가 늘 뛰어다닙니다(실측).
        // 달리기 상태를 복사하는 것이 아니라 걷기 속도에 여유를 주는 것이라 §6.1에 어긋나지 않습니다.
        m_agent.speed = m_isSprinting
            ? leaderController.SprintSpeed
            : leaderController.MoveSpeed * Mathf.Max(1.0f, m_followSpeedMultiplier);
    }

    /// <summary>리더의 이동 컨트롤러를 캐시와 함께 가져옵니다.</summary>
    /// <param name="leader">현재 리더 Transform입니다.</param>
    /// <returns>리더의 <see cref="ThirdPersonController"/>이며 없으면 null입니다.</returns>
    private ThirdPersonController ResolveLeaderController(Transform leader)
    {
        if (m_cachedLeaderTransform == leader)
        {
            return m_cachedLeaderController;
        }

        m_cachedLeaderTransform = leader;
        m_cachedLeaderController = leader != null ? leader.GetComponent<ThirdPersonController>() : null;
        return m_cachedLeaderController;
    }

    /// <summary>
    /// NavMeshAgent 이동 속도를 기준으로 캐릭터가 이동 방향을 바라보도록 회전합니다.
    /// </summary>
    private void HandleRotation()
    {
        // isStopped는 NavMesh 위에 있는 Agent에서만 읽을 수 있습니다. NavMesh를 벗어난 동안에는
        // 회전시킬 이동 속도도 없으므로 그대로 빠집니다. 순서를 바꾸면 읽는 순간 에러가 납니다.
        if (m_agent == null || !m_agent.isOnNavMesh || m_agent.isStopped)
        {
            return;
        }

        Vector3 velocity = m_agent.velocity;
        velocity.y = 0f;

        if (velocity.sqrMagnitude < 0.001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(velocity.normalized);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            Time.deltaTime * m_rotationSpeed);
    }

    /// <summary>
    /// 현재 이동 속도를 애니메이터의 이동 파라미터에 반영합니다.
    /// </summary>
    private void HandleAnimation()
    {
        if (m_agent == null)
        {
            UpdateMoveAnimation(Vector3.zero);
            return;
        }

        Vector3 velocity = m_agent.velocity;
        velocity.y = 0f;

        UpdateMoveAnimation(velocity);
    }

    /// <summary>
    /// NavMeshAgent 이동을 정지하고 현재 경로를 초기화합니다.
    /// </summary>
    private void StopAgent()
    {
        // NavMesh 위에 없으면 멈출 경로도 없습니다. 그 상태에서 isStopped와 ResetPath를 부르면
        // Unity가 매 프레임 에러를 냅니다("can only be called on an active agent that has been
        // placed on a NavMesh"). 개체가 NavMesh를 벗어나는 경로는 실재합니다 - 낙하, 텔레포트,
        // 런타임 NavMesh 재생성. 그때 콘솔이 에러로 덮이면 정작 원인 로그가 묻힙니다.
        if (m_agent == null || !m_agent.enabled || !m_agent.isOnNavMesh)
        {
            return;
        }

        m_agent.isStopped = true;
        m_agent.ResetPath();
    }

    /// <summary>
    /// AI 멤버의 이동 애니메이터 파라미터를 갱신합니다.
    /// </summary>
    /// <param name="velocity">현재 수평 이동 속도 벡터입니다.</param>
    /// <remarks>
    /// 조작 중인 멤버는 <see cref="ThirdPersonController"/>가 이 묶음을 채우지만, AI 멤버는 그 컴포넌트가
    /// 꺼져 있습니다(<see cref="SquadMemberController"/>가 직접 조작 멤버만 켭니다). 그래서 여기서 같은
    /// 파라미터를 채워야 합니다. 채우지 않으면 이렇게 어긋납니다.
    ///
    /// - <c>MoveState</c>가 0에 머물러 웅크림 트리가 재생됩니다(0은 웅크림 단계입니다).
    /// - <c>MoveX</c>/<c>MoveZ</c>가 0에 머물러 방향 블렌드의 중심, 즉 정지 클립만 나옵니다.
    /// - <c>IsGrounded</c>가 false로 남아, 조작하던 중 점프한 멤버를 전환으로 넘기면 <c>JumpStart</c>에서
    ///   나오지 못합니다. 나가는 조건이 <c>IsGrounded &amp;&amp; !IsJump</c>이기 때문입니다.
    ///
    /// AI 멤버는 NavMeshAgent로 지면을 따라 움직이므로 접지는 항상 true, 점프와 낙하는 항상 false입니다.
    /// AI가 점프하게 되면 그때 이 값들을 실제 상태로 바꿔야 합니다.
    /// </remarks>
    private void UpdateMoveAnimation(Vector3 velocity)
    {
        if (m_animator == null)
        {
            return;
        }

        float speed = velocity.magnitude;
        bool moving = speed > MoveDirectionThreshold;

        m_animator.SetFloat(SpeedHash, speed);

        // 동행 속도 배수만큼 재생 속도를 올려 발이 미끄러지지 않게 합니다.
        // 걷기 클립은 플레이어의 걷기 속도에 맞춰져 있는데 AI는 그보다 배수만큼 빠르게 이동하므로,
        // 재생 속도를 그대로 두면 이동 거리와 보폭이 어긋납니다. 합류(달리기) 중에는 달리기 클립과
        // 달리기 속도가 짝이 맞으므로 배수를 적용하지 않습니다.
        float motionSpeed = !m_isSprinting && moving
            ? Mathf.Max(1.0f, m_followSpeedMultiplier)
            : 1.0f;
        m_animator.SetFloat(MotionSpeedHash, motionSpeed);

        // 방향은 몸 기준으로 바꿔 넣습니다. 월드 방향을 그대로 쓰면 몸이 어디를 보든 같은 값이 됩니다.
        Vector3 local = moving
            ? transform.InverseTransformDirection(velocity / speed)
            : Vector3.zero;

        m_animator.SetFloat(MoveXHash, local.x, MoveDirectionDamp, Time.deltaTime);
        m_animator.SetFloat(MoveZHash, local.z, MoveDirectionDamp, Time.deltaTime);

        m_animator.SetBool(IsMoveHash, moving);

        // 조깅과 달리기 사이를 즉시 바꾸면 발걸음이 튑니다. ThirdPersonController와 같은 방식으로 보간합니다.
        // 멈춰 있을 때는 조깅 자세로 돌려놓아야 다음 출발이 달리기 포즈에서 시작하지 않습니다.
        float targetMoveState = moving && m_isSprinting ? MoveStateRun : MoveStateJog;
        m_moveState = m_moveStateBlendDuration <= 0.0f
            ? targetMoveState
            : Mathf.MoveTowards(m_moveState, targetMoveState, Time.deltaTime / m_moveStateBlendDuration);

        m_animator.SetFloat(MoveStateHash, m_moveState);

        m_animator.SetBool(GroundedHash, true);
        m_animator.SetBool(JumpHash, false);
        m_animator.SetBool(FreeFallHash, false);
    }

    /// <summary>
    /// 합류를 시작하는 경로상 거리를 설정합니다.
    /// </summary>
    /// <param name="value">새 합류 시작 거리입니다.</param>
    public void SetJoinStartDistance(float value)
    {
        m_joinStartDistance = Mathf.Max(0.0f, value);
    }

    /// <summary>
    /// 합류를 끝내는 경로상 거리를 설정합니다.
    /// </summary>
    /// <param name="value">새 합류 완료 거리입니다.</param>
    public void SetJoinCompleteDistance(float value)
    {
        m_joinCompleteDistance = Mathf.Max(0.0f, value);
    }

    /// <summary>
    /// 목표 위치 갱신 주기를 설정합니다.
    /// </summary>
    /// <param name="value">새 갱신 주기입니다.</param>
    public void SetUpdateInterval(float value)
    {
        m_updateInterval = Mathf.Max(0.01f, value);
    }

    /// <summary>
    /// 이동 방향 회전 속도를 설정합니다.
    /// </summary>
    /// <param name="value">새 회전 속도입니다.</param>
    public void SetRotationSpeed(float value)
    {
        m_rotationSpeed = Mathf.Max(0.0f, value);
    }

    /// <summary>
    /// 선택했을 때 합류 시작·완료 거리를 리더 기준 원 두 개로 그립니다.
    /// </summary>
    /// <remarks>
    /// 두 값은 히스테리시스 쌍입니다. 바깥 원을 넘으면 합류를 시작하고 안쪽 원에 들어오면 끝납니다.
    /// 숫자 두 개만 보면 그 사이 간격이 충분한지 알 수 없고, 좁으면 경계에서 합류가 껐다 켜졌다 합니다.
    ///
    /// <b>리더 기준으로 그립니다.</b> 판정 자체가 "리더까지 얼마나 먼가"이므로 이 AI를 중심에 두면 방향이 반대가 됩니다.
    /// 리더는 런타임에 찾으므로 Edit Mode에서는 그려지지 않습니다. 정지 상태의 배치보다 따라다니는 중의
    /// 간격이 봐야 할 대상이라 실질적인 제약은 아닙니다.
    ///
    /// 원은 실제 판정과 한 가지 다릅니다. 판정은 <b>경로상 거리</b>를 쓰는데(§6.2) 원은 직선거리라,
    /// 벽 너머에서는 원 안이어도 합류가 끝나지 않습니다. 그 차이를 보는 것이 이 표시의 쓸모이기도 합니다.
    /// </remarks>
    private void OnDrawGizmosSelected()
    {
        if (!m_debugDrawJoinDistances || m_squadManager == null || m_squadManager.PlayerSquadMember == null)
        {
            return;
        }

        Vector3 leaderPosition = m_squadManager.PlayerSquadMember.transform.position;

        // 바깥 원: 이 밖으로 나가면 합류를 시작합니다.
        Gizmos.color = new Color(1.0f, 0.75f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(leaderPosition, m_joinStartDistance);

        // 안쪽 원: 여기 들어오면 합류가 끝납니다.
        Gizmos.color = new Color(0.3f, 1.0f, 0.45f, 0.8f);
        Gizmos.DrawWireSphere(leaderPosition, m_joinCompleteDistance);

        // 지금 이 AI가 리더와 얼마나 떨어져 있는지. 직선이지만 어느 원 사이인지 읽는 데 씁니다.
        Gizmos.color = m_isJoining
            ? new Color(1.0f, 0.5f, 0.2f, 0.9f)
            : new Color(0.5f, 0.5f, 0.5f, 0.5f);
        Gizmos.DrawLine(transform.position, leaderPosition);
    }
}
