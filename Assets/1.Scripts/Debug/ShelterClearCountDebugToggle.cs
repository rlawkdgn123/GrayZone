using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// ShelterScene_Jung 전용 디버그 키입니다. F12를 누르면 방어전 클리어 횟수를 1 올리고, 다시 누르면 원래 값으로 되돌립니다.
/// </summary>
/// <remarks>
/// 클리어 횟수로 갈리는 NPC 말풍선 대사 등을 셸터 안에서 바로 확인하기 위한 것입니다.
/// 이 씬에서는 F12 셸터 트레이너(<see cref="ShelterTrainerOverlay"/>)와 F10 조준선 인스펙터(<see cref="CrosshairDebugInspector"/>)가 열리지 않습니다.
/// 에디터와 개발 빌드에서만 동작합니다.
/// </remarks>
public sealed class ShelterClearCountDebugToggle : MonoBehaviour
{
    private const string TargetSceneName = "ShelterScene_Jung";
    private const float MessageDuration = 2.5f;

    private static ShelterClearCountDebugToggle s_instance;

    private bool m_isRaised;
    private int m_originalCount;
    private string m_message = string.Empty;
    private float m_messageHideAt;

    /// <summary>지금 활성 씬이 이 디버그 키를 쓰는 셸터 씬인지 여부입니다. 다른 디버그 창이 이 씬에서 키를 양보할 때 확인합니다.</summary>
    public static bool IsTargetSceneActive => SceneManager.GetActiveScene().name == TargetSceneName;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        s_instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (s_instance != null || !Debug.isDebugBuild)
        {
            return;
        }

        GameObject holder = new GameObject("[Debug] Shelter Clear Count Toggle");
        holder.hideFlags = HideFlags.DontSave;
        holder.AddComponent<ShelterClearCountDebugToggle>();
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
        {
            s_instance = null;
        }
    }

    private void Update()
    {
        if (!IsTargetSceneActive || Keyboard.current == null || !Keyboard.current.f12Key.wasPressedThisFrame)
        {
            return;
        }

        GameDataManager gameData = GameDataManager.Instance;
        if (gameData == null)
        {
            ShowMessage("GameDataManager가 없어 클리어 횟수를 바꿀 수 없습니다.");
            return;
        }

        if (!m_isRaised)
        {
            m_originalCount = gameData.DefenseClearCount;
            gameData.SetDefenseClearCount(m_originalCount + 1);
            m_isRaised = true;
        }
        else
        {
            gameData.SetDefenseClearCount(m_originalCount);
            m_isRaised = false;
        }

        RestartSpeechBubbles();
        ShowMessage($"방어전 클리어 횟수 {gameData.DefenseClearCount}" + (m_isRaised ? " (F12: 원래대로)" : " (원래 값)"));
    }

    // 말풍선은 루프 시작 시점에 대사 묶음을 정하므로, 재생 중인 말풍선을 다시 시작해 바뀐 횟수를 반영
    private static void RestartSpeechBubbles()
    {
        foreach (NpcSpeechBubble bubble in FindObjectsByType<NpcSpeechBubble>(FindObjectsSortMode.None))
        {
            if (bubble.IsPlaying)
            {
                bubble.Play();
            }
        }
    }

    private void ShowMessage(string message)
    {
        m_message = message;
        m_messageHideAt = Time.unscaledTime + MessageDuration;
        Debug.Log($"[ShelterClearCountDebugToggle] {message}", this);
    }

    private void OnGUI()
    {
        if (string.IsNullOrEmpty(m_message) || Time.unscaledTime > m_messageHideAt)
        {
            return;
        }

        GUI.Label(new Rect(16f, 16f, 480f, 24f), m_message);
    }
}
