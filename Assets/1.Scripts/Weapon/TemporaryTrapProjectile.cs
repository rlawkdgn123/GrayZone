using UnityEngine;

/// <summary>
/// 충돌 지점에 지정한 함정을 즉시 설치하고, 일정 시간이 지나면 제거하는 투척물입니다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public sealed class TemporaryTrapProjectile : ProjectileBase
{
    private const string MolotovImpactSoundEventPath = "event:/World/Throwable/Molotov/Impact";
    private const string MolotovFireLoopEventPath = "event:/World/Throwable/Molotov/FireLoop";
    private static bool s_loggedMissingMolotovImpactSound;

    [Tooltip("충돌 지점에 생성할 Trap 계열 프리팹입니다. 생성 직후 비용 없이 설치 완료 상태가 됩니다.")]
    [SerializeField] private Trap m_trapPrefab;

    [Tooltip("생성된 함정을 유지할 시간(초)입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_trapLifetime = 10.0f;

    [Tooltip("충돌하지 않은 투척물을 제거하기까지의 최대 비행 시간(초)입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_maxFlightTime = 10.0f;

    [Tooltip("투척 경로와 실제 충돌 대상에서 제외할 Layer입니다.")]
    [SerializeField] private LayerMask m_contactExcludeLayers = 1 << 8;

    [Tooltip("Trap에 ParticleSystem이나 Trigger SphereCollider가 없을 때 사용할 원형 효과 범위 반지름입니다. Fire VFX가 있으면 ParticleSystem Shape의 XZ 반지름을 우선 사용합니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_impactPreviewRadius = 2.0f;

    [Tooltip("바닥으로 볼 Layer입니다. 이 Layer에 닿으면 표면 법선에 맞춰 함정을 세우고, 그 밖(벽 등)에 닿으면 회전 없이 수평으로 생성합니다.")]
    [SerializeField] private LayerMask m_groundLayers = 1 << 3;

    /// <summary>접점에서 표면을 다시 찾을 때 법선 방향으로 물러나 쏘는 거리입니다.</summary>
    private const float SurfaceProbeDistance = 0.2f;

    /// <summary>표면 확인용 레이캐스트 결과 버퍼입니다. 투척물마다 할당하지 않도록 공유합니다.</summary>
    private static readonly RaycastHit[] s_probeHits = new RaycastHit[8];

    private bool m_hasDeployed;
    private float m_elapsedTime;

    /// <inheritdoc />
    public override float FlightTimeLimit => Mathf.Max(0.01f, m_maxFlightTime);

    /// <inheritdoc />
    public override float ImpactPreviewRadius => ResolveImpactPreviewRadius();

    /// <inheritdoc />
    public override LayerMask ContactExcludeLayers => m_contactExcludeLayers;

    private float ResolveImpactPreviewRadius()
    {
        ParticleSystem effect = m_trapPrefab != null
            ? m_trapPrefab.GetComponentInChildren<ParticleSystem>(true)
            : null;

        if (effect != null && effect.shape.enabled)
        {
            ParticleSystem.ShapeModule shape = effect.shape;
            float effectScale = Mathf.Max(
                Mathf.Abs(effect.transform.localScale.x),
                Mathf.Abs(effect.transform.localScale.z));
            float shapeScale = Mathf.Max(Mathf.Abs(shape.scale.x), Mathf.Abs(shape.scale.z));
            return Mathf.Max(0.0f, shape.radius) * shapeScale * effectScale;
        }

        SphereCollider trigger = m_trapPrefab != null
            ? m_trapPrefab.GetComponent<SphereCollider>()
            : null;

        if (trigger == null || !trigger.isTrigger)
        {
            return Mathf.Max(0.0f, m_impactPreviewRadius);
        }

        float horizontalScale = Mathf.Max(
            Mathf.Abs(trigger.transform.localScale.x),
            Mathf.Abs(trigger.transform.localScale.z));
        return Mathf.Max(0.0f, trigger.radius) * horizontalScale;
    }

    private void Update()
    {
        m_elapsedTime += Time.deltaTime;
        if (!m_hasDeployed && m_elapsedTime >= FlightTimeLimit)
        {
            Destroy(gameObject);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (m_hasDeployed)
        {
            return;
        }

        int otherLayerMask = 1 << collision.gameObject.layer;
        if ((m_contactExcludeLayers.value & otherLayerMask) != 0)
        {
            return;
        }

        Vector3 contactPoint = transform.position;
        Vector3 normal = Vector3.up;

        if (collision.contactCount > 0)
        {
            ContactPoint contact = collision.GetContact(0);
            contactPoint = contact.point;
            normal = contact.normal;
        }

        DeployAt(contactPoint, normal, IsGroundLayer(collision.gameObject.layer));
    }

    /// <inheritdoc />
    /// <remarks>
    /// 포물선 이동기는 맞은 콜라이더를 넘겨주지 않으므로 접점에서 표면을 다시 찾아 바닥인지 판단합니다.
    /// </remarks>
    public override void ImpactAt(
        Vector3 projectilePosition,
        Vector3 contactPoint,
        Vector3 surfaceNormal)
    {
        DeployAt(contactPoint, surfaceNormal, IsGroundSurface(contactPoint, surfaceNormal));
    }

    /// <summary>지정한 위치와 표면 방향에 함정을 생성합니다. 표면이 바닥인지는 접점에서 다시 찾아 판단합니다.</summary>
    public void DeployAt(Vector3 position, Vector3 surfaceNormal)
    {
        DeployAt(position, surfaceNormal, IsGroundSurface(position, surfaceNormal));
    }

    /// <summary>
    /// 지정한 위치에 함정을 생성합니다.
    /// </summary>
    /// <param name="isGround">
    /// 닿은 표면이 바닥 Layer인지 여부입니다. 바닥이면 표면 법선에 맞춰 세우고, 아니면 회전을 모두 0으로 두어
    /// 터진 지점에서 수평(x/z)으로 생성합니다. 벽에 맞았을 때 함정이 벽에 세로로 박히는 것을 막습니다.
    /// </param>
    private void DeployAt(Vector3 position, Vector3 surfaceNormal, bool isGround)
    {
        if (m_hasDeployed)
        {
            return;
        }

        m_hasDeployed = true;

        if (m_trapPrefab == null)
        {
            Debug.LogWarning($"[{name}] 생성할 함정 Prefab이 지정되지 않았습니다.", this);
            Destroy(gameObject);
            return;
        }

        Quaternion rotation = isGround
            ? ResolveSurfaceRotation(surfaceNormal)
            : Quaternion.identity;
        Trap trap = Instantiate(m_trapPrefab, position, rotation);

        // 이 함정이 주는 피해는 함정이 아니라 투척물(화염병 등)의 피해로 남깁니다.
        trap.SetDamageSource(CreateDamageInfo());
        FireTrap fireTrap = trap as FireTrap;
        if (fireTrap != null)
        {
            PlayMolotovImpactSound(position);
        }

        trap.Build();
        if (fireTrap != null)
        {
            fireTrap.StartLoopingSound(MolotovFireLoopEventPath);
        }

        Destroy(trap.gameObject, Mathf.Max(0.01f, m_trapLifetime));
        Destroy(gameObject);
    }

    /// <summary>화염병이 깨지고 점화되는 지점에서 3D 충돌음을 한 번 재생합니다.</summary>
    private void PlayMolotovImpactSound(Vector3 position)
    {
        if (!SoundManager.IsReady)
        {
            return;
        }

        try
        {
            SoundManager.Instance.PlayOneShot(MolotovImpactSoundEventPath, position);
        }
        catch (FMODUnity.EventNotFoundException exception)
        {
            if (!s_loggedMissingMolotovImpactSound)
            {
                s_loggedMissingMolotovImpactSound = true;
                Debug.LogWarning(
                    $"[TemporaryTrapProjectile] FMOD 이벤트를 찾지 못했습니다: {MolotovImpactSoundEventPath}\n{exception.Message}",
                    this);
            }
        }
    }

    private bool IsGroundLayer(int layer)
    {
        return (m_groundLayers.value & (1 << layer)) != 0;
    }

    /// <summary>
    /// 접점의 표면이 바닥 Layer인지 확인합니다.
    /// </summary>
    /// <remarks>
    /// 접점에서 법선 방향으로 조금 물러난 곳에서 표면을 향해 쏩니다. 접점에서 바로 쏘면 이미 표면에
    /// 붙어 있어 맞지 않을 수 있습니다. 표면을 찾지 못하면 바닥이 아닌 것으로 봅니다.
    /// </remarks>
    private bool IsGroundSurface(Vector3 contactPoint, Vector3 surfaceNormal)
    {
        Vector3 normal = surfaceNormal.sqrMagnitude > 0.0001f
            ? surfaceNormal.normalized
            : Vector3.up;
        Vector3 origin = contactPoint + normal * SurfaceProbeDistance;

        int count = Physics.RaycastNonAlloc(
            origin,
            -normal,
            s_probeHits,
            SurfaceProbeDistance * 2.0f,
            ~m_contactExcludeLayers.value,
            QueryTriggerInteraction.Ignore);

        // 투척물 자신의 콜라이더가 접점에 겹쳐 있을 수 있어 건너뛰고 가장 가까운 표면을 고릅니다.
        Collider nearest = null;
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = s_probeHits[i];
            if (hit.collider.transform.IsChildOf(transform) || hit.distance >= nearestDistance)
            {
                continue;
            }

            nearest = hit.collider;
            nearestDistance = hit.distance;
        }

        return nearest != null && IsGroundLayer(nearest.gameObject.layer);
    }

    private Quaternion ResolveSurfaceRotation(Vector3 surfaceNormal)
    {
        Vector3 up = surfaceNormal.sqrMagnitude > 0.0001f
            ? surfaceNormal.normalized
            : Vector3.up;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up);

        if (forward.sqrMagnitude <= 0.0001f)
        {
            forward = Vector3.ProjectOnPlane(transform.right, up);
        }

        return forward.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(forward.normalized, up)
            : Quaternion.FromToRotation(Vector3.up, up);
    }
}
