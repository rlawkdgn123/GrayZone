using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 조작 중인 캐릭터의 발밑에 목표 지점으로 가는 길의 방향을 화살표로 보여 줍니다.
/// </summary>
/// <remarks>
/// 목표를 향한 직선 방향이 아니라 NavMesh 경로를 따라간 방향을 가리킵니다. 벽 너머 목표를 직선으로 가리키면
/// 플레이어가 벽으로 걸어가기 때문입니다. 경로는 <see cref="NavMesh.CalculatePath(Vector3, Vector3, NavMeshQueryFilter, NavMeshPath)"/>로
/// 일정 간격마다 다시 계산하고, 캐릭터에서 경로를 따라 <see cref="m_lookAheadDistance"/>만큼 간 지점을 향합니다.
/// 첫 꺾임점만 보면 꺾임점 바로 앞에서 방향이 갑자기 바뀌기 때문입니다.
///
/// 캐릭터를 바꾸면 <see cref="SquadManager.PlayerSquadMember"/>를 따라 새 캐릭터 발밑으로 옮겨 갑니다.
/// 화살표 모양(메시, 데칼, 스프라이트)은 <see cref="m_arrowVisual"/>에 연결한 오브젝트가 맡습니다. 그 오브젝트의
/// 정면(파란 축)이 화살표 끝을 향하게 둡니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class ObjectivePathArrow : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("길을 안내할 목표입니다. 실행 중에는 SetTarget으로 바꿉니다.")]
    [SerializeField] private Transform m_target;

    [Tooltip("목표 Transform에서 실제로 길을 찾을 위치를 보정합니다.")]
    [SerializeField] private Vector3 m_targetOffset;

    [Tooltip("테스트용 초기 표시 상태입니다. 실제 기능에서는 SetVisible로 제어합니다.")]
    [SerializeField] private bool m_visibleOnStart;

    [Header("Origin")]
    [Tooltip("화살표를 띄울 캐릭터입니다. 비워 두면 SquadManager의 조작 캐릭터를 따라갑니다.")]
    [SerializeField] private Transform m_originOverride;

    [Header("View")]
    [Tooltip("회전시키고 옮길 화살표 오브젝트입니다. 정면(파란 축)이 화살표 끝을 향해야 합니다. " +
             "이 컴포넌트가 붙은 오브젝트 자신은 넣지 않습니다(숨길 때 같이 꺼집니다). 비워 두면 경로 선만 그립니다.")]
    [SerializeField] private Transform m_arrowVisual;

    [Tooltip("캐릭터 중심에서 화살표까지의 수평 거리(m)입니다. 0이면 발밑 한가운데에 둡니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_ringRadius = 0.8f;

    [Tooltip("바닥에 묻히지 않도록 캐릭터 발 위치에서 띄울 높이(m)입니다.")]
    [SerializeField] private float m_heightOffset = 0.05f;

    [Tooltip("화살표가 새 방향으로 도는 최대 속도(도/초)입니다.")]
    [Min(1.0f)]
    [SerializeField] private float m_turnSpeed = 540.0f;

    [Tooltip("목표까지 남은 경로 길이(m)가 이보다 짧으면 화살표를 숨깁니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_hideDistance = 3.0f;

    [Header("Path")]
    [Tooltip("경로를 찾고 선 높이를 맞출 NavMesh의 Agent Type 이름입니다. 적·팀원 AI가 쓰는 Humanoid와 따로, 장애물 윗면을 빼고 " +
             "Height Mesh를 켜서 구운 안내 전용 NavMesh를 씁니다. 이 이름이 없으면 Humanoid NavMesh를 씁니다.")]
    [SerializeField] private string m_agentTypeName = "PathGuide";

    [Tooltip("캐릭터에서 경로를 따라 이 거리(m)만큼 간 지점을 가리킵니다. 짧으면 꺾임점에서 방향이 급하게 바뀝니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_lookAheadDistance = 2.5f;

    [Tooltip("경로를 다시 계산하는 간격(초)입니다.")]
    [Min(0.02f)]
    [SerializeField] private float m_repathInterval = 0.25f;

    [Tooltip("캐릭터와 목표 위치를 NavMesh 위로 붙일 때 찾아볼 반경(m)입니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_sampleRadius = 2.0f;

    [Tooltip("켜면 목표까지 길이 끊겨 있을 때 갈 수 있는 데까지의 경로를 안내합니다. 끄면 화살표를 숨깁니다.")]
    [SerializeField] private bool m_allowPartialPath = true;

    [Header("Path Line")]
    [Tooltip("켜면 캐릭터에서 목표까지의 경로를 바닥에 선으로 그립니다. 선은 실행 중에 이 오브젝트 아래에 만듭니다.")]
    [SerializeField] private bool m_drawPathLine = true;

    [Tooltip("경로 선의 색입니다.")]
    [SerializeField] private Color m_pathLineColor = Color.red;

    [Tooltip("경로 선의 두께(m)입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_pathLineWidth = 0.12f;

    [Tooltip("경로 선을 바닥에서 띄울 높이(m)입니다.")]
    [SerializeField] private float m_pathLineHeight = 0.1f;

    [Tooltip("바닥 높이를 확인할 간격(m)입니다. 작을수록 굴곡을 잘 따라가지만 확인 횟수가 늘어납니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_groundSampleSpacing = 0.5f;

    [Tooltip("선 점마다 NavMesh 바닥을 찾아볼 반경(m)입니다. 못 찾으면 꺾임점을 이은 높이를 그대로 씁니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_groundSnapRadius = 1.0f;

    /// <summary>경로 계산 결과를 받는 버퍼입니다. 매번 새로 만들지 않으려고 재사용합니다.</summary>
    private NavMeshPath m_path;

    /// <summary>안내 전용 NavMesh로 조회하기 위한 조건입니다. <see cref="m_agentTypeName"/>에서 Awake 때 정합니다.</summary>
    private NavMeshQueryFilter m_queryFilter;

    /// <summary>마지막으로 계산한 경로의 꺾임점입니다. 경로가 없으면 비어 있습니다.</summary>
    private Vector3[] m_corners = new Vector3[0];

    private bool m_hasPath;
    private float m_remainingPathLength;
    private float m_repathTimer;
    private bool m_visibleRequested;

    /// <summary>화살표가 지금 가리키는 수평 각도(도)입니다. 회전을 부드럽게 하려고 프레임 사이에 남겨 둡니다.</summary>
    private float m_currentYaw;
    private bool m_hasYaw;

    private LineRenderer m_pathLine;
    private Material m_pathLineMaterial;

    /// <summary>첫 꺾임점부터 목표까지를 바닥에 붙인 점입니다. 경로를 다시 계산할 때만 새로 만듭니다.</summary>
    private readonly System.Collections.Generic.List<Vector3> m_groundedTail = new System.Collections.Generic.List<Vector3>();

    /// <summary>경로 선에 넘길 점입니다. 캐릭터에서 첫 꺾임점까지의 점 + <see cref="m_groundedTail"/>입니다.</summary>
    private readonly System.Collections.Generic.List<Vector3> m_linePoints = new System.Collections.Generic.List<Vector3>();

    /// <summary>표시할 목표를 바꿉니다. null이면 화살표를 숨깁니다.</summary>
    public void SetTarget(Transform target)
    {
        m_target = target;
        RequestRepath();
    }

    /// <summary>화살표 표시를 켜거나 끕니다.</summary>
    public void SetVisible(bool visible)
    {
        m_visibleRequested = visible;
        if (visible)
        {
            RequestRepath();
        }
        else
        {
            HideAll();
        }
    }

    /// <summary>다음 프레임에 경로를 바로 다시 계산합니다. 길을 막거나 여는 장애물이 바뀌었을 때 부릅니다.</summary>
    public void RequestRepath()
    {
        m_repathTimer = 0.0f;
    }

    private void Awake()
    {
        m_path = new NavMeshPath();
        m_queryFilter = new NavMeshQueryFilter
        {
            agentTypeID = ResolveAgentTypeId(),
            areaMask = NavMesh.AllAreas
        };
        m_visibleRequested = m_visibleOnStart;
        if (m_drawPathLine)
        {
            CreatePathLine();
        }

        HideAll();
    }

    private void OnDisable()
    {
        HideAll();
    }

    private void OnDestroy()
    {
        if (m_pathLineMaterial != null)
        {
            Destroy(m_pathLineMaterial);
        }
    }

    // 캐릭터 이동이 끝난 뒤에 따라 붙어야 화살표와 선이 한 프레임 늦게 끌려오지 않습니다.
    private void LateUpdate()
    {
        Transform origin = ResolveOrigin();
        if (!m_visibleRequested || m_target == null || origin == null)
        {
            HideAll();
            return;
        }

        m_repathTimer -= Time.deltaTime;
        if (m_repathTimer <= 0.0f)
        {
            m_repathTimer = m_repathInterval;
            RecalculatePath(origin.position);
        }

        if (!m_hasPath || m_remainingPathLength <= m_hideDistance ||
            !TryGetLookAheadDirection(origin.position, out Vector3 direction))
        {
            HideAll();
            return;
        }

        UpdatePathLine(origin.position);
        if (m_arrowVisual == null)
        {
            return;
        }

        float targetYaw = Quaternion.LookRotation(direction).eulerAngles.y;
        m_currentYaw = m_hasYaw
            ? Mathf.MoveTowardsAngle(m_currentYaw, targetYaw, m_turnSpeed * Time.deltaTime)
            : targetYaw;
        m_hasYaw = true;

        Quaternion rotation = Quaternion.Euler(0.0f, m_currentYaw, 0.0f);
        Vector3 position = origin.position + rotation * Vector3.forward * m_ringRadius;
        position.y = origin.position.y + m_heightOffset;
        m_arrowVisual.SetPositionAndRotation(position, rotation);
        SetArrowActive(true);
    }

    /// <summary>Agent Type 이름으로 ID를 찾습니다. 없으면 경고를 남기고 기본 Agent Type(Humanoid, 0)을 씁니다.</summary>
    private int ResolveAgentTypeId()
    {
        for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
        {
            int id = NavMesh.GetSettingsByIndex(i).agentTypeID;
            if (NavMesh.GetSettingsNameFromID(id) == m_agentTypeName)
            {
                return id;
            }
        }

        Debug.LogWarning($"{nameof(ObjectivePathArrow)}: Agent Type '{m_agentTypeName}'이 없어 기본 NavMesh로 길을 찾습니다.", this);
        return 0;
    }

    private Transform ResolveOrigin()
    {
        if (m_originOverride != null)
        {
            return m_originOverride;
        }

        SquadMemberController player = SquadManager.Instance != null ? SquadManager.Instance.PlayerSquadMember : null;
        return player != null ? player.transform : null;
    }

    /// <summary>캐릭터 위치에서 목표까지 경로를 다시 계산합니다.</summary>
    /// <remarks>
    /// 두 위치를 먼저 NavMesh 위로 붙입니다. 캐릭터는 점프나 경사로 NavMesh에서 조금 떠 있을 수 있고, 목표는 건물
    /// 안쪽처럼 NavMesh 밖에 놓이는 일이 많아서, 그대로 넘기면 경로 계산이 실패하기 때문입니다.
    /// </remarks>
    private void RecalculatePath(Vector3 originPosition)
    {
        m_hasPath = false;
        m_corners = new Vector3[0];
        m_remainingPathLength = 0.0f;

        Vector3 targetPosition = m_target.position + m_targetOffset;
        if (!NavMesh.SamplePosition(originPosition, out NavMeshHit originHit, m_sampleRadius, m_queryFilter) ||
            !NavMesh.SamplePosition(targetPosition, out NavMeshHit targetHit, m_sampleRadius, m_queryFilter))
        {
            return;
        }

        if (!NavMesh.CalculatePath(originHit.position, targetHit.position, m_queryFilter, m_path))
        {
            return;
        }

        bool usable = m_path.status == NavMeshPathStatus.PathComplete ||
            (m_allowPartialPath && m_path.status == NavMeshPathStatus.PathPartial);
        if (!usable || m_path.corners.Length < 2)
        {
            return;
        }

        m_corners = m_path.corners;
        for (int i = 1; i < m_corners.Length; i++)
        {
            m_remainingPathLength += HorizontalDistance(m_corners[i - 1], m_corners[i]);
        }

        m_hasPath = true;
        if (m_pathLine != null)
        {
            BuildGroundedTail();
        }
    }

    /// <summary>캐릭터에서 경로를 따라 내다볼 거리만큼 간 지점으로의 수평 방향을 구합니다.</summary>
    /// <remarks>
    /// 경로는 일정 간격마다만 다시 계산하므로 그 사이 캐릭터는 첫 꺾임점에서 벗어나 있습니다. 그래서 거리는 경로
    /// 시작점이 아니라 지금 캐릭터 위치에서 잽니다. 캐릭터에서 다음 꺾임점까지는 직선으로, 그 뒤로는 경로를 따라 잽니다.
    /// </remarks>
    private bool TryGetLookAheadDirection(Vector3 originPosition, out Vector3 direction)
    {
        direction = Vector3.zero;
        if (m_corners.Length < 2)
        {
            return false;
        }

        Vector3 from = originPosition;
        float remaining = m_lookAheadDistance;
        Vector3 lookPoint = m_corners[m_corners.Length - 1];
        for (int i = 1; i < m_corners.Length; i++)
        {
            float segment = HorizontalDistance(from, m_corners[i]);
            if (segment >= remaining)
            {
                lookPoint = Vector3.Lerp(from, m_corners[i], remaining / Mathf.Max(segment, 0.0001f));
                break;
            }

            remaining -= segment;
            from = m_corners[i];
        }

        direction = lookPoint - originPosition;
        direction.y = 0.0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        direction.Normalize();
        return true;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0.0f;
        b.y = 0.0f;
        return Vector3.Distance(a, b);
    }

    /// <summary>경로 선을 그릴 LineRenderer와 단색 머티리얼을 만듭니다.</summary>
    /// <remarks>
    /// 셰이더는 투척 궤적 미리보기와 같은 URP Unlit을 씁니다. 조명과 그림자에 영향받지 않아 어두운 곳에서도 색이 그대로 보입니다.
    /// 선 오브젝트는 이 오브젝트의 자식이지만 월드 좌표로 그려서, 이 오브젝트가 어디에 있든 상관없습니다.
    /// </remarks>
    private void CreatePathLine()
    {
        GameObject lineObject = new GameObject("ObjectivePathLine");
        lineObject.transform.SetParent(transform, false);

        m_pathLine = lineObject.AddComponent<LineRenderer>();
        m_pathLine.useWorldSpace = true;
        m_pathLine.widthMultiplier = m_pathLineWidth;
        m_pathLine.numCornerVertices = 4;
        m_pathLine.numCapVertices = 4;
        m_pathLine.alignment = LineAlignment.View;
        m_pathLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        m_pathLine.receiveShadows = false;
        m_pathLine.startColor = m_pathLineColor;
        m_pathLine.endColor = m_pathLineColor;
        m_pathLine.positionCount = 0;

        Shader lineShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (lineShader != null)
        {
            m_pathLineMaterial = new Material(lineShader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            m_pathLineMaterial.SetColor("_BaseColor", m_pathLineColor);
            m_pathLine.sharedMaterial = m_pathLineMaterial;
        }
    }

    /// <summary>첫 꺾임점부터 목표까지를 잘게 나눠 바닥에 붙인 점을 만듭니다.</summary>
    /// <remarks>
    /// NavMesh는 꺾임점 사이를 직선으로 잇기 때문에, 그 사이에 지형이 솟아 있으면 선이 땅에 묻힙니다.
    /// 그래서 일정 간격마다 실제 바닥 높이를 찾아 붙입니다. 이 구간은 경로가 바뀔 때만 다시 만듭니다.
    /// </remarks>
    private void BuildGroundedTail()
    {
        // 첫 구간(캐릭터 → 첫 꺾임점)은 캐릭터가 움직이므로 매 프레임 따로 만듭니다. 여기서는 첫 꺾임점 다음부터 만듭니다.
        m_groundedTail.Clear();
        for (int i = 2; i < m_corners.Length; i++)
        {
            AppendGroundedSegment(m_groundedTail, m_corners[i - 1], m_corners[i], false);
        }
    }

    /// <summary>a에서 b까지를 바닥 확인 간격으로 나눈 점을 바닥에 붙여 넣습니다. b는 넣고 a는 includeStart일 때만 넣습니다.</summary>
    private void AppendGroundedSegment(System.Collections.Generic.List<Vector3> points, Vector3 a, Vector3 b, bool includeStart)
    {
        int count = SegmentSampleCount(a, b);
        if (includeStart)
        {
            points.Add(SnapToGround(a));
        }

        for (int k = 1; k <= count; k++)
        {
            points.Add(SnapToGround(Vector3.Lerp(a, b, k / (float)count)));
        }
    }

    private int SegmentSampleCount(Vector3 a, Vector3 b)
    {
        return Mathf.Max(1, Mathf.CeilToInt(HorizontalDistance(a, b) / m_groundSampleSpacing));
    }

    /// <summary>점에서 가장 가까운 안내용 NavMesh 바닥 높이에 선 높이를 더한 위치를 돌려줍니다.</summary>
    /// <remarks>
    /// 안내용 NavMesh는 Height Mesh를 켜고 구워서 실제 바닥 높이를 돌려줍니다. 장애물 윗면은 빼고 구웠기 때문에
    /// 선이 바리케이드나 차량 위로 올라타지 않습니다. 바닥 레이어 정리 없이 씬마다 그대로 쓸 수 있습니다.
    /// </remarks>
    private Vector3 SnapToGround(Vector3 point)
    {
        if (NavMesh.SamplePosition(point, out NavMeshHit hit, m_groundSnapRadius, m_queryFilter))
        {
            point.y = hit.position.y;
        }

        point.y += m_pathLineHeight;
        return point;
    }

    /// <summary>캐릭터 위치에서 시작해 남은 경로를 잇는 선을 그립니다.</summary>
    private void UpdatePathLine(Vector3 originPosition)
    {
        if (m_pathLine == null)
        {
            return;
        }

        // 경로의 첫 점은 계산할 때의 캐릭터 위치라 그 뒤 움직인 만큼 어긋납니다. 첫 구간만 지금 캐릭터 위치로 다시 만듭니다.
        m_linePoints.Clear();
        AppendGroundedSegment(m_linePoints, originPosition, m_corners[1], true);
        m_linePoints.AddRange(m_groundedTail);

        m_pathLine.positionCount = m_linePoints.Count;
        for (int i = 0; i < m_linePoints.Count; i++)
        {
            m_pathLine.SetPosition(i, m_linePoints[i]);
        }
        if (!m_pathLine.enabled)
        {
            m_pathLine.enabled = true;
        }
    }

    private void HideAll()
    {
        SetArrowActive(false);
        m_hasYaw = false;
        if (m_pathLine != null)
        {
            m_pathLine.enabled = false;
        }
    }

    private void SetArrowActive(bool active)
    {
        if (m_arrowVisual != null && m_arrowVisual.gameObject.activeSelf != active)
        {
            m_arrowVisual.gameObject.SetActive(active);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (m_corners == null || m_corners.Length < 2)
        {
            return;
        }

        Gizmos.color = new Color(0.2f, 0.9f, 1.0f, 0.9f);
        for (int i = 1; i < m_corners.Length; i++)
        {
            Gizmos.DrawLine(m_corners[i - 1], m_corners[i]);
            Gizmos.DrawWireSphere(m_corners[i], 0.15f);
        }
    }
}
