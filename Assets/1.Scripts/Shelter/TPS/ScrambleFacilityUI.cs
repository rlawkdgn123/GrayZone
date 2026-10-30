using System;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// Scramble 시설 버튼 입력을 시설 로직에 전달하고 업그레이드 상태와 비용을 표시합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ScrambleFacilityUI : MonoBehaviour, IFacilityStageSource
{
    [Serializable]
    private sealed class UpgradeRowView
    {
        [SerializeField] private TMP_Text m_requirementText;

        [Header("Card")]
        [SerializeField] private RectTransform m_cardRoot;
        [SerializeField] private Image m_iconImage;
        [SerializeField] private TMP_Text m_nameText;
        [SerializeField] private TMP_Text m_currentLevelText;
        [SerializeField] private TMP_Text m_arrowText;
        [SerializeField] private TMP_Text m_nextLevelText;

        public RectTransform CardRoot => m_cardRoot;
        public Sprite Icon => m_iconImage != null ? m_iconImage.sprite : null;
        public string DisplayName => m_nameText != null ? m_nameText.text : string.Empty;

        public void Cache(Transform uiRoot, string rowPath)
        {
            if (uiRoot == null || string.IsNullOrWhiteSpace(rowPath))
                return;

            Transform row = uiRoot.Find(rowPath);
            if (row == null)
                return;

            m_requirementText ??= row.Find("matter")?.GetComponent<TMP_Text>();
            m_cardRoot ??= row as RectTransform;
            m_iconImage ??= row.Find("Image")?.GetComponent<Image>();
            m_nameText ??= row.Find("Info")?.GetComponent<TMP_Text>();
            m_currentLevelText ??= row.Find("Level/Lv")?.GetComponent<TMP_Text>();
            m_arrowText ??= row.Find("Level/Arrow")?.GetComponent<TMP_Text>();
            m_nextLevelText ??= row.Find("Level/Next")?.GetComponent<TMP_Text>();
        }

        public void Refresh(string requirement, int currentLevel, int maxLevel)
        {
            SetText(m_requirementText, requirement);

            bool isMaxLevel = currentLevel >= maxLevel;
            SetText(m_currentLevelText, isMaxLevel ? "Lv. MAX" : $"Lv. {currentLevel}");
            SetText(m_nextLevelText, isMaxLevel ? string.Empty : (currentLevel + 1).ToString());
            SetActive(m_arrowText, !isMaxLevel);
            SetActive(m_nextLevelText, !isMaxLevel);
        }
    }

    [Header("Facility")]
    [SerializeField] private ScrambleFacility m_facility;
    [SerializeField] private ResourceDefinitionCatalog m_resourceCatalog;

    [Header("Menu")]
    [Tooltip("Scramble UI를 열면 먼저 보이는 업그레이드/출격 선택 화면입니다.")]
    [SerializeField] private GameObject m_menuRoot;
    [SerializeField] private Button m_upgradeMenuButton;
    [SerializeField] private Button m_sortieMenuButton;

    [Header("Upgrade View")]
    [Tooltip("Menu의 업그레이드 버튼을 누르면 나타나는 기존 업그레이드 화면입니다.")]
    [SerializeField] private GameObject m_upgradeRoot;

    [Header("Sortie Confirm")]
    [Tooltip("Menu의 출격 버튼을 누르면 나타나는 출격 확인 화면입니다.")]
    [SerializeField] private GameObject m_sortieConfirmRoot;
    [SerializeField] private Button m_sortieConfirmButton;
    [SerializeField] private Button m_sortieCancelButton;

    [Header("Buttons")]
    [SerializeField] private Button m_battleSceneButton;
    [FormerlySerializedAs("m_shooter01UpgradeButton")]
    [SerializeField] private Button m_trapUpgradeButton;
    [SerializeField] private Button m_spikeUpgradeButton;
    [SerializeField] private Button m_explosiveUpgradeButton;
    [SerializeField] private Button m_shooterUpgradeButton;
    [SerializeField] private Button m_wireUpgradeButton;

    [Header("Upgrade Rows (Optional)")]
    [SerializeField] private UpgradeRowView m_trapRow = new();
    [SerializeField] private UpgradeRowView m_spikeRow = new();
    [SerializeField] private UpgradeRowView m_explosiveRow = new();
    [SerializeField] private UpgradeRowView m_shooterRow = new();
    [SerializeField] private UpgradeRowView m_wireRow = new();

    [Header("Upgrade Detail (Optional)")]
    [Tooltip("연결하면 카드 클릭은 선택만 하고, 이 강화 버튼으로 업그레이드합니다. 비워 두면 카드 클릭 즉시 업그레이드합니다.")]
    [SerializeField] private Button m_upgradeConfirmButton;
    [SerializeField] private RectTransform m_selectFrame;
    [SerializeField] private TMP_Text m_detailNameText;
    [SerializeField] private Image m_detailIconImage;
    [Tooltip("Detail/Level/LvInfo. \"Lv\" 라벨은 씬에 고정해 두고, 현재 레벨 숫자만 여기에 표시합니다. 최대 레벨이면 꺼집니다.")]
    [SerializeField] private TMP_Text m_detailCurrentLevelValueText;
    [Tooltip("Detail/Level/Lv. 최대 레벨이면 화살표/다음 레벨과 함께 꺼집니다.")]
    [SerializeField] private TMP_Text m_detailLevelLabelText;
    [Tooltip("Detail/Level/LvMax. 최대 레벨일 때만 켜집니다.")]
    [SerializeField] private GameObject m_detailMaxLevelObject;
    [SerializeField] private TMP_Text m_detailArrowText;
    [SerializeField] private TMP_Text m_detailNextLevelText;
    [Tooltip("선택한 업그레이드의 ScrambleUpgradeLevel 시트 Info 문구를 표시합니다. 다음 레벨 효과를, 최대 레벨이면 현재 레벨 효과를 보여 줍니다.")]
    [FormerlySerializedAs("m_detailDescriptionText")]
    [SerializeField] private TMP_Text m_detailLevelInfoText;
    [SerializeField] private GameObject m_costRoot;
    [SerializeField] private TMP_Text m_costText;
    [Tooltip("업그레이드 비용 자원(ScrambleFacility의 Upgrade Resource)의 현재 보유량을 표시합니다.")]
    [SerializeField] private TMP_Text m_ownedResourceText;
    [SerializeField] private ScrambleUpgradeType m_defaultSelectedType = ScrambleUpgradeType.Trap;

    [Header("Notice (Optional)")]
    [SerializeField] private TMP_Text m_noticeText;

    [Header("Back")]
    [Tooltip("Scramble UI가 열려 있는 동안 항상 표시되며, 누를 때마다 Esc처럼 한 단계 뒤로 돌아갑니다.")]
    [SerializeField] private Button m_backButton;

    private ScrambleUpgradeType m_selectedType;
    private StorageFacility m_boundStorage;
    private FacilityUIStage m_stage = FacilityUIStage.None;

    public bool IsOpen => gameObject.activeSelf;
    public FacilityDialogueContext CurrentContext => new FacilityDialogueContext(m_stage);
    public event Action Closed;
    public event Action<FacilityDialogueContext> ContextChanged;

    private bool UsesSelection => m_upgradeConfirmButton != null;

    private Transform UpgradeContentRoot =>
        m_upgradeRoot != null ? m_upgradeRoot.transform : transform;

    private void Awake()
    {
        m_selectedType = m_defaultSelectedType;
        CacheViews();
        CacheFacility();
        CacheResourceCatalog();
        CacheUpgradeButtons();
        CacheUpgradeRows();
    }

    private void OnEnable()
    {
        CacheViews();
        CacheFacility();
        CacheResourceCatalog();
        CacheUpgradeButtons();
        CacheUpgradeRows();
        Bind();
        Refresh();
    }

    private void OnDisable()
    {
        Unbind();
        SetStage(FacilityUIStage.None);
    }

    public void Open(ScrambleFacility facility)
    {
        if (facility == null)
        {
            Debug.LogWarning("[ScrambleFacilityUI] ScrambleFacility is not available.", this);
            return;
        }

        Unbind();
        m_facility = facility;
        gameObject.SetActive(true);
        CacheViews();
        CacheResourceCatalog();
        CacheUpgradeButtons();
        CacheUpgradeRows();
        Bind();
        ClearNotice();
        m_selectedType = m_defaultSelectedType;
        ShowMenu();
        Refresh();
    }

    public void Close()
    {
        bool wasOpen = gameObject.activeSelf;
        ClearNotice();
        // 닫는 중에는 Menu 단계를 알리지 않고 화면만 초기 상태로 되돌림
        ResetToMenuView();
        gameObject.SetActive(false);
        SetStage(FacilityUIStage.None);

        if (wasOpen)
            Closed?.Invoke();
    }

    /// <summary>
    /// 업그레이드 화면이나 출격 확인 화면이 열려 있으면 Menu로 돌아가고, 아니면 Scramble UI를 닫습니다.
    /// </summary>
    public bool TryHandleEscape()
    {
        if (!IsOpen)
            return false;

        if (m_menuRoot != null && !m_menuRoot.activeSelf)
        {
            ClearNotice();
            ShowMenu();
            return true;
        }

        Close();
        return true;
    }

    public void Refresh()
    {
        if (m_facility == null)
            return;

        CacheResourceCatalog();
        CacheUpgradeRows();

        if (m_battleSceneButton != null)
            m_battleSceneButton.interactable = true;
        RefreshCardButton(m_trapUpgradeButton, ScrambleUpgradeType.Trap);
        RefreshCardButton(m_spikeUpgradeButton, ScrambleUpgradeType.Spike);
        RefreshCardButton(m_explosiveUpgradeButton, ScrambleUpgradeType.Explosive);
        RefreshCardButton(m_shooterUpgradeButton, ScrambleUpgradeType.Shooter);
        RefreshCardButton(m_wireUpgradeButton, ScrambleUpgradeType.Wire);

        SetText(m_ownedResourceText, m_facility.GetOwnedUpgradeResourceAmount().ToString());

        string resourceDisplayName = ResolveUpgradeResourceDisplayName();
        RefreshUpgradeRow(
            m_trapRow,
            ScrambleUpgradeType.Trap,
            resourceDisplayName);
        RefreshUpgradeRow(
            m_spikeRow,
            ScrambleUpgradeType.Spike,
            resourceDisplayName);
        RefreshUpgradeRow(
            m_explosiveRow,
            ScrambleUpgradeType.Explosive,
            resourceDisplayName);
        RefreshUpgradeRow(
            m_shooterRow,
            ScrambleUpgradeType.Shooter,
            resourceDisplayName);
        RefreshUpgradeRow(
            m_wireRow,
            ScrambleUpgradeType.Wire,
            resourceDisplayName);

        RefreshSelectedDetail();
    }

    /// <summary>
    /// 강화 버튼이 연결된 선택 방식에서는 카드를 항상 누를 수 있게 두고,
    /// 기존 방식에서는 업그레이드 가능 여부로 카드 버튼을 막습니다.
    /// </summary>
    private void RefreshCardButton(Button button, ScrambleUpgradeType upgradeType)
    {
        if (button != null)
            button.interactable = UsesSelection || m_facility.CanUpgrade(upgradeType);
    }

    private void RefreshSelectedDetail()
    {
        if (!UsesSelection || m_facility == null)
            return;

        UpgradeRowView row = GetRow(m_selectedType);
        int currentLevel = m_facility.GetUpgradeLevel(m_selectedType);
        int maxLevel = m_facility.GetMaxUpgradeLevel(m_selectedType);
        bool isMaxLevel = currentLevel >= maxLevel;

        if (m_selectFrame != null && row?.CardRoot != null)
            m_selectFrame.position = row.CardRoot.position;

        SetText(m_detailNameText, row?.DisplayName ?? string.Empty);
        if (m_detailIconImage != null)
        {
            m_detailIconImage.sprite = row?.Icon;
            m_detailIconImage.enabled = m_detailIconImage.sprite != null;
        }

        SetText(m_detailCurrentLevelValueText, currentLevel.ToString());
        SetText(m_detailNextLevelText, isMaxLevel ? string.Empty : (currentLevel + 1).ToString());
        SetActive(m_detailLevelLabelText, !isMaxLevel);
        SetActive(m_detailCurrentLevelValueText, !isMaxLevel);
        SetActive(m_detailMaxLevelObject, isMaxLevel);
        SetActive(m_detailArrowText, !isMaxLevel);
        SetActive(m_detailNextLevelText, !isMaxLevel);
        SetText(
            m_detailLevelInfoText,
            m_facility.GetUpgradeLevelInfo(m_selectedType, isMaxLevel ? currentLevel : currentLevel + 1));

        // 최대 레벨에서도 비용 영역(아이콘 포함)은 그대로 두고, 더 낼 비용이 없다는 뜻으로 "-"를 표시합니다.
        SetActive(m_costRoot, true);
        SetText(m_costText, isMaxLevel ? "-" : m_facility.GetUpgradeCost(m_selectedType).ToString());

        m_upgradeConfirmButton.interactable = m_facility.CanUpgrade(m_selectedType);
    }

    private void SelectUpgrade(ScrambleUpgradeType upgradeType)
    {
        ClearNotice();
        m_selectedType = upgradeType;
        RefreshSelectedDetail();
    }

    private void HandleTrapCardClicked() => SelectUpgrade(ScrambleUpgradeType.Trap);

    private void HandleSpikeCardClicked() => SelectUpgrade(ScrambleUpgradeType.Spike);

    private void HandleExplosiveCardClicked() => SelectUpgrade(ScrambleUpgradeType.Explosive);

    private void HandleShooterCardClicked() => SelectUpgrade(ScrambleUpgradeType.Shooter);

    private void HandleWireCardClicked() => SelectUpgrade(ScrambleUpgradeType.Wire);

    private void HandleUpgradeConfirmClicked()
    {
        switch (m_selectedType)
        {
            case ScrambleUpgradeType.Trap:
                HandleTrapUpgradeClicked();
                break;
            case ScrambleUpgradeType.Spike:
                HandleSpikeUpgradeClicked();
                break;
            case ScrambleUpgradeType.Explosive:
                HandleExplosiveUpgradeClicked();
                break;
            case ScrambleUpgradeType.Shooter:
                HandleShooterUpgradeClicked();
                break;
            case ScrambleUpgradeType.Wire:
                HandleWireUpgradeClicked();
                break;
        }
    }

    private UpgradeRowView GetRow(ScrambleUpgradeType upgradeType)
    {
        return upgradeType switch
        {
            ScrambleUpgradeType.Trap => m_trapRow,
            ScrambleUpgradeType.Spike => m_spikeRow,
            ScrambleUpgradeType.Explosive => m_explosiveRow,
            ScrambleUpgradeType.Shooter => m_shooterRow,
            ScrambleUpgradeType.Wire => m_wireRow,
            _ => null,
        };
    }

    private void HandleBattleSceneClicked()
    {
        TryEnterBattleScene();
    }

    /// <summary>
    /// BattleButton과 출격 확인의 "한다"가 공유하는 전투 씬 진입 판정입니다.
    /// </summary>
    private bool TryEnterBattleScene()
    {
        ClearNotice();
        if (m_facility != null && m_facility.TryLoadBattleScene())
            return true;

        SetNotice("전투 씬으로 이동하지 못했습니다.");
        return false;
    }

    private void HandleUpgradeMenuClicked()
    {
        ClearNotice();
        SetViewState(menu: false, upgrade: true, sortieConfirm: false);
        SetStage(FacilityUIStage.Upgrade);
        Refresh();
    }

    private void HandleSortieMenuClicked()
    {
        ClearNotice();
        SetViewState(menu: false, upgrade: false, sortieConfirm: true);
        SetStage(FacilityUIStage.Main);
    }

    private void HandleSortieConfirmClicked()
    {
        if (!TryEnterBattleScene())
            ShowMenu();
    }

    /// <summary>
    /// 돌아가기 버튼은 Esc 키와 같은 경로(UIManager)로 처리해 최상위 UI부터 한 단계씩 닫습니다.
    /// </summary>
    private void HandleBackClicked()
    {
        if (!IsOpen)
            return;

        UIManager uiManager = FindFirstObjectByType<UIManager>();
        if (uiManager != null && uiManager.TryHandleEscape())
            return;

        TryHandleEscape();
    }

    private void HandleSortieCancelClicked()
    {
        ClearNotice();
        ShowMenu();
    }

    private void ShowMenu()
    {
        ResetToMenuView();
        SetStage(m_menuRoot != null ? FacilityUIStage.Menu : FacilityUIStage.Upgrade);
    }

    private void ResetToMenuView()
    {
        // Menu가 없는 기존 구성에서는 업그레이드 화면을 그대로 보여줍니다.
        if (m_menuRoot == null)
        {
            SetViewState(menu: false, upgrade: true, sortieConfirm: false);
            return;
        }

        SetViewState(menu: true, upgrade: false, sortieConfirm: false);
    }

    private void SetStage(FacilityUIStage stage)
    {
        if (m_stage == stage)
            return;

        m_stage = stage;
        ContextChanged?.Invoke(CurrentContext);
    }

    private void SetViewState(bool menu, bool upgrade, bool sortieConfirm)
    {
        SetActive(m_menuRoot, menu);
        SetActive(m_upgradeRoot, upgrade);
        SetActive(m_sortieConfirmRoot, sortieConfirm);
    }

    private void HandleTrapUpgradeClicked()
    {
        ClearNotice();
        if (m_facility == null || !m_facility.TryUpgradeTrap())
            SetNotice("Trap을 업그레이드하지 못했습니다.");
    }

    private void HandleSpikeUpgradeClicked()
    {
        ClearNotice();
        if (m_facility == null || !m_facility.TryUpgradeSpike())
            SetNotice("Spike를 업그레이드하지 못했습니다.");
    }

    private void HandleExplosiveUpgradeClicked()
    {
        ClearNotice();
        if (m_facility == null || !m_facility.TryUpgradeExplosive())
            SetNotice("Explosive를 업그레이드하지 못했습니다.");
    }

    private void HandleShooterUpgradeClicked()
    {
        ClearNotice();
        if (m_facility == null || !m_facility.TryUpgradeShooter())
            SetNotice("Shooter를 업그레이드하지 못했습니다.");
    }

    private void HandleWireUpgradeClicked()
    {
        ClearNotice();
        if (m_facility == null || !m_facility.TryUpgradeWire())
            SetNotice("Wire를 업그레이드하지 못했습니다.");
    }

    private void HandleFacilityStateChanged()
    {
        ClearNotice();
        Refresh();
    }

    /// <summary>UI가 열린 동안 다른 경로로 자원이 바뀌어도 보유량과 업그레이드 가능 여부를 맞춥니다.</summary>
    private void HandleResourcesChanged()
    {
        Refresh();
    }

    private void Bind()
    {
        Unbind();

        if (m_upgradeMenuButton != null)
            m_upgradeMenuButton.onClick.AddListener(HandleUpgradeMenuClicked);
        if (m_sortieMenuButton != null)
            m_sortieMenuButton.onClick.AddListener(HandleSortieMenuClicked);
        if (m_sortieConfirmButton != null)
            m_sortieConfirmButton.onClick.AddListener(HandleSortieConfirmClicked);
        if (m_sortieCancelButton != null)
            m_sortieCancelButton.onClick.AddListener(HandleSortieCancelClicked);
        if (m_battleSceneButton != null)
            m_battleSceneButton.onClick.AddListener(HandleBattleSceneClicked);
        if (m_backButton != null)
            m_backButton.onClick.AddListener(HandleBackClicked);
        if (UsesSelection)
        {
            m_upgradeConfirmButton.onClick.AddListener(HandleUpgradeConfirmClicked);
            AddListener(m_trapUpgradeButton, HandleTrapCardClicked);
            AddListener(m_spikeUpgradeButton, HandleSpikeCardClicked);
            AddListener(m_explosiveUpgradeButton, HandleExplosiveCardClicked);
            AddListener(m_shooterUpgradeButton, HandleShooterCardClicked);
            AddListener(m_wireUpgradeButton, HandleWireCardClicked);
        }
        else
        {
            AddListener(m_trapUpgradeButton, HandleTrapUpgradeClicked);
            AddListener(m_spikeUpgradeButton, HandleSpikeUpgradeClicked);
            AddListener(m_explosiveUpgradeButton, HandleExplosiveUpgradeClicked);
            AddListener(m_shooterUpgradeButton, HandleShooterUpgradeClicked);
            AddListener(m_wireUpgradeButton, HandleWireUpgradeClicked);
        }
        if (m_facility != null)
            m_facility.StateChanged += HandleFacilityStateChanged;

        m_boundStorage = ShelterSceneDataManager.Instance?.Storage;
        if (m_boundStorage != null)
            m_boundStorage.ResourcesChanged += HandleResourcesChanged;
    }

    private void Unbind()
    {
        if (m_upgradeMenuButton != null)
            m_upgradeMenuButton.onClick.RemoveListener(HandleUpgradeMenuClicked);
        if (m_sortieMenuButton != null)
            m_sortieMenuButton.onClick.RemoveListener(HandleSortieMenuClicked);
        if (m_sortieConfirmButton != null)
            m_sortieConfirmButton.onClick.RemoveListener(HandleSortieConfirmClicked);
        if (m_sortieCancelButton != null)
            m_sortieCancelButton.onClick.RemoveListener(HandleSortieCancelClicked);
        if (m_battleSceneButton != null)
            m_battleSceneButton.onClick.RemoveListener(HandleBattleSceneClicked);
        if (m_backButton != null)
            m_backButton.onClick.RemoveListener(HandleBackClicked);
        if (m_upgradeConfirmButton != null)
            m_upgradeConfirmButton.onClick.RemoveListener(HandleUpgradeConfirmClicked);
        RemoveListener(m_trapUpgradeButton, HandleTrapCardClicked, HandleTrapUpgradeClicked);
        RemoveListener(m_spikeUpgradeButton, HandleSpikeCardClicked, HandleSpikeUpgradeClicked);
        RemoveListener(m_explosiveUpgradeButton, HandleExplosiveCardClicked, HandleExplosiveUpgradeClicked);
        RemoveListener(m_shooterUpgradeButton, HandleShooterCardClicked, HandleShooterUpgradeClicked);
        RemoveListener(m_wireUpgradeButton, HandleWireCardClicked, HandleWireUpgradeClicked);
        if (m_facility != null)
            m_facility.StateChanged -= HandleFacilityStateChanged;

        if (m_boundStorage != null)
            m_boundStorage.ResourcesChanged -= HandleResourcesChanged;
        m_boundStorage = null;
    }

    private void CacheViews()
    {
        if (m_menuRoot == null)
            m_menuRoot = transform.Find("Menu")?.gameObject;

        if (m_menuRoot != null)
        {
            m_upgradeMenuButton ??= m_menuRoot.transform.Find("UpgradeButton")?.GetComponent<Button>();
            m_sortieMenuButton ??= m_menuRoot.transform.Find("SortieButton")?.GetComponent<Button>();
        }

        if (m_upgradeRoot == null)
            m_upgradeRoot = transform.Find("Upgrade")?.gameObject;

        if (m_sortieConfirmRoot == null)
            m_sortieConfirmRoot = transform.Find("SortieConfirm")?.gameObject;

        Transform content = UpgradeContentRoot;
        m_upgradeConfirmButton ??= content.Find("UpgradeButton")?.GetComponent<Button>();
        m_selectFrame ??= content.Find("SelectFrame") as RectTransform;
        m_detailNameText ??= content.Find("Detail/Name")?.GetComponent<TMP_Text>();
        m_detailIconImage ??= content.Find("Detail/Icon")?.GetComponent<Image>();
        m_detailCurrentLevelValueText ??= content.Find("Detail/Level/LvInfo")?.GetComponent<TMP_Text>();
        m_detailLevelLabelText ??= content.Find("Detail/Level/Lv")?.GetComponent<TMP_Text>();
        if (m_detailMaxLevelObject == null)
            m_detailMaxLevelObject = content.Find("Detail/Level/LvMax")?.gameObject;
        m_detailArrowText ??= content.Find("Detail/Level/Arrow")?.GetComponent<TMP_Text>();
        m_detailNextLevelText ??= content.Find("Detail/Level/Next")?.GetComponent<TMP_Text>();
        if (m_detailLevelInfoText == null)
            m_detailLevelInfoText = content.Find("Detail/LevelInfo")?.GetComponent<TMP_Text>();
        if (m_costRoot == null)
            m_costRoot = content.Find("Resource")?.gameObject;
        m_costText ??= content.Find("Resource/Number")?.GetComponent<TMP_Text>();
        if (m_ownedResourceText == null)
            m_ownedResourceText = content.Find("CurrentResource/Number")?.GetComponent<TMP_Text>();

        if (m_sortieConfirmRoot != null)
        {
            m_sortieConfirmButton ??= m_sortieConfirmRoot.transform.Find("YesButton")?.GetComponent<Button>();
            m_sortieCancelButton ??= m_sortieConfirmRoot.transform.Find("NoButton")?.GetComponent<Button>();
        }
    }

    private void CacheFacility()
    {
        if (m_facility == null)
            m_facility = FindFirstObjectByType<ScrambleFacility>();
    }

    private void CacheResourceCatalog()
    {
        if (m_resourceCatalog != null)
            return;

        ShelterInventoryUI inventoryUI =
            FindFirstObjectByType<ShelterInventoryUI>(FindObjectsInactive.Include);
        if (inventoryUI != null)
            m_resourceCatalog = inventoryUI.ResourceCatalog;
    }

    private void CacheUpgradeButtons()
    {
        m_trapUpgradeButton ??= FindUpgradeButton("TrapUpgrade/Trap_Botton");
        m_spikeUpgradeButton ??= FindUpgradeButton("SpikeUpgrade/Spike_Botton");
        m_explosiveUpgradeButton ??= FindUpgradeButton("ExplosiveUpgrade/Explosive_Botton");
        m_shooterUpgradeButton ??= FindUpgradeButton("ShooterUpgrade/Shooter_Botton");
        m_wireUpgradeButton ??= FindUpgradeButton("WireUpgrade/Wire_Botton");
    }

    private void CacheUpgradeRows()
    {
        m_trapRow ??= new UpgradeRowView();
        m_spikeRow ??= new UpgradeRowView();
        m_explosiveRow ??= new UpgradeRowView();
        m_shooterRow ??= new UpgradeRowView();
        m_wireRow ??= new UpgradeRowView();

        Transform contentRoot = UpgradeContentRoot;
        m_trapRow.Cache(contentRoot, "TrapUpgrade");
        m_spikeRow.Cache(contentRoot, "SpikeUpgrade");
        m_explosiveRow.Cache(contentRoot, "ExplosiveUpgrade");
        m_shooterRow.Cache(contentRoot, "ShooterUpgrade");
        m_wireRow.Cache(contentRoot, "WireUpgrade");
    }

    private Button FindUpgradeButton(string relativePath)
    {
        Transform buttonTransform = UpgradeContentRoot.Find(relativePath);
        return buttonTransform != null
            ? buttonTransform.GetComponent<Button>()
            : null;
    }

    private string ResolveUpgradeResourceDisplayName()
    {
        string resourceId = m_facility.UpgradeResourceId;
        if (m_resourceCatalog != null
            && m_resourceCatalog.TryGetPresentation(
                resourceId,
                out ResourcePresentation presentation))
        {
            return string.IsNullOrWhiteSpace(presentation.DisplayName)
                ? resourceId
                : presentation.DisplayName;
        }

        return resourceId;
    }

    private void RefreshUpgradeRow(
        UpgradeRowView row,
        ScrambleUpgradeType upgradeType,
        string resourceDisplayName)
    {
        if (row == null)
            return;

        int currentLevel = m_facility.GetUpgradeLevel(upgradeType);
        int maxLevel = m_facility.GetMaxUpgradeLevel(upgradeType);
        string requirement = currentLevel >= maxLevel
            ? "업그레이드 완료"
            : $"{resourceDisplayName} x{m_facility.GetUpgradeCost(upgradeType)}";

        row.Refresh(requirement, currentLevel, maxLevel);
    }

    private void SetNotice(string message)
    {
        if (m_noticeText != null)
            m_noticeText.text = message;
        else
            Debug.LogWarning($"[ScrambleFacilityUI] {message}", this);
    }

    private void ClearNotice()
    {
        SetText(m_noticeText, string.Empty);
    }

    private static void SetText(TMP_Text target, string value)
    {
        if (target != null)
            target.text = value;
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }

    private static void SetActive(Component target, bool active)
    {
        if (target != null)
            SetActive(target.gameObject, active);
    }

    private static void AddListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null)
            button.onClick.AddListener(action);
    }

    private static void RemoveListener(
        Button button,
        UnityEngine.Events.UnityAction selectAction,
        UnityEngine.Events.UnityAction upgradeAction)
    {
        if (button == null)
            return;

        button.onClick.RemoveListener(selectAction);
        button.onClick.RemoveListener(upgradeAction);
    }
}
