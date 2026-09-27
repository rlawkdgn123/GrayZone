using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 필드에서 발생하는 독립적인 3D one-shot 사운드의 AudioSource 풀과 동시 발음 상한을 관리합니다.
/// </summary>
/// <remarks>
/// 어떤 행동에서 어떤 소리를 낼지는 무기·감염체·표면 시스템이 결정합니다.
/// 이 컴포넌트는 전달받은 클립을 지정된 월드 위치에서 재생하는 공간 출력 정책만 담당합니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class FieldAudioSystem : MonoBehaviour
{
    private sealed class Voice
    {
        public AudioSource Source;
        public float StartedAt;
    }

    [Tooltip("동시에 유지할 필드 위치형 one-shot AudioSource의 최대 개수입니다. 초과하면 가장 오래된 음성을 교체합니다.")]
    [SerializeField] private int m_maxWorldVoices = 32;

    [Tooltip("위치형 one-shot 사운드가 최대 크기로 들리는 기본 거리(m)입니다.")]
    [SerializeField] private float m_minDistance = 1.0f;

    [Tooltip("위치형 one-shot 사운드가 감쇠되어 들리는 기본 최대 거리(m)입니다.")]
    [SerializeField] private float m_maxDistance = 40.0f;

    [Tooltip("필드 위치형 효과음을 보낼 AudioMixer 그룹입니다. 비어 있으면 AudioSource 기본 출력을 사용합니다.")]
    [SerializeField] private AudioMixerGroup m_outputMixerGroup;

    private readonly List<Voice> m_voices = new List<Voice>();

    /// <summary>필드 효과음 AudioSource에 공통 적용할 AudioMixer 출력 그룹입니다.</summary>
    public AudioMixerGroup OutputMixerGroup => m_outputMixerGroup;

    /// <summary>지정한 월드 위치에서 한 번 재생하고, 필요하면 비어 있는 음성을 재사용합니다.</summary>
    /// <returns>유효한 클립을 재생 요청했으면 true입니다.</returns>
    public bool PlayOneShotAt(AudioClip clip, Vector3 position, float volume = 1.0f, float pitch = 1.0f)
    {
        if (clip == null || m_maxWorldVoices <= 0)
        {
            return false;
        }

        Voice voice = AcquireVoice();
        AudioSource source = voice.Source;
        source.transform.position = position;
        source.outputAudioMixerGroup = m_outputMixerGroup;
        source.volume = Mathf.Clamp01(volume);
        source.pitch = Mathf.Clamp(pitch, 0.1f, 3.0f);
        source.clip = clip;
        source.Play();
        voice.StartedAt = Time.unscaledTime;
        return true;
    }

    private Voice AcquireVoice()
    {
        for (int i = 0; i < m_voices.Count; i++)
        {
            if (!m_voices[i].Source.isPlaying)
            {
                return m_voices[i];
            }
        }

        if (m_voices.Count < m_maxWorldVoices)
        {
            Voice created = CreateVoice(m_voices.Count);
            m_voices.Add(created);
            return created;
        }

        Voice oldest = m_voices[0];
        for (int i = 1; i < m_voices.Count; i++)
        {
            if (m_voices[i].StartedAt < oldest.StartedAt)
            {
                oldest = m_voices[i];
            }
        }

        oldest.Source.Stop();
        return oldest;
    }

    private Voice CreateVoice(int index)
    {
        GameObject voiceObject = new GameObject($"World OneShot {index:00}");
        voiceObject.transform.SetParent(transform, false);

        AudioSource source = voiceObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 1.0f;
        source.dopplerLevel = 0.0f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = m_minDistance;
        source.maxDistance = Mathf.Max(m_minDistance, m_maxDistance);
        source.outputAudioMixerGroup = m_outputMixerGroup;

        return new Voice { Source = source, StartedAt = float.NegativeInfinity };
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        m_maxWorldVoices = Mathf.Max(0, m_maxWorldVoices);
        m_minDistance = Mathf.Max(0.01f, m_minDistance);
        m_maxDistance = Mathf.Max(m_minDistance, m_maxDistance);
    }
#endif
}
