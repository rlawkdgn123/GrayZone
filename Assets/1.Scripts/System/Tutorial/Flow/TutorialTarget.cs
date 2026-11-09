using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 튜토리얼 시퀀스가 ID로 가리킬 씬 오브젝트에 붙입니다(처치할 적, 상호작용할 대상 등).
/// </summary>
/// <remarks>
/// 시퀀스는 에셋이라 씬 오브젝트를 직접 넣을 수 없어서, 시퀀스에는 ID 문자열을 적고 씬 쪽에서 이 컴포넌트로 이어 줍니다.
/// 같은 ID가 여럿이면 먼저 켜진 쪽을 씁니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class TutorialTarget : MonoBehaviour
{
    private static readonly List<TutorialTarget> s_targets = new();

    [Tooltip("시퀀스의 대상 ID 칸에 적을 이름입니다.")]
    [SerializeField] private string m_id;

    /// <summary>대상 ID입니다.</summary>
    public string Id => m_id;

    /// <summary>켜져 있는 대상 중 ID가 같은 것을 찾습니다. 없으면 null입니다.</summary>
    public static TutorialTarget Find(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        for (int i = 0; i < s_targets.Count; i++)
        {
            if (s_targets[i] != null && s_targets[i].m_id == id)
            {
                return s_targets[i];
            }
        }

        return null;
    }

    private void OnEnable()
    {
        s_targets.Add(this);
    }

    private void OnDisable()
    {
        s_targets.Remove(this);
    }
}
