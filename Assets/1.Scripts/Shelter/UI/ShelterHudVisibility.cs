using UnityEngine;

/// <summary>
/// 시설 UI 등 차단형 UI가 열려 있는 동안 HUD(미니맵, 목표 안내 등)를 숨김
/// </summary>
/// <remarks>
/// 대상의 활성 상태(SetActive)는 다른 스크립트가 관리할 수 있으므로 건드리지 않고
/// CanvasGroup의 alpha와 입력 차단만 바꿔서 숨깁니다.
/// </remarks>
public class ShelterHudVisibility : MonoBehaviour
{
    [Tooltip("비워두면 씬의 UIManager를 자동 탐색")]
    [SerializeField] private UIManager uiManager;
    [Tooltip("숨길 HUD 루트. CanvasGroup이 없으면 자동으로 추가")]
    [SerializeField] private GameObject[] targets;

    private CanvasGroup[] canvasGroups;

    private void Awake()
    {
        if (uiManager == null)
            uiManager = FindFirstObjectByType<UIManager>(FindObjectsInactive.Include);

        canvasGroups = new CanvasGroup[targets.Length];
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null)
                continue;

            if (!targets[i].TryGetComponent(out canvasGroups[i]))
                canvasGroups[i] = targets[i].AddComponent<CanvasGroup>();
        }
    }

    private void OnEnable()
    {
        if (uiManager == null)
            return;

        uiManager.HudSuppressionChanged += HandleHudSuppressionChanged;
        HandleHudSuppressionChanged(uiManager.IsHudSuppressed);
    }

    private void OnDisable()
    {
        if (uiManager != null)
            uiManager.HudSuppressionChanged -= HandleHudSuppressionChanged;

        SetHudVisible(true);
    }

    // 차단형 UI가 열려 있거나 NPC 상호작용 대사가 표시 중이면 숨김
    private void HandleHudSuppressionChanged(bool suppressed)
    {
        SetHudVisible(!suppressed);
    }

    private void SetHudVisible(bool visible)
    {
        if (canvasGroups == null)
            return;

        foreach (CanvasGroup group in canvasGroups)
        {
            if (group == null)
                continue;

            group.alpha = visible ? 1f : 0f;
            group.blocksRaycasts = visible;
            group.interactable = visible;
        }
    }
}
