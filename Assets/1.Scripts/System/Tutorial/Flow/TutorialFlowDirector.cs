using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VInspector;

/// <summary>
/// 튜토리얼(<see cref="TutorialSequence"/>)을 챕터별로 한 줄씩 진행합니다. 챕터의 줄을 모두 넘기고 종료 조건을 채우면 다음 챕터로 넘어갑니다.
/// </summary>
/// <remarks>
/// 대사 표시는 <see cref="DialoguePlayer"/>에 맡기고, 이 컴포넌트는 순서, 조건, 이벤트만 봅니다.
/// 기존 페이지형 튜토리얼(<see cref="TutorialManager"/>)과 별개이며, 두 튜토리얼을 한 씬에서 함께 켜지 않습니다.
/// <para>
/// 줄은 대사의 넘김 방식(입력/자동)대로 넘어갑니다. 챕터에 종료 조건이 있으면 마지막 줄의 대사를 띄워 둔 채 조건을 기다립니다.
/// 처치·상호작용은 챕터가 시작된 순간부터 세서, 앞 줄에서 적을 만들고 대사를 읽는 동안 처치한 것도 들어갑니다.
/// </para>
/// 줄의 이벤트는 <see cref="TutorialEvents"/>가 실행합니다. 기다리는 이벤트(창 띄우기 등)가 끝날 때까지
/// 다음 줄로 넘어가지 않고, 종료 조건도 확인하지 않습니다.
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

    [Tooltip("줄의 이벤트를 실행할 곳입니다. 비어 있으면 씬에서 찾고, 없으면 이 오브젝트에 붙입니다.")]
    [SerializeField] private TutorialEvents m_events;

    [Header("Flow")]
    [Tooltip("켜면 씬 시작 시 처음부터 튜토리얼을 시작합니다. 끄면 Begin()을 불러야 시작합니다.")]
    [SerializeField] private bool m_playOnStart = true;

    [Tooltip("켜면 방어전 1회차(DefenseSceneDataManager.DefenseRound <= 1)에서만 시작합니다. 회차 정보가 없으면(씬 단독 실행 등) 시작합니다.")]
    [SerializeField] private bool m_onlyFirstDefenseRound = true;

    [Header("Current")]
    [Tooltip("현재 묶음 번호입니다. 진행 중이 아니면 -1입니다.")]
    [ReadOnly][SerializeField] private int m_stepIndex = NoIndex;

    [Tooltip("현재 묶음 안의 줄 번호입니다. 진행 중이 아니면 -1입니다.")]
    [ReadOnly][SerializeField] private int m_lineIndex = NoIndex;

    /// <summary>현재 줄의 대사가 떴는지(띄워 두는 마지막 줄) 또는 넘어갔는지(그 밖의 줄) 여부입니다.</summary>
    private bool m_dialogueDone;

    /// <summary>현재 줄이 뜬 뒤 흐른 실제 시간(초)입니다. Time 조건에 씁니다.</summary>
    private float m_lineTimer;

    /// <summary>줄의 이벤트를 실행 중인지 여부입니다. 기다리는 이벤트가 끝날 때까지 다음 줄로 넘어가지 않습니다.</summary>
    private bool m_actionsBusy;

    /// <summary>KillEnemy: 챕터가 시작된 뒤 처치한(프리팹이 맞는) 적 수입니다.</summary>
    private int m_killed;

    /// <summary>대상 ID로 대상을 찾지 못했는지 여부입니다. 튜토리얼이 멈추지 않도록 경고 후 조건을 채운 것으로 봅니다.</summary>
    private bool m_targetMissing;

    /// <summary>Interact: 조건에 맞는 상호작용을 했는지 여부입니다.</summary>
    private bool m_interacted;

    /// <summary>KillEnemy: 사망 알림을 걸어 둔 적과 그 처리기입니다. 조건이 끝나면 모두 뗍니다.</summary>
    private readonly Dictionary<EnemyHealth, Action> m_deathHandlers = new();

    /// <summary>씬에 있는 활성 튜토리얼 진행기입니다. 없으면 null입니다.</summary>
    public static TutorialFlowDirector Instance => s_instance;

    /// <summary>씬에서 이 튜토리얼이 진행 중인지 여부입니다. 진행기가 없으면 false입니다.</summary>
    public static bool IsAnyRunning => s_instance != null && s_instance.IsRunning;

    /// <summary>튜토리얼이 진행 중인지 여부입니다.</summary>
    public bool IsRunning => m_sequence != null && m_stepIndex >= 0 && m_stepIndex < m_sequence.Steps.Count;

    /// <summary>현재 묶음 번호입니다. 진행 중이 아니면 -1입니다.</summary>
    public int CurrentStepIndex => IsRunning ? m_stepIndex : NoIndex;

    /// <summary>현재 줄 번호입니다. 진행 중이 아니면 -1입니다.</summary>
    public int CurrentLineIndex => IsRunning ? m_lineIndex : NoIndex;

    /// <summary>줄이 바뀔 때 (묶음 번호, 줄 번호)로 발생합니다.</summary>
    public event Action<int, int> OnLineChanged;

    /// <summary>마지막 줄까지 마쳐 튜토리얼이 끝났을 때 발생합니다. <see cref="End"/>로 끝낼 때는 발생하지 않습니다.</summary>
    public event Action OnCompleted;

    /// <summary>처음부터 튜토리얼을 시작합니다. 줄이 하나도 없으면 아무것도 하지 않습니다.</summary>
    public void Begin()
    {
        if (m_sequence == null)
        {
            return;
        }

        EnterNextLine(0, NoIndex);
    }

    /// <summary>튜토리얼을 즉시 끝내고 대사를 내립니다. 막아 둔 조작도 엽니다. <see cref="OnCompleted"/>는 발생하지 않습니다.</summary>
    public void End()
    {
        m_stepIndex = NoIndex;
        m_lineIndex = NoIndex;
        m_actionsBusy = false;
        StopAllCoroutines();
        StopCondition();
        // 씬이 내려갈 때도 불리므로 여기서는 찾거나 붙이지 않고, 이미 연결된 것만 되돌립니다.
        if (m_events != null)
        {
            m_events.ReleaseAll();
        }
        ResolveDialogue()?.Stop();
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

        // 분대원이 늦게 준비되면 처음 건 잠금이 닿지 않을 수 있어 매 프레임 맞춥니다. 값이 같으면 아무것도 하지 않습니다.
        PlayerInputController.SetInputLock(m_sequence.Steps[m_stepIndex].LockedInputs);

        if (!m_dialogueDone || m_actionsBusy)
        {
            return;
        }

        TutorialStep step = CurrentStep;
        m_lineTimer += Time.unscaledDeltaTime;
        bool isLastLine = m_lineIndex == step.Lines.Count - 1;
        if (isLastLine)
        {
            if (!IsConditionMet(step))
            {
                return;
            }

            StopCondition();
        }

        FinishLine(CurrentLine);
    }

    private TutorialStep CurrentStep => m_sequence.Steps[m_stepIndex];

    private TutorialLine CurrentLine => CurrentStep.Lines[m_lineIndex];

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

    private bool IsConditionMet(TutorialStep step)
    {
        switch (step.Condition)
        {
            case TutorialConditionType.None:
                // 조건 없는 챕터는 마지막 대사를 넘긴 순간(m_dialogueDone)이 끝입니다.
                return true;

            case TutorialConditionType.Time:
                return m_lineTimer >= step.Seconds;

            case TutorialConditionType.KillEnemy:
                return m_killed >= step.KillCount;

            case TutorialConditionType.ReachArea:
                return TutorialEvents.IsPlayerInArea(step.AreaCenter, step.AreaRadius);

            case TutorialConditionType.Interact:
                return m_interacted || m_targetMissing;

            default:
                return false;
        }
    }

    /// <summary>
    /// 챕터의 종료 조건을 세기 시작합니다. 처치·상호작용처럼 알림이 필요한 조건은 여기서 알림을 겁니다.
    /// </summary>
    private void StartCondition(TutorialStep step)
    {
        StopCondition();

        switch (step.Condition)
        {
            case TutorialConditionType.KillEnemy:
                EnemyHealth[] enemies = FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);
                for (int i = 0; i < enemies.Length; i++)
                {
                    WatchEnemy(enemies[i]);
                }

                // 조건을 세는 동안 새로 나오는(스폰되거나 풀에서 꺼내지는) 적도 셉니다.
                EnemyHealth.OnEnemyEnabled += WatchEnemy;
                break;

            case TutorialConditionType.Interact:
                if (!string.IsNullOrEmpty(step.TargetId))
                {
                    FindTarget(step.TargetId);
                }

                InteractionController.OnAnyInteracted += HandleInteracted;
                break;
        }
    }

    /// <summary>조건 세기를 멈추고 걸어 둔 알림을 모두 뗍니다.</summary>
    private void StopCondition()
    {
        EnemyHealth.OnEnemyEnabled -= WatchEnemy;
        InteractionController.OnAnyInteracted -= HandleInteracted;
        foreach (KeyValuePair<EnemyHealth, Action> pair in m_deathHandlers)
        {
            if (pair.Key != null)
            {
                pair.Key.OnDeath -= pair.Value;
            }
        }

        m_deathHandlers.Clear();
        m_killed = 0;
        m_targetMissing = false;
        m_interacted = false;
    }

    /// <summary>대상 ID로 씬 대상을 찾습니다. 없으면 경고를 남기고, 튜토리얼이 멈추지 않도록 조건을 채운 것으로 둡니다.</summary>
    private TutorialTarget FindTarget(string id)
    {
        TutorialTarget target = TutorialTarget.Find(id);
        if (target == null)
        {
            Debug.LogWarning($"튜토리얼 대상 '{id}'를 찾지 못해 조건을 건너뜁니다. 씬 오브젝트에 같은 ID의 {nameof(TutorialTarget)}이 있는지 확인하세요.", this);
            m_targetMissing = true;
        }

        return target;
    }

    private void WatchEnemy(EnemyHealth enemy)
    {
        if (enemy == null || m_deathHandlers.ContainsKey(enemy) || !IsRunning
            || !TutorialEvents.IsFromPrefab(enemy, CurrentStep.EnemyPrefab))
        {
            return;
        }

        Action handler = () => m_killed++;
        enemy.OnDeath += handler;
        m_deathHandlers[enemy] = handler;
    }

    private void HandleInteracted(IInteractable interactable, GameObject interactor)
    {
        string id = CurrentStep.TargetId;
        if (string.IsNullOrEmpty(id))
        {
            m_interacted = true;
            return;
        }

        TutorialTarget target = interactable is Component component ? component.GetComponentInParent<TutorialTarget>() : null;
        if (target != null && target.Id == id)
        {
            m_interacted = true;
        }
    }

    /// <summary>
    /// 지금 위치 다음의 줄로 들어갑니다. 줄이 없는 묶음은 건너뛰고, 더 없으면 튜토리얼을 마칩니다.
    /// </summary>
    private void EnterNextLine(int stepIndex, int lineIndex)
    {
        IReadOnlyList<TutorialStep> steps = m_sequence.Steps;
        lineIndex++;
        while (stepIndex < steps.Count && lineIndex >= steps[stepIndex].Lines.Count)
        {
            stepIndex++;
            lineIndex = 0;
        }

        if (stepIndex >= steps.Count)
        {
            bool wasRunning = IsRunning;
            End();
            if (wasRunning)
            {
                OnCompleted?.Invoke();
            }

            return;
        }

        bool isNewStep = stepIndex != m_stepIndex || lineIndex == 0;
        m_stepIndex = stepIndex;
        m_lineIndex = lineIndex;
        m_dialogueDone = false;
        m_lineTimer = 0.0f;

        // 처치·상호작용은 챕터가 시작된 순간부터 셉니다.
        TutorialStep step = steps[stepIndex];
        if (isNewStep)
        {
            StartCondition(step);
        }

        TutorialLine line = CurrentLine;
        PlayerInputController.SetInputLock(steps[stepIndex].LockedInputs);
        RunActions(line, TutorialActionTiming.OnShow, null);
        OnLineChanged?.Invoke(stepIndex, lineIndex);

        DialoguePlayer dialogue = ResolveDialogue();
        if (dialogue == null)
        {
            m_dialogueDone = true;
            return;
        }

        // 종료 조건이 있는 챕터의 마지막 줄은 띄워 둔 채로 뜬 순간 알림을 받고, 그 밖의 줄은 넘긴 순간 알림을 받습니다.
        // 빈 대사는 Play 안에서 바로 알림이 옵니다.
        bool hold = step.HasCondition && lineIndex == step.Lines.Count - 1;
        int capturedStep = stepIndex;
        int capturedLine = lineIndex;
        dialogue.PlayLine(line.Dialogue, hold, () => HandleDialogueDone(capturedStep, capturedLine));
    }

    private void HandleDialogueDone(int stepIndex, int lineIndex)
    {
        if (stepIndex == m_stepIndex && lineIndex == m_lineIndex)
        {
            m_dialogueDone = true;
        }
    }

    private void FinishLine(TutorialLine line)
    {
        int stepIndex = m_stepIndex;
        int lineIndex = m_lineIndex;
        RunActions(line, TutorialActionTiming.OnFinish, () => EnterNextLine(stepIndex, lineIndex));
    }

    /// <summary>
    /// 줄의 이벤트 중 해당 시점의 것을 한꺼번에 시작하고, 기다리는 이벤트가 모두 끝나면 <paramref name="onDone"/>을 부릅니다.
    /// </summary>
    /// <param name="onDone">모두 끝난 뒤 부를 동작입니다. 선택 항목입니다.</param>
    private void RunActions(TutorialLine line, TutorialActionTiming timing, Action onDone)
    {
        m_actionsBusy = true;
        StartCoroutine(RunActionsRoutine(line, timing, onDone));
    }

    private IEnumerator RunActionsRoutine(TutorialLine line, TutorialActionTiming timing, Action onDone)
    {
        TutorialEvents events = ResolveEvents();
        IReadOnlyList<TutorialAction> actions = line.Actions;
        int running = 0;
        for (int i = 0; i < actions.Count; i++)
        {
            if (actions[i] != null && actions[i].Timing == timing)
            {
                running++;
                StartCoroutine(RunTracked(events.Run(actions[i]), () => running--));
            }
        }

        while (running > 0)
        {
            yield return null;
        }

        // 기다리는 이벤트가 없으면 StartCoroutine 안에서 바로 여기까지 옵니다. onDone이 다음 줄의 이벤트를 시작할 수 있어 먼저 풉니다.
        m_actionsBusy = false;
        onDone?.Invoke();
    }

    private DialoguePlayer ResolveDialogue()
    {
        if (m_dialogue == null)
        {
            m_dialogue = FindFirstObjectByType<DialoguePlayer>(FindObjectsInactive.Include);
        }

        return m_dialogue;
    }

    private static IEnumerator RunTracked(IEnumerator routine, Action onDone)
    {
        yield return routine;
        onDone();
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
