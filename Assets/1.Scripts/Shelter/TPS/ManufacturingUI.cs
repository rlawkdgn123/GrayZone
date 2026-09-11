using System;
using TMPro;
using UnityEngine;

/// <summary>
/// 제조 시설 UI를 열고 제조 슬롯, 레시피 선택, 헬퍼 UI를 매니저 상태로 투영합니다.
/// </summary>
public sealed class ManufacturingUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject m_root;
    [SerializeField] private bool m_hideOnAwake = true;

    [Header("Manufacturing Slots")]
    [SerializeField] private ManufacturingSlotView[] m_slots;

    [Header("Create View")]
    [SerializeField] private ManufacturingCreateView m_createView;
    [SerializeField] private TMP_Text m_noticeText;

    [Header("Staff")]
    [SerializeField] private ManufacturingStaffSlotController m_staffSlotController;

    private ManufacturingManager m_currentManager;
    private ManufacturingCreateView m_boundCreateView;
    private bool m_isOpening;
    private bool m_isOpen;

    public bool IsOpen => m_isOpen;
    public event Action Closed;

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
        BindCreateView();
    }

    private void OnDisable()
    {
        UnbindCreateView();
        UnbindManager();
        if (!m_isOpening)
            SetOpenState(false);
    }

    private void OnDestroy()
    {
        UnbindCreateView();
    }

    /// <summary>
    /// 현재 제조 UI의 최상위 Escape 동작을 처리합니다.
    /// CreateView가 열려 있으면 그것만 닫고, 아니면 제조 UI 전체를 닫습니다.
    /// </summary>
    public bool TryHandleEscape()
    {
        if (!m_isOpen)
            return false;

        if (m_createView != null && m_createView.TryHandleEscape())
            return true;

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
        BindCreateView();
        CloseCreateView();
        if (m_staffSlotController != null)
            m_staffSlotController.gameObject.SetActive(false);
        // 임시 빌드에서는 제작 헬퍼 UI와 헬퍼 생산력 연결을 사용하지 않는다.
        // m_staffSlotController?.SetManager(m_currentManager);
        ClearNotice();
        Refresh();
    }

    public void Close()
    {
        CloseCreateView();
        UnbindCreateView();
        UnbindManager();
        SetRootActive(false);
        SetOpenState(false);
    }

    public void Refresh()
    {
        CacheChildViews();
        if (m_slots == null)
            return;

        bool allowSlotInput = m_createView == null || !m_createView.IsOpen;
        for (int i = 0; i < m_slots.Length; i++)
        {
            ManufacturingSlotView slot = m_slots[i];
            if (slot == null)
                continue;

            ManufacturingJobRuntimeData job = null;
            ManufacturingRecipeDefinition recipe = null;
            /* 날짜 기반 제작 작업 표시를 다시 사용할 때 복구할 기존 상태 조회.
            job = FindJob(i);
            if (job != null)
                m_currentManager?.TryGetRecipe(job.RecipeId, out recipe);
            */

            slot.Bind(
                i,
                m_currentManager != null && m_currentManager.IsCraftingSlotUnlocked(i),
                m_currentManager != null && m_currentManager.IsCraftingSlotAvailable(i),
                job,
                recipe,
                m_currentManager != null ? m_currentManager.FinalProductivity : 0,
                HandleSlotClicked,
                HandleCancelClicked);
            slot.SetInteractionEnabled(allowSlotInput);
        }

        // 임시 빌드에서는 제작 헬퍼 UI를 표시하지 않는다.
        // m_staffSlotController?.RefreshSlots();
    }

    private void HandleSlotClicked(int slotIndex)
    {
        if (m_currentManager == null
            || !m_currentManager.IsCraftingSlotUnlocked(slotIndex)
            || !m_currentManager.IsCraftingSlotAvailable(slotIndex)
            || (m_createView != null && m_createView.IsOpen))
        {
            return;
        }

        if (m_createView == null)
        {
            SetNotice("제작 상세 창을 찾을 수 없습니다.");
            return;
        }

        ClearNotice();
        if (!m_createView.Open(slotIndex))
        {
            SetNotice("선택한 슬롯에서 제작을 시작할 수 없습니다.");
            return;
        }

        Refresh();
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

    private void HandleCancelClicked(int slotIndex)
    {
        if (m_currentManager != null && m_currentManager.TryCancelJob(slotIndex))
        {
            ClearNotice();
            Refresh();
        }
        else
        {
            SetNotice("제작 작업을 취소할 수 없습니다.");
        }
    }

    private ManufacturingJobRuntimeData FindJob(int slotIndex)
    {
        if (m_currentManager == null)
            return null;

        var jobs = m_currentManager.Jobs;
        for (int i = 0; i < jobs.Count; i++)
        {
            if (jobs[i] != null && jobs[i].SlotIndex == slotIndex)
                return jobs[i];
        }

        return null;
    }

    private void HandleCreateViewClosed()
    {
        ClearNotice();
        Refresh();
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
        if (m_slots == null || m_slots.Length == 0)
            m_slots = GetComponentsInChildren<ManufacturingSlotView>(true);

        if (m_staffSlotController == null)
            m_staffSlotController = GetComponentInChildren<ManufacturingStaffSlotController>(true);

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

    private void SetOpenState(bool isOpen)
    {
        if (m_isOpen == isOpen)
            return;

        m_isOpen = isOpen;
        if (!m_isOpen)
            Closed?.Invoke();
    }
}
