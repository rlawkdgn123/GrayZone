using UnityEngine;

public class BulletManager : MonoBehaviour
{
    private Rigidbody bulletRigidbody;

    [SerializeField]
    private float moveSpeed = 10f;

    [SerializeField]
    private float destroyTime = 3f;

    [SerializeField]
    private int damage = 1;

    // 이 탄환을 발사한 주체의 진영입니다. 적대 진영에게만 피해를 적용합니다.
    // 현재 물리 탄환은 플레이어 무기에서만 생성되므로 기본값은 Player이며,
    // 다른 진영이 사용할 경우 발사 시 SetFaction으로 설정합니다.
    private Faction m_ownerFaction = Faction.Player;

    private float currentLifeTime;

    private void Start()
    {
        bulletRigidbody = GetComponent<Rigidbody>();
        currentLifeTime = destroyTime;
    }

    private void OnEnable()
    {
        currentLifeTime = destroyTime;
    }

    private void Update()
    {
        currentLifeTime -= Time.deltaTime;

        if (currentLifeTime <= 0f)
        {
            DestroyBullet();
            return;
        }

        BulletMove();
    }

    private void BulletMove()
    {
        if (bulletRigidbody != null)
        {
            bulletRigidbody.linearVelocity = transform.forward * moveSpeed;
        }
    }

    private void DestroyBullet()
    {
        gameObject.SetActive(false);
        currentLifeTime = destroyTime;
    }

    /// <summary>
    /// 이 탄환을 발사한 주체의 진영을 설정합니다.
    /// </summary>
    /// <param name="faction">발사 주체의 진영입니다.</param>
    public void SetFaction(Faction faction)
    {
        m_ownerFaction = faction;
    }

    private void OnTriggerEnter(Collider other)
    {
        // 히트스캔(Gun)과 동일한 진영 기반 공용 피해 경로를 사용합니다.
        // 대상 구체 타입을 모른 채 CombatDamage가 IDamageable 조회 후 적대 판정하여 적용합니다.
        // 발사한 주체를 따로 들고 있지 않아 공격자는 비워 두고, 세부 종류에 탄환 이름을 남깁니다.
        CombatDamage.TryApplyDamage(other, m_ownerFaction, new DamageInfo(damage, DamageCategory.Ranged, name, null));

        DestroyBullet();
    }
}
