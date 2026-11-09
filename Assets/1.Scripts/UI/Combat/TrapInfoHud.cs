using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 조작 대원이 설치하거나 수리할 수 있는 함정을 바라보고 있을 때 함정 이름·동작(설치/수리)과 비용·설명과 F 홀드 진행도를 표시합니다.
/// </summary>
/// <remarks>
/// 구조 HUD(<see cref="ReviveHudController"/>)와 같은 방식입니다. 레이아웃은 씬에 미리 배치돼 있고(Figma
/// <c>Trap HUD</c>의 <c>Rectangle 71</c> 영역), 이 컴포넌트는 값과 표시 여부만 갱신합니다.
///
/// 함정 외에 정문 수리(<see cref="DefenseRepairInteractable"/>)처럼 <see cref="IInteractionHudInfo"/>를 구현한 대상도 같은 패널로 보여 줍니다.
///
/// 표시 조건은 <see cref="InteractionController.Current"/>가 <see cref="IInteractionHudInfo"/>인지 하나뿐입니다. 상호작용
/// 대상 판정(거리, 설치 구간, 자원)을 여기서 다시 하면 HUD와 실제 F 입력 결과가 갈릴 수 있어서입니다.
/// 그래서 자원이 모자라 설치할 수 없는 함정은 대상이 되지 않고, 안내도 뜨지 않습니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class TrapInfoHud : MonoBehaviour
{
    [Header("References")]
    [Tooltip("안내 패널 루트입니다. 함정을 바라볼 때만 켜집니다.")]
    [SerializeField] private GameObject m_panelRoot;

    [Tooltip("함정 이름 텍스트입니다.")]
    [SerializeField] private TMP_Text m_nameText;

    [Tooltip("설치 비용 텍스트입니다. 비용이 없는 함정이면 '소모 없음' 문구를 표시합니다.")]
    [SerializeField] private TMP_Text m_costText;

    [Tooltip("설치 비용 아이콘입니다. 비용이 없는 함정이면 숨깁니다. 선택 사항입니다.")]
    [SerializeField] private GameObject m_costIcon;

    [Tooltip("비용 줄 맨 앞(아이콘 왼쪽)에 \"설치\"/\"수리\"를 표시할 텍스트입니다. 비워 두면 실행할 때 비용 텍스트를 복제해 비용 줄 맨 앞에 만듭니다.")]
    [SerializeField] private TMP_Text m_actionText;

    [Tooltip("실행할 때 만든 동작 텍스트(설치/수리)의 색입니다. 직접 지정한 동작 텍스트에는 적용하지 않습니다.")]
    [SerializeField] private Color m_actionTextColor = Color.white;

    [Tooltip("함정 설명 텍스트입니다.")]
    [SerializeField] private TMP_Text m_descriptionText;

    [Tooltip("F 키 둘레의 원형 홀드 게이지(Filled)입니다. 선택 사항입니다.")]
    [SerializeField] private Image m_holdGaugeFill;

    [Tooltip("설명이 배치된 높이보다 길어질 때(여러 줄) 늘어난 만큼 함께 아래로 늘릴 RectTransform입니다(예: 왼쪽 강조 막대). " +
             "패널 루트와 설명 텍스트는 자동으로 늘어납니다. 위쪽 기준(pivot y=1)으로 배치된 것만 넣습니다. 선택 사항입니다.")]
    [SerializeField] private RectTransform[] m_growWithDescription;

    [Header("Squad")]
    [Tooltip("조작 대원을 제공하는 SquadManager입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private SquadManager m_squadManager;

    [Header("Text")]
    [Tooltip("설치 비용 표시 형식입니다. {0}에 수량이 들어갑니다.")]
    [SerializeField] private string m_costFormat = "{0} 소모";

    [Tooltip("설치 비용이 0인 함정에 표시할 문구입니다.")]
    [SerializeField] private string m_noCostText = "소모 없음";

    [Tooltip("자원이 모자랄 때 비용 옆에 붙일 문구입니다.")]
    [SerializeField] private string m_insufficientText = "자원이 부족합니다!";

    [Tooltip("자원 부족 문구의 색입니다.")]
    [SerializeField] private Color m_insufficientColor = new Color32(255, 59, 59, 255);

    [Header("Focus Outline")]
    [Tooltip("안내가 뜬 함정에 외곽선을 그립니다.")]
    [SerializeField] private bool m_showFocusOutline = true;

    [Tooltip("외곽선 색입니다.")]
    [SerializeField] private Color m_focusOutlineColor = new Color(1.0f, 0.85f, 0.2f, 1.0f);

    [Tooltip("외곽선 굵기(화면 픽셀)입니다. 거리와 관계없이 같은 굵기로 보입니다.")]
    [Range(0.0f, 10.0f)]
    [SerializeField] private float m_focusOutlineWidth = 3.0f;

    [Tooltip("외곽선 머티리얼(GrayZone/FocusOutline)입니다. 비워 두면 Resources/Outline/M_FocusOutline을 씁니다.")]
    [SerializeField] private Material m_focusOutlineMaterial;

    [Tooltip("외곽선 마스크 머티리얼(GrayZone/FocusOutlineMask)입니다. 비워 두면 Resources/Outline/M_FocusOutlineMask를 씁니다.")]
    [SerializeField] private Material m_focusOutlineMaskMaterial;

    private const string DefaultOutlineMaterialPath = "Outline/M_FocusOutline";
    private const string DefaultOutlineMaskMaterialPath = "Outline/M_FocusOutlineMask";
    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");

    private InteractionController m_interaction;
    private GameObject m_interactionOwner;
    private IInteractionHudInfo m_shownInfo;
    private FocusOutlineRenderer m_focusOutline;

    // 대상이 비용 아이콘을 따로 정하면 바꿔 끼우고, 아니면 씬에 배치된 원래 이미지로 되돌립니다.
    private Image m_costIconImage;
    private Sprite m_defaultCostIconSprite;

    // 설명이 여러 줄이면 씬에 배치된 높이에서 늘어난 만큼만 패널을 아래로 키웁니다. 한 줄 설명(함정)은 배치 그대로입니다.
    private RectTransform m_panelRect;
    private RectTransform m_descriptionRect;
    private float m_panelBaseHeight;
    private float m_descriptionBaseHeight;
    private float[] m_growBaseHeights;

    // 색·굵기를 바꿔도 프로젝트의 머티리얼 에셋이 바뀌지 않도록 복제본에 씁니다.
    private Material m_focusOutlineInstance;

    private void Awake()
    {
        if (m_squadManager == null)
        {
            m_squadManager = FindFirstObjectByType<SquadManager>();
        }

        if (m_costIcon != null)
        {
            m_costIconImage = m_costIcon.GetComponent<Image>();
            m_defaultCostIconSprite = m_costIconImage != null ? m_costIconImage.sprite : null;
        }

        EnsureActionText();
        CacheBaseHeights();
        CreateFocusOutline();
        SetVisible(false);
    }

    /// <summary>동작 텍스트(설치/수리)가 비어 있으면 비용 텍스트를 복제해 비용 줄 맨 앞에 만듭니다.</summary>
    /// <remarks>
    /// 비용 줄은 가로 레이아웃이라 맨 앞 자식으로 넣으면 아이콘과 비용이 알아서 오른쪽으로 밀립니다.
    /// 씬마다 텍스트를 배치하지 않아도 같은 모양이 나오게 하려는 것입니다.
    /// </remarks>
    private void EnsureActionText()
    {
        if (m_actionText != null || m_costText == null)
        {
            return;
        }

        Transform row = m_costText.transform.parent;
        m_actionText = Instantiate(m_costText, row);
        m_actionText.name = "Action";
        m_actionText.color = m_actionTextColor;
        m_actionText.text = string.Empty;
        m_actionText.transform.SetSiblingIndex(m_costIcon != null && m_costIcon.transform.parent == row
            ? m_costIcon.transform.GetSiblingIndex()
            : m_costText.transform.GetSiblingIndex());
    }

    /// <summary>동작 텍스트를 바꾸고 폭을 글자에 맞춥니다. 비어 있으면 숨깁니다.</summary>
    private void SetActionLabel(string label)
    {
        if (m_actionText == null)
        {
            return;
        }

        bool show = !string.IsNullOrEmpty(label);
        if (m_actionText.gameObject.activeSelf != show)
        {
            m_actionText.gameObject.SetActive(show);
        }

        if (!show || m_actionText.text == label)
        {
            return;
        }

        m_actionText.text = label;

        // 가로 레이아웃이 자식 폭을 직접 정하지 않는 설정이어도 글자 폭만큼만 차지하게 맞춥니다.
        RectTransform rect = m_actionText.rectTransform;
        float width = Mathf.Ceil(m_actionText.GetPreferredValues(label).x);
        rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
    }

    private void CacheBaseHeights()
    {
        m_panelRect = m_panelRoot != null ? m_panelRoot.transform as RectTransform : null;
        m_descriptionRect = m_descriptionText != null ? m_descriptionText.rectTransform : null;
        m_panelBaseHeight = m_panelRect != null ? m_panelRect.sizeDelta.y : 0.0f;
        m_descriptionBaseHeight = m_descriptionRect != null ? m_descriptionRect.sizeDelta.y : 0.0f;

        int count = m_growWithDescription != null ? m_growWithDescription.Length : 0;
        m_growBaseHeights = new float[count];
        for (int i = 0; i < count; i++)
        {
            m_growBaseHeights[i] = m_growWithDescription[i] != null ? m_growWithDescription[i].sizeDelta.y : 0.0f;
        }
    }

    /// <summary>설명 텍스트가 배치된 높이를 넘는 만큼 패널과 지정한 요소를 아래로 늘립니다. 넘지 않으면 배치 높이로 되돌립니다.</summary>
    private void FitPanelToDescription(string text)
    {
        if (m_descriptionText == null || m_descriptionRect == null)
        {
            return;
        }

        float preferred = m_descriptionText.GetPreferredValues(text, m_descriptionRect.rect.width, 0.0f).y;
        float extra = Mathf.Max(0.0f, Mathf.Ceil(preferred - m_descriptionBaseHeight));

        SetHeight(m_descriptionRect, m_descriptionBaseHeight + extra);
        SetHeight(m_panelRect, m_panelBaseHeight + extra);
        for (int i = 0; i < m_growBaseHeights.Length; i++)
        {
            SetHeight(m_growWithDescription[i], m_growBaseHeights[i] + extra);
        }
    }

    private static void SetHeight(RectTransform rect, float height)
    {
        if (rect == null || Mathf.Approximately(rect.sizeDelta.y, height))
        {
            return;
        }

        rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
    }

    private void OnDestroy()
    {
        if (m_focusOutlineInstance != null)
        {
            Destroy(m_focusOutlineInstance);
        }
    }

    private void CreateFocusOutline()
    {
        Material outline = m_focusOutlineMaterial != null
            ? m_focusOutlineMaterial
            : Resources.Load<Material>(DefaultOutlineMaterialPath);
        Material mask = m_focusOutlineMaskMaterial != null
            ? m_focusOutlineMaskMaterial
            : Resources.Load<Material>(DefaultOutlineMaskMaterialPath);

        if (outline == null || mask == null)
        {
            Debug.LogWarning("[TrapInfoHud] 외곽선 머티리얼을 찾지 못해 함정 외곽선을 그리지 않습니다.", this);
            return;
        }

        m_focusOutlineInstance = new Material(outline);
        m_focusOutline = new FocusOutlineRenderer(mask, m_focusOutlineInstance);
    }

    /// <summary>안내가 뜬 대상의 외곽선을 이번 프레임에 그립니다.</summary>
    private void DrawFocusOutline(IInteractionHudInfo info)
    {
        GameObject target = info?.HudOutlineTarget;
        if (!m_showFocusOutline || m_focusOutline == null || target == null)
        {
            return;
        }

        m_focusOutlineInstance.SetColor(OutlineColorId, m_focusOutlineColor);
        m_focusOutlineInstance.SetFloat(OutlineWidthId, m_focusOutlineWidth);
        m_focusOutline.Draw(target);
    }

    private void LateUpdate()
    {
        InteractionController interaction = ResolveInteraction();
        IInteractionHudInfo info = interaction != null ? interaction.Current as IInteractionHudInfo : null;
        if (info == null)
        {
            m_shownInfo = null;
            SetVisible(false);
            return;
        }

        if (!ReferenceEquals(info, m_shownInfo))
        {
            m_shownInfo = info;
            ApplyInfo(info);
        }

        // 이름(남은 내구도), 자원, 설명(남은 체력 등)은 보고 있는 동안에도 바뀔 수 있어 매 프레임 맞춥니다.
        RefreshName(info);
        RefreshDescription(info);
        RefreshCost(info);

        SetVisible(true);
        DrawFocusOutline(info);

        if (m_holdGaugeFill != null)
        {
            m_holdGaugeFill.fillAmount = interaction.HoldProgress01;
        }
    }

    /// <summary>조작 대원의 InteractionController를 찾습니다. 대원이 바뀔 때만 다시 찾습니다.</summary>
    private InteractionController ResolveInteraction()
    {
        if (m_squadManager == null)
        {
            m_squadManager = FindFirstObjectByType<SquadManager>();
            if (m_squadManager == null)
            {
                return null;
            }
        }

        SquadMemberController member = m_squadManager.PlayerSquadMember;
        GameObject owner = member != null ? member.gameObject : null;
        if (owner != m_interactionOwner)
        {
            m_interactionOwner = owner;
            m_interaction = owner != null ? owner.GetComponent<InteractionController>() : null;
        }

        return m_interaction;
    }

    private void ApplyInfo(IInteractionHudInfo info)
    {
        if (m_costIconImage != null)
        {
            Sprite icon = info.HudCostIcon != null ? info.HudCostIcon : m_defaultCostIconSprite;
            if (m_costIconImage.sprite != icon)
            {
                m_costIconImage.sprite = icon;
            }
        }
    }

    private void RefreshName(IInteractionHudInfo info)
    {
        if (m_nameText == null)
        {
            return;
        }

        string text = info.HudDisplayName;
        if (m_nameText.text != text)
        {
            m_nameText.text = text;
        }
    }

    private void RefreshDescription(IInteractionHudInfo info)
    {
        if (m_descriptionText == null)
        {
            return;
        }

        string text = info.HudDescription;
        if (m_descriptionText.text != text)
        {
            m_descriptionText.text = text;
            FitPanelToDescription(text);
        }
    }

    /// <summary>비용 줄을 갱신합니다. 자원이 모자라면 비용 옆에 빨간 경고 문구를 붙입니다.</summary>
    private void RefreshCost(IInteractionHudInfo info)
    {
        SetActionLabel(info.HudActionLabel);

        ResourceCost cost = info.HudCost;
        bool hasCost = cost.IsValid;
        if (m_costText != null)
        {
            // 비용 줄은 항상 남깁니다. 0일 때 줄째 숨기면 공짜인지 표시 누락인지 구분할 수 없어서입니다.
            string text = hasCost ? string.Format(m_costFormat, cost.Amount) : m_noCostText;
            string blocked = info.HudBlockedMessage;
            if (!string.IsNullOrEmpty(blocked))
            {
                // 자원과 무관하게 막힌 경우(예: 체력이 가득 참)는 그 이유를 먼저 보여 줍니다.
                text += $"  <color=#{ColorUtility.ToHtmlStringRGBA(m_insufficientColor)}>{blocked}</color>";
            }
            else if (hasCost && info.IsHudCostWaived)
            {
                // 디버그 무료 중임을 알립니다. 표시가 없으면 자원이 왜 안 줄었는지 헷갈립니다.
                text += "  (디버그 무료)";
            }
            else if (hasCost && !info.CanAffordHudCost)
            {
                // 비용 칸 하나에 함께 적습니다. 따로 텍스트를 두면 씬마다 배선이 필요합니다.
                text += $"  <color=#{ColorUtility.ToHtmlStringRGBA(m_insufficientColor)}>{m_insufficientText}</color>";
            }

            if (m_costText.text != text)
            {
                m_costText.text = text;
            }
        }

        if (m_costIcon != null && m_costIcon.activeSelf != hasCost)
        {
            m_costIcon.SetActive(hasCost);
        }
    }

    private void SetVisible(bool visible)
    {
        if (m_panelRoot != null && m_panelRoot.activeSelf != visible)
        {
            m_panelRoot.SetActive(visible);
        }
    }
}
