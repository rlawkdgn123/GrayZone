using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// 튜토리얼에서 쓰는 인게임 시네마틱 하나입니다. Timeline을 재생하거나, 카메라 목록을 차례로 비춥니다.
/// </summary>
[Serializable]
public sealed class TutorialCinematic
{
    [Tooltip("시퀀스가 이 시네마틱을 가리키는 ID입니다. 자동으로 붙고, 이름을 바꿔도 그대로입니다.")]
    [SerializeField] private string m_id;

    [Tooltip("시퀀스의 시네마틱 블록 드롭다운에 보일 이름입니다.")]
    [SerializeField] private string m_name = "새 시네마틱";

    [Tooltip("재생할 Timeline입니다. 넣으면 Timeline을 처음부터 끝까지 재생하고, 아래 카메라 목록은 쓰지 않습니다. Play On Awake는 꺼 둡니다.")]
    [SerializeField] private PlayableDirector m_timeline;

    [Tooltip("Timeline이 없을 때 위에서부터 차례로 비출 카메라입니다. 평소에는 꺼 둡니다.")]
    [SerializeField] private List<CinemachineCamera> m_cameras = new();

    [Tooltip("카메라 한 대를 비추는 시간(초, 실제 시간)입니다. Timeline에는 쓰지 않습니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_secondsPerCamera = 3.0f;

    [Tooltip("켜면 재생하는 동안 분대 조작을 막습니다.")]
    [SerializeField] private bool m_lockInput = true;

    [Header("End Transition")]
    [Tooltip("켜면 끝날 때 카메라를 플레이어 쪽으로 옮기지 않고, 화면을 검게 덮은 뒤 플레이어 시점으로 바로 바꾸고 페이드인합니다. " +
             "Timeline이면 마지막 카메라 클립의 Ease Out(플레이어 카메라로 옮겨 가는 구간)이 시작되기 전에 검게 덮습니다.")]
    [SerializeField] private bool m_fadeToPlayer;

    [Tooltip("검게 덮는 시간(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_fadeOutSeconds = 0.5f;

    [Tooltip("검은 화면을 유지하는 시간(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_blackSeconds = 0.3f;

    [Tooltip("플레이어 시점으로 밝아지는 시간(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_fadeInSeconds = 0.8f;

    /// <summary>끝날 때 검은 화면을 거쳐 플레이어 시점으로 바꿀지 여부입니다.</summary>
    public bool FadeToPlayer => m_fadeToPlayer;

    /// <summary>검게 덮는 시간(초)입니다.</summary>
    public float FadeOutSeconds => m_fadeOutSeconds;

    /// <summary>검은 화면을 유지하는 시간(초)입니다.</summary>
    public float BlackSeconds => m_blackSeconds;

    /// <summary>플레이어 시점으로 밝아지는 시간(초)입니다.</summary>
    public float FadeInSeconds => m_fadeInSeconds;

    /// <summary>시퀀스가 이 시네마틱을 가리키는 ID입니다.</summary>
    public string Id => m_id;

    /// <summary>드롭다운에 보일 이름입니다.</summary>
    public string Name => m_name;

    /// <summary>재생할 Timeline입니다. 없으면 null입니다.</summary>
    public PlayableDirector Timeline => m_timeline;

    /// <summary>Timeline이 없을 때 차례로 비출 카메라입니다.</summary>
    public IReadOnlyList<CinemachineCamera> Cameras => m_cameras;

    /// <summary>카메라 한 대를 비추는 시간(초)입니다.</summary>
    public float SecondsPerCamera => m_secondsPerCamera;

    /// <summary>재생하는 동안 분대 조작을 막을지 여부입니다.</summary>
    public bool LockInput => m_lockInput;

    /// <summary>ID가 비었으면 새로 붙입니다. 반환값은 바꿨는지 여부입니다.</summary>
    public bool EnsureId(HashSet<string> used)
    {
        if (!string.IsNullOrEmpty(m_id) && used.Add(m_id))
        {
            return false;
        }

        // 비었거나 복제로 겹치면 새 ID를 붙입니다.
        do
        {
            m_id = Guid.NewGuid().ToString("N").Substring(0, 8);
        }
        while (!used.Add(m_id));

        return true;
    }
}

/// <summary>
/// 씬에서 튜토리얼이 쓰는 인게임 시네마틱 목록입니다. 시퀀스의 시네마틱 블록이 여기서 시네마틱을 고릅니다.
/// </summary>
/// <remarks>
/// 시네마틱은 씬의 카메라와 Timeline을 쓰는데, 시퀀스는 에셋이라 씬 오브젝트를 담을 수 없습니다.
/// 그래서 씬 오브젝트 연결과 재생 설정은 이 컴포넌트가 갖고, 시퀀스는 항목 ID만 저장합니다.
/// 재생은 <see cref="TutorialEvents.PlayCinematic"/>이 합니다. 씬에 하나만 둡니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class TutorialCinematicSettings : MonoBehaviour
{
    [Tooltip("튜토리얼 시네마틱 목록입니다. 시퀀스의 시네마틱 블록 드롭다운에 이름 순서대로 나옵니다.")]
    [SerializeField] private List<TutorialCinematic> m_cinematics = new();

    /// <summary>시네마틱 목록입니다.</summary>
    public IReadOnlyList<TutorialCinematic> Cinematics => m_cinematics;

    /// <summary>ID로 시네마틱을 찾습니다. 없으면 null입니다.</summary>
    public TutorialCinematic Find(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        for (int i = 0; i < m_cinematics.Count; i++)
        {
            if (m_cinematics[i] != null && m_cinematics[i].Id == id)
            {
                return m_cinematics[i];
            }
        }

        return null;
    }

    private void OnValidate()
    {
        HashSet<string> used = new();
        for (int i = 0; i < m_cinematics.Count; i++)
        {
            m_cinematics[i]?.EnsureId(used);
        }
    }
}
