using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Scramble 시설 버튼 입력을 시설 로직에 전달하고 현재 슈터 상태를 표시합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ScrambleFacilityUI : MonoBehaviour
{
    [Header("Facility")]
    [SerializeField] private ScrambleFacility m_facility;

    [Header("Buttons")]
    [SerializeField] private Button m_battleSceneButton;
    [SerializeField] private Button m_shooter01UpgradeButton;
    [SerializeField] private Button m_shooter02UpgradeButton;

    [Header("Status (Optional)")]
    [SerializeField] private TMP_Text m_shooter01StatusText;
    [SerializeField] private TMP_Text m_shooter02StatusText;
    [SerializeField] private TMP_Text m_shooter01CostText;
    [SerializeField] private TMP_Text m_shooter02CostText;
    [SerializeField] private TMP_Text m_noticeText;

    private void Awake()
    {
        CacheFacility();
    }

    private void OnEnable()
    {
        CacheFacility();
        Bind();
        Refresh();
    }

    private void OnDisable()
    {
        Unbind();
    }

    public void Refresh()
    {
        if (m_facility == null)
            return;

        int owned = m_facility.GetOwnedUpgradeResourceAmount();
        bool shooter01 = m_facility.Shooter01;
        bool shooter02 = m_facility.Shooter02;

        if (m_battleSceneButton != null)
            m_battleSceneButton.interactable = true;
        if (m_shooter01UpgradeButton != null)
            m_shooter01UpgradeButton.interactable = m_facility.CanUnlockShooter01();
        if (m_shooter02UpgradeButton != null)
            m_shooter02UpgradeButton.interactable = m_facility.CanUnlockShooter02();

        SetText(m_shooter01StatusText, shooter01 ? "활성화" : "비활성화");
        SetText(m_shooter02StatusText, shooter02 ? "활성화" : "비활성화");
        SetText(
            m_shooter01CostText,
            shooter01 ? "완료" : $"{owned}/{m_facility.Shooter01Cost}");
        SetText(
            m_shooter02CostText,
            shooter02 ? "완료" : $"{owned}/{m_facility.Shooter02Cost}");
    }

    private void HandleBattleSceneClicked()
    {
        ClearNotice();
        if (m_facility == null || !m_facility.TryLoadBattleScene())
            SetNotice("전투 씬으로 이동하지 못했습니다.");
    }

    private void HandleShooter01UpgradeClicked()
    {
        ClearNotice();
        if (m_facility == null || !m_facility.TryUnlockShooter01())
            SetNotice("Shooter01을 활성화하지 못했습니다.");
    }

    private void HandleShooter02UpgradeClicked()
    {
        ClearNotice();
        if (m_facility == null || !m_facility.TryUnlockShooter02())
            SetNotice("Shooter02를 활성화하지 못했습니다.");
    }

    private void HandleFacilityStateChanged()
    {
        ClearNotice();
        Refresh();
    }

    private void Bind()
    {
        Unbind();

        if (m_battleSceneButton != null)
            m_battleSceneButton.onClick.AddListener(HandleBattleSceneClicked);
        if (m_shooter01UpgradeButton != null)
            m_shooter01UpgradeButton.onClick.AddListener(HandleShooter01UpgradeClicked);
        if (m_shooter02UpgradeButton != null)
            m_shooter02UpgradeButton.onClick.AddListener(HandleShooter02UpgradeClicked);
        if (m_facility != null)
            m_facility.StateChanged += HandleFacilityStateChanged;
    }

    private void Unbind()
    {
        if (m_battleSceneButton != null)
            m_battleSceneButton.onClick.RemoveListener(HandleBattleSceneClicked);
        if (m_shooter01UpgradeButton != null)
            m_shooter01UpgradeButton.onClick.RemoveListener(HandleShooter01UpgradeClicked);
        if (m_shooter02UpgradeButton != null)
            m_shooter02UpgradeButton.onClick.RemoveListener(HandleShooter02UpgradeClicked);
        if (m_facility != null)
            m_facility.StateChanged -= HandleFacilityStateChanged;
    }

    private void CacheFacility()
    {
        if (m_facility == null)
            m_facility = FindFirstObjectByType<ScrambleFacility>();
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
}
