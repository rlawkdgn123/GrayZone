using System;
using UnityEngine;
using VInspector;

/// <summary>
/// Scramble 시설의 전투 씬 이동과 독립 슈터 해금 상태를 관리합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ScrambleFacility : MonoBehaviour
{
    [Header("Scene Transition")]
    [SerializeField] private string m_battleSceneName = "CombatPlayTest";

    [Header("Shooter Upgrade Cost")]
    [Variants(
        ResourceIds.Food,
        ResourceIds.Fuel,
        ResourceIds.FacilityUpgradePart,
        ResourceIds.UpgradePartMaterial,
        ResourceIds.WeaponPartMaterial,
        ResourceIds.MedicineMaterial)]
    [SerializeField] private string m_upgradeResourceId = ResourceIds.FacilityUpgradePart;
    [Min(0)][SerializeField] private int m_shooter01Cost = 1;
    [Min(0)][SerializeField] private int m_shooter02Cost = 1;

    public string BattleSceneName => m_battleSceneName?.Trim() ?? string.Empty;
    public string UpgradeResourceId => ResourceIds.Normalize(m_upgradeResourceId);
    public int Shooter01Cost => Mathf.Max(0, m_shooter01Cost);
    public int Shooter02Cost => Mathf.Max(0, m_shooter02Cost);
    public bool Shooter01 => GameDataManager.Instance != null && GameDataManager.Instance.Shooter01;
    public bool Shooter02 => GameDataManager.Instance != null && GameDataManager.Instance.Shooter02;

    public event Action StateChanged;

    private StorageFacility Storage => ShelterSceneDataManager.Instance?.Storage;

    private void OnValidate()
    {
        m_shooter01Cost = Mathf.Max(0, m_shooter01Cost);
        m_shooter02Cost = Mathf.Max(0, m_shooter02Cost);
    }

    /// <summary>현재 보유한 시설 업그레이드 자원 수량을 반환합니다.</summary>
    public int GetOwnedUpgradeResourceAmount()
    {
        return Storage?.GetResourceAmount(UpgradeResourceId) ?? 0;
    }

    public bool CanUnlockShooter01()
    {
        return !Shooter01 && CanSpendUpgradeResource(Shooter01Cost);
    }

    public bool CanUnlockShooter02()
    {
        return !Shooter02 && CanSpendUpgradeResource(Shooter02Cost);
    }

    /// <summary>시설 자원을 소비하고 첫 번째 슈터를 해금합니다.</summary>
    public bool TryUnlockShooter01()
    {
        GameDataManager gameData = GameDataManager.Instance;
        if (gameData == null || gameData.Shooter01)
            return false;

        if (!TrySpendUpgradeResource(Shooter01Cost))
            return false;

        gameData.SetShooter01Active(true);
        NotifyStateChanged();
        return true;
    }

    /// <summary>시설 자원을 소비하고 두 번째 슈터를 해금합니다.</summary>
    public bool TryUnlockShooter02()
    {
        GameDataManager gameData = GameDataManager.Instance;
        if (gameData == null || gameData.Shooter02)
            return false;

        if (!TrySpendUpgradeResource(Shooter02Cost))
            return false;

        gameData.SetShooter02Active(true);
        NotifyStateChanged();
        return true;
    }

    /// <summary>현재 셸터 작업 데이터를 전역 데이터에 동기화한 뒤 전투 씬으로 이동합니다.</summary>
    public bool TryLoadBattleScene()
    {
        string sceneName = BattleSceneName;
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning("[ScrambleFacility] Battle scene name is empty.", this);
            return false;
        }

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogWarning(
                $"[ScrambleFacility] Battle scene is not available in Build Settings: {sceneName}",
                this);
            return false;
        }

        ShelterSceneDataManager shelterData = ShelterSceneDataManager.Instance;
        if (shelterData == null || !shelterData.PushToDataManager())
        {
            Debug.LogWarning(
                "[ScrambleFacility] Failed to synchronize shelter data before scene transition.",
                this);
            return false;
        }

        SceneTransitionController.LoadScene(sceneName);
        return true;
    }

    private bool CanSpendUpgradeResource(int amount)
    {
        if (amount <= 0)
            return true;

        StorageFacility storage = Storage;
        return storage != null
            && storage.CanSpendResource(
                new ResourceCost(UpgradeResourceId, amount));
    }

    private bool TrySpendUpgradeResource(int amount)
    {
        if (amount <= 0)
            return true;

        StorageFacility storage = Storage;
        if (storage == null)
        {
            Debug.LogWarning(
                "[ScrambleFacility] StorageFacility is not available.",
                this);
            return false;
        }

        return storage.TrySpendResource(
            new ResourceCost(UpgradeResourceId, amount));
    }

    private void NotifyStateChanged()
    {
        ShelterSceneDataManager.Instance?.MarkDirty();
        StateChanged?.Invoke();
    }
}
