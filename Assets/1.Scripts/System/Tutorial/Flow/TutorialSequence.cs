using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 튜토리얼 스텝 목록입니다. 위에서부터 차례로 진행합니다.
/// </summary>
/// <remarks>대사와 순서를 코드 수정 없이 에디터에서 짜기 위한 데이터 에셋입니다. 진행은 <see cref="TutorialFlowDirector"/>가 맡습니다.</remarks>
[CreateAssetMenu(fileName = "TutorialSequence", menuName = "GrayZone/Tutorial/Tutorial Sequence")]
public sealed class TutorialSequence : ScriptableObject
{
    [Tooltip("튜토리얼 스텝 목록입니다. 위에서부터 0번, 1번… 순서로 진행합니다.")]
    [SerializeField] private List<TutorialStep> m_steps = new();

    /// <summary>스텝 목록입니다.</summary>
    public IReadOnlyList<TutorialStep> Steps => m_steps;
}
