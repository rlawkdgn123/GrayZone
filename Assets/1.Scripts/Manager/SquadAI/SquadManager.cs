using System.Collections;
using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using VInspector;

/// <summary>
/// 스쿼드 멤버의 PlayerSquadMember 역할과 카메라 타겟 전환을 관리하는 컴포넌트입니다.
/// </summary>
/// <remarks>
/// 지정된 입력 키를 통해 조작 중인 스쿼드 멤버를 전환하고,
/// PlayerSquadMember의 카메라 타겟을 follow/aim 카메라에 반영합니다.
/// </remarks>
public class SquadManager : MonoBehaviour
{
    private static SquadManager s_instance;

    /// <summary>현재 씬의 인스턴스입니다. 스쿼드가 없는 씬이면 <c>null</c>입니다.</summary>
    /// <remarks>
    /// 씬에 속하므로 씬 전환과 함께 사라집니다. 셸터처럼 스쿼드가 없는 씬에서는 없는 것이 정상입니다.
    /// <para>
    /// <b>캐시가 비었으면 한 번 더 찾습니다(실측으로 발견).</b> Play Mode 중에 스크립트를 다시 컴파일하면
    /// 도메인 리로드로 static이 초기화되는데, 씬 오브젝트는 그대로 살아 있어 <c>Awake</c>가 다시 돌지 않습니다.
    /// 그러면 이 캐시가 영영 <c>null</c>로 남아 스쿼드에 의존하는 모든 기능이 조용히 멈춥니다.
    /// 재탐색은 캐시가 빈 경우에만 하므로 평소 비용은 없습니다.
    /// </para>
    /// </remarks>
    public static SquadManager Instance
    {
        get
        {
            if (s_instance == null && Application.isPlaying)
            {
                s_instance = FindFirstObjectByType<SquadManager>();
            }

            return s_instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        s_instance = null;
    }

    private readonly SquadEngagement m_engagement = new SquadEngagement();

    /// <summary>스쿼드가 공유하는 전투/비전투 상태입니다.</summary>
    /// <remarks>
    /// 변이체가 <see cref="EnemyTargetSensor.SetEngaged"/>로 교전에 들고 날 때 갱신됩니다.
    /// 컴포넌트가 아니라 이 매니저가 소유하는 일반 객체이므로 씬 배선이 필요 없습니다.
    /// 자세한 규칙과 방향 제약은 <see cref="SquadEngagement"/>를 보십시오.
    /// </remarks>
    public SquadEngagement Engagement => m_engagement;

    private readonly SquadEnemyIntel m_enemyIntel = new SquadEnemyIntel();

    /// <summary>스쿼드가 공유하는 교전 적 위치 정보입니다.</summary>
    /// <remarks>
    /// 공용 문서 §8이 정본입니다. 플레이어 확인(§8.2)은 이 매니저가 카메라로 판정하고,
    /// AI 확인(§8.3)은 각 <see cref="SquadAIController"/>가 자기 시야로 판정해 보고합니다.
    /// </remarks>
    public SquadEnemyIntel EnemyIntel => m_enemyIntel;

    [Foldout("Squad Options")]
    [Tooltip("관리할 스쿼드 멤버 목록입니다.")]
    [FormerlySerializedAs("squadMembers")]
    [SerializeField] private List<SquadMemberController> m_squadMembers = new List<SquadMemberController>();

    [Tooltip("현재 PlayerSquadMember의 스쿼드 목록 인덱스입니다.")]
    [FormerlySerializedAs("currentMemberIndex")]
    [FormerlySerializedAs("m_currentMemberIndex")]
    [SerializeField] private int m_playerSquadMemberIndex;

    [Tooltip("스쿼드 멤버 순서와 같은 플레이어 공개 데이터 목록입니다.")]
    [SerializeField] private List<PlayerbleUnitData> m_playerDataSources = new List<PlayerbleUnitData>();

    [Foldout("Switch Options")]
    [Tooltip("카메라 타겟을 새 멤버로 넘기는 대신 PlayerSquadMember와 전환 대상 멤버의 Transform을 스왑하는 전환 방식을 사용할지 여부입니다.")]
    [SerializeField] private bool m_swapMemberTransforms;

    [Tooltip("PlayerSquadMember가 사망해 자동 전환될 때도 Transform 스왑 방식을 사용할지 여부입니다.")]
    [SerializeField] private bool m_swapMemberTransformsOnDeath;

    [Tooltip("다운 강제 전환에서 카메라가 새 캐릭터로 넘어가는 데 쓰는 시간입니다. 이 시간이 지나야 조작권이 넘어가고, 그동안은 AI가 그 캐릭터를 계속 조작합니다.")]
    [SerializeField] private float m_forcedSwitchCameraDuration = 0.5f;

    [Tooltip("전환 입력을 다시 받기까지 기다리는 시간(초)입니다. 연타로 전환이 겹쳐 쌓이는 것을 막습니다. 사망 자동 전환에는 적용하지 않습니다.")]
    [SerializeField] private float m_switchInputCooldown = 0.1f;

    [Foldout("Camera Options")]
    [Tooltip("현재 멤버를 따라가는 기본 카메라입니다.")]
    [FormerlySerializedAs("followCamera")]
    [SerializeField] private CinemachineCamera m_followCamera;

    [Tooltip("현재 멤버를 따라가는 조준 카메라입니다.")]
    [FormerlySerializedAs("aimCamera")]
    [SerializeField] private CinemachineCamera m_aimCamera;

    [Foldout("Input Options")]
    [Tooltip("다음 스쿼드 멤버로 전환하는 키입니다.")]
    [FormerlySerializedAs("nextMemberKey")]
    [SerializeField] private Key m_nextMemberKey = Key.Tab;

    [Tooltip("첫 번째 스쿼드 멤버로 전환하는 키입니다.")]
    [FormerlySerializedAs("member1Key")]
    [SerializeField] private Key m_member1Key = Key.Digit1;

    [Tooltip("두 번째 스쿼드 멤버로 전환하는 키입니다.")]
    [FormerlySerializedAs("member2Key")]
    [SerializeField] private Key m_member2Key = Key.Digit2;

    [Tooltip("세 번째 스쿼드 멤버로 전환하는 키입니다.")]
    [FormerlySerializedAs("member3Key")]
    [SerializeField] private Key m_member3Key = Key.Digit3;

    [Foldout("Enemy Intel Options")]
    [Tooltip("교전 적을 지금 보고 있는지 다시 판정하는 주기입니다. 짧을수록 반응이 빠르지만 시야 판정 비용이 늘어납니다.")]
    [SerializeField] private float m_enemyIntelInterval = 0.2f;

    [Tooltip("시야를 가로막는 고정 환경 장애물 레이어입니다. 다른 캐릭터나 적은 여기 포함하지 않습니다.")]
    [SerializeField] private LayerMask m_intelObstacleLayer;

    [Tooltip("적을 겨눌 때 발밑에서 얼마나 위를 보는지입니다. 변이체 쪽 시야 판정과 같은 방식으로 가슴 높이를 겨눕니다.")]
    [SerializeField] private float m_intelTargetHeight = 1.0f;

    [Tooltip("피격으로 확인한 공격자 위치를 실시간으로 유지하는 시간입니다. 뒤에서 맞아도 잠시 위치를 공유합니다.")]
    [SerializeField] private float m_attackerIntelHoldDuration = 3.0f;

    [Foldout("Squad AI Options")]
    [Tooltip("AI 팀원끼리 유지할 간격입니다. 목적지가 다른 AI의 현재 위치나 찜한 목적지와 이 거리 안이면 겹친 것으로 봅니다. 캡슐 반지름이 0.3이라 이 값에서 0.6을 뺀 만큼이 실제 몸 사이 여유입니다.")]
    [SerializeField] private float m_aiMemberSpacing = 1.5f;

    [Tooltip("팀 AI의 발사 허용 여부입니다. 끄면 조준과 대상 선정은 그대로 두고 발사와 재장전만 멈춥니다. 조작 중인 캐릭터의 사격에는 영향이 없습니다.")]
    [SerializeField] private bool m_aiFiringAllowed = true;

    // Debug 구역은 직렬화 필드의 맨 끝에 둡니다. Foldout은 다음 Foldout이나 EndFoldout이 나올 때까지 이어지므로,
    // 중간에 두면 뒤따르는 필드가 전부 Debug 구역으로 딸려 들어갑니다.
    [Foldout("Debug")]
    [Tooltip("멤버 전환 직후 각 멤버의 제어 주체, 위치, NavMeshAgent, Animator 상태를 콘솔에 출력합니다.")]
    [SerializeField] private bool m_logSwitchDebug = true;

    /// <summary>전환 입력을 다시 받을 수 있는 시각입니다.</summary>
    private float m_nextSwitchInputTime;

    /// <summary>다음 적 정보 갱신 시각입니다.</summary>
    private float m_nextEnemyIntelTime;

    /// <summary>해석이 끝난 시야 차단 레이어 마스크 캐시입니다.</summary>
    private int m_resolvedIntelObstacleMask;

    private bool m_hasInitialized;
    private bool m_squadEliminationNotified;
    private readonly List<SquadMemberController> m_subscribedMemberDeathEvents = new List<SquadMemberController>();

    /// <summary>진행 중인 다운 강제 전환의 카메라 이동 구간입니다. 없으면 null입니다(§15.1).</summary>
    private Coroutine m_forcedSwitchRoutine;

    // 자동 구조를 맡은 AI 멤버와 그 대상입니다(§16). 하나만 구조하도록 스쿼드가 중재합니다.
    private SquadMemberController m_autoRescuer;
    private SquadMemberController m_autoRescueTarget;

    // AI가 찜해 둔 이동 목적지입니다(§6.1, §6.3). 서로 같은 자리를 노리지 않게 스쿼드가 들고 있습니다.
    private readonly Dictionary<SquadMemberController, Vector3> m_destinationClaims =
        new Dictionary<SquadMemberController, Vector3>();

    /// <summary>지금 다운 강제 전환의 카메라 이동 중인지 여부입니다.</summary>
    /// <remarks>
    /// 이 구간에는 조작권이 아직 넘어가지 않았고 대상 캐릭터를 AI가 조작합니다(§15.1). 진단용입니다.
    /// </remarks>
    public bool IsForcedSwitching => m_forcedSwitchRoutine != null;

    /// <summary>조작 가능한 스쿼드원이 한 명도 남지 않아 게임오버 조건이 성립했을 때 발생합니다.</summary>
    public event Action OnSquadEliminated;

    /// <summary>현재 PlayerSquadMember의 스쿼드 목록 인덱스입니다.</summary>
    public int PlayerSquadMemberIndex => m_playerSquadMemberIndex;

    /// <summary>멤버 전환 시 카메라 타겟 전환 대신 Transform 스왑 방식을 사용할지 여부입니다.</summary>
    public bool SwapMemberTransforms => m_swapMemberTransforms;

    /// <summary>PlayerSquadMember 사망으로 자동 전환될 때 Transform 스왑 방식을 사용할지 여부입니다.</summary>
    public bool SwapMemberTransformsOnDeath => m_swapMemberTransformsOnDeath;

    /// <summary>관리 중인 스쿼드 멤버 목록입니다.</summary>
    public IReadOnlyList<SquadMemberController> SquadMembers => m_squadMembers;

    /// <summary>스쿼드 멤버 순서에 대응하는 플레이어 공개 데이터 목록입니다.</summary>
    public IReadOnlyList<PlayerbleUnitData> PlayerDataSources => m_playerDataSources;

    /// <summary>현재 PlayerSquadMember에 대응하는 플레이어 공개 데이터입니다.</summary>
    public PlayerbleUnitData PlayerSquadMemberData => GetPlayerData(m_playerSquadMemberIndex);

    /// <summary>현재 플레이어가 직접 조작 중인 스쿼드 멤버입니다.</summary>
    public SquadMemberController PlayerSquadMember
    {
        get
        {
            if (m_squadMembers == null || m_squadMembers.Count == 0)
            {
                return null;
            }

            if (m_playerSquadMemberIndex < 0 || m_playerSquadMemberIndex >= m_squadMembers.Count)
            {
                return null;
            }

            return m_squadMembers[m_playerSquadMemberIndex];
        }
    }

    /// <summary>지정한 스쿼드 목록 인덱스에 대응하는 플레이어 공개 데이터를 반환합니다.</summary>
    /// <param name="index">조회할 스쿼드 목록 인덱스입니다.</param>
    /// <returns>대응하는 데이터가 없으면 null입니다.</returns>
    public PlayerbleUnitData GetPlayerData(int index)
    {
        SyncPlayerDataSources();

        if (m_playerDataSources == null || index < 0 || index >= m_playerDataSources.Count)
        {
            return null;
        }

        return m_playerDataSources[index];
    }

    /// <summary>다음 멤버로 전환하는 입력 키입니다.</summary>
    public Key NextMemberKey => m_nextMemberKey;

    /// <summary>첫 번째 멤버로 전환하는 입력 키입니다.</summary>
    public Key Member1Key => m_member1Key;

    /// <summary>두 번째 멤버로 전환하는 입력 키입니다.</summary>
    public Key Member2Key => m_member2Key;

    /// <summary>세 번째 멤버로 전환하는 입력 키입니다.</summary>
    public Key Member3Key => m_member3Key;

    /// <summary>
    /// Inspector에서 컴포넌트가 추가되거나 Reset될 때 현재 씬 기준으로 참조를 자동 보정합니다.
    /// </summary>
    private void Reset()
    {
        AutoFindReferences();
        NormalizeMemberIndex();
    }

    /// <summary>
    /// 런타임 시작 시 필요한 참조를 보정하고 현재 멤버 인덱스를 유효 범위로 보정합니다.
    /// </summary>
    private void Awake()
    {
        // 스쿼드 구성은 씬에 하나만 있어야 합니다. 중복이 있으면 조작 캐릭터가 둘로 갈립니다.
        if (s_instance != null && s_instance != this)
        {
            Debug.LogWarning("[SquadManager] 씬에 이미 인스턴스가 있어 중복된 쪽을 제거합니다.", this);
            Destroy(gameObject);
            return;
        }

        s_instance = this;

        AutoFindReferences();
        NormalizeMemberIndex();
        ApplyInitialMemberRolePresets();
        RefreshCharacterCameraCollisionResponses();
    }

    private void OnDestroy()
    {
        if (s_instance == this)
        {
            s_instance = null;
        }
    }

    private void OnEnable()
    {
        AutoFindReferences();
        SubscribeMemberDeathEvents();
    }

    private void OnDisable()
    {
        UnsubscribeMemberDeathEvents();
    }

    /// <summary>
    /// 스쿼드 멤버들의 조작 상태를 초기화한 뒤 현재 멤버만 직접 조작 상태로 전환합니다.
    /// </summary>
    /// <returns>Unity 코루틴 실행을 위한 IEnumerator입니다.</returns>
    private IEnumerator Start()
    {
        UpdateCameraTarget();

        // Awake에서 역할 프리셋을 이미 적용했습니다. PlayerInput, Controller, NavMeshAgent의 초기 활성 상태가
        // 안정화될 때까지 대기한 뒤 현재 상태를 한 번 더 확인합니다.
        yield return null;
        yield return null;

        UpdateCameraTarget();
        RefreshPlayerSquadMemberWeaponUI();

        // follower, input, animation 상태를 한 번 더 강제로 동기화합니다.
        yield return null;
        ForceRefreshMembers();
        UpdateCameraTarget();
        RefreshPlayerSquadMemberWeaponUI();

        m_hasInitialized = true;
    }

    /// <summary>
    /// 매 프레임 스쿼드 멤버 전환 입력을 처리합니다.
    /// </summary>
    private void Update()
    {
        if (!m_hasInitialized)
        {
            return;
        }

        HandleSwitchInput();
        UpdateEnemyIntel();
    }

    /// <summary>
    /// 교전 적을 지금 누가 보고 있는지 판정해 공유 정보를 갱신합니다(§8).
    /// </summary>
    /// <remarks>
    /// 매 프레임이 아니라 주기로 돕니다. 판정마다 적 수만큼 Raycast가 나가므로 주기를 없애면
    /// 교전 규모에 비례해 비용이 커집니다. 확인 자체에는 누적 시간이 없으므로(§8.2) 주기가
    /// 곧 반응 지연이며, 그 값은 밸런스 영역입니다.
    /// </remarks>
    private void UpdateEnemyIntel()
    {
        if (!m_engagement.IsInCombat && m_enemyIntel.TrackedCount == 0)
        {
            return;
        }

        if (Time.time < m_nextEnemyIntelTime)
        {
            return;
        }

        m_nextEnemyIntelTime = Time.time + Mathf.Max(0.02f, m_enemyIntelInterval);

        m_enemyIntel.Refresh(m_engagement, IsEnemyConfirmedBySquad);
    }

    /// <summary>
    /// 스쿼드원 중 누구라도 이 적을 직접 확인하고 있는지 판정합니다.
    /// </summary>
    /// <param name="enemy">확인할 적입니다.</param>
    /// <returns>한 명이라도 확인하고 있으면 true입니다.</returns>
    /// <remarks>
    /// 플레이어는 카메라 기준(§8.2), AI는 자기 캐릭터 시야 기준(§8.3)으로 서로 다르게 판정합니다.
    /// 한 명만 확인하면 스쿼드 전체가 공유하므로 첫 성공에서 바로 끊습니다.
    /// </remarks>
    private bool IsEnemyConfirmedBySquad(EnemyController enemy)
    {
        if (enemy == null)
        {
            return false;
        }

        Vector3 targetPoint = enemy.transform.position + Vector3.up * m_intelTargetHeight;

        if (IsVisibleToPlayerCamera(targetPoint))
        {
            return true;
        }

        for (int i = 0; i < m_squadMembers.Count; i++)
        {
            SquadMemberController member = m_squadMembers[i];
            if (member == null || member.IsPlayerSquadMember || !member.IsAlive || member.IsDown)
            {
                continue;
            }

            SquadAIController ai = member.GetComponent<SquadAIController>();
            if (ai != null && ai.enabled && ai.CanSeePoint(targetPoint, ResolveIntelObstacleMask()))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 게임플레이 카메라가 이 지점을 보고 있는지 판정합니다(§8.2).
    /// </summary>
    /// <param name="point">확인할 지점입니다.</param>
    /// <returns>화면 안에 있고 장애물에 가리지 않으면 true입니다.</returns>
    /// <remarks>
    /// 캐릭터 시야가 아니라 <b>카메라</b>가 기준입니다. 플레이어는 화면으로 보기 때문이며,
    /// 문서가 "캐릭터가 아닌 카메라가 확인한 결과를 반영한다"고 명시합니다.
    /// 차폐 검사의 시작점도 카메라입니다. 화면에 보이는데 캐릭터 눈높이에서 가렸다고 판정하면
    /// 플레이어가 본 것을 동료가 모르는 상황이 생깁니다.
    /// </remarks>
    private bool IsVisibleToPlayerCamera(Vector3 point)
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            return false;
        }

        Vector3 viewport = cam.WorldToViewportPoint(point);
        if (viewport.z <= 0.0f || viewport.x < 0.0f || viewport.x > 1.0f || viewport.y < 0.0f || viewport.y > 1.0f)
        {
            return false;
        }

        Vector3 origin = cam.transform.position;
        Vector3 delta = point - origin;
        float distance = delta.magnitude;
        if (distance <= 0.0001f)
        {
            return true;
        }

        return !Physics.Raycast(origin, delta / distance, distance, ResolveIntelObstacleMask(), QueryTriggerInteraction.Ignore);
    }

    /// <summary>시야·사격선 차폐 판정에 쓸 레이어 마스크입니다.</summary>
    /// <remarks>AI가 사격선을 판정할 때도 같은 마스크를 써야 판정이 어긋나지 않습니다.</remarks>
    public int IntelObstacleMask => ResolveIntelObstacleMask();

    /// <summary>시야 차폐 판정에 쓸 레이어 마스크를 구합니다.</summary>
    /// <remarks>비어 있으면 변이체 쪽과 같은 기본 장애물 레이어로 대체합니다.</remarks>
    private int ResolveIntelObstacleMask()
    {
        if (m_resolvedIntelObstacleMask == 0)
        {
            m_resolvedIntelObstacleMask = EnemyLayers.ResolveObstacleMask(m_intelObstacleLayer, this, "적 정보 공유 시야 차단");
        }

        return m_resolvedIntelObstacleMask;
    }

    /// <summary>
    /// 스쿼드원이 적에게 피격됐음을 알려 공격자 위치를 즉시 공유합니다(§8.4).
    /// </summary>
    /// <param name="attacker">피해를 입힌 적입니다.</param>
    /// <remarks>시야와 무관하게 성립합니다. 뒤에서 맞아도 누가 때렸는지는 알기 때문입니다.</remarks>
    public void NotifySquadDamagedBy(EnemyController attacker)
    {
        m_enemyIntel.NotifyDamagedBy(attacker, m_attackerIntelHoldDuration);
    }

    /// <summary>
    /// 씬 또는 자식 오브젝트에서 필요한 참조를 자동 탐색합니다.
    /// </summary>
    private void AutoFindReferences()
    {
        if (m_squadMembers == null)
        {
            m_squadMembers = new List<SquadMemberController>();
        }

        if (m_squadMembers.Count == 0)
        {
            GetComponentsInChildren(true, m_squadMembers);
        }

        RemoveNullMembers();
        SyncPlayerDataSources();
    }

    /// <summary>
    /// 멤버 목록에서 null 항목을 제거합니다.
    /// </summary>
    private void RemoveNullMembers()
    {
        if (m_squadMembers == null)
        {
            return;
        }

        for (int i = m_squadMembers.Count - 1; i >= 0; i--)
        {
            if (m_squadMembers[i] == null)
            {
                m_squadMembers.RemoveAt(i);
            }
        }
    }

    private void SyncPlayerDataSources()
    {
        if (m_playerDataSources == null)
        {
            m_playerDataSources = new List<PlayerbleUnitData>();
        }

        int memberCount = m_squadMembers != null ? m_squadMembers.Count : 0;
        while (m_playerDataSources.Count < memberCount)
        {
            m_playerDataSources.Add(null);
        }

        while (m_playerDataSources.Count > memberCount)
        {
            m_playerDataSources.RemoveAt(m_playerDataSources.Count - 1);
        }

        for (int i = 0; i < memberCount; i++)
        {
            SquadMemberController member = m_squadMembers[i];
            PlayerbleUnitData currentData = m_playerDataSources[i];
            if (currentData == null || member == null || currentData.gameObject != member.gameObject)
            {
                m_playerDataSources[i] = member != null ? member.GetComponent<PlayerbleUnitData>() : null;
            }
        }
    }

    /// <summary>
    /// 현재 멤버 인덱스를 멤버 목록의 유효 범위 안으로 보정합니다.
    /// </summary>
    private void NormalizeMemberIndex()
    {
        if (m_squadMembers == null || m_squadMembers.Count == 0)
        {
            m_playerSquadMemberIndex = 0;
            return;
        }

        m_playerSquadMemberIndex = Mathf.Clamp(m_playerSquadMemberIndex, 0, m_squadMembers.Count - 1);
    }

    /// <summary>
    /// 키보드 입력을 확인하여 현재 조작 멤버를 전환합니다.
    /// </summary>
    private void HandleSwitchInput()
    {
        if (Keyboard.current == null)
        {
            return;
        }

        if (Time.time < m_nextSwitchInputTime)
        {
            return;
        }

        if (Keyboard.current[m_nextMemberKey].wasPressedThisFrame)
        {
            RequestSwitchByInput(-1);
        }

        if (Keyboard.current[m_member1Key].wasPressedThisFrame)
        {
            RequestSwitchByInput(0);
        }

        if (Keyboard.current[m_member2Key].wasPressedThisFrame)
        {
            RequestSwitchByInput(1);
        }

        if (Keyboard.current[m_member3Key].wasPressedThisFrame)
        {
            RequestSwitchByInput(2);
        }
    }

    /// <summary>
    /// 입력으로 요청한 전환을 처리하고 다음 입력까지의 간격을 둡니다.
    /// </summary>
    /// <param name="index">전환할 멤버 인덱스이며, -1이면 다음 멤버로 넘깁니다.</param>
    /// <remarks>
    /// 간격을 두는 것은 연타로 전환이 짧은 시간에 쌓이는 것을 막기 위해서입니다.
    /// 전환마다 위치가 맞바뀌므로 반복되면 무슨 일이 일어났는지 보이지 않고,
    /// 전환 직후 밀림이 남아 있는 동안 다시 전환되면 그 영향이 누적됩니다.
    ///
    /// 사망으로 인한 자동 전환에는 걸지 않습니다. 그쪽까지 막으면 조작할 캐릭터가 없는 시간이 생깁니다.
    /// 실제로 전환이 일어났을 때만 간격을 두는 것은, 같은 멤버를 다시 누르거나 전환할 수 없는 대상을 눌렀을 때
    /// 아무 일도 없었는데 입력이 씹히는 것처럼 느껴지지 않게 하기 위해서입니다.
    /// </remarks>
    private void RequestSwitchByInput(int index)
    {
        bool switched = index < 0
            ? TrySwitchToNextMember(m_swapMemberTransforms)
            : TrySwitchToMember(index);

        if (switched)
        {
            m_nextSwitchInputTime = Time.time + Mathf.Max(0f, m_switchInputCooldown);
        }
    }

    /// <summary>지정한 인덱스로 전환을 시도하고 실제로 바뀌었는지 알려 줍니다.</summary>
    /// <param name="index">전환할 스쿼드 멤버 인덱스입니다.</param>
    /// <returns>전환이 일어났으면 true입니다.</returns>
    private bool TrySwitchToMember(int index)
    {
        if (m_squadMembers == null || index < 0 || index >= m_squadMembers.Count)
        {
            return false;
        }

        if (index == m_playerSquadMemberIndex || !CanSwitchTo(index))
        {
            return false;
        }

        SwitchToMember(index, m_swapMemberTransforms);
        return true;
    }

    /// <summary>
    /// 현재 멤버 다음에 있는 전환 가능한 멤버로 조작 대상을 변경합니다.
    /// </summary>
    public void SwitchToNextMember()
    {
        TrySwitchToNextMember(m_swapMemberTransforms);
    }

    private bool TrySwitchToNextMember(bool useTransformSwap, bool logDebug = true)
    {
        if (!TryFindNextSwitchableIndex(out int nextIndex))
        {
            return false;
        }

        SwitchToMember(nextIndex, useTransformSwap, logDebug);
        return true;
    }

    /// <summary>
    /// 고정된 슬롯 순서에 따라 다음으로 조작 가능한 멤버를 찾습니다.
    /// </summary>
    /// <param name="index">찾은 멤버의 인덱스입니다.</param>
    /// <returns>후보를 찾았으면 true입니다.</returns>
    /// <remarks>
    /// 공용 문서 `스쿼드 AI 시스템` §15.1의 "고정된 슬롯 순서에 따라 다음 조작 가능한 슬롯을 탐색한다"와
    /// "다운되거나 전투 이탈한 캐릭터가 배정된 슬롯은 전환 후보에서 제외한다"입니다. 제외 조건은
    /// <see cref="CanSwitchTo"/>가 들고 있습니다.
    /// <para>
    /// 탐색과 전환을 나눈 이유는 강제 전환이 <b>둘 사이에 카메라 이동 구간</b>을 두기 때문입니다(§15.1).
    /// 즉시 전환하는 경로와 대상만 먼저 정하는 경로가 같은 탐색을 씁니다.
    /// </para>
    /// </remarks>
    private bool TryFindNextSwitchableIndex(out int index)
    {
        index = -1;

        if (m_squadMembers == null || m_squadMembers.Count == 0)
        {
            return false;
        }

        int startIndex = m_playerSquadMemberIndex;
        int nextIndex = m_playerSquadMemberIndex;

        do
        {
            nextIndex++;

            if (nextIndex >= m_squadMembers.Count)
            {
                nextIndex = 0;
            }

            if (CanSwitchTo(nextIndex))
            {
                index = nextIndex;
                return true;
            }
        }
        while (nextIndex != startIndex);

        return false;
    }

    /// <summary>
    /// 지정한 인덱스의 스쿼드 멤버로 조작 대상을 변경합니다.
    /// </summary>
    /// <param name="index">전환할 스쿼드 멤버 인덱스입니다.</param>
    public void SwitchToMember(int index)
    {
        SwitchToMember(index, m_swapMemberTransforms);
    }

    private void SwitchToMember(int index, bool useTransformSwap, bool logDebug = true)
    {
        if (m_squadMembers == null || m_squadMembers.Count == 0)
        {
            return;
        }

        if (index < 0 || index >= m_squadMembers.Count)
        {
            return;
        }

        if (!CanSwitchTo(index))
        {
            return;
        }

        if (index == m_playerSquadMemberIndex)
        {
            UpdateCameraTarget();
            RefreshCharacterCameraCollisionResponses();
            RefreshPlayerSquadMemberWeaponUI();
            return;
        }

        SquadMemberController previousMember = PlayerSquadMember;
        SquadMemberController nextMember = m_squadMembers[index];
        bool carrySwitchState = useTransformSwap && previousMember != null && nextMember != null;
        SquadMemberController.SwitchCarryoverState switchState = carrySwitchState
            ? previousMember.CaptureSwitchCarryoverState()
            : default;
        SquadAIController.FollowCarryoverState followState = carrySwitchState
            ? nextMember.CaptureFollowCarryoverState()
            : default;

        if (previousMember != null)
        {
            previousMember.SetPlayerSquadMember(false);
        }

        m_playerSquadMemberIndex = index;

        if (nextMember != null)
        {
            nextMember.SetPlayerSquadMember(true);

            if (carrySwitchState)
            {
                nextMember.ApplySwitchCarryoverState(switchState);
            }
        }

        if (useTransformSwap)
        {
            ApplyMemberTransformSwap(previousMember, nextMember);

            if (previousMember != null)
            {
                previousMember.ApplyFollowCarryoverState(followState);
            }

            SyncPhysicsAfterMemberTeleport();
        }

        UpdateCameraTarget();
        RefreshCharacterCameraCollisionResponses();
        RefreshPlayerSquadMemberWeaponUI();

        if (useTransformSwap)
        {
            CancelCameraDamping();
        }

        if (logDebug && m_logSwitchDebug)
        {
            StartCoroutine(LogSwitchDebug(previousMember, nextMember));
        }
    }

    private void SubscribeMemberDeathEvents()
    {
        if (m_squadMembers == null)
        {
            return;
        }

        for (int i = 0; i < m_squadMembers.Count; i++)
        {
            SquadMemberController member = m_squadMembers[i];
            if (member == null || m_subscribedMemberDeathEvents.Contains(member))
            {
                continue;
            }

            member.OnMemberDied += HandleMemberDied;
            member.OnMemberDowned += HandleMemberDowned;
            m_subscribedMemberDeathEvents.Add(member);
        }
    }

    private void UnsubscribeMemberDeathEvents()
    {
        for (int i = 0; i < m_subscribedMemberDeathEvents.Count; i++)
        {
            SquadMemberController member = m_subscribedMemberDeathEvents[i];
            if (member != null)
            {
                member.OnMemberDied -= HandleMemberDied;
                member.OnMemberDowned -= HandleMemberDowned;
            }
        }

        m_subscribedMemberDeathEvents.Clear();
    }

    private void HandleMemberDied(SquadMemberController member)
    {
        if (member == null)
        {
            return;
        }

        bool wasPlayerSquadMember = member == PlayerSquadMember;
        bool switched = !wasPlayerSquadMember || TrySwitchToNextMember(m_swapMemberTransformsOnDeath, false);

        member.ClearDeadControlState();

        if (wasPlayerSquadMember && !switched && m_logSwitchDebug)
        {
            Debug.Log("[SquadManager] No available squad member remains after current member death.", this);
        }

        NotifySquadEliminatedIfNeeded();
    }

    /// <summary>
    /// 멤버가 다운(빈사) 상태가 되면, 그 멤버가 현재 조작 대상일 때 조작 가능한 다른 멤버로 자동 전환합니다.
    /// </summary>
    /// <remarks>
    /// 다운은 사망과 달리 구조로 복귀할 수 있으므로 사망 정리(ClearDeadControlState)는 수행하지 않습니다.
    /// 조작 권한 해제는 다운 진입 시 <see cref="SquadMemberController"/>가 이미 처리합니다.
    /// </remarks>
    private void HandleMemberDowned(SquadMemberController member)
    {
        if (member == null || member != PlayerSquadMember)
        {
            return;
        }

        bool switched = BeginForcedSwitch();

        if (!switched && m_logSwitchDebug)
        {
            Debug.Log("[SquadManager] No available squad member remains after current member down.", this);
        }

        NotifySquadEliminatedIfNeeded();
    }

    /// <summary>
    /// 다운 강제 전환을 시작합니다. 카메라만 먼저 옮기고 조작권은 나중에 넘깁니다(§15.1).
    /// </summary>
    /// <returns>넘길 수 있는 멤버를 찾아 전환을 시작했으면 true입니다.</returns>
    /// <remarks>
    /// 문서가 조작권 이양을 <b>카메라 이동이 끝난 뒤</b>로 규정합니다 - "카메라 이동 중 선택된 슬롯은
    /// 기존 AI 판단으로 해당 캐릭터를 계속 조작한다", "카메라 이동 완료 시 선택된 슬롯의 역할을 AI 조작에서
    /// 플레이어 조작으로 변경한다". 그래서 대상만 먼저 정해 카메라를 보내고, 역할 변경은 코루틴이 뒤에 합니다.
    /// <para>
    /// <b>이동 시간은 고정값입니다</b>(<see cref="m_forcedSwitchCameraDuration"/>). 두 기획 문서 모두
    /// 완료 시점을 규정하지 않아 사용자 결정으로 고정 시간을 택했습니다. 카메라가 목표에 닿았는지로
    /// 판정하면 멀리 떨어진 동료로 갈 때 입력 잠금이 길어지고, 감쇠 특성상 끝이 느려져 임계값에 민감해집니다.
    /// </para>
    /// <para>
    /// 전환 구간 동안 입력은 저절로 막힙니다. 다운된 멤버는 다운으로, 새 멤버는 아직 AI라서 양쪽 모두
    /// <see cref="SquadMemberController"/>가 <see cref="PlayerInputController"/>를 꺼 둔 상태이기 때문입니다.
    /// 그래서 "마우스와 카메라 회전 입력을 적용하거나 누적하지 않는다"(`캐릭터 행동 시스템` §14)에
    /// 별도 잠금 경로가 필요하지 않습니다.
    /// </para>
    /// </remarks>
    private bool BeginForcedSwitch()
    {
        if (!TryFindNextSwitchableIndex(out int nextIndex))
        {
            return false;
        }

        // 이미 전환 중이면 그것을 버리고 새로 시작합니다. 앞 대상이 전환 중에 다운됐을 때
        // 옛 코루틴이 살아 있으면 조작 불가 캐릭터로 조작권이 넘어갑니다.
        if (m_forcedSwitchRoutine != null)
        {
            StopCoroutine(m_forcedSwitchRoutine);
        }

        m_forcedSwitchRoutine = StartCoroutine(ForcedSwitchRoutine(nextIndex));
        return true;
    }

    /// <summary>
    /// 카메라를 먼저 보내고, 이동 시간이 지난 뒤 조작권을 넘깁니다(§15.1).
    /// </summary>
    /// <param name="index">조작권을 넘길 멤버 인덱스입니다.</param>
    private IEnumerator ForcedSwitchRoutine(int index)
    {
        SquadMemberController target = m_squadMembers[index];

        // 역할은 그대로 두고 카메라만 보냅니다. 대상은 아직 AI 조작이라 이 구간에도 스스로 움직입니다.
        SetCameraTarget(target);

        yield return new WaitForSeconds(Mathf.Max(0.0f, m_forcedSwitchCameraDuration));

        m_forcedSwitchRoutine = null;

        // 이동하는 동안 대상이 다운되거나 죽었을 수 있습니다. 그러면 처음부터 다시 고릅니다.
        if (!CanSwitchTo(index))
        {
            if (!BeginForcedSwitch())
            {
                NotifySquadEliminatedIfNeeded();
            }

            yield break;
        }

        // 강제 전환은 캐릭터와 위치를 교환하지 않습니다(§15.1).
        SwitchToMember(index, false, false);

        // AI가 들고 있던 이동 목적지·조준·연속 사격 의도는 여기서 끝납니다. 역할이 바뀌면
        // SquadMemberController가 SquadAIController를 끄고, 그때 ClearFollowState가 판단 정보를 지웁니다.
        // 남은 것은 사람 입력 인계뿐입니다(`캐릭터 행동 시스템` §14).
        PlayerInputController input = target.GetComponent<PlayerInputController>();
        if (input != null)
        {
            input.ApplyForcedSwitchInputHandover();
        }
    }

    /// <summary>조작 가능한 멤버가 한 명도 남지 않았으면 전멸 이벤트를 한 번만 발생시킵니다.</summary>
    private void NotifySquadEliminatedIfNeeded()
    {
        if (m_squadEliminationNotified || m_squadMembers == null || m_squadMembers.Count == 0)
        {
            return;
        }

        for (int i = 0; i < m_squadMembers.Count; i++)
        {
            if (CanSwitchTo(i))
            {
                return;
            }
        }

        m_squadEliminationNotified = true;
        OnSquadEliminated?.Invoke();
    }

    /// <summary>
    /// 지금 자동 구조를 맡고 있는 AI 멤버입니다. 없으면 <c>null</c>입니다(§16).
    /// </summary>
    /// <remarks>
    /// 문서가 "자동 구조를 수행할 수 있는 AI 조작 슬롯 <b>하나만</b> 구조를 시작한다"고 규정하므로,
    /// 누가 맡았는지는 개별 AI가 아니라 스쿼드가 알아야 합니다. 여럿이 동시에 달려가면 나머지는
    /// 헛걸음하고 그동안 동행이 비는데, 각자 판단해서는 그것을 막을 수 없습니다.
    /// </remarks>
    public SquadMemberController AutoRescuer => m_autoRescuer;

    /// <summary>
    /// 자동 구조 대상을 선점합니다(§16).
    /// </summary>
    /// <param name="claimant">구조를 맡겠다는 AI 멤버입니다.</param>
    /// <param name="target">구조할 다운된 멤버입니다.</param>
    /// <returns>선점했으면 true입니다. 이미 다른 멤버가 맡고 있으면 false입니다.</returns>
    /// <remarks>
    /// 이미 자기가 들고 있으면 true를 돌려줍니다. 매 프레임 다시 불러도 되게 하기 위해서입니다.
    /// 선점자가 죽거나 다운되면 그 선점은 무효이므로 다른 멤버가 가져갈 수 있습니다.
    /// </remarks>
    public bool TryClaimAutoRescue(SquadMemberController claimant, SquadMemberController target)
    {
        if (claimant == null || target == null)
        {
            return false;
        }

        if (m_autoRescuer == claimant)
        {
            m_autoRescueTarget = target;
            return true;
        }

        // 앞선 선점자가 더 이상 구조할 수 있는 상태가 아니면 자리를 비웁니다.
        if (m_autoRescuer != null && (!m_autoRescuer.IsAlive || m_autoRescuer.IsDown))
        {
            m_autoRescuer = null;
            m_autoRescueTarget = null;
        }

        if (m_autoRescuer != null)
        {
            return false;
        }

        m_autoRescuer = claimant;
        m_autoRescueTarget = target;
        return true;
    }

    /// <summary>
    /// 자동 구조 선점을 놓습니다(§16). 자기가 들고 있을 때만 풀립니다.
    /// </summary>
    /// <param name="claimant">선점을 놓는 멤버입니다.</param>
    public void ReleaseAutoRescue(SquadMemberController claimant)
    {
        if (claimant == null || m_autoRescuer != claimant)
        {
            return;
        }

        m_autoRescuer = null;
        m_autoRescueTarget = null;
    }

    /// <summary>
    /// AI 팀원끼리 유지할 간격입니다(§6.1 "신체 겹침이나 길막이 발생하면 다른 유효 위치로 이동한다").
    /// </summary>
    /// <remarks>
    /// <b>멤버가 아니라 스쿼드가 소유합니다.</b> 이것은 "이 캐릭터의 성질"이 아니라 스쿼드 전체에 걸리는
    /// 편성 규칙이고, 멤버마다 사본을 두면 값이 서로 어긋나거나 나중에 합류한 멤버만 옛 값을 씁니다.
    /// 조작 캐릭터를 전환해도 값이 그대로여야 한다는 요구와도 맞습니다.
    /// </remarks>
    public float AiMemberSpacing
    {
        get => m_aiMemberSpacing;
        set => m_aiMemberSpacing = Mathf.Max(0.0f, value);
    }

    /// <summary>
    /// 팀 AI의 발사 허용 여부입니다.
    /// </summary>
    /// <remarks>
    /// <b>스쿼드 전체 설정입니다.</b> 끄면 AI가 조준과 대상 선정은 계속하되 발사와 재장전만 멈춥니다.
    /// <para>
    /// <b>조작 중인 캐릭터는 영향을 받지 않습니다.</b> 사람의 사격은 <see cref="AimController"/>와
    /// <see cref="Gun"/>을 거치고, 이 값은 <see cref="SquadAIController"/>만 읽는데 그 컴포넌트는
    /// 조작 멤버에서 꺼져 있기 때문입니다. 그래서 "끄면 플레이어는 쏠 수 있고 팀 AI만 못 쏜다"가 됩니다.
    /// </para>
    /// <para>
    /// 값을 멤버가 아니라 여기 두는 이유는 전환 때문입니다. 멤버마다 사본을 들고 있으면 전환으로
    /// 역할이 바뀔 때마다 누가 옛 값을 쥐고 있는지가 갈리고, 새로 합류하거나 구조로 돌아온 멤버는
    /// 프리팹 기본값을 그대로 씁니다.
    /// </para>
    /// </remarks>
    public bool AiFiringAllowed
    {
        get => m_aiFiringAllowed;
        set => m_aiFiringAllowed = value;
    }

    /// <summary>
    /// AI가 가려는 목적지를 찜해 둡니다(§6.1, §6.3).
    /// </summary>
    /// <param name="member">목적지를 정한 멤버입니다.</param>
    /// <param name="destination">그 멤버가 가려는 자리입니다.</param>
    /// <remarks>
    /// <b>이 대장이 없으면 두 AI가 같은 자리로 걸어갑니다.</b> 각자 "그 자리가 비었는가"를 다른 멤버의
    /// <b>현재 위치</b>로만 판정하는데, 둘 다 아직 멀리서 오는 중이면 그 자리는 실제로 비어 있어
    /// 양쪽 다 통과합니다. 도착하고 나서야 겹침이 드러나고 그때는 이미 붙어 있습니다.
    /// 실측에서 두 AI의 목적지가 0.31m 떨어져 몸이 맞닿았습니다(캡슐 반지름 각 0.30m).
    /// <para>
    /// 내가 어디로 갈 작정인지는 나만 알기 때문에, 이 정보는 개별 AI가 아니라 스쿼드가 들고 있어야 합니다.
    /// </para>
    /// </remarks>
    public void ClaimDestination(SquadMemberController member, Vector3 destination)
    {
        if (member == null)
        {
            return;
        }

        m_destinationClaims[member] = destination;
    }

    /// <summary>찜해 둔 목적지를 놓습니다. 제자리를 지키기로 했거나 AI에서 벗어날 때 부릅니다.</summary>
    /// <param name="member">놓는 멤버입니다.</param>
    public void ReleaseDestination(SquadMemberController member)
    {
        if (member == null)
        {
            return;
        }

        m_destinationClaims.Remove(member);
    }

    /// <summary>
    /// 그 자리를 다른 멤버가 이미 찜했는지 확인합니다(§6.3 "다른 캐릭터와 겹치는 위치는 제외한다").
    /// </summary>
    /// <param name="asker">묻는 멤버입니다. 자기 찜은 걸림돌로 보지 않습니다.</param>
    /// <param name="position">확인할 자리입니다.</param>
    /// <param name="clearance">이 거리 안이면 겹친 것으로 봅니다.</param>
    /// <returns>다른 멤버의 찜과 겹치면 true입니다.</returns>
    /// <remarks>
    /// 죽었거나 다운된 멤버, 그리고 조작 멤버의 찜은 무시합니다. 조작 멤버는 AI 목적지를 잡지 않으며,
    /// 전환으로 조작권이 넘어간 멤버의 옛 찜이 남아 있을 수 있습니다.
    /// </remarks>
    public bool IsDestinationClaimedByOther(SquadMemberController asker, Vector3 position, float clearance)
    {
        if (clearance <= 0.0f)
        {
            return false;
        }

        float clearanceSqr = clearance * clearance;

        foreach (KeyValuePair<SquadMemberController, Vector3> claim in m_destinationClaims)
        {
            SquadMemberController member = claim.Key;
            if (member == null || member == asker || member.IsPlayerSquadMember)
            {
                continue;
            }

            if (!member.IsAlive || member.IsDown)
            {
                continue;
            }

            Vector3 delta = claim.Value - position;
            delta.y = 0.0f;
            if (delta.sqrMagnitude < clearanceSqr)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 입력 모드에 따른 멤버별 상태를 스쿼드 전원에게 적용합니다.
    /// </summary>
    /// <param name="gameplayEnabled">게임플레이 입력을 받으면 true, UI 모드처럼 막으면 false입니다.</param>
    /// <remarks>
    /// <b>조작 멤버에게만 걸면 안 되는 것들만 여기 모읍니다.</b> 입력 게이트와 카메라 잠금은 멤버마다
    /// 따로 있고 컴포넌트가 꺼져도 값이 남습니다. 그래서 걸 때와 풀 때의 조작 멤버가 다르면
    /// 한쪽이 막힌 채 남고, 나중에 그 멤버로 전환하면 사격도 시점 회전도 죽습니다(실제 보고된 결함).
    /// <para>
    /// 조작 멤버가 바뀌는 경로는 입력만이 아닙니다. 다운 강제 전환은 체력 이벤트로 일어나 UI가 열린
    /// 중에도 발생합니다. 그래서 "지금 조작 멤버"를 기준으로 거는 방식은 원리적으로 새 나갑니다.
    /// </para>
    /// <para>
    /// 커서는 화면에 하나뿐이라 여기서 다루지 않습니다. 그쪽은 모드 소유자가 조작 멤버 하나에 겁니다.
    /// </para>
    /// </remarks>
    public void ApplyInputModeToSquad(bool gameplayEnabled)
    {
        if (m_squadMembers == null)
        {
            return;
        }

        for (int i = 0; i < m_squadMembers.Count; i++)
        {
            SquadMemberController member = m_squadMembers[i];
            if (member == null)
            {
                continue;
            }

            PlayerInputController input = member.GetComponent<PlayerInputController>();
            if (input != null)
            {
                input.SetInputGate(gameplayEnabled);
            }

            ThirdPersonController controller = member.GetComponent<ThirdPersonController>();
            if (controller != null)
            {
                controller.SetLockCameraPosition(!gameplayEnabled);
            }

            if (!gameplayEnabled)
            {
                AimController aim = member.GetComponent<AimController>();
                if (aim != null)
                {
                    aim.ForceStopAim();
                }
            }
        }
    }

    /// <summary>
    /// 지정한 인덱스의 스쿼드 멤버가 조작 대상으로 전환 가능한지 확인합니다.
    /// </summary>
    /// <param name="index">검사할 스쿼드 멤버 인덱스입니다.</param>
    /// <returns>전환 가능하면 true입니다.</returns>
    private bool CanSwitchTo(int index)
    {
        if (m_squadMembers == null || index < 0 || index >= m_squadMembers.Count)
        {
            return false;
        }

        SquadMemberController member = m_squadMembers[index];

        if (member == null)
        {
            return false;
        }

        if (!member.IsAlive)
        {
            return false;
        }

        if (member.IsDown)
        {
            return false;
        }

        // 자동 구조 중인 AI 슬롯은 전환 대상에서 제외합니다(§16 "구조 중인 AI 조작 슬롯은 일반 캐릭터
        // 전환 대상으로 선택할 수 없다"). 전환하면 조작권이 넘어오며 AI 판단이 지워져 구조가 끊깁니다.
        //
        // 다만 다운 강제 전환에서 이 멤버가 마지막 후보라면 막을 수 없습니다. 그러면 조작할 캐릭터가
        // 없어 게임오버가 되는데, 구조를 지키자고 게임을 끝내는 것은 뒤집힌 우선순위입니다.
        if (m_autoRescuer == member && HasOtherSwitchableMember(index))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 지정한 인덱스 말고 조작 가능한 멤버가 또 있는지 확인합니다.
    /// </summary>
    /// <param name="excludedIndex">제외할 인덱스입니다.</param>
    /// <returns>다른 후보가 있으면 true입니다.</returns>
    /// <remarks>
    /// 구조 중인 멤버를 전환 후보에서 빼도 되는지 판단하는 데 씁니다. 재귀를 피하려고
    /// <see cref="CanSwitchTo"/>가 아니라 같은 조건을 직접 봅니다(구조 제외 조건만 뺀 것입니다).
    /// </remarks>
    private bool HasOtherSwitchableMember(int excludedIndex)
    {
        if (m_squadMembers == null)
        {
            return false;
        }

        for (int i = 0; i < m_squadMembers.Count; i++)
        {
            if (i == excludedIndex)
            {
                continue;
            }

            SquadMemberController member = m_squadMembers[i];
            if (member != null && member.IsAlive && !member.IsDown)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 시작 시 스쿼드 인덱스 기준으로 각 멤버의 역할별 초기 프리셋을 적용합니다.
    /// </summary>
    private void ApplyInitialMemberRolePresets()
    {
        if (m_squadMembers == null)
        {
            return;
        }

        for (int i = 0; i < m_squadMembers.Count; i++)
        {
            if (m_squadMembers[i] == null)
            {
                continue;
            }

            ApplyInitialRolePreset(m_squadMembers[i], i == m_playerSquadMemberIndex);
        }
    }

    /// <summary>
    /// 필드 씬에서 새 플레이어블 캐릭터를 만든 직후 호출할 역할별 초기 프리셋 진입점입니다.
    /// </summary>
    /// <remarks>
    /// 이 메서드는 스쿼드 목록 등록을 대신하지 않습니다. 생성 시스템은 멤버를 목록에 등록한 뒤,
    /// 해당 멤버의 첫 역할에 맞춰 이 메서드를 호출합니다.
    /// </remarks>
    /// <param name="member">초기화할 플레이어블 스쿼드 멤버입니다.</param>
    /// <param name="isPlayerSquadMember">첫 프레임부터 직접 조작할 멤버이면 true입니다.</param>
    public void ApplyInitialRolePreset(SquadMemberController member, bool isPlayerSquadMember)
    {
        if (member == null)
        {
            return;
        }

        if (isPlayerSquadMember)
        {
            member.ApplyPlayerInitialSetup();
        }
        else
        {
            member.ApplyAiInitialSetup();
        }
    }

    /// <summary>
    /// 모든 스쿼드 멤버의 입력, AI, 애니메이션 상태를 현재 조작 상태에 맞게 강제로 갱신합니다.
    /// </summary>
    private void ForceRefreshMembers()
    {
        if (m_squadMembers == null)
        {
            return;
        }

        for (int i = 0; i < m_squadMembers.Count; i++)
        {
            if (m_squadMembers[i] == null)
            {
                continue;
            }

            m_squadMembers[i].ForceRefreshState();
        }
    }

    /// <summary>
    /// 현재 조작 중인 멤버의 무기 탄약 UI를 현재 무기 상태로 갱신합니다.
    /// </summary>
    private void RefreshPlayerSquadMemberWeaponUI()
    {
        if (PlayerSquadMember != null)
        {
            PlayerSquadMember.RefreshWeaponUI();
        }
    }

    /// <summary>
    /// 스쿼드 멤버 전환 시 이전 멤버와 다음 멤버의 월드 위치와 회전을 맞바꿉니다.
    /// </summary>
    /// <param name="previousMember">전환 전 조작 중이던 스쿼드 멤버입니다.</param>
    /// <param name="nextMember">전환 후 조작할 스쿼드 멤버입니다.</param>
    private void ApplyMemberTransformSwap(SquadMemberController previousMember, SquadMemberController nextMember)
    {
        if (previousMember == null || nextMember == null)
        {
            return;
        }

        Transform previousTransform = previousMember.transform;
        Transform nextTransform = nextMember.transform;
        Transform previousCameraTarget = previousMember.CameraTarget;

        Vector3 previousPosition = previousTransform.position;
        Quaternion previousRotation = previousTransform.rotation;
        Quaternion previousCameraTargetRotation = previousCameraTarget.rotation;

        Vector3 nextPosition = nextTransform.position;
        Quaternion nextRotation = nextTransform.rotation;

        // 옮기는 동안에는 두 CharacterController를 모두 끕니다.
        // 한 명씩 끝까지 옮기면 먼저 옮긴 멤버가 아직 자리를 비우지 않은 상대 위에 겹쳐 놓이므로,
        // 둘 다 끈 뒤에 위치를 정하고 자리를 다 잡은 다음에 함께 켭니다.
        // 옮긴 위치가 물리 엔진에 반영되는 것은 이 함수가 아니라 SyncPhysicsAfterMemberTeleport()가 책임집니다.
        CharacterController previousController = previousMember.GetComponent<CharacterController>();
        CharacterController nextController = nextMember.GetComponent<CharacterController>();

        bool previousControllerWasEnabled = previousController != null && previousController.enabled;
        bool nextControllerWasEnabled = nextController != null && nextController.enabled;

        if (previousControllerWasEnabled)
        {
            previousController.enabled = false;
        }

        if (nextControllerWasEnabled)
        {
            nextController.enabled = false;
        }

        PlaceMember(previousMember, nextPosition, nextRotation);
        PlaceMember(nextMember, previousPosition, previousRotation);

        if (previousControllerWasEnabled)
        {
            previousController.enabled = true;
        }

        if (nextControllerWasEnabled)
        {
            nextController.enabled = true;
        }

        nextMember.SyncCameraTargetRotation(previousCameraTargetRotation);

        static void PlaceMember(SquadMemberController member, Vector3 position, Quaternion rotation)
        {
            Transform memberTransform = member.transform;
            UnityEngine.AI.NavMeshAgent navMeshAgent = member.GetComponent<UnityEngine.AI.NavMeshAgent>();

            if (navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.Warp(position);
                navMeshAgent.ResetPath();
                memberTransform.rotation = rotation;
            }
            else
            {
                memberTransform.SetPositionAndRotation(position, rotation);
            }
        }
    }

    /// <summary>
    /// 멤버를 스크립트로 순간이동시킨 직후 물리 엔진의 콜라이더 위치를 Transform과 강제로 맞춥니다.
    /// </summary>
    /// <remarks>
    /// 이 프로젝트는 `Physics.autoSyncTransforms`가 꺼져 있어, 스크립트로 옮긴 Transform은 다음 FixedUpdate까지
    /// 물리 엔진에 반영되지 않습니다. 그 상태로 같은 프레임의 <see cref="CharacterController.Move"/>가 돌면
    /// 상대 멤버의 캡슐이 아직 옛 위치(= 방금 이 멤버가 놓인 자리)에 있는 것으로 보여, 겹침 해소가
    /// 정확히 `반지름 + 상대 반지름 + skinWidth`(0.58m)만큼 캐릭터를 밀어냅니다. 두 캡슐이 완전히 겹친
    /// 상태라 밀리는 방향도 월드 +X로 고정됩니다. 스왑과 지면 보정이 모두 끝난 뒤 한 번 동기화하면
    /// 그 한 프레임 팝과, 팝이 스윕 없는 위치 보정이라 벽을 지나쳐 생기던 건물 관통이 함께 사라집니다.
    /// 순간이동이 끝난 지점에서만 부르십시오. 옮기는 중간에 부르면 같은 문제가 다시 생깁니다.
    /// </remarks>
    private static void SyncPhysicsAfterMemberTeleport()
    {
        Physics.SyncTransforms();
    }

    /// <summary>
    /// 멤버 Transform 스왑 직후 Cinemachine의 이전 추적 상태를 초기화해 카메라 보간 이동을 막습니다.
    /// </summary>
    private void CancelCameraDamping()
    {
        if (m_followCamera != null)
        {
            m_followCamera.CancelDamping(true);
        }

        if (m_aimCamera != null)
        {
            m_aimCamera.CancelDamping(true);
        }
    }

    /// <summary>
    /// 멤버 전환 후 여러 시점의 상태를 콘솔에 출력해 공중/낙하 상태가 어느 멤버에 남는지 확인합니다.
    /// </summary>
    /// <param name="previousMember">전환 전 플레이어 조작 멤버입니다.</param>
    /// <param name="nextMember">전환 후 플레이어 조작 멤버입니다.</param>
    private IEnumerator LogSwitchDebug(SquadMemberController previousMember, SquadMemberController nextMember)
    {
        LogSwitchDebugSnapshot("immediate", previousMember, nextMember);

        yield return null;
        LogSwitchDebugSnapshot("next-frame", previousMember, nextMember);

        yield return new WaitForSeconds(0.25f);
        LogSwitchDebugSnapshot("after-0.25s", previousMember, nextMember);

        yield return new WaitForSeconds(0.75f);
        LogSwitchDebugSnapshot("after-1.00s", previousMember, nextMember);
    }

    /// <summary>
    /// 현재 스쿼드 멤버들의 런타임 상태를 한 번 출력합니다.
    /// </summary>
    /// <param name="label">출력 시점 라벨입니다.</param>
    /// <param name="previousMember">전환 전 플레이어 조작 멤버입니다.</param>
    /// <param name="nextMember">전환 후 플레이어 조작 멤버입니다.</param>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void LogSwitchDebugSnapshot(string label, SquadMemberController previousMember, SquadMemberController nextMember)
    {
        string previousName = previousMember != null ? previousMember.name : "null";
        string nextName = nextMember != null ? nextMember.name : "null";
        string playerSquadMemberName = PlayerSquadMember != null ? PlayerSquadMember.name : "null";

        Debug.Log(
            $"[SquadSwitchDebug] {label} previous={previousName} next={nextName} playerSquadMember={playerSquadMemberName} playerSquadMemberIndex={m_playerSquadMemberIndex} swap={m_swapMemberTransforms}",
            this);

        if (m_squadMembers == null)
        {
            return;
        }

        for (int i = 0; i < m_squadMembers.Count; i++)
        {
            SquadMemberController member = m_squadMembers[i];

            if (member == null)
            {
                Debug.Log($"[SquadSwitchDebug] {label} [{i}] null", this);
                continue;
            }

            CharacterController characterController = member.GetComponent<CharacterController>();
            UnityEngine.AI.NavMeshAgent navMeshAgent = member.GetComponent<UnityEngine.AI.NavMeshAgent>();
            ThirdPersonController thirdPersonController = member.GetComponent<ThirdPersonController>();
            Animator animator = member.GetComponent<Animator>();

            bool agentEnabled = navMeshAgent != null && navMeshAgent.enabled;
            bool agentOnNavMesh = agentEnabled && navMeshAgent.isOnNavMesh;
            bool grounded = animator != null && animator.GetBool("IsGrounded");
            bool jump = animator != null && animator.GetBool("IsJump");
            bool freeFall = animator != null && animator.GetBool("IsFreeFall");
            float speed = animator != null ? animator.GetFloat("MoveSpeed") : 0.0f;
            float motionSpeed = animator != null ? animator.GetFloat("MotionSpeed") : 0.0f;
            Vector3 agentVelocity = agentEnabled ? navMeshAgent.velocity : Vector3.zero;

            Debug.Log(
                $"[SquadSwitchDebug] {label} [{i}] name={member.name} playerSquadMember={member.IsPlayerSquadMember} pos={member.transform.position} " +
                $"cc={(characterController != null && characterController.enabled)} tpc={(thirdPersonController != null && thirdPersonController.enabled)} " +
                $"agent={agentEnabled} onNav={agentOnNavMesh} agentVel={agentVelocity} " +
                $"grounded={grounded} jump={jump} freeFall={freeFall} speed={speed:F3} motion={motionSpeed:F3}",
                member);
        }
    }

    /// <summary>
    /// 현재 조작 중인 스쿼드 멤버의 카메라 타겟을 follow/aim 카메라에 반영합니다.
    /// </summary>
    private void UpdateCameraTarget()
    {
        SetCameraTarget(PlayerSquadMember);
    }

    /// <summary>
    /// 현재 PlayerSquadMember의 카메라 충돌 반응만 활성화하고 나머지 멤버의 반응은 비활성화합니다.
    /// </summary>
    private void RefreshCharacterCameraCollisionResponses()
    {
        if (m_squadMembers == null)
        {
            return;
        }

        SquadMemberController playerMember = PlayerSquadMember;

        for (int i = 0; i < m_squadMembers.Count; i++)
        {
            SquadMemberController member = m_squadMembers[i];
            if (member == null || member == playerMember)
            {
                continue;
            }

            CharacterCameraCollisionResponse response =
                member.GetComponent<CharacterCameraCollisionResponse>();
            if (response != null)
            {
                response.enabled = false;
            }
        }

        if (playerMember == null)
        {
            return;
        }

        CharacterCameraCollisionResponse playerResponse =
            playerMember.GetComponent<CharacterCameraCollisionResponse>();
        if (playerResponse != null)
        {
            playerResponse.enabled = true;
        }
    }

    /// <summary>
    /// 지정한 멤버의 카메라 타겟을 follow/aim 카메라에 반영합니다.
    /// </summary>
    /// <param name="member">카메라가 따라갈 멤버입니다.</param>
    /// <remarks>
    /// 조작 멤버가 아닌 대상도 받습니다. 다운 강제 전환은 조작권을 넘기기 <b>전에</b> 카메라부터
    /// 보내야 하기 때문입니다(§15.1 "카메라 이동 중 선택된 슬롯은 기존 AI 판단으로 해당 캐릭터를
    /// 계속 조작한다").
    /// </remarks>
    private void SetCameraTarget(SquadMemberController member)
    {
        if (member == null)
        {
            return;
        }

        Transform target = member.CameraTarget;

        if (target == null)
        {
            return;
        }

        if (m_followCamera != null)
        {
            m_followCamera.Follow = target;
            m_followCamera.LookAt = target;
        }

        if (m_aimCamera != null)
        {
            m_aimCamera.Follow = target;
            m_aimCamera.LookAt = target;
        }
    }

    /// <summary>
    /// 스쿼드 멤버 목록을 새 목록으로 교체합니다.
    /// </summary>
    /// <param name="members">새 스쿼드 멤버 목록입니다.</param>
    public void SetSquadMembers(List<SquadMemberController> members)
    {
        UnsubscribeMemberDeathEvents();
        m_squadMembers = members ?? new List<SquadMemberController>();
        RemoveNullMembers();
        SyncPlayerDataSources();
        NormalizeMemberIndex();
        SubscribeMemberDeathEvents();
        UpdateCameraTarget();
        RefreshCharacterCameraCollisionResponses();
        RefreshPlayerSquadMemberWeaponUI();
    }

    /// <summary>
    /// 현재 조작 멤버 인덱스를 설정합니다.
    /// </summary>
    /// <param name="value">설정할 멤버 인덱스입니다.</param>
    public void SetPlayerSquadMemberIndex(int value)
    {
        if (m_squadMembers == null || m_squadMembers.Count == 0)
        {
            m_playerSquadMemberIndex = 0;
            return;
        }

        SwitchToMember(Mathf.Clamp(value, 0, m_squadMembers.Count - 1));
    }

    /// <summary>
    /// 다음 멤버 전환 키를 설정합니다.
    /// </summary>
    /// <param name="value">새 입력 키입니다.</param>
    public void SetNextMemberKey(Key value)
    {
        m_nextMemberKey = value;
    }

    /// <summary>
    /// 첫 번째 멤버 전환 키를 설정합니다.
    /// </summary>
    /// <param name="value">새 입력 키입니다.</param>
    public void SetMember1Key(Key value)
    {
        m_member1Key = value;
    }

    /// <summary>
    /// 두 번째 멤버 전환 키를 설정합니다.
    /// </summary>
    /// <param name="value">새 입력 키입니다.</param>
    public void SetMember2Key(Key value)
    {
        m_member2Key = value;
    }

    /// <summary>
    /// 세 번째 멤버 전환 키를 설정합니다.
    /// </summary>
    /// <param name="value">새 입력 키입니다.</param>
    public void SetMember3Key(Key value)
    {
        m_member3Key = value;
    }
}
