using UnityEngine;

/// <summary>
/// 충돌하거나 신관 시간이 끝났을 때 범위 안의 대상에게 진영별 고정 피해를 주는 폭발 투사체입니다.
/// </summary>
/// <remarks>
/// 실제 폭발 판정은 <see cref="ExplosionDamage"/>가 합니다. 이 컴포넌트는 언제 터질지와
/// 터진 뒤 자신을 없애는 것만 맡습니다.
/// </remarks>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class ExplosiveProjectile : ProjectileBase
{
    private const string ExplosionSoundEventPath = "event:/World/Throwable/Grenade/Explosion";
    private static bool s_loggedMissingExplosionSound;

    [Tooltip("폭발 범위 안의 Enemy 진영 대상에게 적용할 고정 피해입니다.")]
    [Min(0)]
    [SerializeField] private int m_damage = 10;

    [Tooltip("폭발 범위 안의 Player 진영 대상에게 적용할 고정 피해입니다.")]
    [Min(0)]
    [SerializeField] private int m_playerDamage = 10;

    [Tooltip("폭발 순간 대상을 검색할 원통의 수평 반지름입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_explosionRadius = 5.0f;

    [Tooltip("폭발 피해 원통의 전체 높이입니다. 폭발 지점을 중심으로 위아래 절반씩 적용됩니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_explosionHeight = 20.0f;

    [Tooltip("충돌하지 않았을 때 자동으로 폭발하기까지의 시간입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_fuseTime = 10.0f;

    [Tooltip("폭발 피해 후보로 검색할 Collider Layer입니다.")]
    [SerializeField] private LayerMask m_damageTargetLayers;

    [Tooltip("폭발을 가로막는 벽으로 볼 Layer입니다. 이 Layer에 가려진 대상은 피해를 받지 않습니다.")]
    [SerializeField] private LayerMask m_explosionOccluderLayers;

    [Tooltip("접촉 폭발과 투척 경로 충돌 판정에서 무시할 상대 Layer입니다.")]
    [SerializeField] private LayerMask m_contactExplosionExcludeLayers;

    [Header("Explosion VFX")]
    [Tooltip("Prefab spawned at the explosion position.")]
    [SerializeField] private GameObject m_explosionVfxPrefab;

    [Tooltip("Time before the spawned explosion VFX is destroyed.")]
    [Min(0.0f)]
    [SerializeField] private float m_explosionVfxLifetime = 4.0f;

    private float m_elapsedTime;
    private bool m_hasExploded;
    private Rigidbody m_rigidbody;

    /// <summary>충돌하지 않았을 때 자동 폭발할 때까지의 시간입니다.</summary>
    public float FuseTime => m_fuseTime;

    /// <summary>폭발 피해 원통의 수평 반지름입니다.</summary>
    public float ExplosionRadius => m_explosionRadius;

    /// <summary>접촉 폭발과 투척 경로 충돌에서 무시할 Layer입니다.</summary>
    public LayerMask ContactExplosionExcludeLayers => m_contactExplosionExcludeLayers;

    /// <inheritdoc />
    public override float FlightTimeLimit => m_fuseTime;

    /// <inheritdoc />
    public override float ImpactPreviewRadius => m_explosionRadius;

    /// <inheritdoc />
    public override LayerMask ContactExcludeLayers => m_contactExplosionExcludeLayers;

    private void Reset()
    {
        m_damageTargetLayers = LayerMask.GetMask("Player", "Enemy");
        m_explosionOccluderLayers = LayerMask.GetMask("Default", "Env_Interior");
    }

    private void Awake()
    {
        m_rigidbody = GetComponent<Rigidbody>();

        if (m_damageTargetLayers.value == 0)
        {
            m_damageTargetLayers = LayerMask.GetMask("Player", "Enemy");
        }

        if (m_explosionOccluderLayers.value == 0)
        {
            m_explosionOccluderLayers = LayerMask.GetMask("Default", "Env_Interior");
        }
    }

    private void Update()
    {
        m_elapsedTime += Time.deltaTime;

        if (m_elapsedTime >= m_fuseTime)
        {
            Detonate();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        int otherLayerMask = 1 << collision.gameObject.layer;
        if ((m_contactExplosionExcludeLayers.value & otherLayerMask) != 0)
        {
            return;
        }

        Vector3 projectilePosition = m_rigidbody != null
            ? m_rigidbody.position
            : transform.position;
        Vector3 contactPoint = projectilePosition;
        Vector3 surfaceNormal = Vector3.up;

        if (collision.contactCount > 0)
        {
            ContactPoint contact = collision.GetContact(0);
            contactPoint = contact.point;
            surfaceNormal = contact.normal;
        }

        ImpactAt(projectilePosition, contactPoint, surfaceNormal);
    }

    /// <summary>
    /// 포물선 이동 종료나 스윕 충돌처럼 외부 이동 컴포넌트가 폭발을 요청할 때 사용하는 진입점입니다.
    /// </summary>
    public void Detonate()
    {
        Vector3 explosionCenter = m_rigidbody != null
            ? m_rigidbody.position
            : transform.position;
        DetonateAt(explosionCenter);
    }

    /// <summary>지정한 물리 좌표를 중심으로 폭발 피해를 적용합니다.</summary>
    public void DetonateAt(Vector3 explosionCenter)
    {
        if (m_hasExploded)
        {
            return;
        }

        m_hasExploded = true;
        ExplosionDeathForce deathForce = GetComponent<ExplosionDeathForce>();
        System.Action<IDamageable> onTargetDamaged = deathForce != null
            ? target => deathForce.TryApply(target, explosionCenter)
            : null;

        ExplosionDamage.DetonateCylinder(
            explosionCenter,
            m_explosionRadius,
            m_explosionHeight,
            m_damage,
            m_damageTargetLayers,
            CreateDamageInfo(),
            false,
            m_playerDamage,
            onTargetDamaged,
            m_explosionOccluderLayers);

        PlayExplosionSound(explosionCenter);
        SpawnExplosionVfx(explosionCenter);

        Destroy(gameObject);
    }

    /// <inheritdoc />
    public override void ImpactAt(
        Vector3 projectilePosition,
        Vector3 contactPoint,
        Vector3 surfaceNormal)
    {
        DetonateAt(projectilePosition);
    }

    private void SpawnExplosionVfx(Vector3 explosionCenter)
    {
        if (m_explosionVfxPrefab == null)
        {
            return;
        }

        GameObject explosionVfx = Instantiate(
            m_explosionVfxPrefab,
            explosionCenter,
            Quaternion.identity);

        ParticleSystem[] particleSystems = explosionVfx.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem particleSystem in particleSystems)
        {
            ParticleSystem.MainModule main = particleSystem.main;
            main.loop = false;
        }

        Destroy(explosionVfx, m_explosionVfxLifetime);
    }

    /// <summary>수류탄이 터진 지점에서 3D 폭발음을 한 번 재생합니다.</summary>
    private void PlayExplosionSound(Vector3 explosionCenter)
    {
        if (!SoundManager.IsReady)
        {
            return;
        }

        try
        {
            SoundManager.Instance.PlayOneShot(ExplosionSoundEventPath, explosionCenter);
        }
        catch (FMODUnity.EventNotFoundException exception)
        {
            if (!s_loggedMissingExplosionSound)
            {
                s_loggedMissingExplosionSound = true;
                Debug.LogWarning(
                    $"[ExplosiveProjectile] FMOD 이벤트를 찾지 못했습니다: {ExplosionSoundEventPath}\n{exception.Message}",
                    this);
            }
        }
    }
}
