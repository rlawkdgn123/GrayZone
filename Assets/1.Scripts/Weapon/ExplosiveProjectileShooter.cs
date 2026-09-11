using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 지정한 키를 누르면 발사 지점의 정면으로 폭발 투사체를 생성합니다.
/// </summary>
public class ExplosiveProjectileShooter : MonoBehaviour
{
    [Tooltip("발사할 ExplosiveProjectile Prefab입니다.")]
    [SerializeField] private ExplosiveProjectile m_projectilePrefab;

    [Tooltip("이 오브젝트 중심을 기준으로 투사체를 생성할 로컬 위치 오프셋입니다.")]
    [SerializeField] private Vector3 m_spawnOffset = new Vector3(20.0f, 0.0f, 0.0f);

    [Tooltip("생성된 투사체 Rigidbody에 적용할 초기 속도입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_launchSpeed = 10.0f;

    [Tooltip("폭발 투사체를 발사할 키입니다.")]
    [SerializeField] private Key m_fireKey = Key.E;

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard[m_fireKey].wasPressedThisFrame)
        {
            Fire();
        }
    }

    private void Fire()
    {
        if (m_projectilePrefab == null)
        {
            Debug.LogWarning($"[{name}] 발사할 폭발 투사체 Prefab이 없습니다.", this);
            return;
        }

        Vector3 spawnOrigin = ResolveSpawnOrigin();
        Vector3 spawnPosition = spawnOrigin + transform.TransformDirection(m_spawnOffset);
        ExplosiveProjectile projectile = Instantiate(
            m_projectilePrefab,
            spawnPosition,
            transform.rotation);

        Rigidbody projectileRigidbody = projectile.GetComponent<Rigidbody>();
        projectileRigidbody.linearVelocity = transform.forward * m_launchSpeed;
    }

    private Vector3 ResolveSpawnOrigin()
    {
        return TryGetComponent(out Collider sourceCollider)
            ? sourceCollider.bounds.center
            : transform.position;
    }
}
