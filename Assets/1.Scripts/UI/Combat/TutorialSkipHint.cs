using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 챕터형 튜토리얼이 진행되는 동안 화면에 건너뛰기 키 안내("[P] 튜토리얼 건너뛰기")를 띄웁니다.
/// </summary>
/// <remarks>
/// 대화창과 따로 둡니다. 대사가 없는 챕터에서도 보여야 해서입니다. 키는 진행 중인 시퀀스(<see cref="TutorialSequence.SkipKey"/>)를 따르고,
/// 건너뛸 수 없는 시퀀스(키 None)거나 튜토리얼이 끝나면 숨깁니다. 표시는 이 오브젝트가 아니라 <see cref="m_content"/>를 켜고 끕니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class TutorialSkipHint : MonoBehaviour
{
    private const string ContentName = "Content";
    private const string TextName = "Content/Text";

    [Tooltip("켜고 끌 표시 루트입니다. 비어 있으면 자식 'Content'를 찾습니다.")]
    [SerializeField] private GameObject m_content;

    [Tooltip("안내 텍스트입니다. 비어 있으면 'Content/Text'를 찾습니다.")]
    [SerializeField] private TMP_Text m_text;

    [Tooltip("안내 문구입니다. {0}에 키 이름이 들어갑니다.")]
    [SerializeField] private string m_format = "[{0}] 튜토리얼 건너뛰기";

    private Key m_shownKey = Key.None;

    private void Awake()
    {
        if (m_content == null)
        {
            Transform content = transform.Find(ContentName);
            m_content = content != null ? content.gameObject : null;
        }

        if (m_text == null)
        {
            Transform text = transform.Find(TextName);
            m_text = text != null ? text.GetComponent<TMP_Text>() : null;
        }

        SetVisible(false);
    }

    private void LateUpdate()
    {
        TutorialFlowDirector director = TutorialFlowDirector.Instance;
        Key key = director != null && director.IsRunning ? director.SkipKey : Key.None;
        if (key != m_shownKey && key != Key.None && m_text != null)
        {
            m_text.text = string.Format(m_format, key.ToString());
        }

        m_shownKey = key;
        SetVisible(key != Key.None);
    }

    private void SetVisible(bool visible)
    {
        if (m_content != null && m_content.activeSelf != visible)
        {
            m_content.SetActive(visible);
        }
    }
}
