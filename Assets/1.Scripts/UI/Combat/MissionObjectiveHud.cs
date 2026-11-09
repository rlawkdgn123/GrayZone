using TMPro;
using UnityEngine;

/// <summary>
/// 전투 HUD 왼쪽의 미션 목표(제목, 부제) 문구를 바꿉니다.
/// </summary>
/// <remarks>
/// 문구는 씬에 적어 둔 값으로 시작하고, 튜토리얼 같은 진행 쪽이 <see cref="SetObjective"/>로 바꿉니다.
/// 이 컴포넌트는 누가 언제 바꾸는지 모릅니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class MissionObjectiveHud : MonoBehaviour
{
    private const string TitleName = "Title";
    private const string SubtitleName = "Subtitle";

    [Tooltip("목표 제목 텍스트입니다. 비어 있으면 자식 'Title'을 찾습니다.")]
    [SerializeField] private TMP_Text m_titleText;

    [Tooltip("목표 부제 텍스트입니다. 선택 항목이며, 비어 있으면 자식 'Subtitle'을 찾습니다.")]
    [SerializeField] private TMP_Text m_subtitleText;

    private void Reset()
    {
        ResolveTexts();
    }

    private void Awake()
    {
        ResolveTexts();
    }

    /// <summary>
    /// 목표 문구를 바꿉니다. 비어 있는 값은 바꾸지 않고 지금 문구를 둡니다.
    /// </summary>
    /// <param name="title">새 제목입니다. 비어 있으면 그대로 둡니다.</param>
    /// <param name="subtitle">새 부제입니다. 비어 있으면 그대로 둡니다.</param>
    public void SetObjective(string title, string subtitle)
    {
        ResolveTexts();

        if (m_titleText != null && !string.IsNullOrEmpty(title))
        {
            m_titleText.text = title;
        }

        if (m_subtitleText != null && !string.IsNullOrEmpty(subtitle))
        {
            m_subtitleText.text = subtitle;
        }
    }

    private void ResolveTexts()
    {
        if (m_titleText == null)
        {
            Transform title = transform.Find(TitleName);
            m_titleText = title != null ? title.GetComponent<TMP_Text>() : null;
        }

        if (m_subtitleText == null)
        {
            Transform subtitle = transform.Find(SubtitleName);
            m_subtitleText = subtitle != null ? subtitle.GetComponent<TMP_Text>() : null;
        }
    }
}
