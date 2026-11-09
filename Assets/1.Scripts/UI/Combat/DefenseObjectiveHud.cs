using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 방어 거점(정문)의 체력과 경고를 화면 HUD에 표시합니다. Defense가 시작될 때 나타납니다.
/// </summary>
/// <remarks>
/// 다른 전투 HUD와 같은 방식입니다: 이 컴포넌트는 UI를 만들지 않고, 씬에 미리 배치된 자식을 이름으로
/// 찾아 값(게이지, 문구, 표시 여부)만 채웁니다.
///
/// 거점 위 월드 HP 바와는 표시하는 곳만 다르고 같은 <see cref="DefenseEventHealth"/>를 봅니다.
/// 월드 바는 <see cref="HealthSystemBase"/>가 직접 갱신하고, 화면 HUD는 여기서 갱신합니다.
/// 경고 문구는 <see cref="DefenseEventHealth"/>가 "언제 낼지"만 정하고 표시는 전부 이 컴포넌트가 합니다.
/// </remarks>
[DisallowMultipleComponent]
public class DefenseObjectiveHud : MonoBehaviour
{
    private const string PanelName = "Panel";
    private const string GaugeName = "Gauge";
    private const string LabelName = "Label";
    private const string WarningName = "Warning";
    private const string WarningIconLeftName = "AlertIconLeft";
    private const string WarningIconRightName = "AlertIconRight";

    [Header("References (비워두면 자식 이름으로 자동 탐색)")]
    [Tooltip("HUD 패널 루트입니다. 이 오브젝트를 켜고 끄는 것으로 표시를 전환합니다. 비어 있으면 자식 'Panel'을 찾습니다.")]
    [SerializeField] private GameObject m_panelRoot;

    [Tooltip("거점 체력 게이지입니다. Image는 fillAmount로, Slider는 value로 채웁니다. 비어 있으면 자식 'Gauge'를 찾습니다.")]
    [SerializeField] private Image m_gaugeFill;

    [Tooltip("거점 이름 텍스트입니다. 비어 있으면 자식 'Label'을 찾습니다.")]
    [SerializeField] private TMP_Text m_labelText;

    [Tooltip("경고 문구 텍스트입니다. 비어 있으면 자식 'Warning'을 찾습니다.")]
    [SerializeField] private TMP_Text m_warningText;

    [Tooltip("거점 체력 비율(%)을 표시할 텍스트입니다. 선택 사항입니다.")]
    [SerializeField] private TMP_Text m_gateHpText;

    [Tooltip("전투 구간에 필드에 남은 적 수를 표시할 텍스트입니다. 전투 구간이 아니면 숨깁니다. 선택 사항입니다.")]
    [SerializeField] private TMP_Text m_enemyCountText;

    [Tooltip("남은 적 수 표시 형식입니다. {0}에 남은 적 수가 들어갑니다.")]
    [SerializeField] private string m_enemyCountFormat = "남은 적 {0}";

    [Tooltip("남은 적 수를 다시 셀 간격(초)입니다. 적 수를 세는 비용 때문에 매 프레임 세지 않습니다.")]
    [Min(0.05f)]
    [SerializeField] private float m_enemyCountInterval = 0.25f;

    [Header("Source")]
    [Tooltip("표시할 거점 체력입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private DefenseEventHealth m_objective;

    [Tooltip("표시 시점을 맞출 라운드 매니저입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private DefenseManager m_defenseManager;

    [Tooltip("피격 경고 사운드와 같은 타이밍에 화면 경고를 표시할 컨트롤러입니다. 비어 있으면 방어 목표에서 찾습니다.")]
    [SerializeField] private DefenseObjectiveAudioController m_audioController;

    [Header("Text")]
    [Tooltip("거점 이름입니다.")]
    [SerializeField] private string m_label = "정문";

    [Tooltip("경고 문구를 띄운 뒤 자동으로 지울 시간(초)입니다. 0 이하면 지우지 않습니다.")]
    [SerializeField] private float m_warningDuration = 3.0f;

    [Tooltip("일반 피격 경고음과 함께 표시할 문구입니다.")]
    [SerializeField] private string m_underAttackMessage = "외곽 방어선이 공격받고 있습니다.";

    [Tooltip("위험 체력 진입 경고음과 함께 표시할 문구입니다.")]
    [SerializeField] private string m_criticalAttackMessage = "외곽 방어선이 위험합니다!";

    [Header("Warning Style")]
    [Tooltip("경고 문구 왼쪽 경고 아이콘입니다. 비어 있으면 경고 문구의 자식 'AlertIconLeft'를 찾습니다. 문구 폭에 맞춰 가로 위치만 옮깁니다.")]
    [SerializeField] private RectTransform m_warningIconLeft;

    [Tooltip("경고 문구 오른쪽 경고 아이콘입니다. 비어 있으면 경고 문구의 자식 'AlertIconRight'를 찾습니다.")]
    [SerializeField] private RectTransform m_warningIconRight;

    [Tooltip("경고 문구 글자 끝과 아이콘 사이 간격입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_warningIconGap = 5.0f;

    [Tooltip("경고 문구 기본 색입니다.")]
    [SerializeField] private Color m_warningColor = Color.red;

    [Tooltip("켜면 경고 문구 색이 기본 색과 점멸 색 사이를 부드럽게 오갑니다. 끄면 기본 색으로 고정합니다.")]
    [SerializeField] private bool m_warningBlink = true;

    [Tooltip("점멸할 때 오갈 색입니다.")]
    [SerializeField] private Color m_warningBlinkColor = new Color(1.0f, 126.0f / 255.0f, 126.0f / 255.0f, 1.0f);

    [Tooltip("점멸 한 번(기본 색 → 점멸 색 → 기본 색)에 걸리는 시간(초)입니다.")]
    [Min(0.05f)]
    [SerializeField] private float m_warningBlinkPeriod = 1.0f;

    [Header("Behaviour")]
    [Tooltip("Defense가 시작될 때 자동으로 표시할지 여부입니다. 끄면 Show()를 직접 불러야 합니다.")]
    [SerializeField] private bool m_showOnDefenseStart = true;

    /// <summary>경고 문구를 지울 시각입니다. 0 이하면 예약 없음입니다.</summary>
    private float m_warningClearTime;

    /// <summary>경고 문구를 띄운 시각입니다. 점멸을 기본 색에서 시작하려고 둡니다.</summary>
    private float m_warningShownTime;

    /// <summary>거점이 파괴된 뒤인지 여부입니다. 파괴 문구는 자동으로 지우지 않습니다.</summary>
    private bool m_objectiveDestroyed;

    /// <summary>다음에 남은 적 수를 셀 시각(unscaled)입니다.</summary>
    private float m_nextEnemyCountTime;

    /// <summary>마지막으로 표시한 남은 적 수입니다. 바뀔 때만 문자열을 새로 만듭니다.</summary>
    private int m_lastEnemyCount = -1;

    /// <summary>HUD가 현재 표시 중인지 여부입니다.</summary>
    public bool IsShown => m_panelRoot != null && m_panelRoot.activeSelf;

    private void Reset()
    {
        AutoFindReferences();
    }

    private void Awake()
    {
        AutoFindReferences();
        ApplyLabel();
        ClearWarning();
        SetVisible(false);
    }

    private void OnEnable()
    {
        AutoFindReferences();
        Subscribe();
        RefreshGauge();

        // UI 루트가 꺼진 채 씬이 시작되면 OnEnable이 늦게 돌아 시작 이벤트를 놓칩니다.
        // 이벤트만 믿지 않고, 이미 시작된 상태면 여기서 바로 켭니다.
        if (m_showOnDefenseStart && m_defenseManager != null && m_defenseManager.IsGameStarted)
        {
            Show();
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Update()
    {
        RefreshEnemyCount();
        UpdateWarningColor();

        if (m_objectiveDestroyed || m_warningClearTime <= 0.0f || Time.time < m_warningClearTime)
        {
            return;
        }

        m_warningClearTime = 0.0f;
        ClearWarning();
    }

    /// <summary>HUD를 표시합니다.</summary>
    public void Show()
    {
        AutoFindReferences();
        ApplyLabel();
        RefreshGauge();
        SetVisible(true);
    }

    /// <summary>HUD를 감춥니다.</summary>
    public void Hide()
    {
        SetVisible(false);
    }

    private void Subscribe()
    {
        if (m_defenseManager != null)
        {
            m_defenseManager.OnDefenseStarted -= HandleDefenseStarted;
            m_defenseManager.OnDefenseStarted += HandleDefenseStarted;
        }

        if (m_objective == null)
        {
            SubscribeAudioWarning();
            return;
        }

        m_objective.OnHPChanged -= HandleHpChanged;
        m_objective.OnHPChanged += HandleHpChanged;
        m_objective.OnWarningRaised -= HandleWarningRaised;
        m_objective.OnWarningRaised += HandleWarningRaised;
        m_objective.OnDestroyed -= HandleObjectiveDestroyed;
        m_objective.OnDestroyed += HandleObjectiveDestroyed;
        SubscribeAudioWarning();
    }

    private void Unsubscribe()
    {
        if (m_defenseManager != null)
        {
            m_defenseManager.OnDefenseStarted -= HandleDefenseStarted;
        }

        if (m_objective == null)
        {
            UnsubscribeAudioWarning();
            return;
        }

        m_objective.OnHPChanged -= HandleHpChanged;
        m_objective.OnWarningRaised -= HandleWarningRaised;
        m_objective.OnDestroyed -= HandleObjectiveDestroyed;
        UnsubscribeAudioWarning();
    }

    private void HandleDefenseStarted()
    {
        if (!m_showOnDefenseStart)
        {
            return;
        }

        m_objectiveDestroyed = false;
        ClearWarning();
        Show();
    }

    private void HandleHpChanged(int currentHp, int maxHp)
    {
        ApplyGauge(maxHp > 0 ? Mathf.Clamp01((float)currentHp / maxHp) : 0.0f);
    }

    private void HandleWarningRaised(string message)
    {
        ShowWarning(message);
    }

    /// <summary>피격 경고 사운드가 발동하는 타이밍에 같은 단계의 화면 문구를 표시합니다.</summary>
    private void HandleAudioWarningIssued(DefenseObjectiveAudioController.WarningKind kind)
    {
        string message = kind == DefenseObjectiveAudioController.WarningKind.Critical
            ? m_criticalAttackMessage
            : m_underAttackMessage;
        ShowWarning(message);
    }

    private void ShowWarning(string message)
    {
        if (m_warningText == null || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        m_warningText.text = message;
        m_warningText.gameObject.SetActive(true);
        m_warningShownTime = Time.time;
        m_warningClearTime = m_warningDuration > 0.0f ? Time.time + m_warningDuration : 0.0f;
        LayoutWarningIcons();
        UpdateWarningColor();
    }

    /// <summary>경고 아이콘을 문구 실제 폭의 양 끝 바깥에 붙입니다. 문구마다 길이가 달라서 띄울 때마다 맞춥니다.</summary>
    /// <remarks>아이콘은 문구 RectTransform 가운데를 앵커로 둔다고 가정합니다. 세로 위치는 씬에 둔 값을 그대로 씁니다.</remarks>
    private void LayoutWarningIcons()
    {
        m_warningText.ForceMeshUpdate();
        float halfWidth = m_warningText.preferredWidth * 0.5f;
        if (m_warningIconLeft != null)
        {
            m_warningIconLeft.anchoredPosition = new Vector2(
                -halfWidth - m_warningIconGap - (m_warningIconLeft.rect.width * (1.0f - m_warningIconLeft.pivot.x)),
                m_warningIconLeft.anchoredPosition.y);
        }

        if (m_warningIconRight != null)
        {
            m_warningIconRight.anchoredPosition = new Vector2(
                halfWidth + m_warningIconGap + (m_warningIconRight.rect.width * m_warningIconRight.pivot.x),
                m_warningIconRight.anchoredPosition.y);
        }
    }

    /// <summary>경고 문구가 떠 있으면 점멸 설정에 따라 색을 맞춥니다. 띄운 순간 기본 색에서 시작합니다.</summary>
    private void UpdateWarningColor()
    {
        if (m_warningText == null || !m_warningText.gameObject.activeSelf)
        {
            return;
        }

        if (!m_warningBlink)
        {
            m_warningText.color = m_warningColor;
            return;
        }

        float phase = (Time.time - m_warningShownTime) / m_warningBlinkPeriod;
        float blend = 0.5f - (0.5f * Mathf.Cos(phase * Mathf.PI * 2.0f));
        m_warningText.color = Color.Lerp(m_warningColor, m_warningBlinkColor, blend);
    }

    private void SubscribeAudioWarning()
    {
        if (m_audioController == null)
        {
            return;
        }

        m_audioController.OnWarningIssued -= HandleAudioWarningIssued;
        m_audioController.OnWarningIssued += HandleAudioWarningIssued;
    }

    private void UnsubscribeAudioWarning()
    {
        if (m_audioController != null)
        {
            m_audioController.OnWarningIssued -= HandleAudioWarningIssued;
        }
    }

    /// <summary>파괴되면 게이지를 비우고 마지막 문구를 남깁니다.</summary>
    /// <remarks>게임오버 화면이 뜨기 전까지 사유가 보여야 해서 자동 삭제를 걸지 않습니다.</remarks>
    private void HandleObjectiveDestroyed()
    {
        m_objectiveDestroyed = true;
        m_warningClearTime = 0.0f;
        ApplyGauge(0.0f);
    }

    private void RefreshGauge()
    {
        if (m_objective == null)
        {
            return;
        }

        int max = m_objective.MaxHP;
        ApplyGauge(max > 0 ? Mathf.Clamp01((float)m_objective.CurrentHP / max) : 0.0f);
    }

    private void ApplyGauge(float normalized)
    {
        if (m_gaugeFill != null)
        {
            m_gaugeFill.fillAmount = normalized;
        }

        if (m_gateHpText != null)
        {
            // 1% 미만으로 남았을 때 0%로 보이면 이미 무너진 것으로 오해하므로 올림합니다.
            m_gateHpText.text = $"{Mathf.CeilToInt(normalized * 100.0f)}%";
        }
    }

    /// <summary>
    /// 전투 구간에만 남은 적 수를 표시합니다.
    /// </summary>
    /// <remarks>
    /// 정리 구간에는 <see cref="DefenseManager"/>의 구간 라벨이 같은 수를 보여 주므로 여기서는 숨겨 중복을 피합니다.
    /// </remarks>
    private void RefreshEnemyCount()
    {
        if (m_enemyCountText == null)
        {
            return;
        }

        bool shouldShow = m_defenseManager != null && m_defenseManager.IsPlaying;
        if (m_enemyCountText.gameObject.activeSelf != shouldShow)
        {
            m_enemyCountText.gameObject.SetActive(shouldShow);
            m_lastEnemyCount = -1;
        }

        if (!shouldShow || Time.unscaledTime < m_nextEnemyCountTime)
        {
            return;
        }

        m_nextEnemyCountTime = Time.unscaledTime + m_enemyCountInterval;
        int count = m_defenseManager.RemainingEnemyCount;
        if (count != m_lastEnemyCount)
        {
            m_lastEnemyCount = count;
            m_enemyCountText.text = string.Format(m_enemyCountFormat, count);
        }
    }

    private void ApplyLabel()
    {
        if (m_labelText != null)
        {
            m_labelText.text = m_label;
        }
    }

    private void ClearWarning()
    {
        if (m_warningText == null)
        {
            return;
        }

        m_warningText.text = string.Empty;
        m_warningText.gameObject.SetActive(false);
    }

    private void SetVisible(bool visible)
    {
        if (m_panelRoot != null && m_panelRoot.activeSelf != visible)
        {
            m_panelRoot.SetActive(visible);
        }
    }

    private void AutoFindReferences()
    {
        if (m_objective == null)
        {
            m_objective = FindFirstObjectByType<DefenseEventHealth>(FindObjectsInactive.Include);
        }

        if (m_defenseManager == null)
        {
            m_defenseManager = FindFirstObjectByType<DefenseManager>(FindObjectsInactive.Include);
        }

        if (m_audioController == null)
        {
            m_audioController = m_objective != null
                ? m_objective.GetComponent<DefenseObjectiveAudioController>()
                : FindFirstObjectByType<DefenseObjectiveAudioController>(FindObjectsInactive.Include);
        }

        if (m_panelRoot == null)
        {
            Transform panel = transform.Find(PanelName);
            if (panel != null)
            {
                m_panelRoot = panel.gameObject;
            }
        }

        Transform searchRoot = m_panelRoot != null ? m_panelRoot.transform : transform;

        if (m_gaugeFill == null)
        {
            Transform gauge = searchRoot.Find(GaugeName);
            if (gauge != null)
            {
                m_gaugeFill = gauge.GetComponent<Image>();
            }
        }

        if (m_labelText == null)
        {
            Transform label = searchRoot.Find(LabelName);
            if (label != null)
            {
                m_labelText = label.GetComponent<TMP_Text>();
            }
        }

        if (m_warningText == null)
        {
            Transform warning = searchRoot.Find(WarningName);
            if (warning != null)
            {
                m_warningText = warning.GetComponent<TMP_Text>();
            }
        }

        if (m_warningText != null)
        {
            if (m_warningIconLeft == null)
            {
                m_warningIconLeft = m_warningText.transform.Find(WarningIconLeftName) as RectTransform;
            }

            if (m_warningIconRight == null)
            {
                m_warningIconRight = m_warningText.transform.Find(WarningIconRightName) as RectTransform;
            }
        }
    }
}
