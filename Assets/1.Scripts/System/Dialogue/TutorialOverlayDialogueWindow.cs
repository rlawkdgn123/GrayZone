using UnityEngine;

/// <summary>
/// 기존 튜토리얼 창(<see cref="DefenseTutorialOverlay"/>)을 대화창으로 빌려 쓰는 임시 대화창입니다.
/// </summary>
/// <remarks>
/// 이름은 창의 제목 칸, 대사는 본문 칸, 초상화는 이미지 칸에 넣습니다.
/// 전용 대화창 UI가 나오면 이 컴포넌트 대신 그 UI용 <see cref="DialogueWindow"/>를 연결합니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class TutorialOverlayDialogueWindow : DialogueWindow
{
    [Tooltip("빌려 쓸 튜토리얼 창입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private DefenseTutorialOverlay m_overlay;

    /// <inheritdoc/>
    public override void ShowLine(string speakerName, string role, string text, Sprite portrait)
    {
        DefenseTutorialOverlay overlay = ResolveOverlay();
        if (overlay == null)
        {
            return;
        }

        overlay.ShowDialogue(TutorialPage.CreateDialoguePage(speakerName, text, portrait));
    }

    /// <inheritdoc/>
    public override void Hide()
    {
        ResolveOverlay()?.Hide();
    }

    private DefenseTutorialOverlay ResolveOverlay()
    {
        if (m_overlay == null)
        {
            m_overlay = FindFirstObjectByType<DefenseTutorialOverlay>(FindObjectsInactive.Include);
        }

        return m_overlay;
    }
}
