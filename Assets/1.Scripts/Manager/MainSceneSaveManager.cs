using System.Collections.Generic;
using UnityEngine;

public class MainSceneSaveManager : MonoBehaviour
{
    public static MainSceneSaveManager Instance { get; private set; }

    [Header("New Game Defaults")]
    [SerializeField] private string defaultProfileId = SaveFilePaths.DefaultProfileId;
    [SerializeField] private int newGameStartDay = 1;
    [SerializeField] private string newGameStartStageId = string.Empty;
    [SerializeField] private NPCChar[] startingNpcChars;
    [SerializeField] private FacilityDefinition[] startingFacilityDefinitions;

    private void Awake()
    {
        if (TryRejectDuplicate())
        {
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        // Temporary test hook. Remove when the main menu flow calls this explicitly.
        if (!StartNewGame())
        {
            return;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public bool StartNewGame()
    {
        if (GameDataManager.Instance == null)
        {
            Debug.LogWarning("[MainSceneSaveManager] GameDataManager.Instance is null.");
            return false;
        }

        SaveData newGameSaveData = CreateNewGameSaveData();
        GameDataManager.Instance.ApplySaveData(newGameSaveData);

        // Temporary test-scene sync. Scene managers should copy from GameDataManager on scene load later.
        if (ShelterDataManager.Instance != null)
        {
            ShelterDataManager.Instance.CopyFromDataManager();
        }

        return true;
    }

    public void OnStartNewGameButtonClicked()
    {
        StartNewGame();
    }

    public SaveData CreateNewGameSaveData()
    {
        SaveData saveData = new SaveData
        {
            schemaVersion = SaveData.CurrentSchemaVersion,
            profileId = ResolveProfileId(defaultProfileId),
            shared = new SaveData.SharedSaveData
            {
                lastStageId = newGameStartStageId ?? string.Empty
            },
            shelter = new SaveData.ShelterSaveData
            {
                currentDay = Mathf.Max(1, newGameStartDay)
            }
        };

        AddStartingNpcs(saveData.shared);
        AddStartingFacilities(saveData.shelter);
        return saveData;
    }

    private void AddStartingNpcs(SaveData.SharedSaveData sharedSaveData)
    {
        if (sharedSaveData == null || startingNpcChars == null)
        {
            return;
        }

        for (int i = 0; i < startingNpcChars.Length; i++)
        {
            SaveData.NpcSaveData npcSaveData = NpcSaveDataMapper.FromNpcChar(startingNpcChars[i]);
            if (npcSaveData == null)
            {
                continue;
            }

            sharedSaveData.npcs.Add(npcSaveData);
        }

        sharedSaveData.playableCharacterCount = sharedSaveData.npcs.Count;
        sharedSaveData.nonPlayableNpcCount = 0;
    }

    private void AddStartingFacilities(SaveData.ShelterSaveData shelterSaveData)
    {
        if (shelterSaveData == null || startingFacilityDefinitions == null)
        {
            return;
        }

        HashSet<string> addedFacilityIds = new HashSet<string>();
        foreach (FacilityDefinition definition in startingFacilityDefinitions)
        {
            SaveData.FacilitySaveData facilitySaveData = FacilitySaveDataMapper.FromDefinition(definition);
            if (facilitySaveData == null)
            {
                continue;
            }

            if (!addedFacilityIds.Add(facilitySaveData.facilityId))
            {
                continue;
            }

            shelterSaveData.facilities.Add(facilitySaveData);
        }
    }

    private string ResolveProfileId(string profileId)
    {
        return string.IsNullOrWhiteSpace(profileId) ? SaveFilePaths.DefaultProfileId : profileId;
    }

    private bool TryRejectDuplicate()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return true;
        }

        return false;
    }
}
