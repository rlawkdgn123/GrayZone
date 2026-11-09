using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 튜토리얼 페이지를 화면에 그리는 공용 템플릿입니다. 페이지 순서와 넘김 조건은 <see cref="TutorialManager"/>가 정합니다.
/// </summary>
/// <remarks>
/// 다른 전투 HUD와 같은 방식입니다: 이 컴포넌트는 UI를 만들지 않고, 씬에 미리 배치된 자식을
/// 이름으로 찾아 값(텍스트, 이미지, 표시 여부)만 채웁니다. 레이아웃은 씬에서 조정합니다(Figma `Tutorial HUD` 기준).
///
/// 튜토리얼이 떠 있는 동안 좌측 공지(<see cref="m_noticeRoot"/>)도 함께 띄웁니다. 공지 문구는 씬에 적어 둔 그대로입니다.
///
/// 페이지에 <see cref="TutorialPage.OverridePrefab"/>이 있으면 공용 패널을 끄고 그 프리팹을 이 오브젝트 아래에 띄웁니다.
/// 프리팹 안에 Title/Body/Hint/Image/KeyIcon 이름의 자식이 있으면 같은 내용을 채웁니다.
/// </remarks>
[DisallowMultipleComponent]
public class DefenseTutorialOverlay : MonoBehaviour
{
    private const string PanelName = "Panel";
    private const string TitleName = "Title";
    private const string BodyName = "Body";
    private const string HintName = "Hint";
    private const string ImageName = "Image";
    private const string KeyIconName = "KeyIcon";
    private const string NoticeName = "Notice";
    private const string KeyGaugeFillName = "KeyGauge/Fill";

    [Header("References (비워두면 자식 이름으로 자동 탐색)")]
    [Tooltip("안내 패널 루트입니다. 이 오브젝트를 켜고 끄는 것으로 표시를 전환합니다. 비어 있으면 자식 'Panel'을 찾습니다.")]
    [SerializeField] private GameObject m_panelRoot;

    [Tooltip("페이지 제목 텍스트입니다. 선택 항목이며, 비어 있으면 패널 아래 'Title'을 찾습니다.")]
    [SerializeField] private TMP_Text m_titleText;

    [Tooltip("안내 본문 텍스트입니다. 비어 있으면 자식 'Body'를 찾습니다.")]
    [SerializeField] private TMP_Text m_bodyText;

    [Tooltip("하단 안내 텍스트입니다. 비어 있으면 자식 'Hint'를 찾습니다.")]
    [SerializeField] private TMP_Text m_hintText;

    [Tooltip("페이지 이미지입니다. 선택 항목이며, 비어 있으면 패널 아래 'Image'를 찾습니다.")]
    [SerializeField] private Image m_image;

    [Tooltip("본문 사이에 끼워 넣는 키 아이콘입니다. 선택 항목이며, 비어 있으면 패널 아래 'KeyIcon'을 찾습니다.")]
    [SerializeField] private Image m_keyIcon;

    [Tooltip("넘김 키 게이지의 채움 링입니다(Image, Filled / Radial 360). 선택 항목이며, 비어 있으면 패널 아래 'KeyGauge/Fill'을 찾습니다.")]
    [SerializeField] private Image m_keyGaugeFill;

    [Tooltip("튜토리얼과 함께 띄울 좌측 공지 루트입니다. 선택 항목이며, 비어 있으면 자식 'Notice'를 찾습니다.")]
    [SerializeField] private GameObject m_noticeRoot;

    [Header("Text")]
    [Min(0.0f)]
    [Tooltip("본문 줄 간격(px)입니다. Figma 본문은 39px입니다. 0이면 폰트 기본 줄 간격을 씁니다.")]
    [SerializeField] private float m_bodyLineHeight = 39.0f;

    [Header("Compact")]
    [Range(0.2f, 1.0f)]
    [Tooltip("압축 표시(2회차 시작 안내)에서 패널 높이를 원래 높이의 몇 배로 줄일지입니다. 위쪽은 그대로 두고 아래를 잘라냅니다.")]
    [SerializeField] private float m_compactHeightRatio = 0.5f;

    /// <summary>현재 페이지를 대신 그리고 있는 프리팹 인스턴스입니다.</summary>
    private GameObject m_overrideInstance;

    /// <summary>압축 표시 중인지 여부입니다.</summary>
    private bool m_isCompact;

    /// <summary>원래 레이아웃을 기억했는지 여부입니다. 압축 표시에서 되돌릴 때 씁니다.</summary>
    private bool m_hasFullLayout;
    private Vector2 m_fullPanelSize;
    private Rect m_fullPanelUv;
    private Vector2 m_fullHintPosition;
    private Vector2 m_fullGaugePosition;

    /// <summary>Awake 전에 Show가 불렸는지입니다. 오브젝트가 꺼진 채 시작해 Awake가 늦게 돌 때 표시를 덮어쓰지 않기 위해 씁니다.</summary>
    private bool m_showRequested;

    /// <summary>템플릿에 적혀 있던 공용 패널 제목입니다. 대화창으로 빌려 쓰며 화자 이름으로 바꾼 뒤 되돌릴 때 씁니다.</summary>
    private string m_templateTitle;

    /// <summary>안내가 현재 표시 중인지 여부입니다.</summary>
    public bool IsShown => (m_panelRoot != null && m_panelRoot.activeSelf) || m_overrideInstance != null;

    private void Reset()
    {
        AutoFindReferences();
    }

    private void Awake()
    {
        AutoFindReferences();

        // 첫 페이지는 TutorialManager가 띄웁니다. 매니저가 없으면 안내는 뜨지 않습니다.
        if (!m_showRequested)
        {
            SetPanelVisible(false);
            SetNoticeVisible(false);
        }
    }

    /// <summary>
    /// 페이지 내용을 표시합니다. 이전 페이지가 띄운 프리팹은 제거합니다.
    /// </summary>
    public void Show(TutorialPage page)
    {
        m_showRequested = true;
        AutoFindReferences();
        ClearOverride();
        ApplyCompactLayout(false);
        SetNoticeVisible(true);

        if (page.OverridePrefab != null)
        {
            SetPanelVisible(false);
            m_overrideInstance = Instantiate(page.OverridePrefab, transform);
            FillByName(m_overrideInstance.transform, page);
            return;
        }

        ApplyPage(m_titleText, m_bodyText, m_hintText, m_image, m_keyIcon, page);
        SetPanelVisible(true);
    }

    /// <summary>
    /// 같은 패널을 아래쪽을 잘라낸 압축 크기로 띄웁니다. 2회차부터 튜토리얼 없이 시작할 때 시작 안내(Z 3초)에 씁니다.
    /// </summary>
    /// <remarks>
    /// 패널 위쪽 위치는 그대로 두고 높이만 <see cref="m_compactHeightRatio"/>배로 줄입니다. 배경 RawImage는 늘리지 않고
    /// 위쪽 부분만 보이도록 UV를 잘라, 원래 창의 아래를 잘라낸 모양이 되게 합니다. 하단 안내 문구와 키 게이지는
    /// 줄어든 만큼 위로 올립니다. 다음 <see cref="Show"/>나 <see cref="Hide"/>에서 원래 크기로 돌아갑니다.
    /// </remarks>
    public void ShowCompact(TutorialPage page)
    {
        m_showRequested = true;
        AutoFindReferences();
        ClearOverride();
        ApplyCompactLayout(true);
        ApplyPage(m_titleText, m_bodyText, m_hintText, m_image, m_keyIcon, page);
        SetPanelVisible(true);
    }

    /// <summary>
    /// 이 창을 대화창으로 빌려 쓸 때 대사를 띄웁니다. 좌측 공지와 넘김 키 게이지는 비웁니다.
    /// </summary>
    /// <remarks>
    /// 제목이 비어 있으면 <see cref="ApplyPage"/>가 템플릿 제목("튜토리얼")을 넣으므로, 화자 이름이 없을 때는 제목을 직접 비웁니다.
    /// </remarks>
    public void ShowDialogue(TutorialPage page)
    {
        Show(page);
        SetNoticeVisible(false);
        SetGaugeProgress(0.0f);

        if (m_titleText != null && string.IsNullOrEmpty(page.Title))
        {
            m_titleText.text = string.Empty;
        }
    }

    /// <summary>압축 표시 중인지 여부입니다.</summary>
    public bool IsCompact => m_isCompact && IsShown;

    /// <summary>안내를 감춥니다. 띄워 둔 프리팹과 좌측 공지도 함께 감춥니다.</summary>
    public void Hide()
    {
        m_showRequested = false;
        ClearOverride();
        SetPanelVisible(false);
        SetNoticeVisible(false);
        ApplyCompactLayout(false);
    }

    /// <summary>패널 크기·배경 UV·하단 안내 위치를 압축 또는 원래 크기로 맞춥니다.</summary>
    private void ApplyCompactLayout(bool compact)
    {
        if (m_panelRoot == null || !(m_panelRoot.transform is RectTransform panel))
        {
            return;
        }

        RawImage background = m_panelRoot.GetComponent<RawImage>();
        RectTransform hint = m_hintText != null ? m_hintText.rectTransform : null;
        RectTransform gauge = m_keyGaugeFill != null ? m_keyGaugeFill.rectTransform.parent as RectTransform : null;

        if (!m_hasFullLayout)
        {
            m_hasFullLayout = true;
            m_fullPanelSize = panel.sizeDelta;
            m_fullPanelUv = background != null ? background.uvRect : new Rect(0.0f, 0.0f, 1.0f, 1.0f);
            m_fullHintPosition = hint != null ? hint.anchoredPosition : Vector2.zero;
            m_fullGaugePosition = gauge != null ? gauge.anchoredPosition : Vector2.zero;
        }

        if (m_isCompact == compact)
        {
            return;
        }

        m_isCompact = compact;
        float ratio = compact ? m_compactHeightRatio : 1.0f;
        float cut = m_fullPanelSize.y * (1.0f - ratio);

        panel.sizeDelta = new Vector2(m_fullPanelSize.x, m_fullPanelSize.y * ratio);

        if (background != null)
        {
            // UV의 y는 아래에서 위로 셉니다. 위쪽 ratio만큼만 보이게 아래쪽을 잘라냅니다.
            background.uvRect = new Rect(
                m_fullPanelUv.x,
                m_fullPanelUv.y + m_fullPanelUv.height * (1.0f - ratio),
                m_fullPanelUv.width,
                m_fullPanelUv.height * ratio);
        }

        // 하단 안내와 게이지는 패널 왼쪽 위 기준이라, 잘라낸 높이만큼 위로 올려야 줄어든 창 바닥에 붙습니다.
        if (hint != null)
        {
            hint.anchoredPosition = m_fullHintPosition + new Vector2(0.0f, cut);
        }

        if (gauge != null)
        {
            gauge.anchoredPosition = m_fullGaugePosition + new Vector2(0.0f, cut);
        }
    }

    private void ClearOverride()
    {
        if (m_overrideInstance != null)
        {
            Destroy(m_overrideInstance);
            m_overrideInstance = null;
        }
    }

    private void AutoFindReferences()
    {
        if (m_panelRoot == null)
        {
            Transform panel = transform.Find(PanelName);
            if (panel != null)
            {
                m_panelRoot = panel.gameObject;
            }
        }

        if (m_noticeRoot == null)
        {
            Transform notice = transform.Find(NoticeName);
            if (notice != null)
            {
                m_noticeRoot = notice.gameObject;
            }
        }

        Transform searchRoot = m_panelRoot != null ? m_panelRoot.transform : transform;

        if (m_titleText == null)
        {
            m_titleText = FindChild<TMP_Text>(searchRoot, TitleName);
        }

        if (m_templateTitle == null && m_titleText != null)
        {
            m_templateTitle = m_titleText.text;
        }

        if (m_bodyText == null)
        {
            m_bodyText = FindChild<TMP_Text>(searchRoot, BodyName);
        }

        if (m_hintText == null)
        {
            m_hintText = FindChild<TMP_Text>(searchRoot, HintName);
        }

        if (m_image == null)
        {
            m_image = FindChild<Image>(searchRoot, ImageName);
        }

        if (m_keyIcon == null)
        {
            m_keyIcon = FindChild<Image>(searchRoot, KeyIconName);
        }

        if (m_keyGaugeFill == null)
        {
            m_keyGaugeFill = FindChild<Image>(searchRoot, KeyGaugeFillName);
        }
    }

    private void FillByName(Transform root, TutorialPage page)
    {
        ApplyPage(
            FindChild<TMP_Text>(root, TitleName),
            FindChild<TMP_Text>(root, BodyName),
            FindChild<TMP_Text>(root, HintName),
            FindChild<Image>(root, ImageName),
            FindChild<Image>(root, KeyIconName),
            page);
    }

    private void ApplyPage(TMP_Text title, TMP_Text body, TMP_Text hint, Image image, Image keyIcon, TutorialPage page)
    {
        // 제목이 비어 있으면 템플릿에 적힌 제목("튜토리얼")을 씁니다. 대화창으로 빌려 쓰며 화자 이름으로 바꿨을 수 있어 되돌립니다.
        if (title != null)
        {
            if (!string.IsNullOrEmpty(page.Title))
            {
                title.text = page.Title;
            }
            else if (title == m_titleText && m_templateTitle != null)
            {
                title.text = m_templateTitle;
            }
        }

        if (body != null)
        {
            // Figma 본문 줄 간격(39px)을 폰트와 관계없이 맞추려고 줄 간격 태그를 앞에 붙입니다.
            body.text = m_bodyLineHeight > 0.0f
                ? $"<line-height={m_bodyLineHeight:0.##}px>{page.Body}"
                : page.Body;
        }

        if (hint != null)
        {
            hint.text = page.Hint;
        }

        if (image != null)
        {
            image.sprite = page.Image;
            image.gameObject.SetActive(page.Image != null);
        }

        ApplyKeyIcon(keyIcon, page);
    }

    /// <summary>
    /// 페이지의 키 아이콘을 지정한 위치와 크기로 놓습니다. 위치는 패널 왼쪽 위 기준이고 y는 아래쪽이 양수입니다.
    /// </summary>
    private static void ApplyKeyIcon(Image keyIcon, TutorialPage page)
    {
        if (keyIcon == null)
        {
            return;
        }

        bool hasIcon = page.KeyIcon != null;
        keyIcon.gameObject.SetActive(hasIcon);
        if (!hasIcon)
        {
            return;
        }

        keyIcon.sprite = page.KeyIcon;

        RectTransform rect = keyIcon.rectTransform;
        rect.anchorMin = new Vector2(0.0f, 1.0f);
        rect.anchorMax = new Vector2(0.0f, 1.0f);
        rect.pivot = new Vector2(0.0f, 1.0f);
        rect.anchoredPosition = new Vector2(page.KeyIconPosition.x, -page.KeyIconPosition.y);
        if (page.KeyIconSize.x > 0.0f && page.KeyIconSize.y > 0.0f)
        {
            rect.sizeDelta = page.KeyIconSize;
        }
    }

    /// <summary>
    /// 넘김 키 게이지를 채웁니다. 0이면 바닥 링만 보이고, 1이면 한 바퀴가 다 찬 상태입니다.
    /// </summary>
    public void SetGaugeProgress(float progress01)
    {
        if (m_keyGaugeFill != null)
        {
            m_keyGaugeFill.fillAmount = Mathf.Clamp01(progress01);
        }
    }

    private static T FindChild<T>(Transform root, string name) where T : Component
    {
        Transform child = root.Find(name);
        return child != null ? child.GetComponent<T>() : null;
    }

    private void SetPanelVisible(bool visible)
    {
        if (m_panelRoot != null && m_panelRoot.activeSelf != visible)
        {
            m_panelRoot.SetActive(visible);
        }
    }

    private void SetNoticeVisible(bool visible)
    {
        if (m_noticeRoot != null && m_noticeRoot.activeSelf != visible)
        {
            m_noticeRoot.SetActive(visible);
        }
    }
}
