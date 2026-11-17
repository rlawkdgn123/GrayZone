using UnityEngine;

/// <summary>
/// 히트스캔의 확정된 총구-탄착 구간 위로 짧은 발광 선분을 이동시키고 페이드합니다.
/// </summary>
/// <remarks>
/// 피해 판정과 무관한 연출 전용 컴포넌트입니다. 수명과 재사용은
/// <see cref="WeaponFeedbackEmitter"/>의 개인 이펙트 풀이 관리합니다.
/// </remarks>
public sealed class BulletTrailVisual : MonoBehaviour
{
    [SerializeField] private LineRenderer[] m_renderers;

    [Tooltip("트레이서 선분이 이동하는 시각 속도(m/s)입니다.")]
    [Min(1.0f)]
    [SerializeField] private float m_speed = 900.0f;

    [Tooltip("한 번에 화면에 보이는 선분의 최대 길이(m)입니다.")]
    [Min(0.05f)]
    [SerializeField] private float m_segmentLength = 3.5f;

    [Tooltip("아주 가까운 탄착에서도 트레이서가 한 프레임에 사라지지 않게 보장하는 이동 시간입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_minTravelDuration = 0.018f;

    [Tooltip("장거리 사격에서 트레이서가 지나치게 늦게 도착하지 않게 제한하는 이동 시간입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_maxTravelDuration = 0.08f;

    [Tooltip("탄착점에 도달한 뒤 선분이 사라지는 시간입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_fadeDuration = 0.055f;

    private Color[] m_baseStartColors;
    private Color[] m_baseEndColors;
    private Vector3 m_start;
    private Vector3 m_direction;
    private float m_distance;
    private float m_travelDuration;
    private float m_elapsed;
    private bool m_configured;
    private bool m_initialized;

    private void Awake()
    {
        InitializeRenderers();
    }

    private void OnEnable()
    {
        InitializeRenderers();
        m_configured = false;
        SetAlpha(0.0f);
    }

    private void OnDisable()
    {
        m_configured = false;
    }

    /// <summary>이번 재생에서 사용할 총구와 실제 탄착점을 설정합니다.</summary>
    public void Configure(Vector3 start, Vector3 end)
    {
        InitializeRenderers();

        Vector3 delta = end - start;
        m_distance = delta.magnitude;
        if (m_distance <= 0.0001f)
        {
            m_configured = false;
            SetAlpha(0.0f);
            return;
        }

        m_start = start;
        m_direction = delta / m_distance;
        m_elapsed = 0.0f;
        m_travelDuration = Mathf.Clamp(
            m_distance / Mathf.Max(1.0f, m_speed),
            Mathf.Min(m_minTravelDuration, m_maxTravelDuration),
            Mathf.Max(m_minTravelDuration, m_maxTravelDuration));
        m_configured = true;

        float initialHead = Mathf.Min(m_distance, Mathf.Max(0.15f, m_segmentLength * 0.12f));
        RenderSegment(initialHead, 1.0f);
    }

    private void Update()
    {
        if (!m_configured)
        {
            return;
        }

        m_elapsed += Time.deltaTime;
        if (m_elapsed <= m_travelDuration)
        {
            float travel01 = m_travelDuration > 0.0f
                ? Mathf.Clamp01(m_elapsed / m_travelDuration)
                : 1.0f;
            RenderSegment(m_distance * travel01, 1.0f);
            return;
        }

        float fade01 = m_fadeDuration > 0.0f
            ? Mathf.Clamp01((m_elapsed - m_travelDuration) / m_fadeDuration)
            : 1.0f;
        RenderSegment(m_distance, 1.0f - fade01);

        if (fade01 >= 1.0f)
        {
            m_configured = false;
        }
    }

    private void InitializeRenderers()
    {
        if (m_initialized)
        {
            return;
        }

        if (m_renderers == null || m_renderers.Length == 0)
        {
            m_renderers = GetComponentsInChildren<LineRenderer>(true);
        }

        int count = m_renderers != null ? m_renderers.Length : 0;
        m_baseStartColors = new Color[count];
        m_baseEndColors = new Color[count];
        for (int i = 0; i < count; i++)
        {
            LineRenderer line = m_renderers[i];
            if (line == null)
            {
                continue;
            }

            line.useWorldSpace = true;
            line.positionCount = 2;
            m_baseStartColors[i] = line.startColor;
            m_baseEndColors[i] = line.endColor;
        }

        m_initialized = true;
    }

    private void RenderSegment(float headDistance, float alpha)
    {
        float clampedHead = Mathf.Clamp(headDistance, 0.0f, m_distance);
        float tailDistance = Mathf.Max(0.0f, clampedHead - m_segmentLength);
        Vector3 tail = m_start + m_direction * tailDistance;
        Vector3 head = m_start + m_direction * clampedHead;

        for (int i = 0; i < m_renderers.Length; i++)
        {
            LineRenderer line = m_renderers[i];
            if (line == null)
            {
                continue;
            }

            line.SetPosition(0, tail);
            line.SetPosition(1, head);
        }

        SetAlpha(alpha);
    }

    private void SetAlpha(float multiplier)
    {
        if (m_renderers == null || m_baseStartColors == null || m_baseEndColors == null)
        {
            return;
        }

        float alpha = Mathf.Clamp01(multiplier);
        for (int i = 0; i < m_renderers.Length; i++)
        {
            LineRenderer line = m_renderers[i];
            if (line == null)
            {
                continue;
            }

            Color start = m_baseStartColors[i];
            Color end = m_baseEndColors[i];
            start.a *= alpha;
            end.a *= alpha;
            line.startColor = start;
            line.endColor = end;
        }
    }
}
