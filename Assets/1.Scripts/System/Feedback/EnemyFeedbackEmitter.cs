using System.Collections.Generic;
using UnityEngine;
using VInspector;

/// <summary>감염체의 상태 진입과 피격·사망 사건을 공간 피드백으로 출력합니다.</summary>
[DisallowMultipleComponent]
public sealed class EnemyFeedbackEmitter : MonoBehaviour
{
    [Tooltip("감염체의 대기·경계·추적·공격 행동 사운드를 재생할 전용 3D AudioSource입니다. 비어 있으면 런타임에 생성합니다.")]
    [SerializeField] private AudioSource m_actionAudioSource;

    [Tooltip("감염체 행동 사운드의 기본 음량입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_volume = 1.0f;

    [Tooltip("매 행동 사운드에 적용할 무작위 피치 편차입니다.")]
    [Range(0.0f, 0.25f)]
    [SerializeField] private float m_pitchVariation = 0.04f;

    [Tooltip("감염체 행동 사운드가 최대 크기로 들리는 거리(m)입니다.")]
    [SerializeField] private float m_minDistance = 1.0f;

    [Tooltip("감염체 행동 사운드가 감쇠되어 들리는 최대 거리(m)입니다.")]
    [SerializeField] private float m_maxDistance = 30.0f;

    [Foldout("Debug")]
    [Tooltip("이 감염체를 선택했을 때 행동 사운드의 감쇠 시작 거리와 소멸 거리를 Scene 뷰에 원으로 표시합니다. 플레이어가 이 개체의 소리를 어디서부터 듣는지에 해당합니다.")]
    [SerializeField] private bool m_debugDrawAudioDistance = false;

    private int m_lastIdleIndex = -1;
    private int m_lastAlertIndex = -1;
    private int m_lastChaseIndex = -1;
    private int m_lastAttackIndex = -1;
    private int m_lastHitIndex = -1;
    private int m_lastDeathIndex = -1;
    private bool m_loggedMissingFmodEvent;

    private void Awake()
    {
        EnsureActionAudioSource();
    }

    /// <summary>배회 상태에 진입한 순간의 행동 사운드를 출력합니다.</summary>
    public void PlayIdle(EnemyFeedbackSO feedback) => PlayLocal(feedback != null ? feedback.IdleSounds : null, ref m_lastIdleIndex, false, AudioPriorityClass.EnemyAmbient);

    /// <summary>교전 상태에 처음 진입한 순간의 경계 사운드를 출력합니다.</summary>
    public void PlayAlert(EnemyFeedbackSO feedback) => PlayLocal(feedback != null ? feedback.AlertSounds : null, ref m_lastAlertIndex, true, AudioPriorityClass.EnemyCritical);

    /// <summary>추적 상태에 진입한 순간의 행동 사운드를 출력합니다.</summary>
    public void PlayChase(EnemyFeedbackSO feedback) => PlayLocal(feedback != null ? feedback.ChaseSounds : null, ref m_lastChaseIndex, false, AudioPriorityClass.EnemyAmbient);

    /// <summary>공격 동작을 시작한 순간의 행동 사운드를 출력합니다.</summary>
    public void PlayAttack(EnemyFeedbackSO feedback) => PlayLocal(feedback != null ? feedback.AttackSounds : null, ref m_lastAttackIndex, true, AudioPriorityClass.EnemyCritical);

    /// <summary>실제 피격 위치에서 피격 이펙트·사운드·혈흔을 출력합니다.</summary>
    public void PlayHit(EnemyFeedbackSO feedback, Vector3 point, Vector3 normal, Transform hitTransform)
    {
        if (feedback == null)
        {
            return;
        }

        // 직접 Instantiate하지 않고 EffectManager를 거칩니다. 혈흔이 지형 탄흔과 같은 예산을 나눠 써야
        // 씬 전체의 데칼 총량이 잡힙니다. 각자 만들면 총량을 아무도 모릅니다.
        EffectManager effects = FieldManager.Instance != null ? FieldManager.Instance.EffectManager : null;

        if (effects != null)
        {
            effects.SpawnEffect(feedback.HitEffectPrefab, point, normal, feedback.HitEffectLifetime, hitTransform);
            effects.SpawnDecal(feedback.BloodDecalPrefab, point, normal, feedback.BloodDecalLifetime, hitTransform);
        }

        if (!TryPlayFmodAt(feedback.HitEvent, point))
        {
            PlayWorld(feedback.HitSounds, ref m_lastHitIndex, point, AudioPriorityClass.EnemyCritical);
        }
    }

    /// <summary>사망 위치에서 사망 사운드를 독립 one-shot으로 출력합니다.</summary>
    public void PlayDeath(EnemyFeedbackSO feedback)
    {
        if (m_actionAudioSource != null)
        {
            m_actionAudioSource.Stop();
        }

        PlayWorld(feedback != null ? feedback.DeathSounds : null, ref m_lastDeathIndex, transform.position, AudioPriorityClass.EnemyCritical);
    }

    private void PlayLocal(
        IReadOnlyList<AudioClip> clips,
        ref int lastIndex,
        bool interruptCurrent,
        AudioPriorityClass priorityClass)
    {
        AudioSource source = EnsureActionAudioSource();
        if (source == null || (source.isPlaying && !interruptCurrent))
        {
            return;
        }

        if (!FeedbackPlaybackUtility.TryPickClip(clips, ref lastIndex, out AudioClip clip))
        {
            return;
        }

        if (interruptCurrent)
        {
            source.Stop();
        }

        // 한 소스가 배회음과 경계음을 함께 내므로 등급은 소스 준비 때가 아니라 재생마다 갱신해야 합니다.
        source.priority = AudioManager.ResolveUnityPriority(priorityClass);
        source.pitch = 1.0f + Random.Range(-m_pitchVariation, m_pitchVariation);
        source.PlayOneShot(clip, m_volume);
    }

    private void PlayWorld(
        IReadOnlyList<AudioClip> clips,
        ref int lastIndex,
        Vector3 position,
        AudioPriorityClass priorityClass)
    {
        if (!FeedbackPlaybackUtility.TryPickClip(clips, ref lastIndex, out AudioClip clip))
        {
            return;
        }

        float pitch = 1.0f + Random.Range(-m_pitchVariation, m_pitchVariation);
        AudioManager fieldAudio = FieldManager.Instance != null ? FieldManager.Instance.AudioManager : null;
        if (fieldAudio != null && fieldAudio.PlayOneShotAt(clip, position, priorityClass, m_volume, pitch))
        {
            return;
        }

        AudioSource source = EnsureActionAudioSource();
        if (source != null)
        {
            source.priority = AudioManager.ResolveUnityPriority(priorityClass);
            source.pitch = pitch;
            source.PlayOneShot(clip, m_volume);
        }
    }

    /// <summary>피격 위치에 FMOD 3D one-shot을 재생합니다.</summary>
    /// <returns>FMOD 이벤트를 시작했으면 <c>true</c>, AudioClip 폴백이 필요하면 <c>false</c>입니다.</returns>
    private bool TryPlayFmodAt(FMODUnity.EventReference eventReference, Vector3 position)
    {
        if (eventReference.IsNull || !FMODUnity.RuntimeManager.IsInitialized)
        {
            return false;
        }

        try
        {
            FMOD.Studio.EventInstance instance = FMODUnity.RuntimeManager.CreateInstance(eventReference);
            if (!instance.isValid())
            {
                return false;
            }

            instance.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(position));
            instance.setVolume(m_volume);
            instance.start();
            instance.release();
            return true;
        }
        catch (FMODUnity.EventNotFoundException exception)
        {
            if (!m_loggedMissingFmodEvent)
            {
                Debug.LogWarning($"[EnemyFeedbackEmitter] FMOD 이벤트를 찾지 못해 AudioClip으로 대체합니다: {exception.Message}", this);
                m_loggedMissingFmodEvent = true;
            }

            return false;
        }
    }

    private AudioSource EnsureActionAudioSource()
    {
        if (m_actionAudioSource == null)
        {
            GameObject sourceObject = new GameObject("Enemy Feedback Audio");
            sourceObject.transform.SetParent(transform, false);
            m_actionAudioSource = sourceObject.AddComponent<AudioSource>();
        }

        m_actionAudioSource.loop = false;

        AudioManager fieldAudio = FieldManager.Instance != null ? FieldManager.Instance.AudioManager : null;
        if (fieldAudio != null)
        {
            // 이 소스는 풀 밖에서 소리를 내므로, 개인 사운드로 등록해 씬 전체 동시 발음 총량에 포함시킵니다.
            fieldAudio.ApplyWorldSourcePolicy(
                m_actionAudioSource,
                AudioPriorityClass.EnemyAmbient,
                m_minDistance,
                m_maxDistance);
            fieldAudio.RegisterPersonalSource(m_actionAudioSource);
        }
        else
        {
            m_actionAudioSource.playOnAwake = false;
            m_actionAudioSource.spatialBlend = 1.0f;
            m_actionAudioSource.dopplerLevel = 0.0f;
            m_actionAudioSource.rolloffMode = AudioRolloffMode.Logarithmic;
            m_actionAudioSource.minDistance = Mathf.Max(0.01f, m_minDistance);
            m_actionAudioSource.maxDistance = Mathf.Max(m_actionAudioSource.minDistance, m_maxDistance);
        }

        return m_actionAudioSource;
    }

    /// <summary>개체가 사라질 때 개인 사운드 등록을 해제합니다.</summary>
    private void OnDestroy()
    {
        AudioManager fieldAudio = FieldManager.Instance != null ? FieldManager.Instance.AudioManager : null;
        if (fieldAudio != null)
        {
            fieldAudio.UnregisterPersonalSource(m_actionAudioSource);
        }
    }

    /// <summary>
    /// 선택했을 때 이 개체 행동 사운드의 감쇠 시작·소멸 거리를 그립니다.
    /// </summary>
    /// <remarks>
    /// 안쪽 원(minDistance)까지는 원래 크기로 들리고, 바깥 원(maxDistance)에서 완전히 사라집니다.
    /// 플레이어가 변이체의 존재를 소리로 먼저 알아채는 거리이므로, 변이체의 시야 반경보다 넉넉해야
    /// "보이기 전에 들린다"가 성립합니다. 시야 기즈모(<see cref="EnemyTargetSensor"/>)와 같이 보면 비교됩니다.
    /// </remarks>
    private void OnDrawGizmosSelected()
    {
        if (!m_debugDrawAudioDistance)
        {
            return;
        }

        Gizmos.color = new Color(1.0f, 0.7f, 0.9f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.01f, m_minDistance));

        Gizmos.color = new Color(0.7f, 0.4f, 0.6f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(m_minDistance, m_maxDistance));
    }
}
