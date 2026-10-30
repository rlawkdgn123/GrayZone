using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 제조 시설 UI를 열고 제조 슬롯, 레시피 선택, 헬퍼 UI를 매니저 상태로 투영합니다.
/// </summary>
public sealed class ManufacturingUI : MonoBehaviour, IFacilityStageSource
{
    [Header("Root")]
    [SerializeField] private GameObject m_root;
    [SerializeField] private bool m_hideOnAwake = true;

    [Header("Menu")]
    [SerializeField] private GameObject m_menuRoot;
    [SerializeField] private Button m_createButton;
    [SerializeField] private Button m_upgradeButton;
    [SerializeField] private FacilityUpgradeButton m_upgradeAction;

    [Header("Upgrade View")]
    [Tooltip("Menu의 업그레이드 버튼을 누르면 나타나는 Upgrade 오브젝트입니다.")]
    [SerializeField] private GameObject m_upgradeRoot;

    [Header("Create View")]
    [SerializeField] private ManufacturingCreateView m_createView;
    [SerializeField] private TMP_Text m_noticeText;

    [Header("Back")]
    [Tooltip("제조 UI가 열려 있는 동안 항상 표시되며, 누를 때마다 Esc처럼 한 단계 뒤로 돌아갑니다.")]
    [SerializeField] private Button m_backButton;

    private ManufacturingManager m_currentManager;
    private Button m_boundCreateButton;
    private Button m_boundUpgradeButton;
    private Button m_boundBackButton;
    private ManufacturingCreateView m_boundCreateView;
    private bool m_isClosing;
    private bool m_isUpgradeViewOpen;
    private bool m_isOpening;
    private bool m_isOpen;
    private FacilityUIStage m_stage = FacilityUIStage.None;

    public bool IsOpen => m_isOpen;
    public FacilityDialogueContext CurrentContext => new FacilityDialogueContext(m_stage);
    public event Action Closed;
    public event Action<FacilityDialogueContext> ContextChanged;

    private void Awake()
    {
        if (m_root == null)
            m_root = gameObject;

        CacheChildViews();
        m_isOpen = m_root.activeSelf;
        if (m_hideOnAwake && !m_isOpening)
            Close();
    }

    private void Reset()
    {
        m_root = gameObject;
        CacheChildViews();
    }

    private void OnEnable()
    {
        CacheChildViews();
        BindCreateButton();
        BindUpgradeButton();
        BindBackButton();
        BindCreateView();
    }

    private void OnDisable()
    {
        UnbindCreateButton();
        UnbindUpgradeButton();
        UnbindBackButton();
        UnbindCreateView();
        UnbindManager();
        if (!m_isOpening)
            SetOpenState(false);
    }

    private void OnDestroy()
    {
        UnbindCreateButton();
        UnbindUpgradeButton();
        UnbindBackButton();
        UnbindCreateView();
    }

    /// <summary>
    /// 현재 제조 UI의 최상위 Escape 동작을 처리합니다.
    /// CreateView나 Upgrade 화면이 열려 있으면 그것만 닫고, 아니면 제조 UI 전체를 닫습니다.
    /// </summary>
    public bool TryHandleEscape()
    {
        if (!m_isOpen)
            return false;

        if (m_createView != null && m_createView.TryHandleEscape())
            return true;

        if (m_isUpgradeViewOpen)
        {
            SetUpgradeViewOpen(false);
            SetMenuActive(true);
            SetStage(FacilityUIStage.Menu);
            return true;
        }

        Close();
        return true;
    }

    public void Open(ManufacturingManager manager)
    {
        UnbindManager();
        m_currentManager = manager;
        if (m_currentManager != null)
            m_currentManager.StateChanged += Refresh;

        m_isOpening = true;
        SetRootActive(true);
        SetOpenState(true);
        m_isOpening = false;

        CacheChildViews();
        BindCreateButton();
        BindUpgradeButton();
        BindBackButton();
        BindCreateView();
        ClearNotice();
        SetUpgradeViewOpen(false);
        if (m_menuRoot != null)
        {
            m_isClosing = true;
            CloseCreateView();
            m_isClosing = false;
            SetMenuActive(true);
            SetStage(FacilityUIStage.Menu);
        }
        else if (m_createView != null && m_createView.Open(0))
        {
            SetStage(FacilityUIStage.Main);
        }
        else
        {
            SetNotice("제작 화면을 열 수 없습니다.");
            Close();
        }
    }

    public void Close()
    {
        m_isClosing = true;
        CloseCreateView();
        m_isClosing = false;
        SetUpgradeViewOpen(false);
        SetMenuActive(true);
        UnbindCreateButton();
        UnbindUpgradeButton();
        UnbindBackButton();
        UnbindCreateView();
        UnbindManager();
        SetRootActive(false);
        SetOpenState(false);
    }

    public void Refresh()
    {
        CacheChildViews();
        if (m_createView != null && m_createView.IsOpen)
            m_createView.Refresh();
    }

    /// <summary>
    /// 공용 시설 업그레이드 UI가 열리고 닫힐 때 호출됩니다.
    /// 업그레이드 UI가 열려 있는 동안 Menu는 숨기고 Upgrade 버튼은 유지하며,
    /// 닫히면 Upgrade 버튼을 숨기고 Menu로 돌아갑니다.
    /// </summary>
    public void SetMenuVisible(bool visible)
    {
        if (!m_isOpen)
            return;

        if (!visible)
        {
            SetMenuActive(false);
            SetStage(FacilityUIStage.Upgrade);
            return;
        }

        SetUpgradeViewOpen(false);
        SetMenuActive(true);
        SetStage(FacilityUIStage.Menu);
    }

    private void HandleCreateRequested(ManufacturingCreateRequest request)
    {
        if (m_currentManager == null
            || m_createView == null
            || !m_createView.IsOpen)
        {
            return;
        }

        if (request.SlotIndex != m_createView.PendingSlotIndex
            || request.RequestedBatchCount != m_createView.RequestedBatchCount
            || !string.Equals(
                request.RecipeId,
                m_createView.SelectedRecipeId,
                StringComparison.Ordinal))
        {
            Debug.LogWarning(
                "[ManufacturingUI] Ignored a stale CreateView confirmation request.",
                this);
            return;
        }

        /* 날짜 기반 제작 작업을 다시 사용할 때 복구할 기존 시작 호출.
        if (m_currentManager.TryStartJob(
                request.SlotIndex,
                request.RecipeId,
                request.RequestedBatchCount,
                out ManufacturingStartJobFailureReason failureReason))
        {
            ClearNotice();
            m_createView.CloseAfterConfirmed();
            return;
        }
        */

        if (m_currentManager.TryCraftImmediately(
                request.SlotIndex,
                request.RecipeId,
                request.RequestedBatchCount,
                out ManufacturingStartJobFailureReason failureReason))
        {
            ClearNotice();
            m_createView.CloseAfterConfirmed();
            Refresh();
            return;
        }

        m_createView.Refresh();
        if (TryGetExpectedStartFailureMessage(failureReason, out string message))
        {
            SetNotice(message);
            Refresh();
            return;
        }

        SetNotice("제작 시작 중 오류가 발생했습니다.");
        Debug.LogError(
            $"[ManufacturingUI] Unexpected manufacturing start failure: {failureReason}.",
            this);
        Refresh();
    }

    private void HandleCreateViewClosed()
    {
        if (m_isClosing || !m_isOpen)
            return;

        if (m_menuRoot != null)
        {
            SetMenuActive(true);
            SetStage(FacilityUIStage.Menu);
        }
        else
        {
            Close();
        }
    }

    private void HandleCreateButtonClicked()
    {
        if (!m_isOpen)
            return;

        ClearNotice();
        SetMenuActive(false);
        if (m_createView != null && m_createView.Open(0))
        {
            SetStage(FacilityUIStage.Main);
            return;
        }

        SetMenuActive(true);
        SetNotice("제작 화면을 열 수 없습니다.");
    }

    private void HandleUpgradeButtonClicked()
    {
        if (!m_isOpen)
            return;

        // 공용 업그레이드 UI와 함께 Upgrade 버튼을 보여줍니다.
        // 업그레이드 UI가 닫히면 SetMenuVisible(true)에서 다시 Menu로 돌아갑니다.
        ClearNotice();
        SetUpgradeViewOpen(true);
        SetStage(FacilityUIStage.Upgrade);

        if (m_upgradeAction != null)
        {
            m_upgradeAction.OpenUpgradeUI();
            return;
        }

        if (m_currentManager == null)
            return;

        UIManager uiManager = FindFirstObjectByType<UIManager>();
        if (uiManager != null)
            uiManager.OpenFacilityUpgradeUI(m_currentManager.FacilityId);
        else
            Debug.LogWarning("[ManufacturingUI] UIManager is not assigned.", this);
    }

    private void BindCreateButton()
    {
        if (m_boundCreateButton == m_createButton)
            return;

        UnbindCreateButton();
        m_boundCreateButton = m_createButton;
        if (m_boundCreateButton != null)
            m_boundCreateButton.onClick.AddListener(HandleCreateButtonClicked);
    }

    private void UnbindCreateButton()
    {
        if (m_boundCreateButton == null)
            return;

        m_boundCreateButton.onClick.RemoveListener(HandleCreateButtonClicked);
        m_boundCreateButton = null;
    }

    private void BindUpgradeButton()
    {
        if (m_boundUpgradeButton == m_upgradeButton)
            return;

        UnbindUpgradeButton();
        m_boundUpgradeButton = m_upgradeButton;
        if (m_boundUpgradeButton != null)
            m_boundUpgradeButton.onClick.AddListener(HandleUpgradeButtonClicked);
    }

    private void UnbindUpgradeButton()
    {
        if (m_boundUpgradeButton == null)
            return;

        m_boundUpgradeButton.onClick.RemoveListener(HandleUpgradeButtonClicked);
        m_boundUpgradeButton = null;
    }

    /// <summary>
    /// 돌아가기 버튼은 Esc 키와 같은 경로(UIManager)로 처리해 업그레이드 UI 등 최상위 UI부터 한 단계씩 닫습니다.
    /// </summary>
    private void HandleBackButtonClicked()
    {
        if (!m_isOpen)
            return;

        UIManager uiManager = FindFirstObjectByType<UIManager>();
        if (uiManager != null && uiManager.TryHandleEscape())
            return;

        TryHandleEscape();
    }

    private void BindBackButton()
    {
        if (m_boundBackButton == m_backButton)
            return;

        UnbindBackButton();
        m_boundBackButton = m_backButton;
        if (m_boundBackButton != null)
            m_boundBackButton.onClick.AddListener(HandleBackButtonClicked);
    }

    private void UnbindBackButton()
    {
        if (m_boundBackButton == null)
            return;

        m_boundBackButton.onClick.RemoveListener(HandleBackButtonClicked);
        m_boundBackButton = null;
    }

    private void BindCreateView()
    {
        if (m_boundCreateView == m_createView)
            return;

        UnbindCreateView();
        m_boundCreateView = m_createView;
        if (m_boundCreateView == null)
            return;

        m_boundCreateView.ConfirmRequested += HandleCreateRequested;
        m_boundCreateView.Closed += HandleCreateViewClosed;
    }

    private void UnbindCreateView()
    {
        if (m_boundCreateView == null)
            return;

        m_boundCreateView.ConfirmRequested -= HandleCreateRequested;
        m_boundCreateView.Closed -= HandleCreateViewClosed;
        m_boundCreateView = null;
    }

    private void CloseCreateView()
    {
        if (m_createView != null && m_createView.IsOpen)
            m_createView.Cancel();
    }

    private void CacheChildViews()
    {
        if (m_menuRoot == null)
            m_menuRoot = transform.Find("Menu")?.gameObject;

        if (m_createButton == null && m_menuRoot != null)
            m_createButton = m_menuRoot.transform.Find("CreateButton")?.GetComponent<Button>();

        if (m_upgradeButton == null && m_menuRoot != null)
            m_upgradeButton = m_menuRoot.transform.Find("UpgradeButton")?.GetComponent<Button>();

        if (m_upgradeRoot == null)
            m_upgradeRoot = transform.Find("Upgrade")?.gameObject;

        if (m_upgradeAction == null)
            m_upgradeAction = GetComponentInChildren<FacilityUpgradeButton>(true);

        if (m_createView == null)
            m_createView = GetComponentInChildren<ManufacturingCreateView>(true);
    }

    private static bool TryGetExpectedStartFailureMessage(
        ManufacturingStartJobFailureReason failureReason,
        out string message)
    {
        switch (failureReason)
        {
            case ManufacturingStartJobFailureReason.InsufficientResources:
                message = "제작에 필요한 재료가 부족합니다.";
                return true;
            case ManufacturingStartJobFailureReason.SlotOccupied:
                message = "이미 제작 중인 슬롯입니다.";
                return true;
            case ManufacturingStartJobFailureReason.SlotLocked:
                message = "잠긴 제작 슬롯입니다.";
                return true;
            case ManufacturingStartJobFailureReason.RecipeLocked:
                message = "아직 해금되지 않은 레시피입니다.";
                return true;
            case ManufacturingStartJobFailureReason.InvalidQuantity:
                message = "제작 수량을 확인하세요.";
                return true;
            default:
                message = string.Empty;
                return false;
        }
    }

    private void UnbindManager()
    {
        if (m_currentManager != null)
            m_currentManager.StateChanged -= Refresh;

        m_currentManager = null;
    }

    private void SetNotice(string message)
    {
        if (m_noticeText != null)
            m_noticeText.text = message;
        else
            Debug.LogWarning($"[ManufacturingUI] {message}", this);
    }

    private void ClearNotice()
    {
        if (m_noticeText != null)
            m_noticeText.text = string.Empty;
    }

    private void SetRootActive(bool active)
    {
        if (m_root != null && m_root.activeSelf != active)
            m_root.SetActive(active);
    }

    private void SetMenuActive(bool active)
    {
        if (m_menuRoot != null && m_menuRoot.activeSelf != active)
            m_menuRoot.SetActive(active);
    }

    private void SetUpgradeViewOpen(bool open)
    {
        m_isUpgradeViewOpen = open && m_upgradeRoot != null;
        SetUpgradeRootActive(m_isUpgradeViewOpen);
    }

    private void SetUpgradeRootActive(bool active)
    {
        if (m_upgradeRoot != null && m_upgradeRoot.activeSelf != active)
            m_upgradeRoot.SetActive(active);
    }

    private void SetOpenState(bool isOpen)
    {
        if (m_isOpen == isOpen)
            return;

        m_isOpen = isOpen;
        if (!m_isOpen)
        {
            SetStage(FacilityUIStage.None);
            Closed?.Invoke();
        }
    }

    private void SetStage(FacilityUIStage stage)
    {
        if (m_stage == stage)
            return;

        m_stage = stage;
        ContextChanged?.Invoke(CurrentContext);
    }
}
