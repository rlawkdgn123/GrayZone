using System;
using UnityEngine;
using UnityEngine.Serialization;
using VInspector;

public enum ScrambleUpgradeType
{
    Trap,
    Spike,
    Explosive,
    Shooter,
    Wire
}

/// <summary>
/// Scramble 시설의 전투 씬 이동과 필드 배치 업그레이드를 관리합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ScrambleFacility : MonoBehaviour
{
    [Header("Scene Transition")]
    [SerializeField] private string m_battleSceneName = "CombatPlayTest";
    [SerializeField] private ShelterSceneDataManager m_shelterSceneDataManager;

    [Header("Upgrade Resource")]
    [Variants(
        ResourceIds.UpgradeMaterial,
        ResourceIds.CraftingMaterial,
        ResourceIds.TrapMaterial,
        ResourceIds.TrapUpgradeMaterial)]
    [SerializeField] private string m_upgradeResourceId = ResourceIds.TrapUpgradeMaterial;

    [Header("Upgrade Table")]
    [Tooltip("레벨별 비용과 Info 문구 표입니다. 연결하면 아래 단일 비용·최대 레벨 대신 이 표를 씁니다. 해당 타입 배열이 비어 있으면 아래 값을 씁니다.")]
    [SerializeField] private ScrambleUpgradeTableSO m_upgradeTable;

    [Header("Upgrade Cost Per Level")]
    [Min(0)][SerializeField] private int m_trapUpgradeCost = 1;
    [Min(0)][SerializeField] private int m_spikeUpgradeCost = 1;
    [Min(0)][SerializeField] private int m_explosiveUpgradeCost = 1;
    [FormerlySerializedAs("m_shooter01Cost")]
    [Min(0)][SerializeField] private int m_shooterUpgradeCost = 1;
    [Min(0)][SerializeField] private int m_wireUpgradeCost = 1;

    [Header("Max Upgrade Level")]
    [Min(0)][SerializeField] private int m_trapMaxLevel = 1;
    [Min(0)][SerializeField] private int m_spikeMaxLevel = 1;
    [Min(0)][SerializeField] private int m_explosiveMaxLevel = 1;
    [Min(0)][SerializeField] private int m_shooterMaxLevel = 2;
    [Min(0)][SerializeField] private int m_wireMaxLevel = 1;

    public string BattleSceneName => m_battleSceneName?.Trim() ?? string.Empty;
    public string UpgradeResourceId => ResourceIds.Normalize(m_upgradeResourceId);
    public int TrapUpgradeCost => Mathf.Max(0, m_trapUpgradeCost);
    public int SpikeUpgradeCost => Mathf.Max(0, m_spikeUpgradeCost);
    public int ExplosiveUpgradeCost => Mathf.Max(0, m_explosiveUpgradeCost);
    public int ShooterUpgradeCost => Mathf.Max(0, m_shooterUpgradeCost);
    public int WireUpgradeCost => Mathf.Max(0, m_wireUpgradeCost);
    public int TrapUpgradeLevel => GetUpgradeLevel(ScrambleUpgradeType.Trap);
    public int SpikeUpgradeLevel => GetUpgradeLevel(ScrambleUpgradeType.Spike);
    public int ExplosiveUpgradeLevel => GetUpgradeLevel(ScrambleUpgradeType.Explosive);
    public int ShooterUpgradeLevel => GetUpgradeLevel(ScrambleUpgradeType.Shooter);
    public int WireUpgradeLevel => GetUpgradeLevel(ScrambleUpgradeType.Wire);

    public event Action StateChanged;

    private StorageFacility Storage => ShelterSceneDataManager.Instance?.Storage;

    private void OnValidate()
    {
        m_trapUpgradeCost = Mathf.Max(0, m_trapUpgradeCost);
        m_spikeUpgradeCost = Mathf.Max(0, m_spikeUpgradeCost);
        m_explosiveUpgradeCost = Mathf.Max(0, m_explosiveUpgradeCost);
        m_shooterUpgradeCost = Mathf.Max(0, m_shooterUpgradeCost);
        m_wireUpgradeCost = Mathf.Max(0, m_wireUpgradeCost);
        m_trapMaxLevel = Mathf.Max(0, m_trapMaxLevel);
        m_spikeMaxLevel = Mathf.Max(0, m_spikeMaxLevel);
        m_explosiveMaxLevel = Mathf.Max(0, m_explosiveMaxLevel);
        m_shooterMaxLevel = Mathf.Max(0, m_shooterMaxLevel);
        m_wireMaxLevel = Mathf.Max(0, m_wireMaxLevel);
    }

    /// <summary>현재 보유한 시설 업그레이드 자원 수량을 반환합니다.</summary>
    public int GetOwnedUpgradeResourceAmount()
    {
        return Storage?.GetResourceAmount(UpgradeResourceId) ?? 0;
    }

    public bool CanUpgrade(ScrambleUpgradeType upgradeType)
    {
        if (GameDataManager.Instance == null)
            return false;

        int currentLevel = GetUpgradeLevel(upgradeType);
        int maxLevel = GetMaxUpgradeLevel(upgradeType);

        return currentLevel < maxLevel
            && CanSpendUpgradeResource(GetUpgradeCost(upgradeType));
    }

    public bool TryUpgradeTrap() => TryUpgrade(ScrambleUpgradeType.Trap);

    public bool TryUpgradeSpike() => TryUpgrade(ScrambleUpgradeType.Spike);

    public bool TryUpgradeExplosive() => TryUpgrade(ScrambleUpgradeType.Explosive);

    public bool TryUpgradeShooter() => TryUpgrade(ScrambleUpgradeType.Shooter);

    public bool TryUpgradeWire() => TryUpgrade(ScrambleUpgradeType.Wire);

    /// <summary>자원을 소비하고 지정한 필드 배치 업그레이드 레벨을 1 올립니다.</summary>
    public bool TryUpgrade(ScrambleUpgradeType upgradeType)
    {
        GameDataManager gameData = GameDataManager.Instance;
        int currentLevel = GetUpgradeLevel(upgradeType);
        int maxLevel = GetMaxUpgradeLevel(upgradeType);
        if (gameData == null || currentLevel >= maxLevel)
            return false;

        if (!TrySpendUpgradeResource(GetUpgradeCost(upgradeType)))
            return false;

        SetUpgradeLevel(gameData, upgradeType, currentLevel + 1);
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

        ShelterSceneDataManager shelterData = CacheShelterSceneDataManager();
        if (shelterData == null || !shelterData.PushToDataManager())
        {
            Debug.LogWarning(
                "[ScrambleFacility] Failed to synchronize shelter data before scene transition.",
                this);
            return false;
        }

        GameDataManager gameData = GameDataManager.Instance;
        if (gameData == null || !gameData.TrySetFixedDefenseSquad())
        {
            Debug.LogWarning(
                "[ScrambleFacility] Failed to set the fixed defense squad.",
                this);
            return false;
        }

        SceneTransitionController.LoadScene(sceneName);
        return true;
    }

    private ShelterSceneDataManager CacheShelterSceneDataManager()
    {
        if (m_shelterSceneDataManager == null)
            m_shelterSceneDataManager = ShelterSceneDataManager.Instance;

        if (m_shelterSceneDataManager == null)
            m_shelterSceneDataManager = FindFirstObjectByType<ShelterSceneDataManager>();

        return m_shelterSceneDataManager;
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

    public int GetUpgradeLevel(ScrambleUpgradeType upgradeType)
    {
        GameDataManager gameData = GameDataManager.Instance;
        if (gameData == null)
            return 0;

        return upgradeType switch
        {
            ScrambleUpgradeType.Trap => gameData.TrapUpgradeLevel,
            ScrambleUpgradeType.Spike => gameData.SpikeUpgradeLevel,
            ScrambleUpgradeType.Explosive => gameData.ExplosiveUpgradeLevel,
            ScrambleUpgradeType.Shooter => gameData.ShooterUpgradeLevel,
            ScrambleUpgradeType.Wire => gameData.WireUpgradeLevel,
            _ => 0
        };
    }

    /// <summary>다음 레벨로 올리는 비용입니다. 표가 있으면 다음 레벨 칸의 비용을 씁니다.</summary>
    public int GetUpgradeCost(ScrambleUpgradeType upgradeType)
    {
        if (m_upgradeTable != null
            && m_upgradeTable.TryGetLevel(
                upgradeType,
                GetUpgradeLevel(upgradeType) + 1,
                out ScrambleUpgradeTableSO.LevelEntry entry))
            return entry.Cost;

        return upgradeType switch
        {
            ScrambleUpgradeType.Trap => TrapUpgradeCost,
            ScrambleUpgradeType.Spike => SpikeUpgradeCost,
            ScrambleUpgradeType.Explosive => ExplosiveUpgradeCost,
            ScrambleUpgradeType.Shooter => ShooterUpgradeCost,
            ScrambleUpgradeType.Wire => WireUpgradeCost,
            _ => 0
        };
    }

    public int GetMaxUpgradeLevel(ScrambleUpgradeType upgradeType)
    {
        if (m_upgradeTable != null && m_upgradeTable.TryGetMaxLevel(upgradeType, out int tableMaxLevel))
            return tableMaxLevel;

        return upgradeType switch
        {
            ScrambleUpgradeType.Trap => m_trapMaxLevel,
            ScrambleUpgradeType.Spike => m_spikeMaxLevel,
            ScrambleUpgradeType.Explosive => m_explosiveMaxLevel,
            ScrambleUpgradeType.Shooter => m_shooterMaxLevel,
            ScrambleUpgradeType.Wire => m_wireMaxLevel,
            _ => 0
        };
    }

    /// <summary>지정한 레벨(1부터)의 Info 문구를 표에서 찾습니다. 표가 없거나 칸이 없으면 빈 문자열입니다.</summary>
    public string GetUpgradeLevelInfo(ScrambleUpgradeType upgradeType, int level)
    {
        return m_upgradeTable != null
            && m_upgradeTable.TryGetLevel(upgradeType, level, out ScrambleUpgradeTableSO.LevelEntry entry)
            ? entry.Info
            : string.Empty;
    }

    private static void SetUpgradeLevel(
        GameDataManager gameData,
        ScrambleUpgradeType upgradeType,
        int level)
    {
        switch (upgradeType)
        {
            case ScrambleUpgradeType.Trap:
                gameData.SetTrapUpgradeLevel(level);
                break;
            case ScrambleUpgradeType.Spike:
                gameData.SetSpikeUpgradeLevel(level);
                break;
            case ScrambleUpgradeType.Explosive:
                gameData.SetExplosiveUpgradeLevel(level);
                break;
            case ScrambleUpgradeType.Shooter:
                gameData.SetShooterUpgradeLevel(level);
                break;
            case ScrambleUpgradeType.Wire:
                gameData.SetWireUpgradeLevel(level);
                break;
        }
    }

    private void NotifyStateChanged()
    {
        ShelterSceneDataManager.Instance?.MarkDirty();
        StateChanged?.Invoke();
    }
}
