#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// F12 development trainer for shelter facility levels, base resources,
/// and owned-character injury gauges.
/// </summary>
[DefaultExecutionOrder(10000)]
public sealed class ShelterTrainerOverlay : MonoBehaviour
{
    private const int WindowId = 0x534854;
    private const float WindowWidth = 760.0f;
    private const float MinimumWindowHeight = 420.0f;
    private const float MaximumWindowHeight = 820.0f;

    private static readonly string[] OperationsRoomLevelRootNames =
    {
        "Operations_Room_Base",
        "Operations_Room_1",
        "Operations_Room_2",
        "Operations_Room_3",
    };

    private static ShelterTrainerOverlay s_instance;

    private readonly Dictionary<string, string> m_resourceInputs =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> m_injuryInputs =
        new(StringComparer.Ordinal);
    private readonly GameObject[] m_operationsRoomLevelRoots =
        new GameObject[OperationsRoomLevelRootNames.Length];

    private Rect m_windowRect;
    private Vector2 m_scrollPosition;
    private bool m_isOpen;
    private string m_statusMessage = string.Empty;

    private PlayerMove m_playerMove;
    private CameraLook m_cameraLook;
    private UIManager m_uiManager;
    private bool m_previousMoveLocked;
    private bool m_previousLookLocked;
    private bool m_wasUiBlockingOnOpen;
    private CursorLockMode m_previousCursorLockMode;
    private bool m_previousCursorVisible;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureCreated()
    {
        if (s_instance != null)
            return;

        GameObject host = new("[Debug] Shelter Trainer");
        host.hideFlags = HideFlags.DontSave;
        DontDestroyOnLoad(host);
        host.AddComponent<ShelterTrainerOverlay>();
    }

    private void Awake()
    {
        if (s_instance != null && s_instance != this)
        {
            Destroy(gameObject);
            return;
        }

        s_instance = this;
        ResetWindowRect();
    }

    private void OnDestroy()
    {
        if (m_isOpen)
            ApplyControlLock(false);

        if (s_instance == this)
            s_instance = null;
    }

    private void Update()
    {
        if (WasTogglePressed())
            SetOpen(!m_isOpen);
    }

    private void OnGUI()
    {
        if (!m_isOpen)
            return;

        float targetHeight = Mathf.Clamp(
            Screen.height - 60.0f,
            MinimumWindowHeight,
            MaximumWindowHeight);
        m_windowRect.height = targetHeight;
        m_windowRect.x = Mathf.Clamp(
            m_windowRect.x,
            0.0f,
            Mathf.Max(0.0f, Screen.width - m_windowRect.width));
        m_windowRect.y = Mathf.Clamp(
            m_windowRect.y,
            0.0f,
            Mathf.Max(0.0f, Screen.height - m_windowRect.height));

        GUI.depth = -1000;
        m_windowRect = GUI.Window(
            WindowId,
            m_windowRect,
            DrawWindow,
            "Shelter Trainer (F12)");
    }

    private void DrawWindow(int windowId)
    {
        GUILayout.BeginVertical();

        GUILayout.BeginHorizontal();
        GUILayout.Label(
            "Editor / Development Build only",
            GUILayout.ExpandWidth(true));
        if (GUILayout.Button("Close", GUILayout.Width(80.0f)))
        {
            SetOpen(false);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            return;
        }
        GUILayout.EndHorizontal();

        m_scrollPosition = GUILayout.BeginScrollView(m_scrollPosition);
        DrawFacilitySection();
        GUILayout.Space(12.0f);
        DrawResourceSection();
        GUILayout.Space(12.0f);
        DrawCharacterSection();
        GUILayout.EndScrollView();

        if (!string.IsNullOrWhiteSpace(m_statusMessage))
            GUILayout.Label(m_statusMessage);

        GUILayout.EndVertical();
        GUI.DragWindow(new Rect(0.0f, 0.0f, WindowWidth, 24.0f));
    }

    private void DrawFacilitySection()
    {
        GUILayout.Label("1. Facility Level");

        FacilityManager manager = FacilityManager.Instance;
        if (manager == null)
        {
            GUILayout.Label("FacilityManager is not available in this scene.");
        }
        else
        {
            foreach (KeyValuePair<string, FacilityState> pair in manager.States)
            {
                FacilityState state = pair.Value;
                if (state == null)
                    continue;

                string facilityName = state.Definition != null
                    && !string.IsNullOrWhiteSpace(state.Definition.FacilityName)
                    ? state.Definition.FacilityName
                    : pair.Key;
                int maxLevelIndex =
                    manager.GetMaxUpgradeLevelForDebug(pair.Key);

                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    $"{facilityName} ({pair.Key})  Current: "
                    + $"{(state.IsUnlocked ? $"Lv.{state.UpgradeLevel + 1}" : "Locked")}",
                    GUILayout.Width(360.0f));

                if (maxLevelIndex < 0)
                {
                    GUILayout.Label("Facility instance is not registered.");
                    GUILayout.EndHorizontal();
                    continue;
                }

                for (int levelIndex = 0;
                     levelIndex <= maxLevelIndex;
                     levelIndex++)
                {
                    Color previousColor = GUI.backgroundColor;
                    if (state.IsUnlocked
                        && state.UpgradeLevel == levelIndex)
                    {
                        GUI.backgroundColor = new Color(
                            0.45f,
                            0.9f,
                            0.5f);
                    }

                    if (GUILayout.Button(
                            $"Lv.{levelIndex + 1}",
                            GUILayout.Width(65.0f)))
                    {
                        bool changed = manager.TrySetUpgradeLevelForDebug(
                            pair.Key,
                            levelIndex);
                        m_statusMessage = changed
                            ? $"{facilityName} forced to Lv.{levelIndex + 1}."
                            : $"Failed to change {facilityName}.";
                    }

                    GUI.backgroundColor = previousColor;
                }

                GUILayout.EndHorizontal();
            }
        }

        GUILayout.Space(6.0f);
        DrawTemporaryOperationsRoomPreview();
    }

    private void DrawTemporaryOperationsRoomPreview()
    {
        GUILayout.Label(
            "[Temporary] Operations_Room visual preview "
            + "(not facility state)");

        if (!TryResolveOperationsRoomLevelRoots())
        {
            GUILayout.Label(
                "Operations_Room Base/1/2/3 roots were not found.");
            return;
        }

        int activeLevelIndex = GetOperationsRoomActiveLevelIndex();

        GUILayout.BeginHorizontal();
        GUILayout.Label(
            "Operations_Room  Current: "
            + (activeLevelIndex >= 0
                ? $"Lv.{activeLevelIndex + 1}"
                : "Mixed/None"),
            GUILayout.Width(360.0f));

        for (int levelIndex = 0;
             levelIndex < m_operationsRoomLevelRoots.Length;
             levelIndex++)
        {
            Color previousColor = GUI.backgroundColor;
            if (activeLevelIndex == levelIndex)
            {
                GUI.backgroundColor = new Color(
                    0.45f,
                    0.9f,
                    0.5f);
            }

            if (GUILayout.Button(
                    $"Lv.{levelIndex + 1}",
                    GUILayout.Width(65.0f)))
            {
                ShowOperationsRoomLevel(levelIndex);
                m_statusMessage =
                    $"Operations_Room preview set to "
                    + $"Lv.{levelIndex + 1}.";
            }

            GUI.backgroundColor = previousColor;
        }

        GUILayout.EndHorizontal();
    }

    private void DrawResourceSection()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(
            "2. Base Resources",
            GUILayout.ExpandWidth(true));
        if (GUILayout.Button(
                "Reload Values",
                GUILayout.Width(110.0f)))
        {
            ReloadResourceInputs();
        }
        GUILayout.EndHorizontal();

        StorageFacility storage = ShelterSceneDataManager.Instance?.Storage;
        if (storage == null)
        {
            GUILayout.Label("StorageFacility is not available in this scene.");
            return;
        }

        for (int i = 0; i < ResourceIds.All.Count; i++)
        {
            string resourceId = ResourceIds.All[i];
            int currentAmount = storage.GetResourceAmount(resourceId);
            if (!m_resourceInputs.ContainsKey(resourceId))
                m_resourceInputs[resourceId] = currentAmount.ToString();

            GUILayout.BeginHorizontal();
            GUILayout.Label(
                $"{resourceId}  Current: {currentAmount}",
                GUILayout.Width(390.0f));
            m_resourceInputs[resourceId] = GUILayout.TextField(
                m_resourceInputs[resourceId],
                GUILayout.Width(150.0f));

            if (GUILayout.Button("Set", GUILayout.Width(65.0f)))
            {
                if (TryParseNonNegative(
                        m_resourceInputs[resourceId],
                        out int amount))
                {
                    storage.SetResourceAmount(resourceId, amount);
                    m_resourceInputs[resourceId] = amount.ToString();
                    m_statusMessage =
                        $"{resourceId} set to {amount}.";
                }
                else
                {
                    m_statusMessage =
                        $"{resourceId}: enter an integer from 0 to {int.MaxValue}.";
                }
            }

            GUILayout.EndHorizontal();
        }
    }

    private void DrawCharacterSection()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(
            "3. Owned Character Injury",
            GUILayout.ExpandWidth(true));
        if (GUILayout.Button(
                "Reload Values",
                GUILayout.Width(110.0f)))
        {
            ReloadInjuryInputs();
        }
        GUILayout.EndHorizontal();

        CharacterManager manager = CharacterManager.Instance;
        if (manager == null)
        {
            GUILayout.Label("CharacterManager is not available in this scene.");
            return;
        }

        IReadOnlyList<ShelterMemberRuntimeData> characters =
            manager.Characters;
        if (characters.Count == 0)
        {
            GUILayout.Label("No owned characters.");
            return;
        }

        for (int i = 0; i < characters.Count; i++)
        {
            ShelterMemberRuntimeData character = characters[i];
            if (character == null
                || string.IsNullOrWhiteSpace(character.RuntimeId))
            {
                continue;
            }

            string runtimeId = character.RuntimeId;
            if (!m_injuryInputs.ContainsKey(runtimeId))
            {
                m_injuryInputs[runtimeId] =
                    character.InjuryGauge.ToString("0.##");
            }

            GUILayout.Label(
                $"{character.DisplayName} ({runtimeId})  "
                + $"{character.InjuryState}  "
                + $"{character.InjuryGauge:0.##}/{character.MaxInjuryGauge:0.##}");

            GUILayout.BeginHorizontal();
            float requestedGauge = GUILayout.HorizontalSlider(
                character.InjuryGauge,
                0.0f,
                character.MaxInjuryGauge,
                GUILayout.Width(430.0f));

            if (!Mathf.Approximately(
                    requestedGauge,
                    character.InjuryGauge))
            {
                ApplyInjuryGauge(
                    manager,
                    character,
                    requestedGauge);
            }

            m_injuryInputs[runtimeId] = GUILayout.TextField(
                m_injuryInputs[runtimeId],
                GUILayout.Width(120.0f));

            if (GUILayout.Button("Set", GUILayout.Width(65.0f)))
            {
                if (float.TryParse(
                        m_injuryInputs[runtimeId],
                        out float exactGauge))
                {
                    ApplyInjuryGauge(
                        manager,
                        character,
                        exactGauge);
                }
                else
                {
                    m_statusMessage =
                        $"{character.DisplayName}: enter a valid number.";
                }
            }

            GUILayout.EndHorizontal();
        }
    }

    private void ApplyInjuryGauge(
        CharacterManager manager,
        ShelterMemberRuntimeData character,
        float requestedGauge)
    {
        float clampedGauge = Mathf.Clamp(
            requestedGauge,
            0.0f,
            character.MaxInjuryGauge);
        bool changed = manager.TrySetInjuryGauge(
            character.RuntimeId,
            clampedGauge,
            out CharacterActionFailure failure);

        if (changed)
        {
            m_injuryInputs[character.RuntimeId] =
                clampedGauge.ToString("0.##");
            m_statusMessage =
                $"{character.DisplayName} injury set to "
                + $"{clampedGauge:0.##} ({character.InjuryState}).";
        }
        else if (failure != CharacterActionFailure.None)
        {
            m_statusMessage =
                $"Failed to change {character.DisplayName}: {failure}.";
        }
    }

    private void SetOpen(bool open)
    {
        if (m_isOpen == open)
            return;

        m_isOpen = open;
        m_statusMessage = string.Empty;

        if (m_isOpen)
        {
            ResetWindowRect();
            ReloadResourceInputs();
            ReloadInjuryInputs();
        }

        ApplyControlLock(m_isOpen);
    }

    private void ApplyControlLock(bool locked)
    {
        if (locked)
        {
            CacheControlTargets();
            m_previousMoveLocked =
                m_playerMove != null && m_playerMove.IsMoveLocked;
            m_previousLookLocked =
                m_cameraLook != null && m_cameraLook.IsLookLocked;
            m_wasUiBlockingOnOpen =
                m_uiManager != null && m_uiManager.HasOpenBlockingUI;
            m_previousCursorLockMode = Cursor.lockState;
            m_previousCursorVisible = Cursor.visible;

            m_playerMove?.SetMoveLocked(true);
            m_cameraLook?.SetLookLocked(true);
            if (m_cameraLook == null)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            return;
        }

        bool uiStillBlocking =
            m_uiManager != null && m_uiManager.HasOpenBlockingUI;
        bool restoreExternalMoveLock =
            m_previousMoveLocked && !m_wasUiBlockingOnOpen;
        bool restoreExternalLookLock =
            m_previousLookLocked && !m_wasUiBlockingOnOpen;

        m_playerMove?.SetMoveLocked(
            uiStillBlocking || restoreExternalMoveLock);
        m_cameraLook?.SetLookLocked(
            uiStillBlocking || restoreExternalLookLock);

        if (m_cameraLook == null)
        {
            Cursor.lockState = uiStillBlocking
                ? CursorLockMode.None
                : m_previousCursorLockMode;
            Cursor.visible = uiStillBlocking
                || m_previousCursorVisible;
        }
    }

    private void CacheControlTargets()
    {
        m_playerMove = FindFirstObjectByType<PlayerMove>();
        m_cameraLook = FindFirstObjectByType<CameraLook>();
        m_uiManager = FindFirstObjectByType<UIManager>();
    }

    private void ReloadResourceInputs()
    {
        m_resourceInputs.Clear();
        StorageFacility storage = ShelterSceneDataManager.Instance?.Storage;
        if (storage == null)
            return;

        for (int i = 0; i < ResourceIds.All.Count; i++)
        {
            string resourceId = ResourceIds.All[i];
            m_resourceInputs[resourceId] =
                storage.GetResourceAmount(resourceId).ToString();
        }
    }

    private void ReloadInjuryInputs()
    {
        m_injuryInputs.Clear();
        CharacterManager manager = CharacterManager.Instance;
        if (manager == null)
            return;

        IReadOnlyList<ShelterMemberRuntimeData> characters =
            manager.Characters;
        for (int i = 0; i < characters.Count; i++)
        {
            ShelterMemberRuntimeData character = characters[i];
            if (character == null
                || string.IsNullOrWhiteSpace(character.RuntimeId))
            {
                continue;
            }

            m_injuryInputs[character.RuntimeId] =
                character.InjuryGauge.ToString("0.##");
        }
    }

    private bool TryResolveOperationsRoomLevelRoots()
    {
        bool alreadyResolved = true;
        for (int i = 0; i < m_operationsRoomLevelRoots.Length; i++)
        {
            if (m_operationsRoomLevelRoots[i] == null)
            {
                alreadyResolved = false;
                break;
            }
        }

        if (alreadyResolved)
            return true;

        Array.Clear(
            m_operationsRoomLevelRoots,
            0,
            m_operationsRoomLevelRoots.Length);

        Transform[] transforms = FindObjectsByType<Transform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int transformIndex = 0;
             transformIndex < transforms.Length;
             transformIndex++)
        {
            Transform candidate = transforms[transformIndex];
            if (candidate == null
                || !string.Equals(
                    candidate.name,
                    "Operations_Room",
                    StringComparison.Ordinal))
            {
                continue;
            }

            bool foundAll = true;
            for (int levelIndex = 0;
                 levelIndex < OperationsRoomLevelRootNames.Length;
                 levelIndex++)
            {
                Transform levelRoot = candidate.Find(
                    OperationsRoomLevelRootNames[levelIndex]);
                if (levelRoot == null)
                {
                    foundAll = false;
                    break;
                }

                m_operationsRoomLevelRoots[levelIndex] =
                    levelRoot.gameObject;
            }

            if (foundAll)
                return true;

            Array.Clear(
                m_operationsRoomLevelRoots,
                0,
                m_operationsRoomLevelRoots.Length);
        }

        return false;
    }

    private int GetOperationsRoomActiveLevelIndex()
    {
        int activeLevelIndex = -1;
        for (int i = 0; i < m_operationsRoomLevelRoots.Length; i++)
        {
            if (!m_operationsRoomLevelRoots[i].activeSelf)
                continue;

            if (activeLevelIndex >= 0)
                return -1;

            activeLevelIndex = i;
        }

        return activeLevelIndex;
    }

    private void ShowOperationsRoomLevel(int levelIndex)
    {
        if (!TryResolveOperationsRoomLevelRoots())
            return;

        int targetLevel = Mathf.Clamp(
            levelIndex,
            0,
            m_operationsRoomLevelRoots.Length - 1);

        for (int i = 0; i < m_operationsRoomLevelRoots.Length; i++)
        {
            GameObject levelRoot = m_operationsRoomLevelRoots[i];
            if (levelRoot != null)
                levelRoot.SetActive(i == targetLevel);
        }
    }

    private void ResetWindowRect()
    {
        float height = Mathf.Clamp(
            Screen.height - 60.0f,
            MinimumWindowHeight,
            MaximumWindowHeight);
        m_windowRect = new Rect(
            Mathf.Max(20.0f, (Screen.width - WindowWidth) * 0.5f),
            30.0f,
            WindowWidth,
            height);
    }

    private static bool TryParseNonNegative(
        string value,
        out int amount)
    {
        if (int.TryParse(value, out amount) && amount >= 0)
            return true;

        amount = 0;
        return false;
    }

    private static bool WasTogglePressed()
    {
        // ShelterScene_Jung에서는 F12를 클리어 횟수 토글이 사용
        if (ShelterClearCountDebugToggle.IsTargetSceneActive)
            return false;

#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null
            && Keyboard.current.f12Key.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.F12);
#endif
    }
}
#endif
