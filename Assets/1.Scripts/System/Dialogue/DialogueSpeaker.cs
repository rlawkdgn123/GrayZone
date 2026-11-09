using UnityEngine;

/// <summary>
/// 대사를 말하는 인물 한 명의 정보입니다. 대사 줄의 화자 칸에서 고릅니다.
/// </summary>
/// <remarks>
/// 플레이어블 캐릭터(나린, 청솔, 서하)는 <see cref="Character"/>를 지정해 그 캐릭터 머리 위에 말풍선을 띄웁니다.
/// 기지 NPC나 무전처럼 화면에 없는 인물은 <see cref="PlayerbleCharacterId.Unknown"/>으로 두면 말풍선 없이 대화창만 띄웁니다.
/// 새 인물은 이 에셋을 하나 만들면 코드 수정 없이 화자 선택지에 추가됩니다.
/// </remarks>
[CreateAssetMenu(fileName = "DialogueSpeaker", menuName = "GrayZone/Dialogue/Dialogue Speaker")]
public sealed class DialogueSpeaker : ScriptableObject
{
    [Tooltip("대화창 이름 칸에 쓸 이름입니다. 비우면 연결한 캐릭터의 표시 이름을 씁니다.")]
    [SerializeField] private string m_displayName;

    [Tooltip("이름 위에 작게 쓰는 보직입니다(예: 쉘터 리더). 선택 항목입니다.")]
    [SerializeField] private string m_role;

    [Tooltip("대화창 초상화입니다. 비우면 연결한 캐릭터의 HUD 초상화를 씁니다.")]
    [SerializeField] private Sprite m_portrait;

    [Tooltip("연결할 플레이어블 캐릭터입니다. 지정하면 그 캐릭터 머리 위에 말풍선을 띄웁니다. 기지 NPC는 Unknown으로 둡니다.")]
    [SerializeField] private PlayerbleCharacterId m_character;

    /// <summary>대화창 이름 칸에 쓸 이름입니다. 비어 있을 수 있습니다.</summary>
    public string DisplayName => m_displayName;

    /// <summary>이름 위에 쓰는 보직입니다. 비어 있을 수 있습니다.</summary>
    public string Role => m_role;

    /// <summary>대화창 초상화입니다. 없으면 null입니다.</summary>
    public Sprite Portrait => m_portrait;

    /// <summary>연결한 플레이어블 캐릭터입니다. 말풍선이 없는 인물은 Unknown입니다.</summary>
    public PlayerbleCharacterId Character => m_character;
}
