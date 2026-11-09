using System;
using UnityEngine;

/// <summary>
/// 대사 한 줄을 다음 줄로 넘기는 방식입니다.
/// </summary>
public enum DialogueAdvanceMode
{
    /// <summary>플레이어가 넘김 입력(기본 F키)을 해야 넘어갑니다. 조작이 막힌 컷신용입니다.</summary>
    Input = 0,

    /// <summary>표시 시간이 지나면 저절로 넘어갑니다. 조작 중에 띄우는 대사용입니다.</summary>
    Auto = 1,
}

/// <summary>
/// 대화창과 말풍선에 함께 띄우는 대사 한 줄입니다.
/// </summary>
/// <remarks>
/// 튜토리얼 전용이 아니라 대사를 띄우는 어디서든 쓰는 데이터입니다. 화자가 플레이어블 캐릭터와 연결돼 있으면 그 캐릭터 머리 위에도 말풍선을 띄웁니다.
/// 대사가 비어 있으면 아무것도 띄우지 않습니다(<see cref="HasText"/>). 대사 사이에 이벤트만 끼워 넣는 줄에 씁니다.
/// </remarks>
[Serializable]
public struct DialogueLine
{
    [Tooltip("말하는 인물입니다. 비우면 이름 없이 대화창만 띄웁니다. 기지 NPC처럼 캐릭터와 연결되지 않은 인물은 말풍선이 없습니다.")]
    [SerializeField] private DialogueSpeaker m_speaker;

    [Tooltip("이 줄에서만 바꿔 쓸 초상화입니다(표정 등). 선택 항목이며, 비우면 화자의 초상화를 씁니다.")]
    [SerializeField] private Sprite m_portrait;

    [Tooltip("대화창에 띄울 대사입니다.")]
    [TextArea(2, 4)]
    [SerializeField] private string m_text;

    [Tooltip("말풍선에 띄울 짧은 문구입니다. 선택 항목이며, 비우면 대화창 대사를 그대로 씁니다.")]
    [TextArea(1, 3)]
    [SerializeField] private string m_bubbleText;

    [Tooltip("다음 줄로 넘기는 방식입니다. 컷신은 Input, 조작 중 대사는 Auto를 씁니다.")]
    [SerializeField] private DialogueAdvanceMode m_advanceMode;

    [Tooltip("Auto 줄의 표시 시간(초)입니다. 0이면 글자 수로 계산합니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_autoSeconds;

    /// <summary>말하는 인물입니다. 없으면 null입니다.</summary>
    public DialogueSpeaker Speaker => m_speaker;

    /// <summary>이 줄에서만 바꿔 쓸 초상화입니다. 없으면 null입니다.</summary>
    public Sprite Portrait => m_portrait;

    /// <summary>대화창에 띄울 대사입니다.</summary>
    public string Text => m_text;

    /// <summary>띄울 대사가 있는지 여부입니다. 없으면 대화창과 말풍선을 띄우지 않습니다.</summary>
    public bool HasText => !string.IsNullOrWhiteSpace(m_text);

    /// <summary>말풍선에 띄울 문구입니다. 따로 적지 않았으면 대화창 대사입니다.</summary>
    public string BubbleText => string.IsNullOrWhiteSpace(m_bubbleText) ? m_text : m_bubbleText;

    /// <summary>다음 줄로 넘기는 방식입니다.</summary>
    public DialogueAdvanceMode AdvanceMode => m_advanceMode;

    /// <summary>Auto 줄의 표시 시간(초)입니다. 0이면 글자 수로 계산합니다.</summary>
    public float AutoSeconds => m_autoSeconds;
}
