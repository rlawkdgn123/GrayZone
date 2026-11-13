using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;
using VInspector;

/// <summary>
/// 방어전에서 적이 어떤 대상군을 우선시할지 나타냅니다.
/// </summary>
/// <remarks>
/// 방어전 스폰 경로를 모두 통과한 뒤 <see cref="PlayerFirst"/>는 현재 조작 플레이어에게 먼저 향하고,
/// <see cref="TargetFirst"/>는 스폰 포인트가 제공한 방어 목표 위치로 향합니다.
/// </remarks>
public enum EnemyDefenseDisposition
{
    /// <summary>기존 적 탐색 규칙을 그대로 사용합니다.</summary>
    Default,

    /// <summary>웨이포인트 통과 후 현재 조작 플레이어에게 먼저 향하는 성향입니다.</summary>
    PlayerFirst,

    /// <summary>웨이포인트 통과 후 스폰 포인트의 목표 위치로 향하는 성향입니다.</summary>
    TargetFirst,

    /// <summary>
    /// 혼합형입니다. 방어 대상 우선처럼 움직이다가, 스쿼드원에게 피격되면 그 캐릭터를 공격 대상으로 전환합니다.
    /// </summary>
    /// <remarks>
    /// 전환된 어그로는 유지 시간 없이 교전이 이어지는 동안 유지하고, 교전이 끝나면 남은 웨이포인트를 건너뛰고
    /// 곧장 방어 대상으로 향합니다. 지정사수와 함정의 피격은 전환하지 않습니다(스쿼드원이 아니므로).
    /// 적이 <see cref="DefenseTargetZone"/> 안에 있고 쏜 캐릭터가 그 구역 밖이면 전환하지 않습니다.
    /// 값은 정수로 저장되므로 이 항목은 반드시 맨 뒤에 둡니다.
    /// </remarks>
    TargetUntilAttacked,
}

/// <summary>
/// Enemy 행동을 엄브렐라 HFSM으로 오케스트레이션하는 호스트입니다.
/// </summary>
/// <remarks>
/// 상태 전이는 <see cref="TransitionTo"/>로 처리하고, 이산 자극(피격/사망)은 이벤트로 전이를 요청합니다(하이브리드 트리거).
/// 감지·공격·체력은 각각 <see cref="EnemyTargetSensor"/>, <see cref="EnemyAttack"/>, <see cref="EnemyHealth"/>에 위임합니다.
/// 밸런스 수치는 <see cref="EnemyBalanceSO"/>가 있으면 <see cref="ApplyBalance"/>로 각 모듈에 주입하고, 없으면 직렬화 기본값을 유지합니다.
/// 슬라이스 1(뼈대): 상태 패턴 배선·전이·엄브렐라 구조만. 각 상태의 실제 행동(Tick 본문)·타이머·전이 조건·<see cref="MoveTo"/> 구현은 후속 슬라이스.
/// 설계 근거: privateDoc ENEMY_SYSTEM_DESIGN.md §5~6(행동), §1(밸런스).
/// </remarks>
[RequireComponent(typeof(EnemyHealth))]
[RequireComponent(typeof(EnemyTargetSensor))]
[RequireComponent(typeof(EnemyAttack))]
public class EnemyController : MonoBehaviour, IKnockbackReceiver
{
    [Header("Identity")]
    [Tooltip("이 감염체의 종류입니다. 시체 처리처럼 종류별로 다른 설정을 고를 때의 키로 씁니다. 밸런스 수치와는 무관합니다.")]
    [SerializeField] private EnemyType m_enemyType = EnemyType.Howler;

    [Tooltip("이 적 한 마리의 위험도 포인트입니다. 스폰 그룹의 총 위험도를 계산할 때 씁니다. 기준값: 스크래처 1, 스토커 3, 블로터 5, 하울러 7, 크러셔 10.")]
    [Min(0)]
    [SerializeField] private int m_dangerLevel = 1;

    [Foldout("Defense")]
    [Tooltip("이 프리팹이 기본적으로 Defense 전용 적인지 여부입니다. EnemyDefenseSpawnPoint에서 생성되면 런타임에도 true로 설정됩니다.")]
    [SerializeField] private bool m_isDefenseEnemy;

    [Tooltip("방어전 경로 이후 행동입니다. Player First는 현재 조작 플레이어, Target First는 스폰 포인트의 목표 위치로 향합니다. Spawn SO로 생성되면 SO 값이 우선합니다.")]
    [SerializeField] private EnemyDefenseDisposition m_defenseDisposition = EnemyDefenseDisposition.Default;

    [EndFoldout]
    [Header("Balance Data")]
    [Tooltip("선택 사항인 적 밸런스 데이터입니다. 지정하면 아래 레거시 기본값보다 우선 적용됩니다.")]
    [FormerlySerializedAs("m_balance")]
    [SerializeField] private EnemyBalanceSO m_balanceSO;

    [Header("Feedback Data")]
    [Tooltip("이 감염체 타입이 사용할 피격 표현, 혈흔, 행동 사운드 피드백 데이터입니다.")]
    [FormerlySerializedAs("m_feedbackProfile")]
    [SerializeField] private EnemyFeedbackSO m_feedback;

    [Header("Move")]
    [Tooltip("스폰 지점을 기준으로 배회 목적지를 고를 반경(m)입니다.")]
    [SerializeField] private float wanderRadius = 8f;

    [Tooltip("배회 목적지를 다시 고르는 기본 주기(초)입니다.")]
    [SerializeField] private float wanderInterval = 3f;

    [Tooltip("배회 상태에서 사용할 NavMeshAgent 이동 속도(m/s)입니다.")]
    [SerializeField] private float wanderSpeed = 1.2f;

    [Tooltip("추적 상태에서 사용할 NavMeshAgent 이동 속도(m/s)입니다.")]
    [SerializeField] private float chaseSpeed = 3.2f;

    [Tooltip("활성화하면 이동 상태에서 걷기 속도와 걷기 모션을 사용하지 않고 항상 달리기 속도와 달리기 모션을 사용합니다. 정지 상태의 Idle은 유지합니다. Spawn SO로 생성되면 SO 값이 우선합니다.")]
    [SerializeField] private bool m_alwaysRun;

    [Tooltip("대상을 향해 몸을 돌리는 최대 각속도(도/초)입니다. 200이면 180도 도는 데 약 0.9초가 걸립니다. 값이 클수록 고개가 튕기듯 돌아갑니다.")]
    [SerializeField] private float rotationSpeed = 200f;

    [Header("Idle Variation")]
    [Tooltip("대기 동작 혼합 비율의 최솟값입니다. 개체마다 이 범위에서 한 번 뽑아 고정합니다.")]
    [SerializeField] private float idleTypeMin = 0f;

    [Tooltip("대기 동작 혼합 비율의 최댓값입니다. 최솟값과 같으면 모든 개체가 같은 대기 동작을 씁니다.")]
    [SerializeField] private float idleTypeMax = 1f;

    /// <summary>이 개체가 쓰는 대기 동작 혼합 비율입니다. 초기화 때 한 번 뽑아 바뀌지 않습니다.</summary>
    public float IdleType { get; private set; }

    [Header("Detect")]
    [Tooltip("대상을 처음 발견했을 때 경계 상태에 머무는 시간(초)입니다.")]
    [SerializeField] private float alertDuration = 0.5f;

    [Tooltip("시야에서 벗어난 뒤에도 대상의 실시간 위치를 계속 아는 시간(초)입니다. 끝나면 마지막 확인 위치만 남습니다.")]
    [SerializeField] private float loseSightDelay = 2f;

    [Header("Noise")]
    [Tooltip("소음 위치로 이동할 때의 속도(m/s)입니다.")]
    [SerializeField] private float noiseChaseSpeed = 2f;

    [Tooltip("소음 위치에 도착했다고 볼 거리(m)입니다.")]
    [SerializeField] private float noiseArriveDistance = 1.5f;

    [Tooltip("소음 위치 도착 후 주변을 수색하는 전체 시간(초)입니다.")]
    [SerializeField] private float noiseSearchDuration = 8f;

    [Tooltip("소음 수색 중 배회할 반경(m)입니다.")]
    [SerializeField] private float noiseSearchRadius = 5f;

    [Header("Combat Search")]
    [Tooltip("마지막 확인 위치에 도착한 뒤 그 주변을 훑는 시간(초)입니다. 이 시간 안에 대상을 다시 못 찾으면 교전이 끝나고 배회로 돌아갑니다.")]
    [SerializeField] private float combatSearchDuration = 6f;

    [Tooltip("교전 수색 중 배회할 반경(m)입니다. 마지막 확인 위치가 기준입니다.")]
    [SerializeField] private float combatSearchRadius = 6f;

    [Header("Target")]
    [Tooltip("현재 대상을 다시 고를지 판단하는 주기(초)입니다. 이 주기가 곧 대상의 최소 유지 시간이 됩니다.")]
    [SerializeField] private float targetReevaluateInterval = 1f;

    [Tooltip("새 후보가 현재 대상보다 이만큼(m) 더 가까워야 대상을 바꿉니다. 경계에서 대상이 떨리는 것을 막습니다.")]
    [SerializeField] private float targetSwitchPathDistanceDelta = 2f;

    [Header("Physics")]
    [Tooltip("피격 및 폭발 충격량을 속도로 바꿀 때 쓰는 유효 질량(kg)입니다. 속도 = 충격량 / 이 값이므로, 크게 하면 같은 충격량에 덜 밀립니다.")]
    [FormerlySerializedAs("m_knockbackMass")]
    [Min(0.1f)]
    [SerializeField] private float m_mass = 60.0f;

    [Header("Knockback")]

    [Tooltip("넉백 속도가 초당 줄어드는 비율입니다. 4면 약 0.25초 만에 거의 멈춥니다. 클수록 짧고 탁 밀립니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_knockbackDamping = 8.0f;

    [Tooltip("넉백으로 낼 수 있는 최대 속도(m/s)입니다. 강한 무기로도 순간이동처럼 날아가지 않게 막습니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_maxKnockbackSpeed = 6.0f;

    [Header("Hit")]
    [Tooltip("피격 상태에서 이동과 상태 전환을 잠그는 시간(초)입니다.")]
    [SerializeField] private float hitStunDuration = 0.35f;

    [Tooltip("연속 피격 시 피격 상태를 다시 시작할 수 있는 최소 간격(초)입니다.")]
    [SerializeField] private float hitStunCooldown = 0.2f;

    [Header("Attack Timing")]
    [Tooltip("공격 시작 후 이 시점(초)에 방향을 확정합니다. 이후에는 대상을 따라 회전하지 않습니다.")]
    [SerializeField] private float attackDirectionLockTime = 0.25f;

    [Tooltip("공격 시작 후 이 시점(초)에 공간 판정을 수행합니다. 방향 고정 시점보다 뒤여야 합니다.")]
    [SerializeField] private float attackImpactTime = 0.45f;

    [Tooltip("판정 후 다음 행동까지의 후딜레이(초)입니다. 이 값이 곧 공격 간격이며 별도 쿨다운은 두지 않습니다.")]
    [SerializeField] private float attackRecoveryDuration = 0.75f;

    [Foldout("Debug")]
    [Tooltip("이 개체를 선택했을 때 배회 반경과 소음 수색 반경을 Scene 뷰에 표시합니다. 배회는 스폰 지점, 수색은 현재 위치가 기준입니다.")]
    [SerializeField] private bool m_debugDrawWanderRadius = false;

    [Tooltip("경직이 끝날 때 잠금 시간과 루트 모션이 실제로 옮긴 거리를 콘솔에 남깁니다. 클립이 의도한 후퇴량과 어긋나는지 확인할 때 켭니다.")]
    [SerializeField] private bool m_debugLogStagger = false;

    // =========================
    // 컴포넌트 참조
    // =========================
    /// <summary>Enemy의 NavMesh 이동을 담당하는 컴포넌트입니다.</summary>
    private NavMeshAgent agent;

    /// <summary>남아 있는 넉백 속도입니다. 매 프레임 감쇠하며 0이 되면 넉백이 끝납니다.</summary>
    private Vector3 m_knockbackVelocity;

    /// <summary>Enemy 애니메이션 파라미터와 트리거를 제어하는 컴포넌트입니다.</summary>
    private Animator animator;

    /// <summary>피해와 사망 이벤트를 제공하는 체력 컴포넌트입니다.</summary>
    private EnemyHealth enemyHealth;

    /// <summary>현재 추적할 스쿼드 멤버를 찾고 시야를 판정하는 센서입니다.</summary>
    private EnemyTargetSensor targetSensor;

    /// <summary>공격 가능 여부, 쿨다운, 실제 피해 적용을 담당하는 공격 모듈입니다.</summary>
    private EnemyAttack enemyAttack;

    /// <summary>선택 사항인 물리 래그돌 전환 컴포넌트입니다.</summary>
    private RagdollController ragdollController;

    /// <summary>행동·피격·사망 피드백의 출력 컴포넌트입니다.</summary>
    private EnemyFeedbackEmitter feedbackEmitter;

    /// <summary>이동 애니메이션 주기에 맞춰 표면별 맨발 소리를 출력하는 모듈입니다.</summary>
    private EnemyFootstepEmitter m_footstepEmitter;

    /// <summary>전역 감염체 보컬 관리자에 이 개체의 상태를 제공하는 모듈입니다.</summary>
    private EnemyVocalEmitter m_vocalEmitter;

    // =========================
    // 최상위 상태 인스턴스 (상태 간 전이에 사용)
    // =========================
    /// <summary>비전투 배회 상태입니다.</summary>
    public WanderState Wander { get; private set; }

    /// <summary>비전투 소음 경계(두리번) 상태입니다.</summary>
    public AlertState Alert { get; private set; }

    /// <summary>비전투 소음 추적 상태입니다.</summary>
    public NoiseChaseState NoiseChase { get; private set; }

    /// <summary>비전투 소음 수색 상태입니다.</summary>
    public NoiseSearchState NoiseSearch { get; private set; }

    /// <summary>교전 엄브렐라 상태입니다.</summary>
    public CombatState Combat { get; private set; }

    /// <summary>처치 상태입니다.</summary>
    public DeadState Dead { get; private set; }
    private AttackState m_defenseAttack;
    private DefenseEventHealth m_defenseObjective;
    private bool m_hasDefenseApproachSettings;
    private float m_defenseApproachStoppingDistance;

    /// <summary>방어 목표에서 이 개체가 고른 공격 포인트입니다. 포인트가 없는 목표면 null입니다.</summary>
    private DefenseAttackPoint m_defenseAttackPoint;

    /// <summary>공격 포인트로 가다가 막혀 서 있던 시간(초)입니다.</summary>
    private float m_defenseAttackPointBlockedTime;

    /// <summary>이 시간(초) 넘게 막혀 있으면 다른 공격 포인트를 다시 고릅니다.</summary>
    private const float DefenseAttackPointRepickDelay = 1.5f;

    /// <summary>방어전 접근을 시작하기 전의 회피 우선순위입니다. 접근을 끝낼 때 되돌립니다.</summary>
    private int m_defenseApproachAvoidancePriority;

    /// <summary>대기 줄에서 다른 포인트의 빈 앞자리를 다시 확인하기까지 남은 시간(초)입니다.</summary>
    private float m_defenseWaitRecheckTime;

    /// <summary>대기 위치를 좌우로 흩뜨릴 개체별 값(-1~1)입니다. 포인트를 고를 때마다 새로 뽑습니다.</summary>
    private float m_defenseWaitLateralSeed;

    /// <summary>대기 줄에서 다음 허공 휘두르기까지 남은 시간(초)입니다.</summary>
    private float m_defenseWaitFlourishTimer;

    /// <summary>지금 하고 있는 허공 휘두르기가 끝나는 시각입니다. 0이면 하고 있지 않습니다.</summary>
    private float m_defenseWaitFlourishEndTime;

    /// <summary>허공 휘두르기 사이 간격의 최솟값(초)입니다.</summary>
    private const float DefenseWaitFlourishMinInterval = 2.5f;

    /// <summary>허공 휘두르기 사이 간격의 최댓값(초)입니다.</summary>
    private const float DefenseWaitFlourishMaxInterval = 6.0f;

    /// <summary>허공 휘두르기 한 번의 길이(초)입니다. 공격 클립 길이에 맞춥니다.</summary>
    private const float DefenseWaitFlourishDuration = 1.5f;

    /// <summary>성문을 때리는 동안의 회피 우선순위입니다. 낮을수록 다른 에이전트가 비켜 갑니다.</summary>
    private const int DefenseAttackAvoidancePriority = 20;

    /// <summary>앞자리로 다가가는 동안의 회피 우선순위입니다.</summary>
    private const int DefenseApproachAvoidancePriority = 40;

    /// <summary>대기 줄에 있는 동안의 회피 우선순위입니다. 가장 낮게 두어 들어가는 적에게 비켜섭니다.</summary>
    private const int DefenseWaitAvoidancePriority = 80;

    /// <summary>대기 위치에 도착했다고 볼 정지 거리(m)입니다.</summary>
    private const float DefenseWaitStoppingDistance = 0.3f;

    /// <summary>대기 위치에서 이 거리(m) 안이면 도착으로 보고 멈춥니다. 옆 대기 적에게 조금 밀려도 다시 맞추려 떨지 않게 합니다.</summary>
    private const float DefenseWaitArriveDistance = 0.8f;

    /// <summary>대기 위치까지 이만큼(m) 남았는데 막혀 멈췄으면 그 자리에서 기다립니다.</summary>
    private const float DefenseWaitSettleDistance = 1.5f;

    /// <summary>대기 줄에서 다른 포인트의 빈 앞자리를 확인하는 간격(초)입니다.</summary>
    private const float DefenseWaitRecheckInterval = 1.0f;

    /// <summary>방어전 접근 목적지가 이 거리(m) 넘게 바뀔 때만 경로를 다시 잡습니다.</summary>
    private const float DefenseDestinationRefreshDistance = 0.3f;

    /// <summary>플레이어처럼 움직이는 방어전 목적지를 다시 지정할 수 있는 최소 간격(초)입니다.</summary>
    private const float DefenseMovingDestinationRefreshInterval = 0.2f;

    /// <summary>방어전 경로에서 이 시간(초) 동안 전진하지 못하면 경로 복구를 시도합니다.</summary>
    private const float DefensePathStallTimeout = 2.5f;

    /// <summary>방어전 경로 계산 대기가 이 시간(초)을 넘으면 계산이 굳은 것으로 봅니다.</summary>
    private const float DefensePathPendingTimeout = 1.5f;

    /// <summary>복구에 실패한 목적지를 다시 시도하는 간격(초)입니다.</summary>
    private const float DefensePathRetryInterval = 0.75f;

    /// <summary>원래 목적지 주변에서 도달 가능한 NavMesh 지점을 찾는 반경(m)입니다.</summary>
    private const float DefensePathRecoverySampleRadius = 3.0f;

    /// <summary>같은 목적지에서 경로를 다시 만든 뒤에도 정체되면 목적지별 실패 처리를 수행합니다.</summary>
    private const int DefensePathRecoveryAttemptLimit = 2;

    /// <summary>전진한 것으로 인정해 정체 타이머를 되돌리는 거리(m)입니다.</summary>
    private const float DefensePathProgressDistance = 0.2f;

    /// <summary>Player First가 현재 스쿼드 추적 대상을 다시 비교하는 간격(초)입니다.</summary>
    private const float DefenseSquadTargetReevaluationInterval = 1.0f;

    /// <summary>도달 실패한 스쿼드원을 다시 후보로 올리기까지 기다리는 시간(초)입니다.</summary>
    private const float DefenseSquadTargetRetryCooldown = 2.0f;

    /// <summary>스쿼드 추적 대상을 바꾼 뒤 다른 대상으로 다시 바꿀 수 없는 시간(초)입니다.</summary>
    private const float DefenseSquadTargetSwitchCooldown = 2.0f;

    /// <summary>현재 추적 대상을 바꿀 만큼 새 대상의 경로가 더 짧아야 하는 최소 차이(m)입니다.</summary>
    private const float DefenseSquadTargetSwitchPathDistanceDelta = 2.0f;

    /// <summary>스포너의 목표 위치 또는 그 부모에 연결된 방어 목표 체력입니다. 좌표 전용 마커면 null입니다.</summary>
    public DefenseEventHealth DefenseObjective => m_defenseObjective;

    /// <summary>방어 목표에서 이 개체가 고른 공격 포인트입니다. 고르지 않았거나 포인트가 없는 목표면 null입니다.</summary>
    public DefenseAttackPoint DefenseAttackPoint => m_defenseAttackPoint;

    /// <summary>
    /// 지금 목적지까지 더 다가갈 수 없는 상태(도착)인지 여부입니다.
    /// </summary>
    /// <remarks>
    /// 공격 포인트가 목표 표면에 있어도 NavMesh는 목표 둘레가 비어 있어 그 점까지 걸어가지 못할 수 있습니다.
    /// 그때 공격 시작 거리만 보면 영원히 공격하지 못하므로, 도착했으면 일반 공격 거리로 시작하게 하는 데 씁니다.
    /// </remarks>
    public bool HasSettledDefenseApproach => agent != null && agent.enabled && agent.isOnNavMesh &&
        !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.2f;

    /// <summary>현재 방어 목표 공격 상태를 실행하고 있는지 여부입니다.</summary>
    public bool IsAttackingDefenseObjective => m_defenseAttack != null && m_current == m_defenseAttack;

    /// <summary>
    /// 공격 포인트를 골랐지만 앞자리가 아니라서 대기 줄에 서 있는지 여부입니다.
    /// </summary>
    /// <remarks>대기 중에는 제자리에 서 있으므로, 멈춘 적을 찾는 쪽(정리 구간 안전장치)이 이 상태를 멈춤으로 보지 않게 하는 데 씁니다.</remarks>
    public bool IsWaitingForDefenseAttackPoint => m_defenseAttackPoint != null && !m_defenseAttackPoint.IsHolder(this);
    // 각성 준비(AwakenState)는 슬라이스 2에서 추가.
    // 경직은 상태가 아니라 상태 위에 얹히는 잠금이라 여기 목록에 없습니다(§5.7). HandleStaggered 참고.

    /// <summary>현재 활성 상태입니다.</summary>
    private EnemyStateBase m_current;

    // Start가 한 번 실행된 뒤에만 풀 재사용 시 초기 상태 전이를 직접 수행합니다.
    private bool m_hasStarted;

    /// <summary>현재 Spawn SO에서 주입한 이동 속도, Always Run, 방어전 성향이 있는지 여부입니다.</summary>
    private bool m_hasSpawnConfiguration;

    /// <summary>이번 생성에서 선정되어 사망 또는 풀 반환까지 유지되는 Spawn SO 걷기 속도(m/s)입니다.</summary>
    private float m_spawnWalkSpeed;

    /// <summary>이번 생성에서 선정되어 사망 또는 풀 반환까지 유지되는 Spawn SO 달리기 속도(m/s)입니다.</summary>
    private float m_spawnRunSpeed;

    /// <summary>이번 생성에서 Spawn SO가 지정한 Always Run 값입니다. 프리팹 값보다 우선합니다.</summary>
    private bool m_spawnAlwaysRun;

    /// <summary>이번 생성에서 Spawn SO가 지정한 방어전 성향입니다. Defense 적일 때만 프리팹 값보다 우선합니다.</summary>
    private EnemyDefenseDisposition m_spawnDefenseDisposition;

    /// <summary>현재 개체가 EnemyDefenseSpawnPoint에서 생성됐는지 나타내는 런타임 표식입니다.</summary>
    private bool m_isDefenseSpawn;

    /// <summary>현재 방어전 생성 개체가 순서대로 통과할 웨이포인트입니다.</summary>
    private readonly List<Transform> m_defenseWaypoints = new List<Transform>();

    /// <summary>웨이포인트 통과 후 사용할 외부 방어선 또는 방어 목표 위치입니다.</summary>
    private Transform m_defenseTargetPosition;

    /// <summary>현재 향하고 있는 방어전 웨이포인트 인덱스입니다.</summary>
    private int m_defenseWaypointIndex;

    /// <summary>이번 생성에서 방어전 진입 경로와 목표 복귀 행동을 적용할지 여부입니다.</summary>
    private bool m_defenseNavigationActive;

    private enum DefenseDestinationKind
    {
        None,
        Waypoint,
        SquadMember,
        Objective,
        AttackPoint,
        WaitPoint,
    }

    /// <summary>현재 방어전 이동 명령이 향하는 목적지 종류입니다.</summary>
    private DefenseDestinationKind m_defenseDestinationKind;

    /// <summary>목적지 종류가 같아도 대상 교체를 구분하기 위한 원본 오브젝트입니다.</summary>
    private UnityEngine.Object m_defenseDestinationSource;

    /// <summary>방어전 판단이 마지막으로 요청한 원래 목적지입니다. NavMesh 복구 좌표와는 구분합니다.</summary>
    private Vector3 m_defenseRequestedDestination;

    /// <summary>현재 목적지의 정체 여부를 비교할 마지막 진행 위치입니다.</summary>
    private Vector3 m_defenseLastProgressPosition;

    private float m_defenseLastProgressTime;
    private float m_defensePathIssuedTime;
    private float m_defenseNextMovingDestinationRefreshTime;
    private float m_defenseNextPathRecoveryTime;
    private int m_defensePathRecoveryAttempts;
    private bool m_hasDefenseDestination;

    /// <summary>Player First가 현재 방어 이동으로 추적하는 플레이어 또는 AI 동료입니다.</summary>
    private SquadMemberController m_defenseSquadPursuitTarget;

    /// <summary>이번 생성에서 Player First의 최초 추적 대상을 한 번 정했는지 여부입니다.</summary>
    private bool m_hasDefenseSquadTargetSelection;

    /// <summary>스쿼드 경로 거리 후보를 다시 비교할 수 있는 시각입니다.</summary>
    private float m_defenseNextSquadTargetEvaluationTime;

    /// <summary>경로 실패 대상별 재선택 금지 종료 시각입니다.</summary>
    private readonly Dictionary<SquadMemberController, float> m_defenseSquadTargetRetryTimes =
        new Dictionary<SquadMemberController, float>();

    /// <summary>스쿼드 후보 경로 길이 계산에 재사용하는 버퍼입니다.</summary>
    private NavMeshPath m_defenseSquadTargetPath;

    /// <summary>Player First가 플레이어보다 Defense 웨이포인트를 우선할지 여부입니다.</summary>
    private bool m_prioritizeDefenseWaypointsForPlayerFirst = true;

    /// <summary>혼합형(<see cref="EnemyDefenseDisposition.TargetUntilAttacked"/>)이 피격으로 스쿼드원에게 전환한 상태인지 여부입니다.</summary>
    /// <remarks>교전이 끝나면 해제되고, 다음 피격에서 다시 전환할 수 있습니다.</remarks>
    private bool m_hybridAggroActive;

    /// <summary>
    /// 지금 스쿼드원을 공격 대상에서 빼야 하는지 여부입니다.
    /// </summary>
    /// <remarks>
    /// 방어 대상 우선은 항상, 혼합형은 피격으로 전환하기 전까지 스쿼드원을 무시합니다.
    /// 방어 대상을 치는 동작에 앞을 막아 선 캐릭터가 맞지 않게 하는 데도 씁니다(<see cref="EnemyAttack"/>).
    /// </remarks>
    public bool IsIgnoringSquad => IsDefenseEnemy
        && (DefenseDisposition == EnemyDefenseDisposition.TargetFirst
            || (DefenseDisposition == EnemyDefenseDisposition.TargetUntilAttacked && !m_hybridAggroActive));

    /// <summary>현재 활성 상태입니다.</summary>
    public EnemyStateBase Current => m_current;

    /// <summary>이 적에 붙은 특수 능력입니다. Awake에서 한 번 수집합니다.</summary>
    private EnemyAbility[] m_abilities = System.Array.Empty<EnemyAbility>();

    /// <summary>이 적에 붙은 특수 능력 목록입니다. 능력이 없는 적은 빈 목록입니다.</summary>
    /// <remarks>교전 상태가 분기점마다 이 순서대로 묻습니다(<see cref="EnemyAbility"/>).</remarks>
    public IReadOnlyList<EnemyAbility> Abilities => m_abilities;

    /// <summary>
    /// 종류별 시체 유지 시간이 끝났을 때 풀 소유자가 개체를 회수할 기회를 받는 콜백입니다.
    /// </summary>
    /// <remarks>
    /// 반환값이 true인 콜백이 하나라도 있으면 해당 소유자가 개체를 처리한 것으로 보고
    /// <see cref="EnemyFSM.DeadState"/>는 <see cref="Object.Destroy(Object)"/>를 호출하지 않습니다.
    /// 일반 배치 적처럼 등록한 소유자가 없으면 기존 파괴 경로를 그대로 사용합니다.
    /// </remarks>
    public event System.Func<bool> OnCorpseLifetimeElapsed;

    // =========================
    // 상태가 읽어 쓰는 접근자 (튜닝 수치는 컨트롤러가 소유, 상태는 읽기만)
    // =========================
    /// <summary>NavMesh 이동 컴포넌트입니다.</summary>
    public NavMeshAgent Agent => agent;

    /// <summary>애니메이터입니다.</summary>
    public Animator Animator => animator;

    /// <summary>체력 컴포넌트입니다.</summary>
    public EnemyHealth Health => enemyHealth;

    /// <summary>대상 감지 센서입니다.</summary>
    public EnemyTargetSensor Sensor => targetSensor;

    /// <summary>공격 모듈입니다.</summary>
    public EnemyAttack Attack => enemyAttack;

    /// <summary>
    /// 물리 골격이 준비되어 있으면 현재 자세에서 래그돌로 전환합니다.
    /// </summary>
    /// <returns>래그돌을 활성화했으면 true이고, 구성이 없으면 false입니다.</returns>
    public bool TryActivateRagdoll()
    {
        return ragdollController != null && ragdollController.TryActivateRagdoll();
    }

    /// <summary>
    /// 이 개체에 Joint로 연결된 물리 골격이 준비되어 있는지 여부입니다.
    /// </summary>
    /// <remarks>
    /// 래그돌 전환 실패가 "골격이 없어서"인지 "설정에서 껐거나 이미 켜져 있어서"인지 가르는 데 씁니다.
    /// 골격이 없는 것은 프리팹 구성 누락이라 경고할 일이고, 나머지는 정상 경로입니다.
    /// </remarks>
    public bool HasRagdollSkeleton => ragdollController != null && ragdollController.IsConfigured;

    /// <summary>
    /// 시체 유지 시간이 끝났을 때 등록된 풀 소유자에게 회수 처리를 요청합니다.
    /// </summary>
    /// <returns>어느 한 소유자라도 개체 회수를 처리했으면 true입니다.</returns>
    /// <remarks>
    /// 콜백을 단순 알림으로 두지 않고 처리 여부를 반환하게 해, 풀링하지 않는 적의 기존
    /// <see cref="Object.Destroy(Object)"/> 흐름과 풀링 적의 비활성 반환 흐름을 함께 유지합니다.
    /// </remarks>
    public bool TryHandleCorpseLifetimeElapsed()
    {
        System.Func<bool> handlers = OnCorpseLifetimeElapsed;
        if (handlers == null)
        {
            return false;
        }

        System.Delegate[] invocationList = handlers.GetInvocationList();
        for (int i = 0; i < invocationList.Length; i++)
        {
            if (invocationList[i] is System.Func<bool> handler && handler.Invoke())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 이 개체가 피격으로 밀려날 때 적용할 충격량 하한을 래그돌에 알립니다.
    /// </summary>
    /// <param name="minimumImpulse">충격량 하한(N·s)입니다.</param>
    /// <remarks>
    /// 하한은 종류별 시체 설정이 정하고, 실제 적용은 쏜 쪽이 <see cref="RagdollController.ApplyHitImpulse"/>로
    /// 합니다. 사망 시점에 한 번 넣어 두면 그 뒤에 도착하는 임펄스가 이 값을 하한으로 씁니다.
    /// </remarks>
    public void SetRagdollMinimumHitImpulse(float minimumImpulse)
    {
        ragdollController?.SetMinimumHitImpulse(minimumImpulse);
    }

    /// <summary>
    /// 피격 방향으로 이 개체를 밀어냅니다.
    /// </summary>
    /// <param name="direction">공격자에서 피격 지점으로 향하는 방향입니다.</param>
    /// <param name="hitPoint">피격 지점의 월드 좌표입니다.</param>
    /// <param name="impulse">충격량(N·s)입니다.</param>
    /// <param name="hitBone">맞은 부위의 리지드바디입니다. 알 수 없으면 null입니다.</param>
    /// <returns>넉백을 적용했으면 true입니다.</returns>
    /// <remarks>
    /// <b>넉백의 기본 대상은 살아 있는 개체입니다.</b> 사망 후 래그돌이 받는 충격은 같은 값을 쓰는 부가
    /// 효과이며, 이 함수가 상태를 보고 두 경로 중 하나를 고릅니다. 때리는 쪽은 어느 쪽인지 몰라도 됩니다.
    ///
    /// 살아 있는 경우 충격량을 유효 질량으로 나눠 속도로 바꿉니다. 뼈 하나가 아니라 몸 전체가 밀리므로
    /// 래그돌 뼈 질량이 아니라 <see cref="m_mass"/>를 씁니다.
    /// 수평 성분만 씁니다. 살아 있는 개체는 NavMesh 위에 붙어 있어 위아래 성분이 의미가 없고,
    /// 아래로 밀면 지면에 박히려는 변위가 되어 이동이 끊깁니다.
    /// </remarks>
    public bool ApplyKnockback(Vector3 direction, Vector3 hitPoint, float impulse, Rigidbody hitBone)
    {
        // 사망 후에는 래그돌이 물리로 받습니다. 부가 효과 경로입니다.
        // 같은 유효 질량을 넘겨 무기 값 하나가 두 경로에서 같은 속도를 내게 합니다.
        if (m_current == Dead)
        {
            return ragdollController != null
                && ragdollController.ApplyHitImpulse(
                    direction, hitPoint, impulse, hitBone, GetMass());
        }

        if (impulse <= 0.0f)
        {
            return false;
        }

        Vector3 horizontal = new Vector3(direction.x, 0.0f, direction.z);
        if (horizontal.sqrMagnitude <= Mathf.Epsilon)
        {
            return false;
        }

        float speed = Mathf.Min(impulse / GetMass(), m_maxKnockbackSpeed);
        if (speed <= 0.0f)
        {
            return false;
        }

        // 연속 피격은 더해집니다. 다만 상한을 넘지 않게 잘라 연사에 끝없이 가속하지 않도록 합니다.
        m_knockbackVelocity += horizontal.normalized * speed;
        if (m_knockbackVelocity.magnitude > m_maxKnockbackSpeed)
        {
            m_knockbackVelocity = m_knockbackVelocity.normalized * m_maxKnockbackSpeed;
        }

        return true;
    }

    /// <summary>사망 후 활성화된 래그돌에 지정한 속도 변화를 직접 적용합니다.</summary>
    /// <returns>래그돌 뼈에 속도 변화를 적용했으면 true입니다.</returns>
    public bool ApplyRagdollVelocityChange(
        Vector3 direction,
        Vector3 hitPoint,
        float speed,
        Rigidbody hitBone)
    {
        return m_current == Dead
            && ragdollController != null
            && ragdollController.ApplyHitVelocityChange(direction, hitPoint, speed, hitBone);
    }

    /// <summary>피격 및 폭발 충격량 계산에 사용할 이 적의 유효 질량(kg)을 반환합니다.</summary>
    public float GetMass()
    {
        return Mathf.Max(0.1f, m_mass);
    }

    /// <summary>
    /// 래그돌 골격이 있으면 래그돌용 콜라이더는 남기고 게임플레이 콜라이더만 걷어냅니다.
    /// </summary>
    /// <returns>래그돌 컴포넌트가 처리했으면 true이고, 없으면 false입니다.</returns>
    /// <remarks>
    /// 사망 직후에는 아직 래그돌로 넘기지 않아도 피격·이동 충돌은 즉시 사라져야 합니다(§5.10.4).
    /// 어느 콜라이더가 래그돌 소속인지는 <see cref="RagdollController"/>만 알고 있으므로 그쪽에 맡깁니다.
    /// false가 돌아오면 호출자가 콜라이더를 통째로 끄면 됩니다.
    /// </remarks>
    public bool TryDisableGameplayColliders()
    {
        if (ragdollController == null)
        {
            return false;
        }

        ragdollController.DisableGameplayColliders();
        return true;
    }

    /// <summary>배회 상태 진입 피드백을 출력합니다.</summary>
    public void PlayIdleFeedback() => EnsureFeedbackEmitter()?.PlayIdle(m_feedback);

    /// <summary>교전 진입 경계 피드백을 출력합니다.</summary>
    public void PlayAlertFeedback() => EnsureFeedbackEmitter()?.PlayAlert(m_feedback);

    /// <summary>추적 상태 진입 피드백을 출력합니다.</summary>
    public void PlayChaseFeedback() => EnsureFeedbackEmitter()?.PlayChase(m_feedback);

    /// <summary>공격 시작 피드백을 출력합니다.</summary>
    public void PlayAttackFeedback() => EnsureFeedbackEmitter()?.PlayAttack(m_feedback);

    /// <summary>실제 피격 위치에 데이터 기반 피격 이펙트·사운드·혈흔을 출력합니다.</summary>
    public void PlayHitFeedback(Vector3 point, Vector3 normal, Transform hitTransform) =>
        EnsureFeedbackEmitter()?.PlayHit(m_feedback, point, normal, hitTransform);

    /// <summary>사망 위치에 데이터 기반 사망 사운드를 출력합니다.</summary>
    public void PlayDeathFeedback() => EnsureFeedbackEmitter()?.PlayDeath(m_feedback);

    /// <summary>배회 목적지 반경입니다.</summary>
    public float WanderRadius => wanderRadius;

    /// <summary>배회 목적지 갱신 주기입니다.</summary>
    public float WanderInterval => wanderInterval;

    /// <summary>배회 이동 속도입니다.</summary>
    public float WanderSpeed => ResolveMoveSpeed(wanderSpeed, false);

    /// <summary>추적 이동 속도입니다.</summary>
    public float ChaseSpeed => ResolveMoveSpeed(chaseSpeed, true);

    /// <summary>이동 중 걷기 단계 없이 항상 달리기 속도와 모션을 사용할지 여부입니다.</summary>
    /// <remarks>Spawn SO로 생성된 개체는 SO 값을, 그 밖에는 프리팹 Inspector 값을 사용합니다.</remarks>
    public bool AlwaysRun => m_hasSpawnConfiguration ? m_spawnAlwaysRun : m_alwaysRun;

    /// <summary>대상 방향 회전 보간 속도입니다.</summary>
    public float RotationSpeed => rotationSpeed;

    /// <summary>소음 위치로 이동할 때의 속도입니다.</summary>
    public float NoiseChaseSpeed => ResolveMoveSpeed(noiseChaseSpeed, false);

    /// <summary>이번 생성에 최종 적용된 개체별 이동 속도입니다. Spawn SO 값이 없으면 Balance/Inspector의 추적 속도를 반환합니다.</summary>
    public float CurrentMoveSpeed => ResolveMoveSpeed(chaseSpeed, true);

    /// <summary>
    /// 지금 걸려 있는 이동 속도 배율들입니다. 키는 효과를 건 쪽(함정 등)이고 값은 배율입니다.
    /// </summary>
    /// <remarks>
    /// 값을 저장했다 되돌리는 방식을 쓰지 않는 이유는, FSM이 상태가 바뀔 때마다
    /// <c>agent.speed = CurrentMoveSpeed</c>로 덮어쓰기 때문입니다. 저장해 둔 값은 그 순간 사라집니다.
    /// 그래서 속도를 구하는 단일 통로인 <see cref="ResolveMoveSpeed"/>에 배율을 걸어, 누가 언제 덮어쓰든
    /// 감속이 유지되게 합니다.
    ///
    /// 리스트가 아니라 키를 쓰는 이유는 겹침 때문입니다. 함정 두 개에 동시에 걸렸다가 하나만 빠져나올 때,
    /// 어느 것이 빠졌는지 알아야 나머지 하나의 감속을 남길 수 있습니다. 같은 대상이 중복으로 걸어도
    /// 한 번만 쌓이므로 해제 누락으로 영구 감속이 남는 사고도 줄어듭니다.
    /// </remarks>
    private readonly Dictionary<Object, float> m_moveSpeedMultipliers = new Dictionary<Object, float>();

    /// <summary>모든 배율을 곱한 최종 이동 속도 배율입니다. 걸린 효과가 없으면 1입니다.</summary>
    private float m_moveSpeedMultiplier = 1.0f;

    /// <summary>프리팹이 정한 NavMeshAgent 가속도입니다. 배율을 되돌릴 기준값입니다.</summary>
    private float m_baseAcceleration;

    /// <summary>프리팹이 정한 NavMeshAgent 회전 속도입니다. 배율을 되돌릴 기준값입니다.</summary>
    private float m_baseAngularSpeed;

    /// <summary>위 두 기준값을 이미 잡았는지 여부입니다.</summary>
    private bool m_hasBaseAgentMotion;

    /// <summary>
    /// 지금 이 개체를 걷기로 묶어 둔 쪽들입니다. 하나라도 있으면 달리지 못합니다.
    /// </summary>
    /// <remarks>
    /// 배율로 속도만 깎으면 애니메이션은 달리기 그대로라 발이 미끄러져 보입니다. 이동량과 발 회전이
    /// 함께 느려지려면 블렌드 트리가 걷기 구간으로 내려가야 하고, 그러려면 속도 값이 아니라
    /// "달리기 금지"를 눌러야 합니다.
    ///
    /// 배율과 같은 키 방식인 이유도 같습니다. 함정 두 개에 겹쳐 걸렸다가 하나만 빠져나올 때
    /// 나머지 하나의 구속이 남아야 합니다.
    /// </remarks>
    private readonly HashSet<Object> m_forceWalkSources = new HashSet<Object>();

    /// <summary>지금 걷기로 묶여 있는지 여부입니다.</summary>
    public bool IsForcedToWalk => m_forceWalkSources.Count > 0;

    /// <summary>이 개체를 걷기로 묶습니다. 같은 <paramref name="source"/>로 여러 번 불러도 한 번만 쌓입니다.</summary>
    /// <param name="source">구속을 건 쪽입니다. 해제할 때 같은 값을 넘겨야 합니다.</param>
    public void AddForceWalk(Object source)
    {
        if (source == null || !m_forceWalkSources.Add(source))
        {
            return;
        }

        RefreshMoveSpeedMultiplier();
    }

    /// <summary>걸어 둔 걷기 구속을 해제합니다.</summary>
    /// <param name="source">구속을 걸 때 넘겼던 값입니다.</param>
    public void RemoveForceWalk(Object source)
    {
        if (source == null || !m_forceWalkSources.Remove(source))
        {
            return;
        }

        RefreshMoveSpeedMultiplier();
    }

    /// <summary>지금 적용 중인 이동 속도 배율입니다.</summary>
    public float MoveSpeedMultiplier => m_moveSpeedMultiplier;

    /// <summary>
    /// 이동 속도 배율을 겁니다. 같은 <paramref name="source"/>로 다시 부르면 값만 바뀝니다.
    /// </summary>
    /// <param name="source">효과를 건 쪽입니다. 해제할 때 같은 값을 넘겨야 합니다.</param>
    /// <param name="multiplier">곱할 배율입니다. 0.5면 절반 속도이며 0 미만은 0으로 막습니다.</param>
    public void AddMoveSpeedMultiplier(Object source, float multiplier)
    {
        if (source == null)
        {
            return;
        }

        m_moveSpeedMultipliers[source] = Mathf.Max(0.0f, multiplier);
        RefreshMoveSpeedMultiplier();
    }

    /// <summary>이 적에게 걸린 버프 목록입니다. 처음 쓸 때 만듭니다.</summary>
    private EnemyBuffSet m_buffs;

    /// <summary>버프 목록입니다. 다른 적의 Awake 순서와 무관하게 쓸 수 있도록 처음 접근할 때 만듭니다.</summary>
    private EnemyBuffSet Buffs => m_buffs ??= new EnemyBuffSet(this);

    /// <summary>
    /// 버프를 겁니다. 같은 종류가 이미 걸려 있으면 새 버프 기준으로 교체하고 지속 시간을 다시 셉니다.
    /// </summary>
    /// <param name="buff">걸 버프 종류입니다.</param>
    /// <remarks>한 번 걸면 지속 시간이 끝날 때까지 유지합니다. 건 쪽이 먼저 죽어도 풀리지 않습니다.</remarks>
    public void ApplyBuff(EnemyBuffSO buff)
    {
        Buffs.Apply(buff);
    }

    /// <summary>소유자가 있는 버프를 적용합니다.</summary>
    /// <returns>새 적용 또는 죽은 소유자에서 새 소유자로 교체됐으면 true입니다.</returns>
    public bool ApplyBuff(EnemyBuffSO buff, EnemyController owner)
    {
        return Buffs.Apply(buff, owner);
    }

    /// <summary>지정한 하울러가 이 버프의 단일 소유권을 지금 확보할 수 있는지 확인합니다.</summary>
    public bool CanReceiveBuffFrom(EnemyBuffSO buff, EnemyController owner)
    {
        return Buffs.CanAcceptOwner(buff, owner);
    }

    /// <summary>현재 이 버프를 소유한 적을 반환합니다.</summary>
    public EnemyController GetBuffOwner(EnemyBuffSO buff)
    {
        return m_buffs != null ? m_buffs.GetOwner(buff) : null;
    }

    /// <summary>소유자 사망 뒤 감소할 버프 잔여시간을 반환합니다.</summary>
    public float GetBuffRemainingDuration(EnemyBuffSO buff)
    {
        return m_buffs != null ? m_buffs.GetRemainingDuration(buff) : 0.0f;
    }

    /// <summary>지정한 종류의 버프가 걸려 있는지 확인합니다.</summary>
    /// <param name="buff">확인할 버프 종류입니다.</param>
    public bool HasBuff(EnemyBuffSO buff)
    {
        return m_buffs != null && m_buffs.Has(buff);
    }

    /// <summary>걸어 둔 이동 속도 배율을 해제합니다. 걸린 적이 없으면 아무 일도 하지 않습니다.</summary>
    /// <param name="source">효과를 걸 때 넘겼던 값입니다.</param>
    public void RemoveMoveSpeedMultiplier(Object source)
    {
        if (source == null || !m_moveSpeedMultipliers.Remove(source))
        {
            return;
        }

        RefreshMoveSpeedMultiplier();
    }

    /// <summary>
    /// 배율 합산값을 다시 구하고 NavMeshAgent에 즉시 반영합니다.
    /// </summary>
    /// <remarks>
    /// 여기서 바로 <c>agent.speed</c>를 갱신하는 이유는, 다음 상태 전환까지 기다리면 감속이 늦게 걸리기
    /// 때문입니다. 함정을 밟은 순간 느려져야 하는데 상태가 그대로면 몇 초 뒤에야 반영됩니다.
    ///
    /// 최고 속도만 줄이면 감속이 잘 느껴지지 않습니다. 가속도가 그대로면 줄어든 속도에 즉시 도달해
    /// "느리지만 기민한" 움직임이 됩니다. 회전 속도도 그대로면 제자리에서 홱홱 돌아 발이 묶인 느낌이
    /// 나지 않습니다. 그래서 세 값을 같은 배율로 함께 줄입니다.
    ///
    /// 가속도와 회전 속도는 FSM이 따로 대입하지 않아 여기서 직접 넣고 되돌려야 합니다.
    /// 속도만 <see cref="ResolveMoveSpeed"/>를 거치는 것과 다른 점입니다.
    /// </remarks>
    private void RefreshMoveSpeedMultiplier()
    {
        float product = 1.0f;
        foreach (var pair in m_moveSpeedMultipliers)
        {
            product *= pair.Value;
        }

        m_moveSpeedMultiplier = product;

        if (agent == null || !agent.enabled)
        {
            return;
        }

        if (agent.isOnNavMesh)
        {
            agent.speed = CurrentMoveSpeed;

            // 이미 붙어 있던 속도는 최고 속도를 낮춰도 스스로 사라지지 않습니다.
            // 가속도를 따라 천천히 줄어들 뿐이라, 함정을 밟은 뒤에도 한동안 원래 속도로 미끄러집니다.
            // NavMeshAgent.velocity는 대입할 수 있으므로 새 상한을 넘는 만큼 즉시 잘라 냅니다.
            // 방향은 그대로 두고 크기만 줄여야 진행 방향이 튀지 않습니다.
            Vector3 current = agent.velocity;
            float limit = agent.speed;
            if (current.sqrMagnitude > limit * limit)
            {
                agent.velocity = current.normalized * limit;
            }
        }

        if (m_hasBaseAgentMotion)
        {
            agent.acceleration = m_baseAcceleration * m_moveSpeedMultiplier;
            agent.angularSpeed = m_baseAngularSpeed * m_moveSpeedMultiplier;
        }
    }

    /// <summary>소음 위치에 도착했다고 볼 거리입니다.</summary>
    public float NoiseArriveDistance => noiseArriveDistance;

    /// <summary>소음 수색 전체 시간입니다.</summary>
    public float NoiseSearchDuration => noiseSearchDuration;

    /// <summary>소음 수색 중 배회할 반경입니다.</summary>
    public float NoiseSearchRadius => noiseSearchRadius;

    /// <summary>교전 수색 시간입니다. 이 시간이 지나면 교전이 끝납니다(§5.8.4).</summary>
    public float CombatSearchDuration => combatSearchDuration;

    /// <summary>교전 수색 중 배회할 반경입니다.</summary>
    public float CombatSearchRadius => combatSearchRadius;

    /// <summary>각성 준비(경계) 시간입니다.</summary>
    public float AlertDuration => alertDuration;

    /// <summary>시야에서 벗어난 뒤 실시간 위치를 계속 아는 시간입니다.</summary>
    public float LoseSightDelay => loseSightDelay;

    /// <summary>현재 대상 재평가 주기입니다.</summary>
    public float TargetReevaluateInterval => targetReevaluateInterval;

    /// <summary>대상 교체에 필요한 경로 거리 차이입니다.</summary>
    public float TargetSwitchPathDistanceDelta => targetSwitchPathDistanceDelta;

    /// <summary>피격 경직 지속 시간입니다.</summary>
    public float HitStunDuration => hitStunDuration;

    /// <summary>연속 피격 경직 재진입 최소 간격입니다.</summary>
    public float HitStunCooldown => hitStunCooldown;

    /// <summary>공격 시작 기준 방향 고정 시점입니다.</summary>
    public float AttackDirectionLockTime => attackDirectionLockTime;

    /// <summary>공격 시작 기준 판정 시점입니다.</summary>
    public float AttackImpactTime => Mathf.Max(attackDirectionLockTime, attackImpactTime);

    /// <summary>판정 후 후딜레이이며 곧 공격 간격입니다.</summary>
    public float AttackRecoveryDuration => attackRecoveryDuration;

    /// <summary>이 감염체의 종류입니다. 종류별 설정을 고를 때의 키입니다.</summary>
    public EnemyType EnemyType => m_enemyType;

    /// <summary>이 적 한 마리의 위험도 포인트입니다.</summary>
    /// <remarks>
    /// 스폰 그룹의 총 위험도(위험도 × 마릿수의 합)를 계산하는 기준입니다. 체력이나 공격력 같은 밸런스 수치와는 따로
    /// 기획자가 정하는 값이며, 같은 종류의 프리팹이 여러 개면 각각 같은 값을 넣어야 합니다.
    /// </remarks>
    public int DangerLevel => m_dangerLevel;

    /// <summary>이 감염체가 방어전에서 우선시할 대상 성향입니다.</summary>
    /// <remarks>
    /// 방어전 생성 경로 통과 후 플레이어 탐색과 외부 방어선 이동의 우선순위를 결정합니다.
    /// Spawn SO로 생성된 Defense 적은 SO 값을, 그 밖에는 프리팹 Inspector 값을 사용합니다.
    /// Defense 여부를 읽는 시점에 확인하므로 <see cref="ConfigureSpawn"/>과
    /// <see cref="ConfigureDefenseSpawn"/>의 호출 순서와 관계없이 같은 값이 나옵니다.
    /// 정리 구간 안전장치로 <see cref="ForcePursuePlayer"/>가 불렸으면 무엇보다 우선해 플레이어 우선입니다.
    /// </remarks>
    public EnemyDefenseDisposition DefenseDisposition =>
        m_forcedPlayerPursuit && IsDefenseEnemy ? EnemyDefenseDisposition.PlayerFirst
        : m_hasSpawnConfiguration && IsDefenseEnemy ? m_spawnDefenseDisposition : m_defenseDisposition;

    /// <summary>정리 구간 안전장치로 플레이어 우선이 강제된 상태인지 여부입니다. 풀 반환 때 해제됩니다.</summary>
    private bool m_forcedPlayerPursuit;

    /// <summary>
    /// 방어전 정리 구간이 너무 길어질 때, 이 적이 지금 조작 중인 캐릭터에게 향하도록 강제합니다.
    /// </summary>
    /// <remarks>
    /// 성향을 플레이어 우선으로 바꾸고 남은 웨이포인트를 건너뜁니다. 그러면 방어전 이동이 매번 현재 조작 캐릭터 위치로
    /// 목적지를 다시 잡으므로(<see cref="SquadManager.PlayerSquadMember"/>), 어디에 있든 플레이어 쪽으로 옵니다.
    /// 방어 대상 우선과 혼합형의 "스쿼드원 무시"도 함께 풀립니다. 이미 교전 중이면 교전을 그대로 둡니다.
    /// Defense 적이 아니면 아무 일도 하지 않습니다. 풀로 돌아갈 때 해제됩니다.
    /// </remarks>
    public void ForcePursuePlayer()
    {
        if (!IsDefenseEnemy || m_current == Dead)
        {
            return;
        }

        m_forcedPlayerPursuit = true;
        m_prioritizeDefenseWaypointsForPlayerFirst = false;
        m_defenseWaypointIndex = m_defenseWaypoints.Count;
        m_defenseNavigationActive = true;
        ResetDefenseSquadTargetSelection();
        ResetDefenseDestinationTracking();
    }

    /// <summary>프리팹 설정 또는 현재 생성 경로에 의해 Defense 전용 적으로 활성화됐는지 여부입니다.</summary>
    public bool IsDefenseEnemy => m_isDefenseEnemy || m_isDefenseSpawn;

    /// <summary>
    /// 스폰 포인트가 Inspector/Balance 결과보다 우선할 이번 생성의 이동 속도, Always Run, 방어전 성향을 주입합니다.
    /// </summary>
    /// <param name="walkSpeed">생성 시 한 번 선정된 개체별 걷기 이동 속도(m/s)입니다.</param>
    /// <param name="runSpeed">생성 시 한 번 선정된 개체별 달리기 이동 속도(m/s)입니다.</param>
    /// <param name="alwaysRun">이동 중 항상 달리기 속도와 모션을 사용할지 여부입니다.</param>
    /// <param name="defenseDisposition">Defense 적일 때 사용할 방어전 성향입니다. Defense 적이 아니면 쓰이지 않습니다.</param>
    /// <remarks>프리팹 활성화 전에 호출해도 되며, 풀 반환 전까지 모든 이동 상태에서 유지됩니다.</remarks>
    public void ConfigureSpawn(float walkSpeed, float runSpeed, bool alwaysRun, EnemyDefenseDisposition defenseDisposition)
    {
        m_hasSpawnConfiguration = true;
        m_spawnWalkSpeed = Mathf.Max(0.0f, walkSpeed);
        m_spawnRunSpeed = Mathf.Max(0.0f, runSpeed);
        m_spawnAlwaysRun = alwaysRun;
        m_spawnDefenseDisposition = defenseDisposition;

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            // 재사용에서는 Wander.Enter가 이미 실행됐습니다. SO 데이터는 덮어쓰되
            // 현재 상태의 걷기/달리기 선택을 바꾸지 않아 첫 생성과 재사용을 일치시킵니다.
            if (m_defenseNavigationActive)
            {
                agent.speed = CurrentMoveSpeed;
            }
            else
            {
                m_current?.ResumeMovement();
            }
        }
    }

    /// <summary>풀 반환 시 이번 Spawn SO가 주입한 런타임 값만 제거합니다.</summary>
    /// <remarks>
    /// 이동 속도 배율도 함께 비웁니다. 함정 안에서 죽어 풀로 돌아가면 <c>OnTriggerExit</c>이 오지 않을 수
    /// 있는데, 그대로 두면 다음에 재사용될 때 감속이 걸린 채로 나옵니다.
    /// </remarks>
    public void ClearSpawnConfiguration()
    {
        m_hasSpawnConfiguration = false;
        m_spawnWalkSpeed = 0.0f;
        m_spawnRunSpeed = 0.0f;
        m_spawnAlwaysRun = false;
        m_spawnDefenseDisposition = EnemyDefenseDisposition.Default;

        // 걷기 구속도 함께 비웁니다. 철조망·스파이크 안에서 풀로 돌아가면 OnTriggerExit이 오지 않아, 다시 스폰된 개체가
        // 이전 생애의 걷기 구속을 안고 나옵니다.
        m_forceWalkSources.Clear();

        if (m_moveSpeedMultipliers.Count > 0)
        {
            m_moveSpeedMultipliers.Clear();
        }

        RefreshMoveSpeedMultiplier();

        // 버프 배율은 위에서 함께 비워졌으므로 기록만 지웁니다. 남기면 재사용된 개체가 이전 생애의 버프를
        // 가진 것으로 보이고, 같은 종류를 다시 받을 때도 기록이 어긋납니다.
        m_buffs?.ClearRecordsOnly();
    }

    /// <summary>
    /// Defense 스폰 포인트가 이번 생성의 Defense 표식, 웨이포인트와 목표 위치를 주입합니다.
    /// </summary>
    /// <param name="waypoints">먼저 순서대로 통과할 웨이포인트 목록입니다.</param>
    /// <param name="targetPosition">경로 통과 후 사용할 외부 방어선 또는 방어 목표 위치입니다.</param>
    /// <param name="prioritizeWaypointsForPlayerFirst">Player First가 플레이어보다 웨이포인트를 먼저 처리할지 여부입니다. Target First는 이 값을 무시합니다.</param>
    /// <remarks>
    /// 풀에서 활성화되기 전에 호출할 수 있습니다. 웨이포인트 참조는 개체별 목록으로 복사합니다.
    /// </remarks>
    public void ConfigureDefenseSpawn(
        IReadOnlyList<Transform> waypoints,
        Transform targetPosition,
        bool prioritizeWaypointsForPlayerFirst)
    {
        m_isDefenseSpawn = true;
        m_defenseTargetPosition = targetPosition;
        m_defenseObjective = targetPosition != null ? targetPosition.GetComponentInParent<DefenseEventHealth>() : null;
        m_prioritizeDefenseWaypointsForPlayerFirst = prioritizeWaypointsForPlayerFirst;
        m_defenseWaypoints.Clear();

        if (waypoints != null)
        {
            for (int i = 0; i < waypoints.Count; i++)
            {
                Transform waypoint = waypoints[i];
                if (waypoint != null)
                {
                    m_defenseWaypoints.Add(waypoint);
                }
            }
        }

        RestartDefenseNavigation();
    }

    /// <summary>풀 반환 시 이번 생성에만 적용한 Defense 표식과 경로 설정을 제거합니다.</summary>
    /// <remarks>프리팹의 Inspector 설정은 바꾸지 않고 Defense 스폰 포인트가 주입한 런타임 값만 해제합니다.</remarks>
    public void ClearDefenseSpawnConfiguration()
    {
        RestoreDefenseApproachSettings();
        ReleaseDefenseAttackPoint();
        m_isDefenseSpawn = false;
        m_defenseWaypoints.Clear();
        m_defenseTargetPosition = null;
        m_defenseObjective = null;
        m_defenseWaypointIndex = 0;
        m_defenseNavigationActive = false;
        m_prioritizeDefenseWaypointsForPlayerFirst = true;
        m_hybridAggroActive = false;
        m_forcedPlayerPursuit = false;
        ResetDefenseSquadTargetSelection();
        ResetDefenseDestinationTracking(true);
    }

    /// <summary>현재 적용 대상으로 지정된 적 밸런스 데이터입니다.</summary>
    public EnemyBalanceSO Balance => m_balanceSO;

    /// <summary>현재 이 감염체 타입에 지정된 피드백 데이터입니다.</summary>
    public EnemyFeedbackSO Feedback => m_feedback;

    /// <summary>현재 HP입니다. 체력 컴포넌트가 없으면 0을 반환합니다.</summary>
    public int CurrentHP => enemyHealth != null ? enemyHealth.CurrentHP : 0;

    /// <summary>풀 재사용 전후를 관찰자 캐시가 구분할 수 있는 생성 세대입니다.</summary>
    /// <remarks>인스턴스 참조가 같아도 ResetForSpawn마다 증가하며 저장 데이터에는 쓰지 않습니다.</remarks>
    public uint SpawnGeneration { get; private set; }

    /// <summary>현재 유효한 추적 대상 스쿼드 멤버입니다.</summary>
    public SquadMemberController CurrentTarget => targetSensor != null ? targetSensor.CurrentTarget : null;

    /// <summary>필수 컴포넌트를 캐싱하고 상태 인스턴스를 생성한 뒤 밸런스를 적용합니다.</summary>
    private void Awake()
    {
        CacheReferences();
        CollectAbilities();
        CreateStates();
        ApplyBalance(m_balanceSO);
    }

    /// <summary>같은 GameObject에 붙은 특수 능력을 모아 이 적에 연결합니다.</summary>
    /// <remarks>
    /// 밸런스 적용보다 먼저 해야 능력도 밸런스 값을 받습니다. 능력은 런타임에 붙이거나 떼지 않는다고 보고
    /// 한 번만 수집합니다.
    /// </remarks>
    private void CollectAbilities()
    {
        m_abilities = GetComponents<EnemyAbility>();
        for (int i = 0; i < m_abilities.Length; i++)
        {
            m_abilities[i].Initialize(this);
        }
    }

    /// <summary>
    /// 지정한 적 밸런스 데이터를 컨트롤러와 하위 감지·공격·체력 모듈에 적용합니다.
    /// </summary>
    /// <remarks>
    /// null이면 기존 컴포넌트 직렬화 기본값을 유지합니다. 최초 Awake에서는 체력 Start 초기화 전에
    /// 최대 HP만 교체하며, 런타임 재적용 시 현재 HP를 강제로 회복하지 않습니다.
    /// </remarks>
    /// <param name="balance">적용할 순수 수치 밸런스 데이터입니다.</param>
    public void ApplyBalance(EnemyBalanceSO balance)
    {
        if (balance == null)
        {
            return;
        }

        m_balanceSO = balance;

        m_mass = balance.Mass;
        wanderRadius = balance.WanderRadius;
        wanderInterval = balance.WanderInterval;
        wanderSpeed = balance.WanderSpeed;
        chaseSpeed = balance.ChaseSpeed;
        rotationSpeed = balance.RotationSpeed;
        idleTypeMin = balance.IdleTypeMin;
        idleTypeMax = balance.IdleTypeMax;
        noiseChaseSpeed = balance.NoiseChaseSpeed;
        noiseArriveDistance = balance.NoiseArriveDistance;
        noiseSearchDuration = balance.NoiseSearchDuration;
        noiseSearchRadius = balance.NoiseSearchRadius;
        combatSearchDuration = balance.CombatSearchDuration;
        combatSearchRadius = balance.CombatSearchRadius;
        alertDuration = balance.AlertDuration;
        loseSightDelay = balance.LoseSightDelay;
        targetReevaluateInterval = balance.TargetReevaluateInterval;
        targetSwitchPathDistanceDelta = balance.TargetSwitchPathDistanceDelta;
        attackDirectionLockTime = balance.AttackDirectionLockTime;
        attackImpactTime = balance.AttackImpactTime;
        attackRecoveryDuration = balance.AttackRecoveryDuration;
        hitStunDuration = balance.HitStunDuration;
        hitStunCooldown = balance.HitStunCooldown;
        targetSensor?.ApplyBalance(balance);
        enemyAttack?.ApplyBalance(balance);
        enemyHealth?.SetMaxHP(balance.MaxHp);
        enemyHealth?.ApplyBalance(balance);

        for (int i = 0; i < m_abilities.Length; i++)
        {
            m_abilities[i].ApplyBalance(balance);
        }
    }

    /// <summary>
    /// 풀에서 다시 꺼낼 적의 런타임 상태를 초기화합니다.
    /// </summary>
    /// <remarks>
    /// 비활성화 시점에 해제된 이벤트 구독은 <see cref="OnEnable"/>에서 다시 연결됩니다.
    /// 첫 활성화에서는 <see cref="Start"/>가 초기 상태 진입을 담당하고, 재사용 인스턴스만 여기서 배회 상태로 되돌립니다.
    /// </remarks>
    public void ResetForSpawn()
    {
        unchecked { SpawnGeneration++; }
        CacheReferences();

        m_knockbackVelocity = Vector3.zero;
        m_isStaggered = false;
        RestoreAgentPositionOwnership();

        ragdollController?.ResetForReuse();
        enemyAttack?.SetHitboxActive(false);

        targetSensor?.ClearAllInfo();
        targetSensor?.ClearNoise();
        targetSensor?.SetEngaged(false);

        enemyHealth?.RestoreFull();
        enemyHealth?.ResetStagger();

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.ResetPath();
        }

        ResetAnimation();

        if (!m_hasStarted)
        {
            return;
        }

        RollIdleType();
        TransitionTo(Wander);
        RestartDefenseNavigation();
    }

    /// <summary>활성화될 때 체력 이벤트를 구독합니다.</summary>
    private void OnEnable()
    {
        CacheReferences();

        if (enemyHealth == null)
        {
            return;
        }

        enemyHealth.OnDamaged += HandleDamaged;
        enemyHealth.OnDeath += HandleDied;
        enemyHealth.OnStaggered += HandleStaggered;
    }

    /// <summary>비활성화될 때 체력 이벤트 구독을 해제합니다.</summary>
    private void OnDisable()
    {
        if (enemyHealth == null)
        {
            return;
        }

        enemyHealth.OnDamaged -= HandleDamaged;
        enemyHealth.OnDeath -= HandleDied;
        enemyHealth.OnStaggered -= HandleStaggered;

        // 잠긴 채로 꺼지면 다시 켜졌을 때 남은 시간만큼 굳어 있습니다. 개체를 재사용(풀링)할 때 드러나는 문제라
        // 여기서 확실히 풀어 둡니다.
        m_isStaggered = false;
        RestoreAgentPositionOwnership();
    }

    /// <summary>초기 상태로 진입합니다.</summary>
    private void Start()
    {
        m_hasStarted = true;
        RollIdleType();

        // TODO(슬라이스 2): 휴면/배회 배치 구분, 스폰 기준점 기록.
        TransitionTo(Wander);
        RestartDefenseNavigation();
    }

    /// <summary>이동 애니메이션을 갱신하고, 경직 중이 아니면 현재 상태를 Tick합니다.</summary>
    /// <remarks>
    /// 경직은 상태를 교체하지 않고 <b>상태 위에 얹히는 잠금</b>입니다(§5.7). 그래서 여기서 Tick만 건너뜁니다.
    /// 상태 자체는 그대로 남아 있으므로 경직이 풀리면 하던 행동이 이어지며, 복귀 대상을 따로 기억할 필요가 없습니다.
    /// 넉백이 상태를 취소하지 않고 변위만 더하는 것과 같은 층위입니다.
    ///
    /// <b>감지는 경직 중에도 계속 돕니다.</b> 경직은 행동을 끊는 것이지 눈을 감기는 것이 아닙니다.
    /// 멈추면 대상 정보가 그 시간만큼 낡고, 경직 시간(<see cref="HitStunDuration"/>)이 시야 유지 시간
    /// (<c>LoseSightDelay</c>)보다 길면 풀리는 순간 "그동안 못 봤다"로 판정되어 대상을 잃고 배회로 돌아갑니다.
    /// 눈앞에 서 있는 상대를 경직 한 번에 놓치는 셈이라, 감지만은 잠금 밖에 둡니다.
    /// </remarks>
    private void Update()
    {
        // 버프 만료는 경직이나 사망과 관계없이 시간으로만 진행합니다.
        m_buffs?.Tick();

        UpdateLocomotionAnimator();
        UpdateKnockback();

        if (UpdateStagger())
        {
            targetSensor?.UpdatePerception();
            return;
        }

        // 잠금은 풀렸지만 이탈 블렌드가 남아 있으면 위치는 아직 루트 모션이 쥐고 있습니다.
        // 판단은 이미 재개된 상태이므로 아래 Tick은 그대로 돌립니다.
        UpdateStaggerHandover();

        // 제자리에 세워 둔 개체는 이동·추격·공격 판단을 하지 않습니다. 피격 경직과 넉백은 위에서 그대로 처리합니다.
        if (m_holdStill)
        {
            StopMoving();
            return;
        }

        if (TickDefenseNavigation())
        {
            return;
        }

        m_current?.Tick();
    }

    /// <summary>
    /// 남아 있는 넉백 속도만큼 이 개체를 밀어내고 속도를 줄입니다.
    /// </summary>
    /// <remarks>
    /// 살아 있는 개체는 뼈가 kinematic이고 이동을 NavMeshAgent가 소유하므로 물리 힘으로 밀 수 없습니다.
    /// 그래서 <see cref="NavMeshAgent.Move"/>로 변위를 더합니다. Agent가 NavMesh 경계와 높이를 맞춰 주므로
    /// Transform을 직접 움직일 때 생기는 벽 관통과 공중 부양을 피할 수 있습니다.
    ///
    /// 상태 머신의 이동 지시를 취소하지 않고 그 위에 변위를 더합니다. 밀리는 동안에도 추격을 계속하는 편이
    /// "밀렸지만 다시 달려든다"는 모습에 맞고, 경직으로 행동을 끊는 것은 별도 규칙(<see cref="HandleStaggered"/>)의 몫입니다.
    /// 경직 중에도 이 변위는 계속 더해집니다. 넉백과 경직 루트 모션은 둘 다 밀어내는 값이라 그대로 합산됩니다.
    ///
    /// 지수 감쇠로 줄입니다. 일정량씩 빼면 초기 속도에 따라 지속 시간이 달라져 무기마다 감이 흔들립니다.
    /// </remarks>
    private void UpdateKnockback()
    {
        if (m_knockbackVelocity.sqrMagnitude <= 0.0001f)
        {
            m_knockbackVelocity = Vector3.zero;
            return;
        }

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.Move(m_knockbackVelocity * Time.deltaTime);
        }

        m_knockbackVelocity *= Mathf.Exp(-m_knockbackDamping * Time.deltaTime);
    }

    // =========================
    // 경직 (오버레이 인터럽트)
    // 별도 상태가 아니라 하위 행동 위에 얹히는 잠금입니다(§5.7).
    // 누적·한계치·면역은 EnemyHealth가 소유하고, 여기서는 발동 이후의 잠금과 연출만 다룹니다.
    // =========================

    /// <summary>지금 경직 중인지 여부입니다.</summary>
    private bool m_isStaggered;

    /// <summary>경직 잠금이 풀리는 시각입니다.</summary>
    private float m_staggerEndTime;

    /// <summary>이번 경직에서 애니메이터의 실제 클립 길이를 이미 반영했는지 여부입니다.</summary>
    private bool m_staggerLengthResolved;

    /// <summary>
    /// 위치를 루트 모션이 쥐고 있는 구간인지 여부입니다.
    /// </summary>
    /// <remarks>
    /// <b>행동 잠금보다 길게 유지됩니다.</b> 잠금이 풀리는 순간 위치를 Agent에 돌려주면, 애니메이터가 아직
    /// 경직에서 빠져나오는 이탈 블렌드(0.25초) 중인데 몸은 Agent가 끌고 가 위치가 끊겨 보입니다.
    /// 블렌드가 끝날 때까지 루트 모션이 위치를 계속 쥐고 있어야 일어서는 동작과 달리기가 이어집니다.
    /// </remarks>
    private bool m_staggerRootMotionActive;

    /// <summary>이탈 블렌드가 끝나지 않아도 위치를 Agent에 돌려줄 한계 시각입니다.</summary>
    /// <remarks>
    /// 애니메이터 배선이 없거나 다른 상태로 튀어 블렌드 종료를 감지하지 못하면 위치 소유권이 영구히
    /// 루트 모션에 남습니다. 그러면 Agent 이동이 화면에 반영되지 않아 제자리에서 미끄러지는 모습이 됩니다.
    /// </remarks>
    private float m_staggerHandoverDeadline;

    /// <summary>이탈 블렌드를 기다려 줄 최대 시간(초)입니다.</summary>
    /// <remarks>애니메이터의 이탈 전이(0.25초)보다 넉넉하게 둡니다.</remarks>
    private const float StaggerHandoverTimeout = 0.6f;

    /// <summary>경직 전 Agent의 회전 갱신 설정입니다. 인계 시 그대로 되돌립니다.</summary>
    /// <remarks>프리팹 값을 가정하지 않고 원래 값을 기억합니다. 다른 시스템이 껐다 켰을 수 있습니다.</remarks>
    private bool m_agentRotationBeforeStagger = true;

    /// <summary>경직이 시작된 위치입니다. 루트 모션이 실제로 얼마나 옮겼는지 확인하는 데 씁니다.</summary>
    private Vector3 m_staggerStartPosition;

    /// <summary>경직 스테이트의 짧은 이름 해시입니다.</summary>
    /// <remarks>
    /// 잠금 시간을 애니메이터의 실제 재생 길이에 맞추려면 지금 어느 스테이트인지 알아야 합니다.
    /// 파라미터 이름(DoStagger/IsStagger)과 같은 어휘를 쓰므로 배선이 갈라질 여지가 적습니다.
    /// </remarks>
    private static readonly int StaggerStateHash = Animator.StringToHash("Stagger");

    /// <summary>지금 경직으로 행동이 잠겨 있는지 여부입니다.</summary>
    public bool IsStaggered => m_isStaggered;

    /// <summary>
    /// 경직 발동을 받아 행동을 잠그고 경직 연출을 시작합니다.
    /// </summary>
    /// <remarks>
    /// 진행 중이던 공격은 여기서 취소합니다(§5.7). 경직은 아무것도 못 하는 상태이므로
    /// 판정 전이든 후딜레이 중이든 그 공격은 이어지지 않습니다. 하위 상태를 추격으로 되돌리면
    /// <see cref="AttackState.Exit"/>가 판정 콜라이더와 공격 애니메이터 플래그를 정리해 줍니다.
    ///
    /// 하울링도 같은 이유로 취소됩니다. 하울링 시도 기회는 성공·취소와 무관하게 소모되므로(§5.5.1)
    /// 취소해도 규칙이 깨지지 않습니다. 취소하지 않으면 경직 클립이 하울링 위에 얹혔다가
    /// 하울링 도중으로 되돌아가 자세가 어긋납니다.
    /// </remarks>
    private void HandleStaggered()
    {
        if (m_current == Dead)
        {
            return;
        }

        // 공격·하울링 같은 진행 중인 행동을 먼저 정리한 뒤 잠급니다.
        // 순서를 뒤집으면 정리 과정이 다시 이동을 지시해 잠금 직후에 움직입니다.
        //
        // 무엇이 끊겼는지에 따라 경직 이후가 달라지므로(하울링 전 공격이 끊기면 경직 후 하울링, §5.5.2)
        // 취소 판단은 교전 상태가 소유합니다.
        if (m_current == Combat && Combat != null)
        {
            Combat.CancelForStagger();
        }
        else if (IsAttackingDefenseObjective)
        {
            // 목표물 공격도 경직에서 중단하고, 잠금 해제 후 경로가 공격 가능 여부를 다시 판단합니다.
            TransitionTo(Wander);
        }

        StopMoving();

        // 경직 동안 위치의 주인을 루트 모션으로 넘깁니다. Agent가 계속 자기 내부 위치를 transform에 쓰면
        // 밀려난 만큼을 매 프레임 조금씩 되돌려, 결국 클립이 끝난 자리가 아닌 어중간한 지점에 서게 됩니다.
        // 끝날 때 Warp로 Agent를 최종 위치에 맞춰 다시 넘겨줍니다.
        if (agent != null && agent.enabled)
        {
            agent.updatePosition = false;

            // 회전도 함께 뺏습니다. 위치만 넘겨받으면 Agent가 자기 내부 속도 방향으로 몸을 계속 돌려,
            // 쓰러져 있는 동안 몸이 제자리에서 회전하며 버벅이는 것처럼 보입니다.
            // 경직 클립의 몸 흔들림은 이미 뼈 애니메이션이 표현하므로 이 구간에 회전 주체가 따로 있을 이유가 없습니다.
            m_agentRotationBeforeStagger = agent.updateRotation;
            agent.updateRotation = false;
        }

        m_isStaggered = true;
        m_staggerRootMotionActive = true;
        m_staggerLengthResolved = false;
        m_staggerStartPosition = transform.position;

        // 애니메이터 스테이트에 들어가기 전까지 쓰는 임시값입니다. 들어간 뒤에는 실제 재생 길이로 다시 잡습니다.
        m_staggerEndTime = Time.time + HitStunDuration;

        // 경직 동안에는 만료 시계를 세웁니다. 행동은 잠기지만 누가 쐈는지까지 잊을 이유는 없습니다.
        // 잠금이 추적 유지 시간보다 길면, 대상을 볼 수 없는 각도에서 맞았을 때 일어나는 순간 대상이 사라져
        // 아무 일도 없었다는 듯 배회로 돌아갑니다.
        targetSensor?.ExtendTrackingHold(HitStunDuration);

        PlayStaggerAnimation();
    }

    /// <summary>
    /// 경직 잠금을 진행시키고, 아직 잠겨 있으면 true를 돌려줍니다.
    /// </summary>
    /// <returns>경직 중이라 이번 프레임의 상태 Tick을 건너뛰어야 하면 true입니다.</returns>
    /// <remarks>
    /// 잠금 시간은 <b>애니메이터가 실제로 재생하는 길이</b>를 따릅니다. 밸런스의 <see cref="HitStunDuration"/>은
    /// 스테이트에 들어가기 전(전이 중)과 애니메이터가 없는 개체를 위한 초기값일 뿐입니다.
    ///
    /// 숫자를 직접 맞춰 두지 않는 이유는 둘이 조용히 어긋나기 때문입니다. 잠금이 짧으면 클립이 중간에 끊겨
    /// 일어나다 만 자세로 복귀하고, 길면 다 일어난 채로 굳어 있습니다. 클립을 교체하거나 스테이트 속도를
    /// 바꿔도 이 방식은 그대로 따라옵니다.
    /// </remarks>
    private bool UpdateStagger()
    {
        if (!m_isStaggered)
        {
            return false;
        }

        ResolveStaggerLengthFromAnimator();

        if (Time.time < m_staggerEndTime)
        {
            return true;
        }

        EndStagger();
        return false;
    }

    /// <summary>
    /// 애니메이터가 경직 스테이트에 들어왔으면 남은 재생 시간으로 잠금 종료 시각을 다시 잡습니다.
    /// </summary>
    /// <remarks>
    /// 진입 전이가 끝나야 스테이트 정보를 읽을 수 있으므로 매 프레임 확인하다가 한 번만 반영합니다.
    /// <c>length</c>는 스테이트 속도가 반영된 실제 재생 길이이며, 남은 시간은 진행도를 빼서 구합니다.
    /// </remarks>
    private void ResolveStaggerLengthFromAnimator()
    {
        if (m_staggerLengthResolved || animator == null)
        {
            return;
        }

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (state.shortNameHash != StaggerStateHash)
        {
            return;
        }

        m_staggerLengthResolved = true;

        float remaining = state.length * Mathf.Max(0.0f, 1.0f - state.normalizedTime);
        m_staggerEndTime = Time.time + remaining;
    }

    /// <summary>경직 잠금을 풀고 면역 윈도우를 시작시킵니다.</summary>
    /// <remarks>
    /// 경직 전에 세워 둔 경로는 밀려나기 전 위치를 기준으로 계산된 것이라 그대로 두면 밀려난 만큼을
    /// 되돌아가는 이동이 먼저 나옵니다. 경로를 비워 두면 다음 Tick에서 지금 위치를 기준으로 다시 세웁니다.
    /// </remarks>
    private void EndStagger()
    {
        if (!m_isStaggered)
        {
            return;
        }

        m_isStaggered = false;

        if (m_debugLogStagger)
        {
            Vector3 moved = transform.position - m_staggerStartPosition;
            moved.y = 0.0f;

            // 뒤로 밀렸는지 확인하려면 몸이 보는 방향 기준으로 봐야 합니다. 월드 좌표로는 회전에 따라 부호가 뒤집힙니다.
            float backward = -Vector3.Dot(moved, transform.forward);

            Debug.Log(
                $"[Stagger] 종료 | 후퇴 {backward:F2}m (총 이동 {moved.magnitude:F2}m) " +
                $"| 클립길이반영={m_staggerLengthResolved}",
                this);
        }

        EndStaggerAnimation();

        // 위치 소유권은 여기서 돌려주지 않습니다. 애니메이터가 아직 경직에서 빠져나오는 중이므로,
        // 이탈 블렌드가 끝날 때까지 루트 모션이 위치를 계속 쥐고 있어야 동작이 이어집니다.
        // 인계는 UpdateStaggerHandover가 처리합니다.
        m_staggerHandoverDeadline = Time.time + StaggerHandoverTimeout;

        // 면역 윈도우는 경직이 끝난 시점부터 재므로, 끝났다는 사실을 누적 소유자에게 알려야 합니다.
        enemyHealth?.NotifyStaggerEnded();

        // 잠금이 멈춘 이동을 다시 켭니다. 경직 진입에서 StopMoving을 불렀으므로 여기서 켜 주지 않으면
        // 상태는 그대로인데 발만 멈춘 채로 남습니다.
        // 교전은 하위 상태와 하울링 이어가기까지 함께 판단해야 하므로 교전 상태가 직접 처리합니다(§5.5.2).
        if (m_current == Combat && Combat != null)
        {
            Combat.ResumeAfterStagger();
            return;
        }

        // 배회·소음 추적처럼 하위 상태가 없는 경로입니다. 진입 처리 전체가 아니라 이동만 되살립니다.
        m_current?.ResumeMovement();
    }

    /// <summary>
    /// 남아 있는 루트 모션 변위를 경직 중에만 실제 이동으로 옮깁니다.
    /// </summary>
    /// <remarks>
    /// 경직 클립은 제자리 애니메이션이 아니라 몸이 뒤로 밀려나는 이동을 품고 있습니다(실측 후퇴 약 1.25m).
    /// 그 변위를 버리면 화면에서는 밀려나는데 실제 위치는 그대로여서, 발이 미끄러지고
    /// 사거리 판정도 밀리지 않은 위치를 기준으로 남습니다.
    ///
    /// <b>클립 임포트 설정에 의존합니다.</b> 경직 클립의 Root Transform Position(XZ)에서
    /// <c>Bake Into Pose</c>가 켜져 있으면 수평 이동이 포즈에 구워져 루트가 움직이지 않고,
    /// <see cref="Animator.deltaPosition"/>의 XZ가 0이 되어 이 코드가 아무 일도 하지 않습니다.
    /// 그 상태에서는 몸만 뒤로 밀려 보이다가 클립이 끝나면 제자리로 돌아옵니다.
    /// 이동 클립(달리기 등)은 NavMeshAgent가 이동을 소유해야 하므로 반대로 켜 두는 것이 맞습니다.
    ///
    /// <b>경직 동안에는 Agent가 아니라 루트 모션이 위치의 주인입니다.</b> <c>updatePosition</c>을 켠 채로 두면
    /// Agent가 매 프레임 <c>transform.position</c>을 자기 내부 위치로 덮어써서, 밀려난 만큼이 조금씩 되돌아가
    /// 클립이 끝난 자리가 아닌 어중간한 지점에 서게 됩니다. 그래서 <see cref="HandleStaggered"/>에서
    /// <c>updatePosition</c>을 끄고, <see cref="EndStagger"/>에서 <c>Warp</c>로 맞춰 돌려줍니다.
    ///
    /// 목표 위치는 <see cref="Animator.rootPosition"/>(루트 모션이 가려는 절대 위치)을 그대로 씁니다.
    /// 프레임 변위를 누적하는 것보다 어긋남이 쌓이지 않습니다.
    ///
    /// <see cref="NavMesh.SamplePosition"/>으로 한 번 보정하는 것은 벽을 뚫고 밀려나거나 바닥 밖으로
    /// 나가는 것을 막기 위해서입니다. 밀리는 거리가 1.25m라 좁은 통로에서는 실제로 벽에 닿습니다.
    ///
    /// <b>이 구간 밖에서는 변위를 버립니다.</b> 이동 클립에도 루트 모션이 들어 있어 그대로 소비하면
    /// NavMeshAgent가 시키는 이동 위에 애니메이션 이동이 겹쳐 두 배로 움직입니다.
    /// 이동의 주인은 여전히 Agent이고, 경직과 그 이탈 블렌드만 예외입니다.
    ///
    /// 구간이 잠금보다 긴 이유는 <see cref="UpdateStaggerHandover"/>에 있습니다. 이탈 블렌드 동안에는
    /// 경직 자세와 이동 자세가 섞이므로, 그 섞인 결과의 루트 모션을 그대로 따라가야 위치가 이어집니다.
    ///
    /// 회전(<see cref="Animator.deltaRotation"/>)은 일부러 쓰지 않습니다. 이 클립의 루트 회전은
    /// 시작과 끝이 같아 순 회전이 0이고(흔들렸다가 제자리로 돌아옴), 몸이 휘청이는 모습은 이미 뼈 애니메이션이
    /// 표현합니다. 굳이 소비하면 Agent의 회전 갱신과 주인이 겹칩니다.
    /// </remarks>
    private void OnAnimatorMove()
    {
        // 기존처럼 Enemy 루트 자체에 Animator가 있는 프리팹도 지원합니다.
        // 모델 자식 Animator를 쓰는 경우에는 같은 GameObject의 Relay가 이 메서드 대신 전달합니다.
        if (animator != null && animator.gameObject == gameObject)
        {
            ApplyAnimatorRootMotion(animator);
        }
    }

    /// <summary>현재 Enemy Animator가 만든 루트 모션을 경직 구간에만 소비합니다.</summary>
    /// <param name="sourceAnimator">이번 프레임의 루트 모션을 계산한 Animator입니다.</param>
    internal void ApplyAnimatorRootMotion(Animator sourceAnimator)
    {
        if (!m_staggerRootMotionActive || sourceAnimator == null || sourceAnimator != animator)
        {
            return;
        }

        // 이 프레임의 루트 모션 변위를 현재 위치에 더합니다.
        //
        // 절대 위치(rootPosition)가 아니라 변위(deltaPosition)를 쓰는 이유는, 아바타 루트와 GameObject
        // 피벗이 어긋나 있으면 절대 위치 대입이 매 프레임 그 차이만큼 튀기 때문입니다.
        // 변위는 그 차이와 무관하게 "이번 프레임에 얼마나 움직였는가"만 담습니다.
        Vector3 target = transform.position + sourceAnimator.deltaPosition;

        // NavMesh 밖으로 한 발이라도 나가면 그 지점에서 출발하는 경로 계산이 전부 실패하고
        // (EnemyTargetSensor.GetPathDistance가 PathComplete만 인정) 유효 대상이 사라집니다.
        // 그러면 추격이 갈 곳을 잃고 제자리에 굳습니다. 밀리다 마는 것이 굳는 것보다 낫습니다.
        if (!NavMesh.SamplePosition(target, out NavMeshHit hit, StaggerNavMeshSampleRadius, NavMesh.AllAreas))
        {
            return;
        }

        // 스냅 결과를 매 프레임 그대로 쓰면 안 됩니다. SamplePosition은 "가장 가까운 NavMesh 지점"이라
        // 경계나 기울어진 바닥에서는 프레임마다 조금씩 다른 곳을 돌려주고, 그 흔들림이 버벅임으로 보입니다.
        //
        // 그래서 수평 편차로 판정만 합니다. 편차가 작으면 이미 NavMesh 위이므로 루트 모션의 수평 이동을
        // 그대로 두고 높이만 바닥에 맞춥니다. 편차가 크면 실제로 벗어난 것이므로 통째로 당깁니다.
        Vector3 flatTarget = new Vector3(target.x, 0.0f, target.z);
        Vector3 flatHit = new Vector3(hit.position.x, 0.0f, hit.position.z);

        transform.position = (flatTarget - flatHit).sqrMagnitude
                <= StaggerOnMeshTolerance * StaggerOnMeshTolerance
            ? new Vector3(target.x, hit.position.y, target.z)
            : hit.position;
    }

    /// <summary>루트 모션 목표 지점을 NavMesh 위로 볼 수평 허용 오차(m)입니다.</summary>
    /// <remarks>
    /// 이 값보다 가까우면 스냅 결과를 무시하고 루트 모션의 수평 이동을 그대로 씁니다.
    /// 매 프레임 스냅으로 생기는 미세한 좌우 흔들림을 없애기 위한 것입니다.
    /// </remarks>
    private const float StaggerOnMeshTolerance = 0.25f;

    /// <summary>경직 루트 모션 위치를 NavMesh 위로 당길 때 허용하는 반경(m)입니다.</summary>
    /// <remarks>
    /// 후퇴량이 1.25m라 좁은 통로나 바닥 경계에서는 실제로 NavMesh를 벗어납니다.
    /// 벽 쪽으로 스냅되는 것이 NavMesh 밖에 서는 것보다 낫기 때문에 후퇴량보다 넉넉하게 둡니다.
    /// </remarks>
    private const float StaggerNavMeshSampleRadius = 2.0f;

    /// <summary>이동 속도를 애니메이터 MoveSpeed 파라미터로 전달합니다.</summary>
    /// <remarks>
    /// 보통은 <see cref="NavMeshAgent.velocity"/>를 씁니다. 다만 경직과 그 이탈 블렌드 동안에는 위치의 주인이
    /// 루트 모션이라 Agent 속도가 실제 이동과 다릅니다. Agent는 내부 경로만 진행하고 있어서, 그 값을 쓰면
    /// 일어서는 중인데 이동 블렌드가 예전 방향으로 전력 질주를 가리켜 자세가 어긋납니다.
    ///
    /// 그 구간에는 <b>0을 넣습니다.</b> 실제 변위로 속도를 재는 방법도 써 봤지만, 프레임 단위 루트 모션은
    /// 값이 크게 출렁여서 이동 블렌드가 따라 떨리고 그것이 일어날 때의 버벅임으로 보였습니다.
    /// 일어서는 동안은 걷는 것이 아니므로 0이 뜻으로도 맞습니다.
    ///
    /// 인계 직후 값이 튀지 않는 이유는, <c>ResetPath</c>와 <c>Warp</c>를 거친 Agent가 정지 상태에서
    /// 가속도(8m/s²)를 따라 올라오기 때문입니다. 0에서 실제 속도로 이어집니다.
    /// </remarks>
    private void UpdateLocomotionAnimator()
    {
        if (animator == null || agent == null)
        {
            return;
        }

        Vector3 velocity = m_staggerRootMotionActive ? Vector3.zero : agent.velocity;

        float speed = velocity.magnitude;

        // 애니메이션은 실제 이동 속도를 그대로 따릅니다. 감속이 걸리면 블렌드 트리가
        // 느린 구간(Run → Walk)으로 내려가고, 재생 속도(animator.speed)는 건드리지 않습니다.
        // 공격·피격 모션까지 느려지는 것을 피하기 위해서입니다.
        float animationSpeed = AlwaysRun && !IsForcedToWalk && speed > 0.01f
            ? Mathf.Max(speed, ChaseSpeed)
            : speed;

        animator.SetFloat(AnimMoveSpeed, animationSpeed);

        // 이동 방향을 자기 기준으로 바꿔 2D 블렌드 축에 넣습니다.
        // 월드 방향을 그대로 쓰면 몸이 어디를 보든 같은 값이 되어 옆걸음과 앞걸음을 구분하지 못합니다.
        if (speed <= 0.01f)
        {
            animator.SetFloat(AnimMoveX, 0f);
            animator.SetFloat(AnimMoveZ, 0f);
            return;
        }

        Vector3 local = transform.InverseTransformDirection(velocity / speed);
        animator.SetFloat(AnimMoveX, local.x);
        animator.SetFloat(AnimMoveZ, local.z);
    }

    // =========================
    // 애니메이터 연동
    // 파라미터 이름을 여기 모아 두어 상태 클래스들이 같은 문자열을 각자 들고 있지 않게 합니다.
    // =========================
    private static readonly int AnimMoveSpeed = Animator.StringToHash("MoveSpeed");
    private static readonly int AnimInAttackRange = Animator.StringToHash("InAttackRange");
    private static readonly int AnimAttack = Animator.StringToHash("DoAttack");

    // 사망 관련 애니메이터 파라미터는 두지 않습니다. 사망은 애니메이터를 끄고 래그돌이 이어받으므로
    // 재생할 클립도, 종료를 기다릴 스테이트도 없습니다(DeadState 참고).

    /// <summary>공격 중인지 여부입니다. 공격 스테이트를 유지하는 조건입니다.</summary>
    private static readonly int AnimIsAttack = Animator.StringToHash("IsAttack");

    /// <summary>몇 번째 공격인지입니다. 1이 첫 공격, 2가 이어지는 공격입니다.</summary>
    private static readonly int AnimAttackCombo = Animator.StringToHash("AttackCombo");

    /// <summary>어느 손으로 치는지입니다. 0이 왼손, 1이 오른손입니다.</summary>
    private static readonly int AnimAttackSide = Animator.StringToHash("AttackSide");

    /// <summary>이동 방향의 좌우 성분입니다. 2D 이동 블렌드의 X축입니다.</summary>
    private static readonly int AnimMoveX = Animator.StringToHash("MoveX");

    /// <summary>이동 방향의 앞뒤 성분입니다. 2D 이동 블렌드의 세로축이며, 값은 몸 기준 z 성분입니다.</summary>
    /// <remarks>이름이 MoveY였을 때도 담고 있던 값은 z였습니다. 플레이어와 규약을 맞추며 이름을 정정했습니다.</remarks>
    private static readonly int AnimMoveZ = Animator.StringToHash("MoveZ");

    /// <summary>대기 동작 혼합 비율입니다. 개체마다 한 번 뽑아 고정합니다.</summary>
    private static readonly int AnimIdleType = Animator.StringToHash("IdleType");

    /// <summary>애니메이터를 기본 상태로 되돌리는 트리거입니다.</summary>
    private static readonly int AnimReset = Animator.StringToHash("DoReset");

    /// <summary>사망 클립 트리거입니다. 래그돌을 쓸 수 없는 개체의 폴백 연출에만 씁니다.</summary>
    private static readonly int AnimDead = Animator.StringToHash("DoDeath");

    /// <summary>소음 경계(두리번) 중인지 여부입니다. 클립은 `WW_LookAround`를 씁니다.</summary>
    /// <remarks>
    /// 트리거가 아니라 bool입니다. 두리번은 지속 상태이고 나가는 길이 셋(추적/배회/교전)이라
    /// 트리거로는 "지금 경계 중인가"를 되읽을 수 없습니다.
    ///
    /// 이름을 `IsAlert`가 아니라 `IsLookAround`로 맞췄습니다. 애니메이터의 스테이트·클립·진입 트리거가
    /// 모두 `LookAround` 어휘를 쓰므로, 코드만 다른 이름을 쓰면 파라미터가 조용히 어긋납니다.
    /// </remarks>
    private static readonly int AnimIsAlert = Animator.StringToHash("IsLookAround");

    /// <summary>두리번을 시작하는 트리거입니다.</summary>
    private static readonly int AnimLookAround = Animator.StringToHash("DoLookAround");

    /// <summary>소음 인지 게이지의 진행도입니다. 두리번 강도 블렌드에 쓰는 선택 파라미터입니다.</summary>
    private static readonly int AnimAlertLevel = Animator.StringToHash("AlertLevel");

    /// <summary>경직을 시작하는 트리거입니다. 클립은 `WW_Stagger_Large01`을 씁니다.</summary>
    private static readonly int AnimStagger = Animator.StringToHash("DoStagger");

    /// <summary>경직 스테이트를 유지하는 조건입니다.</summary>
    /// <remarks>
    /// 경직 스테이트의 탈출 전이가 이 값이 false가 되는 것을 조건으로 하고 종료 시점(Exit Time)을 쓰지 않으므로,
    /// 클립을 언제까지 재생할지는 전적으로 코드가 정합니다. 잠금 시간과 클립 길이를 맞춰야 하는 이유가 이것입니다.
    /// </remarks>
    private static readonly int AnimIsStagger = Animator.StringToHash("IsStagger");

    /// <summary>
    /// 애니메이터를 기본 상태로 되돌립니다.
    /// </summary>
    /// <remarks>
    /// 어떤 상태에 걸려 빠져나오지 못할 때 쓰는 탈출구입니다.
    /// 지금은 부르는 곳이 없습니다. 필요한 시점이 정해지면 그때 연결합니다.
    /// </remarks>
    public void ResetAnimation()
    {
        animator?.SetTrigger(AnimReset);
    }

    /// <summary>공격 애니메이션을 재생합니다.</summary>
    /// <param name="comboStep">1이면 첫 공격, 2면 이어지는 두 번째 공격입니다.</param>
    public void PlayAttackAnimation(int comboStep = 1)
    {
        if (animator == null)
        {
            return;
        }

        // 같은 동작만 반복되면 단조로워 보이므로 휘두르는 손을 매번 새로 고릅니다.
        // 중간값을 넣으면 좌우 동작이 섞여 어색해지므로 0이나 1만 넣습니다.
        animator.SetFloat(AnimAttackSide, Random.value < 0.5f ? 0f : 1f);

        animator.SetBool(AnimIsAttack, true);
        animator.SetInteger(AnimAttackCombo, comboStep);
        animator.SetTrigger(AnimAttack);
    }

    /// <summary>
    /// 공격 상태에서 빠져나왔음을 애니메이터에 알립니다.
    /// </summary>
    /// <remarks>공격 스테이트는 이 값이 false가 되어야 이동으로 돌아갑니다.</remarks>
    public void EndAttackAnimation()
    {
        if (animator == null)
        {
            return;
        }

        animator.SetBool(AnimIsAttack, false);
        animator.SetInteger(AnimAttackCombo, 0);
    }

    /// <summary>공격 사거리 안에 있는지를 애니메이터에 전달합니다.</summary>
    /// <param name="inRange">사거리 안이면 true입니다.</param>
    public void SetInAttackRangeAnimation(bool inRange)
    {
        animator?.SetBool(AnimInAttackRange, inRange);
    }

    /// <summary>
    /// 사망 클립을 재생합니다. 래그돌을 쓸 수 없는 개체의 폴백 연출입니다.
    /// </summary>
    /// <remarks>
    /// 정상 경로는 래그돌입니다(<see cref="DeadState"/> 주석 참고 — 클립을 쓰면 정해진 방향으로만 쓰러져
    /// 모두 똑같이 죽습니다). 이 함수는 물리 골격이 없는 개체가 마지막 자세로 굳어 버리는 것만 막습니다.
    ///
    /// 사거리 표시를 함께 내리는 이유는, 공격 사거리 안에서 죽으면 그 값이 참인 채로 남아
    /// 사망 클립 위로 공격 대기 자세가 섞이기 때문입니다.
    /// </remarks>
    public void PlayDeathAnimation()
    {
        if (animator == null)
        {
            return;
        }

        animator.SetBool(AnimInAttackRange, false);
        animator.SetBool(AnimIsAttack, false);
        animator.SetTrigger(AnimDead);
    }

    /// <summary>
    /// 소음 경계(두리번) 상태 여부를 애니메이터에 전달합니다.
    /// </summary>
    /// <param name="alert">경계 중이면 true입니다.</param>
    /// <remarks>
    /// <b>파라미터가 없어도 동작해야 합니다.</b> 두리번 클립과 애니메이터 배선은 별도로 진행 중이며,
    /// 없는 파라미터에 값을 넣으면 유니티가 매 호출마다 경고를 찍어 콘솔이 묻힙니다.
    /// 배선이 끝나면 이 확인은 통과하므로 코드를 되돌릴 필요가 없습니다.
    /// </remarks>
    public void SetAlertAnimation(bool alert)
    {
        if (animator == null || !HasAnimatorParameter(AnimIsAlert))
        {
            return;
        }

        animator.SetBool(AnimIsAlert, alert);

        // 진입은 트리거가 함께 있어야 성립합니다. 애니메이터 조건이 DoLookAround AND IsLookAround입니다.
        // bool만 세우면 스테이트에 들어가지 못하고, 그러면 두리번 자세가 아예 나오지 않습니다.
        if (alert && HasAnimatorParameter(AnimLookAround))
        {
            animator.SetTrigger(AnimLookAround);
        }

        // 두리번 강도 블렌드는 선택 사항입니다. 파라미터를 두지 않아도 경계 자체는 동작합니다.
        if (HasAnimatorParameter(AnimAlertLevel))
        {
            animator.SetFloat(AnimAlertLevel, targetSensor != null ? targetSensor.NoiseAwareness01 : 0f);
        }
    }

    /// <summary>
    /// 능력 행동의 애니메이션을 시작합니다. 진입 트리거와 유지 bool을 함께 세웁니다.
    /// </summary>
    /// <param name="triggerHash">스테이트로 진입시키는 트리거 파라미터 해시입니다(예: `DoHowl`).</param>
    /// <param name="boolHash">스테이트를 유지하는 bool 파라미터 해시입니다(예: `IsHowl`).</param>
    /// <remarks>
    /// 하울링·두리번·경직과 같은 규약입니다. bool만 세우면 스테이트에 들어가지 못하고, 트리거만 쏘면
    /// 탈출 조건이 즉시 성립해 한 프레임 만에 빠져나옵니다.
    /// 파라미터가 없어도 동작해야 하므로 존재를 확인합니다. 행동 진행은 능력의 타이머가 맡고 애니메이션은 표현입니다.
    /// </remarks>
    public void PlayActionAnimation(int triggerHash, int boolHash)
    {
        if (animator == null)
        {
            return;
        }

        if (HasAnimatorParameter(boolHash))
        {
            animator.SetBool(boolHash, true);
        }

        if (HasAnimatorParameter(triggerHash))
        {
            animator.SetTrigger(triggerHash);
        }
    }

    /// <summary>능력 행동의 스테이트에서 빠져나왔음을 애니메이터에 알립니다.</summary>
    /// <param name="boolHash">스테이트를 유지하던 bool 파라미터 해시입니다.</param>
    public void EndActionAnimation(int boolHash)
    {
        if (animator == null || !HasAnimatorParameter(boolHash))
        {
            return;
        }

        animator.SetBool(boolHash, false);
    }

    /// <summary>경직 애니메이션을 시작합니다.</summary>
    /// <remarks>
    /// 하울링·두리번과 같은 규약입니다. 진입은 트리거, 유지는 bool이 담당합니다.
    /// bool만 세우면 스테이트에 들어가지 못하고, 트리거만 쏘면 탈출 조건이 즉시 성립해 한 프레임 만에 빠져나옵니다.
    ///
    /// <b>파라미터가 없어도 동작해야 합니다.</b> 애니메이터 배선이 없는 개체라도 경직 잠금 자체는 성립해야 하며,
    /// 없는 파라미터에 값을 넣으면 유니티가 매 호출마다 경고를 찍어 콘솔이 묻힙니다.
    /// </remarks>
    public void PlayStaggerAnimation()
    {
        if (animator == null)
        {
            Debug.LogWarning("[Stagger] Animator가 없어 경직 연출을 재생하지 못했습니다. 잠금은 그대로 걸립니다.", this);
            return;
        }

        bool hasBool = HasAnimatorParameter(AnimIsStagger);
        bool hasTrigger = HasAnimatorParameter(AnimStagger);

        if (hasBool)
        {
            animator.SetBool(AnimIsStagger, true);
        }

        if (hasTrigger)
        {
            animator.SetTrigger(AnimStagger);
        }

        // 둘 중 하나라도 없으면 스테이트로 들어가지 못합니다. 잠금만 걸리고 자세는 그대로라
        // "경직이 안 걸린다"로 보이므로, 원인을 파라미터 쪽으로 특정할 수 있게 남깁니다.
        if (!hasBool || !hasTrigger)
        {
            Debug.LogWarning(
                $"[Stagger] 애니메이터 파라미터 누락 (IsStagger={hasBool}, DoStagger={hasTrigger}) - " +
                $"컨트롤러 '{(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "없음")}'",
                this);
        }
    }

    /// <summary>경직 상태에서 빠져나왔음을 애니메이터에 알립니다.</summary>
    public void EndStaggerAnimation()
    {
        if (animator == null || !HasAnimatorParameter(AnimIsStagger))
        {
            return;
        }

        animator.SetBool(AnimIsStagger, false);
    }

    /// <summary>지정한 파라미터가 현재 애니메이터에 선언되어 있는지 확인합니다.</summary>
    /// <remarks>
    /// 매 프레임 부르는 경로가 아니므로 순회 비용은 문제가 되지 않습니다.
    /// 상태 진입·이탈에서만 호출합니다.
    /// </remarks>
    private bool HasAnimatorParameter(int nameHash)
    {
        if (animator == null)
        {
            return false;
        }

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].nameHash == nameHash)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>현재 상태를 빠져나가고 지정한 상태로 진입합니다.</summary>
    /// <param name="next">전이할 상태입니다.</param>
    public void TransitionTo(EnemyStateBase next)
    {
        if (next == Combat && ShouldDeferSquadCombat()) return;
        if (next == null || next == m_current)
        {
            return;
        }

        // 혼합형의 어그로는 교전이 이어지는 동안만 유지합니다. 교전을 벗어나면 방어 대상으로 돌아가고,
        // 다음 피격에서 다시 전환할 수 있게 둡니다.
        if (m_current == Combat) m_hybridAggroActive = false;

        EndDefenseWaitFlourish();
        m_current?.Exit();
        if (next == Combat || next == Dead)
        {
            ResetDefenseDestinationTracking();
            RestoreDefenseApproachSettings();
            // 교전으로 빠지거나 죽으면 공격 포인트를 놓아 다른 적이 고를 수 있게 합니다. 돌아오면 다시 고릅니다.
            ReleaseDefenseAttackPoint();
        }
        m_current = next;
        m_current.Enter();
    }

    /// <summary>공격 애니메이션 이벤트에서 호출되어 판정 시점을 알립니다.</summary>
    /// <remarks>
    /// 이제 여기서 피해를 넣지 않습니다. 실제 적중은 손에 달린 판정 콜라이더가 결정합니다.
    /// 이름이 하는 일과 어긋나 보이지만, 기존 클립의 애니메이션 이벤트가 이 이름으로 묶여 있어 그대로 둡니다.
    /// 클립을 새로 만들 때 이벤트 이름을 정리하면 그때 함께 바꿉니다.
    /// 상태는 이 통보와 자체 타이머 중 먼저 오는 것을 판정 시점으로 삼고 한 번의 공격에서 한 번만 처리합니다.
    /// </remarks>
    /// <summary>공격 클립의 애니메이션 이벤트에서 호출되어 판정 콜라이더를 켭니다.</summary>
    /// <remarks>
    /// 이 시점부터 손에 달린 판정 콜라이더가 닿은 것에 피해를 줍니다.
    /// 판정 시점 통보도 함께 처리해 상태가 후딜레이로 넘어갈 수 있게 합니다.
    /// </remarks>
    public void OnAttackHitboxOn()
    {
        AttackState activeAttack = GetActiveAttackState();
        if (activeAttack == null || m_isStaggered)
        {
            return;
        }

        // Attack은 판정을 담당하는 모듈이고, Combat.Attack은 공격 상태입니다. 이름이 같으니 주의합니다.
        Attack?.SetHitboxActive(true);
        activeAttack.NotifyAnimationImpact();
    }

    /// <summary>공격 클립의 애니메이션 이벤트에서 호출되어 판정 콜라이더를 끕니다.</summary>
    /// <remarks>
    /// 죽은 상태에서도 반드시 꺼야 합니다. 켜진 채로 남으면 다음 공격 전까지 스치는 것마다 피해가 들어갑니다.
    /// 클립이 중간에 끊겨 이 이벤트가 오지 않는 경우는 <see cref="AttackState"/>가 종료 시 다시 끕니다.
    /// </remarks>
    public void OnAttackHitboxOff()
    {
        Attack?.SetHitboxActive(false);
    }

    /// <summary>공격 판정 시점에 실제 피해를 적용합니다.</summary>
    /// <remarks>
    /// 애니메이션 이벤트에서도 호출되므로 사망 상태에서 들어올 수 있어 먼저 걸러냅니다.
    /// 한 번의 공격에서 판정은 한 번만 일어나야 하므로 소모 여부는 공격 상태가 관리합니다.
    /// </remarks>
    public void ApplyAttackDamage()
    {
        if (m_current == Dead)
        {
            return;
        }

        if (!m_isStaggered) GetActiveAttackState()?.NotifyAnimationImpact();
    }

    private AttackState GetActiveAttackState()
    {
        if (IsAttackingDefenseObjective) return m_defenseAttack;
        return m_current == Combat && Combat.CurrentSub == Combat.Attack ? Combat.Attack : null;
    }

    /// <summary>
    /// 이 개체가 쓸 대기 동작 혼합 비율을 한 번 뽑아 고정합니다.
    /// </summary>
    /// <remarks>
    /// 개체마다 대기 자세가 조금씩 달라 보이게 하려는 것이라 매번 새로 뽑지 않습니다.
    /// 계속 흔들리면 대기 중에 몸이 미세하게 떨리는 것처럼 보입니다.
    ///
    /// 범위는 밸런스 SO가 소유하는 상수이고, 뽑힌 값은 이 개체의 런타임 상태입니다.
    /// Awake가 아니라 Start에서 뽑는 것은 ApplyBalance가 Awake에서 범위를 채우기 때문입니다.
    /// </remarks>
    private void RollIdleType()
    {
        IdleType = Random.Range(idleTypeMin, idleTypeMax);
        animator?.SetFloat(AnimIdleType, IdleType);
    }

    /// <summary>Spawn SO 값이 있으면 그 값을, 없으면 Always Run 또는 상태별 Balance/Inspector 속도를 반환합니다.</summary>
    /// <remarks>
    /// 모든 이동 속도 속성이 이 함수를 거칩니다. 함정 감속 같은 외부 배율도 여기서 곱해야
    /// 상태가 바뀌어 <c>agent.speed</c>가 다시 대입돼도 효과가 유지됩니다.
    /// </remarks>
    private float ResolveMoveSpeed(float fallbackSpeed, bool runSpeed)
    {
        return ResolveRawMoveSpeed(fallbackSpeed, runSpeed) * m_moveSpeedMultiplier;
    }

    /// <summary>
    /// 감속 배율을 곱하기 전의 이동 속도입니다.
    /// </summary>
    /// <remarks>
    /// 애니메이터 블렌드 트리에 넣을 값을 구할 때 씁니다. 블렌드 임계값(Idle 0 / Walk 2 / Run 6)은
    /// 감속을 모르는 상태로 만들어져 있어서, 깎인 속도를 그대로 넣으면 달리던 적이 제자리걸음 구간으로
    /// 내려가 발만 헛돌게 됩니다. 어떤 동작인지는 감속 전 속도로 고르고, 빠르기는 재생 속도로 맞춥니다.
    /// </remarks>
    private float ResolveRawMoveSpeed(float fallbackSpeed, bool runSpeed)
    {
        bool forceWalk = IsForcedToWalk;

        if (m_hasSpawnConfiguration)
        {
            bool useRun = !forceWalk && (AlwaysRun || runSpeed);
            return useRun ? m_spawnRunSpeed : m_spawnWalkSpeed;
        }

        return forceWalk ? wanderSpeed : (AlwaysRun ? chaseSpeed : fallbackSpeed);
    }

    /// <summary>이번 생성의 방어전 경로 진행도를 처음으로 되돌리고 첫 목적지를 준비합니다.</summary>
    private void RestartDefenseNavigation()
    {
        m_defenseWaypointIndex = 0;
        m_defenseNavigationActive = IsDefenseEnemy
            && (m_defenseWaypoints.Count > 0
            || m_defenseTargetPosition != null
            || DefenseDisposition == EnemyDefenseDisposition.PlayerFirst);
        ResetDefenseSquadTargetSelection();
        ResetDefenseDestinationTracking();

        if (!m_defenseNavigationActive || agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        agent.speed = CurrentMoveSpeed;
        if (ShouldFollowDefenseWaypoints() && m_defenseWaypoints.Count > 0 && m_defenseWaypoints[0] != null)
        {
            Transform waypoint = m_defenseWaypoints[0];
            MoveToDefenseDestination(
                waypoint.position,
                DefenseDestinationKind.Waypoint,
                waypoint,
                false);
        }
        else if (TryResolveDefenseDestination(out Vector3 destination, out _, out Transform source))
        {
            bool movingTarget = source != m_defenseTargetPosition;
            MoveToDefenseDestination(
                destination,
                movingTarget ? DefenseDestinationKind.SquadMember : DefenseDestinationKind.Objective,
                source,
                movingTarget);
        }
    }

    /// <summary>웨이포인트를 순서대로 통과한 뒤 방어 성향에 맞는 목표 행동을 수행합니다.</summary>
    /// <returns>이번 프레임의 이동 판단을 방어전 경로가 처리했으면 true입니다.</returns>
    /// <remarks>
    /// Player First는 Spawn SO 설정에 따라 웨이포인트를 먼저 처리하거나 플레이어 추적을 바로 시작합니다.
    /// Target First는 피격/하울링에도 스쿼드 추격으로 전환하지 않습니다. Player First의 경로 우선 옵션도
    /// 남은 웨이포인트가 있는 동안 지키며, 그 뒤에만 기존 스쿼드 전투 HFSM에 제어권을 넘깁니다.
    /// </remarks>
    private bool TickDefenseNavigation()
    {
        if (!IsDefenseEnemy || !m_defenseNavigationActive || m_current == Dead || m_current == Combat || IsAttackingDefenseObjective)
        {
            return false;
        }

        if (agent == null || !agent.enabled)
        {
            return true;
        }

        if (!agent.isOnNavMesh && !TryRecoverDefenseAgentToNavMesh())
        {
            return true;
        }

        agent.speed = CurrentMoveSpeed;

        while (ShouldFollowDefenseWaypoints() && m_defenseWaypointIndex < m_defenseWaypoints.Count)
        {
            Transform waypoint = m_defenseWaypoints[m_defenseWaypointIndex];
            if (waypoint == null)
            {
                m_defenseWaypointIndex++;
                continue;
            }

            if (HasReachedDefensePosition(waypoint.position))
            {
                m_defenseWaypointIndex++;
                ResetDefenseDestinationTracking(true);
                continue;
            }

            MoveToDefenseDestination(
                waypoint.position,
                DefenseDestinationKind.Waypoint,
                waypoint,
                false);
            return true;
        }

        if (DefenseDisposition == EnemyDefenseDisposition.Default)
        {
            FinishDefenseNavigation();
            return false;
        }

        if (DefenseDisposition == EnemyDefenseDisposition.PlayerFirst && targetSensor != null)
        {
            targetSensor.UpdatePerception();
            targetSensor.ReevaluateTarget();
            // 알고만 있지만 갈 수 없는 대상은 CurrentTarget으로 뽑히지 않습니다. HasAnyValidTarget만 보면
            // Combat의 도달 불가 복구가 끝난 직후 다시 Combat에 들어가는 순환이 생깁니다.
            SquadMemberController sensedTarget = targetSensor.CurrentTarget;
            if (sensedTarget != null && CanPursueDefenseSquadTarget(sensedTarget))
            {
                TransitionTo(Combat);
                return true;
            }
        }

        if (!TryResolveDefenseDestination(
            out Vector3 targetPosition,
            out bool usesObjective,
            out Transform destinationSource))
        {
            StopMoving();
            return true;
        }

        // 좌표 도착과 공격 가능 조건은 다릅니다. 목표물은 표면으로 접근하고 기존 공격 타이밍을 재사용합니다.
        if (usesObjective)
        {
            EnsureDefenseAttackPoint();
        }

        if (usesObjective &&
            enemyAttack != null && enemyAttack.TryGetDefenseAttackPoint(m_defenseObjective, out Vector3 attackPoint))
        {
            if (!m_hasDefenseApproachSettings)
            {
                m_defenseApproachStoppingDistance = agent.stoppingDistance;
                m_defenseApproachAvoidancePriority = agent.avoidancePriority;
                m_hasDefenseApproachSettings = true;
            }

            Vector3 direction = attackPoint - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(direction), RotationSpeed * Time.deltaTime);

            // 앞자리가 없으면 포인트 뒤쪽 대기 줄에 서서 기다립니다. 앞자리 적과 같은 점으로 비집고 들어가면
            // 서로 밀며 떨리기 때문입니다. 앞자리가 비면 포인트가 가장 가까운 대기 적을 올려 줍니다.
            if (m_defenseAttackPoint != null && !m_defenseAttackPoint.IsHolder(this))
            {
                TickDefenseAttackPointWaiting();
                return true;
            }

            EndDefenseWaitFlourish();

            float approachStoppingDistance = m_defenseAttackPoint != null
                ? m_defenseAttackPoint.StandDistance
                : Mathf.Max(0.0f, enemyAttack.AttackStartRange * 0.5f);
            // 공격 포인트의 서는 거리는 피격 영역에 맞춘 값이라 그대로 씁니다. 포인트가 없으면 원래 값보다 늘리지 않습니다.
            agent.stoppingDistance = m_defenseAttackPoint != null
                ? approachStoppingDistance
                : Mathf.Min(m_defenseApproachStoppingDistance, approachStoppingDistance);
            SetDefenseAvoidancePriority(DefenseApproachAvoidancePriority);
            if (enemyAttack.CanStartDefenseAttack(m_defenseObjective))
            {
                // 때리는 동안 뒤에서 오는 적에게 밀려 자리를 잃지 않게 회피 우선순위를 가장 높입니다.
                SetDefenseAvoidancePriority(DefenseAttackAvoidancePriority);
                TransitionTo(m_defenseAttack);
                return true;
            }
            MoveToDefenseDestination(
                attackPoint,
                DefenseDestinationKind.AttackPoint,
                m_defenseAttackPoint != null ? m_defenseAttackPoint : m_defenseObjective,
                false);
            UpdateDefenseAttackPointBlocked();
            return true;
        }

        RestoreDefenseApproachSettings();

        if (HasReachedDefensePosition(targetPosition))
        {
            StopMoving();
            return true;
        }

        MoveToDefenseDestination(
            targetPosition,
            usesObjective ? DefenseDestinationKind.Objective : DefenseDestinationKind.SquadMember,
            destinationSource,
            !usesObjective);
        return true;
    }

    private bool ShouldDeferSquadCombat()
    {
        return IsIgnoringSquad || (IsDefenseEnemy &&
            m_defenseNavigationActive && DefenseDisposition == EnemyDefenseDisposition.PlayerFirst &&
             m_prioritizeDefenseWaypointsForPlayerFirst && m_defenseWaypointIndex < m_defenseWaypoints.Count);
    }

    /// <summary>
    /// 혼합형이 이번 피격으로 공격자에게 전환할지 판단하고, 전환하면 어그로를 켭니다.
    /// </summary>
    /// <param name="attacker">피해를 입힌 오브젝트입니다.</param>
    /// <remarks>
    /// 스쿼드원(조작 캐릭터와 AI 동료)이 쐈을 때만 전환합니다. 지정사수와 함정은 스쿼드원이 아니라 전환하지 않습니다.
    /// 이 적이 <see cref="DefenseTargetZone"/> 안에 있고 쏜 캐릭터가 그 구역 밖이면 전환하지 않습니다. 방어 대상이 훨씬
    /// 가까운데 멀리서 쏜 캐릭터를 쫓아가는 것을 막기 위해서입니다. 구역 판정은 전환하는 순간에만 합니다. 이미 쫓고 있는
    /// 적이 구역에 들어와도 어그로를 풀지 않습니다.
    /// 전환하면 남은 웨이포인트를 건너뛴 것으로 표시합니다. 교전이 끝나면 곧장 방어 대상으로 가게 하기 위해서입니다.
    /// </remarks>
    private void TryBeginHybridAggro(GameObject attacker)
    {
        if (!IsDefenseEnemy || DefenseDisposition != EnemyDefenseDisposition.TargetUntilAttacked || m_hybridAggroActive)
        {
            return;
        }

        SquadMemberController member = attacker != null ? attacker.GetComponentInParent<SquadMemberController>() : null;
        if (member == null)
        {
            return;
        }

        if (DefenseTargetZone.ShouldIgnoreAttacker(transform.position, member.transform.position))
        {
            return;
        }

        m_hybridAggroActive = true;
        m_defenseWaypointIndex = m_defenseWaypoints.Count;
        ResetDefenseDestinationTracking();
    }

    /// <summary>방어 목표에 공격 포인트가 있으면, 아직 고르지 않았을 때 하나 고릅니다.</summary>
    /// <remarks>목표가 바뀌었거나 고른 포인트가 꺼졌으면 다시 고릅니다. 포인트가 없는 목표면 고른 것을 놓습니다.</remarks>
    private void EnsureDefenseAttackPoint()
    {
        if (m_defenseObjective == null || !m_defenseObjective.HasAttackPoints)
        {
            ReleaseDefenseAttackPoint();
            return;
        }

        if (m_defenseAttackPoint != null && m_defenseAttackPoint.isActiveAndEnabled &&
            m_defenseAttackPoint.Owner == m_defenseObjective)
        {
            return;
        }

        AssignDefenseAttackPoint(null);
    }

    /// <summary>지금 포인트를 놓고 새 공격 포인트를 고릅니다.</summary>
    /// <param name="avoid">가능하면 고르지 않을 포인트입니다. 막혀서 다시 고를 때 지금 포인트를 넘깁니다.</param>
    private void AssignDefenseAttackPoint(DefenseAttackPoint avoid)
    {
        ReleaseDefenseAttackPoint();
        m_defenseAttackPoint = m_defenseObjective != null
            ? m_defenseObjective.PickAttackPoint(transform.position, avoid)
            : null;
        m_defenseAttackPoint?.AddOccupant(this);
        m_defenseWaitLateralSeed = Random.Range(-1.0f, 1.0f);
        m_defenseWaitRecheckTime = DefenseWaitRecheckInterval;
        m_defenseWaitFlourishTimer = Random.Range(0.5f, DefenseWaitFlourishMaxInterval);
    }

    /// <summary>고른 공격 포인트를 놓습니다. 교전으로 빠지거나 죽거나 풀로 돌아갈 때 부릅니다.</summary>
    private void ReleaseDefenseAttackPoint()
    {
        EndDefenseWaitFlourish();
        if (m_defenseDestinationKind == DefenseDestinationKind.AttackPoint
            || m_defenseDestinationKind == DefenseDestinationKind.WaitPoint)
        {
            ResetDefenseDestinationTracking(true);
        }

        if (m_defenseAttackPoint != null)
        {
            m_defenseAttackPoint.RemoveOccupant(this);
        }

        m_defenseAttackPoint = null;
        m_defenseAttackPointBlockedTime = 0.0f;
    }

    /// <summary>
    /// 공격 포인트로 가는 길이 막혀 서 있으면 시간을 재고, 오래 막히면 다른 포인트를 고릅니다.
    /// </summary>
    /// <remarks>
    /// 막힘은 "거의 멈춰 있는데 목적지까지 남은 거리가 있는" 상태로 봅니다. 대개 앞에서 같은 포인트를 때리는 적에게
    /// 막힌 경우입니다. 그대로 두면 뒤에 선 적이 끝까지 공격하지 못하므로 옆 포인트로 흩어 보냅니다.
    /// </remarks>
    private void UpdateDefenseAttackPointBlocked()
    {
        if (m_defenseAttackPoint == null || agent == null || agent.pathPending)
        {
            return;
        }

        bool blocked = agent.velocity.sqrMagnitude < 0.04f &&
            agent.remainingDistance > agent.stoppingDistance + 0.5f;
        m_defenseAttackPointBlockedTime = blocked ? m_defenseAttackPointBlockedTime + Time.deltaTime : 0.0f;

        if (m_defenseAttackPointBlockedTime >= DefenseAttackPointRepickDelay)
        {
            AssignDefenseAttackPoint(m_defenseAttackPoint);
        }
    }

    private void RestoreDefenseApproachSettings()
    {
        if (!m_hasDefenseApproachSettings) return;
        if (agent != null)
        {
            agent.stoppingDistance = m_defenseApproachStoppingDistance;
            agent.avoidancePriority = m_defenseApproachAvoidancePriority;
        }
        m_hasDefenseApproachSettings = false;
    }

    /// <summary>
    /// 공격 포인트의 대기 줄에서 기다립니다. 대기 위치까지 가서 멈추고, 다른 포인트에 앞자리가 나면 옮겨 갑니다.
    /// </summary>
    /// <remarks>
    /// 대기 위치에 도착하면 이동을 멈춥니다. 목적지를 계속 다시 잡으면 앞 적과 맞닿은 채로 밀며 떨리기 때문입니다.
    /// 회피 우선순위는 가장 낮게 두어, 앞자리로 들어가는 적이 지나갈 때 비켜서게 합니다.
    /// </remarks>
    private void TickDefenseAttackPointWaiting()
    {
        agent.stoppingDistance = DefenseWaitStoppingDistance;
        SetDefenseAvoidancePriority(DefenseWaitAvoidancePriority);
        m_defenseAttackPointBlockedTime = 0.0f;

        m_defenseWaitRecheckTime -= Time.deltaTime;
        if (m_defenseWaitRecheckTime <= 0.0f)
        {
            m_defenseWaitRecheckTime = DefenseWaitRecheckInterval;
            if (m_defenseObjective != null && m_defenseObjective.HasFreeAttackPoint(m_defenseAttackPoint))
            {
                EndDefenseWaitFlourish();
                AssignDefenseAttackPoint(m_defenseAttackPoint);
                return;
            }
        }

        Vector3 waitPosition = m_defenseAttackPoint.GetWaitPosition(this, m_defenseWaitLateralSeed);
        // 앞 대기 적에 막혀 거의 다 와서 멈췄으면 거기서 기다립니다. 끝까지 밀고 들어가면 다시 떨리기 때문입니다.
        // 경로가 아직 없으면 remainingDistance가 0이라 "거의 다 왔다"로 잘못 읽힙니다. 지금 대기 위치로 가는 경로가
        // 있을 때만 막힘으로 봅니다. 줄이 당겨져 대기 위치가 바뀌었으면 옛 경로로는 판정하지 않고 다시 걸어갑니다.
        Vector3 destinationDelta = agent.destination - waitPosition;
        destinationDelta.y = 0.0f;
        bool headingToWaitPosition = agent.hasPath && !agent.pathPending &&
            destinationDelta.sqrMagnitude < DefenseDestinationRefreshDistance * DefenseDestinationRefreshDistance;
        bool blockedNearby = headingToWaitPosition && agent.velocity.sqrMagnitude < 0.04f &&
            agent.remainingDistance < DefenseWaitSettleDistance;
        Vector3 waitDelta = waitPosition - transform.position;
        waitDelta.y = 0.0f;
        bool closeEnough = waitDelta.sqrMagnitude <= DefenseWaitArriveDistance * DefenseWaitArriveDistance;
        if (closeEnough || blockedNearby)
        {
            StopMoving();
            UpdateDefenseWaitFlourish();
            return;
        }

        EndDefenseWaitFlourish();
        MoveToDefenseDestination(
            waitPosition,
            DefenseDestinationKind.WaitPoint,
            m_defenseAttackPoint,
            true);
    }

    /// <summary>
    /// 방어전 목적지의 종류·원본·좌표 변화가 있을 때만 새 경로를 요청하고, 기존 경로의 실패와 정체를 감시합니다.
    /// </summary>
    /// <param name="destination">현재 판단한 원래 목적지입니다.</param>
    /// <param name="kind">웨이포인트, 플레이어, 공격 포인트 등 목적지 역할입니다.</param>
    /// <param name="source">같은 역할 안에서 실제 대상이 교체됐는지 구분할 원본입니다.</param>
    /// <param name="movingTarget">계속 움직이는 대상이면 좌표 변화에 갱신 간격을 적용합니다.</param>
    private void MoveToDefenseDestination(
        Vector3 destination,
        DefenseDestinationKind kind,
        UnityEngine.Object source,
        bool movingTarget)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        bool identityChanged = !m_hasDefenseDestination
            || m_defenseDestinationKind != kind
            || m_defenseDestinationSource != source;
        Vector3 requestedDelta = destination - m_defenseRequestedDestination;
        requestedDelta.y = 0.0f;
        bool positionChanged = !m_hasDefenseDestination
            || requestedDelta.sqrMagnitude >= DefenseDestinationRefreshDistance * DefenseDestinationRefreshDistance;
        bool refreshWindowOpen = !movingTarget || Time.time >= m_defenseNextMovingDestinationRefreshTime;
        bool retryWindowOpen = Time.time >= m_defenseNextPathRecoveryTime;
        bool shouldIssue = identityChanged
            || (positionChanged && refreshWindowOpen)
            || (retryWindowOpen && agent.isStopped);

        if (shouldIssue)
        {
            if (identityChanged || positionChanged)
            {
                m_defensePathRecoveryAttempts = 0;
            }

            m_hasDefenseDestination = true;
            m_defenseDestinationKind = kind;
            m_defenseDestinationSource = source;
            m_defenseRequestedDestination = destination;
            m_defenseNextMovingDestinationRefreshTime = Time.time + DefenseMovingDestinationRefreshInterval;

            if (!IssueDefenseDestination(destination, identityChanged))
            {
                m_defenseNextPathRecoveryTime = Time.time + DefensePathRetryInterval;
            }
        }

        TickDefensePathRecovery();
    }

    /// <summary>실제 NavMeshAgent에 목적지를 한 번 전달하고 정체 관찰 기준을 갱신합니다.</summary>
    private bool IssueDefenseDestination(Vector3 destination, bool resetProgressClock)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return false;
        }

        agent.isStopped = false;
        bool accepted = agent.SetDestination(destination);
        m_defensePathIssuedTime = Time.time;
        if (resetProgressClock || m_defenseLastProgressTime <= 0.0f)
        {
            m_defenseLastProgressPosition = transform.position;
            m_defenseLastProgressTime = Time.time;
        }
        return accepted;
    }

    /// <summary>
    /// 방어전 목적지의 경로 계산 실패·부분 경로 종단·무진행을 감시하고 도달 가능한 주변 지점으로 복구합니다.
    /// </summary>
    private void TickDefensePathRecovery()
    {
        if (!m_hasDefenseDestination || agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        Vector3 progress = transform.position - m_defenseLastProgressPosition;
        progress.y = 0.0f;
        if (progress.sqrMagnitude >= DefensePathProgressDistance * DefensePathProgressDistance)
        {
            m_defenseLastProgressPosition = transform.position;
            m_defenseLastProgressTime = Time.time;
            m_defensePathRecoveryAttempts = 0;
        }

        bool pendingTimedOut = agent.pathPending
            && Time.time - m_defensePathIssuedTime >= DefensePathPendingTimeout;
        bool requestSettled = Time.time - m_defensePathIssuedTime >= 0.1f;
        bool missingOrInvalid = requestSettled && !agent.pathPending
            && (!agent.hasPath || agent.pathStatus == NavMeshPathStatus.PathInvalid);
        bool partialAtEnd = !agent.pathPending && agent.hasPath
            && agent.pathStatus == NavMeshPathStatus.PathPartial
            && (agent.velocity.sqrMagnitude < 0.04f
                || agent.remainingDistance <= agent.stoppingDistance + 0.2f);
        bool stalled = !agent.pathPending
            && !HasReachedDefensePosition(m_defenseRequestedDestination)
            && Time.time - m_defenseLastProgressTime >= DefensePathStallTimeout;

        if ((!pendingTimedOut && !missingOrInvalid && !partialAtEnd && !stalled)
            || Time.time < m_defenseNextPathRecoveryTime)
        {
            return;
        }

        m_defenseNextPathRecoveryTime = Time.time + DefensePathRetryInterval;
        m_defensePathRecoveryAttempts++;

        if (m_defensePathRecoveryAttempts <= DefensePathRecoveryAttemptLimit
            && TryFindReachableDefenseDestination(m_defenseRequestedDestination, out Vector3 recoveredDestination))
        {
            IssueDefenseDestination(recoveredDestination, true);
            return;
        }

        if (m_defensePathRecoveryAttempts < DefensePathRecoveryAttemptLimit)
        {
            IssueDefenseDestination(m_defenseRequestedDestination, true);
            return;
        }

        HandleUnreachableDefenseDestination();
    }

    /// <summary>원래 목적지 주변에서 현재 위치로 완전 경로가 이어지는 NavMesh 좌표를 찾습니다.</summary>
    private bool TryFindReachableDefenseDestination(Vector3 destination, out Vector3 reachableDestination)
    {
        reachableDestination = destination;
        if (agent == null || !agent.enabled || !agent.isOnNavMesh
            || !NavMesh.SamplePosition(
                destination,
                out NavMeshHit hit,
                DefensePathRecoverySampleRadius,
                agent.areaMask))
        {
            return false;
        }

        NavMeshPath recoveryPath = new NavMeshPath();
        if (!NavMesh.CalculatePath(transform.position, hit.position, agent.areaMask, recoveryPath)
            || recoveryPath.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        reachableDestination = hit.position;
        return true;
    }

    /// <summary>복구로도 도달하지 못한 목적지를 역할에 맞게 건너뛰거나 재선정합니다.</summary>
    private void HandleUnreachableDefenseDestination()
    {
        switch (m_defenseDestinationKind)
        {
            case DefenseDestinationKind.Waypoint:
                m_defenseWaypointIndex++;
                ResetDefenseDestinationTracking(true);
                return;

            case DefenseDestinationKind.AttackPoint:
            case DefenseDestinationKind.WaitPoint:
                DefenseAttackPoint failedPoint = m_defenseAttackPoint;
                AssignDefenseAttackPoint(failedPoint);
                return;

            case DefenseDestinationKind.SquadMember:
                RejectUnreachableDefenseSquadTarget();
                return;

            default:
                // 최종 방어 목표는 건너뛸 수 없습니다. 경로를 비우고 간격을 둬 재평가하되,
                // 매 프레임 같은 실패 요청을 반복하지 않습니다.
                if (agent != null && agent.enabled && agent.isOnNavMesh)
                {
                    agent.ResetPath();
                    agent.isStopped = true;
                }

                m_defensePathRecoveryAttempts = 0;
                m_defenseLastProgressPosition = transform.position;
                m_defenseLastProgressTime = Time.time;
                m_defenseNextPathRecoveryTime = Time.time + DefensePathRetryInterval;
                return;
        }
    }

    /// <summary>스폰·넉백 등으로 NavMesh 밖에 놓인 방어전 적을 가까운 NavMesh 위로 복구합니다.</summary>
    private bool TryRecoverDefenseAgentToNavMesh()
    {
        if (agent == null || !agent.enabled || Time.time < m_defenseNextPathRecoveryTime)
        {
            return false;
        }

        m_defenseNextPathRecoveryTime = Time.time + DefensePathRetryInterval;
        if (!NavMesh.SamplePosition(
            transform.position,
            out NavMeshHit recovery,
            DefensePathRecoverySampleRadius,
            agent.areaMask)
            || !agent.Warp(recovery.position)
            || !agent.isOnNavMesh)
        {
            return false;
        }

        ResetDefenseDestinationTracking();
        return true;
    }

    /// <summary>방어전 목적지와 경로 복구 관찰 상태를 초기화합니다.</summary>
    private void ResetDefenseDestinationTracking(bool resetAgentPath = false)
    {
        m_defenseDestinationKind = DefenseDestinationKind.None;
        m_defenseDestinationSource = null;
        m_defenseRequestedDestination = Vector3.zero;
        m_defenseLastProgressPosition = transform.position;
        m_defenseLastProgressTime = Time.time;
        m_defensePathIssuedTime = 0.0f;
        m_defenseNextMovingDestinationRefreshTime = 0.0f;
        m_defenseNextPathRecoveryTime = 0.0f;
        m_defensePathRecoveryAttempts = 0;
        m_hasDefenseDestination = false;

        if (resetAgentPath && agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.ResetPath();
        }
    }

    /// <summary>
    /// 대기 줄에 서 있는 동안 가끔 허공에 공격 동작을 합니다.
    /// </summary>
    /// <remarks>
    /// 줄에 선 적이 가만히 서 있기만 하면 순서를 기다리는 것처럼 보여서, 애니메이션만 한 번씩 재생합니다.
    /// 공격 상태로 들어가지 않으므로 클립의 판정 켜기 이벤트(<see cref="OnAttackHitboxOn"/>)가 와도 판정은 켜지지 않습니다.
    /// 간격은 개체마다 무작위라 줄 전체가 같이 휘두르지 않습니다.
    /// </remarks>
    private void UpdateDefenseWaitFlourish()
    {
        if (m_defenseWaitFlourishEndTime > 0.0f)
        {
            if (Time.time >= m_defenseWaitFlourishEndTime)
            {
                EndDefenseWaitFlourish();
            }

            return;
        }

        m_defenseWaitFlourishTimer -= Time.deltaTime;
        if (m_defenseWaitFlourishTimer > 0.0f || m_isStaggered)
        {
            return;
        }

        PlayAttackAnimation(1);
        m_defenseWaitFlourishEndTime = Time.time + DefenseWaitFlourishDuration;
        m_defenseWaitFlourishTimer = Random.Range(DefenseWaitFlourishMinInterval, DefenseWaitFlourishMaxInterval);
    }

    /// <summary>하고 있던 허공 휘두르기를 멈춥니다. 걷기 시작하거나 앞자리로 가거나 상태가 바뀔 때 부릅니다.</summary>
    private void EndDefenseWaitFlourish()
    {
        if (m_defenseWaitFlourishEndTime <= 0.0f)
        {
            return;
        }

        m_defenseWaitFlourishEndTime = 0.0f;
        EndAttackAnimation();
    }

    private void SetDefenseAvoidancePriority(int priority)
    {
        if (agent != null && agent.avoidancePriority != priority)
        {
            agent.avoidancePriority = priority;
        }
    }

    /// <summary>현재 Defense 성향에서 스폰 웨이포인트를 처리해야 하는지 반환합니다.</summary>
    /// <remarks>
    /// 웨이포인트 우선 옵션은 Player First에만 적용합니다. Target First는 플레이어 반응 여부와 무관하게
    /// 스폰 포인트가 지정한 경로와 목표를 따라야 하므로 항상 true입니다.
    /// </remarks>
    private bool ShouldFollowDefenseWaypoints()
    {
        return DefenseDisposition != EnemyDefenseDisposition.PlayerFirst
            || m_prioritizeDefenseWaypointsForPlayerFirst;
    }

    /// <summary>방어 성향에 따라 웨이포인트 이후의 실시간 이동 목적지를 구합니다.</summary>
    /// <param name="destination">이동할 월드 좌표입니다.</param>
    /// <returns>현재 사용할 수 있는 목적지가 있으면 true입니다.</returns>
    /// <remarks>
    /// <see cref="EnemyDefenseDisposition.PlayerFirst"/>는 현재 조작 캐릭터를 먼저 추적합니다. 현재 대상까지 경로가
    /// 계속 실패하면 도달 가능한 다른 생존 스쿼드원을 고르고, 그마저 없으면 방어 목표를 대체 목적지로 사용합니다.
    /// 대상 비교는 매 프레임 하지 않고 재평가 주기와 전환 거리 차이, 실패 대상 쿨다운으로 안정화합니다.
    /// </remarks>
    private bool TryResolveDefenseDestination(
        out Vector3 destination,
        out bool usesObjective,
        out Transform source)
    {
        usesObjective = false;
        source = null;
        if (DefenseDisposition == EnemyDefenseDisposition.PlayerFirst
            && TryResolvePlayerFirstSquadTarget(out SquadMemberController squadTarget))
        {
            destination = squadTarget.transform.position;
            source = squadTarget.transform;
            return true;
        }

        if (m_defenseTargetPosition != null)
        {
            usesObjective = true;
            source = m_defenseTargetPosition;
            destination = m_defenseTargetPosition.position;
            return true;
        }

        destination = Vector3.zero;
        return false;
    }

    /// <summary>
    /// Player First의 현재 스쿼드 추적 대상을 유지하거나, 주기가 되었을 때 도달 가능한 후보로 바꿉니다.
    /// </summary>
    private bool TryResolvePlayerFirstSquadTarget(out SquadMemberController target)
    {
        target = null;
        SquadManager squadManager = SquadManager.Instance;
        if (squadManager == null)
        {
            m_defenseSquadPursuitTarget = null;
            return false;
        }

        if (!m_hasDefenseSquadTargetSelection)
        {
            // 최초에는 Player First의 이름 그대로 현재 조작 캐릭터를 먼저 시도합니다. 순간적인 경로 계산 실패로
            // 바로 포기하지 않고, 방어 경로 복구가 실제 실패를 확정했을 때 다른 동료로 넘어갑니다.
            m_hasDefenseSquadTargetSelection = true;
            SquadMemberController player = squadManager.PlayerSquadMember;
            if (IsAvailableDefenseSquadTarget(player)
                && !IsDefenseSquadTargetRetryBlocked(player)
                && CanPursueDefenseSquadTarget(player))
            {
                SetDefenseSquadPursuitTarget(player, false);
            }
        }

        if (!IsAvailableDefenseSquadTarget(m_defenseSquadPursuitTarget)
            || !CanPursueDefenseSquadTarget(m_defenseSquadPursuitTarget))
        {
            m_defenseSquadPursuitTarget = null;
            m_defenseNextSquadTargetEvaluationTime = 0.0f;
        }

        if (Time.time >= m_defenseNextSquadTargetEvaluationTime)
        {
            ReevaluateDefenseSquadPursuitTarget(squadManager);
        }

        target = m_defenseSquadPursuitTarget;
        return target != null;
    }

    /// <summary>
    /// 현재 대상을 함부로 뒤집지 않으면서, 충분히 더 가까운 도달 가능 스쿼드원만 새 대상으로 선택합니다.
    /// </summary>
    private void ReevaluateDefenseSquadPursuitTarget(SquadManager squadManager)
    {
        m_defenseNextSquadTargetEvaluationTime = Time.time + DefenseSquadTargetReevaluationInterval;
        IReadOnlyList<SquadMemberController> members = squadManager != null ? squadManager.SquadMembers : null;
        if (members == null || members.Count == 0 || agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        SquadMemberController current = m_defenseSquadPursuitTarget;
        bool currentUsable = IsAvailableDefenseSquadTarget(current)
            && !IsDefenseSquadTargetRetryBlocked(current)
            && CanPursueDefenseSquadTarget(current);
        float currentDistance = float.PositiveInfinity;
        bool currentReachable = currentUsable
            && TryGetCompleteDefensePathDistance(current.transform.position, out currentDistance);

        if (currentUsable && !currentReachable)
        {
            // 한 번의 경로 계산 실패만으로 대상을 뒤집지 않습니다. 실제 이동의 Partial/정체 복구가 실패를
            // 확정하면 RejectUnreachableDefenseSquadTarget이 호출되어 즉시 대체 대상을 고릅니다.
            return;
        }

        SquadMemberController player = squadManager.PlayerSquadMember;
        SquadMemberController best = null;
        float bestDistance = float.PositiveInfinity;
        float playerDistance = float.PositiveInfinity;

        for (int i = 0; i < members.Count; i++)
        {
            SquadMemberController candidate = members[i];
            if (!IsAvailableDefenseSquadTarget(candidate)
                || IsDefenseSquadTargetRetryBlocked(candidate)
                || !CanPursueDefenseSquadTarget(candidate)
                || !TryGetCompleteDefensePathDistance(candidate.transform.position, out float candidateDistance))
            {
                continue;
            }

            if (candidate == player)
            {
                playerDistance = candidateDistance;
            }

            if (candidateDistance < bestDistance)
            {
                best = candidate;
                bestDistance = candidateDistance;
            }
        }

        if (!currentReachable)
        {
            // 방어 목표를 치고 있거나 이전 스쿼드원이 무효가 된 경우에는, 도달 가능한 플레이어를 우선하고
            // 플레이어가 불가능할 때 가장 가까운 동료를 고릅니다.
            SquadMemberController replacement = !float.IsPositiveInfinity(playerDistance) ? player : best;
            SetDefenseSquadPursuitTarget(replacement, replacement != null);
            return;
        }

        if (best != null && best != current
            && currentDistance - bestDistance >= DefenseSquadTargetSwitchPathDistanceDelta)
        {
            SetDefenseSquadPursuitTarget(best, true);
        }
    }

    /// <summary>스쿼드 추적 대상을 바꾸고, 실제 변경에는 재전환 쿨다운을 적용합니다.</summary>
    private void SetDefenseSquadPursuitTarget(SquadMemberController target, bool applySwitchCooldown)
    {
        if (m_defenseSquadPursuitTarget == target)
        {
            return;
        }

        m_defenseSquadPursuitTarget = target;
        if (target != null)
        {
            m_defenseSquadTargetRetryTimes.Remove(target);
        }

        m_defenseNextSquadTargetEvaluationTime = Time.time + (applySwitchCooldown
            ? DefenseSquadTargetSwitchCooldown
            : DefenseSquadTargetReevaluationInterval);
    }

    /// <summary>방어 경로 복구로도 닿지 못한 스쿼드원을 잠시 후보에서 제외하고 즉시 대체 대상을 찾게 합니다.</summary>
    private void RejectUnreachableDefenseSquadTarget()
    {
        SquadMemberController failedTarget = m_defenseSquadPursuitTarget;
        if (failedTarget == null && m_defenseDestinationSource is Transform sourceTransform)
        {
            failedTarget = sourceTransform.GetComponentInParent<SquadMemberController>();
        }

        if (failedTarget != null)
        {
            m_defenseSquadTargetRetryTimes[failedTarget] = Time.time + DefenseSquadTargetRetryCooldown;
        }

        m_defenseSquadPursuitTarget = null;
        m_defenseNextSquadTargetEvaluationTime = 0.0f;
        ResetDefenseDestinationTracking(true);
    }

    /// <summary>현재 대상까지 완전한 NavMesh 경로 길이를 계산합니다. 후보 재평가 주기에서만 호출합니다.</summary>
    private bool TryGetCompleteDefensePathDistance(Vector3 destination, out float distance)
    {
        distance = float.PositiveInfinity;
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return false;
        }

        if (m_defenseSquadTargetPath == null)
        {
            m_defenseSquadTargetPath = new NavMeshPath();
        }

        if (!NavMesh.CalculatePath(transform.position, destination, agent.areaMask, m_defenseSquadTargetPath)
            || m_defenseSquadTargetPath.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        Vector3[] corners = m_defenseSquadTargetPath.corners;
        distance = 0.0f;
        for (int i = 1; i < corners.Length; i++)
        {
            distance += Vector3.Distance(corners[i - 1], corners[i]);
        }

        return true;
    }

    /// <summary>죽거나 다운되지 않아 방어전 추적 후보로 쓸 수 있는 스쿼드원인지 확인합니다.</summary>
    private static bool IsAvailableDefenseSquadTarget(SquadMemberController target)
    {
        return target != null && target.IsAlive && !target.IsDown;
    }

    /// <summary>최근 경로 실패로 이 스쿼드원을 다시 고르면 안 되는지 확인합니다.</summary>
    private bool IsDefenseSquadTargetRetryBlocked(SquadMemberController target)
    {
        if (target == null || !m_defenseSquadTargetRetryTimes.TryGetValue(target, out float retryTime))
        {
            return false;
        }

        if (Time.time < retryTime)
        {
            return true;
        }

        m_defenseSquadTargetRetryTimes.Remove(target);
        return false;
    }

    /// <summary>
    /// 방어 목표 구역 안의 적이 구역 밖 스쿼드원에게 끌려가면 안 되는지 확인합니다.
    /// 정리 구간의 강제 플레이어 추적은 전투 종료 안전장치이므로 이 제한보다 우선합니다.
    /// </summary>
    public bool CanPursueDefenseSquadTarget(SquadMemberController target)
    {
        if (!IsAvailableDefenseSquadTarget(target))
        {
            return false;
        }

        return !IsDefenseEnemy
            || m_forcedPlayerPursuit
            || !DefenseTargetZone.ShouldIgnoreAttacker(transform.position, target.transform.position);
    }

    /// <summary>
    /// 방어 목표를 공격 중인 Player First가 시야 밖의 도달 가능한 스쿼드원을 다시 추적하도록 목표 공격에서 빠져나옵니다.
    /// </summary>
    /// <remarks>
    /// 방어 목표를 바라보는 동안에는 뒤쪽 스쿼드원이 센서 시야에 잡히지 않을 수 있습니다. 이 경우에도 기존
    /// Player First 선택 규칙(플레이어 우선, 도달 불가 시 동료, 모두 불가 시 방어 목표)을 그대로 재사용합니다.
    /// 호출 주기는 <see cref="AttackState"/>가 제한하므로 여기서는 별도 프레임 타이머를 두지 않습니다.
    /// </remarks>
    /// <returns>스쿼드원 추적을 재개하도록 목표 공격 상태를 벗어났으면 true입니다.</returns>
    public bool TryResumePlayerFirstSquadPursuitFromDefenseObjective()
    {
        if (!IsAttackingDefenseObjective
            || DefenseDisposition != EnemyDefenseDisposition.PlayerFirst
            || !TryResolvePlayerFirstSquadTarget(out SquadMemberController target)
            || !CanPursueDefenseSquadTarget(target))
        {
            return false;
        }

        TransitionTo(Wander);
        return true;
    }

    /// <summary>이번 생성의 Player First 대상 선택 기록과 실패 쿨다운을 초기화합니다.</summary>
    private void ResetDefenseSquadTargetSelection()
    {
        m_defenseSquadPursuitTarget = null;
        m_hasDefenseSquadTargetSelection = false;
        m_defenseNextSquadTargetEvaluationTime = 0.0f;
        m_defenseSquadTargetRetryTimes.Clear();
    }

    /// <summary>현재 위치가 방어전 경로 목적지에 충분히 도착했는지 X/Z 평면에서 확인합니다.</summary>
    private bool HasReachedDefensePosition(Vector3 position)
    {
        float arrivalDistance = agent != null
            ? Mathf.Max(0.35f, agent.stoppingDistance)
            : 0.35f;
        Vector3 delta = position - transform.position;
        delta.y = 0.0f;
        return delta.sqrMagnitude <= arrivalDistance * arrivalDistance;
    }

    /// <summary>사용할 방어 목표가 없을 때 경로 우선권을 해제하고 현재 위치를 기준으로 배회를 다시 시작합니다.</summary>
    private void FinishDefenseNavigation()
    {
        m_defenseNavigationActive = false;
        ResetDefenseDestinationTracking(true);

        if (m_current == Wander)
        {
            m_current.Exit();
            m_current.Enter();
            return;
        }

        TransitionTo(Wander);
    }

    /// <summary>지정한 월드 좌표로 NavMesh 이동을 지시합니다.</summary>
    /// <param name="worldPosition">이동 목적지 월드 좌표입니다.</param>
    /// <remarks>이동 속도는 호출 상태가 <c>Agent.speed</c>로 미리 설정합니다(배회/추격 등).</remarks>
    public void MoveTo(Vector3 worldPosition)
    {
        if (agent == null || !agent.isOnNavMesh)
        {
            return;
        }

        agent.isStopped = false;
        agent.SetDestination(worldPosition);
    }

    /// <summary>
    /// 켜면 이 개체가 제자리에 서서 움직이지도 공격하지도 않습니다. 맞으면 경직과 넉백은 그대로 받습니다.
    /// 튜토리얼에서 적을 표적처럼 세워 둘 때 씁니다.
    /// </summary>
    public bool HoldStill
    {
        get => m_holdStill;
        set => m_holdStill = value;
    }

    private bool m_holdStill;

    /// <summary>이동을 멈춥니다.</summary>
    public void StopMoving()
    {
        if (agent == null || !agent.isOnNavMesh)
        {
            return;
        }

        agent.isStopped = true;
    }

    /// <summary>Enemy 동작에 필요한 컴포넌트를 찾고 없으면 보조 모듈을 추가합니다.</summary>
    private void CacheReferences()
    {
        if (agent == null)
        {
            agent = GetComponent<NavMeshAgent>();
        }

        // 감속 배율을 걸 때 되돌릴 기준값입니다. 한 번만 잡습니다.
        // 배율이 걸린 상태에서 다시 잡으면 줄어든 값이 기준이 되어 점점 느려집니다.
        if (agent != null && !m_hasBaseAgentMotion)
        {
            m_baseAcceleration = agent.acceleration;
            m_baseAngularSpeed = agent.angularSpeed;
            m_hasBaseAgentMotion = true;
        }

        if (animator == null)
        {
            animator = ResolveAnimator();
        }

        if (enemyHealth == null)
        {
            enemyHealth = GetComponent<EnemyHealth>();
        }

        if (targetSensor == null && !TryGetComponent(out targetSensor))
        {
            targetSensor = gameObject.AddComponent<EnemyTargetSensor>();
        }

        if (enemyAttack == null && !TryGetComponent(out enemyAttack))
        {
            enemyAttack = gameObject.AddComponent<EnemyAttack>();
        }

        if (ragdollController == null)
        {
            ragdollController = GetComponent<RagdollController>();
        }

        if (m_footstepEmitter == null && !TryGetComponent(out m_footstepEmitter))
        {
            m_footstepEmitter = gameObject.AddComponent<EnemyFootstepEmitter>();
        }

        if (m_vocalEmitter == null && !TryGetComponent(out m_vocalEmitter))
        {
            m_vocalEmitter = gameObject.AddComponent<EnemyVocalEmitter>();
        }

        enemyAttack?.SetAnimator(animator);
        ragdollController?.SetAnimator(animator);

        if (m_feedback != null)
        {
            EnsureFeedbackEmitter();
        }
    }

    /// <summary>
    /// 실제 FBX 골격 루트에 붙은 Animator를 선택하고 Enemy 루트 Animator의 설정을 넘깁니다.
    /// </summary>
    /// <remarks>
    /// FBX에서 생성한 Humanoid Avatar는 FBX 루트 기준의 골격 경로를 사용합니다. 프리팹 최상위 Enemy에
    /// 같은 Avatar를 연결하면 모델 래퍼 Transform이 경로 앞에 추가되어 스킨 bind pose와 애니메이션 골격이
    /// 어긋날 수 있습니다. 따라서 SkinnedMeshRenderer를 소유한 모델 자식 Animator를 우선 사용합니다.
    /// </remarks>
    private Animator ResolveAnimator()
    {
        Animator rootAnimator = GetComponent<Animator>();
        Animator modelAnimator = FindModelAnimator();

        if (modelAnimator == null)
        {
            return rootAnimator;
        }

        if (rootAnimator != null && rootAnimator != modelAnimator)
        {
            bool shouldEnableModelAnimator = rootAnimator.enabled;

            modelAnimator.runtimeAnimatorController = rootAnimator.runtimeAnimatorController;
            if (modelAnimator.avatar == null && rootAnimator.avatar != null)
            {
                modelAnimator.avatar = rootAnimator.avatar;
            }

            modelAnimator.applyRootMotion = rootAnimator.applyRootMotion;
            modelAnimator.updateMode = rootAnimator.updateMode;
            modelAnimator.cullingMode = rootAnimator.cullingMode;
            modelAnimator.speed = rootAnimator.speed;

            rootAnimator.enabled = false;
            modelAnimator.enabled = shouldEnableModelAnimator;
        }

        EnemyAnimatorRootMotionRelay relay = modelAnimator.GetComponent<EnemyAnimatorRootMotionRelay>();
        if (relay == null)
        {
            relay = modelAnimator.gameObject.AddComponent<EnemyAnimatorRootMotionRelay>();
        }

        relay.Initialize(this, modelAnimator);

        // 애니메이션 이벤트는 Animator가 붙은 GameObject의 컴포넌트에만 전달됩니다. 재생을 모델 자식으로
        // 옮긴 이상 루트의 이 클래스는 공격 클립 이벤트를 받지 못하므로, 같은 방식의 전달 계층을 붙입니다.
        // 없으면 OnAttackHitboxOn이 오지 않아 판정 콜라이더가 켜지지 않고 근접 공격이 무피해가 됩니다.
        EnemyAnimatorEventRelay eventRelay = modelAnimator.GetComponent<EnemyAnimatorEventRelay>();
        if (eventRelay == null)
        {
            eventRelay = modelAnimator.gameObject.AddComponent<EnemyAnimatorEventRelay>();
        }

        eventRelay.Initialize(this);
        return modelAnimator;
    }

    /// <summary>SkinnedMeshRenderer 골격을 직접 포함하는 모델 자식 Animator를 찾습니다.</summary>
    private Animator FindModelAnimator()
    {
        Animator[] candidates = GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < candidates.Length; i++)
        {
            Animator candidate = candidates[i];
            if (candidate.transform == transform)
            {
                continue;
            }

            if (candidate.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Feedback SO가 있을 때만 감염체 피드백 emitter를 런타임에 준비합니다.</summary>
    private EnemyFeedbackEmitter EnsureFeedbackEmitter()
    {
        if (m_feedback == null)
        {
            return null;
        }

        if (feedbackEmitter == null && !TryGetComponent(out feedbackEmitter))
        {
            feedbackEmitter = gameObject.AddComponent<EnemyFeedbackEmitter>();
        }

        return feedbackEmitter;
    }

    /// <summary>상태 인스턴스를 생성합니다.</summary>
    private void CreateStates()
    {
        Wander = new WanderState(this);
        Alert = new AlertState(this);
        NoiseChase = new NoiseChaseState(this);
        NoiseSearch = new NoiseSearchState(this);
        Combat = new CombatState(this);
        Dead = new DeadState(this);
        m_defenseAttack = new AttackState(this, true);
    }

    /// <summary>피해를 받으면 공격자를 인식하고 교전으로 전이합니다.</summary>
    /// <param name="damage">이번에 적용된 피해량입니다.</param>
    /// <param name="attacker">피해를 입힌 대상입니다. 공격자를 알 수 없는 경로면 null입니다.</param>
    /// <remarks>
    /// 직접 피격은 비전투 감지 보호(§5.6)를 무시합니다. 시야·접촉·소음은 AI 동료를 감지 대상에서 빼지만,
    /// 실제로 맞았다면 누가 쐈든 알아채야 합니다. 오사로 맞은 변이체가 쏜 쪽을 쫓아오는 규칙이 여기서 성립합니다.
    ///
    /// 경직은 여기서 다루지 않습니다. 피해와 경직은 서로 다른 값으로 판정되며(피해 0인 경직도, 경직 0인 피해도 성립),
    /// 경직력 누적은 <see cref="EnemyHealth.ApplyStagger"/>가 따로 받습니다. 발동하면 <see cref="HandleStaggered"/>로 옵니다.
    /// </remarks>
    private void HandleDamaged(float damage, GameObject attacker)
    {
        if (m_current == Dead)
        {
            return;
        }

        SquadMemberController member = attacker != null
            ? attacker.GetComponentInParent<SquadMemberController>()
            : null;

        // 방어 목표 구역 안에서는 구역 밖 공격자의 사격보다 목표 유지 규칙이 우선합니다.
        // 정리 구간 강제 추적은 CanPursueDefenseSquadTarget 내부에서 예외 처리합니다.
        if (IsDefenseEnemy
            && DefenseDisposition != EnemyDefenseDisposition.Default
            && member != null
            && !CanPursueDefenseSquadTarget(member))
        {
            return;
        }

        // 혼합형은 스쿼드원에게 맞으면 여기서 어그로를 켜고, 그 뒤로는 일반 피격 규칙을 따릅니다.
        TryBeginHybridAggro(attacker);

        // 피해/경직 자체는 체력 모듈이 처리합니다. 여기서는 경로 정책을 깨는 어그로 전환만 막습니다.
        if (ShouldDeferSquadCombat()) return;

        if (member != null)
        {
            if (DefenseDisposition == EnemyDefenseDisposition.PlayerFirst)
            {
                m_hasDefenseSquadTargetSelection = true;
                SetDefenseSquadPursuitTarget(member, true);
            }

            targetSensor?.NotifyDamagedBy(member);
        }

        TransitionTo(Combat);
    }

    /// <summary>
    /// 선택했을 때 반경 계열 수치를 Scene 뷰에 그립니다.
    /// </summary>
    /// <remarks>
    /// 반경은 숫자만 봐서는 넓이가 잡히지 않아 튜닝이 감으로 흐릅니다.
    ///
    /// 시야 반경·각도는 <see cref="EnemyTargetSensor"/>가 이미 그리므로 여기서 중복해서 그리지 않습니다.
    /// 공격 시작 거리는 <see cref="EnemyAttack"/>가, 하울링 반경은 <see cref="HowlAbility"/>가 소유합니다.
    /// 각 수치를 소유한 컴포넌트가 자기 것을 그립니다.
    ///
    /// <see cref="OnDrawGizmos"/>가 아니라 선택 시에만 그립니다. 씬에 개체가 14마리 넘게 있어 상시로 그리면
    /// 원이 겹쳐 아무것도 못 읽습니다.
    /// </remarks>
    private void OnDrawGizmosSelected()
    {
        if (!m_debugDrawWanderRadius)
        {
            return;
        }

        // 배회 기준점은 배회에 진입한 시점의 위치입니다(WanderState). Edit Mode에서는 배치 위치가 곧 기준점이지만,
        // Play 중에는 교전 이탈 지점이 기준이 되어 현재 위치와 다를 수 있습니다.
        Gizmos.color = new Color(0.3f, 0.8f, 1.0f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, wanderRadius);

        Gizmos.color = new Color(0.6f, 0.4f, 1.0f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, noiseSearchRadius);
    }

    /// <summary>사망 이벤트를 받아 처치 상태로 전이합니다.</summary>
    /// <remarks>
    /// 경직 중에 죽을 수 있으므로 잠금을 먼저 풉니다. 잠금이 남아 있으면 <see cref="DeadState"/>의 Tick이
    /// 남은 경직 시간만큼 돌지 않아 시체 정리가 그만큼 늦어집니다.
    /// 애니메이터 플래그는 되돌리지 않습니다. 사망은 애니메이터를 끄고 래그돌이 이어받으므로 의미가 없습니다.
    /// </remarks>
    private void HandleDied()
    {
        m_isStaggered = false;
        RestoreAgentPositionOwnership();

        TransitionTo(Dead);
    }

    /// <summary>
    /// 경직 중 넘겼던 위치 소유권을 Agent에 돌려줍니다.
    /// </summary>
    /// <remarks>
    /// 경직이 정상 종료되지 않는 경로(사망·비활성화)를 위한 것입니다. 여기서 되돌리지 않으면
    /// <c>updatePosition</c>이 꺼진 채로 남아 이후 Agent 이동이 화면에 전혀 반영되지 않습니다.
    /// 제자리에서 미끄러지듯 애니메이션만 도는 증상으로 나타납니다.
    /// </remarks>
    private void RestoreAgentPositionOwnership()
    {
        m_staggerRootMotionActive = false;

        if (agent == null || !agent.enabled || agent.updatePosition)
        {
            return;
        }

        // 루트 모션이 옮겨 놓은 최종 위치로 Agent를 통째로 이동시킨 뒤 소유권을 돌려줍니다.
        // Warp는 내부 위치와 경로 상태를 함께 맞춰 주므로 nextPosition만 대입할 때 남는 어긋남이 없습니다.
        agent.Warp(transform.position);
        agent.updatePosition = true;
        agent.updateRotation = m_agentRotationBeforeStagger;

        // 그래도 NavMesh 밖이면 되돌립니다. 밖에 서 있으면 경로 계산이 전부 실패해 유효 대상을 잃고,
        // 추격이 갈 곳 없이 제자리에 굳습니다(경직을 두 번 당한 개체가 일어나서 멈춰 있던 증상).
        if (!agent.isOnNavMesh
            && NavMesh.SamplePosition(
                transform.position, out NavMeshHit recovery, StaggerNavMeshSampleRadius, NavMesh.AllAreas))
        {
            agent.Warp(recovery.position);
        }

        if (agent.isOnNavMesh)
        {
            // 경직 전 경로는 밀려나기 전 위치 기준이라 그대로 두면 밀려난 만큼 되돌아가는 이동이 먼저 나옵니다.
            agent.ResetPath();
        }
        else if (m_debugLogStagger)
        {
            Debug.LogWarning("[Stagger] 경직 후 NavMesh 밖에 남았습니다. 추격이 멈출 수 있습니다.", this);
        }
    }

    /// <summary>
    /// 경직 이탈 블렌드가 끝났으면 위치 소유권을 Agent에 돌려줍니다.
    /// </summary>
    /// <remarks>
    /// 잠금이 풀린 뒤에도 애니메이터는 이탈 전이(0.25초) 동안 경직 자세에서 빠져나오는 중입니다.
    /// 그 구간의 루트 모션까지 위치에 반영해야 일어서는 동작과 이후 이동이 이어집니다.
    /// 블렌드 중에 Agent가 위치를 끌고 가면 몸이 자세와 어긋나 순간이동처럼 보입니다.
    ///
    /// 블렌드 중에도 <b>행동은 이미 재개된 상태</b>입니다. 잠금은 위치가 아니라 판단을 막는 것이므로,
    /// 이 구간에 추격이 경로를 세워 두면 인계 직후 곧바로 달릴 수 있습니다.
    ///
    /// 감지 실패를 대비해 시간 제한을 둡니다. 애니메이터 배선이 없으면 블렌드 종료를 알 수 없고,
    /// 그대로 두면 Agent 이동이 영구히 화면에 반영되지 않습니다.
    /// </remarks>
    private void UpdateStaggerHandover()
    {
        if (!m_staggerRootMotionActive || m_isStaggered)
        {
            return;
        }

        if (animator != null && Time.time < m_staggerHandoverDeadline)
        {
            // 전이 중이면 아직 경직 자세가 섞여 있습니다.
            if (animator.IsInTransition(0))
            {
                return;
            }

            // 전이가 없더라도 아직 경직 스테이트면 기다립니다.
            if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == StaggerStateHash)
            {
                return;
            }
        }

        RestoreAgentPositionOwnership();
    }
}
