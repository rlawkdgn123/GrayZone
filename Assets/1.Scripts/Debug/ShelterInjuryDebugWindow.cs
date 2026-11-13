using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 임시 디버그 창입니다. F11을 누르면 현재 셸터에 있는 캐릭터 목록을 읽어 부상 상태(부상 게이지)를 바꿀 수 있습니다.
/// </summary>
/// <remarks>
/// 의료시설 슬롯의 부상 상태 텍스트·색, 치료 대상 등록을 셸터 안에서 바로 확인하기 위한 것입니다.
/// 상태 버튼은 <see cref="CharacterInjuryStateRule"/>의 구간(10·40·70%) 안쪽 값으로 게이지를 설정합니다.
/// 의료시설 치료 후보는 HP 감소 기준(<see cref="MedicalManager.CanAssignPatient"/>)이므로,
/// 게이지를 바꿀 때 결과 부상 상태에 맞춰 HP도 함께 맞춥니다(건강이면 HP 최대).
/// CharacterManager가 있는 씬에서만 열리며, 에디터와 개발 빌드에서만 동작합니다.
/// </remarks>
public sealed class ShelterInjuryDebugWindow : MonoBehaviour
{
    private const float WindowWidth = 560f;
    private const float WindowHeight = 480f;
    private const int WindowId = 0x1A11;

    // 상태별 게이지 비율(최대 게이지 대비, 각 상태 구간의 안쪽 값)과 함께 맞출 HP 비율(최대 HP 대비).
    private static readonly (CharacterInjuryState state, float gaugeRatio, float hpRatio)[] s_statePresets =
    {
        (CharacterInjuryState.Normal, 0.0f, 1.0f),
        (CharacterInjuryState.Minor, 0.25f, 0.75f),
        (CharacterInjuryState.Serious, 0.55f, 0.45f),
        (CharacterInjuryState.Critical, 0.85f, 0.15f),
    };

    private static ShelterInjuryDebugWindow s_instance;

    private bool m_isOpen;
    private Rect m_windowRect;
    private Vector2 m_scroll;
    private string m_statusMessage = string.Empty;

    private PlayerMove m_playerMove;
    private CameraLook m_cameraLook;
    private bool m_previousMoveLocked;
    private bool m_previousLookLocked;
    private UIManager m_uiManager;
    private bool m_wasUiBlockingOnOpen;
    private CursorLockMode m_previousCursorLockMode;
    private bool m_previousCursorVisible;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        s_instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (s_instance != null || !Debug.isDebugBuild)
            return;

        GameObject holder = new GameObject("[Debug] Shelter Injury Debug Window");
        holder.hideFlags = HideFlags.DontSave;
        holder.AddComponent<ShelterInjuryDebugWindow>();
        DontDestroyOnLoad(holder);
    }

    private void Awake()
    {
        if (s_instance != null && s_instance != this)
        {
            Destroy(gameObject);
            return;
        }

        s_instance = this;
    }

    private void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;
    }

    private void Update()
    {
        // 씬 전환 등으로 CharacterManager가 사라지면 창을 닫고 조작 잠금을 되돌린다.
        if (m_isOpen && CharacterManager.Instance == null)
        {
            SetOpen(false);
            return;
        }

        if (Keyboard.current == null || !Keyboard.current.f11Key.wasPressedThisFrame)
            return;

        if (!m_isOpen && CharacterManager.Instance == null)
        {
            Debug.Log("[ShelterInjuryDebugWindow] CharacterManager가 없는 씬이라 열 수 없습니다.", this);
            return;
        }

        SetOpen(!m_isOpen);
    }

    private void OnGUI()
    {
        if (!m_isOpen)
            return;

        m_windowRect = GUI.Window(WindowId, m_windowRect, DrawWindow, "부상 상태 디버그 (F11 닫기)");
    }

    private void DrawWindow(int id)
    {
        CharacterManager manager = CharacterManager.Instance;
        if (manager == null)
        {
            GUILayout.Label("CharacterManager가 없습니다.");
            GUI.DragWindow();
            return;
        }

        IReadOnlyList<ShelterMemberRuntimeData> characters = manager.Characters;
        GUILayout.Label($"셸터 캐릭터 {characters.Count}명");

        if (GUILayout.Button("전원 건강으로"))
            ApplyToAll(manager, characters, 0.0f);

        m_scroll = GUILayout.BeginScrollView(m_scroll);
        for (int i = 0; i < characters.Count; i++)
        {
            ShelterMemberRuntimeData character = characters[i];
            if (character == null || string.IsNullOrWhiteSpace(character.RuntimeId))
                continue;

            DrawCharacterRow(manager, character);
        }
        GUILayout.EndScrollView();

        if (!string.IsNullOrEmpty(m_statusMessage))
            GUILayout.Label(m_statusMessage);

        GUI.DragWindow();
    }

    private void DrawCharacterRow(CharacterManager manager, ShelterMemberRuntimeData character)
    {
        GUILayout.BeginVertical(GUI.skin.box);

        GUILayout.Label(
            $"{character.DisplayName}  [{CharacterInjuryStateText.ToWord(character.InjuryState)}]  "
            + $"게이지 {character.InjuryGauge:0.#}/{character.MaxInjuryGauge:0.#}  "
            + $"HP {character.CurrentHp}/{character.MaxHp}");

        GUILayout.BeginHorizontal();
        foreach ((CharacterInjuryState state, float gaugeRatio, float _) in s_statePresets)
        {
            bool isCurrent = character.InjuryState == state;
            GUI.enabled = !isCurrent;
            if (GUILayout.Button(CharacterInjuryStateText.ToWord(state)))
                ApplyGauge(manager, character, character.MaxInjuryGauge * gaugeRatio);
            GUI.enabled = true;
        }
        GUILayout.EndHorizontal();

        float requested = GUILayout.HorizontalSlider(character.InjuryGauge, 0.0f, character.MaxInjuryGauge);
        if (!Mathf.Approximately(requested, character.InjuryGauge))
            ApplyGauge(manager, character, requested);

        GUILayout.EndVertical();
    }

    private void ApplyToAll(CharacterManager manager, IReadOnlyList<ShelterMemberRuntimeData> characters, float ratio)
    {
        // 게이지 변경이 CharactersChanged를 발생시켜 목록이 바뀔 수 있으므로 복사본으로 순회한다.
        List<ShelterMemberRuntimeData> snapshot = new List<ShelterMemberRuntimeData>(characters);
        foreach (ShelterMemberRuntimeData character in snapshot)
        {
            if (character != null && !string.IsNullOrWhiteSpace(character.RuntimeId))
                ApplyGauge(manager, character, character.MaxInjuryGauge * ratio);
        }
    }

    private void ApplyGauge(CharacterManager manager, ShelterMemberRuntimeData character, float gauge)
    {
        float clamped = Mathf.Clamp(gauge, 0.0f, character.MaxInjuryGauge);
        if (!manager.TrySetInjuryGauge(character.RuntimeId, clamped, out CharacterActionFailure failure))
        {
            m_statusMessage = $"{character.DisplayName} 변경 실패: {failure}";
            return;
        }

        // 치료 후보는 HP 감소로 판정되므로, 바뀐 부상 상태에 맞춰 HP도 맞춘다.
        int targetHp = GetPresetHp(character);
        if (!manager.TrySetCurrentHp(character.RuntimeId, targetHp, out failure))
        {
            m_statusMessage = $"{character.DisplayName} HP 변경 실패: {failure}";
            return;
        }

        m_statusMessage =
            $"{character.DisplayName}: 게이지 {clamped:0.#} → {CharacterInjuryStateText.ToWord(character.InjuryState)}, "
            + $"HP {character.CurrentHp}/{character.MaxHp}";
    }

    // 현재 부상 상태의 프리셋 HP. 부상 상태면 최소 1, 최대 HP-1로 맞춰 사망 처리나 '감소 없음'이 되지 않게 한다.
    private static int GetPresetHp(ShelterMemberRuntimeData character)
    {
        int maxHp = character.MaxHp;
        foreach ((CharacterInjuryState state, float _, float hpRatio) in s_statePresets)
        {
            if (state != character.InjuryState)
                continue;

            if (state == CharacterInjuryState.Normal)
                return maxHp;

            int hp = Mathf.RoundToInt(maxHp * hpRatio);
            return Mathf.Clamp(hp, 1, Mathf.Max(1, maxHp - 1));
        }

        return maxHp;
    }

    private void SetOpen(bool open)
    {
        if (m_isOpen == open)
            return;

        m_isOpen = open;
        m_statusMessage = string.Empty;

        if (open)
        {
            m_windowRect = new Rect(
                (Screen.width - WindowWidth) * 0.5f,
                (Screen.height - WindowHeight) * 0.5f,
                WindowWidth,
                WindowHeight);
        }

        ApplyControlLock(open);
    }

    // 창이 열린 동안 이동·시점 회전을 막고 커서를 보이게 한다. 닫으면 열기 전 상태로 되돌린다.
    private void ApplyControlLock(bool locked)
    {
        if (locked)
        {
            m_playerMove = FindFirstObjectByType<PlayerMove>();
            m_cameraLook = FindFirstObjectByType<CameraLook>();
            m_previousMoveLocked = m_playerMove != null && m_playerMove.IsMoveLocked;
            m_previousLookLocked = m_cameraLook != null && m_cameraLook.IsLookLocked;
            m_uiManager = FindFirstObjectByType<UIManager>();
            m_wasUiBlockingOnOpen = m_uiManager != null && m_uiManager.HasOpenBlockingUI;
            m_previousCursorLockMode = Cursor.lockState;
            m_previousCursorVisible = Cursor.visible;

            if (m_playerMove != null)
                m_playerMove.SetMoveLocked(true);
            // CameraLook이 있으면 시점 잠금이 커서도 함께 관리하므로 직접 건드리지 않는다.
            if (m_cameraLook != null)
            {
                m_cameraLook.SetLookLocked(true);
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            return;
        }

        // 창이 열린 사이 의료 UI 등이 열렸다면 그 UI가 잠금을 유지해야 하므로 풀지 않는다.
        bool uiStillBlocking = m_uiManager != null && m_uiManager.HasOpenBlockingUI;
        bool restoreMoveLock = m_previousMoveLocked && !m_wasUiBlockingOnOpen;
        bool restoreLookLock = m_previousLookLocked && !m_wasUiBlockingOnOpen;

        if (m_playerMove != null)
            m_playerMove.SetMoveLocked(uiStillBlocking || restoreMoveLock);

        if (m_cameraLook != null)
        {
            m_cameraLook.SetLookLocked(uiStillBlocking || restoreLookLock);
        }
        else
        {
            Cursor.lockState = uiStillBlocking ? CursorLockMode.None : m_previousCursorLockMode;
            Cursor.visible = uiStillBlocking || m_previousCursorVisible;
        }
    }
}
