using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 초상화, 보직, 이름, 대사를 띄우는 대화창입니다(Figma `Tutorial_text`).
/// </summary>
/// <remarks>
/// 다른 전투 HUD와 같은 방식입니다: UI를 만들지 않고, 씬에 미리 배치된 자식을 이름으로 찾아 값만 채웁니다.
/// <para>
/// 대사 뒤 반투명 박스는 처음부터 씬에 배치한 크기로 나오고, 폭은 바꾸지 않습니다. 대사가 대사 칸 폭을 넘으면
/// 아래로 줄을 바꾸고, 늘어난 줄 수만큼 박스 높이만 키웁니다. 박스는 씬에 배치한 가운데 높이를 지킨 채 위아래로 함께 커지고,
/// 대사 칸은 박스 위쪽에 붙어 있어 박스와 함께 올라갑니다. 박스의 pivot과 상관없이 같습니다.
/// </para>
/// 창이 나타날 때와 사라질 때 <see cref="m_fadeSeconds"/> 동안 페이드합니다. 줄이 바뀔 때도 사라졌다가 새 줄로 다시 나타납니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class PortraitDialogueWindow : DialogueWindow
{
    private const string ContentName = "Content";
    private const string BaseName = "Content/Base";
    private const string TextName = "Content/Base/Text";
    private const string PortraitName = "Content/Portrait";
    private const string RoleName = "Content/Role";
    private const string RoleLineName = "Content/RoleLine";
    private const string NameName = "Content/Name";

    [Header("References (비워두면 자식 이름으로 자동 탐색)")]
    [Tooltip("표시를 켜고 끌 루트입니다. 비어 있으면 자식 'Content'를 찾습니다.")]
    [SerializeField] private GameObject m_content;

    [Tooltip("페이드용 CanvasGroup입니다. 비어 있으면 Content에서 찾고, 없으면 페이드 없이 켜고 끕니다.")]
    [SerializeField] private CanvasGroup m_canvasGroup;

    [Tooltip("대사 뒤 반투명 박스입니다. 줄이 늘면 높이만 커집니다. 비어 있으면 'Content/Base'를 찾습니다.")]
    [SerializeField] private RectTransform m_base;

    [Tooltip("대사 텍스트입니다. 박스의 자식이어야 합니다. 폭은 씬에 배치한 값을 그대로 씁니다. 비어 있으면 'Content/Base/Text'를 찾습니다.")]
    [SerializeField] private TMP_Text m_text;

    [Tooltip("초상화 이미지입니다. 비어 있으면 'Content/Portrait'를 찾습니다.")]
    [SerializeField] private Image m_portrait;

    [Tooltip("보직 텍스트입니다. 비어 있으면 'Content/Role'을 찾습니다.")]
    [SerializeField] private TMP_Text m_role;

    [Tooltip("보직 아래 노란 선입니다. 보직이 없으면 함께 숨깁니다. 비어 있으면 'Content/RoleLine'을 찾습니다.")]
    [SerializeField] private GameObject m_roleLine;

    [Tooltip("이름 텍스트입니다. 비어 있으면 'Content/Name'을 찾습니다.")]
    [SerializeField] private TMP_Text m_name;

    [Header("Fade")]
    [Min(0.0f)]
    [Tooltip("창이 나타나고 사라질 때 페이드 시간(초)입니다. 0이면 바로 켜고 끕니다.")]
    [SerializeField] private float m_fadeSeconds = 0.2f;

    [Header("Typing")]
    [Min(0.0f)]
    [Tooltip("초당 표시 글자 수입니다. 0이면 한 번에 표시합니다.")]
    [SerializeField] private float m_charactersPerSecond = 30.0f;

    private Coroutine m_typingRoutine;
    private Coroutine m_fadeRoutine;

    /// <summary>씬에 배치된 박스 높이입니다. 한 줄일 때의 높이로 쓰며, 처음 찾을 때 한 번 기억합니다.</summary>
    private float m_baseHeight = -1.0f;

    /// <summary>씬에 배치된 대사 칸 높이입니다. 줄이 늘어난 만큼을 재는 기준입니다.</summary>
    private float m_textHeight = -1.0f;

    /// <summary>씬에 배치된 박스 위치입니다. 박스가 커져도 가운데 높이가 그대로이도록 옮길 때 기준으로 씁니다.</summary>
    private Vector2 m_basePosition;

    private void Reset()
    {
        AutoFindReferences();
    }

    private void Awake()
    {
        AutoFindReferences();
        SetVisibleImmediate(false);
    }

    private void OnDisable()
    {
        StopTyping();
        StopFade();
    }

    /// <inheritdoc/>
    public override void ShowLine(string speakerName, string role, string text, Sprite portrait)
    {
        AutoFindReferences();
        StopTyping();

        // 이미 떠 있으면 사라졌다가 새 줄로 다시 나타나게 합니다. 꺼져 있으면 바로 채우고 나타납니다.
        bool isVisible = m_content != null && m_content.activeSelf && m_canvasGroup != null && m_canvasGroup.alpha > 0.0f;
        if (isVisible && m_fadeSeconds > 0.0f && isActiveAndEnabled)
        {
            StopFade();
            m_fadeRoutine = StartCoroutine(SwapRoutine(speakerName, role, text, portrait));
            return;
        }

        ApplyLine(speakerName, role, text, portrait);
        FadeTo(1.0f);
        if (m_text != null)
        {
            StartTyping();
        }
    }

    private IEnumerator SwapRoutine(string speakerName, string role, string text, Sprite portrait)
    {
        yield return FadeRoutine(0.0f, false);

        ApplyLine(speakerName, role, text, portrait);
        if (m_text != null)
        {
            StartTyping();
        }

        yield return FadeRoutine(1.0f, true);
    }

    private void ApplyLine(string speakerName, string role, string text, Sprite portrait)
    {
        if (m_portrait != null)
        {
            m_portrait.sprite = portrait;
            m_portrait.enabled = portrait != null;
        }

        bool hasRole = !string.IsNullOrWhiteSpace(role);
        if (m_role != null)
        {
            m_role.text = hasRole ? role : string.Empty;
        }

        if (m_roleLine != null)
        {
            m_roleLine.SetActive(hasRole);
        }

        if (m_name != null)
        {
            m_name.text = speakerName ?? string.Empty;
        }

        if (m_text != null)
        {
            m_text.text = text ?? string.Empty;
            FitBaseHeight();
        }
    }

    /// <inheritdoc/>
    public override void Hide()
    {
        StopTyping();
        FadeTo(0.0f);
    }

    /// <summary>
    /// 대사 줄 수에 맞춰 박스와 대사 칸의 높이를 바꾸고, 박스 가운데가 제자리에 있도록 박스를 옮깁니다.
    /// 한 줄이면 씬에 배치한 크기와 위치 그대로입니다.
    /// </summary>
    private void FitBaseHeight()
    {
        if (m_base == null)
        {
            return;
        }

        RectTransform textRect = m_text.rectTransform;
        float width = textRect.rect.width;

        // 한 줄 높이를 기준으로 늘어난 줄 수만큼만 키웁니다. 폰트 줄 높이와 Figma 글자 상자가 달라도 한 줄일 때는 배치한 크기 그대로입니다.
        float height = m_text.GetPreferredValues(m_text.text, width, float.PositiveInfinity).y;
        float oneLine = m_text.GetPreferredValues("가", width, float.PositiveInfinity).y;
        float extra = Mathf.Max(0.0f, height - oneLine);

        textRect.sizeDelta = new Vector2(textRect.sizeDelta.x, m_textHeight + extra);
        m_base.sizeDelta = new Vector2(m_base.sizeDelta.x, m_baseHeight + extra);

        // 가운데는 위치 + (0.5 - pivot) x 높이입니다. 높이가 extra만큼 늘어도 가운데가 같으려면 위치를 (pivot - 0.5) x extra만큼 옮깁니다.
        // pivot이 위쪽(1)이면 extra의 절반만큼 올라가, 박스가 위아래로 절반씩 커집니다.
        m_base.anchoredPosition = m_basePosition + new Vector2(0.0f, (m_base.pivot.y - 0.5f) * extra);
    }

    private void FadeTo(float target)
    {
        if (m_content == null)
        {
            return;
        }

        StopFade();
        if (target > 0.0f && !m_content.activeSelf)
        {
            if (m_canvasGroup != null)
            {
                m_canvasGroup.alpha = 0.0f;
            }

            m_content.SetActive(true);
        }

        if (m_canvasGroup == null || m_fadeSeconds <= 0.0f || !isActiveAndEnabled)
        {
            SetVisibleImmediate(target > 0.0f);
            return;
        }

        if (!m_content.activeSelf)
        {
            return;
        }

        m_fadeRoutine = StartCoroutine(FadeRoutine(target, true));
    }

    /// <param name="finish">켜면 끝난 뒤 표시 상태를 확정합니다(0이면 끔). 줄 교체 중간처럼 이어서 다시 나타날 때는 끕니다.</param>
    private IEnumerator FadeRoutine(float target, bool finish)
    {
        // 사라지는 중에 다음 줄이 오면 지금 투명도에서 다시 올라오므로, 줄이 바뀔 때 깜빡이지 않습니다.
        float start = m_canvasGroup.alpha;
        float duration = m_fadeSeconds * Mathf.Abs(target - start);
        float elapsed = 0.0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            m_canvasGroup.alpha = Mathf.Lerp(start, target, elapsed / duration);
            yield return null;
        }

        if (!finish)
        {
            m_canvasGroup.alpha = target;
            yield break;
        }

        m_fadeRoutine = null;
        SetVisibleImmediate(target > 0.0f);
    }

    private void StopFade()
    {
        if (m_fadeRoutine != null)
        {
            StopCoroutine(m_fadeRoutine);
            m_fadeRoutine = null;
        }
    }

    private void StartTyping()
    {
        StopTyping();
        if (m_charactersPerSecond <= 0.0f || !isActiveAndEnabled)
        {
            m_text.maxVisibleCharacters = int.MaxValue;
            return;
        }

        m_typingRoutine = StartCoroutine(TypeRoutine());
    }

    private void StopTyping()
    {
        if (m_typingRoutine != null)
        {
            StopCoroutine(m_typingRoutine);
            m_typingRoutine = null;
        }
    }

    private IEnumerator TypeRoutine()
    {
        m_text.ForceMeshUpdate();
        int total = m_text.textInfo.characterCount;
        m_text.maxVisibleCharacters = 0;

        // 컷신이나 안내 중에 게임 시간이 멈춰도 글자가 이어서 나오도록 실제 시간으로 셉니다.
        float elapsed = 0.0f;
        while (m_text.maxVisibleCharacters < total)
        {
            elapsed += Time.unscaledDeltaTime;
            m_text.maxVisibleCharacters = Mathf.Min(total, Mathf.FloorToInt(elapsed * m_charactersPerSecond));
            yield return null;
        }

        m_typingRoutine = null;
    }

    private void SetVisibleImmediate(bool visible)
    {
        if (m_canvasGroup != null)
        {
            m_canvasGroup.alpha = visible ? 1.0f : 0.0f;
        }

        if (m_content != null && m_content.activeSelf != visible)
        {
            m_content.SetActive(visible);
        }
    }

    private void AutoFindReferences()
    {
        if (m_content == null)
        {
            Transform content = transform.Find(ContentName);
            m_content = content != null ? content.gameObject : null;
        }

        if (m_canvasGroup == null && m_content != null)
        {
            m_canvasGroup = m_content.GetComponent<CanvasGroup>();
        }

        if (m_base == null)
        {
            m_base = transform.Find(BaseName) as RectTransform;
        }

        if (m_text == null)
        {
            m_text = FindChild<TMP_Text>(TextName);
        }

        if (m_portrait == null)
        {
            m_portrait = FindChild<Image>(PortraitName);
        }

        if (m_role == null)
        {
            m_role = FindChild<TMP_Text>(RoleName);
        }

        if (m_roleLine == null)
        {
            Transform line = transform.Find(RoleLineName);
            m_roleLine = line != null ? line.gameObject : null;
        }

        if (m_name == null)
        {
            m_name = FindChild<TMP_Text>(NameName);
        }

        // 한 줄일 때 크기는 씬에 배치한 값입니다. 줄 수에 따라 바꾸기 전에 한 번만 기억합니다.
        if (m_baseHeight < 0.0f && m_base != null)
        {
            m_baseHeight = m_base.sizeDelta.y;
            m_basePosition = m_base.anchoredPosition;
        }

        if (m_textHeight < 0.0f && m_text != null)
        {
            m_textHeight = m_text.rectTransform.sizeDelta.y;
        }
    }

    private T FindChild<T>(string path) where T : Component
    {
        Transform child = transform.Find(path);
        return child != null ? child.GetComponent<T>() : null;
    }
}
