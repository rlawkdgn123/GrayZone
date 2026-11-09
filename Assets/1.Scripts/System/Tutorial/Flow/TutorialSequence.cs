using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 튜토리얼 스텝 목록입니다. 위에서부터 차례로 진행합니다.
/// </summary>
/// <remarks>대사와 순서를 코드 수정 없이 에디터에서 짜기 위한 데이터 에셋입니다. 진행은 <see cref="TutorialFlowDirector"/>가 맡습니다.</remarks>
[CreateAssetMenu(fileName = "TutorialSequence", menuName = "GrayZone/Tutorial/Tutorial Sequence")]
public sealed class TutorialSequence : ScriptableObject
{
    [Tooltip("튜토리얼을 건너뛰는 키입니다. 누르면 남은 챕터를 모두 건너뛰고 튜토리얼을 마칩니다. None이면 건너뛸 수 없습니다.")]
    [SerializeField] private Key m_skipKey = Key.P;

    [Tooltip("튜토리얼 스텝 목록입니다. 위에서부터 0번, 1번… 순서로 진행합니다.")]
    [SerializeField] private List<TutorialStep> m_steps = new();

    /// <summary>튜토리얼을 건너뛰는 키입니다. <see cref="Key.None"/>이면 건너뛸 수 없습니다.</summary>
    public Key SkipKey => m_skipKey;

    /// <summary>스텝 목록입니다.</summary>
    public IReadOnlyList<TutorialStep> Steps => m_steps;
}
