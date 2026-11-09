using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>튜토리얼 페이지를 넘기는 입력 방식입니다.</summary>
/// <remarks>값은 페이지 설정에 숫자로 저장됩니다. 기존 항목의 순서를 바꾸지 말고 끝에 추가하세요.</remarks>
public enum TutorialAdvanceInput
{
    None = 0,

    /// <summary>상호작용 액션을 짧게 눌렀다 뗍니다. 키 바인딩을 따라갑니다.</summary>
    InteractTap = 1,

    /// <summary>지정한 키를 지정한 시간 동안 유지합니다.</summary>
    KeyHold = 2,
}

/// <summary>
/// 튜토리얼 한 페이지의 표시 내용과 다음 페이지로 넘어가는 조건입니다.
/// </summary>
/// <remarks>
/// 넘김 조건은 이벤트 대기와 입력 두 가지이며, 각각 켜고 끌 수 있습니다.
/// 둘 다 켜면 이벤트가 먼저 와야 하고, 입력은 그 뒤부터 셉니다. 이벤트 전에 누르고 있던 입력은 인정하지 않습니다.
/// 둘 다 끄면 이 페이지는 스스로 넘어가지 않습니다(<see cref="TutorialManager.End"/>로만 닫힘).
///
/// 이벤트는 페이지 번호로 옵니다. 게임 코드가 <see cref="TutorialManager.NotifyEventEnd"/>에 이 페이지의 번호를 넘기면 잠금이 풀립니다.
/// </remarks>
[Serializable]
public struct TutorialPage
{
    [Header("Content")]
    [Tooltip("페이지 제목입니다. 비어 있으면 템플릿에 적힌 제목을 그대로 둡니다.")]
    [SerializeField] private string m_title;

    [TextArea(3, 8)]
    [Tooltip("페이지 본문입니다. 줄바꿈을 그대로 씁니다.")]
    [SerializeField] private string m_body;

    [Tooltip("페이지 이미지입니다. 비어 있으면 이미지 오브젝트를 끕니다.")]
    [SerializeField] private Sprite m_image;

    [Tooltip("하단 안내 문구입니다. 키 이름은 여기에 직접 적습니다(예: \"[Z] 3초 유지\").")]
    [SerializeField] private string m_hint;

    [Tooltip("본문 사이에 끼워 넣을 키 아이콘입니다(예: H, F). 비어 있으면 아이콘을 끕니다. " +
             "본문에는 아이콘 자리만큼 공백을 넣어 둡니다.")]
    [SerializeField] private Sprite m_keyIcon;

    [Tooltip("키 아이콘의 위치(px)입니다. 패널 왼쪽 위 기준이며 y는 아래로 갈수록 커집니다(Figma 좌표와 같음).")]
    [SerializeField] private Vector2 m_keyIconPosition;

    [Tooltip("키 아이콘의 크기(px)입니다.")]
    [SerializeField] private Vector2 m_keyIconSize;

    [Tooltip("이 페이지만 공용 템플릿 대신 띄울 UI 프리팹입니다. 비워두면 공용 템플릿을 씁니다. " +
             "프리팹 안에 Title/Body/Hint/Image 이름의 자식이 있으면 같은 내용을 채웁니다.")]
    [SerializeField] private GameObject m_overridePrefab;

    [Header("Next Page Condition")]
    [Tooltip("켜면 이 페이지 번호로 이벤트가 올 때까지 페이지가 잠깁니다.")]
    [SerializeField] private bool m_waitEvent;

    [Tooltip("페이지를 넘길 입력입니다. 이벤트 대기도 켜져 있으면 이벤트가 온 뒤부터 입력을 셉니다.\n" +
             "None: 입력으로 넘기지 않음\n" +
             "InteractTap: 상호작용 액션 단일 탭(키 바인딩을 따라감). 길게 누르면 탭으로 보지 않아, 트랩 설치 홀드와 겹치지 않습니다.\n" +
             "KeyHold: 아래 키를 지정 시간 유지")]
    [SerializeField] private TutorialAdvanceInput m_advanceInput;

    [Tooltip("KeyHold일 때 페이지를 넘길 키입니다.")]
    [SerializeField] private Key m_key;

    [Min(0.0f)]
    [Tooltip("KeyHold일 때 키를 유지해야 하는 시간(초)입니다. 0이면 누르는 순간 넘어갑니다. 중간에 떼면 처음부터 다시 셉니다.")]
    [SerializeField] private float m_holdSeconds;

    [Tooltip("켜면 이 페이지의 넘김 입력을 채웠을 때 방어전을 시작하고 튜토리얼을 끝냅니다. 마지막 페이지(예: X 3초 유지로 시작)에 씁니다.")]
    [SerializeField] private bool m_startDefenseOnAdvance;

    [Header("Input Lock")]
    [Tooltip("조건이 해결되기 전까지 닫아 둘 플레이어 입력입니다. 이벤트 대기 페이지는 이벤트가 오면 열리고, " +
             "이벤트 대기가 없는 페이지는 다음 페이지로 넘어갈 때 열립니다. 튜토리얼이 끝나면 모두 열립니다.")]
    [SerializeField] private PlayerInputLock m_lockedInputs;

    /// <summary>페이지 제목입니다.</summary>
    public string Title => m_title;

    /// <summary>페이지 본문입니다.</summary>
    public string Body => m_body;

    /// <summary>페이지 이미지입니다. 없으면 null입니다.</summary>
    public Sprite Image => m_image;

    /// <summary>하단 안내 문구입니다.</summary>
    public string Hint => m_hint;

    /// <summary>공용 템플릿 대신 띄울 프리팹입니다. 없으면 null입니다.</summary>
    public GameObject OverridePrefab => m_overridePrefab;

    /// <summary>이벤트가 올 때까지 잠기는 페이지인지 여부입니다.</summary>
    public bool WaitEvent => m_waitEvent;

    /// <summary>페이지를 넘길 입력 방식입니다.</summary>
    public TutorialAdvanceInput AdvanceInput => m_advanceInput;

    /// <summary>입력으로 넘기는 페이지인지 여부입니다.</summary>
    public bool WaitInput => m_advanceInput != TutorialAdvanceInput.None;

    /// <summary><see cref="TutorialAdvanceInput.KeyHold"/>일 때의 넘김 키입니다.</summary>
    public Key Key => m_key;

    /// <summary><see cref="TutorialAdvanceInput.KeyHold"/>일 때의 유지 시간(초)입니다. 0이면 누르는 순간입니다.</summary>
    public float HoldSeconds => m_holdSeconds;

    /// <summary>넘김 입력을 채우면 방어전을 시작하고 튜토리얼을 끝내는 페이지인지 여부입니다.</summary>
    public bool StartDefenseOnAdvance => m_startDefenseOnAdvance;

    /// <summary>조건 해결 전까지 닫아 둘 입력입니다.</summary>
    public PlayerInputLock LockedInputs => m_lockedInputs;

    /// <summary>본문 사이에 끼워 넣을 키 아이콘입니다. 없으면 null입니다.</summary>
    public Sprite KeyIcon => m_keyIcon;

    /// <summary>키 아이콘 위치(px, 패널 왼쪽 위 기준, y는 아래쪽이 양수)입니다.</summary>
    public Vector2 KeyIconPosition => m_keyIconPosition;

    /// <summary>키 아이콘 크기(px)입니다.</summary>
    public Vector2 KeyIconSize => m_keyIconSize;

    /// <summary>
    /// 코드에서 기본 페이지를 만들 때 씁니다. 인스펙터에서 새로 추가한 페이지에는 적용되지 않습니다.
    /// </summary>
    public static TutorialPage CreateEventPage(string body, string hint)
    {
        return new TutorialPage
        {
            m_body = body,
            m_hint = hint,
            m_waitEvent = true,
        };
    }

    /// <summary>
    /// 튜토리얼 창에 띄울 표시 전용 페이지를 만듭니다. 넘김 조건은 없습니다.
    /// </summary>
    /// <remarks>2회차부터 튜토리얼 없이 시작할 때 시작 안내(Z 3초)를 튜토리얼 창에 띄우는 데 씁니다.</remarks>
    public static TutorialPage CreateDisplayPage(
        string title,
        string body,
        string hint,
        Sprite keyIcon,
        Vector2 keyIconPosition,
        Vector2 keyIconSize)
    {
        return new TutorialPage
        {
            m_title = title,
            m_body = body,
            m_hint = hint,
            m_keyIcon = keyIcon,
            m_keyIconPosition = keyIconPosition,
            m_keyIconSize = keyIconSize,
        };
    }

    /// <summary>
    /// 튜토리얼 창을 대화창으로 빌려 쓸 때 띄울 표시 전용 페이지를 만듭니다. 넘김 조건은 없습니다.
    /// </summary>
    /// <remarks>제목 칸에 화자 이름, 본문에 대사, 이미지 칸에 초상화를 넣습니다(<see cref="TutorialOverlayDialogueWindow"/>).</remarks>
    public static TutorialPage CreateDialoguePage(string speakerName, string text, Sprite portrait)
    {
        return new TutorialPage
        {
            m_title = speakerName,
            m_body = text,
            m_hint = string.Empty,
            m_image = portrait,
        };
    }

    /// <summary>
    /// 튜토리얼 이벤트(<see cref="TutorialEvents.ShowWindow"/>)가 띄울 표시 전용 페이지를 만듭니다. 넘김 조건은 없습니다.
    /// </summary>
    public static TutorialPage CreateWindowPage(string title, string body, Sprite image, string hint)
    {
        return new TutorialPage
        {
            m_title = title,
            m_body = body,
            m_image = image,
            m_hint = hint,
        };
    }

    /// <summary>인스펙터에서 음수로 입력된 유지 시간을 바로잡습니다.</summary>
    public void Sanitize()
    {
        m_holdSeconds = Mathf.Max(0.0f, m_holdSeconds);
    }
}
