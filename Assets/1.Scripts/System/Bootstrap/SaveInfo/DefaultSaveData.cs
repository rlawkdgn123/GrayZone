using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>새 게임 시작 시 사용할 최소 Auto 복구 데이터의 작성 템플릿입니다.</summary>
[CreateAssetMenu(fileName = "DefaultSaveData", menuName = "GrayZone/Save/Default Save Data")]
public sealed class DefaultSaveData : ScriptableObject
{
    [Serializable]
    public struct StartingResourceAmount
    {
        [SerializeField] private string resourceId;
        [SerializeField, Min(0)] private int amount;

        public StartingResourceAmount(string resourceId, int amount)
        {
            this.resourceId = resourceId;
            this.amount = amount;
        }

        public string ResourceId => ResourceIds.Normalize(resourceId);
        public int Amount => Mathf.Max(0, amount);
    }

    [Header("Checkpoint")]
    [SerializeField] private ShelterCheckpointId shelterCheckpointId = ShelterCheckpointId.BeforeDefense1;

    [Header("Defense Upgrade Defaults")]
    [SerializeField, Min(0)] private int trapUpgradeLevel;
    [SerializeField, Min(0)] private int spikeUpgradeLevel;
    [SerializeField, Min(0)] private int explosiveUpgradeLevel;
    [SerializeField, Min(0)] private int shooterUpgradeLevel;
    [SerializeField, Min(0)] private int wireUpgradeLevel;

    [Header("Starting State")]
    [Tooltip("StartingResource 시트(Assets/5.Data/DataSheet/TableData/StartingResource.csv)에서 가져옵니다. 여기서 직접 고쳐도 다음 가져오기에서 덮어씁니다.")]
    [SerializeField] private StartingResourceAmount[] startingResources = Array.Empty<StartingResourceAmount>();
    [SerializeField] private PlayerbleCharacterDefinition[] startingCharacterDefinitions = Array.Empty<PlayerbleCharacterDefinition>();

    public SaveData CreateSaveData()
    {
        SaveData saveData = new SaveData
        {
            schemaVersion = SaveData.CurrentSchemaVersion,
            shelterCheckpointId = shelterCheckpointId,
            defenseClearCount = 0,
            shelterFlowState = ShelterFlowState.NotStarted,
            trapUpgradeLevel = Mathf.Max(0, trapUpgradeLevel),
            spikeUpgradeLevel = Mathf.Max(0, spikeUpgradeLevel),
            explosiveUpgradeLevel = Mathf.Max(0, explosiveUpgradeLevel),
            shooterUpgradeLevel = Mathf.Max(0, shooterUpgradeLevel),
            wireUpgradeLevel = Mathf.Max(0, wireUpgradeLevel)
        };

        AddStartingResources(saveData);
        AddStartingCharacterStates(saveData);
        return saveData;
    }

    public List<CharacterSnapshotData> CreateStartingCharacterSnapshots()
    {
        List<CharacterSnapshotData> snapshots = new List<CharacterSnapshotData>();
        if (startingCharacterDefinitions == null)
        {
            return snapshots;
        }

        HashSet<PlayerbleCharacterId> addedCharacterIds = new HashSet<PlayerbleCharacterId>();
        for (int i = 0; i < startingCharacterDefinitions.Length; i++)
        {
            PlayerbleCharacterDefinition definition = startingCharacterDefinitions[i];
            if (definition == null
                || definition.CharacterId == PlayerbleCharacterId.Unknown
                || !addedCharacterIds.Add(definition.CharacterId))
            {
                continue;
            }

            snapshots.Add(definition.CreateSnapshot());
        }

        return snapshots;
    }

    private void AddStartingResources(SaveData saveData)
    {
        if (startingResources == null)
        {
            return;
        }

        HashSet<string> addedResourceIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < startingResources.Length; i++)
        {
            StartingResourceAmount startingResource = startingResources[i];
            string resourceId = startingResource.ResourceId;
            if (string.IsNullOrEmpty(resourceId) || !addedResourceIds.Add(resourceId))
            {
                continue;
            }

            saveData.resources.Add(new SaveData.ResourceAmountData
            {
                resourceId = resourceId,
                amount = startingResource.Amount
            });
        }
    }

    private void AddStartingCharacterStates(SaveData saveData)
    {
        List<CharacterSnapshotData> snapshots = CreateStartingCharacterSnapshots();
        for (int i = 0; i < snapshots.Count; i++)
        {
            CharacterSnapshotData snapshot = snapshots[i];
            saveData.characters.Add(new SaveData.CharacterStateSaveData
            {
                characterId = snapshot.CharacterId,
                currentHp = snapshot.CurrentHp,
                maxHp = snapshot.MaxHp,
                injurySeverityGauge = snapshot.InjurySeverityGauge,
                maxInjuryGauge = snapshot.MaxInjuryGauge,
                injuryState = snapshot.InjuryState,
                isDown = snapshot.IsDown,
                isCombatOut = snapshot.IsCombatOut
            });
        }
    }

#if UNITY_EDITOR
    /// <summary>시트 임포터 전용입니다. 검증을 마친 <c>StartingResource</c> 시트 행으로 시작 자원을 통째로 바꿉니다.</summary>
    public void EditorReplaceStartingResources(List<StartingResourceAmount> rows)
    {
        startingResources = rows.ToArray();
    }
#endif
}
