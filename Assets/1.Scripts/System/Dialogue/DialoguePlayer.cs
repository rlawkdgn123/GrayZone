using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 대사 목록을 한 줄씩 대화창과 말풍선에 띄우고, 줄마다 정한 방식(입력/자동)으로 넘깁니다.
/// </summary>
/// <remarks>
/// 튜토리얼 전용이 아닙니다. 대사를 띄우는 쪽은 <see cref="Play"/>에 줄 목록을 넘기고 끝 알림만 받습니다.
/// <para>
/// 마지막 줄 유지(<c>holdLastLine</c>)를 켜면 마지막 줄을 넘기지 않고 띄워 둡니다. 행동을 유도하는 대사가
/// 행동을 마칠 때까지 화면에 남아야 해서입니다. 이때 끝 알림은 마지막 줄이 뜨는 순간 보내고, 내리는 것은 부른 쪽이 <see cref="Stop"/>으로 합니다.
/// </para>
/// 시간은 실제 시간으로 셉니다. 컷신이나 안내 중에 게임 시간이 멈춰도 자동 줄이 넘어가게 하기 위해서입니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class DialoguePlayer : MonoBehaviour
{
    private const int NoLine = -1;

    [Header("References")]
    [Tooltip("대사를 그릴 대화창입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private DialogueWindow m_window;

    [Tooltip("캐릭터 머리 위에 띄울 말풍선 프리팹입니다. 선택 항목이며, 비우면 말풍선 없이 대화창만 띄웁니다.")]
    [SerializeField] private DialogueSpeechBubble m_bubblePrefab;

    [Tooltip("끄면 말풍선 없이 대화창만 띄웁니다.")]
    [SerializeField] private bool m_showBubbles = true;

    [Tooltip("말풍선을 캐릭터 발밑 기준 얼마나 위에 띄울지(m)입니다.")]
    [SerializeField] private float m_bubbleHeight = 1.9f;

    [Header("Auto Advance")]
    [Min(0.0f)]
    [Tooltip("자동 줄의 기본 표시 시간(초)입니다. 줄의 시간이 0일 때 여기에 글자 수 시간을 더합니다.")]
    [SerializeField] private float m_autoBaseSeconds = 1.5f;

    [Min(0.0f)]
    [Tooltip("자동 줄에서 글자 하나당 더하는 표시 시간(초)입니다.")]
    [SerializeField] private float m_autoSecondsPerCharacter = 0.08f;

    [Header("Input Advance")]
    [Min(0.0f)]
    [Tooltip("줄이 뜬 직후 넘김 입력을 무시하는 시간(초)입니다. 앞 줄을 넘긴 입력이 다음 줄까지 넘기지 않게 합니다.")]
    [SerializeField] private float m_inputGuardSeconds = 0.2f;

    [Tooltip("입력 줄을 넘기는 키입니다.")]
    [SerializeField] private Key m_advanceKey = Key.F;

    private readonly List<DialogueLine> m_lines = new();
    private readonly DialogueLine[] m_singleLine = new DialogueLine[1];
    private readonly Dictionary<PlayerbleCharacterId, DialogueSpeechBubble> m_bubbles = new();

    private int m_lineIndex = NoLine;
    private float m_lineTimer;
    private bool m_holdLastLine;
    private Action m_onFinished;
    private DialogueSpeechBubble m_activeBubble;

    /// <summary>대사를 띄우고 있는지 여부입니다. 마지막 줄을 유지하는 중에도 true입니다.</summary>
    public bool IsPlaying => m_lineIndex != NoLine;

    /// <summary>마지막 줄을 넘기지 않고 띄워 두는 중인지 여부입니다.</summary>
    public bool IsHoldingLastLine => IsPlaying && m_holdLastLine && m_lineIndex == m_lines.Count - 1;

    /// <summary>
    /// 대사 목록을 처음부터 띄웁니다. 이미 띄우던 대사는 끝 알림 없이 멈춥니다.
    /// </summary>
    /// <param name="lines">띄울 줄 목록입니다. 비어 있으면 바로 끝 알림을 보냅니다.</param>
    /// <param name="holdLastLine">켜면 마지막 줄을 넘기지 않고 띄워 둡니다.</param>
    /// <param name="onFinished">모든 줄을 넘겼을 때(유지 모드는 마지막 줄이 뜬 순간) 한 번 불립니다. 선택 항목입니다.</param>
    public void Play(IReadOnlyList<DialogueLine> lines, bool holdLastLine, Action onFinished)
    {
        Stop();

        if (lines != null)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                m_lines.Add(lines[i]);
            }
        }

        m_holdLastLine = holdLastLine;
        m_onFinished = onFinished;

        if (m_lines.Count == 0)
        {
            Finish();
            return;
        }

        EnterLine(0);
    }

    /// <summary>
    /// 대사 한 줄만 띄웁니다. 줄마다 넘김 조건이 따로 있는 쪽(튜토리얼)이 한 줄씩 진행할 때 씁니다.
    /// </summary>
    /// <param name="line">띄울 줄입니다.</param>
    /// <param name="hold">켜면 줄을 넘기지 않고 띄워 두고, 뜨는 순간 끝 알림을 보냅니다. 내리는 것은 부른 쪽이 합니다.</param>
    /// <param name="onFinished">줄을 넘겼을 때(유지 모드는 뜬 순간) 한 번 불립니다. 선택 항목입니다.</param>
    public void PlayLine(DialogueLine line, bool hold, Action onFinished)
    {
        m_singleLine[0] = line;
        Play(m_singleLine, hold, onFinished);
    }

    /// <summary>대사를 멈추고 대화창과 말풍선을 내립니다. 끝 알림은 보내지 않습니다.</summary>
    public void Stop()
    {
        m_lineIndex = NoLine;
        m_lines.Clear();
        m_onFinished = null;
        HideAll();
    }

    private void OnDisable()
    {
        Stop();
    }

    private void Update()
    {
        if (!IsPlaying || IsHoldingLastLine)
        {
            return;
        }

        m_lineTimer += Time.unscaledDeltaTime;

        DialogueLine line = m_lines[m_lineIndex];
        bool advance = !line.HasText
            || (line.AdvanceMode == DialogueAdvanceMode.Auto
                ? m_lineTimer >= ResolveAutoSeconds(line)
                : m_lineTimer >= m_inputGuardSeconds && WasAdvancePressed());

        if (advance)
        {
            NextLine();
        }
    }

    private void NextLine()
    {
        int next = m_lineIndex + 1;
        if (next >= m_lines.Count)
        {
            m_lineIndex = NoLine;
            HideAll();
            Finish();
            return;
        }

        EnterLine(next);
    }

    private void EnterLine(int index)
    {
        m_lineIndex = index;
        m_lineTimer = 0.0f;

        DialogueLine line = m_lines[index];
        if (!line.HasText)
        {
            // 이벤트만 끼워 넣는 빈 줄입니다. 띄울 것이 없으므로 앞 줄의 창과 말풍선도 내립니다.
            HideAll();
        }
        else
        {
            DialogueSpeaker speakerInfo = line.Speaker;
            PlayerbleCharacterId characterId = speakerInfo != null ? speakerInfo.Character : PlayerbleCharacterId.Unknown;
            PlayerbleUnitData character = FindCharacter(characterId);

            string speakerName = speakerInfo != null && !string.IsNullOrWhiteSpace(speakerInfo.DisplayName)
                ? speakerInfo.DisplayName
                : character != null ? character.DisplayName : string.Empty;
            Sprite portrait = line.Portrait != null ? line.Portrait
                : speakerInfo != null && speakerInfo.Portrait != null ? speakerInfo.Portrait
                : character != null ? character.HudPortrait : null;

            string role = speakerInfo != null ? speakerInfo.Role : string.Empty;
            ResolveWindow()?.ShowLine(speakerName, role, line.Text, portrait);
            ShowBubble(characterId, character, line.BubbleText);
        }

        if (IsHoldingLastLine)
        {
            Finish();
        }
    }

    private void Finish()
    {
        // 알림 안에서 다음 대사를 Play할 수 있어, 비운 뒤에 부릅니다.
        Action callback = m_onFinished;
        m_onFinished = null;
        callback?.Invoke();
    }

    private float ResolveAutoSeconds(DialogueLine line)
    {
        if (line.AutoSeconds > 0.0f)
        {
            return line.AutoSeconds;
        }

        int length = string.IsNullOrEmpty(line.Text) ? 0 : line.Text.Length;
        return m_autoBaseSeconds + length * m_autoSecondsPerCharacter;
    }

    private bool WasAdvancePressed()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && m_advanceKey != Key.None && keyboard[m_advanceKey].wasPressedThisFrame;
    }

    /// <summary>
    /// 화자 머리 위에 말풍선을 띄우고, 다른 캐릭터의 말풍선은 내립니다. 화자가 없으면 모두 내립니다.
    /// </summary>
    /// <remarks>말풍선은 캐릭터마다 처음 말할 때 한 번 만들어 그 캐릭터 아래에 붙여 두고 다시 씁니다.</remarks>
    private void ShowBubble(PlayerbleCharacterId id, PlayerbleUnitData speaker, string text)
    {
        DialogueSpeechBubble bubble = speaker != null ? ResolveBubble(id, speaker) : null;
        if (m_activeBubble != null && m_activeBubble != bubble)
        {
            m_activeBubble.Hide();
        }

        m_activeBubble = bubble;
        if (bubble != null && !string.IsNullOrWhiteSpace(text))
        {
            bubble.Show(text);
        }
        else
        {
            bubble?.Hide();
        }
    }

    private DialogueSpeechBubble ResolveBubble(PlayerbleCharacterId id, PlayerbleUnitData speaker)
    {
        if (!m_showBubbles || m_bubblePrefab == null)
        {
            return null;
        }

        if (m_bubbles.TryGetValue(id, out DialogueSpeechBubble bubble) && bubble != null)
        {
            return bubble;
        }

        bubble = Instantiate(m_bubblePrefab, speaker.transform);
        bubble.transform.localPosition = new Vector3(0.0f, m_bubbleHeight, 0.0f);
        bubble.transform.localRotation = Quaternion.identity;
        m_bubbles[id] = bubble;
        return bubble;
    }

    private void HideAll()
    {
        ResolveWindow()?.Hide();
        foreach (DialogueSpeechBubble bubble in m_bubbles.Values)
        {
            if (bubble != null)
            {
                bubble.Hide();
            }
        }

        m_activeBubble = null;
    }

    private static PlayerbleUnitData FindCharacter(PlayerbleCharacterId id)
    {
        if (id == PlayerbleCharacterId.Unknown || SquadManager.Instance == null)
        {
            return null;
        }

        IReadOnlyList<PlayerbleUnitData> members = SquadManager.Instance.PlayerDataSources;
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i] != null && members[i].CharacterId == id)
            {
                return members[i];
            }
        }

        return null;
    }

    private DialogueWindow ResolveWindow()
    {
        if (m_window == null)
        {
            m_window = FindFirstObjectByType<DialogueWindow>(FindObjectsInactive.Include);
        }

        return m_window;
    }
}
