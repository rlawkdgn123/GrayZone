using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using VInspector;

/// <summary>
/// 튜토리얼(<see cref="TutorialSequence"/>)을 진행합니다. 챕터의 블록(대사, 트리거, 엔드포인트)을 위에서부터 하나씩 차례로 처리합니다.
/// </summary>
/// <remarks>
/// 대사 표시는 <see cref="DialoguePlayer"/>에, 이벤트 실행은 <see cref="TutorialEvents"/>에 맡기고, 이 컴포넌트는 순서와 조건만 봅니다.
/// 기존 페이지형 튜토리얼(<see cref="TutorialManager"/>)과 별개이며, 두 튜토리얼을 한 씬에서 함께 켜지 않습니다.
/// <para>
/// 블록별 처리:
/// 대사는 넘김 방식(입력/자동)대로 넘어가고, 바로 다음 블록이 엔드포인트면 조건을 채울 때까지 띄워 둡니다.
/// 트리거는 이벤트를 한꺼번에 실행하고 자기 완료 조건을 세기 시작한 뒤 바로 다음 블록으로 갑니다.
/// 엔드포인트는 자기가 닫는 트리거(여러 개 가능)가 모두 완료될 때까지 기다렸다가 완료 이벤트를 실행하고 넘어갑니다.
/// 시네마틱은 인게임 시네마틱을 끝까지 재생합니다.
/// </para>
/// <para>
/// 엔드포인트는 다음 챕터로 넘어가는 기준입니다. 챕터의 트리거가 모두 해소돼야 다음 챕터가 시작되고,
/// 엔드포인트 없이 남은 트리거가 있으면 경고를 남기고 그 트리거가 완료될 때까지 기다립니다.
/// </para>
/// 기다리는 이벤트(창 띄우기, 도착까지 대기 등)가 있으면 끝날 때까지 다음 블록으로 넘어가지 않습니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class TutorialFlowDirector : MonoBehaviour
{
    private const int NoIndex = -1;

    private static TutorialFlowDirector s_instance;

    [Header("References")]
    [Tooltip("진행할 튜토리얼입니다.")]
    [SerializeField] private TutorialSequence m_sequence;

    [Tooltip("대사를 띄울 플레이어입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private DialoguePlayer m_dialogue;

    [Tooltip("블록의 이벤트를 실행할 곳입니다. 비어 있으면 씬에서 찾고, 없으면 이 오브젝트에 붙입니다.")]
    [SerializeField] private TutorialEvents m_events;

    [Header("Flow")]
    [Tooltip("켜면 씬 시작 시 처음부터 튜토리얼을 시작합니다. 끄면 Begin()을 불러야 시작합니다.")]
    [SerializeField] private bool m_playOnStart = true;

    [Tooltip("켜면 방어전 1회차(DefenseSceneDataManager.DefenseRound <= 1)에서만 시작합니다. 회차 정보가 없으면(씬 단독 실행 등) 시작합니다.")]
    [SerializeField] private bool m_onlyFirstDefenseRound = true;

    [Header("Debug")]
    [Tooltip("켜면 에디터와 개발 빌드에서 넘기기 키로 지금 기다리는 블록을 바로 넘깁니다. 대사, 엔드포인트(완료 조건 무시), 시네마틱, 기다리는 이벤트가 모두 해당합니다.\n" +
             "넘기기 키가 상호작용 키와 같으면 상호작용할 때도 넘어가므로, 실제 완료 조건을 확인할 때는 끕니다.")]
    [SerializeField] private bool m_debugAdvance = true;

    [Tooltip("디버그 넘기기 키입니다.")]
    [SerializeField] private Key m_debugAdvanceKey = Key.F;

    [Header("Current")]
    [Tooltip("현재 챕터 번호입니다. 진행 중이 아니면 -1입니다.")]
    [ReadOnly][SerializeField] private int m_stepIndex = NoIndex;

    [Tooltip("현재 챕터 안의 블록 번호입니다. 진행 중이 아니면 -1입니다.")]
    [ReadOnly][SerializeField] private int m_blockIndex = NoIndex;

    /// <summary>트리거 블록 번호별 완료 조건 감시자입니다. 엔드포인트가 닫거나 챕터·튜토리얼이 끝나면 정리합니다.</summary>
    private readonly Dictionary<int, TutorialConditionWatcher> m_watchers = new();

    private bool m_isRunning;

    /// <summary>엔드포인트를 기다리는 동안 띄워 둔 대사가 있는지 여부입니다. 엔드포인트가 닫히면 내립니다.</summary>
    private bool m_isHoldingDialogue;

    /// <summary>
    /// 지금 블록을 시작한 프레임입니다. 같은 프레임에 디버그 넘기기가 두 블록을 한꺼번에 넘기지 않도록 이보다 뒤 프레임의 입력만 받습니다.
    /// </summary>
    private int m_blockStartFrame;

    /// <summary>씬에 있는 활성 튜토리얼 진행기입니다. 없으면 null입니다.</summary>
    public static TutorialFlowDirector Instance => s_instance;

    /// <summary>씬에서 이 튜토리얼이 진행 중인지 여부입니다. 진행기가 없으면 false입니다.</summary>
    public static bool IsAnyRunning => s_instance != null && s_instance.IsRunning;

    /// <summary>튜토리얼이 진행 중인지 여부입니다.</summary>
    public bool IsRunning => m_isRunning;

    /// <summary>현재 챕터 번호입니다. 진행 중이 아니면 -1입니다.</summary>
    public int CurrentStepIndex => IsRunning ? m_stepIndex : NoIndex;

    /// <summary>현재 블록 번호입니다. 진행 중이 아니면 -1입니다.</summary>
    public int CurrentBlockIndex => IsRunning ? m_blockIndex : NoIndex;

    /// <summary>블록이 바뀔 때 (챕터 번호, 블록 번호)로 발생합니다.</summary>
    public event Action<int, int> OnBlockChanged;

    /// <summary>
    /// 마지막 블록까지 마치거나 <see cref="Skip"/>으로 건너뛰어 튜토리얼이 끝났을 때 발생합니다. <see cref="End"/>로 끝낼 때는 발생하지 않습니다.
    /// </summary>
    public event Action OnCompleted;

    /// <summary>튜토리얼을 건너뛰는 키입니다. 진행할 튜토리얼이 없으면 <see cref="Key.None"/>입니다.</summary>
    public Key SkipKey => m_sequence != null ? m_sequence.SkipKey : Key.None;

    /// <summary>처음부터 튜토리얼을 시작합니다. 이미 진행 중이면 처음부터 다시 시작합니다.</summary>
    public void Begin()
    {
        if (m_sequence == null)
        {
            return;
        }

        End();
        m_isRunning = true;
        StartCoroutine(RunSequence());
    }

    /// <summary>
    /// 남은 챕터를 모두 건너뛰고 튜토리얼을 마칩니다. 대사, 창, 마커, 멈춘 시간, 막아 둔 조작을 모두 되돌리고 <see cref="OnCompleted"/>를 보냅니다.
    /// </summary>
    /// <remarks>마친 것과 같게 알리는 이유는, 튜토리얼이 끝나기를 기다리는 쪽(전투 시작 등)이 건너뛴 경우에도 이어서 진행해야 해서입니다.</remarks>
    public void Skip()
    {
        if (!IsRunning)
        {
            return;
        }

        End();
        OnCompleted?.Invoke();
    }

    /// <summary>튜토리얼을 즉시 끝내고 대사를 내립니다. 막아 둔 조작도 엽니다. <see cref="OnCompleted"/>는 발생하지 않습니다.</summary>
    public void End()
    {
        m_isRunning = false;
        m_stepIndex = NoIndex;
        m_blockIndex = NoIndex;
        m_isHoldingDialogue = false;
        StopAllCoroutines();
        DisposeWatchers();
        // 씬이 내려갈 때도 불리므로 여기서는 찾거나 붙이지 않고, 이미 연결된 것만 되돌립니다.
        if (m_events != null)
        {
            m_events.ReleaseAll();
        }

        if (m_dialogue != null)
        {
            m_dialogue.Stop();
        }

        PlayerInputController.SetInputLock(PlayerInputLock.None);
    }

    private void Awake()
    {
        if (s_instance != null && s_instance != this)
        {
            Debug.LogWarning($"{nameof(TutorialFlowDirector)}가 씬에 둘 이상 있습니다. 먼저 등록된 쪽을 씁니다.", this);
            return;
        }

        s_instance = this;
    }

    private void OnDisable()
    {
        // 진행기가 꺼지면 조작을 풀어 줄 쪽이 없어지므로 여기서 엽니다.
        if (IsRunning)
        {
            End();
        }
    }

    private void OnDestroy()
    {
        if (s_instance == this)
        {
            s_instance = null;
        }
    }

    private void Start()
    {
        if (m_playOnStart && IsTutorialRound())
        {
            Begin();
        }
    }

    private void Update()
    {
        if (!IsRunning)
        {
            return;
        }

        if (WasSkipPressed())
        {
            Skip();
            return;
        }

        // 분대원이 늦게 준비되면 처음 건 잠금이 닿지 않을 수 있어 매 프레임 맞춥니다. 값이 같으면 아무것도 하지 않습니다.
        if (m_stepIndex >= 0)
        {
            PlayerInputController.SetInputLock(m_sequence.Steps[m_stepIndex].LockedInputs);
        }
    }

    private IEnumerator RunSequence()
    {
        IReadOnlyList<TutorialStep> steps = m_sequence.Steps;
        for (int stepIndex = 0; stepIndex < steps.Count; stepIndex++)
        {
            m_stepIndex = stepIndex;
            PlayerInputController.SetInputLock(steps[stepIndex].LockedInputs);
            yield return RunStep(steps[stepIndex]);
        }

        End();
        OnCompleted?.Invoke();
    }

    private IEnumerator RunStep(TutorialStep step)
    {
        IReadOnlyList<TutorialBlock> blocks = step.Blocks;
        TutorialBlockLinks links = TutorialBlockLinks.Build(blocks);

        for (int i = 0; i < blocks.Count; i++)
        {
            TutorialBlock block = blocks[i];
            if (block == null)
            {
                continue;
            }

            m_blockIndex = i;
            m_blockStartFrame = Time.frameCount;
            OnBlockChanged?.Invoke(m_stepIndex, i);

            switch (block.Type)
            {
                case TutorialBlockType.Dialogue:
                    bool hold = i + 1 < blocks.Count && blocks[i + 1] != null && blocks[i + 1].Type == TutorialBlockType.Endpoint;
                    yield return PlayDialogue(block.Dialogue, hold);
                    break;

                case TutorialBlockType.Trigger:
                    if (links.ClosedBy[i] == TutorialBlockLinks.None)
                    {
                        Debug.LogWarning($"튜토리얼 챕터 '{step.Label}'의 {i}번 트리거를 닫는 엔드포인트가 없습니다. 이벤트만 실행하고 넘어갑니다.", this);
                    }
                    else
                    {
                        m_watchers[i] = new TutorialConditionWatcher(block, this);
                    }

                    yield return RunActions(block.Actions);
                    break;

                case TutorialBlockType.Endpoint:
                    List<int> closes = links.Closes[i];
                    if (closes.Count == 0)
                    {
                        Debug.LogWarning($"튜토리얼 챕터 '{step.Label}'의 {i}번 엔드포인트가 닫을 트리거가 없습니다. 기다리지 않고 넘어갑니다.", this);
                    }

                    // 고른 트리거가 모두 완료돼야 닫힙니다. 먼저 완료된 트리거도 나머지를 기다립니다.
                    while (!AreTriggersMet(closes) && !ConsumeDebugAdvance())
                    {
                        yield return null;
                    }

                    for (int k = 0; k < closes.Count; k++)
                    {
                        if (m_watchers.TryGetValue(closes[k], out TutorialConditionWatcher watcher))
                        {
                            watcher.Dispose();
                            m_watchers.Remove(closes[k]);
                        }
                    }

                    ReleaseHeldDialogue();
                    yield return RunActions(block.Actions);
                    break;

                case TutorialBlockType.Cinematic:
                    ReleaseHeldDialogue();
                    yield return PlayCinematic(block.CinematicId);
                    break;
            }
        }

        // 엔드포인트는 다음 챕터로 넘어가는 기준입니다. 엔드포인트 없이 남은 트리거도 완료돼야 다음 챕터로 넘어갑니다.
        if (m_watchers.Count > 0)
        {
            Debug.LogWarning($"튜토리얼 챕터 '{step.Label}'에 엔드포인트로 닫히지 않은 트리거가 {m_watchers.Count}개 있습니다. 완료될 때까지 기다린 뒤 다음 챕터로 넘어갑니다.", this);
            m_blockStartFrame = Time.frameCount;
            while (!AreTriggersMet(new List<int>(m_watchers.Keys)) && !ConsumeDebugAdvance())
            {
                yield return null;
            }
        }

        ReleaseHeldDialogue();
        DisposeWatchers();
    }

    /// <summary>
    /// 대사 한 줄을 띄우고 끝날 때까지 기다립니다. <paramref name="hold"/>면 뜨는 순간 끝나고, 다음 엔드포인트가 닫힐 때 내립니다.
    /// </summary>
    private IEnumerator PlayDialogue(DialogueLine line, bool hold)
    {
        DialoguePlayer dialogue = ResolveDialogue();
        if (dialogue == null)
        {
            yield break;
        }

        bool done = false;
        dialogue.PlayLine(line, hold, () => done = true);
        m_isHoldingDialogue = hold;
        while (!done)
        {
            if (ConsumeDebugAdvance())
            {
                dialogue.Stop();
                yield break;
            }

            yield return null;
        }
    }

    /// <summary>시네마틱을 끝까지 재생합니다. 디버그 넘기기를 누르면 바로 끝내고 카메라 복귀만 기다립니다.</summary>
    private IEnumerator PlayCinematic(string cinematicId)
    {
        TutorialEvents events = ResolveEvents();
        bool done = false;
        StartCoroutine(RunTracked(events.PlayCinematic(cinematicId), () => done = true));
        while (!done)
        {
            if (ConsumeDebugAdvance())
            {
                events.SkipCinematic();
            }

            yield return null;
        }
    }

    private void ReleaseHeldDialogue()
    {
        if (m_isHoldingDialogue && m_dialogue != null)
        {
            m_dialogue.Stop();
        }

        m_isHoldingDialogue = false;
    }

    /// <summary>이벤트를 한꺼번에 시작하고, 기다리는 이벤트가 모두 끝날 때까지 기다립니다.</summary>
    private IEnumerator RunActions(IReadOnlyList<TutorialAction> actions)
    {
        if (actions == null || actions.Count == 0)
        {
            yield break;
        }

        TutorialEvents events = ResolveEvents();
        int running = 0;
        for (int i = 0; i < actions.Count; i++)
        {
            if (actions[i] != null)
            {
                running++;
                StartCoroutine(RunTracked(events.Run(actions[i]), () => running--));
            }
        }

        // 디버그 넘기기로 기다림만 끝냅니다. 이미 시작한 이벤트는 그대로 돌게 둡니다.
        while (running > 0 && !ConsumeDebugAdvance())
        {
            yield return null;
        }
    }

    private static IEnumerator RunTracked(IEnumerator routine, Action onDone)
    {
        yield return routine;
        onDone();
    }

    /// <summary>트리거 블록 번호들의 완료 조건이 모두 채워졌는지 여부입니다. 감시자가 없는 트리거는 완료로 봅니다.</summary>
    private bool AreTriggersMet(List<int> triggers)
    {
        for (int i = 0; i < triggers.Count; i++)
        {
            if (m_watchers.TryGetValue(triggers[i], out TutorialConditionWatcher watcher) && !watcher.IsMet)
            {
                return false;
            }
        }

        return true;
    }

    private void DisposeWatchers()
    {
        foreach (TutorialConditionWatcher watcher in m_watchers.Values)
        {
            watcher.Dispose();
        }

        m_watchers.Clear();
    }

    /// <summary>
    /// 이번 프레임에 디버그 넘기기 키를 눌렀으면 true를 돌려주고 받은 것으로 표시합니다. 에디터와 개발 빌드에서만 받습니다.
    /// </summary>
    /// <remarks>
    /// 블록을 시작한 프레임의 입력은 받지 않습니다. 앞 블록을 넘긴 같은 누름으로 다음 블록까지 넘어가지 않게 하기 위해서입니다.
    /// </remarks>
    private bool ConsumeDebugAdvance()
    {
        if (!m_debugAdvance || !Debug.isDebugBuild || m_debugAdvanceKey == Key.None || Time.frameCount <= m_blockStartFrame)
        {
            return false;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard[m_debugAdvanceKey].wasPressedThisFrame)
        {
            return false;
        }

        m_blockStartFrame = Time.frameCount;
        return true;
    }

    private bool WasSkipPressed()
    {
        Key key = SkipKey;
        Keyboard keyboard = Keyboard.current;
        return key != Key.None && keyboard != null && keyboard[key].wasPressedThisFrame;
    }

    /// <summary>
    /// 이번 판에 튜토리얼을 진행할지 여부입니다. 방어전 1회차에서만 진행합니다.
    /// </summary>
    /// <remarks>회차는 클리어 횟수 + 1이라, 1회차를 실패하고 다시 들어오면 튜토리얼도 다시 나옵니다.</remarks>
    private bool IsTutorialRound()
    {
        if (!m_onlyFirstDefenseRound)
        {
            return true;
        }

        DefenseSceneDataManager data = DefenseSceneDataManager.Instance;
        return data == null || data.DefenseRound <= 1;
    }

    private DialoguePlayer ResolveDialogue()
    {
        if (m_dialogue == null)
        {
            m_dialogue = FindFirstObjectByType<DialoguePlayer>(FindObjectsInactive.Include);
        }

        return m_dialogue;
    }

    private TutorialEvents ResolveEvents()
    {
        if (m_events == null)
        {
            m_events = FindFirstObjectByType<TutorialEvents>(FindObjectsInactive.Include);
        }

        if (m_events == null)
        {
            m_events = gameObject.AddComponent<TutorialEvents>();
        }

        return m_events;
    }
}
