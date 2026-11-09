using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>
/// 튜토리얼 시퀀스에서 고를 수 있는 이벤트 메서드임을 표시합니다. 시퀀스 Inspector의 드롭다운에 이 이름으로 나옵니다.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TutorialEventAttribute : Attribute
{
    /// <summary>드롭다운에 보일 이름입니다.</summary>
    public string DisplayName { get; }

    public TutorialEventAttribute(string displayName)
    {
        DisplayName = displayName;
    }
}

/// <summary>
/// 튜토리얼 이벤트 메서드를 모아 둔 곳입니다. 시퀀스의 줄마다 여기 메서드를 골라 값만 채워 실행합니다.
/// </summary>
/// <remarks>
/// 새 이벤트는 여기에 public 메서드를 만들고 <see cref="TutorialEventAttribute"/>를 붙이면 드롭다운에 나옵니다.
/// 파라미터는 string, float, int, bool, Vector3, 열거형, 에셋(Sprite 등)만 받습니다. 시퀀스가 에셋이라 씬 오브젝트는 저장할 수 없어서입니다.
/// <para>
/// 메서드가 <see cref="IEnumerator"/>를 돌려주면 끝날 때까지 튜토리얼이 다음으로 넘어가지 않습니다(창을 닫을 때까지 대기 등).
/// 메서드 이름이 에셋에 저장되므로, 이미 쓰는 메서드의 이름을 바꾸지 마세요.
/// </para>
/// </remarks>
[DisallowMultipleComponent]
public sealed class TutorialEvents : MonoBehaviour
{
    [Header("References")]
    [Tooltip("미션 목표 변경이 바꿀 HUD입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private MissionObjectiveHud m_objective;

    [Tooltip("튜토리얼 창 띄우기가 쓸 창입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private DefenseTutorialOverlay m_window;

    [Tooltip("화살표 마커 표시가 쓸 안내 컴포넌트입니다(화면 안 마커, 화면 밖 방향 화살표). " +
             "공격로 마커와 겹치지 않게 튜토리얼 전용으로 하나 둡니다. 비어 있으면 마커 이벤트는 경고만 남깁니다.")]
    [SerializeField] private ObjectiveIndicatorController m_marker;

    [Tooltip("마커 위치로 옮길 오브젝트입니다. 보통 안내 컴포넌트의 월드 마커 루트를 넣습니다. 비어 있으면 빈 오브젝트를 만들어 씁니다.")]
    [SerializeField] private Transform m_markerAnchor;

    [Tooltip("경로선 안내가 쓸 발밑 경로 화살표입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private ObjectivePathArrow m_pathArrow;

    [Header("Enemy Spawn")]
    [Min(0.1f)]
    [Tooltip("적 생성 위치를 NavMesh 위로 붙일 때 찾는 반경(m)입니다.")]
    [SerializeField] private float m_spawnSampleRadius = 3.0f;

    [Header("Window")]
    [Tooltip("튜토리얼 창을 닫는 키입니다.")]
    [SerializeField] private Key m_closeKey = Key.F;

    [Min(0.0f)]
    [Tooltip("창이 뜬 직후 닫기 입력을 무시하는 시간(초)입니다. 앞 대사를 넘긴 입력으로 창이 바로 닫히지 않게 합니다.")]
    [SerializeField] private float m_closeGuardSeconds = 0.3f;

    private static readonly Dictionary<string, MethodInfo> s_methods = new();

    private bool m_isHoldingPause;
    private float m_resumeTimeScale = 1.0f;
    private bool m_isWindowOpen;

    /// <summary>경로선 목표 위치로 옮길 오브젝트입니다. 처음 경로선을 띄울 때 만듭니다.</summary>
    private Transform m_pathAnchor;

    /// <summary>기다리지 않는 경로선 안내의 도착 감시입니다. 도착하면 경로선을 숨깁니다.</summary>
    private Coroutine m_pathWatch;

    /// <summary>적 생성으로 만든 적입니다. 튜토리얼 적 제거가 여기서만 고릅니다.</summary>
    private readonly List<EnemyController> m_spawnedEnemies = new();

    /// <summary>
    /// 이벤트 하나를 실행합니다. 메서드가 기다리는 이벤트면 끝날 때까지 이어집니다.
    /// </summary>
    /// <remarks>부르는 쪽은 코루틴 안에서 <c>yield return Run(action)</c>으로 씁니다.</remarks>
    public IEnumerator Run(TutorialAction action)
    {
        if (action == null || string.IsNullOrEmpty(action.Method))
        {
            yield break;
        }

        MethodInfo method = FindMethod(action.Method);
        if (method == null)
        {
            Debug.LogWarning($"튜토리얼 이벤트 '{action.Method}'를 {nameof(TutorialEvents)}에서 찾지 못했습니다. 메서드 이름이 바뀌었는지 확인하세요.", this);
            yield break;
        }

        ParameterInfo[] parameters = method.GetParameters();
        object[] values = new object[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            Type type = parameters[i].ParameterType;
            values[i] = i < action.Args.Count
                ? action.Args[i].GetValue(type)
                : default(TutorialEventArg).GetValue(type);
        }

        if (method.Invoke(this, values) is IEnumerator wait)
        {
            yield return wait;
        }
    }

    /// <summary>튜토리얼이 끝날 때 이벤트가 남긴 상태(멈춘 시간, 열린 창, 마커)를 되돌립니다.</summary>
    public void ReleaseAll()
    {
        if (m_isWindowOpen)
        {
            m_isWindowOpen = false;
            ResolveWindow()?.Hide();
        }

        HideMarker();
        HidePathLine();
        ResumeTime();
    }

    /// <summary>조작 중인 캐릭터가 위치의 반경 안에 있는지 여부입니다. 높이는 보지 않고 평면 거리로 잽니다.</summary>
    public static bool IsPlayerInArea(Vector3 center, float radius)
    {
        SquadMemberController member = SquadManager.Instance != null ? SquadManager.Instance.PlayerSquadMember : null;
        if (member == null)
        {
            return false;
        }

        Vector3 offset = member.transform.position - center;
        offset.y = 0.0f;
        return offset.sqrMagnitude <= radius * radius;
    }

    /// <summary>
    /// 적이 정한 프리팹으로 만든 것인지 여부입니다. 프리팹이 null이면 늘 true입니다.
    /// </summary>
    /// <remarks>
    /// 만든 적은 프리팹을 기억하지 않아서 이름으로 봅니다. Instantiate는 "프리팹 이름(Clone)"으로 이름을 붙이므로,
    /// 스포너가 만든 적도 같은 방식으로 맞습니다. 이름이 앞부분이 같은 다른 프리팹(Zombie, Zombie_Fast)은 구분합니다.
    /// </remarks>
    public static bool IsFromPrefab(Component enemy, EnemyController prefab)
    {
        if (prefab == null)
        {
            return true;
        }

        if (enemy == null)
        {
            return false;
        }

        string name = enemy.gameObject.name;
        string prefabName = prefab.gameObject.name;
        return name == prefabName || name.StartsWith(prefabName + "(", StringComparison.Ordinal)
            || name.StartsWith(prefabName + " (", StringComparison.Ordinal);
    }

    /// <summary>
    /// 이벤트로 고를 수 있는 메서드를 이름으로 찾습니다. Inspector 드롭다운도 같은 목록을 씁니다.
    /// </summary>
    public static MethodInfo FindMethod(string name)
    {
        if (s_methods.Count == 0)
        {
            foreach (MethodInfo method in GetEventMethods())
            {
                s_methods[method.Name] = method;
            }
        }

        return s_methods.TryGetValue(name, out MethodInfo found) ? found : null;
    }

    /// <summary><see cref="TutorialEventAttribute"/>가 붙은 public 메서드 목록입니다.</summary>
    public static IEnumerable<MethodInfo> GetEventMethods()
    {
        foreach (MethodInfo method in typeof(TutorialEvents).GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            if (method.GetCustomAttribute<TutorialEventAttribute>() != null)
            {
                yield return method;
            }
        }
    }

    // ───────────── 이벤트 메서드 ─────────────

    /// <summary>미션 목표 문구를 바꿉니다. 비운 칸은 지금 문구를 둡니다.</summary>
    [TutorialEvent("미션 목표 변경")]
    public void SetObjective(string title, string subtitle)
    {
        if (m_objective == null)
        {
            m_objective = FindFirstObjectByType<MissionObjectiveHud>(FindObjectsInactive.Include);
        }

        if (m_objective != null)
        {
            m_objective.SetObjective(title, subtitle);
        }
    }

    /// <summary>
    /// 예전 튜토리얼 방식의 창을 띄우고, 닫기 키를 누를 때까지 기다립니다. 닫으면 창을 내립니다.
    /// </summary>
    /// <param name="pauseTime">켜면 창이 떠 있는 동안 게임 시간을 멈추고, 닫을 때 되돌립니다.</param>
    [TutorialEvent("튜토리얼 창 띄우기 (닫을 때까지 대기)")]
    public IEnumerator ShowWindow(string title, string body, Sprite image, string hint, bool pauseTime)
    {
        DefenseTutorialOverlay window = ResolveWindow();
        if (window == null)
        {
            yield break;
        }

        window.Show(TutorialPage.CreateWindowPage(title, body, image, hint));
        m_isWindowOpen = true;
        if (pauseTime)
        {
            PauseTime();
        }

        // 시간이 멈춰 있어도 닫을 수 있도록 실제 시간으로 셉니다.
        float elapsed = 0.0f;
        while (m_isWindowOpen)
        {
            elapsed += Time.unscaledDeltaTime;
            Keyboard keyboard = Keyboard.current;
            if (elapsed >= m_closeGuardSeconds && keyboard != null && m_closeKey != Key.None && keyboard[m_closeKey].wasPressedThisFrame)
            {
                break;
            }

            yield return null;
        }

        m_isWindowOpen = false;
        window.Hide();
        if (pauseTime)
        {
            ResumeTime();
        }
    }

    /// <summary>게임 시간을 멈추고 분대 조작을 막습니다. 시간 재개를 부르거나 튜토리얼이 끝날 때까지 유지합니다.</summary>
    [TutorialEvent("시간 멈춤")]
    public void PauseTime()
    {
        if (!m_isHoldingPause && Time.timeScale > 0.0f)
        {
            m_resumeTimeScale = Time.timeScale;
        }

        m_isHoldingPause = true;
        Time.timeScale = 0.0f;
        SetSquadGameplayInput(false);
    }

    /// <summary>시간 멈춤으로 멈춘 게임 시간과 분대 조작을 되돌립니다. 멈춘 적이 없으면 아무것도 하지 않습니다.</summary>
    [TutorialEvent("시간 재개")]
    public void ResumeTime()
    {
        if (!m_isHoldingPause)
        {
            return;
        }

        m_isHoldingPause = false;
        if (Time.timeScale <= 0.0f)
        {
            Time.timeScale = m_resumeTimeScale > 0.0f ? m_resumeTimeScale : 1.0f;
        }

        SetSquadGameplayInput(true);
    }

    /// <summary>
    /// 정한 위치에 화살표 마커를 띄웁니다. 화면 밖이면 가장자리에 방향 화살표가 나옵니다. 마커 숨기기를 부르거나 튜토리얼이 끝날 때까지 유지합니다.
    /// </summary>
    /// <param name="position">마커를 띄울 월드 좌표입니다.</param>
    [TutorialEvent("화살표 마커 표시")]
    public void ShowMarker(Vector3 position)
    {
        if (m_marker == null)
        {
            Debug.LogWarning($"{nameof(TutorialEvents)}에 마커용 {nameof(ObjectiveIndicatorController)}가 연결되지 않아 마커를 띄우지 못했습니다.", this);
            return;
        }

        if (m_markerAnchor == null)
        {
            m_markerAnchor = new GameObject("TutorialMarkerAnchor").transform;
        }

        m_markerAnchor.position = position;
        m_marker.SetTarget(m_markerAnchor);
        m_marker.SetVisible(true);
    }

    /// <summary>화살표 마커를 숨깁니다.</summary>
    [TutorialEvent("화살표 마커 숨기기")]
    public void HideMarker()
    {
        if (m_marker != null)
        {
            m_marker.SetVisible(false);
        }
    }

    /// <summary>
    /// 캐릭터 발밑에서 정한 위치까지 NavMesh 경로선과 방향 화살표를 띄웁니다. 캐릭터가 도착 반경에 들어가면 숨깁니다.
    /// </summary>
    /// <param name="position">안내할 위치(월드 좌표)입니다.</param>
    /// <param name="arriveRadius">도착으로 볼 반경(m)입니다. 높이는 보지 않습니다.</param>
    /// <param name="waitForArrival">켜면 도착할 때까지 다음 줄로 넘어가지 않습니다.</param>
    [TutorialEvent("경로선 안내 (도착하면 숨김)")]
    public IEnumerator ShowPathLine(Vector3 position, float arriveRadius, bool waitForArrival)
    {
        ObjectivePathArrow arrow = ResolvePathArrow();
        if (arrow == null)
        {
            Debug.LogWarning($"씬에 {nameof(ObjectivePathArrow)}가 없어 경로선을 띄우지 못했습니다.", this);
            yield break;
        }

        if (m_pathAnchor == null)
        {
            m_pathAnchor = new GameObject("TutorialPathAnchor").transform;
        }

        StopPathWatch();
        m_pathAnchor.position = position;
        arrow.SetTarget(m_pathAnchor);
        arrow.SetVisible(true);

        IEnumerator watch = WatchArrival(position, Mathf.Max(0.1f, arriveRadius));
        if (waitForArrival)
        {
            yield return watch;
        }
        else
        {
            m_pathWatch = StartCoroutine(watch);
        }
    }

    /// <summary>경로선과 방향 화살표를 숨깁니다.</summary>
    [TutorialEvent("경로선 숨기기")]
    public void HidePathLine()
    {
        StopPathWatch();
        if (m_pathArrow != null)
        {
            m_pathArrow.SetVisible(false);
        }
    }

    /// <summary>
    /// 정한 위치 둘레에 적을 만듭니다. 위치는 NavMesh 위로 붙이고, 조작 중인 캐릭터를 바라보게 세웁니다.
    /// </summary>
    /// <param name="prefab">만들 적 프리팹입니다.</param>
    /// <param name="count">만들 수입니다.</param>
    /// <param name="position">만들 위치(월드 좌표)입니다.</param>
    /// <param name="spreadRadius">위치 둘레로 흩어 놓을 반경(m)입니다. 0이면 모두 같은 위치입니다.</param>
    [TutorialEvent("적 생성")]
    public void SpawnEnemies(EnemyController prefab, int count, Vector3 position, float spreadRadius)
    {
        if (prefab == null)
        {
            Debug.LogWarning("적 생성 이벤트에 적 프리팹이 비어 있습니다.", this);
            return;
        }

        SquadMemberController player = SquadManager.Instance != null ? SquadManager.Instance.PlayerSquadMember : null;
        for (int i = 0; i < count; i++)
        {
            Vector2 offset = spreadRadius > 0.0f ? UnityEngine.Random.insideUnitCircle * spreadRadius : Vector2.zero;
            Vector3 spawnPosition = position + new Vector3(offset.x, 0.0f, offset.y);
            if (NavMesh.SamplePosition(spawnPosition, out NavMeshHit hit, m_spawnSampleRadius, NavMesh.AllAreas))
            {
                spawnPosition = hit.position;
            }

            Quaternion rotation = Quaternion.identity;
            if (player != null)
            {
                Vector3 look = player.transform.position - spawnPosition;
                look.y = 0.0f;
                if (look.sqrMagnitude > 0.0001f)
                {
                    rotation = Quaternion.LookRotation(look);
                }
            }

            m_spawnedEnemies.Add(Instantiate(prefab, spawnPosition, rotation));
        }
    }

    /// <summary>
    /// 적 생성으로 만든 적 중 살아 있는 것을 없앱니다. 스포너나 씬에 원래 있던 적은 건드리지 않습니다.
    /// </summary>
    /// <param name="prefab">없앨 적 프리팹입니다. 비우면 프리팹을 가리지 않습니다.</param>
    /// <param name="count">없앨 수입니다. 0이면 모두 없앱니다.</param>
    [TutorialEvent("튜토리얼 적 제거")]
    public void DespawnEnemies(EnemyController prefab, int count)
    {
        int removed = 0;
        for (int i = m_spawnedEnemies.Count - 1; i >= 0; i--)
        {
            EnemyController enemy = m_spawnedEnemies[i];
            if (enemy == null)
            {
                m_spawnedEnemies.RemoveAt(i);
                continue;
            }

            if (!IsFromPrefab(enemy, prefab))
            {
                continue;
            }

            m_spawnedEnemies.RemoveAt(i);
            Destroy(enemy.gameObject);
            removed++;
            if (count > 0 && removed >= count)
            {
                return;
            }
        }
    }

    /// <summary>정한 시간(초, 실제 시간)만큼 기다립니다.</summary>
    [TutorialEvent("대기")]
    public IEnumerator Wait(float seconds)
    {
        float elapsed = 0.0f;
        while (elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    // ───────────── 내부 ─────────────

    private void Update()
    {
        // 로딩 화면처럼 자기가 기억한 배율로 시간을 되돌리는 UI가 늦게 닫힐 수 있어, 멈춘 동안에는 매 프레임 다시 멈춥니다.
        if (m_isHoldingPause && Time.timeScale > 0.0f)
        {
            m_resumeTimeScale = Time.timeScale;
            Time.timeScale = 0.0f;
        }
    }

    private void OnDisable()
    {
        ReleaseAll();
    }

    private IEnumerator WatchArrival(Vector3 position, float radius)
    {
        while (!IsPlayerInArea(position, radius))
        {
            yield return null;
        }

        // 기다리지 않는 안내는 감시 코루틴이 자기 자신이라, 숨기면서 멈추지 않도록 먼저 비웁니다.
        m_pathWatch = null;
        HidePathLine();
    }

    private void StopPathWatch()
    {
        if (m_pathWatch != null)
        {
            StopCoroutine(m_pathWatch);
            m_pathWatch = null;
        }
    }

    private ObjectivePathArrow ResolvePathArrow()
    {
        if (m_pathArrow == null)
        {
            m_pathArrow = FindFirstObjectByType<ObjectivePathArrow>(FindObjectsInactive.Include);
        }

        return m_pathArrow;
    }

    private DefenseTutorialOverlay ResolveWindow()
    {
        if (m_window == null)
        {
            m_window = FindFirstObjectByType<DefenseTutorialOverlay>(FindObjectsInactive.Include);
        }

        return m_window;
    }

    private static void SetSquadGameplayInput(bool enabled)
    {
        SquadManager squadManager = SquadManager.Instance != null
            ? SquadManager.Instance
            : FindFirstObjectByType<SquadManager>(FindObjectsInactive.Include);
        if (squadManager != null)
        {
            squadManager.ApplyInputModeToSquad(enabled);
        }
    }
}
