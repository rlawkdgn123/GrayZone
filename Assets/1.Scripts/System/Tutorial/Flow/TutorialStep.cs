using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 튜토리얼 블록의 종류입니다. 챕터의 블록은 위에서부터 차례로 하나씩 진행합니다.
/// </summary>
/// <remarks>값은 에셋에 숫자로 저장되므로 이미 쓰는 값의 번호를 바꾸지 마세요.</remarks>
public enum TutorialBlockType
{
    /// <summary>대사 한 줄을 띄웁니다. 대사의 넘김 방식(입력/자동)대로 넘어갑니다. 바로 다음이 엔드포인트면 엔드포인트가 닫힐 때까지 띄워 둡니다.</summary>
    Dialogue = 0,

    /// <summary>
    /// 이벤트 여러 개를 한꺼번에 실행하고, 이 트리거의 완료 조건을 세기 시작합니다. 기다리지 않고 바로 다음 블록으로 넘어갑니다.
    /// 뒤에 이 트리거를 닫는 엔드포인트가 하나 있어야 합니다.
    /// </summary>
    Trigger = 1,

    /// <summary>
    /// 앞에서 열린 트리거 중 고른 것들이 모두 완료될 때까지 기다렸다가 닫고, 완료 이벤트를 실행한 뒤 넘어갑니다.
    /// 트리거 하나는 엔드포인트 하나로만 닫히고, 엔드포인트 하나는 트리거 여러 개를 함께 닫을 수 있습니다. 열린 트리거가 있을 때만 둘 수 있습니다.
    /// 챕터의 트리거가 모두 해소돼야 다음 챕터로 넘어갑니다.
    /// </summary>
    Endpoint = 2,

    /// <summary>
    /// 인게임 시네마틱을 재생하고 끝날 때까지 기다립니다. 시네마틱은 씬의 <see cref="TutorialCinematicSettings"/> 목록에서 고릅니다
    /// (Timeline이 있으면 Timeline, 없으면 카메라 목록을 차례로 비춤).
    /// </summary>
    Cinematic = 3,
}

/// <summary>
/// 트리거가 완료로 취급되는 조건 종류입니다. 조건은 트리거가 실행된 순간부터 셉니다.
/// </summary>
/// <remarks>값은 에셋에 숫자로 저장되므로 이미 쓰는 값의 번호를 바꾸지 마세요. 새 조건은 뒤에 번호를 붙여 추가합니다.</remarks>
public enum TutorialConditionType
{
    /// <summary>조건 없음입니다. 이벤트를 실행하면 바로 완료입니다.</summary>
    None = 0,

    /// <summary>트리거가 실행된 뒤 정한 시간(초)이 지나면 완료입니다.</summary>
    Time = 1,

    /// <summary>
    /// 트리거가 실행된 뒤 적을 정한 수만큼 처치하면 완료입니다. 적 프리팹을 넣으면 그 프리팹으로 만든 적만, 비우면 아무 적이나 셉니다.
    /// </summary>
    KillEnemy = 2,

    /// <summary>조작 중인 캐릭터가 정한 위치의 반경 안에 들어가면 완료입니다.</summary>
    ReachArea = 3,

    /// <summary>
    /// 트리거가 실행된 뒤 상호작용을 마치면 완료입니다. 대상 ID를 넣으면 그 <see cref="TutorialTarget"/>이 붙은 대상, 비우면 아무 대상이나입니다.
    /// </summary>
    Interact = 4,

    /// <summary>
    /// 조작 중인 캐릭터가 정한 번호(캐릭터 변경 키 1~3과 같은 번호)의 캐릭터면 완료입니다.
    /// 번호를 0으로 두면 트리거가 실행될 때와 다른 캐릭터로 바꾸면 완료입니다.
    /// </summary>
    SwitchCharacter = 5,

    /// <summary>
    /// 트리거가 실행된 뒤 스킬을 쓰면 완료입니다. 캐릭터 번호를 0으로 두면 아무 캐릭터, 1~3이면 그 캐릭터의 스킬만 셉니다.
    /// </summary>
    UseSkill = 6,

    /// <summary>고른 명령 키(Q/E)의 AI 팀원이 위치 사수 명령을 받은 상태면 완료입니다.</summary>
    HoldOrder = 7,

    /// <summary>
    /// 고른 명령 키(Q/E)의 AI 팀원이 위치 사수 명령 없이 복귀한 상태면 완료입니다. 길게 눌러 취소하든 전원 복귀 키(CapsLock)를 쓰든 같습니다.
    /// </summary>
    RecallOrder = 8,
}

/// <summary>
/// 위치 사수 조건이 볼 AI 팀원입니다. 명령 키와 같은 기준으로, 조작 중이 아닌 멤버를 목록 순서대로 Q, E에 붙입니다.
/// </summary>
/// <remarks>값은 에셋에 숫자로 저장되므로 이미 쓰는 값의 번호를 바꾸지 마세요.</remarks>
public enum TutorialOrderKey
{
    /// <summary>Q 키로 명령하는 AI 팀원입니다.</summary>
    Q = 0,

    /// <summary>E 키로 명령하는 AI 팀원입니다.</summary>
    E = 1,

    /// <summary>Q와 E의 AI 팀원 둘 다입니다.</summary>
    Both = 2,
}

/// <summary>
/// 튜토리얼 이벤트 메서드에 넘길 값 하나입니다. 파라미터 타입에 맞는 칸 하나만 씁니다.
/// </summary>
/// <remarks>
/// 에셋에는 씬 오브젝트를 넣을 수 없어서, 값은 문자열·숫자·위치(Vector3)·에셋(Sprite 등)만 받습니다. 열거형은 숫자 칸에 저장합니다.
/// 씬 오브젝트를 가리켜야 하면 그 오브젝트에 <see cref="TutorialTarget"/>을 붙이고 ID 문자열을 넘깁니다.
/// </remarks>
[Serializable]
public struct TutorialEventArg
{
    [SerializeField] private string m_string;
    [SerializeField] private float m_float;
    [SerializeField] private int m_int;
    [SerializeField] private bool m_bool;
    [SerializeField] private Vector3 m_vector;
    [SerializeField] private UnityEngine.Object m_object;

    /// <summary>파라미터 타입에 맞는 칸의 값을 꺼냅니다. 받을 수 없는 타입이면 그 타입의 기본값입니다.</summary>
    public object GetValue(Type type)
    {
        if (type == typeof(string)) return m_string ?? string.Empty;
        if (type == typeof(float)) return m_float;
        if (type == typeof(int)) return m_int;
        if (type == typeof(bool)) return m_bool;
        if (type == typeof(Vector3)) return m_vector;
        if (type.IsEnum) return Enum.ToObject(type, m_int);
        if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return m_object != null && type.IsInstanceOfType(m_object) ? m_object : null;
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}

/// <summary>
/// 트리거나 엔드포인트에 붙이는 이벤트 하나입니다. <see cref="TutorialEvents"/>의 메서드 하나와 넘길 값을 가집니다.
/// </summary>
/// <remarks>
/// 버튼의 OnClick처럼 메서드를 골라 값만 채웁니다. 메서드는 이름으로 저장하므로, 이미 쓰는 이벤트 메서드의 이름을 바꾸면
/// 에셋에서 연결이 끊깁니다(실행 시 경고). 고를 수 있는 메서드는 <see cref="TutorialEventAttribute"/>가 붙은 것뿐입니다.
/// </remarks>
[Serializable]
public sealed class TutorialAction
{
    [Tooltip("실행할 TutorialEvents 메서드 이름입니다.")]
    [SerializeField] private string m_method;

    [Tooltip("메서드에 넘길 값입니다. 파라미터 순서대로입니다.")]
    [SerializeField] private List<TutorialEventArg> m_args = new();

    /// <summary>실행할 메서드 이름입니다. 비어 있으면 아무것도 하지 않습니다.</summary>
    public string Method => m_method;

    /// <summary>메서드에 넘길 값입니다.</summary>
    public IReadOnlyList<TutorialEventArg> Args => m_args;
}

/// <summary>
/// 챕터 안의 블록 하나입니다. <see cref="TutorialBlockType"/>에 따라 쓰는 칸이 다릅니다.
/// </summary>
/// <remarks>
/// 대사: <see cref="Dialogue"/>. 트리거: <see cref="TriggerId"/>, <see cref="Actions"/>(실행할 이벤트), 조건 칸들(완료 조건).
/// 엔드포인트: <see cref="CloseTriggerIds"/>(닫을 트리거), <see cref="Actions"/>(모두 완료된 뒤 실행할 이벤트). 쓰지 않는 칸은 무시합니다.
/// </remarks>
[Serializable]
public sealed class TutorialBlock
{
    [Tooltip("블록 종류입니다. 대사 / 트리거(이벤트 실행, 완료 조건 세기 시작) / 엔드포인트(고른 트리거가 모두 완료될 때까지 기다려 닫음)")]
    [SerializeField] private TutorialBlockType m_type;

    [Tooltip("대사: 띄울 대사입니다.")]
    [SerializeField] private DialogueLine m_dialogue;

    [Tooltip("트리거: 엔드포인트가 이 트리거를 가리키는 ID입니다. Inspector가 자동으로 붙입니다.")]
    [SerializeField] private string m_triggerId;

    [Tooltip("엔드포인트: 닫을 트리거의 ID입니다. 고른 트리거가 모두 완료돼야 닫힙니다.")]
    [SerializeField] private List<string> m_closeTriggerIds = new();

    [Tooltip("트리거: 한꺼번에 실행할 이벤트입니다. 엔드포인트: 고른 트리거가 모두 완료된 뒤 실행할 이벤트입니다.\n기다리는 이벤트가 있으면 모두 끝나야 다음 블록으로 넘어갑니다.")]
    [SerializeField] private List<TutorialAction> m_actions = new();

    [Tooltip("트리거: 이 트리거가 완료로 취급되는 조건입니다. 트리거가 실행된 순간부터 셉니다.\n" +
             "None: 바로 완료 / Time: 시간 / KillEnemy: 적 처치 / ReachArea: 위치 도착 / Interact: 상호작용 완료\n" +
             "SwitchCharacter: 캐릭터 변경 / UseSkill: 스킬 사용 / HoldOrder: 위치 사수 명령 / RecallOrder: 위치 사수 복귀")]
    [SerializeField] private TutorialConditionType m_condition;

    [Tooltip("Time: 트리거가 실행된 뒤 기다릴 시간(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_seconds = 2.0f;

    [Tooltip("KillEnemy: 셀 적 프리팹입니다. 이 프리팹으로 만든 적만 셉니다(스포너가 만든 적 포함). 비우면 아무 적이나 셉니다.")]
    [SerializeField] private EnemyController m_enemyPrefab;

    [Tooltip("KillEnemy: 처치해야 하는 적 수입니다.")]
    [Min(1)]
    [SerializeField] private int m_killCount = 1;

    [Tooltip("ReachArea: 도착할 위치(월드 좌표)입니다.")]
    [SerializeField] private Vector3 m_areaCenter;

    [Tooltip("ReachArea: 도착으로 볼 반경(m)입니다. 높이는 보지 않습니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_areaRadius = 2.0f;

    [Tooltip("Interact: 대상 ID입니다. 씬 오브젝트에 붙인 TutorialTarget의 ID와 같게 적습니다. 비우면 아무 대상이나 셉니다.")]
    [SerializeField] private string m_targetId;

    [Tooltip("SwitchCharacter: 바꿔야 하는 캐릭터 번호(캐릭터 변경 키 1~3)입니다. 0이면 아무 캐릭터로든 바꾸면 완료입니다.\n" +
             "UseSkill: 스킬을 쓸 캐릭터 번호(1~3)입니다. 0이면 아무 캐릭터나 셉니다.")]
    [Range(0, 3)]
    [SerializeField] private int m_memberNumber = 1;

    [Tooltip("HoldOrder / RecallOrder: 볼 AI 팀원입니다. Q, E 키로 명령하는 팀원 또는 둘 다입니다.")]
    [SerializeField] private TutorialOrderKey m_orderKey;

    [Tooltip("시네마틱: 재생할 시네마틱의 ID입니다. 씬의 TutorialCinematicSettings 목록에서 드롭다운으로 고릅니다.")]
    [SerializeField] private string m_cinematicId;

    /// <summary>블록 종류입니다.</summary>
    public TutorialBlockType Type => m_type;

    /// <summary>대사 블록의 대사입니다.</summary>
    public DialogueLine Dialogue => m_dialogue;

    /// <summary>트리거의 ID입니다. 엔드포인트가 이 값으로 닫을 트리거를 가리킵니다.</summary>
    public string TriggerId => m_triggerId;

    /// <summary>엔드포인트가 닫을 트리거의 ID입니다.</summary>
    public IReadOnlyList<string> CloseTriggerIds => m_closeTriggerIds;

    /// <summary>트리거가 실행할 이벤트, 또는 엔드포인트가 고른 트리거가 모두 완료된 뒤 실행할 이벤트입니다.</summary>
    public IReadOnlyList<TutorialAction> Actions => m_actions;

    /// <summary>트리거가 완료로 취급되는 조건입니다.</summary>
    public TutorialConditionType Condition => m_condition;

    /// <summary>Time 조건의 대기 시간(초)입니다.</summary>
    public float Seconds => m_seconds;

    /// <summary>KillEnemy 조건에서 셀 적 프리팹입니다. null이면 아무 적이나입니다.</summary>
    public EnemyController EnemyPrefab => m_enemyPrefab;

    /// <summary>KillEnemy 조건에서 처치할 적 수입니다.</summary>
    public int KillCount => m_killCount;

    /// <summary>ReachArea 조건의 도착 위치입니다.</summary>
    public Vector3 AreaCenter => m_areaCenter;

    /// <summary>ReachArea 조건의 반경(m)입니다.</summary>
    public float AreaRadius => m_areaRadius;

    /// <summary>Interact 조건의 대상 ID입니다. 비어 있으면 아무 대상이나입니다.</summary>
    public string TargetId => m_targetId;

    /// <summary>SwitchCharacter·UseSkill 조건의 캐릭터 번호(1~3)입니다. 0이면 아무 캐릭터입니다.</summary>
    public int MemberNumber => m_memberNumber;

    /// <summary>HoldOrder·RecallOrder 조건이 볼 AI 팀원입니다.</summary>
    public TutorialOrderKey OrderKey => m_orderKey;

    /// <summary>시네마틱 블록이 재생할 시네마틱의 ID입니다(<see cref="TutorialCinematicSettings"/>의 항목).</summary>
    public string CinematicId => m_cinematicId;
}

/// <summary>
/// 튜토리얼 챕터입니다. 블록 목록과 챕터 동안 막아 둘 조작을 가집니다.
/// </summary>
/// <remarks>
/// 블록은 위에서부터 하나씩 진행합니다. 트리거는 ID를 갖고, 뒤쪽 엔드포인트가 그 ID를 골라 닫습니다.
/// 트리거 하나는 엔드포인트 하나로만 닫히고, 엔드포인트 하나는 앞에서 열린 트리거 여러 개를 함께 닫을 수 있습니다.
/// 연결 확인은 <see cref="TutorialBlockLinks.Build(IReadOnlyList{TutorialBlock})"/>가 하고, 진행기와 Inspector가 같은 결과를 씁니다.
/// </remarks>
[Serializable]
public sealed class TutorialStep
{
    [Tooltip("인스펙터에서 챕터를 구분하는 메모입니다. 게임에는 나오지 않습니다.")]
    [SerializeField] private string m_label;

    [Tooltip("이 챕터 동안 막아 둘 조작입니다. 유도하는 행동보다 앞서 다른 행동을 하지 못하게 할 때 씁니다.")]
    [SerializeField] private PlayerInputLock m_lockedInputs;

    [Tooltip("이 챕터의 블록입니다. 위에서부터 하나씩 진행합니다.\n대사 / 트리거(이벤트 실행, 완료 조건) / 엔드포인트(고른 트리거가 모두 완료될 때까지 기다려 닫음)")]
    [FormerlySerializedAs("m_lines")]
    [SerializeField] private List<TutorialBlock> m_blocks = new();

    /// <summary>인스펙터 메모입니다.</summary>
    public string Label => m_label;

    /// <summary>이 챕터 동안 막아 둘 조작입니다.</summary>
    public PlayerInputLock LockedInputs => m_lockedInputs;

    /// <summary>블록 목록입니다.</summary>
    public IReadOnlyList<TutorialBlock> Blocks => m_blocks;
}

/// <summary>
/// 챕터 블록 목록에서 어느 엔드포인트가 어느 트리거를 닫는지 셉니다.
/// </summary>
/// <remarks>
/// 엔드포인트는 자기보다 위쪽에 있고 아직 다른 엔드포인트가 닫지 않은 트리거만 닫을 수 있습니다.
/// 같은 ID의 트리거가 둘 이상이면 위쪽 것만 씁니다(블록을 복제하면 ID도 복제되므로, Inspector가 아래쪽에 새 ID를 붙입니다).
/// </remarks>
public sealed class TutorialBlockLinks
{
    /// <summary>연결이 없을 때의 값입니다.</summary>
    public const int None = -1;

    private TutorialBlockLinks(int count)
    {
        ClosedBy = new int[count];
        Closes = new List<int>[count];
        Number = new int[count];
        HasBadReference = new bool[count];
        IsDuplicateId = new bool[count];
        for (int i = 0; i < count; i++)
        {
            ClosedBy[i] = None;
            Closes[i] = new List<int>();
        }
    }

    /// <summary>트리거마다 그것을 닫는 엔드포인트 번호입니다. 닫는 엔드포인트가 없거나 트리거가 아니면 <see cref="None"/>입니다.</summary>
    public int[] ClosedBy { get; }

    /// <summary>엔드포인트마다 닫는 트리거 번호입니다. 엔드포인트가 아니면 비어 있습니다.</summary>
    public List<int>[] Closes { get; }

    /// <summary>트리거마다 챕터 안 순번(1부터)입니다. Inspector에 "#1"처럼 보입니다. 트리거가 아니면 0입니다.</summary>
    public int[] Number { get; }

    /// <summary>엔드포인트가 닫을 수 없는 ID(없는 트리거, 아래쪽 트리거, 이미 닫힌 트리거)를 가리키는지 여부입니다.</summary>
    public bool[] HasBadReference { get; }

    /// <summary>트리거의 ID가 비었거나 위쪽 트리거와 같은지 여부입니다.</summary>
    public bool[] IsDuplicateId { get; }

    /// <summary>
    /// 블록 종류, 트리거 ID, 엔드포인트가 고른 ID로 연결을 셉니다.
    /// </summary>
    public static TutorialBlockLinks Build(IReadOnlyList<TutorialBlockType> types, IReadOnlyList<string> triggerIds, IReadOnlyList<IReadOnlyList<string>> closeIds)
    {
        int count = types.Count;
        TutorialBlockLinks links = new(count);
        Dictionary<string, int> open = new();
        HashSet<string> seen = new();
        int nextNumber = 1;
        for (int i = 0; i < count; i++)
        {
            switch (types[i])
            {
                case TutorialBlockType.Trigger:
                    links.Number[i] = nextNumber++;
                    string id = triggerIds[i];
                    if (string.IsNullOrEmpty(id) || !seen.Add(id))
                    {
                        links.IsDuplicateId[i] = true;
                        break;
                    }

                    open[id] = i;
                    break;

                case TutorialBlockType.Endpoint:
                    IReadOnlyList<string> ids = closeIds[i];
                    for (int k = 0; ids != null && k < ids.Count; k++)
                    {
                        if (ids[k] != null && open.TryGetValue(ids[k], out int trigger))
                        {
                            open.Remove(ids[k]);
                            links.ClosedBy[trigger] = i;
                            links.Closes[i].Add(trigger);
                        }
                        else
                        {
                            links.HasBadReference[i] = true;
                        }
                    }

                    break;
            }
        }

        return links;
    }

    /// <summary><see cref="Build(IReadOnlyList{TutorialBlockType}, IReadOnlyList{string}, IReadOnlyList{IReadOnlyList{string}})"/>를 챕터 블록으로 부릅니다.</summary>
    public static TutorialBlockLinks Build(IReadOnlyList<TutorialBlock> blocks)
    {
        int count = blocks.Count;
        TutorialBlockType[] types = new TutorialBlockType[count];
        string[] triggerIds = new string[count];
        IReadOnlyList<string>[] closeIds = new IReadOnlyList<string>[count];
        for (int i = 0; i < count; i++)
        {
            TutorialBlock block = blocks[i];
            types[i] = block != null ? block.Type : TutorialBlockType.Dialogue;
            triggerIds[i] = block != null ? block.TriggerId : null;
            closeIds[i] = block != null ? block.CloseTriggerIds : null;
        }

        return Build(types, triggerIds, closeIds);
    }

    /// <summary>
    /// <paramref name="index"/>번 블록 자리에서 닫을 수 있는 트리거입니다. 위쪽에 있고, 닫히지 않았거나 이 자리 이후에서 닫히는 트리거입니다.
    /// </summary>
    public List<int> OpenTriggersAt(int index)
    {
        List<int> result = new();
        for (int i = 0; i < index && i < Number.Length; i++)
        {
            if (Number[i] > 0 && !IsDuplicateId[i] && (ClosedBy[i] == None || ClosedBy[i] >= index))
            {
                result.Add(i);
            }
        }

        return result;
    }
}
