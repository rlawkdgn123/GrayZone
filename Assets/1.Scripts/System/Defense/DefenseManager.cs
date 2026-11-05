using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using VInspector;

/// <summary>
/// Defense 라운드의 전투/정리/휴식 진행과 스폰 포인트 제어를 관리합니다.
/// </summary>
/// <remarks>
/// 라운드 진행 규칙과 UI가 읽을 런타임 카운트만 소유합니다. 무엇이 나올지는 방어전 SO와 스포너가 정합니다.
///
/// 한 웨이브는 전투 → 정리 → 휴식 순서입니다. 전투 시간이 끝나면 새 스폰만 멈추고, 이 매니저의 스포너가 내보낸
/// 살아 있는 적이 0이 되면 휴식으로 넘어갑니다(함정 설치가 휴식에만 가능해서, 적이 남은 채 넘어가면 전투와 설치를
/// 동시에 해야 하기 때문입니다). 마지막 웨이브는 정리가 끝나면 휴식 없이 귀환 구역을 엽니다.
///
/// 무엇이 나올지는 방어전 SO가 정하고, 방어전 SO는 입장할 때 회차 표에서 정해집니다(<see cref="DefenseSceneDataManager.DefenseStage"/>). 이 매니저는 전투가 시작될 때마다 방어전 스포너(<see cref="EnemyDefenseSpawnPoint"/>)에
/// 공격로 이름별 웨이브 SO를 넘겨 실행시키고, 총 웨이브 수도 방어전 SO를 따릅니다. 방어전 SO가 없으면 시작하지 않습니다.
/// 전투·휴식 시간은 이 매니저 값을 모든 웨이브, 모든 공격로에 적용합니다.
/// 설계 근거: privateDoc `DEFENSE_WAVE_SPAWN_SPEC_KR.md` §4.4, §5.
///
/// 방어전 씬의 씬 컨트롤러이기도 합니다. 하위 매니저(이펙트·오디오·적·인벤토리) 연결, 입력 모드 전환,
/// 게임오버 화면 표시는 부모 <see cref="CombatSceneManager"/>가 맡습니다. 필드 씬의 FieldManager와 같은 자리입니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class DefenseManager : CombatSceneManager
{
    /// <summary>현재 방어전 씬의 인스턴스입니다. 방어전 씬이 아니면 <c>null</c>입니다.</summary>
    public static new DefenseManager Instance => CombatSceneManager.Instance as DefenseManager;

    /// <inheritdoc />
    /// <remarks>방어전 맵에는 소음 차폐를 쓰지 않으므로 NoiseManager가 없어도 경고하지 않습니다.</remarks>
    protected override bool UsesNoiseManager => false;

    /// <inheritdoc />
    /// <remarks>방어전에서는 정문으로 가는 적도 막아야 하므로, 스쿼드와 교전하지 않은 적이라도 보이면 바로 노립니다.</remarks>
    public override SquadEnemyAwareness SquadEnemyAwareness => SquadEnemyAwareness.AnyVisible;

    /// <summary>Shooter 업그레이드 한 레벨에서 함께 배치되는 좌우 지정사수 한 쌍입니다.</summary>
    /// <remarks>
    /// 기획 기준(2026-09-29): 레벨마다 좌우 한 명씩, 2명이 추가됩니다. 1레벨은 좌우 2명(A_01, B_01),
    /// 2레벨은 좌우 4명(A_01, B_01, A_02, B_02)입니다. 네 명 모두 같은 지정사수 기능(사격·장전·애니메이션)을 쓰고
    /// 모델만 다를 예정이므로, 칸에는 <see cref="DefenseMarksmanController"/>가 붙은 NPC를 넣습니다.
    /// </remarks>
    [Serializable]
    public struct MarksmanTier
    {
        [Tooltip("이 레벨에서 켤 왼쪽 지정사수입니다. 비어 있으면 무시합니다.")]
        public GameObject Left;

        [Tooltip("이 레벨에서 켤 오른쪽 지정사수입니다. 비어 있으면 무시합니다.")]
        public GameObject Right;
    }


    [Header("Defense Round")]
    [Tooltip("전투 구간 시간(초)입니다. 모든 웨이브, 모든 공격로에 같이 적용합니다. 이 시간이 끝나면 새 스폰만 멈추고 남은 적 정리로 넘어갑니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_roundDuration = 50.0f;

    [Tooltip("남은 적을 모두 정리한 뒤 다음 전투까지의 휴식 시간(초)입니다. 함정은 이 구간에만 설치할 수 있습니다. 마지막 웨이브 뒤에는 휴식이 없습니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_restDuration = 30.0f;

    [Tooltip("모든 공격로를 합쳐 동시에 살아 있을 수 있는 방어전 적의 상한입니다. 그룹이 남은 자리보다 크면 위험도가 낮은 적부터 가능한 수만 생성합니다.")]
    [Min(1)]
    [SerializeField] private int m_maxActiveEnemies = 35;

    [Tooltip("이 매니저가 웨이브를 실행시킬 방어전 스포너 목록입니다. 남은 적 수도 이 목록의 스포너가 내보낸 적만 셉니다. 비어 있는 항목은 무시합니다.")]
    [SerializeField] private List<EnemyDefenseSpawnPoint> m_spawnPoints = new List<EnemyDefenseSpawnPoint>();

    [Header("Victory Return Zone")]
    [Tooltip("씬에 미리 배치해 둔 귀환 구역입니다. 평소에는 꺼 두고, 마지막 웨이브를 막으면 켭니다. 위치는 씬에서 이 오브젝트를 직접 옮겨 정합니다.")]
    [SerializeField] private GameObject m_returnPoint;

    [Tooltip("웨이브 진행과 방어전 결과를 기록할 방어전 데이터 매니저입니다. 비워 두면 런타임에 찾습니다.")]
    [SerializeField] private DefenseSceneDataManager m_defenseSceneDataManager;

    [Header("Marksman Placement")]
    [Tooltip("Shooter 업그레이드 레벨별 지정사수 배치입니다. 첫 칸이 1레벨, 둘째 칸이 2레벨입니다. " +
             "레벨이 N이면 1~N번째 칸의 좌우 지정사수를 모두 켜고 나머지는 끕니다. " +
             "비워 두면 이 매니저는 지정사수를 건드리지 않습니다.")]
    [SerializeField] private MarksmanTier[] m_marksmanTiers = Array.Empty<MarksmanTier>();

    [Header("Defense HUD")]
    [Tooltip("전투 또는 휴식의 남은 시간을 분:초로 표시할 텍스트입니다. 비어 있으면 타이머 표시는 생략합니다.")]
    [SerializeField] private TMP_Text m_timerText;

    [Tooltip("지금이 전투 구간인지 휴식 구간인지 표시할 텍스트입니다. 비어 있으면 구간 표시는 생략합니다.")]
    [SerializeField] private TMP_Text m_phaseLabelText;

    [Tooltip("남은 웨이브 수를 표시할 텍스트입니다. 비어 있으면 남은 웨이브 표시는 생략합니다.")]
    [SerializeField] private TMP_Text m_remainingWaveText;

    [Tooltip("남은 웨이브 표시 형식입니다. {0}에 남은 수, {1}에 전체 수가 들어갑니다.")]
    [SerializeField] private string m_remainingWaveFormat = "남은 라운드 {0}";

    [Tooltip("전투 구간에 표시할 문구입니다.")]
    [SerializeField] private string m_combatPhaseLabel = "전투시간";

    [Tooltip("휴식 구간에 표시할 문구입니다.")]
    [SerializeField] private string m_restPhaseLabel = "휴식시간";

    [Tooltip("전투 시간이 끝나고 남은 적을 정리하는 동안 구간 라벨에 표시할 형식입니다. {0}에 남은 적 수가 들어갑니다.")]
    [SerializeField] private string m_clearingPhaseLabelFormat = "남은 적 {0}";

    [Tooltip("웨이브 시작 알림을 표시할 텍스트입니다. 비어 있으면 시작 알림은 생략합니다.")]
    [SerializeField] private TMP_Text m_waveStartMessageText;

    [Tooltip("웨이브 시작 알림의 투명도를 제어할 CanvasGroup입니다. 비어 있으면 텍스트 알파만 바꿉니다.")]
    [SerializeField] private CanvasGroup m_waveStartMessageCanvasGroup;

    [Tooltip("웨이브 시작 때 표시할 알림 문구입니다.")]
    [SerializeField] private string m_waveStartMessage = "전투가 시작됩니다";

    [Tooltip("정비 구간 시작 때 표시할 알림 문구입니다.")]
    [SerializeField] private string m_restStartMessage = "정비 시간이 시작됩니다";

    [Tooltip("웨이브 시작 알림이 불투명하게 유지되는 시간(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_waveStartMessageHoldDuration = 0.5f;

    [Tooltip("웨이브 시작 알림이 완전히 사라질 때까지 페이드아웃하는 시간(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_waveStartMessageFadeDuration = 1.0f;

    [Header("Clearing Safety")]
    [Tooltip("정리 구간이 시작되고 이 시간(초)이 지나도 적이 남아 있으면, 남은 적을 모두 플레이어 우선으로 바꿔 지금 조작 중인 캐릭터 쪽으로 끌어옵니다. 0이면 끌어오지 않습니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_clearingPullDelay = 20.0f;

    [Tooltip("정리 구간이 시작되고 이 시간(초)이 지나면, 카메라에 보이지 않는 남은 적을 풀로 되돌립니다. 처치로 세지 않습니다. 0이면 회수하지 않습니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_clearingOffscreenDespawnDelay = 40.0f;

    [Tooltip("정리 구간이 시작되고 이 시간(초)이 지나면, 보이는지와 관계없이 남은 적을 모두 풀로 되돌려 정리를 끝냅니다. 보이는 곳에 갇힌 적 때문에 진행이 멈추는 것을 막습니다. 0이면 강제로 끝내지 않습니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_clearingForceEndDelay = 60.0f;

    [Header("Defense Start Input")]
    [Tooltip("방어전 시작 전, 현재 스쿼드 조작 멤버가 Z키를 이 시간(초)만큼 누르고 있으면 방어전을 시작합니다. 실제 시간으로 셉니다. " +
             "튜토리얼 여부와 관계없이 매 방어전 같은 규칙이고, 튜토리얼 중에는 마지막 페이지가 같은 규칙으로 시작을 맡습니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_emptySpaceHoldStartDuration = 3.0f;

    [Tooltip("켜면 방어전을 정비(휴식) 구간부터 시작합니다. 튜토리얼이 있으면 튜토리얼이 끝난 뒤, 없으면 씬에 들어오자마자 정비 시간이 흐릅니다. " +
             "정비 구간마다 Z를 위 시간만큼 누르고 있으면 남은 정비 시간을 건너뛰고 바로 공세를 시작합니다. " +
             "끄면 기존처럼 시작 전에 Z 홀드로 첫 공세를 바로 시작하고, 정비 구간은 건너뛸 수 없습니다.")]
    [SerializeField] private bool m_beginWithMaintenance;

    /// <summary>방어전 시작 홀드 키입니다. 튜토리얼 마지막 페이지 안내(Z 3초)와 같은 키입니다.</summary>
    private const Key StartHoldKey = Key.Z;

    [Foldout("Start Prompt")]
    [Tooltip("튜토리얼 없이 시작하는 판(2회차부터)에서 시작 안내를 띄울 튜토리얼 창입니다. 아래쪽을 잘라낸 압축 크기로 띄우고, " +
             "Z를 누르는 동안 키 게이지가 찹니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private DefenseTutorialOverlay m_startPromptOverlay;

    [Tooltip("시작 안내 창의 제목입니다. 비우면 튜토리얼 창에 적힌 제목을 그대로 둡니다.")]
    [SerializeField] private string m_startPromptTitle = string.Empty;

    [TextArea(2, 4)]
    [Tooltip("시작 안내 본문입니다. 튜토리얼 마지막 페이지의 시작 안내 부분과 같은 문구이며, 키 아이콘 자리만큼 공백을 둡니다.")]
    [SerializeField] private string m_startPromptBody = "모든 정비를 마치셨으면        를 3초간\n눌러 시작합니다.";

    [Tooltip("시작 안내 하단 문구입니다. 오른쪽에 Z 키 게이지가 붙습니다.")]
    [SerializeField] private string m_startPromptHint = "시작하려면";

    [Tooltip("본문에 끼워 넣을 Z 키 아이콘입니다. 튜토리얼 마지막 페이지와 같은 이미지를 씁니다.")]
    [SerializeField] private Sprite m_startPromptKeyIcon;

    [Tooltip("Z 키 아이콘 위치(px)입니다. 패널 왼쪽 위 기준이고 y는 아래쪽이 양수입니다.")]
    [SerializeField] private Vector2 m_startPromptKeyIconPosition = new Vector2(279.0f, 99.0f);

    [Tooltip("Z 키 아이콘 크기(px)입니다.")]
    [SerializeField] private Vector2 m_startPromptKeyIconSize = new Vector2(30.0f, 30.0f);

    [Foldout("Mission Text")]
    [Tooltip("좌상단 목표 패널의 제목 텍스트입니다. 비어 있으면 설명 텍스트와 같은 패널의 Title 오브젝트에서 찾습니다.")]
    [SerializeField] private TMP_Text m_missionTitleText;

    [Tooltip("전투 중 목표 패널 제목 문구입니다. 비어 있으면 바꾸지 않습니다.")]
    [SerializeField] private string m_combatMissionTitle = "외곽 방어선을 지키십시오";

    [Tooltip("좌상단 목표 패널의 설명 텍스트입니다. 전투 중에는 아래 문구로 바꾸고, 휴식·시작 전에는 원래 문구로 돌려놓습니다. 비어 있으면 바꾸지 않습니다.")]
    [SerializeField] private TMP_Text m_missionDescriptionText;

    [Tooltip("전투 중 목표 패널 설명 문구입니다.")]
    [SerializeField] private string m_combatMissionDescription = "몰려드는 적을 저지하십시오.";
    [EndFoldout]

    /// <summary>Defense 게임이 시작될 때 발생합니다. 튜토리얼 안내처럼 시작 시점에 붙는 UI가 구독합니다.</summary>
    /// <remarks>
    /// <see cref="StartDefenseGame"/>를 다시 부르면 재시작으로 보고 매번 발생합니다.
    /// 구독자가 중복 표시를 원하지 않으면 자기 쪽에서 한 번만 처리하도록 판단합니다.
    /// </remarks>
    public event Action OnDefenseStarted;

    /// <summary>플레이 라운드가 끝나고 휴식 구간이 시작될 때 발생합니다.</summary>
    /// <remarks>
    /// 휴식 동안에만 할 수 있는 정비 행동이 구독합니다. 예를 들어 다 쓴 함정은 이 시점에 다시
    /// 설치할 수 있게 됩니다. 마지막 웨이브 뒤에는 휴식으로 넘어가지 않으므로 발생하지 않습니다.
    /// </remarks>
    public event Action OnRestStarted;

    /// <summary>마지막 웨이브를 막아 귀환 구역이 준비되었을 때 발생합니다.</summary>
    public event Action OnDefenseVictoryReady;

    /// <summary>게임이 시작되어 라운드 매니저가 타이머를 갱신 중인지 여부입니다.</summary>
    private bool m_isGameStarted;

    /// <summary>거점 파괴나 스쿼드 전멸로 게임오버가 되어 진행을 멈춘 상태인지 여부입니다.</summary>
    private bool m_isGameOver;

    /// <summary>현재 라운드 플레이 구간인지 여부입니다. false이면 정리 또는 휴식 구간입니다.</summary>
    private bool m_isPlaying;

    /// <summary>전투 시간이 끝나 새 스폰을 멈추고, 남은 적이 0이 되기를 기다리는 구간인지 여부입니다.</summary>
    private bool m_isClearing;

    /// <summary>정리 구간 라벨을 마지막으로 갱신할 때의 남은 적 수입니다. 바뀔 때만 다시 그리기 위해서입니다.</summary>
    private int m_lastShownRemainingEnemyCount = -1;

    /// <summary>이번 정리 구간이 시작된 뒤 지난 시간(초)입니다.</summary>
    private float m_clearingElapsed;

    /// <summary>이번 정리 구간에서 남은 적 끌어오기를 이미 했는지 여부입니다.</summary>
    private bool m_clearingPullApplied;

    /// <summary>화면 밖 회수를 다음에 검사할 정리 경과 시간(초)입니다. 매 프레임 검사하지 않기 위해서입니다.</summary>
    private float m_nextOffscreenCheckTime;

    /// <summary>안전장치가 남은 적을 모을 때 쓰는 재사용 목록입니다.</summary>
    private readonly List<EnemyController> m_clearingEnemyBuffer = new List<EnemyController>();

    /// <summary>화면 밖 회수를 검사하는 간격(초)입니다.</summary>
    private const float OffscreenCheckInterval = 1.0f;

    /// <summary>마지막 웨이브를 완료하여 귀환 구역 진입만 기다리는 상태인지 여부입니다.</summary>
    private bool m_isVictoryReady;

    /// <summary>현재 라운드에 남은 시간(초)입니다.</summary>
    private float m_roundTimer;

    /// <summary>현재 휴식 시간에 남은 시간(초)입니다.</summary>
    private float m_restTimer;

    /// <summary>웨이브 시작 알림의 남은 표시 시간(초)입니다.</summary>
    private float m_waveStartMessageRemaining;

    /// <summary>허공 상호작용키를 연속해서 누른 시간(초)입니다.</summary>
    private float m_emptySpaceHoldStartTimer;

    /// <summary>
    /// 정비 건너뛰기 홀드를 세기 전에 Z를 한 번 떼야 하는지 여부입니다.
    /// 튜토리얼 마지막 페이지를 Z 홀드로 넘기면 그 누름이 이어져 정비가 곧바로 건너뛰어지는 것을 막습니다.
    /// </summary>
    private bool m_maintenanceSkipNeedsRelease;

    /// <summary>정비 시작 모드에서 자동 시작을 이미 시도했는지 여부입니다. 방어전 구성이 없을 때 매 프레임 오류를 내지 않기 위해서입니다.</summary>
    private bool m_autoStartAttempted;

    /// <summary>전투 문구로 바꾸기 전 목표 패널 제목입니다. null이면 아직 바꾸지 않은 상태입니다.</summary>
    private string m_missionTitleOriginalText;

    /// <summary>전투 문구로 바꾸기 전 목표 패널 설명입니다. null이면 아직 바꾸지 않은 상태입니다.</summary>
    private string m_missionDescriptionOriginalText;

    /// <summary>현재 실행 중인 웨이브 번호입니다. 첫 웨이브는 1입니다.</summary>
    private int m_currentWave;

    /// <summary>함정 재료 클리어 보상을 마지막으로 지급한 웨이브 번호입니다.</summary>
    /// <remarks>정리 완료가 디버그 경로로 중복 호출되어도 같은 웨이브 보상을 두 번 주지 않기 위해 씁니다.</remarks>
    private int m_lastRewardedWave;


    /// <summary>Defense 게임이 시작되었는지 여부입니다.</summary>
    public bool IsGameStarted => m_isGameStarted;

    /// <summary>현재 적 스폰이 허용된 라운드 플레이 구간인지 여부입니다.</summary>
    public bool IsPlaying => m_isGameStarted && m_isPlaying;

    /// <summary>마지막 웨이브 완료 후 귀환 구역 진입을 기다리는 상태인지 여부입니다.</summary>
    public bool IsVictoryReady => m_isGameStarted && m_isVictoryReady;

    /// <summary>현재 진행 중이거나 마지막으로 시작한 웨이브 번호입니다.</summary>
    public int CurrentWave => m_currentWave;

    /// <summary>이 방어전의 총 웨이브 수입니다. 방어전 SO가 없으면 0입니다.</summary>
    public int TotalWaveCount => m_activeStage != null ? m_activeStage.TotalWaveCount : 0;

    /// <summary>이번 방어전 구성입니다.</summary>
    public DefenseStageSO DefenseStage => m_activeStage;

    /// <summary>이번 방어전 구성입니다. 입장 데이터에서 가져오며, 방어전을 시작할 때마다 다시 읽습니다.</summary>
    private DefenseStageSO m_activeStage;

    /// <summary>현재 라운드에 남은 시간(초)입니다. 플레이 구간이 아니면 0입니다.</summary>
    public float RoundTimer => IsPlaying ? m_roundTimer : 0.0f;

    /// <summary>현재 휴식에 남은 시간(초)입니다. 휴식 구간이 아니면 0입니다.</summary>
    public float RestTimer => IsResting ? m_restTimer : 0.0f;

    /// <summary>모든 공격로를 합친 방어전 생존 적 상한입니다.</summary>
    public int MaxActiveEnemies
    {
        get
        {
            int stageLimit = m_activeStage != null ? m_activeStage.MaxActiveEnemies : 0;
            return stageLimit > 0 ? stageLimit : Mathf.Max(1, m_maxActiveEnemies);
        }
    }

    /// <summary>현재 전역 상한 안에서 추가로 생성할 수 있는 적 수입니다.</summary>
    public int AvailableEnemySpawnSlots => Mathf.Max(0, MaxActiveEnemies - CountManagedLiveEnemies());

    /// <summary>전투 시간이 끝나 남은 적을 정리하는 구간인지 여부입니다.</summary>
    public bool IsClearing => m_isGameStarted && m_isClearing;

    /// <summary>휴식 구간인지 여부입니다. 방어전 시작 전도 포함합니다.</summary>
    /// <remarks>
    /// 방어전 시작 전(시작 홀드를 기다리는 시간)도 휴식으로 취급합니다(사용자 확정 2026-10-01). 그 시간에도 함정을 설치할 수 있어야
    /// 하기 때문입니다. 시작 전에는 휴식 타이머가 돌지 않으므로 <see cref="RestTimer"/>는 0입니다.
    /// </remarks>
    public bool IsResting => !m_isGameOver && !m_isPlaying && !m_isClearing && !m_isVictoryReady;

    /// <summary>거점 파괴나 스쿼드 전멸로 게임오버가 되어 진행을 멈췄는지 여부입니다.</summary>
    public bool IsGameOver => m_isGameOver;

    /// <summary>
    /// 지금 함정을 설치할 수 있는 구간인지 여부입니다. 휴식 구간(방어전 시작 전 포함)입니다.
    /// </summary>
    /// <remarks>
    /// 전투, 남은 적 정리, 승리 뒤에는 닫힙니다. 상시 설치 함정(재설치 정책 Always)은 이 값과 관계없이 설치할 수 있습니다.
    /// 구간이 바뀔 때마다 씬의 함정에 알립니다(<see cref="Trap.SetBuildWindowOpen"/>).
    /// </remarks>
    public bool IsTrapBuildWindowOpen => IsResting;

    /// <summary>설치 구간을 알릴 씬의 함정 목록입니다. 처음 알릴 때 한 번 찾습니다.</summary>
    private Trap[] m_traps;

    /// <summary>이 매니저의 스포너가 내보내 아직 살아 있는 적 수입니다. 시체는 세지 않습니다.</summary>
    public int RemainingEnemyCount => CountManagedLiveEnemies();

    /// <summary>이 매니저가 제어하는 스폰 포인트 목록입니다.</summary>
    public IReadOnlyList<EnemyDefenseSpawnPoint> SpawnPoints => m_spawnPoints;

    /// <summary>방어전 시작 전 Z키 홀드 진행도입니다. 정비 시작 모드에서는 정비 건너뛰기 홀드 진행도입니다.</summary>
    public float EmptySpaceHoldStartProgress => IsStartHoldAvailable
        ? Mathf.Clamp01(m_emptySpaceHoldStartTimer / m_emptySpaceHoldStartDuration)
        : 0.0f;

    /// <summary>방어전을 정비 구간부터 시작하고, 정비 구간을 Z 홀드로 건너뛸 수 있는지 여부입니다.</summary>
    public bool BeginsWithMaintenance => m_beginWithMaintenance;

    /// <summary>
    /// 지금 Z 홀드가 의미 있는 구간인지 여부입니다. 기본 모드는 시작 전, 정비 시작 모드는 시작 뒤 정비 구간입니다.
    /// </summary>
    private bool IsStartHoldAvailable => !m_isGameOver && (m_beginWithMaintenance
        ? m_isGameStarted && IsResting
        : !m_isGameStarted);

    protected override void Awake()
    {
        base.Awake();

        // 부모가 중복으로 판단해 이 컴포넌트를 지웠으면 더 진행하지 않습니다.
        if (CombatSceneManager.Instance != this)
        {
            return;
        }

        // 시작 전 상태는 조작을 막지 않되, 이 매니저가 소유한 적 생산만 확실히 차단합니다.
        m_isGameOver = false;
        m_isGameStarted = false;
        m_isPlaying = false;
        m_isClearing = false;
        m_isVictoryReady = false;
        m_roundTimer = 0.0f;
        m_restTimer = 0.0f;
        m_emptySpaceHoldStartTimer = 0.0f;
        m_currentWave = 0;
        m_lastRewardedWave = 0;
        RefreshTimerText();
        HideWaveStartMessage();
    }

    protected override void Start()
    {
        // 부모가 게임오버 요청(거점 파괴·스쿼드 전멸) 구독을 붙입니다.
        base.Start();

        // DefenseSceneDataManager는 실행 순서상 먼저 Start까지 마쳐 입장 데이터를 갖고 있습니다.
        if (ResolveActiveStage() != null)
        {
            ResolveDefenseSceneDataManager()?.ConfigureWaves(TotalWaveCount);
        }
        else
        {
            Debug.LogError("[DefenseManager] 이번 방어전 구성(방어전 SO)을 정하지 못했습니다. DefenseSceneDataManager의 Defense Schedule을 확인하세요. 이대로는 방어전을 시작할 수 없습니다.", this);
        }

        ApplyMarksmanPlacement();
    }

    /// <summary>
    /// 이번 방어전 구성을 입장 데이터에서 가져옵니다.
    /// </summary>
    /// <remarks>
    /// 방어전 SO는 이 매니저가 들고 있지 않습니다. 회차마다 달라야 하므로, 입장할 때 <see cref="DefenseSceneDataManager"/>가
    /// 회차 표에서 골라 입장 데이터에 고정한 값을 씁니다(<see cref="DefenseSceneDataManager.DefenseStage"/>).
    /// </remarks>
    private DefenseStageSO ResolveActiveStage()
    {
        DefenseSceneDataManager defenseData = ResolveDefenseSceneDataManager();
        m_activeStage = defenseData != null ? defenseData.DefenseStage : null;
        return m_activeStage;
    }

    private void Update()
    {
        // 게임오버 뒤에는 휴식·시작 홀드·다음 웨이브로 넘어가지 않습니다.
        if (m_isGameOver)
        {
            return;
        }

        UpdateWaveStartMessage();
        RefreshMissionText();

        if (!m_isGameStarted)
        {
            if (m_beginWithMaintenance)
            {
                TryAutoStartMaintenance();
            }
            else
            {
                UpdateEmptySpaceHoldStart();
            }

            RefreshStartPrompt();
            return;
        }

        RefreshStartPrompt();

        if (m_isVictoryReady)
        {
            return;
        }

        if (m_isClearing)
        {
            UpdateClearing();
            return;
        }

        float remainingDelta = Time.deltaTime;
        if (m_isPlaying)
        {
            if (m_roundTimer > remainingDelta)
            {
                m_roundTimer -= remainingDelta;
                RefreshTimerText();
                return;
            }

            // 전투 시간이 끝나도 바로 휴식으로 가지 않습니다. 남은 적 수는 다음 프레임부터 정리 구간이 셉니다.
            m_roundTimer = 0.0f;
            BeginClearing();
            return;
        }

        if (m_beginWithMaintenance && UpdateMaintenanceSkipHold())
        {
            return;
        }

        if (m_restTimer > remainingDelta)
        {
            m_restTimer -= remainingDelta;
            RefreshTimerText();
            return;
        }

        m_restTimer = 0.0f;
        BeginRound();
    }

    /// <summary>Defense 게임을 시작하고 첫 웨이브 타이머를 시작합니다.</summary>
    /// <remarks>이미 실행 중이면 현재 웨이브와 휴식 카운트를 초기화하고 첫 웨이브부터 다시 시작합니다.</remarks>
    [ContextMenu("Start Defense")]
    public void StartDefense()
    {
        if (ResolveActiveStage() == null)
        {
            Debug.LogError("[DefenseManager] 이번 방어전 구성(방어전 SO)이 없어 방어전을 시작하지 않습니다. DefenseSceneDataManager의 Defense Schedule을 확인하세요.", this);
            return;
        }

        m_isGameOver = false;
        m_isGameStarted = true;
        m_isClearing = false;
        m_isVictoryReady = false;
        m_emptySpaceHoldStartTimer = 0.0f;
        m_currentWave = 0;
        m_lastRewardedWave = 0;
        SetReturnPointActive(false);
        // 재시작이면 앞 판의 웨이브 기록을 비우고 다시 시작합니다.
        ResolveDefenseSceneDataManager()?.ConfigureWaves(TotalWaveCount);
        PrepareStageSpawners();
        if (m_beginWithMaintenance)
        {
            // 첫 공세 전에 정비 시간을 줍니다.
            BeginRest();
        }
        else
        {
            BeginRound();
        }

        RefreshStartPrompt();
        OnDefenseStarted?.Invoke();
    }

    /// <summary>기존 Inspector 및 외부 호출 호환성을 위해 Defense 시작을 전달합니다.</summary>
    [ContextMenu("Start Defense Game")]
    public void StartDefenseGame()
    {
        StartDefense();
    }

    /// <summary>Defense 게임을 중지하고 이 매니저가 제어한 스포너를 끕니다.</summary>
    [ContextMenu("Stop Defense Game")]
    public void StopDefenseGame()
    {
        m_isGameStarted = false;
        m_isPlaying = false;
        m_isClearing = false;
        m_isVictoryReady = false;
        m_roundTimer = 0.0f;
        m_restTimer = 0.0f;
        m_emptySpaceHoldStartTimer = 0.0f;
        m_currentWave = 0;
        m_lastRewardedWave = 0;
        SetReturnPointActive(false);
        EndStageCombat();
        RefreshTimerText();
        HideWaveStartMessage();
        RefreshTrapBuildWindow();
    }

    /// <summary>
    /// 거점 파괴나 스쿼드 전멸로 게임오버가 되면 게임오버 화면을 띄우고 방어전 진행을 그 자리에서 멈춥니다.
    /// </summary>
    /// <remarks>
    /// <see cref="StopDefenseGame"/>을 쓰지 않는 이유: 그 함수는 시작 전 상태로 되돌려, 함정 설치 구간이 다시 열리고
    /// 빈 곳 홀드로 방어전이 다시 시작될 수 있습니다. 여기서는 새 스폰만 멈추고 이후 진행을 막습니다.
    /// </remarks>
    protected override void HandleGameOverRequested()
    {
        if (!m_isGameOver)
        {
            m_isGameOver = true;
            m_isPlaying = false;
            m_isClearing = false;
            m_roundTimer = 0.0f;
            m_restTimer = 0.0f;
            m_emptySpaceHoldStartTimer = 0.0f;
            EndStageCombat();
            HideWaveStartMessage();
            RefreshTimerText();
            RefreshTrapBuildWindow();

            // 정비 중 게임오버가 나면 Update가 더 돌지 않으므로 여기서 안내를 내립니다.
            RefreshStartPrompt();
        }

        base.HandleGameOverRequested();
    }

    /// <summary>
    /// 지금 구간이 함정 설치 구간인지 씬의 모든 함정에 알립니다.
    /// </summary>
    /// <remarks>
    /// 상시 설치가 아닌 함정은 설치 구간에만 청사진을 보이고 설치를 받습니다. 이미 설치된 함정은 그대로 둡니다.
    /// 진행 중에 켜진 함정은 스스로 <see cref="IsTrapBuildWindowOpen"/>을 읽어 맞춥니다.
    /// </remarks>
    private void RefreshTrapBuildWindow()
    {
        // 부를 때마다 다시 찾습니다. 화염병·드럼통 불처럼 진행 중에 생기는 함정이 있어, 처음 목록만 쓰면 그것들은
        // 구간 변화를 받지 못합니다. 구간이 바뀔 때만 불리므로 비용은 작습니다.
        m_traps = FindObjectsByType<Trap>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        bool isOpen = IsTrapBuildWindowOpen;
        for (int i = 0; i < m_traps.Length; i++)
        {
            if (m_traps[i] != null)
            {
                m_traps[i].SetBuildWindowOpen(isOpen);
            }
        }
    }

    [Foldout("Debug")]
    [Button("라운드 스킵")]
    /// <summary>
    /// 지금 구간의 남은 시간을 무시하고 다음 구간으로 넘어갑니다.
    /// </summary>
    /// <remarks>
    /// 검증용입니다. 전투 중이면 정리로, 정리 중이면 휴식으로(마지막 웨이브였다면 승리로), 휴식 중이면 다음 전투로
    /// 넘어갑니다. 적을 치우는 옵션이 켜져 있으면 전투를 넘길 때 정리도 곧바로 끝납니다.
    /// 타이머가 자연히 끝났을 때와 같은 경로를 타므로 <see cref="OnRestStarted"/> 같은 알림도 똑같이
    /// 발생합니다. 따로 처리했다면 버튼으로 넘어갈 때와 시간으로 넘어갈 때가 달라져 검증이 무의미해집니다.
    ///
    /// 방어전이 시작되지 않았거나 이미 승리 상태면 아무 일도 하지 않습니다.
    /// </remarks>
    public void SkipRound()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[DefenseManager] 라운드 스킵은 Play Mode에서만 동작합니다.", this);
            return;
        }

        if (!m_isGameStarted)
        {
            Debug.LogWarning("[DefenseManager] 방어전이 시작되지 않아 건너뛸 라운드가 없습니다.", this);
            return;
        }

        if (m_isVictoryReady)
        {
            Debug.LogWarning("[DefenseManager] 이미 마지막 웨이브를 끝낸 상태라 건너뛸 라운드가 없습니다.", this);
            return;
        }

        if (m_debugSkipClearsEnemies)
        {
            int despawnedCount = DespawnManagedEnemies();
            Debug.Log($"[DefenseManager] 라운드 스킵: 남은 적 {despawnedCount}마리를 치웠습니다.", this);
        }

        if (m_isPlaying)
        {
            m_roundTimer = 0.0f;
            BeginClearing();
            return;
        }

        if (m_isClearing)
        {
            FinishClearing();
            return;
        }

        m_restTimer = 0.0f;
        BeginRound();
    }

    [Foldout("Debug")]
    [Tooltip("켜면 라운드 스킵이 필드에 남은 적을 함께 치웁니다. 끄면 적을 둔 채로 구간만 넘깁니다. 휴식으로 넘어갔는데 지난 전투의 적이 돌아다니는 상태를 피하려면 켜 두세요.")]
    [SerializeField] private bool m_debugSkipClearsEnemies = true;

    [Foldout("Debug")]
    [Tooltip("0 이상이면 Shooter 업그레이드 레벨 대신 이 값으로 지정사수를 배치합니다. -1이면 입장 데이터 값을 씁니다. " +
             "셸터를 거치지 않고 이 씬만 실행해 배치를 확인할 때 씁니다.")]
    [Min(-1)]
    [SerializeField] private int m_debugMarksmanLevelOverride = -1;

    /// <summary>
    /// 이 매니저가 제어하는 스폰 포인트가 내보낸 적을 모두 풀로 되돌립니다.
    /// </summary>
    /// <returns>치운 적의 수입니다.</returns>
    /// <remarks>
    /// 죽이는 것이 아니라 없던 일로 하는 것이라 처치 수가 오르지 않습니다. 목록 밖 스폰 포인트가
    /// 내보낸 적은 이 매니저 소유가 아니므로 건드리지 않습니다.
    /// </remarks>
    private int DespawnManagedEnemies()
    {
        if (m_spawnPoints == null)
        {
            return 0;
        }

        int despawnedCount = 0;
        for (int i = 0; i < m_spawnPoints.Count; i++)
        {
            EnemyDefenseSpawnPoint spawnPoint = m_spawnPoints[i];
            if (spawnPoint == null)
            {
                continue;
            }

            despawnedCount += spawnPoint.DespawnActiveEnemies();
        }

        return despawnedCount;
    }

    /// <summary>다음 플레이 라운드를 시작합니다.</summary>
    private void BeginRound()
    {
        m_currentWave++;
        m_isPlaying = true;
        m_isClearing = false;
        m_roundTimer = Mathf.Max(0.01f, m_roundDuration);
        m_restTimer = 0.0f;
        BeginStageCombat();
        RefreshTrapBuildWindow();
        RefreshTimerText();
        ShowPhaseStartMessage(m_waveStartMessage);
        ResolveDefenseSceneDataManager()?.RecordWaveStarted(m_currentWave);
    }

    /// <summary>전투 시간을 끝내고 새 스폰을 멈춘 뒤, 남은 적 정리 구간을 시작합니다.</summary>
    private void BeginClearing()
    {
        m_isPlaying = false;
        m_isClearing = true;
        m_roundTimer = 0.0f;
        m_lastShownRemainingEnemyCount = -1;
        m_clearingElapsed = 0.0f;
        m_clearingPullApplied = false;
        m_nextOffscreenCheckTime = m_clearingOffscreenDespawnDelay;
        EndStageCombat();
        RefreshTimerText();
    }

    /// <summary>남은 적이 0이 되면 정리를 끝냅니다. 그 전까지는 남은 적 수를 갱신하고 안전장치를 시간에 맞춰 적용합니다.</summary>
    /// <remarks>
    /// 안전장치는 정리 구간 시작부터 셉니다(명세 C4). 끌어오기 → 화면 밖 회수 → 강제 종료 순서이며, 각각 0이면 꺼집니다.
    /// 지스타 시연에서 갇힌 적 한 마리 때문에 휴식이 시작되지 않는 일을 막기 위한 장치입니다.
    /// </remarks>
    private void UpdateClearing()
    {
        m_clearingElapsed += Time.deltaTime;

        int remaining = CountManagedLiveEnemies();
        if (remaining > 0)
        {
            ApplyClearingSafety();
            remaining = CountManagedLiveEnemies();
        }

        if (remaining <= 0)
        {
            FinishClearing();
            return;
        }

        if (remaining != m_lastShownRemainingEnemyCount)
        {
            m_lastShownRemainingEnemyCount = remaining;
            RefreshPhaseLabel();
        }
    }

    /// <summary>정리 경과 시간에 맞춰 끌어오기, 화면 밖 회수, 강제 종료를 적용합니다.</summary>
    private void ApplyClearingSafety()
    {
        if (m_clearingForceEndDelay > 0.0f && m_clearingElapsed >= m_clearingForceEndDelay)
        {
            int despawned = DespawnRemainingEnemies(null, "강제 종료");
            Debug.LogWarning($"[DefenseManager] 정리 구간이 {m_clearingForceEndDelay:0}초를 넘어 남은 적 {despawned}마리를 회수하고 정리를 끝냅니다.", this);
            return;
        }

        if (!m_clearingPullApplied && m_clearingPullDelay > 0.0f && m_clearingElapsed >= m_clearingPullDelay)
        {
            m_clearingPullApplied = true;
            PullRemainingEnemiesToPlayer();
        }

        if (m_clearingOffscreenDespawnDelay > 0.0f && m_clearingElapsed >= m_nextOffscreenCheckTime)
        {
            m_nextOffscreenCheckTime = m_clearingElapsed + OffscreenCheckInterval;
            Camera viewCamera = Camera.main;
            if (viewCamera != null)
            {
                DespawnRemainingEnemies(enemy => !IsVisibleToCamera(viewCamera, enemy.transform.position), "화면 밖 회수");
            }
        }
    }

    /// <summary>남은 적을 모두 플레이어 우선으로 바꿔 지금 조작 중인 캐릭터 쪽으로 끌어옵니다.</summary>
    private void PullRemainingEnemiesToPlayer()
    {
        CollectRemainingEnemies();
        for (int i = 0; i < m_clearingEnemyBuffer.Count; i++)
        {
            m_clearingEnemyBuffer[i].ForcePursuePlayer();
        }

        Debug.LogWarning(
            $"[DefenseManager] 정리 구간이 {m_clearingPullDelay:0}초를 넘어 남은 적 {m_clearingEnemyBuffer.Count}마리를 플레이어 쪽으로 끌어옵니다. " +
            DescribeEnemies(m_clearingEnemyBuffer), this);
        m_clearingEnemyBuffer.Clear();
    }

    /// <summary>
    /// 남은 적 가운데 조건에 맞는 적을 풀로 되돌리고, 어떤 적이 어디서 회수됐는지 경고로 남깁니다.
    /// </summary>
    /// <param name="shouldDespawn">회수할 적이면 true입니다. null이면 전부입니다.</param>
    /// <param name="reason">경고에 남길 사유입니다.</param>
    /// <returns>회수한 수입니다.</returns>
    /// <remarks>회수된 위치는 적이 갇히는 곳을 찾는 단서라 이름과 좌표를 함께 남깁니다.</remarks>
    private int DespawnRemainingEnemies(Predicate<EnemyController> shouldDespawn, string reason)
    {
        CollectRemainingEnemies();
        if (shouldDespawn != null)
        {
            m_clearingEnemyBuffer.RemoveAll(enemy => !shouldDespawn(enemy));
        }

        if (m_clearingEnemyBuffer.Count == 0)
        {
            return 0;
        }

        string description = DescribeEnemies(m_clearingEnemyBuffer);
        int despawned = 0;
        for (int i = 0; i < m_spawnPoints.Count; i++)
        {
            if (m_spawnPoints[i] != null)
            {
                despawned += m_spawnPoints[i].DespawnLiveEnemies(enemy => m_clearingEnemyBuffer.Contains(enemy));
            }
        }

        Debug.LogWarning($"[DefenseManager] 정리 안전장치({reason}): 남은 적 {despawned}마리를 회수했습니다. {description}", this);
        m_clearingEnemyBuffer.Clear();
        return despawned;
    }

    /// <summary>이 매니저의 스포너가 내보내 살아 있는 적을 재사용 목록에 모읍니다.</summary>
    private void CollectRemainingEnemies()
    {
        m_clearingEnemyBuffer.Clear();
        if (m_spawnPoints == null)
        {
            return;
        }

        for (int i = 0; i < m_spawnPoints.Count; i++)
        {
            m_spawnPoints[i]?.CollectLiveEnemies(m_clearingEnemyBuffer);
        }
    }

    /// <summary>위치가 카메라 화면 안(앞쪽)에 있는지 확인합니다. 가림은 보지 않습니다.</summary>
    private static bool IsVisibleToCamera(Camera viewCamera, Vector3 worldPosition)
    {
        Vector3 viewport = viewCamera.WorldToViewportPoint(worldPosition);
        return viewport.z > 0.0f && viewport.x >= 0.0f && viewport.x <= 1.0f && viewport.y >= 0.0f && viewport.y <= 1.0f;
    }

    /// <summary>적 이름과 위치를 한 줄로 만듭니다. 경고에 남겨 적이 갇히는 곳을 찾는 데 씁니다.</summary>
    private static string DescribeEnemies(List<EnemyController> enemies)
    {
        var builder = new System.Text.StringBuilder();
        for (int i = 0; i < enemies.Count; i++)
        {
            Vector3 p = enemies[i].transform.position;
            builder.Append(i == 0 ? string.Empty : ", ").Append(enemies[i].name).Append($" ({p.x:0.#}, {p.y:0.#}, {p.z:0.#})");
        }

        return builder.ToString();
    }

    /// <summary>정리를 끝내고, 마지막 웨이브면 승리로, 아니면 휴식으로 넘어갑니다.</summary>
    private void FinishClearing()
    {
        m_isClearing = false;
        GrantCurrentWaveClearReward();
        if (m_currentWave >= TotalWaveCount)
        {
            if (!BeginVictory())
            {
                Debug.LogError("[DefenseManager] 마지막 웨이브 승리 기록에 실패했습니다. 귀환 지점을 열지 않습니다.", this);
            }

            return;
        }

        BeginRest();
    }

    /// <summary>현재 웨이브의 잔존 적을 모두 정리했을 때 함정 재료 클리어 보상을 한 번 지급합니다.</summary>
    /// <remarks>
    /// 즉시 보관소에 더하므로 다음 정비 구간에서 바로 함정 설치에 쓸 수 있습니다.
    /// 마지막 웨이브도 같은 규칙으로 지급하며, 방어전 전체 승리 보상은 DefenseSceneDataManager가 별도로 기록합니다.
    /// </remarks>
    private void GrantCurrentWaveClearReward()
    {
        if (m_currentWave <= 0 || m_currentWave <= m_lastRewardedWave)
        {
            return;
        }

        // 보상이 0이거나 지급에 실패해도 이 웨이브의 정리 완료는 한 번만 처리합니다.
        m_lastRewardedWave = m_currentWave;

        int amount = m_activeStage != null
            ? m_activeStage.GetWaveClearTrapMaterialReward(m_currentWave - 1)
            : 0;
        if (amount <= 0)
        {
            return;
        }

        StorageFacility storage = ShelterSceneDataManager.Instance?.Storage;
        if (storage == null)
        {
            Debug.LogWarning(
                $"[DefenseManager] {m_currentWave}웨이브 클리어 보상(함정 재료 {amount})을 지급할 보관소를 찾지 못했습니다.",
                this);
            return;
        }

        if (!storage.TryAddResource(ResourceIds.TrapMaterial, amount))
        {
            Debug.LogWarning(
                $"[DefenseManager] {m_currentWave}웨이브 클리어 보상(함정 재료 {amount}) 지급에 실패했습니다.",
                this);
            return;
        }

        Debug.Log(
            $"[DefenseManager] {m_currentWave}웨이브 클리어: 함정 재료 {amount}개를 지급했습니다. " +
            $"현재 {storage.GetResourceAmount(ResourceIds.TrapMaterial)}개입니다.",
            this);
    }

    /// <summary>남은 적을 정리한 뒤 휴식 구간을 시작합니다.</summary>
    private void BeginRest()
    {
        m_isPlaying = false;
        m_isClearing = false;
        m_roundTimer = 0.0f;
        m_restTimer = Mathf.Max(0.01f, m_restDuration);

        // 정비 건너뛰기 홀드는 Z를 한 번 뗀 뒤부터 셉니다. 튜토리얼 마지막 페이지를 Z 홀드로 넘긴 누름이
        // 그대로 이어져 정비가 곧바로 건너뛰어지는 것을 막습니다.
        m_emptySpaceHoldStartTimer = 0.0f;
        m_maintenanceSkipNeedsRelease = true;
        RefreshTimerText();
        ShowPhaseStartMessage(m_restStartMessage);
        ResolveDefenseSceneDataManager()?.RecordRestStarted();

        // 휴식 복구 정책(OnRest) 함정이 먼저 되살아난 뒤 설치 구간을 엽니다. 순서가 바뀌어도 결과는 같지만,
        // 복구와 표시가 한 번에 맞춰지도록 알림을 먼저 보냅니다.
        OnRestStarted?.Invoke();
        RefreshTrapBuildWindow();
    }

    /// <summary>
    /// 남은 웨이브를 건너뛰고 곧바로 승리 상태(귀환 구역 열림)로 만듭니다. 디버그 트레이너 전용입니다.
    /// </summary>
    /// <remarks>정상 승리와 같은 경로라 임무 완료와 승리 보상도 그대로 기록됩니다. 시작 전이거나 게임오버면 하지 않습니다.</remarks>
    public void DebugForceVictory()
    {
        if (!m_isGameStarted || m_isGameOver || m_isVictoryReady)
        {
            Debug.LogWarning("[DefenseManager] 방어전이 진행 중일 때만 강제 승리할 수 있습니다.", this);
            return;
        }

        if (!m_isPlaying && !m_isClearing)
        {
            Debug.LogWarning("[DefenseManager] DebugForceVictory는 전투 또는 정리 단계에서만 사용할 수 있습니다.", this);
            return;
        }

        if (!BeginVictory())
        {
            return;
        }

        DespawnManagedEnemies();
    }

    /// <summary>이 매니저의 스포너가 내보낸 적을 처치로 세지 않고 모두 회수합니다. 디버그 트레이너 전용입니다.</summary>
    /// <returns>회수한 적의 수입니다.</returns>
    public int DebugDespawnManagedEnemies()
    {
        return DespawnManagedEnemies();
    }

    /// <summary>마지막 웨이브를 완료하고, 정산 전 귀환 구역 진입 대기 상태로 전환합니다.</summary>
    private bool BeginVictory()
    {
        if (!RecordVictory())
        {
            return false;
        }

        m_isPlaying = false;
        m_isClearing = false;
        m_isVictoryReady = true;
        EndStageCombat();
        m_roundTimer = 0.0f;
        m_restTimer = 0.0f;
        RefreshTimerText();
        HideWaveStartMessage();

        SetReturnPointActive(true);
        RefreshTrapBuildWindow();
        OnDefenseVictoryReady?.Invoke();
        return true;
    }

    /// <summary>
    /// 공격로 이름을 검사하고, 방어전 스포너마다 그 공격로가 쓰는 항목으로 풀을 준비시킵니다.
    /// </summary>
    /// <remarks>
    /// 이름이 맞지 않아도 진행은 멈추지 않고 경고만 남깁니다. 방어전 SO에 없는 공격로의 스포너는 모든 웨이브를 쉽니다.
    /// </remarks>
    private void PrepareStageSpawners()
    {
        if (m_activeStage == null || m_spawnPoints == null)
        {
            return;
        }

        var problems = new List<string>();
        m_activeStage.CollectProblems(problems);

        var sceneRoutes = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < m_spawnPoints.Count; i++)
        {
            EnemyDefenseSpawnPoint defenseSpawnPoint = m_spawnPoints[i];
            if (defenseSpawnPoint == null)
            {
                continue;
            }

            string routeId = defenseSpawnPoint.RouteId;
            if (string.IsNullOrEmpty(routeId))
            {
                problems.Add($"스포너 '{defenseSpawnPoint.name}'의 공격로 이름이 비어 있어 모든 웨이브를 쉽니다.");
            }
            else if (!m_activeStage.HasRoute(routeId))
            {
                problems.Add($"스포너 '{defenseSpawnPoint.name}'의 공격로 '{routeId}'가 방어전 SO '{m_activeStage.name}'에 없어 모든 웨이브를 쉽니다.");
            }
            else
            {
                sceneRoutes.Add(routeId);
            }

            defenseSpawnPoint.PrepareForStage(m_activeStage);
        }

        IReadOnlyList<DefenseStageSO.Route> routes = m_activeStage.Routes;
        for (int i = 0; i < routes.Count; i++)
        {
            string routeId = routes[i].RouteId;
            if (!string.IsNullOrEmpty(routeId) && !sceneRoutes.Contains(routeId))
            {
                problems.Add($"방어전 SO '{m_activeStage.name}'의 공격로 '{routeId}'를 가진 스포너가 이 매니저 목록에 없어 그 공격로는 나오지 않습니다.");
            }
        }

        for (int i = 0; i < problems.Count; i++)
        {
            Debug.LogWarning($"[DefenseManager] {problems[i]}", this);
        }
    }

    /// <summary>방어전 스포너마다 이번 웨이브에 자기 공격로가 실행할 웨이브 SO를 넘겨 전투를 시작시킵니다.</summary>
    /// <remarks>웨이브 SO가 없는 공격로(빈 칸, 목록보다 뒤)는 null을 받아 이번 전투를 쉽니다.</remarks>
    private void BeginStageCombat()
    {
        if (m_activeStage == null || m_spawnPoints == null)
        {
            return;
        }

        int waveIndex = m_currentWave - 1;
        for (int i = 0; i < m_spawnPoints.Count; i++)
        {
            EnemyDefenseSpawnPoint defenseSpawnPoint = m_spawnPoints[i];
            if (defenseSpawnPoint != null)
            {
                defenseSpawnPoint.BeginCombat(m_activeStage.GetWave(defenseSpawnPoint.RouteId, waveIndex));
            }
        }
    }

    /// <summary>방어전 스포너의 전투를 끝냅니다. 새 그룹을 만들지 않고, 기다리던 그룹은 버립니다.</summary>
    private void EndStageCombat()
    {
        if (m_spawnPoints == null)
        {
            return;
        }

        for (int i = 0; i < m_spawnPoints.Count; i++)
        {
            m_spawnPoints[i]?.EndCombat();
        }
    }

    /// <summary>이 매니저의 스포너가 내보내 아직 살아 있는 적 수를 셉니다. 시체는 세지 않습니다.</summary>
    private int CountManagedLiveEnemies()
    {
        if (m_spawnPoints == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < m_spawnPoints.Count; i++)
        {
            if (m_spawnPoints[i] != null)
            {
                count += m_spawnPoints[i].ActiveEnemyCount;
            }
        }

        return count;
    }

    /// <summary>
    /// 방어전 시작 규칙입니다. 현재 조작 중인 스쿼드원이 있을 때 <see cref="StartHoldKey"/>(Z)를
    /// 일정 시간 누르고 있으면 <see cref="StartDefense"/>를 호출합니다.
    /// </summary>
    /// <remarks>
    /// 튜토리얼 여부와 관계없이 매 방어전 같은 규칙입니다. 끄는 옵션은 두지 않습니다.
    /// 다른 시작 경로가 없어서, 끄면 방어전을 시작할 방법이 없어지기 때문입니다.
    /// 튜토리얼이 진행 중이면 세지 않습니다. 튜토리얼 앞 페이지도 Z로 넘기므로, 여기서도 세면 페이지를 넘기는 동안
    /// 시작될 수 있습니다. 그동안은 튜토리얼 마지막 페이지가 같은 규칙(Z 3초)으로 시작을 맡습니다.
    /// 튜토리얼과 같게 실제 시간으로 셉니다. 게임 시간은 프레임이 느릴 때 잘려 3초를 눌러도 덜 찹니다.
    /// 시간이 멈춘 동안(일시정지 메뉴 등)은 세지 않습니다.
    /// 시작 전 시간은 휴식으로 취급하므로 이 동안 함정을 설치할 수 있습니다(<see cref="IsResting"/>).
    /// </remarks>
    private void UpdateEmptySpaceHoldStart()
    {
        TutorialManager tutorial = TutorialManager.Instance;
        Keyboard keyboard = Keyboard.current;
        if ((tutorial != null && tutorial.IsRunning)
            || Time.timeScale <= 0.0f
            || keyboard == null
            || !HasActiveSquadStartInput()
            || !keyboard[StartHoldKey].isPressed)
        {
            m_emptySpaceHoldStartTimer = 0.0f;
            return;
        }

        m_emptySpaceHoldStartTimer += Time.unscaledDeltaTime;
        if (m_emptySpaceHoldStartTimer >= m_emptySpaceHoldStartDuration)
        {
            StartDefense();
        }
    }

    /// <summary>
    /// 정비 시작 모드에서 튜토리얼이 진행 중이 아니면 방어전을 시작해 정비 시간을 흐르게 합니다.
    /// </summary>
    /// <remarks>
    /// 튜토리얼이 있는 판은 마지막 페이지가 <see cref="StartDefense"/>를 부르므로 여기서는 기다리기만 합니다.
    /// 튜토리얼이 없는 판(2회차부터)은 씬에 들어온 첫 프레임에 시작합니다. 시작에 실패하면(방어전 구성 없음) 다시 시도하지 않습니다.
    /// </remarks>
    private void TryAutoStartMaintenance()
    {
        TutorialManager tutorial = TutorialManager.Instance;
        if (m_autoStartAttempted || (tutorial != null && tutorial.IsRunning))
        {
            return;
        }

        m_autoStartAttempted = true;
        StartDefense();
    }

    /// <summary>
    /// 정비 구간에서 Z를 누르고 있는 시간을 세고, 다 차면 남은 정비 시간을 건너뛰고 바로 공세를 시작합니다.
    /// </summary>
    /// <returns>이번 프레임에 공세를 시작했으면 true입니다.</returns>
    /// <remarks>시작 전 홀드(<see cref="UpdateEmptySpaceHoldStart"/>)와 같은 규칙으로 실제 시간을 셉니다. 시간이 멈춘 동안은 세지 않습니다.</remarks>
    private bool UpdateMaintenanceSkipHold()
    {
        Keyboard keyboard = Keyboard.current;
        bool pressed = keyboard != null && keyboard[StartHoldKey].isPressed;
        if (!pressed)
        {
            m_maintenanceSkipNeedsRelease = false;
        }

        if (!pressed
            || m_maintenanceSkipNeedsRelease
            || Time.timeScale <= 0.0f
            || !HasActiveSquadStartInput())
        {
            m_emptySpaceHoldStartTimer = 0.0f;
            return false;
        }

        m_emptySpaceHoldStartTimer += Time.unscaledDeltaTime;
        if (m_emptySpaceHoldStartTimer < m_emptySpaceHoldStartDuration)
        {
            return false;
        }

        m_emptySpaceHoldStartTimer = 0.0f;
        m_restTimer = 0.0f;
        BeginRound();
        return true;
    }

    /// <summary>
    /// 현재 스쿼드가 직접 조작 중이고 입력을 받는 멤버가 있는지 확인합니다.
    /// </summary>
    /// <remarks>
    /// 시작 입력을 Inspector에 고정하면 스쿼드 전환 뒤 비활성 멤버의 입력을 계속 읽게 됩니다.
    /// 따라서 매 프레임 <see cref="SquadManager.PlayerSquadMember"/>를 정본으로 사용합니다.
    /// </remarks>
    private static bool HasActiveSquadStartInput()
    {
        SquadMemberController activeMember = SquadManager.Instance?.PlayerSquadMember;
        if (activeMember == null || !activeMember.IsPlayerSquadMember)
        {
            return false;
        }

        PlayerInputController startInput = activeMember.GetComponent<PlayerInputController>();
        return startInput != null && startInput.isActiveAndEnabled;
    }

    /// <summary>
    /// 시작 전이고 튜토리얼이 진행 중이 아니면 튜토리얼 창을 압축 크기로 띄워 시작 안내(Z 3초)와 키 게이지를 보여 줍니다.
    /// </summary>
    /// <remarks>
    /// 2회차부터는 튜토리얼만 빠지고 시작 규칙(Z 3초)은 같으므로, 튜토리얼 마지막 페이지의 시작 안내 부분만 같은 창에 띄웁니다.
    /// 좌상단 목표 패널은 1회차와 같게 둡니다. 튜토리얼은 Start에서 시작되므로 첫 Update부터 진행 여부를 바로 알 수 있습니다.
    /// </remarks>
    private void RefreshStartPrompt()
    {
        if (m_startPromptOverlay == null)
        {
            m_startPromptOverlay = FindFirstObjectByType<DefenseTutorialOverlay>(FindObjectsInactive.Include);
            if (m_startPromptOverlay == null)
            {
                return;
            }
        }

        TutorialManager tutorial = TutorialManager.Instance;
        bool tutorialRunning = tutorial != null && tutorial.IsRunning;

        // 기본 모드는 시작 전에, 정비 시작 모드는 정비 구간마다 띄웁니다(정비 건너뛰기 안내).
        bool showPrompt = IsStartHoldAvailable && !tutorialRunning;
        if (showPrompt)
        {
            if (!m_startPromptOverlay.IsCompact)
            {
                m_startPromptOverlay.ShowCompact(TutorialPage.CreateDisplayPage(
                    m_startPromptTitle,
                    m_startPromptBody,
                    m_startPromptHint,
                    m_startPromptKeyIcon,
                    m_startPromptKeyIconPosition,
                    m_startPromptKeyIconSize));
            }

            m_startPromptOverlay.SetGaugeProgress(EmptySpaceHoldStartProgress);
            return;
        }

        // 튜토리얼이 같은 창을 쓰는 중이면 건드리지 않습니다. 압축 안내를 띄운 경우에만 내립니다.
        if (!tutorialRunning && m_startPromptOverlay.IsCompact)
        {
            m_startPromptOverlay.Hide();
        }
    }

    /// <summary>전투 중이면 목표 패널 문구를 전투용으로 바꾸고, 그 외에는 원래 문구로 돌려놓습니다.</summary>
    private void RefreshMissionText()
    {
        TMP_Text missionTitleText = ResolveMissionTitleText();
        if (missionTitleText == null && m_missionDescriptionText == null)
        {
            return;
        }

        if (m_isPlaying && !m_isGameOver)
        {
            if (missionTitleText != null && !string.IsNullOrEmpty(m_combatMissionTitle))
            {
                if (m_missionTitleOriginalText == null)
                {
                    m_missionTitleOriginalText = missionTitleText.text;
                }

                if (missionTitleText.text != m_combatMissionTitle)
                {
                    missionTitleText.text = m_combatMissionTitle;
                }
            }

            if (m_missionDescriptionText != null && !string.IsNullOrEmpty(m_combatMissionDescription))
            {
                if (m_missionDescriptionOriginalText == null)
                {
                    m_missionDescriptionOriginalText = m_missionDescriptionText.text;
                }

                if (m_missionDescriptionText.text != m_combatMissionDescription)
                {
                    m_missionDescriptionText.text = m_combatMissionDescription;
                }
            }
            return;
        }

        if (missionTitleText != null && m_missionTitleOriginalText != null)
        {
            missionTitleText.text = m_missionTitleOriginalText;
            m_missionTitleOriginalText = null;
        }

        if (m_missionDescriptionText != null && m_missionDescriptionOriginalText != null)
        {
            m_missionDescriptionText.text = m_missionDescriptionOriginalText;
            m_missionDescriptionOriginalText = null;
        }
    }

    /// <summary>씬 연결이 없으면 설명 텍스트와 같은 패널의 Title 오브젝트를 사용합니다.</summary>
    private TMP_Text ResolveMissionTitleText()
    {
        if (m_missionTitleText != null)
        {
            return m_missionTitleText;
        }

        if (m_missionDescriptionText == null || m_missionDescriptionText.transform.parent == null)
        {
            return null;
        }

        Transform titleTransform = m_missionDescriptionText.transform.parent.Find("Title");
        if (titleTransform != null)
        {
            m_missionTitleText = titleTransform.GetComponent<TMP_Text>();
        }

        return m_missionTitleText;
    }

    /// <summary>
    /// Shooter 업그레이드 레벨에 맞춰 지정사수를 켜고 끕니다.
    /// </summary>
    /// <remarks>
    /// 레벨은 <see cref="DefenseSceneDataManager"/>의 입장 데이터에서 읽습니다. 씬 도착 뒤 전역 데이터가 바뀌어도
    /// 이번 판의 배치는 출격 시점 값으로 고정됩니다. 레벨이 목록 길이보다 크면 목록 전체를 켭니다.
    /// </remarks>
    private void ApplyMarksmanPlacement()
    {
        if (m_marksmanTiers == null || m_marksmanTiers.Length == 0)
        {
            return;
        }

        int level = ResolveMarksmanLevel();
        for (int i = 0; i < m_marksmanTiers.Length; i++)
        {
            bool active = i < level;
            SetMarksmanActive(m_marksmanTiers[i].Left, active);
            SetMarksmanActive(m_marksmanTiers[i].Right, active);
        }
    }

    /// <summary>지금 지정사수 배치에 쓰는 레벨입니다. 디버그 지정 값이 있으면 그 값입니다.</summary>
    /// <remarks>디버그 창이 매 프레임 읽으므로, 데이터 매니저가 없을 때도 경고를 내지 않습니다.</remarks>
    public int MarksmanLevel
    {
        get
        {
            if (m_debugMarksmanLevelOverride >= 0)
            {
                return m_debugMarksmanLevelOverride;
            }

            DefenseSceneDataManager defenseData = ResolveDefenseSceneDataManager();
            if (defenseData != null)
            {
                return defenseData.GetUpgradeLevel(ScrambleUpgradeType.Shooter);
            }

            return GameDataManager.Instance != null ? GameDataManager.Instance.ShooterUpgradeLevel : 0;
        }
    }

    /// <summary>지정사수 칸 수입니다. 이 값이 배치할 수 있는 최대 레벨입니다.</summary>
    public int MarksmanTierCount => m_marksmanTiers != null ? m_marksmanTiers.Length : 0;

    /// <summary>지정사수 레벨을 디버그 값으로 지정했는지 여부입니다. 아니면 입장 데이터 값을 씁니다.</summary>
    public bool HasMarksmanLevelOverride => m_debugMarksmanLevelOverride >= 0;

    /// <summary>
    /// 디버그용: 지정사수 레벨을 지정하고 바로 다시 배치합니다.
    /// </summary>
    /// <param name="level">배치할 레벨입니다. -1이면 지정을 풀고 입장 데이터 값으로 돌아갑니다.</param>
    /// <remarks>Play 중에만 바뀌는 런타임 값입니다. 씬의 Inspector 값은 바꾸지 않습니다.</remarks>
    public void DebugSetMarksmanLevel(int level)
    {
        m_debugMarksmanLevelOverride = Mathf.Max(-1, level);
        ApplyMarksmanPlacement();
    }

    private int ResolveMarksmanLevel()
    {
        if (m_debugMarksmanLevelOverride >= 0)
        {
            return m_debugMarksmanLevelOverride;
        }

        DefenseSceneDataManager defenseData = ResolveDefenseSceneDataManager();
        if (defenseData != null)
        {
            return defenseData.GetUpgradeLevel(ScrambleUpgradeType.Shooter);
        }

        Debug.LogWarning("[DefenseManager] DefenseSceneDataManager가 없어 GameDataManager의 Shooter 레벨로 지정사수를 배치합니다.", this);
        return GameDataManager.Instance != null ? GameDataManager.Instance.ShooterUpgradeLevel : 0;
    }

    private static void SetMarksmanActive(GameObject marksman, bool active)
    {
        if (marksman != null && marksman.activeSelf != active)
        {
            marksman.SetActive(active);
        }
    }

    /// <summary>웨이브 진행을 기록할 방어전 데이터 매니저 참조를 확보합니다.</summary>
    private DefenseSceneDataManager ResolveDefenseSceneDataManager()
    {
        if (m_defenseSceneDataManager == null)
        {
            m_defenseSceneDataManager = DefenseSceneDataManager.Instance != null
                ? DefenseSceneDataManager.Instance
                : FindFirstObjectByType<DefenseSceneDataManager>();
        }

        return m_defenseSceneDataManager;
    }

    /// <summary>방어전 승리를 데이터 매니저에 기록합니다.</summary>
    /// <remarks>
    /// 임무 완료 표시와 승리 보상 기록은 <see cref="DefenseSceneDataManager.CompleteVictory"/>가 함께 합니다.
    /// </remarks>
    private bool RecordVictory()
    {
        DefenseSceneDataManager defenseData = ResolveDefenseSceneDataManager();
        if (defenseData == null)
        {
            Debug.LogError("[DefenseManager] DefenseSceneDataManager가 없어 승리를 기록하지 못했습니다. 귀환 정산이 실패로 나옵니다.", this);
            return false;
        }

        if (defenseData.CompleteVictory() != null)
        {
            return true;
        }

        Debug.LogError("[DefenseManager] 방어전 승리 상태 기록이 거부되었습니다. 런타임 상태를 확인하세요.", this);
        return false;
    }

    /// <summary>미리 배치해 둔 귀환 구역을 켭니다.</summary>
    /// <remarks>
    /// 런타임에 만들지 않고 씬 오브젝트를 켜고 끄기만 합니다. 위치·크기·모양을 씬에서 눈으로 보고
    /// 끌어다 맞출 수 있어야 하는데, 생성 방식은 기준점과 오프셋 숫자로만 정해져 실제 자리가 실행 전에는
    /// 보이지 않았습니다. 트리거 구성도 프리팹이 이미 갖고 있으므로 코드가 다시 보장할 이유가 없습니다.
    /// </remarks>
    private void SetReturnPointActive(bool isActive)
    {
        if (m_returnPoint == null)
        {
            if (isActive)
            {
                Debug.LogWarning("[DefenseManager] 귀환 구역이 비어 있어 켜지 못했습니다. 인스펙터에서 지정하세요.", this);
            }

            return;
        }

        if (m_returnPoint.activeSelf != isActive)
        {
            m_returnPoint.SetActive(isActive);
        }
    }

    /// <summary>현재 전투 또는 휴식의 남은 시간을 분:초 형식으로 HUD에 반영합니다.</summary>
    private void RefreshTimerText()
    {
        // 구간 라벨과 남은 웨이브도 여기서 함께 갱신합니다. 호출부가 일곱 곳이라 따로 부르게 두면 새 경로가
        // 생길 때마다 한쪽만 빠져 표시가 어긋납니다. 타이머 참조가 없어도 둘은 갱신해야 하므로 null 검사보다 앞입니다.
        RefreshPhaseLabel();
        RefreshRemainingWaveText();

        if (m_timerText == null)
        {
            return;
        }

        bool shouldShow = m_isGameStarted && !m_isVictoryReady;
        if (m_timerText.gameObject.activeSelf != shouldShow)
        {
            m_timerText.gameObject.SetActive(shouldShow);
        }

        if (!shouldShow)
        {
            return;
        }

        float remaining = m_isPlaying ? m_roundTimer : m_restTimer;
        int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(remaining));
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        m_timerText.text = $"{minutes:00}:{seconds:00}";
    }

    /// <summary>
    /// 지금이 전투 구간인지 휴식 구간인지 표시합니다.
    /// </summary>
    /// <remarks>
    /// 타이머와 표시 조건을 같게 둡니다. 숫자만 남고 무엇을 세는 시간인지 사라지거나, 반대로 라벨만
    /// 남는 상태가 생기지 않게 하기 위함입니다. 그래서 <see cref="RefreshTimerText"/>와 같은 곳에서 함께 부릅니다.
    /// </remarks>
    private void RefreshPhaseLabel()
    {
        if (m_phaseLabelText == null)
        {
            return;
        }

        bool shouldShow = m_isGameStarted && !m_isVictoryReady;
        if (m_phaseLabelText.gameObject.activeSelf != shouldShow)
        {
            m_phaseLabelText.gameObject.SetActive(shouldShow);
        }

        if (!shouldShow)
        {
            return;
        }

        if (m_isPlaying)
        {
            m_phaseLabelText.text = m_combatPhaseLabel;
        }
        else if (m_isClearing)
        {
            // 전투 타이머가 00:00인데 휴식이 시작되지 않는 이유를 보여줍니다.
            m_phaseLabelText.text = string.Format(m_clearingPhaseLabelFormat, CountManagedLiveEnemies());
        }
        else
        {
            m_phaseLabelText.text = m_restPhaseLabel;
        }
    }

    /// <summary>
    /// 남은 웨이브 수를 표시합니다.
    /// </summary>
    /// <remarks>
    /// 진행 중인 웨이브를 아직 남은 것으로 셉니다. 1/3 전투 중에 "남은 라운드 2"가 되면 지금 막고 있는
    /// 웨이브가 셈에서 빠져 하나 적게 보입니다. 휴식 구간에서는 그 웨이브가 이미 끝났으므로 자연히 줄어듭니다.
    ///
    /// 타이머·구간 라벨과 표시 조건을 같게 둡니다. 셋이 한 줄에 놓이는데 조건이 갈리면 일부만 남습니다.
    /// </remarks>
    private void RefreshRemainingWaveText()
    {
        if (m_remainingWaveText == null)
        {
            return;
        }

        bool shouldShow = m_isGameStarted && !m_isVictoryReady;
        if (m_remainingWaveText.gameObject.activeSelf != shouldShow)
        {
            m_remainingWaveText.gameObject.SetActive(shouldShow);
        }

        if (!shouldShow)
        {
            return;
        }

        // 정리 중인 웨이브도 아직 끝나지 않았으므로 남은 것으로 셉니다.
        int totalWaveCount = TotalWaveCount;
        int remaining = Mathf.Max(0, totalWaveCount - m_currentWave + (m_isPlaying || m_isClearing ? 1 : 0));
        m_remainingWaveText.text = string.Format(m_remainingWaveFormat, remaining, totalWaveCount);
    }

    /// <summary>구간 시작 알림을 표시하고 설정된 유지·페이드 시간을 시작합니다.</summary>
    private void ShowPhaseStartMessage(string message)
    {
        if (m_waveStartMessageText == null)
        {
            return;
        }

        m_waveStartMessageText.text = message;
        SetWaveStartMessageVisible(true);
        SetWaveStartMessageAlpha(1.0f);

        m_waveStartMessageRemaining = m_waveStartMessageHoldDuration + m_waveStartMessageFadeDuration;
        if (m_waveStartMessageRemaining <= 0.0f)
        {
            HideWaveStartMessage();
        }
    }

    /// <summary>웨이브 시작 알림의 유지·페이드 상태를 매 프레임 갱신합니다.</summary>
    private void UpdateWaveStartMessage()
    {
        if (m_waveStartMessageRemaining <= 0.0f)
        {
            return;
        }

        // 게임 시간 배율과 무관하게 플레이어가 읽을 수 있는 실제 시간으로 알림을 페이드합니다.
        m_waveStartMessageRemaining = Mathf.Max(0.0f, m_waveStartMessageRemaining - Time.unscaledDeltaTime);
        if (m_waveStartMessageRemaining <= 0.0f)
        {
            HideWaveStartMessage();
            return;
        }

        if (m_waveStartMessageRemaining <= m_waveStartMessageFadeDuration
            && m_waveStartMessageFadeDuration > 0.0f)
        {
            SetWaveStartMessageAlpha(m_waveStartMessageRemaining / m_waveStartMessageFadeDuration);
        }
    }

    /// <summary>웨이브 시작 알림을 즉시 숨기고 페이드 상태를 초기화합니다.</summary>
    private void HideWaveStartMessage()
    {
        m_waveStartMessageRemaining = 0.0f;
        SetWaveStartMessageAlpha(0.0f);
        SetWaveStartMessageVisible(false);
    }

    /// <summary>웨이브 시작 알림 루트의 활성 상태를 변경합니다.</summary>
    private void SetWaveStartMessageVisible(bool visible)
    {
        GameObject root = m_waveStartMessageCanvasGroup != null
            ? m_waveStartMessageCanvasGroup.gameObject
            : m_waveStartMessageText != null ? m_waveStartMessageText.gameObject : null;

        if (root != null && root.activeSelf != visible)
        {
            root.SetActive(visible);
        }
    }

    /// <summary>웨이브 시작 알림의 알파값을 CanvasGroup 또는 텍스트에 반영합니다.</summary>
    private void SetWaveStartMessageAlpha(float alpha)
    {
        alpha = Mathf.Clamp01(alpha);
        if (m_waveStartMessageCanvasGroup != null)
        {
            m_waveStartMessageCanvasGroup.alpha = alpha;
            return;
        }

        if (m_waveStartMessageText != null)
        {
            Color color = m_waveStartMessageText.color;
            color.a = alpha;
            m_waveStartMessageText.color = color;
        }
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();

        m_roundDuration = Mathf.Max(0.01f, m_roundDuration);
        m_restDuration = Mathf.Max(0.01f, m_restDuration);
        m_maxActiveEnemies = Mathf.Max(1, m_maxActiveEnemies);
        m_clearingPullDelay = Mathf.Max(0.0f, m_clearingPullDelay);
        m_clearingOffscreenDespawnDelay = Mathf.Max(0.0f, m_clearingOffscreenDespawnDelay);
        m_clearingForceEndDelay = Mathf.Max(0.0f, m_clearingForceEndDelay);
        m_waveStartMessageHoldDuration = Mathf.Max(0.0f, m_waveStartMessageHoldDuration);
        m_waveStartMessageFadeDuration = Mathf.Max(0.0f, m_waveStartMessageFadeDuration);
        m_emptySpaceHoldStartDuration = Mathf.Max(0.01f, m_emptySpaceHoldStartDuration);
    }
#endif
}
