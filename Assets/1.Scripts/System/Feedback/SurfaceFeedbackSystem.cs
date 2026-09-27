using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 구형 필드 씬의 표면 피드백 컴포넌트를 신형 <see cref="EffectManager"/> 구조에 연결합니다.
/// </summary>
/// <remarks>
/// 기존 씬과 프리팹에 저장된 컴포넌트 GUID 및 피드백 배열을 보존하기 위한 호환 계층입니다.
/// 신형 <see cref="EffectManager"/>가 있으면 모든 처리를 위임하고, 없으면
/// <see cref="SurfaceMaterialTag"/>와 <see cref="AudioManager"/>를 사용해 최소 동작을 유지합니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class SurfaceFeedbackSystem : MonoBehaviour
{
    [Tooltip("등록된 표면 타입과 일치하지 않을 때 사용할 기본 표면 피드백입니다.")]
    [FormerlySerializedAs("m_defaultProfile")]
    [SerializeField] private SurfaceFeedbackSO m_defaultFeedback;

    [Tooltip("필드에서 판별할 표면 타입별 피드백 목록입니다.")]
    [FormerlySerializedAs("m_profiles")]
    [SerializeField] private SurfaceFeedbackSO[] m_feedbacks = Array.Empty<SurfaceFeedbackSO>();

    private readonly Dictionary<SurfaceFeedbackSO, int> m_lastSoundIndices =
        new Dictionary<SurfaceFeedbackSO, int>();

    /// <summary>일치하는 표면 타입이 없을 때 사용할 기본 피드백입니다.</summary>
    public SurfaceFeedbackSO DefaultFeedback => m_defaultFeedback;

    /// <summary>필드에서 사용할 표면 타입별 피드백 목록입니다.</summary>
    public IReadOnlyList<SurfaceFeedbackSO> Feedbacks => m_feedbacks;

    /// <summary>Raycast로 맞은 표면을 판별하고 해당 위치에 이펙트·사운드·데칼을 출력합니다.</summary>
    /// <returns>사용할 표면 피드백을 찾았으면 true입니다.</returns>
    public bool PlayImpact(RaycastHit hit)
    {
        if (hit.collider == null)
        {
            return false;
        }

        EffectManager effectManager = FieldManager.Instance != null
            ? FieldManager.Instance.EffectManager
            : null;
        if (effectManager != null)
        {
            return effectManager.PlaySurfaceResponse(hit);
        }

        SurfaceFeedbackSO feedback = ResolveFeedback(SurfaceMaterialTag.Resolve(hit.collider));
        if (feedback == null)
        {
            return false;
        }

        FeedbackPlaybackUtility.SpawnAligned(
            feedback.DecalPrefab,
            hit.point,
            hit.normal,
            feedback.DecalLifetime,
            parent: hit.collider.transform);

        PlayImpactSound(feedback, hit.point);
        return true;
    }

    /// <summary>표면 타입과 일치하는 피드백을 찾고, 없으면 기본 피드백을 반환합니다.</summary>
    public SurfaceFeedbackSO ResolveFeedback(SurfaceMaterialType materialType)
    {
        if (materialType != SurfaceMaterialType.Unknown && m_feedbacks != null)
        {
            for (int feedbackIndex = 0; feedbackIndex < m_feedbacks.Length; feedbackIndex++)
            {
                SurfaceFeedbackSO feedback = m_feedbacks[feedbackIndex];
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

        return m_defaultFeedback;
    }

    /// <summary>
    /// 물리 머티리얼 기반 호출을 사용하는 구형 코드와의 바이너리·소스 호환용 진입점입니다.
    /// 신형 데이터에는 물리 머티리얼 매핑이 없으므로 기본 피드백을 반환합니다.
    /// </summary>
    [Obsolete("PhysicsMaterial 기반 표면 판별은 SurfaceMaterialType 기반 ResolveFeedback으로 교체되었습니다.")]
    public SurfaceFeedbackSO ResolveFeedback(PhysicsMaterial material)
    {
        return m_defaultFeedback;
    }

    private void PlayImpactSound(SurfaceFeedbackSO feedback, Vector3 position)
    {
        m_lastSoundIndices.TryGetValue(feedback, out int lastIndex);
        if (!m_lastSoundIndices.ContainsKey(feedback))
        {
            lastIndex = -1;
        }

        if (!FeedbackPlaybackUtility.TryPickClip(feedback.ImpactSounds, ref lastIndex, out AudioClip clip))
        {
            return;
        }

        m_lastSoundIndices[feedback] = lastIndex;
        AudioManager audioManager = FieldManager.Instance != null ? FieldManager.Instance.AudioManager : null;
        audioManager?.PlayOneShotAt(clip, position, AudioPriorityClass.SurfaceDecor);
    }
}
