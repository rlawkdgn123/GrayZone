using UnityEngine;
using VInspector;

/// <summary>
/// 클레이모어입니다. 밟으면 잠시 뒤 정면으로만 터져, 앞으로 갈수록 넓어지는 사다리꼴 범위 안의
/// 대상에게 한 번씩 피해를 줍니다.
/// </summary>
/// <remarks>
/// 정면은 이 오브젝트의 +Z(파란 축)입니다. 배치할 때 터뜨리고 싶은 방향으로 돌려 놓아야 합니다.
/// 뒤쪽에는 피해가 전혀 가지 않으므로 설치한 쪽은 등 뒤에서 안전합니다.
///
/// 범위를 각도가 아니라 <see cref="m_nearWidth"/>·<see cref="m_farWidth"/> 두 폭으로 잡는 이유는,
/// 각도로 두면 사거리를 조정할 때마다 끝 폭이 함께 변해 기획 수치를 잡기 어렵기 때문입니다.
///
/// 밟는 판정은 이 오브젝트의 트리거 콜라이더입니다. 클레이모어는 보통 앞쪽에 걸리게 하므로,
/// 콜라이더를 정면 쪽으로 밀어 두는 배치가 자연스럽습니다.
/// </remarks>
public sealed class ClaymoreTrap : ExplosiveTrap
{
    /// <inheritdoc />
    protected override string DefaultDisplayName => "클레이모어";

    /// <inheritdoc />
    protected override string DefaultDescription => "밟히면 정면으로 폭발해 앞쪽 적에게 피해를 줍니다";

    /// <inheritdoc />
    protected override TrapKind CostKind => TrapKind.Claymore;

    [Header("Claymore")]
    [Tooltip("정면으로 뻗는 거리(m)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_range = 8.0f;

    [Tooltip("폭발 지점 바로 앞의 폭(m)입니다. 사다리꼴의 짧은 변입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_nearWidth = 1.0f;

    [Tooltip("가장 먼 지점의 폭(m)입니다. 사다리꼴의 긴 변이며, 보통 가까운 쪽보다 넓게 잡습니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_farWidth = 6.0f;

    [Tooltip("피해 범위의 전체 높이(m)입니다. 폭발 지점을 기준으로 위아래 절반씩 적용됩니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_explosionHeight = 3.0f;

    [Tooltip("폭발이 시작되는 지점을 이 오브젝트 기준으로 얼마나 옮길지입니다. 바닥에 붙은 클레이모어의 범위 아래 절반이 땅에 묻히는 것을 피할 때 Y를 올립니다.")]
    [SerializeField] private Vector3 m_explosionOriginOffset = new Vector3(0.0f, 0.5f, 0.0f);

    [Header("Claymore Debug")]
    [Tooltip("켜면 Scene View에 폭발 피해 범위를 그립니다. 표시 전용이라 동작에는 영향이 없습니다.")]
    [SerializeField] private bool m_drawExplosionGizmo = true;

    [Tooltip("폭발 범위를 그릴 색입니다.")]
    [ShowIf(nameof(m_drawExplosionGizmo))]
    [SerializeField] private Color m_explosionGizmoColor = new Color(1.0f, 0.45f, 0.1f, 0.7f);
    [EndIf]

    /// <summary>폭발이 시작되는 월드 좌표입니다. 사다리꼴의 짧은 변이 여기에 놓입니다.</summary>
    /// <remarks>
    /// 오프셋에 스케일을 곱하지 않는 이유는, 프리팹을 크게 키워 배치했을 때 피해 범위까지 함께
    /// 커지면 기획 수치와 실제가 어긋나기 때문입니다. 방향만 트랜스폼을 따릅니다.
    /// </remarks>
    public Vector3 ExplosionOrigin => transform.position + transform.rotation * m_explosionOriginOffset;

    /// <summary>폭발이 향하는 방향입니다.</summary>
    public Vector3 ExplosionDirection => transform.forward;

    /// <summary>정면 사거리(m)입니다. Explosive 업그레이드 배율이 곱해져 있습니다.</summary>
    public float Range => m_range * RangeMultiplier;

    /// <summary>가까운 쪽 폭(m)입니다. 사거리와 같은 배율로 늘려 사다리꼴 모양을 유지합니다.</summary>
    private float NearWidth => m_nearWidth * RangeMultiplier;

    /// <summary>먼 쪽 폭(m)입니다. 사거리와 같은 배율로 늘려 사다리꼴 모양을 유지합니다.</summary>
    private float FarWidth => m_farWidth * RangeMultiplier;

    /// <inheritdoc />
    protected override int ApplyExplosionDamage()
    {
        return ExplosionDamage.DetonateTrapezoid(
            ExplosionOrigin,
            transform.rotation,
            Range,
            NearWidth,
            FarWidth,
            m_explosionHeight,
            Damage,
            m_damageTargetLayers,
            CreateDamageInfo(),
            m_showExplosionRangeVisual);
    }

    /// <inheritdoc />
    protected override GameObject CreateExplosionRangePreview()
    {
        return ExplosionDamage.CreateTrapezoidRangeVisual(
            ExplosionOrigin,
            transform.rotation,
            Range,
            NearWidth,
            FarWidth,
            m_explosionHeight);
    }

    /// <inheritdoc />
    protected override void OnDrawTrapGizmos()
    {
        if (!m_drawExplosionGizmo || Range <= 0.0f || m_explosionHeight <= 0.0f)
        {
            return;
        }

        float halfNearWidth = NearWidth * 0.5f;
        float halfFarWidth = FarWidth * 0.5f;
        float halfHeight = m_explosionHeight * 0.5f;

        Matrix4x4 previous = Gizmos.matrix;

        // 오프셋과 회전을 행렬에 넣어 두면 아래 좌표를 모두 "정면이 +Z"인 기준으로 적을 수 있습니다.
        Gizmos.matrix = Matrix4x4.TRS(ExplosionOrigin, transform.rotation, Vector3.one);
        Gizmos.color = m_explosionGizmoColor;

        Vector3 nearBottomLeft = new Vector3(-halfNearWidth, -halfHeight, 0.0f);
        Vector3 nearBottomRight = new Vector3(halfNearWidth, -halfHeight, 0.0f);
        Vector3 nearTopLeft = new Vector3(-halfNearWidth, halfHeight, 0.0f);
        Vector3 nearTopRight = new Vector3(halfNearWidth, halfHeight, 0.0f);
        Vector3 farBottomLeft = new Vector3(-halfFarWidth, -halfHeight, Range);
        Vector3 farBottomRight = new Vector3(halfFarWidth, -halfHeight, Range);
        Vector3 farTopLeft = new Vector3(-halfFarWidth, halfHeight, Range);
        Vector3 farTopRight = new Vector3(halfFarWidth, halfHeight, Range);

        // 가까운 면
        Gizmos.DrawLine(nearBottomLeft, nearBottomRight);
        Gizmos.DrawLine(nearTopLeft, nearTopRight);
        Gizmos.DrawLine(nearBottomLeft, nearTopLeft);
        Gizmos.DrawLine(nearBottomRight, nearTopRight);

        // 먼 면
        Gizmos.DrawLine(farBottomLeft, farBottomRight);
        Gizmos.DrawLine(farTopLeft, farTopRight);
        Gizmos.DrawLine(farBottomLeft, farTopLeft);
        Gizmos.DrawLine(farBottomRight, farTopRight);

        // 두 면을 잇는 모서리
        Gizmos.DrawLine(nearBottomLeft, farBottomLeft);
        Gizmos.DrawLine(nearBottomRight, farBottomRight);
        Gizmos.DrawLine(nearTopLeft, farTopLeft);
        Gizmos.DrawLine(nearTopRight, farTopRight);

        Gizmos.matrix = previous;
    }
}
