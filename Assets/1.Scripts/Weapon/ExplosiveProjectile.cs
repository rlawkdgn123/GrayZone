using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 충돌하거나 신관 시간이 끝났을 때 주변 적에게 한 번씩 고정 피해를 주는 폭발 투사체입니다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class ExplosiveProjectile : MonoBehaviour
{
    [Tooltip("폭발 범위 안의 각 대상에게 적용할 고정 피해입니다.")]
    [Min(0)]
    [SerializeField] private int m_damage = 10;

    [Tooltip("폭발 순간 적을 검색할 구체의 반지름입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_explosionRadius = 5.0f;

    [Tooltip("충돌하지 않았을 때 자동으로 폭발하기까지의 시간입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_fuseTime = 10.0f;

    [Tooltip("폭발 피해 후보로 검색할 Collider Layer입니다.")]
    [SerializeField] private LayerMask m_damageTargetLayers;

    [Tooltip("접촉해도 즉시 폭발하지 않을 상대 Layer입니다. 물리 충돌 자체는 유지됩니다.")]
    [SerializeField] private LayerMask m_contactExplosionExcludeLayers;

    private float m_elapsedTime;
    private bool m_hasExploded;

    private void Reset()
    {
        m_damageTargetLayers = LayerMask.GetMask("Enemy", "EnemyHitbox");
    }

    private void Awake()
    {
        if (m_damageTargetLayers.value == 0)
        {
            m_damageTargetLayers = LayerMask.GetMask("Enemy", "EnemyHitbox");
        }
    }

    private void Update()
    {
        m_elapsedTime += Time.deltaTime;

        if (m_elapsedTime >= m_fuseTime)
        {
            Explode();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        int otherLayerMask = 1 << collision.gameObject.layer;
        if ((m_contactExplosionExcludeLayers.value & otherLayerMask) != 0)
        {
            return;
        }

        Explode();
    }

    private void Explode()
    {
        if (m_hasExploded)
        {
            return;
        }

        m_hasExploded = true;

        Collider[] colliders = Physics.OverlapSphere(
            transform.position,
            m_explosionRadius,
            m_damageTargetLayers,
            QueryTriggerInteraction.Collide);

        HashSet<IDamageable> damagedTargets = new HashSet<IDamageable>();

        foreach (Collider targetCollider in colliders)
        {
            IDamageable target = targetCollider.GetComponentInParent<IDamageable>();
            if (target == null || !damagedTargets.Add(target))
            {
                continue;
            }

            target.TakeDamage(m_damage, null);
        }

        Destroy(gameObject);
    }
}
