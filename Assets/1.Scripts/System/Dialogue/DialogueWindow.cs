using UnityEngine;

/// <summary>
/// 대사 한 줄을 화면 대화창에 그리는 쪽의 공통 틀입니다.
/// </summary>
/// <remarks>
/// <see cref="DialoguePlayer"/>는 이 틀만 알고, 실제 UI가 무엇인지는 모릅니다.
/// 전용 대화창은 <see cref="PortraitDialogueWindow"/>이고, 기존 튜토리얼 창을 빌려 쓰는 <see cref="TutorialOverlayDialogueWindow"/>도 같은 틀로 바꿔 끼울 수 있습니다.
/// </remarks>
public abstract class DialogueWindow : MonoBehaviour
{
    /// <summary>대사 한 줄을 띄웁니다.</summary>
    /// <param name="speakerName">이름 칸에 쓸 이름입니다. 비어 있을 수 있습니다.</param>
    /// <param name="role">이름 위에 쓸 보직입니다. 비어 있을 수 있습니다.</param>
    /// <param name="text">대사입니다.</param>
    /// <param name="portrait">초상화입니다. 없으면 null입니다.</param>
    public abstract void ShowLine(string speakerName, string role, string text, Sprite portrait);

    /// <summary>대화창을 내립니다.</summary>
    public abstract void Hide();
}
