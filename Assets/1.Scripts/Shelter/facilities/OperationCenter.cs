using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 셸터에서 출격 후보를 조회하고, 세 명의 임시 선택과 출격 확정 흐름을 관리합니다.
/// 선택 세션은 출격 버튼을 누르기 전까지 셸터 런타임 데이터에 기록하지 않습니다.
/// </summary>
public sealed class OperationCenter : MonoBehaviour
{
    [Header("Data Sources")]
    [SerializeField] private CharacterManager m_characterManager;
    [SerializeField] private ShelterSceneDataManager m_shelterSceneDataManager;

    [Header("Scene Transition")]
    [SerializeField] private string m_fieldSceneName = "CombatPlayTest 1";

    private readonly List<string> m_pendingSquadRuntimeIds = new();
    private readonly List<string> m_confirmedSquadRuntimeIds = new();
    private CharacterManager m_boundCharacterManager;

    public IReadOnlyList<string> PendingSquadRuntimeIds => m_pendingSquadRuntimeIds;
    public IReadOnlyList<string> ConfirmedSquadRuntimeIds => m_confirmedSquadRuntimeIds;
    public int PendingCount => m_pendingSquadRuntimeIds.Count;
    public bool HasConfirmedSquad =>
        m_confirmedSquadRuntimeIds.Count == ShelterRuntimeData.MaxFieldSquadSize;

    public event Action SelectionChanged;

    private void Awake()
    {
        CacheReferences();
    }

    private void OnEnable()
    {
        BindCharacterManager(CacheCharacterManager());
    }

    private void OnDisable()
    {
        UnbindCharacterManager();
        CancelSelectionSession();
    }

    private void Reset()
    {
        CacheReferences();
    }

    /// <summary>OperationUI를 열 때 저장되지 않는 새 선택 세션을 시작합니다.</summary>
    public void BeginSelectionSession()
    {
        ClearSelection();
        SelectionChanged?.Invoke();
    }

    /// <summary>현재 선택 세션을 폐기합니다. 셸터 및 GameData 정본은 변경하지 않습니다.</summary>
    public void CancelSelectionSession()
    {
        if (m_pendingSquadRuntimeIds.Count == 0 && m_confirmedSquadRuntimeIds.Count == 0)
            return;

        ClearSelection();
        SelectionChanged?.Invoke();
    }

    /// <summary>정상·경상이며 생존 중인 캐릭터만 출격 후보 버퍼에 채웁니다.</summary>
    public void FillDeploymentCandidates(List<ShelterMemberRuntimeData> results)
    {
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        results.Clear();
        CharacterManager manager = CacheCharacterManager();
        if (manager == null)
            return;

        IReadOnlyList<ShelterMemberRuntimeData> characters = manager.Characters;
        for (int i = 0; i < characters.Count; i++)
        {
            ShelterMemberRuntimeData character = characters[i];
            if (CanDeployCharacter(character))
                results.Add(character);
        }
    }

    /// <summary>캐릭터를 임시 명단에 추가하거나, 이미 선택되어 있으면 제거합니다.</summary>
    public bool TryTogglePendingCharacter(string runtimeId)
    {
        string id = NormalizeId(runtimeId);
        if (string.IsNullOrEmpty(id))
            return false;

        int existingIndex = m_pendingSquadRuntimeIds.IndexOf(id);
        if (existingIndex >= 0)
        {
            m_pendingSquadRuntimeIds.RemoveAt(existingIndex);
            InvalidateConfirmation();
            SelectionChanged?.Invoke();
            return true;
        }

        CharacterManager manager = CacheCharacterManager();
        if (manager == null
            || !manager.TryGetCharacter(id, out ShelterMemberRuntimeData character)
            || !CanDeployCharacter(character))
            return false;

        if (m_pendingSquadRuntimeIds.Count >= ShelterRuntimeData.MaxFieldSquadSize)
            return false;

        m_pendingSquadRuntimeIds.Add(id);
        InvalidateConfirmation();
        SelectionChanged?.Invoke();
        return true;
    }

    /// <summary>지정 슬롯의 임시 선택을 제거하고 뒤 슬롯을 앞으로 당깁니다.</summary>
    public bool TryRemovePendingAt(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= m_pendingSquadRuntimeIds.Count)
            return false;

        m_pendingSquadRuntimeIds.RemoveAt(slotIndex);
        InvalidateConfirmation();
        SelectionChanged?.Invoke();
        return true;
    }

    /// <summary>정확히 세 명인 임시 선택을 현재 UI 세션의 출격 명단으로 확정합니다.</summary>
    public bool TryConfirmSelection()
    {
        if (!ValidateSquad(m_pendingSquadRuntimeIds))
            return false;

        m_confirmedSquadRuntimeIds.Clear();
        m_confirmedSquadRuntimeIds.AddRange(m_pendingSquadRuntimeIds);
        SelectionChanged?.Invoke();
        return true;
    }

    /// <summary>확정 명단을 셸터에 반영·동기화한 뒤 필드 씬으로 이동합니다.</summary>
    public bool TryDeploy()
    {
        if (string.IsNullOrWhiteSpace(m_fieldSceneName))
        {
            Debug.LogWarning("[OperationCenter] Field scene name is empty.", this);
            return false;
        }

        string fieldSceneName = m_fieldSceneName.Trim();
        if (!Application.CanStreamedLevelBeLoaded(fieldSceneName))
        {
            Debug.LogWarning(
                $"[OperationCenter] Field scene is not available in Build Settings: {fieldSceneName}",
                this);
            return false;
        }

        if (!ValidateSquad(m_confirmedSquadRuntimeIds))
        {
            Debug.LogWarning("[OperationCenter] Exactly three deployable characters are required.", this);
            return false;
        }

        ShelterSceneDataManager shelterData = CacheShelterSceneDataManager();
        if (shelterData == null)
        {
            Debug.LogWarning("[OperationCenter] ShelterSceneDataManager is not available.", this);
            return false;
        }

        List<string> previousSquad = new(shelterData.FieldSquadRuntimeIds);
        if (!shelterData.TrySetFieldSquad(m_confirmedSquadRuntimeIds))
        {
            Debug.LogWarning("[OperationCenter] Failed to apply the confirmed field squad.", this);
            return false;
        }

        if (!shelterData.PushToDataManager())
        {
            RestorePreviousSquad(shelterData, previousSquad);
            Debug.LogWarning("[OperationCenter] Failed to synchronize shelter data before deployment.", this);
            return false;
        }

        SceneTransitionController.LoadScene(fieldSceneName);
        return true;
    }

    public bool TryGetCharacter(string runtimeId, out ShelterMemberRuntimeData character)
    {
        character = null;
        CharacterManager manager = CacheCharacterManager();
        return manager != null && manager.TryGetCharacter(runtimeId, out character);
    }

    private bool ValidateSquad(IReadOnlyList<string> runtimeIds)
    {
        if (runtimeIds == null || runtimeIds.Count != ShelterRuntimeData.MaxFieldSquadSize)
            return false;

        CharacterManager manager = CacheCharacterManager();
        if (manager == null)
            return false;

        HashSet<string> uniqueIds = new(StringComparer.Ordinal);
        for (int i = 0; i < runtimeIds.Count; i++)
        {
            string id = NormalizeId(runtimeIds[i]);
            if (string.IsNullOrEmpty(id)
                || !uniqueIds.Add(id)
                || !manager.TryGetCharacter(id, out ShelterMemberRuntimeData character)
                || !CanDeployCharacter(character))
            {
                return false;
            }
        }

        return true;
    }

    private void RevalidateSelection()
    {
        CharacterManager manager = CacheCharacterManager();
        bool changed = false;

        for (int i = m_pendingSquadRuntimeIds.Count - 1; i >= 0; i--)
        {
            if (manager == null
                || !manager.TryGetCharacter(
                    m_pendingSquadRuntimeIds[i],
                    out ShelterMemberRuntimeData character)
                || !CanDeployCharacter(character))
            {
                m_pendingSquadRuntimeIds.RemoveAt(i);
                changed = true;
            }
        }

        if (!ValidateSquad(m_confirmedSquadRuntimeIds))
        {
            changed |= m_confirmedSquadRuntimeIds.Count > 0;
            m_confirmedSquadRuntimeIds.Clear();
        }

        if (changed)
            SelectionChanged?.Invoke();
    }

    private void HandleCharactersChanged()
    {
        RevalidateSelection();
    }

    private void InvalidateConfirmation()
    {
        m_confirmedSquadRuntimeIds.Clear();
    }

    private void ClearSelection()
    {
        m_pendingSquadRuntimeIds.Clear();
        m_confirmedSquadRuntimeIds.Clear();
    }

    private static void RestorePreviousSquad(
        ShelterSceneDataManager shelterData,
        IReadOnlyList<string> previousSquad)
    {
        if (previousSquad != null && previousSquad.Count > 0)
            shelterData.TrySetFieldSquad(previousSquad);
        else
            shelterData.ClearFieldSquad();
    }

    private CharacterManager CacheCharacterManager()
    {
        if (m_characterManager == null)
            m_characterManager = CharacterManager.Instance;

        if (m_characterManager == null)
            m_characterManager = FindFirstObjectByType<CharacterManager>();

        return m_characterManager;
    }

    private ShelterSceneDataManager CacheShelterSceneDataManager()
    {
        if (m_shelterSceneDataManager == null)
            m_shelterSceneDataManager = ShelterSceneDataManager.Instance;

        if (m_shelterSceneDataManager == null)
            m_shelterSceneDataManager = FindFirstObjectByType<ShelterSceneDataManager>();

        return m_shelterSceneDataManager;
    }

    private void CacheReferences()
    {
        CacheCharacterManager();
        CacheShelterSceneDataManager();
    }

    private void BindCharacterManager(CharacterManager manager)
    {
        if (m_boundCharacterManager == manager)
            return;

        UnbindCharacterManager();
        m_boundCharacterManager = manager;
        if (m_boundCharacterManager != null)
            m_boundCharacterManager.CharactersChanged += HandleCharactersChanged;
    }

    private void UnbindCharacterManager()
    {
        if (m_boundCharacterManager != null)
            m_boundCharacterManager.CharactersChanged -= HandleCharactersChanged;

        m_boundCharacterManager = null;
    }

    private static string NormalizeId(string runtimeId)
    {
        return runtimeId?.Trim() ?? string.Empty;
    }

    private static bool CanDeployCharacter(ShelterMemberRuntimeData character)
    {
        if (character == null || character.IsDead || character.IsDown)
            return false;

        return character.InjuryState == CharacterInjuryState.Normal
            || character.InjuryState == CharacterInjuryState.Minor;
    }
}
