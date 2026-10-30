using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 모든 시설이 공용으로 쓰는 업그레이드 창입니다. Canvas에 상시 존재하며 열 때 SetActive로 표시합니다.
/// </summary>
/// <remarks>
/// 특정 시설을 알지 않고 <see cref="Open"/>에 받은 facilityId로 <see cref="FacilityManager"/>에서 데이터를 pull합니다.
/// (시설 이름·효과 문구·비용·업그레이드 실행은 FacilityManager, 보유 자원 조회는 StorageFacility 경유)
/// <para>
/// 구성은 "시설 이름 Lv.현재 → 다음 / 효과" 한 줄, "자원 아이콘 수량 + 질문" 한 줄, 안 한다·한다 버튼입니다.
/// 효과 문구와 비용은 시설의 업그레이드 표(<see cref="FacilityUpgradeTableSO"/>)에서 옵니다.
/// </para>
/// </remarks>
public class FacilityUpgradeUI : MonoBehaviour
{
    [Header("Upgrade Effect")]
    [SerializeField] private TMP_Text m_nameText;
    [Tooltip("현재 레벨입니다. 최대 레벨이면 MAX로 표시합니다.")]
    [FormerlySerializedAs("m_levelText")]
    [SerializeField] private TMP_Text m_currentLevelText;
    [SerializeField] private TMP_Text m_arrowText;
    [SerializeField] private TMP_Text m_nextLevelText;
    [Tooltip("다음 레벨의 업그레이드 효과 문구입니다(시설 업그레이드 표의 Effect).")]
    [SerializeField] private TMP_Text m_effectText;

    [Header("Cost")]
    [Tooltip("자원 아이콘·수량·질문 줄의 루트입니다. 최대 레벨이면 숨깁니다.")]
    [SerializeField] private GameObject m_costLineRoot;
    [SerializeField] private TMP_Text m_costText;
    [Tooltip("자원 수량 표기입니다. {0}에 업그레이드 표의 Amount가 들어갑니다. 자원 이름은 고정 문구로 둡니다.")]
    [SerializeField] private string m_costFormat = "자원 {0}";
    [SerializeField] private TMP_Text m_questionText;
    [Tooltip("{0}에 시설 이름이 들어갑니다.")]
    [SerializeField] private string m_questionFormat = "을 사용하여 {0}을 업그레이드 하시겠어요?";
    [SerializeField] private Color m_enoughColor = Color.white;
    [SerializeField] private Color m_lackColor = Color.red;

    [Header("Buttons / Notice")]
    [Tooltip("한다 버튼입니다. 업그레이드할 수 있을 때만 누를 수 있습니다.")]
    [SerializeField] private Button m_upgradeButton;
    [Tooltip("안 한다 버튼입니다. 창을 닫습니다.")]
    [SerializeField] private Button m_closeButton;
    [Tooltip("시설 UI의 돌아가기 표시와 같은 위치에서 업그레이드 창을 닫는 투명 버튼입니다.")]
    [SerializeField] private Button m_backButton;
    [Tooltip("의료실 화면에 보이는 돌아가기 버튼의 실제 영역입니다.")]
    [SerializeField] private RectTransform m_medicalBackReference;
    [Tooltip("제조실 화면에 보이는 돌아가기 버튼의 실제 영역입니다.")]
    [SerializeField] private RectTransform m_manufacturingBackReference;
    [Tooltip("최대 레벨일 때 자원·질문 줄 대신 표시합니다.")]
    [SerializeField] private GameObject m_maxLevelNotice;

    [Header("Cutscene")]
    [Tooltip("비어 있으면 씬에서 찾습니다. 시설에 업그레이드 연출이 있으면 연출을 통해 업그레이드합니다.")]
    [SerializeField] private UIManager m_uiManager;

    private string m_facilityId;

    /// <summary>시설 업그레이드 UI가 닫힐 때 발생</summary>
    public event System.Action Closed;

    private void Awake()
    {
        if (m_upgradeButton != null)
            m_upgradeButton.onClick.AddListener(OnUpgradeClicked);
        if (m_closeButton != null)
            m_closeButton.onClick.AddListener(Close);
        if (m_backButton != null)
            m_backButton.onClick.AddListener(Close);
    }

    private void OnDestroy()
    {
        if (m_upgradeButton != null)
            m_upgradeButton.onClick.RemoveListener(OnUpgradeClicked);
        if (m_closeButton != null)
            m_closeButton.onClick.RemoveListener(Close);
        if (m_backButton != null)
            m_backButton.onClick.RemoveListener(Close);
    }

    /// <summary>지정한 시설의 업그레이드 창을 연다(여는 쪽이 자기 FacilityId 전달).</summary>
    public void Open(string facilityId)
    {
        m_facilityId = facilityId;
        gameObject.SetActive(true);
        AlignBackButtonToVisibleFacility();
        Refresh();
    }

    /// <summary>창을 닫는다(상시 존재하므로 비활성화).</summary>
    public void Close()
    {
        if (!gameObject.activeSelf)
            return;

        gameObject.SetActive(false);
        Closed?.Invoke();
    }

    private void Refresh()
    {
        FacilityManager fm = FacilityManager.Instance;
        if (fm == null || string.IsNullOrWhiteSpace(m_facilityId))
            return;

        FacilityState state = fm.GetState(m_facilityId);
        int level = fm.GetUpgradeLevel(m_facilityId);
        bool canUpgrade = fm.CanUpgrade(m_facilityId);   // 해금·최대레벨·비자원 조건
        bool canAfford = fm.CanAffordUpgrade(m_facilityId); // + 자원까지
        bool isMaxLevel = state != null && state.IsUnlocked && !canUpgrade; // 해금됐는데 더 못 올림 = 최대
        string facilityName = state?.Definition != null ? state.Definition.FacilityName : m_facilityId;

        // 이름 / 레벨 / 효과
        SetText(m_nameText, facilityName);
        SetText(m_currentLevelText, isMaxLevel ? "Lv. MAX" : $"Lv. {level + 1}");
        SetText(m_nextLevelText, isMaxLevel ? string.Empty : (level + 2).ToString());
        SetActive(m_arrowText, !isMaxLevel);
        SetActive(m_nextLevelText, !isMaxLevel);
        SetText(m_effectText, isMaxLevel ? string.Empty : fm.GetUpgradeEffectText(m_facilityId));

        // 비용 (표의 자원 한 종류 기준, 보유량이 모자라면 lack 색)
        SetActive(m_costLineRoot, !isMaxLevel);
        if (m_maxLevelNotice != null)
            m_maxLevelNotice.SetActive(isMaxLevel);
        RefreshCost(fm.GetUpgradeCost(m_facilityId), facilityName);

        // 버튼
        if (m_upgradeButton != null)
            m_upgradeButton.interactable = canAfford;
    }

    private void RefreshCost(CostBundle cost, string facilityName)
    {
        SetText(m_questionText, string.Format(m_questionFormat ?? string.Empty, facilityName));

        if (cost.IsFree)
        {
            SetText(m_costText, string.Empty);
            return;
        }

        // 자원 이름·아이콘은 씬에 고정하고, 수량만 업그레이드 표 값을 씁니다.
        ResourceCost first = cost.Costs[0];
        StorageFacility storage = ShelterSceneDataManager.Instance?.Storage;
        int owned = storage != null ? storage.GetResourceAmount(first.ResourceId) : 0;
        if (m_costText != null)
        {
            m_costText.text = string.Format(m_costFormat ?? "{0}", first.Amount);
            m_costText.color = owned >= first.Amount ? m_enoughColor : m_lackColor;
        }
    }

    private void OnUpgradeClicked()
    {
        // 연출이 연결된 시설은 연출이 화면을 가린 시점에 업그레이드한다(창은 UIManager가 닫음).
        if (m_uiManager == null)
            m_uiManager = FindFirstObjectByType<UIManager>();
        if (m_uiManager != null && m_uiManager.TryPlayFacilityUpgradeCutscene(m_facilityId))
            return;

        if (FacilityManager.Instance != null && FacilityManager.Instance.TryUpgrade(m_facilityId))
            Refresh(); // 성공 → 새 레벨 기준으로 다시 그림 (슬롯/건물은 매니저 이벤트로 자동 갱신)
    }

    /// <summary>
    /// 실행 해상도와 각 시설 UI의 부모 스케일을 반영해 투명 돌아가기 버튼을 실제 표시 영역에 맞춥니다.
    /// </summary>
    private void AlignBackButtonToVisibleFacility()
    {
        RectTransform hitRect = m_backButton != null ? m_backButton.transform as RectTransform : null;
        RectTransform parentRect = hitRect != null ? hitRect.parent as RectTransform : null;
        RectTransform referenceRect = GetVisibleBackReference();
        if (hitRect == null || parentRect == null || referenceRect == null)
            return;

        Canvas.ForceUpdateCanvases();
        Vector3[] corners = new Vector3[4];
        referenceRect.GetWorldCorners(corners);
        Vector3 localBottomLeft = parentRect.InverseTransformPoint(corners[0]);
        Vector3 localTopRight = parentRect.InverseTransformPoint(corners[2]);

        hitRect.anchorMin = Vector2.zero;
        hitRect.anchorMax = Vector2.zero;
        hitRect.pivot = Vector2.zero;
        hitRect.anchoredPosition = new Vector2(
            localBottomLeft.x - parentRect.rect.xMin,
            localBottomLeft.y - parentRect.rect.yMin);
        hitRect.sizeDelta = new Vector2(
            localTopRight.x - localBottomLeft.x,
            localTopRight.y - localBottomLeft.y);
        hitRect.localRotation = Quaternion.identity;
        hitRect.localScale = Vector3.one;
        hitRect.SetAsLastSibling();
    }

    private RectTransform GetVisibleBackReference()
    {
        if (m_medicalBackReference != null && m_medicalBackReference.gameObject.activeInHierarchy)
            return m_medicalBackReference;
        if (m_manufacturingBackReference != null && m_manufacturingBackReference.gameObject.activeInHierarchy)
            return m_manufacturingBackReference;

        return m_medicalBackReference != null
            ? m_medicalBackReference
            : m_manufacturingBackReference;
    }

    private static void SetText(TMP_Text text, string value)
    {
        if (text != null)
            text.text = value;
    }

    private static void SetActive(Component component, bool active)
    {
        if (component != null && component.gameObject.activeSelf != active)
            component.gameObject.SetActive(active);
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }
}
