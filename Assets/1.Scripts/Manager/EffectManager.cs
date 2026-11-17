using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 필드 씬의 일회성 시각 피드백을 한곳에서 내보내는 매니저입니다.
/// </summary>
/// <remarks>
/// <para>
/// 표면 판별(물리 머티리얼 -> <see cref="SurfaceFeedbackSO"/>)과 필드 공용 생성 요청을 소유합니다.
/// 인스턴스 재사용과 개수 제한은 같은 GameObject의 <see cref="EffectPool"/>이 맡습니다.
/// 두 책임을 나눈 이유는 "무엇을 언제 내는가"와 "씬이 얼마나 감당하는가"가 서로 다른 이유로 바뀌기 때문입니다.
/// </para>
/// <para>
/// 총구 화염·트레이서처럼 무기에 종속된 개인 이펙트는 이 매니저가 생성하거나 재사용하지 않습니다.
/// 개인 이펙트는 각 무기가 풀링하고, 같은 GameObject의 <see cref="EffectPool"/>에는 활성 개수만 보고합니다.
/// </para>
/// <para>
/// 사운드는 내지 않습니다. 표면 피격음도 <see cref="AudioManager"/>에 넘깁니다.
/// 소리와 그림의 수명·예산 규칙이 서로 달라 한 컴포넌트가 둘을 들면 규칙이 섞입니다.
/// </para>
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(EffectPool))]
public sealed class EffectManager : MonoBehaviour
{
    [Tooltip("등록된 표면 타입과 일치하지 않을 때 사용할 기본 표면 피드백입니다.")]
    [FormerlySerializedAs("m_defaultProfile")]
    [FormerlySerializedAs("m_defaultFeedback")]
    [SerializeField] private SurfaceFeedbackSO m_defaultSurfaceFeedback;

    [Tooltip("필드에서 판별할 표면 타입별 피드백 목록입니다.")]
    [FormerlySerializedAs("m_profiles")]
    [FormerlySerializedAs("m_feedbacks")]
    [SerializeField] private SurfaceFeedbackSO[] m_surfaceFeedbacks = Array.Empty<SurfaceFeedbackSO>();

    [Tooltip("인스턴스 재사용과 종류별 개수 제한을 담당하는 같은 GameObject의 풀입니다.")]
    [SerializeField] private EffectPool m_pool;

    private readonly Dictionary<SurfaceFeedbackSO, int> m_lastSoundIndices =
        new Dictionary<SurfaceFeedbackSO, int>();

    /// <summary>일치하는 표면 타입이 없을 때 사용할 기본 피드백입니다.</summary>
    public SurfaceFeedbackSO DefaultSurfaceFeedback => m_defaultSurfaceFeedback;

    /// <summary>필드에서 사용할 표면 타입별 피드백 목록입니다.</summary>
    public IReadOnlyList<SurfaceFeedbackSO> SurfaceFeedbacks => m_surfaceFeedbacks;

    /// <summary>인스턴스 재사용과 개수 제한을 담당하는 풀입니다.</summary>
    public EffectPool Pool => m_pool != null ? m_pool : m_pool = GetComponent<EffectPool>();

    private void Awake()
    {
        if (m_pool == null)
        {
            m_pool = GetComponent<EffectPool>();
        }
    }

    private void Reset()
    {
        m_pool = GetComponent<EffectPool>();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (m_pool == null)
        {
            m_pool = GetComponent<EffectPool>();
        }
    }
#endif

    /// <summary>
    /// 맞은 표면을 판별하고 그 자리에 표면 탄착 이펙트·데칼·사운드를 냅니다.
    /// </summary>
    /// <param name="hit">사격이 멈춘 지점의 충돌 정보입니다.</param>
    /// <returns>재질 전용 순간 탄착 이펙트를 생성했으면 true입니다.</returns>
    public bool PlaySurfaceResponse(RaycastHit hit)
    {
        if (hit.collider == null)
        {
            return false;
        }

        SurfaceFeedbackSO feedback = ResolveSurfaceFeedback(SurfaceMaterialTag.Resolve(hit.collider));
        if (feedback == null)
        {
            return false;
        }

        GameObject impactEffect = SpawnEffect(
            feedback.ImpactEffectPrefab,
            hit.point,
            hit.normal,
            feedback.ImpactEffectLifetime);

        SpawnDecal(feedback.DecalPrefab, hit.point, hit.normal, feedback.DecalLifetime, hit.collider.transform);

        PlayImpactSound(feedback, hit.point);
        return impactEffect != null;
    }

    /// <summary>
    /// 단명 이펙트를 표면 법선에 맞춰 냅니다.
    /// </summary>
    /// <param name="prefab">생성할 프리팹입니다. null이면 아무 일도 하지 않습니다.</param>
    /// <param name="position">생성 위치입니다.</param>
    /// <param name="normal">표면 법선입니다.</param>
    /// <param name="lifetime">자동 회수까지의 시간(초)입니다.</param>
    /// <param name="parent">따라다닐 부모입니다.</param>
    /// <returns>생성하거나 재사용한 인스턴스입니다.</returns>
    public GameObject SpawnEffect(
        GameObject prefab, Vector3 position, Vector3 normal, float lifetime, Transform parent = null)
    {
        return Pool != null
            ? Pool.SpawnAligned(prefab, position, normal, lifetime, EffectCategory.Effect, parent)
            : null;
    }

    /// <summary>
    /// 표면에 남는 자국을 냅니다. 지형 탄흔과 혈흔이 같은 예산을 씁니다.
    /// </summary>
    /// <param name="prefab">생성할 프리팹입니다. null이면 아무 일도 하지 않습니다.</param>
    /// <param name="position">생성 위치입니다.</param>
    /// <param name="normal">표면 법선입니다.</param>
    /// <param name="lifetime">자동 회수까지의 시간(초)입니다.</param>
    /// <param name="parent">따라다닐 부모입니다.</param>
    /// <returns>생성하거나 재사용한 인스턴스입니다.</returns>
    public GameObject SpawnDecal(
        GameObject prefab, Vector3 position, Vector3 normal, float lifetime, Transform parent = null)
    {
        return Pool != null
            ? Pool.SpawnAligned(prefab, position, normal, lifetime, EffectCategory.Decal, parent)
            : null;
    }

    /// <summary>
    /// 회전을 직접 지정해 단명 이펙트를 냅니다. 머즐 플래시나 탄피처럼 표면이 없는 연출에 씁니다.
    /// </summary>
    /// <param name="prefab">생성할 프리팹입니다. null이면 아무 일도 하지 않습니다.</param>
    /// <param name="position">생성 위치입니다.</param>
    /// <param name="rotation">생성 회전입니다.</param>
    /// <param name="lifetime">자동 회수까지의 시간(초)입니다.</param>
    /// <param name="parent">따라다닐 부모입니다.</param>
    /// <returns>생성하거나 재사용한 인스턴스입니다.</returns>
    public GameObject SpawnEffectAt(
        GameObject prefab, Vector3 position, Quaternion rotation, float lifetime, Transform parent = null)
    {
        return Pool != null
            ? Pool.Spawn(prefab, position, rotation, lifetime, EffectCategory.Effect, parent)
            : null;
    }

    /// <summary>재질과 일치하는 피드백을 찾고, 없으면 기본 피드백을 반환합니다.</summary>
    /// <param name="materialType">맞은 구조물의 재질입니다. <c>Unknown</c>이면 곧바로 기본 피드백입니다.</param>
    /// <remarks>
    /// 한 피드백이 재질을 여러 개 나열할 수 있습니다. 탄착 연출은 석재와 벽돌을 같은 돌로 퉁쳐도 되지만
    /// 소음 차폐는 둘을 구분해야 할 수 있어, 축은 잘게 두고 묶는 것은 소비자가 각자 선언합니다.
    /// </remarks>
    public SurfaceFeedbackSO ResolveSurfaceFeedback(SurfaceMaterialType materialType)
    {
        if (materialType != SurfaceMaterialType.Unknown && m_surfaceFeedbacks != null)
        {
            for (int feedbackIndex = 0; feedbackIndex < m_surfaceFeedbacks.Length; feedbackIndex++)
            {
                SurfaceFeedbackSO feedback = m_surfaceFeedbacks[feedbackIndex];
                if (feedback == null || feedback.MaterialTypes == null)
                {
                    continue;
                }

                for (int materialIndex = 0; materialIndex < feedback.MaterialTypes.Count; materialIndex++)
                {
                    if (feedback.MaterialTypes[materialIndex] == materialType)
                    {
                        return feedback;
                    }
                }
            }
        }

        return m_defaultSurfaceFeedback;
    }

    private void PlayImpactSound(SurfaceFeedbackSO feedback, Vector3 position)
    {
        if (!feedback.ImpactSoundEvent.IsNull)
        {
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlayOneShot(feedback.ImpactSoundEvent, position);
            }
            else
            {
                FMODUnity.RuntimeManager.PlayOneShot(feedback.ImpactSoundEvent, position);
            }

            return;
        }

        if (!m_lastSoundIndices.TryGetValue(feedback, out int lastIndex))
        {
            lastIndex = -1;
        }

        if (!FeedbackPlaybackUtility.TryPickClip(feedback.ImpactSounds, ref lastIndex, out AudioClip clip))
        {
            return;
        }

        m_lastSoundIndices[feedback] = lastIndex;

        AudioManager audio = CombatSceneManager.Instance != null ? CombatSceneManager.Instance.AudioManager : null;
        audio?.PlayOneShotAt(clip, position, AudioPriorityClass.SurfaceDecor);
    }
}
