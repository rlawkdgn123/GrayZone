using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>GameDataManager가 평탄하게 보관하는 셸터 전용 캐릭터 시설 배치 정보입니다.</summary>
[Serializable]
public sealed class ShelterCharacterAssignmentData
{
    [SerializeField] private string runtimeId = string.Empty;
    [SerializeField] private string facilityId = string.Empty;
    [SerializeField] private string roomId = string.Empty;
    [SerializeField] private FacilityAssignmentKind kind;

    public string RuntimeId => runtimeId ?? string.Empty;
    public string FacilityId => facilityId ?? string.Empty;
    public string RoomId => roomId ?? string.Empty;
    public FacilityAssignmentKind Kind => kind;
    public bool IsAssigned => !string.IsNullOrWhiteSpace(RuntimeId)
        && !string.IsNullOrWhiteSpace(FacilityId)
        && Kind != FacilityAssignmentKind.None;

    public ShelterCharacterAssignmentData(
        string runtimeId,
        string facilityId,
        string roomId,
        FacilityAssignmentKind kind)
    {
        this.runtimeId = runtimeId?.Trim() ?? string.Empty;
        this.facilityId = facilityId?.Trim() ?? string.Empty;
        this.roomId = string.IsNullOrWhiteSpace(roomId) ? this.facilityId : roomId.Trim();
        this.kind = kind;
    }

    public ShelterCharacterAssignmentData(ShelterMemberRuntimeData character)
        : this(
            character?.RuntimeId,
            character?.AssignedFacilityId,
            character?.AssignedRoomId,
            character?.AssignmentKind ?? FacilityAssignmentKind.None)
    {
    }

    public ShelterCharacterAssignmentData Clone()
        => new ShelterCharacterAssignmentData(RuntimeId, FacilityId, RoomId, Kind);
}

/// <summary>GameDataManager에서 셸터 씬으로 전달하는 진입 패킷입니다.</summary>
[Serializable]
public sealed class ShelterEntryData
{
    [SerializeField] private ShelterRuntimeData initialState = new();

    public ShelterEntryData(ShelterRuntimeData source)
    {
        initialState = source?.Clone() ?? new ShelterRuntimeData();
    }

    public ShelterRuntimeData CreateRuntimeData() => initialState?.Clone() ?? new ShelterRuntimeData();
    public ShelterEntryData Clone() => new ShelterEntryData(initialState);
}

/// <summary>GameDataManager의 평탄 정본에서 셸터 씬이 사용할 값만 복사해 담는 작업 데이터입니다.</summary>
[Serializable]
public sealed class ShelterRuntimeData
{
    public const int MinFieldSquadSize = 1;
    public const int MaxFieldSquadSize = 3;

    [SerializeField] private int shelterStability = 100;
    [SerializeField] private int currentDay = 1;
    [SerializeField] private bool foodShortagePenaltyActive;
    [SerializeField] private bool fuelShortagePenaltyActive;
    [FormerlySerializedAs("battleSquadNpcDefinitionIds")]
    [FormerlySerializedAs("battleSquadRuntimeIds")]
    [SerializeField] private List<string> fieldSquadRuntimeIds = new();
    [SerializeField] private List<FacilityRuntimeState> facilityStates = new();
    [SerializeField] private List<ShelterMemberRuntimeData> characters = new();
    [SerializeField] private ManufacturingRuntimeData manufacturing = new();
    [SerializeField] private List<ItemStorageEntry> itemStorageEntries = new();

    private ResourceStorage resources;

    public int ShelterStability => Mathf.Clamp(shelterStability, 0, 100);
    public int PlayerbleCharacterCount => CharacterCount;
    public int NonPlayerbleNpcCount => 0;
    public int TotalOwnedCharacterCount => CharacterCount;
    public int CharacterCount => Characters.Count;
    public int CurrentDay => Mathf.Max(1, currentDay);
    public bool FoodShortagePenaltyActive => foodShortagePenaltyActive;
    public bool FuelShortagePenaltyActive => fuelShortagePenaltyActive;
    public IReadOnlyList<string> FieldSquadRuntimeIds => fieldSquadRuntimeIds;
    public IReadOnlyList<FacilityRuntimeState> FacilityStates => facilityStates;
    public IReadOnlyList<ShelterMemberRuntimeData> Characters => characters;
    public IReadOnlyList<ItemStorageEntry> ItemStorageEntries => itemStorageEntries;

    internal List<ItemStorageEntry> MutableItemStorageEntries
    {
        get
        {
            itemStorageEntries ??= new List<ItemStorageEntry>();
            return itemStorageEntries;
        }
    }

    public ManufacturingRuntimeData Manufacturing
    {
        get
        {
            manufacturing ??= new ManufacturingRuntimeData();
            manufacturing.EnsureValid();
            return manufacturing;
        }
    }

    public ResourceStorage Resources
    {
        get
        {
            resources ??= new ResourceStorage();
            return resources;
        }
    }

    public void EnsureRuntimeContainers()
    {
        shelterStability = Mathf.Clamp(shelterStability, 0, 100);
        currentDay = Mathf.Max(1, currentDay);
        fieldSquadRuntimeIds ??= new List<string>();
        facilityStates ??= new List<FacilityRuntimeState>();
        characters ??= new List<ShelterMemberRuntimeData>();
        itemStorageEntries ??= new List<ItemStorageEntry>();
        _ = Resources;
        _ = Manufacturing;

        NormalizeCharacters();
        NormalizeFieldSquad();
        NormalizeItemStorageEntries();
        for (int i = facilityStates.Count - 1; i >= 0; i--)
        {
            if (facilityStates[i] == null)
                facilityStates.RemoveAt(i);
            else
                facilityStates[i].EnsureValid();
        }
    }

    public ShelterRuntimeData Clone()
    {
        EnsureRuntimeContainers();
        ShelterRuntimeData clone = new ShelterRuntimeData
        {
            shelterStability = ShelterStability,
            currentDay = CurrentDay,
            foodShortagePenaltyActive = FoodShortagePenaltyActive,
            fuelShortagePenaltyActive = FuelShortagePenaltyActive,
            fieldSquadRuntimeIds = new List<string>(fieldSquadRuntimeIds),
            facilityStates = CloneFacilityStates(facilityStates),
            characters = CloneCharacters(characters),
            manufacturing = Manufacturing.Clone(),
            itemStorageEntries = CloneItemStorageEntries(itemStorageEntries)
        };
        clone.Resources.CopyFrom(Resources);
        return clone;
    }

    public void CopyFrom(ShelterRuntimeData source)
    {
        if (source == null || ReferenceEquals(source, this))
            return;

        source.EnsureRuntimeContainers();
        itemStorageEntries ??= new List<ItemStorageEntry>();
        shelterStability = source.ShelterStability;
        currentDay = source.CurrentDay;
        foodShortagePenaltyActive = source.FoodShortagePenaltyActive;
        fuelShortagePenaltyActive = source.FuelShortagePenaltyActive;
        fieldSquadRuntimeIds = new List<string>(source.fieldSquadRuntimeIds);
        facilityStates = CloneFacilityStates(source.facilityStates);
        characters = CloneCharacters(source.characters);
        Manufacturing.CopyFrom(source.Manufacturing);
        CopyItemStorageEntries(source.itemStorageEntries, itemStorageEntries);
        Resources.CopyFrom(source.Resources);
        EnsureRuntimeContainers();
    }

    public void SetShelterStability(int stability) => shelterStability = Mathf.Clamp(stability, 0, 100);

    public void SetCurrentDay(int day) => currentDay = Mathf.Max(1, day);

    public void SetResourceShortagePenaltyState(bool foodActive, bool fuelActive)
    {
        foodShortagePenaltyActive = foodActive;
        fuelShortagePenaltyActive = fuelActive;
    }

    /// <summary>현재 보유 아이템 목록을 깊은 복사해 셸터 작업 데이터에 적용합니다.</summary>
    public void SetItemStorageEntries(IEnumerable<ItemStorageEntry> entries)
    {
        itemStorageEntries ??= new List<ItemStorageEntry>();
        CopyItemStorageEntries(entries, itemStorageEntries);
        NormalizeItemStorageEntries();
    }

    public void ApplySavedState(
        int day,
        IEnumerable<string> squadRuntimeIds,
        IEnumerable<FacilityRuntimeState> savedFacilityStates)
    {
        SetCurrentDay(day);
        fieldSquadRuntimeIds.Clear();
        if (squadRuntimeIds != null)
        {
            foreach (string runtimeId in squadRuntimeIds)
                TryAddFieldSquadCharacter(runtimeId);
        }

        facilityStates.Clear();
        if (savedFacilityStates == null)
            return;

        foreach (FacilityRuntimeState state in savedFacilityStates)
        {
            if (state == null)
                continue;

            state.EnsureValid();
            if (!string.IsNullOrWhiteSpace(state.facilityId))
                facilityStates.Add(new FacilityRuntimeState(state.facilityId, state.isUnlocked, state.upgradeLevel));
        }
    }

    public void SetCharacters(IEnumerable<CharacterSnapshotData> snapshots)
    {
        characters.Clear();
        if (snapshots == null)
            return;

        foreach (CharacterSnapshotData snapshot in snapshots)
            TryAddCharacter(snapshot, out _);
    }

    public bool TryAddCharacter(CharacterSnapshotData snapshot, out ShelterMemberRuntimeData character)
    {
        character = null;
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.DefinitionId))
            return false;

        ShelterMemberRuntimeData created = new ShelterMemberRuntimeData(snapshot);
        if (!AddCharacter(created))
            return false;

        character = created;
        return true;
    }

    public bool AddCharacter(ShelterMemberRuntimeData character)
    {
        if (character == null || string.IsNullOrWhiteSpace(character.RuntimeId))
            return false;
        if (TryGetCharacter(character.RuntimeId, out _))
            return false;

        characters.Add(character);
        return true;
    }

    public bool RemoveCharacter(string runtimeId)
    {
        if (!TryGetCharacter(runtimeId, out ShelterMemberRuntimeData character))
            return false;

        characters.Remove(character);
        RemoveCharacterReferences(character.RuntimeId);
        return true;
    }

    public bool TryGetCharacter(string runtimeId, out ShelterMemberRuntimeData character)
    {
        character = null;
        if (string.IsNullOrWhiteSpace(runtimeId))
            return false;

        string id = runtimeId.Trim();
        character = characters.Find(item => item != null && item.RuntimeId == id);
        if (character != null)
            return true;

        // schemaVersion 6 이하 정의 ID 기반 호출을 읽는 동안만 사용하는 호환 조회입니다.
        character = characters.Find(item => item != null && item.DefinitionId == id);
        return character != null;
    }

    public FacilityRuntimeState GetOrCreateFacilityState(string facilityId, bool isUnlockedByDefault)
    {
        EnsureRuntimeContainers();
        string id = string.IsNullOrWhiteSpace(facilityId) ? string.Empty : facilityId.Trim();
        if (string.IsNullOrEmpty(id))
            return null;

        foreach (FacilityRuntimeState state in facilityStates)
        {
            if (state == null)
                continue;
            state.EnsureValid();
            if (state.facilityId == id)
                return state;
        }

        FacilityRuntimeState created = new FacilityRuntimeState(id, isUnlockedByDefault);
        facilityStates.Add(created);
        return created;
    }

    public bool TrySetFieldSquad(IEnumerable<string> runtimeIds)
    {
        if (runtimeIds == null)
            return false;

        List<string> normalized = new List<string>();
        foreach (string runtimeId in runtimeIds)
        {
            if (string.IsNullOrWhiteSpace(runtimeId))
                continue;

            string id = ResolveRuntimeId(runtimeId);
            if (string.IsNullOrEmpty(id) || normalized.Contains(id))
                continue;

            normalized.Add(id);
            if (normalized.Count > MaxFieldSquadSize)
                return false;
        }

        if (normalized.Count < MinFieldSquadSize)
            return false;

        fieldSquadRuntimeIds = normalized;
        return true;
    }

    public bool TryAddFieldSquadCharacter(string runtimeId)
    {
        string id = ResolveRuntimeId(runtimeId);
        if (string.IsNullOrEmpty(id))
            return false;
        if (fieldSquadRuntimeIds.Contains(id))
            return true;
        if (fieldSquadRuntimeIds.Count >= MaxFieldSquadSize)
            return false;

        fieldSquadRuntimeIds.Add(id);
        return true;
    }

    public bool TryRemoveFieldSquadCharacter(string runtimeId)
    {
        if (fieldSquadRuntimeIds.Count <= MinFieldSquadSize)
            return false;

        string id = ResolveRuntimeId(runtimeId);
        return !string.IsNullOrEmpty(id) && fieldSquadRuntimeIds.Remove(id);
    }

    public void ClearFieldSquad() => fieldSquadRuntimeIds.Clear();

    public void RemoveCharacterReferences(string runtimeId)
    {
        string id = ResolveRuntimeId(runtimeId);
        if (!string.IsNullOrEmpty(id))
            fieldSquadRuntimeIds.RemoveAll(value => value == id);
    }

    private string ResolveRuntimeId(string id)
    {
        return TryGetCharacter(id, out ShelterMemberRuntimeData character)
            ? character.RuntimeId
            : id?.Trim() ?? string.Empty;
    }

    private void NormalizeCharacters()
    {
        HashSet<string> runtimeIds = new HashSet<string>();
        for (int i = characters.Count - 1; i >= 0; i--)
        {
            ShelterMemberRuntimeData character = characters[i];
            if (character == null
                || string.IsNullOrWhiteSpace(character.RuntimeId)
                || !runtimeIds.Add(character.RuntimeId))
            {
                characters.RemoveAt(i);
            }
        }
    }

    private void NormalizeFieldSquad()
    {
        List<string> normalized = new List<string>();
        foreach (string value in fieldSquadRuntimeIds)
        {
            string id = ResolveRuntimeId(value);
            if (!string.IsNullOrEmpty(id) && !normalized.Contains(id))
                normalized.Add(id);
            if (normalized.Count == MaxFieldSquadSize)
                break;
        }
        fieldSquadRuntimeIds = normalized;
    }

    private static List<ShelterMemberRuntimeData> CloneCharacters(IEnumerable<ShelterMemberRuntimeData> source)
    {
        List<ShelterMemberRuntimeData> clone = new List<ShelterMemberRuntimeData>();
        if (source == null)
            return clone;
        foreach (ShelterMemberRuntimeData character in source)
        {
            if (character != null)
                clone.Add(character.Clone());
        }
        return clone;
    }

    private void NormalizeItemStorageEntries()
    {
        List<ItemStorageEntry> normalized = new List<ItemStorageEntry>();
        Dictionary<string, int> entryIndexes = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (ItemStorageEntry entry in itemStorageEntries)
        {
            if (entry == null)
                continue;

            entry.EnsureValid();
            if (!entry.IsValid)
                continue;

            if (!entryIndexes.TryGetValue(entry.ItemDefinitionId, out int existingIndex))
            {
                entryIndexes.Add(entry.ItemDefinitionId, normalized.Count);
                normalized.Add(entry.Clone());
                continue;
            }

            long combinedQuantity = (long)normalized[existingIndex].Quantity + entry.Quantity;
            normalized[existingIndex] = new ItemStorageEntry(
                entry.ItemDefinitionId,
                combinedQuantity > int.MaxValue ? int.MaxValue : (int)combinedQuantity);
        }

        itemStorageEntries.Clear();
        itemStorageEntries.AddRange(normalized);
    }

    private static List<ItemStorageEntry> CloneItemStorageEntries(IEnumerable<ItemStorageEntry> source)
    {
        List<ItemStorageEntry> clone = new List<ItemStorageEntry>();
        if (source == null)
            return clone;

        foreach (ItemStorageEntry entry in source)
        {
            if (entry != null && entry.IsValid)
                clone.Add(entry.Clone());
        }

        return clone;
    }

    private static void CopyItemStorageEntries(
        IEnumerable<ItemStorageEntry> source,
        List<ItemStorageEntry> destination)
    {
        List<ItemStorageEntry> clone = CloneItemStorageEntries(source);
        destination.Clear();
        destination.AddRange(clone);
    }

    private static List<FacilityRuntimeState> CloneFacilityStates(IEnumerable<FacilityRuntimeState> source)
    {
        List<FacilityRuntimeState> clone = new List<FacilityRuntimeState>();
        if (source == null)
            return clone;
        foreach (FacilityRuntimeState state in source)
        {
            if (state == null)
                continue;
            state.EnsureValid();
            clone.Add(new FacilityRuntimeState(state.facilityId, state.isUnlocked, state.upgradeLevel));
        }
        return clone;
    }
}
