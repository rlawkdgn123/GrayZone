using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 튜토리얼 챕터(<see cref="TutorialStep"/>)를 마치기 전에 채워야 하는 조건 종류입니다.
/// </summary>
/// <remarks>
/// 값은 에셋에 숫자로 저장되므로 이미 쓰는 값의 번호를 바꾸지 마세요. 새 조건은 뒤에 번호를 붙여 추가합니다.
/// </remarks>
public enum TutorialConditionType
{
    /// <summary>조건 없음입니다. 마지막 대사를 넘기면 챕터가 끝납니다.</summary>
    None = 0,

    /// <summary>마지막 대사가 뜬 뒤 정한 시간(초)이 지나면 끝납니다.</summary>
    Time = 1,

    /// <summary>
    /// 챕터가 시작된 뒤 적을 정한 수만큼 처치하면 끝납니다. 적 프리팹을 넣으면 그 프리팹으로 만든 적만, 비우면 아무 적이나 셉니다.
    /// </summary>
    KillEnemy = 2,

    /// <summary>조작 중인 캐릭터가 정한 위치의 반경 안에 들어가면 끝납니다.</summary>
    ReachArea = 3,

    /// <summary>
    /// 챕터가 시작된 뒤 상호작용을 마치면 끝납니다. 대상 ID를 넣으면 그 <see cref="TutorialTarget"/>이 붙은 대상, 비우면 아무 대상이나입니다.
    /// </summary>
    Interact = 4,
}

/// <summary>
/// 튜토리얼 이벤트를 실행하는 시점입니다.
/// </summary>
public enum TutorialActionTiming
{
    /// <summary>줄이 뜰 때 실행합니다.</summary>
    OnShow = 0,

    /// <summary>줄을 넘기고 다음 줄로 넘어갈 때 실행합니다. 챕터의 마지막 줄은 종료 조건을 채운 뒤입니다.</summary>
    OnFinish = 1,
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
/// 튜토리얼 줄에 붙이는 이벤트 하나입니다. <see cref="TutorialEvents"/>의 메서드 하나와 넘길 값을 가집니다.
/// </summary>
/// <remarks>
/// 버튼의 OnClick처럼 메서드를 골라 값만 채웁니다. 메서드는 이름으로 저장하므로, 이미 쓰는 이벤트 메서드의 이름을 바꾸면
/// 에셋에서 연결이 끊깁니다(실행 시 경고). 고를 수 있는 메서드는 <see cref="TutorialEventAttribute"/>가 붙은 것뿐입니다.
/// </remarks>
[Serializable]
public sealed class TutorialAction
{
    [Tooltip("실행 시점입니다. OnShow는 줄이 뜰 때, OnFinish는 다음 줄로 넘어갈 때입니다.")]
    [SerializeField] private TutorialActionTiming m_timing;

    [Tooltip("실행할 TutorialEvents 메서드 이름입니다.")]
    [SerializeField] private string m_method;

    [Tooltip("메서드에 넘길 값입니다. 파라미터 순서대로입니다.")]
    [SerializeField] private List<TutorialEventArg> m_args = new();

    /// <summary>실행 시점입니다.</summary>
    public TutorialActionTiming Timing => m_timing;

    /// <summary>실행할 메서드 이름입니다. 비어 있으면 아무것도 하지 않습니다.</summary>
    public string Method => m_method;

    /// <summary>메서드에 넘길 값입니다.</summary>
    public IReadOnlyList<TutorialEventArg> Args => m_args;
}

/// <summary>
/// 튜토리얼 한 줄입니다. 대사와 이 줄에서 실행할 이벤트를 가집니다.
/// </summary>
/// <remarks>
/// 줄은 대사의 넘김 방식(입력/자동)대로 넘어갑니다. 행동을 기다리는 조건은 줄이 아니라 챕터(<see cref="TutorialStep"/>)에 둡니다.
/// 대사를 비우면 아무것도 띄우지 않고 이벤트만 처리합니다. 대사 사이에 이벤트를 끼워 넣을 때 씁니다.
/// </remarks>
[Serializable]
public sealed class TutorialLine
{
    [Tooltip("띄울 대사입니다. 대사를 비우면 이벤트만 처리합니다.")]
    [SerializeField] private DialogueLine m_dialogue;

    [Tooltip("이 줄에서 실행할 이벤트입니다. 같은 시점의 이벤트는 한꺼번에 시작하고, 기다리는 이벤트가 모두 끝나야 다음으로 넘어갑니다.")]
    [SerializeField] private List<TutorialAction> m_actions = new();

    /// <summary>띄울 대사입니다.</summary>
    public DialogueLine Dialogue => m_dialogue;

    /// <summary>이 줄에서 실행할 이벤트입니다.</summary>
    public IReadOnlyList<TutorialAction> Actions => m_actions;
}

/// <summary>
/// 튜토리얼 챕터입니다. 줄 목록, 챕터 동안 막아 둘 조작, 챕터를 마치는 조건을 가집니다.
/// </summary>
/// <remarks>
/// 종료 조건이 있으면 마지막 줄의 대사를 띄워 둔 채 조건을 기다립니다. 처치·상호작용은 챕터가 시작된 순간부터 세고,
/// 시간은 마지막 줄이 뜬 뒤부터 셉니다. 줄이 하나도 없는 챕터는 조건과 함께 건너뜁니다.
/// </remarks>
[Serializable]
public sealed class TutorialStep
{
    [Tooltip("인스펙터에서 챕터를 구분하는 메모입니다. 게임에는 나오지 않습니다.")]
    [SerializeField] private string m_label;

    [Tooltip("이 챕터 동안 막아 둘 조작입니다. 유도하는 행동보다 앞서 다른 행동을 하지 못하게 할 때 씁니다.")]
    [SerializeField] private PlayerInputLock m_lockedInputs;

    [Tooltip("이 챕터의 줄 목록입니다. 위에서부터 차례로 진행합니다.")]
    [SerializeField] private List<TutorialLine> m_lines = new();

    [Header("Clear Condition")]
    [Tooltip("챕터를 마치기 전에 채워야 하는 조건입니다. 마지막 대사를 띄워 둔 채 기다립니다. None이면 마지막 대사를 넘기면 끝납니다.\n" +
             "Time: 시간 / KillEnemy: 적 처치 / ReachArea: 위치 도착 / Interact: 상호작용 완료")]
    [SerializeField] private TutorialConditionType m_condition;

    [Tooltip("Time: 대기 시간(초)입니다. 마지막 줄이 뜬 뒤부터 셉니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_seconds = 2.0f;

    [Tooltip("KillEnemy: 셀 적 프리팹입니다. 이 프리팹으로 만든 적만 셉니다(스포너가 만든 적 포함). 비우면 아무 적이나 셉니다.")]
    [SerializeField] private EnemyController m_enemyPrefab;

    [Tooltip("KillEnemy: 처치해야 하는 적 수입니다. 챕터가 시작된 뒤 처치한 수만 셉니다.")]
    [Min(1)]
    [SerializeField] private int m_killCount = 1;

    [Tooltip("ReachArea: 도착할 위치(월드 좌표)입니다.")]
    [SerializeField] private Vector3 m_areaCenter;

    [Tooltip("ReachArea: 도착으로 볼 반경(m)입니다. 높이는 보지 않습니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_areaRadius = 2.0f;

    [Tooltip("Interact: 대상 ID입니다. 씬 오브젝트에 붙인 TutorialTarget의 ID와 같게 적습니다. 비우면 아무 대상이나 셉니다.")]
    [SerializeField] private string m_targetId;

    /// <summary>인스펙터 메모입니다.</summary>
    public string Label => m_label;

    /// <summary>이 챕터 동안 막아 둘 조작입니다.</summary>
    public PlayerInputLock LockedInputs => m_lockedInputs;

    /// <summary>줄 목록입니다.</summary>
    public IReadOnlyList<TutorialLine> Lines => m_lines;

    /// <summary>챕터를 마치는 조건입니다.</summary>
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

    /// <summary>종료 조건이 있어 마지막 대사를 띄워 둔 채 기다리는지 여부입니다.</summary>
    public bool HasCondition => m_condition != TutorialConditionType.None;
}
