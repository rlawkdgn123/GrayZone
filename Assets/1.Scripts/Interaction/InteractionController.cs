using System;
using UnityEngine;
using VInspector;

/// <summary>
/// 플레이어 조작 멤버의 상호작용 대상 탐지와 입력(탭/홀드) 처리를 담당하는 컨트롤러입니다.
/// </summary>
/// <remarks>
/// 탐지는 <b>조준 우선 + 스냅 보정</b> 방식입니다: 카메라에서 레이를 쏴 상호작용 대상에 직접 맞으면 그것을 쓰고,
/// 아니면 레이가 멈춘 지점(허공이면 레이 끝점)에서 스냅 반경만큼 오버랩해 가장 가까운 것을 고릅니다.
/// 레이 길이는 별도 값이 아니라 카메라-캐릭터 거리 + 캐릭터 사거리로 파생합니다.
/// 이 컴포넌트는 <b>상호작용 배선만</b> 담당하며, 실제 동작(부활·시설 사용 등)은
/// 각 <see cref="IInteractable"/> 구현체가 자기 도메인 로직으로 수행합니다.
/// <para>
/// <b>선택은 카메라 기준, 거리는 캐릭터 기준</b>으로 갈라져 있습니다. 공용 `인벤토리 시스템` §5.2가 그렇게 정하기
/// 때문입니다(캐릭터가 시작 범위 안 + 카메라 기준으로 선택 + 캐릭터와 적은 판정을 가리지 않음).
/// 그래서 조준으로 잡힌 대상도 캐릭터에서 멀면 탈락합니다.
/// </para>
/// <para>
/// PUBG의 F키 줍기와 같은 모델이며, 그 방식의 알려진 약점(뭉쳐 있는 아이템 중 엉뚱한 것이 잡힘)은 스냅 반경을
/// 작게 두어 억제합니다. 스냅 반경은 줍기 반경이 아니라 <b>조준 오차 허용치</b>입니다. 뭉친 것에서 정확히 고르는
/// 경로는 §11.1의 드래그 앤 드롭이 담당합니다.
/// </para>
/// <para>
/// 조작 멤버 판별은 같은 GameObject의 <see cref="PlayerInputController"/> 활성 여부로 합니다(비조작 멤버는
/// PlayerInputController가 꺼져 있어 탐지/실행을 건너뜁니다).
/// </para>
/// </remarks>
[RequireComponent(typeof(PlayerInputController))]
public class InteractionController : MonoBehaviour
{
    [Foldout("Detection")]
    [Tooltip("상호작용 후보를 탐지할 반경(m)입니다.")]
    [SerializeField] private float m_radius = 2.5f;

    [Tooltip("상호작용 후보 탐지에 사용할 레이어입니다. 비우면 전체를 검사합니다.")]
    [SerializeField] private LayerMask m_interactableLayers = ~0;

    [Tooltip("조준 지점 주변에서 대상을 끌어오는 스냅 보정 반경(m)입니다. 줍기 반경이 아니라 조준 오차 허용치이므로 작게 둡니다. 크게 잡으면 뭉쳐 있는 아이템 중 엉뚱한 것이 잡힙니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_aimSnapRadius = 0.4f;

    [Tooltip("조준과 무관하게 몸 주변에서 대상을 잡아주는 폴백 반경(m)입니다. 0이면 조준한 것만 잡습니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_bodyFallbackRadius;

    [Tooltip("조준 레이를 막지 않는 레이어입니다. 비워 두면 캐릭터·적 계열 기본값을 씁니다. 캐릭터와 적은 상호작용 판정을 가리지 않습니다(공용 인벤토리 시스템 §5.2).")]
    [SerializeField] private LayerMask m_nonBlockingLayers;

    [Tooltip("탐지 원점으로 사용할 Transform입니다. 비우면 자신의 Transform을 사용합니다.")]
    [SerializeField] private Transform m_originOverride;

    [Foldout("Debug")]
    [Tooltip("켜면 캐릭터 거리 게이트·조준 레이·스냅 반경·몸 폴백 반경·현재 대상을 기즈모로 표시합니다. 선택하지 않아도 표시됩니다.")]
    [SerializeField] private bool m_debugDraw = true;

    [Tooltip("켜면 현재 대상 변경과 상호작용 실행을 콘솔에 로그로 남깁니다.")]
    [SerializeField] private bool m_debugLog = false;

    private const int MaxHits = 16;

    /// <summary>
    /// <see cref="m_nonBlockingLayers"/>를 지정하지 않았을 때 조준 레이를 막지 않는 것으로 볼 레이어 이름입니다.
    /// </summary>
    /// <remarks>
    /// 인스펙터 기본값으로 둘 수 없어(LayerMask는 이름을 컴파일 타임에 알 수 없음) 이름으로 해석합니다.
    /// <see cref="NoiseManager"/>가 차폐 레이어에 같은 방식을 쓰며, 새 씬에서 배선을 잊어도 캐릭터가
    /// 상호작용을 가로막지 않게 하려는 의도입니다. 여기에 없는 이름은 조용히 무시합니다.
    /// </remarks>
    private static readonly string[] s_defaultNonBlockingLayerNames =
    {
        "Player", "Enemy", "EnemyHitbox", "PlayerHitbox", "Corpse", "EnemyHitDetect",
    };

    private PlayerInputController m_input;
    private Camera m_camera;
    private readonly Collider[] m_hits = new Collider[MaxHits];
    private readonly RaycastHit[] m_rayHits = new RaycastHit[MaxHits];
    private int m_resolvedNonBlockingMask;

    // 튜토리얼 조건이 상호작용 입력일 때 대상 탐지를 멈춥니다. 조작 멤버가 바뀌어도 유지돼야 해서 정적으로 둡니다.
    private static bool s_targetingSuppressed;

    private IInteractable m_current;
    private bool m_prevPressed;
    private bool m_consumed;
    private bool m_holdStarted;
    private float m_holdTimer;

    // 반복 홀드 대상(IRepeatingHoldInteractable)이 한 바퀴를 끝내고, 뗄 때까지 다음 바퀴를 이어서 채우는 중인지 여부입니다.
    private bool m_repeatingHold;

    /// <summary>현재 상호작용 대상입니다. 없으면 <c>null</c>입니다.</summary>
    public IInteractable Current => m_current;

    /// <summary>현재 홀드 진행도(0~1)입니다. 탭 대상이거나 대상이 없으면 0입니다. 프롬프트/진행바 UI에 사용합니다.</summary>
    public float HoldProgress01 { get; private set; }

    /// <summary>현재 상호작용 대상이 바뀔 때 발생합니다(없어지면 <c>null</c>). UI 프롬프트 갱신에 사용합니다.</summary>
    public event Action<IInteractable> OnCurrentChanged;

    /// <summary>어느 멤버든 상호작용을 실행했을 때 (대상, 실행한 멤버)로 발생합니다. 튜토리얼의 상호작용 완료 조건에 사용합니다.</summary>
    public static event Action<IInteractable, GameObject> OnAnyInteracted;

    /// <summary>상호작용 대상 탐지가 멈춰 있는지 여부입니다.</summary>
    public static bool IsTargetingSuppressed => s_targetingSuppressed;

    /// <summary>
    /// 상호작용 대상 탐지를 멈추거나 다시 켭니다. 모든 멤버에게 같이 적용됩니다.
    /// </summary>
    /// <remarks>
    /// 지금은 멈추는 곳이 없습니다. <see cref="TutorialManager"/>는 "대상을 보고 있으면 대상 우선"으로 바뀌면서
    /// 탐지를 멈추지 않고, 남은 상태를 지우기 위해 false로만 부릅니다.
    /// 멈춰 있는 동안에는 <see cref="Current"/>가 null이므로 프롬프트도 뜨지 않고,
    /// "대상이 없을 때만" 동작하는 방어전 시작 홀드는 조준 방향과 관계없이 동작합니다.
    /// 입력 자체(<see cref="PlayerInputController.Interact"/>)는 막지 않습니다.
    /// </remarks>
    public static void SetTargetingSuppressed(bool suppressed)
    {
        s_targetingSuppressed = suppressed;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticSuppression()
    {
        // 도메인 리로드를 끈 상태에서 Play Mode를 다시 들어가면 이전 값이 남기 때문에 비웁니다.
        s_targetingSuppressed = false;
    }

    private Vector3 Origin =>(m_originOverride != null ? m_originOverride : transform).position;

    private Vector3 Facing => m_camera != null ? m_camera.transform.forward : transform.forward;

    private void Awake()
    {
        m_input = GetComponent<PlayerInputController>();
        m_camera = Camera.main;
        m_resolvedNonBlockingMask = ResolveNonBlockingMask();
    }

    /// <summary>조준 레이를 막지 않을 레이어 마스크를 확정합니다. 지정이 없으면 이름 기본값으로 채웁니다.</summary>
    /// <remarks>
    /// Unity 기본 <c>Ignore Raycast</c> 레이어는 지정 여부와 관계없이 항상 통과시킵니다. 방어 구역 판정 박스
    /// (OnlyTargetZone, 약 24x1x24m 트리거)처럼 적 판정용 볼륨을 이 레이어에 두면 함정 앞에서 레이를 멈추지 않습니다.
    /// 이 레이캐스트는 마스크를 직접 넘기므로 Unity가 기본으로 해 주는 Ignore Raycast 제외가 적용되지 않습니다.
    /// </remarks>
    private int ResolveNonBlockingMask()
    {
        int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
        int ignoreRaycastMask = ignoreRaycastLayer >= 0 ? 1 << ignoreRaycastLayer : 0;

        if (m_nonBlockingLayers.value != 0)
        {
            return m_nonBlockingLayers.value | ignoreRaycastMask;
        }

        int mask = ignoreRaycastMask;
        for (int i = 0; i < s_defaultNonBlockingLayerNames.Length; i++)
        {
            int layer = LayerMask.NameToLayer(s_defaultNonBlockingLayerNames[i]);
            if (layer >= 0)
            {
                mask |= 1 << layer;
            }
        }

        return mask;
    }

    private void OnDisable()
    {
        ResetState();
    }

    private void Update()
    {
        // 조작 멤버(입력 활성)일 때만 상호작용합니다. 그 외에는 상태를 정리합니다.
        if (m_input == null || !m_input.enabled)
        {
            if (m_current != null || m_holdTimer > 0.0f)
            {
                ResetState();
            }
            return;
        }

        // 튜토리얼이 상호작용 입력을 쓰는 동안에는 대상을 잡지 않습니다. 진행 중이던 홀드도 취소됩니다.
        if (s_targetingSuppressed)
        {
            if (m_current != null || m_holdTimer > 0.0f)
            {
                ResetState();
            }
            return;
        }

        if (m_camera == null)
        {
            m_camera = Camera.main;
        }

        SetCurrent(ShouldKeepActiveHoldTarget() ? m_current : FindBest());
        HandleInput();
    }

    private bool ShouldKeepActiveHoldTarget()
    {
        if (!m_holdStarted || m_current == null || !m_current.CanInteract(gameObject))
        {
            return false;
        }

        if (m_current is not Component component || component == null)
        {
            return false;
        }

        // 후보를 고를 때(IsUsableCandidate)와 같은 기준인 콜라이더 표면까지의 거리를 씁니다.
        // 피벗으로 재면 메시가 피벗에서 떨어진 대상(MetalBarrel 등)은 잡혔다가 홀드 도중 풀립니다.
        Collider col = component.GetComponentInChildren<Collider>();
        Vector3 point = col != null ? col.ClosestPoint(Origin) : component.transform.position;
        return (point - Origin).sqrMagnitude <= m_radius * m_radius;
    }

    /// <summary>
    /// 조준 레이 → 스냅 탐색 순서로 현재 시점 최적 상호작용 대상을 찾습니다.
    /// </summary>
    /// <remarks>
    /// <para>순서: ① 카메라에서 레이를 쏴 상호작용 대상에 <b>직접 맞으면 그것</b> ② 대상이 아닌 것에 막히면
    /// <b>막힌 지점</b>에서, 아무것도 맞지 않으면 <b>최대 거리 끝점</b>에서 스냅 반경만큼 오버랩해 가장 가까운 것
    /// ③ 그래도 없고 몸 폴백 반경이 켜져 있으면 몸 주변에서 찾습니다.</para>
    ///
    /// <para>레이를 <c>Collide</c>로 쏘고 거리순으로 훑는 이유: 상호작용 대상은 트리거 콜라이더인 경우가 많아
    /// 트리거를 무시하면 ①이 성립하지 않습니다. 반대로 트리거를 포함해 첫 히트에서 끊으면 아무 트리거 볼륨에
    /// 막힙니다. 그래서 히트를 정렬해 훑으며 "상호작용 대상이면 채택, 통과 레이어(캐릭터·적·Ignore Raycast)는 통과,
    /// 나머지는 차폐물로 보고 중단"으로 가릅니다.
    /// 이 방식은 레이어 마스크에 의존하지 않아 구조물 레이어가 정리되지 않은 현재 상태에서도 동작합니다.</para>
    ///
    /// <para>거리·근접 판정에 <see cref="Collider.ClosestPoint"/>를 쓰는 이유: 피벗으로 재면 인벤토리 박스나 문처럼
    /// 큰 오브젝트에서 콜라이더가 코앞인데 피벗이 멀어 탈락합니다.</para>
    /// </remarks>
    /// <returns>후보가 없으면 <c>null</c>입니다.</returns>
    private IInteractable FindBest()
    {
        Vector3 origin = Origin;
        Vector3 aimOrigin = m_camera != null ? m_camera.transform.position : origin;
        Vector3 facing = Facing;

        if (TryRaycastAim(aimOrigin, facing, GetAimRayLength(aimOrigin, origin), out IInteractable aimed, out Vector3 aimPoint))
        {
            // ① 레이가 상호작용 대상에 직접 맞았습니다.
            if (IsUsableCandidate(aimed, origin, out _))
            {
                return aimed;
            }
        }

        // ② 막힌 지점 또는 최대 거리 끝점에서 스냅 탐색.
        IInteractable best = FindNearestAround(aimPoint, m_aimSnapRadius, origin);
        if (best != null)
        {
            return best;
        }

        // ③ 조준과 무관한 몸 주변 폴백(0이면 사용하지 않습니다).
        return m_bodyFallbackRadius > 0.0f
            ? FindNearestAround(origin, m_bodyFallbackRadius, origin)
            : null;
    }

    /// <summary>
    /// 조준 레이의 길이를 캐릭터 사거리에서 파생합니다.
    /// </summary>
    /// <remarks>
    /// <b>별도의 조준 거리 값을 두지 않습니다.</b> 3인칭 카메라는 캐릭터 뒤(이 프로젝트에서는 약 2.4m)에 있어서,
    /// 카메라 기준 고정 거리를 쓰면 붐 길이가 예산을 먹고 <b>캐릭터 코앞의 대상에 레이가 닿지 못합니다.</b>
    /// 실제로 그 버그를 겪었습니다(카메라→대상 4.21m, 고정 거리 3m, 그러나 캐릭터→대상은 1.80m).
    ///
    /// 사거리의 정본은 <see cref="m_radius"/> 하나뿐이어야 합니다. 그래서 레이는 "카메라에서 캐릭터까지 + 캐릭터 사거리"만큼
    /// 쏩니다. 이러면 레이가 거리 게이트보다 멀리 나가지 않고, 카메라가 얼마나 뒤로 빠지든 자동으로 보정됩니다.
    /// </remarks>
    private float GetAimRayLength(Vector3 aimOrigin, Vector3 characterOrigin)
    {
        return Vector3.Distance(aimOrigin, characterOrigin) + m_radius;
    }

    /// <summary>
    /// 조준 레이를 쏘고, 직접 맞은 상호작용 대상과 스냅 탐색에 쓸 지점을 함께 돌려줍니다.
    /// </summary>
    /// <param name="aimed">레이에 직접 맞은 상호작용 대상입니다. 없으면 <c>null</c>입니다.</param>
    /// <param name="aimPoint">
    /// 스냅 탐색의 중심입니다. 차폐물에 막혔으면 그 지점, 아무것도 맞지 않았으면 최대 거리 끝점입니다.
    /// </param>
    /// <returns>직접 맞은 대상이 있으면 <c>true</c>입니다.</returns>
    private bool TryRaycastAim(Vector3 aimOrigin, Vector3 direction, float rayLength, out IInteractable aimed, out Vector3 aimPoint)
    {
        aimed = null;
        aimPoint = aimOrigin + direction * rayLength;

        int count = Physics.RaycastNonAlloc(
            aimOrigin,
            direction,
            m_rayHits,
            rayLength,
            m_interactableLayers,
            QueryTriggerInteraction.Collide);

        if (count <= 0)
        {
            return false;
        }

        // RaycastNonAlloc은 거리순을 보장하지 않으므로 직접 정렬합니다.
        Array.Sort(m_rayHits, 0, count, RaycastDistanceComparer.Instance);

        for (int i = 0; i < count; i++)
        {
            Collider col = m_rayHits[i].collider;
            if (col == null || col.transform.IsChildOf(transform))
            {
                continue;
            }

            IInteractable interactable = col.GetComponentInParent<IInteractable>();

            // 지금 쓸 수 없는 대상이 감지 범위(트리거)이거나 캐릭터 몸(통과 레이어)이면 통과시킵니다.
            // 팀원은 몸과 구조 감지 구에 "쓰러진 동료 구조" 대상이 붙어 있어, 쓰러지지 않았어도 레이가 거기서 멈췄습니다.
            // 그러면 앞에 선 팀원에 가려 그 뒤의 함정을 잡지 못합니다. 나린을 조작할 때는 팀원이 뒤따라와 가려지지 않고,
            // 다른 멤버를 조작하면 앞의 나린에게 가려져 "나린만 설치된다"로 보였습니다.
            bool passThroughBody = col.isTrigger || (m_resolvedNonBlockingMask & (1 << col.gameObject.layer)) != 0;
            if (interactable != null && passThroughBody && !interactable.CanInteract(gameObject))
            {
                continue;
            }

            if (interactable != null && interactable is Component comp && comp.gameObject != gameObject)
            {
                aimed = interactable;
                aimPoint = m_rayHits[i].point;
                return true;
            }

            // 상호작용 대상이 아닙니다. 캐릭터·적은 판정을 가리지 않으므로 통과시킵니다(§5.2).
            if ((m_resolvedNonBlockingMask & (1 << col.gameObject.layer)) != 0)
            {
                continue;
            }

            // 차폐물입니다. 레이는 여기서 멈추고 이 지점이 스냅 탐색 중심이 됩니다.
            aimPoint = m_rayHits[i].point;
            return false;
        }

        return false;
    }

    /// <summary>지정한 지점 주변에서 상호작용 가능한 가장 가까운 대상을 찾습니다.</summary>
    private IInteractable FindNearestAround(Vector3 center, float radius, Vector3 distanceOrigin)
    {
        if (radius <= 0.0f)
        {
            return null;
        }

        int count = Physics.OverlapSphereNonAlloc(
            center,
            radius,
            m_hits,
            m_interactableLayers,
            QueryTriggerInteraction.Collide);

        IInteractable best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Collider col = m_hits[i];
            if (col == null || col.transform.IsChildOf(transform))
            {
                continue;
            }

            IInteractable interactable = col.GetComponentInParent<IInteractable>();
            if (!IsUsableCandidate(interactable, distanceOrigin, out _))
            {
                continue;
            }

            // 피벗이 아니라 콜라이더 표면까지의 거리로 고릅니다.
            float distance = Vector3.Distance(col.ClosestPoint(center), center);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = interactable;
            }
        }

        return best;
    }

    /// <summary>
    /// 후보가 실제로 쓸 수 있는 대상인지 확인합니다. 캐릭터 기준 거리 게이트가 여기 있습니다.
    /// </summary>
    /// <remarks>
    /// 선택은 카메라 기준이지만 <b>거리는 조작 캐릭터 기준</b>이라는 공용 `인벤토리 시스템` §5.2의 분리를 지킵니다.
    /// 그래서 조준으로 잡혔더라도 캐릭터가 <see cref="m_radius"/> 밖이면 탈락합니다.
    /// </remarks>
    private bool IsUsableCandidate(IInteractable interactable, Vector3 distanceOrigin, out float distance)
    {
        distance = float.MaxValue;

        if (interactable is not Component comp || comp == null || comp.gameObject == gameObject)
        {
            return false;
        }

        if (!interactable.CanInteract(gameObject))
        {
            return false;
        }

        Collider col = comp.GetComponentInChildren<Collider>();
        Vector3 point = col != null ? col.ClosestPoint(distanceOrigin) : comp.transform.position;
        distance = Vector3.Distance(point, distanceOrigin);

        return distance <= m_radius;
    }

    /// <summary>레이 히트를 거리순으로 정렬하기 위한 비교자입니다.</summary>
    private sealed class RaycastDistanceComparer : System.Collections.Generic.IComparer<RaycastHit>
    {
        public static readonly RaycastDistanceComparer Instance = new RaycastDistanceComparer();

        public int Compare(RaycastHit a, RaycastHit b)
        {
            return a.distance.CompareTo(b.distance);
        }
    }

    /// <summary>
    /// 현재 대상을 갱신하고, 바뀌었으면 홀드 상태를 초기화하며 이벤트를 발생시킵니다.
    /// </summary>
    /// <param name="next">이번 프레임에 선택된 대상입니다.</param>
    private void SetCurrent(IInteractable next)
    {
        if (ReferenceEquals(next, m_current))
        {
            return;
        }

        CancelActiveHold();

        m_current = next;
        m_holdTimer = 0.0f;
        HoldProgress01 = 0.0f;
        m_repeatingHold = false;
        // m_consumed는 여기서 리셋하지 않는다. 한 번 누름으로 실행(예: 구조 완료)한 뒤 버튼을 계속 누른 채
        // 다음 대상으로 옮겨가면 자동으로 다시 채워지는 문제를 막기 위해, 버튼을 뗄 때(!pressed)만 리셋한다.
        OnCurrentChanged?.Invoke(m_current);

        if (m_debugLog)
        {
            Debug.Log($"[Interaction] focus → {Describe(m_current)}", this);
        }
    }

    /// <summary>
    /// 상호작용 입력의 탭/홀드 타이밍을 처리하고 조건 충족 시 실행합니다.
    /// </summary>
    private void HandleInput()
    {
        bool pressed = m_input.Interact;
        bool justPressed = pressed && !m_prevPressed;
        m_prevPressed = pressed;

        if (m_current == null)
        {
            CancelActiveHold();
            m_holdTimer = 0.0f;
            HoldProgress01 = 0.0f;
            m_repeatingHold = false;
            if (!pressed)
            {
                m_consumed = false;
            }
            return;
        }

        // 떼면 홀드 상태와 1회 소비 플래그를 초기화합니다.
        if (!pressed)
        {
            CancelActiveHold();
            m_holdTimer = 0.0f;
            HoldProgress01 = 0.0f;
            m_consumed = false;
            m_repeatingHold = false;
            return;
        }

        // 한 번 실행한 누름은 뗄 때까지 재실행하지 않습니다(연타/홀드 반복 방지).
        if (m_consumed)
        {
            return;
        }

        // 대상은 잡혔지만 실행 조건(자원 등)이 모자라면 게이지를 채우지 않습니다. 다 찼는데 아무 일도 없으면 고장으로 보입니다.
        if (!MeetsRequirement(m_current))
        {
            CancelActiveHold();
            m_holdTimer = 0.0f;
            HoldProgress01 = 0.0f;
            m_repeatingHold = false;
            return;
        }

        float hold = Mathf.Max(0.0f, m_current.HoldDuration);

        if (hold <= 0.0f)
        {
            // 탭: 이번 프레임에 새로 눌렸을 때만 실행합니다.
            if (justPressed)
            {
                m_consumed = Execute();
            }
            return;
        }

        // 홀드: 누르고 있는 동안 누적하고, 목표 시간을 채우면 실행합니다.
        if (m_holdTimer <= 0.0f && !m_holdStarted && !justPressed && !m_repeatingHold)
        {
            return;
        }

        BeginActiveHold();
        m_holdTimer += Time.deltaTime;
        HoldProgress01 = Mathf.Clamp01(m_holdTimer / hold);
        UpdateActiveHold();

        if (m_holdTimer >= hold)
        {
            // Execute가 대상의 상태를 바꾸므로(예: 수리로 체력이 다 참) 반복 여부는 실행 전에 읽어 둡니다.
            bool repeat = m_current is IRepeatingHoldInteractable repeating && repeating.RepeatWhileHeld;
            bool executed = Execute();
            if (executed && repeat)
            {
                // 한 바퀴를 끝내고 게이지를 비운 뒤, 누르고 있으면 다음 프레임부터 다시 채웁니다.
                CompleteActiveHold();
                m_holdTimer = 0.0f;
                HoldProgress01 = 0.0f;
                m_repeatingHold = true;
            }
            else if (executed)
            {
                m_consumed = true;
                HoldProgress01 = 1.0f;
                CompleteActiveHold();
            }
            else
            {
                CancelActiveHold();
                HoldProgress01 = 0.0f;
            }
        }
    }

    /// <summary>
    /// 실행 직전 조건을 재확인하고 상호작용을 실행합니다.
    /// </summary>
    private bool Execute()
    {
        if (m_current == null || !m_current.CanInteract(gameObject) || !MeetsRequirement(m_current))
        {
            return false;
        }

        IInteractable target = m_current;
        target.Interact(gameObject);
        OnAnyInteracted?.Invoke(target, gameObject);

        if (m_debugLog)
        {
            Debug.Log($"[Interaction] interact ✔ {Describe(target)} (by {gameObject.name})", this);
        }

        return true;
    }

    /// <summary>대상의 별도 실행 조건(<see cref="IInteractionRequirement"/>)을 만족하는지 여부입니다. 조건이 없으면 항상 참입니다.</summary>
    private bool MeetsRequirement(IInteractable target)
    {
        return target is not IInteractionRequirement requirement
            || requirement.MeetsInteractionRequirement(gameObject);
    }

    private void BeginActiveHold()
    {
        if (m_holdStarted || m_current is not IHoldInteractable holdInteractable)
        {
            return;
        }

        m_holdStarted = true;
        holdInteractable.BeginHold(gameObject);
    }

    private void UpdateActiveHold()
    {
        if (!m_holdStarted || m_current is not IHoldInteractable holdInteractable)
        {
            return;
        }

        holdInteractable.UpdateHold(gameObject, HoldProgress01);
    }

    private void CancelActiveHold()
    {
        if (!m_holdStarted || m_current is not IHoldInteractable holdInteractable)
        {
            m_holdStarted = false;
            return;
        }

        holdInteractable.CancelHold(gameObject);
        m_holdStarted = false;
    }

    private void CompleteActiveHold()
    {
        if (!m_holdStarted || m_current is not IHoldInteractable holdInteractable)
        {
            m_holdStarted = false;
            return;
        }

        holdInteractable.CompleteHold(gameObject);
        m_holdStarted = false;
    }

    /// <summary>
    /// 디버그 로그용으로 상호작용 대상을 사람이 읽기 좋은 문자열로 만듭니다.
    /// </summary>
    /// <param name="interactable">설명할 대상입니다. <c>null</c>이면 "none"을 반환합니다.</param>
    /// <returns>대상 GameObject 이름과 프롬프트를 담은 설명 문자열입니다.</returns>
    private static string Describe(IInteractable interactable)
    {
        if (interactable == null)
        {
            return "none";
        }

        string name = interactable is Component comp && comp != null ? comp.gameObject.name : interactable.GetType().Name;
        return $"{name} (\"{interactable.GetPrompt()}\", hold={interactable.HoldDuration:0.##}s)";
    }

    /// <summary>
    /// 현재 대상과 홀드/입력 상태를 초기화합니다.
    /// </summary>
    private void ResetState()
    {
        CancelActiveHold();

        if (m_current != null)
        {
            m_current = null;
            OnCurrentChanged?.Invoke(null);
        }

        m_holdTimer = 0.0f;
        HoldProgress01 = 0.0f;
        m_prevPressed = false;
        m_consumed = false;
        m_repeatingHold = false;
    }

    private void OnDrawGizmos()
    {
        if (!m_debugDraw)
        {
            return;
        }

        Vector3 origin = Origin;
        Vector3 aimOrigin = m_camera != null ? m_camera.transform.position : origin;
        Vector3 facing = Facing;

        // 캐릭터 기준 거리 게이트(§5.2). 이 밖의 대상은 조준해도 탈락합니다.
        Gizmos.color = new Color(0.0f, 1.0f, 1.0f, 0.25f);
        Gizmos.DrawWireSphere(origin, m_radius);

        // 조준 레이와 그 끝점.
        Vector3 endPoint = aimOrigin + facing * GetAimRayLength(aimOrigin, origin);
        Gizmos.color = new Color(1.0f, 0.85f, 0.2f, 0.9f);
        Gizmos.DrawLine(aimOrigin, endPoint);

        // 스냅 보정 반경. 레이가 막히면 그 지점으로 옮겨가지만, 기즈모는 최대 거리 끝점에 그립니다.
        Gizmos.color = new Color(1.0f, 0.85f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(endPoint, m_aimSnapRadius);

        // 몸 폴백 반경(0이면 사용하지 않습니다).
        if (m_bodyFallbackRadius > 0.0f)
        {
            Gizmos.color = new Color(0.6f, 0.6f, 1.0f, 0.35f);
            Gizmos.DrawWireSphere(origin, m_bodyFallbackRadius);
        }

        // 현재 대상 강조.
        if (m_current is Component comp && comp != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(origin, comp.transform.position);
            Gizmos.DrawWireSphere(comp.transform.position, 0.25f);
        }
    }

}
