using System.Collections;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
// NPC 머리 위 말풍선. 등록한 대사를 지정 주기마다 타이핑 연출로 표시
public sealed class NpcSpeechBubble : MonoBehaviour
{
    private const int MinimumLineCount = 2;

    [Header("Lines")]
    [Tooltip("방어전 클리어 횟수별 대사 표 (NpcSpeechLine 시트). 비우면 기본 대사 사용")]
    [SerializeField] private NpcSpeechTableSO m_speechTable;
    [Tooltip("대사 표에서 이 NPC의 대사를 찾을 ID (시트의 NpcId)")]
    [SerializeField] private string m_npcId;
    [Tooltip("기본 대사 목록 (최소 2개). 대사 표에 맞는 대사가 없을 때 사용")]
    [TextArea(1, 3)]
    [SerializeField] private string[] m_lines = { "안녕하세요.", "오늘도 무사하길." };
    [Tooltip("체크 시 무작위 순서 (같은 대사 연속 방지), 해제 시 순서대로 반복")]
    [SerializeField] private bool m_randomOrder;

    [Header("Timing")]
    [Min(0.1f)]
    [Tooltip("대사 하나가 시작된 뒤 다음 대사가 시작되기까지의 주기(초)")]
    [SerializeField] private float m_interval = 6f;
    [Min(0.1f)]
    [Tooltip("대사가 화면에 떠 있는 시간(초). 주기보다 길면 다음 대사 직전까지 유지")]
    [SerializeField] private float m_displayDuration = 3f;
    [Min(0f)]
    [Tooltip("활성화 후 첫 대사까지 대기 시간(초)")]
    [SerializeField] private float m_startDelay = 0.5f;
    [Min(0f)]
    [Tooltip("초당 표시 글자 수. 0이면 한 번에 표시")]
    [SerializeField] private float m_charactersPerSecond = 20f;
    [Min(0f)]
    [Tooltip("말풍선 페이드 인/아웃 시간(초)")]
    [SerializeField] private float m_fadeDuration = 0.15f;
    [Tooltip("활성화 시 자동 재생")]
    [SerializeField] private bool m_playOnEnable = true;

    [Header("References")]
    [Tooltip("말풍선 루트 (World Space Canvas 또는 그 하위 오브젝트)")]
    [SerializeField] private RectTransform m_bubbleRoot;
    [Tooltip("페이드용 CanvasGroup. 비우면 즉시 켜고 끔")]
    [SerializeField] private CanvasGroup m_canvasGroup;
    [Tooltip("대사 표시 대상")]
    [SerializeField] private TMP_Text m_text;

    [Header("Billboard")]
    [Tooltip("말풍선이 항상 카메라를 바라보도록 회전")]
    [SerializeField] private bool m_faceCamera = true;
    [Tooltip("비우면 Camera.main 사용")]
    [SerializeField] private Camera m_targetCamera;

    [Header("Suppress")]
    [Tooltip("차단형 UI(시설 UI, 업그레이드 UI, 업그레이드 연출)가 열려 있는 동안 말풍선 숨김. 비우면 자동 탐색")]
    [SerializeField] private UIManager m_uiManager;

    [Header("Distance")]
    [Tooltip("표시 거리 안에서는 비활성화, 표시 거리 밖에서는 활성화할 월드 아이콘")]
    [SerializeField] private GameObject m_worldIcon;
    [Tooltip("거리 기준 대상. 비우면 PlayerMove 자동 탐색")]
    [SerializeField] private Transform m_distanceTarget;
    [Min(0f)]
    [Tooltip("이 거리(수평) 안으로 들어오면 말풍선 표시 + 월드 아이콘 숨김. 0이면 거리 판정 안 함")]
    [SerializeField] private float m_showDistance = 6f;
    [Min(0f)]
    [Tooltip("경계 깜빡임 방지 여유 거리. 표시 거리 + 이 값 밖으로 나가야 월드 아이콘으로 전환")]
    [SerializeField] private float m_hideMargin = 1f;

    private Coroutine m_loopRoutine;
    private string[] m_activeLines;
    private int m_lineIndex = -1;
    private bool m_playRequested;
    private bool m_inRange;

    public bool IsPlaying => m_loopRoutine != null;

    // 차단형 UI가 열려 있거나 NPC 상호작용 대사가 표시 중이면 말풍선 억제
    private bool IsSuppressed => m_uiManager != null && m_uiManager.IsHudSuppressed;

    // 차단형 UI가 없고 대상이 표시 거리 안에 있을 때만 말풍선 재생
    private bool CanSpeak => !IsSuppressed && m_inRange;

    private void Reset()
    {
        m_text = GetComponentInChildren<TMP_Text>(true);
        m_canvasGroup = GetComponentInChildren<CanvasGroup>(true);
        if (m_canvasGroup != null)
        {
            m_bubbleRoot = m_canvasGroup.transform as RectTransform;
        }
    }

    private void OnValidate()
    {
        if (m_lines == null || m_lines.Length < MinimumLineCount)
        {
            Debug.LogWarning($"[NpcSpeechBubble] {name}: 대사는 최소 {MinimumLineCount}개 이상 등록해야 합니다.", this);
        }

        if (m_speechTable != null && string.IsNullOrWhiteSpace(m_npcId))
        {
            Debug.LogWarning($"[NpcSpeechBubble] {name}: 대사 표를 쓰려면 NPC ID를 지정해야 합니다.", this);
        }
    }

    private void Awake()
    {
        SetBubbleVisible(false, 0f);
    }

    private void OnEnable()
    {
        // UIManager는 말풍선을 모름. 말풍선이 UI 상태 변경만 구독
        if (m_uiManager == null)
        {
            m_uiManager = FindFirstObjectByType<UIManager>();
        }

        if (m_uiManager != null)
        {
            m_uiManager.HudSuppressionChanged += HandleHudSuppressionChanged;
        }

        // 첫 프레임부터 말풍선 재생 여부가 거리와 맞도록 초기 판정
        m_inRange = false;
        m_inRange = EvaluateInRange();
        SetWorldIconVisible(!m_inRange);

        if (m_playOnEnable)
        {
            Play();
        }
    }

    private void OnDisable()
    {
        if (m_uiManager != null)
        {
            m_uiManager.HudSuppressionChanged -= HandleHudSuppressionChanged;
        }

        Stop();
        SetWorldIconVisible(true);
    }

    private void Update()
    {
        bool inRange = EvaluateInRange();
        if (inRange == m_inRange)
        {
            return;
        }

        m_inRange = inRange;
        SetWorldIconVisible(!m_inRange);

        if (!m_inRange)
        {
            StopLoop();
        }
        else if (m_playRequested && !IsPlaying && CanSpeak)
        {
            StartLoop();
        }
    }

    private void LateUpdate()
    {
        if (!m_faceCamera || m_bubbleRoot == null)
        {
            return;
        }

        Camera cam = m_targetCamera != null ? m_targetCamera : Camera.main;
        if (cam == null)
        {
            return;
        }

        // 카메라 정면 방향과 평행하게 맞춰 화면에 반듯하게 보이도록 처리
        m_bubbleRoot.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
    }

    // 대사 주기 재생 요청. 차단형 UI가 열려 있으면 닫힌 뒤 시작
    public void Play()
    {
        m_playRequested = true;
        StopLoop();
        if (CanSpeak)
        {
            StartLoop();
        }
    }

    // 재생 중지 후 말풍선 숨김. UI가 닫혀도 다시 시작하지 않음
    public void Stop()
    {
        m_playRequested = false;
        StopLoop();
    }

    private void HandleHudSuppressionChanged(bool suppressed)
    {
        if (suppressed)
        {
            StopLoop();
        }
        else if (m_playRequested && !IsPlaying && CanSpeak)
        {
            StartLoop();
        }
    }

    // 표시 중이면 여유 거리까지 유지해 경계에서 깜빡이지 않도록 처리
    private bool EvaluateInRange()
    {
        if (m_showDistance <= 0f)
        {
            return true;
        }

        if (m_distanceTarget == null)
        {
            PlayerMove player = FindFirstObjectByType<PlayerMove>();
            if (player == null)
            {
                return false;
            }

            m_distanceTarget = player.transform;
        }

        Vector3 offset = m_distanceTarget.position - transform.position;
        offset.y = 0f;
        float limit = m_inRange ? m_showDistance + m_hideMargin : m_showDistance;
        return offset.sqrMagnitude <= limit * limit;
    }

    private void StartLoop()
    {
        // 클리어 횟수는 귀환 정산 때만 바뀌므로 루프 시작 시점에 대사 묶음 확정
        string[] lines = ResolveLines();
        if (lines != m_activeLines)
        {
            m_activeLines = lines;
            m_lineIndex = -1;
        }

        if (m_text == null || m_activeLines == null || m_activeLines.Length == 0)
        {
            return;
        }

        m_loopRoutine = StartCoroutine(SpeechLoop());
    }

    // 대사 표에서 방어전 클리어 횟수에 맞는 대사 선택. 없으면 기본 대사
    private string[] ResolveLines()
    {
        if (m_speechTable == null || string.IsNullOrWhiteSpace(m_npcId))
        {
            return m_lines;
        }

        GameDataManager gameData = GameDataManager.Instance;
        int clearCount = gameData != null ? gameData.DefenseClearCount : 0;
        return m_speechTable.TryGetLines(m_npcId.Trim(), clearCount, out string[] lines) ? lines : m_lines;
    }

    private void StopLoop()
    {
        if (m_loopRoutine != null)
        {
            StopCoroutine(m_loopRoutine);
            m_loopRoutine = null;
        }

        SetBubbleVisible(false, 0f);
    }

    private IEnumerator SpeechLoop()
    {
        if (m_startDelay > 0f)
        {
            yield return new WaitForSeconds(m_startDelay);
        }

        while (true)
        {
            float startTime = Time.time;
            float visibleTime = Mathf.Min(m_displayDuration, m_interval);

            yield return ShowLine(NextLine());

            float hideAt = startTime + visibleTime;
            while (Time.time < hideAt)
            {
                yield return null;
            }

            yield return Fade(1f, 0f);

            float nextAt = startTime + m_interval;
            while (Time.time < nextAt)
            {
                yield return null;
            }
        }
    }

    private IEnumerator ShowLine(string line)
    {
        // 비활성 상태에서는 textInfo 갱신이 안 되므로 먼저 활성화
        SetBubbleVisible(true, 0f);
        m_text.text = line;
        m_text.maxVisibleCharacters = 0;
        m_text.ForceMeshUpdate();
        int totalCharacters = m_text.textInfo.characterCount;

        yield return Fade(0f, 1f);

        if (m_charactersPerSecond <= 0f)
        {
            m_text.maxVisibleCharacters = totalCharacters;
            yield break;
        }

        float elapsed = 0f;
        while (m_text.maxVisibleCharacters < totalCharacters)
        {
            elapsed += Time.deltaTime;
            m_text.maxVisibleCharacters = Mathf.Min(totalCharacters, Mathf.FloorToInt(elapsed * m_charactersPerSecond));
            yield return null;
        }
    }

    private string NextLine()
    {
        int count = m_activeLines.Length;
        if (count == 1)
        {
            m_lineIndex = 0;
        }
        else if (m_randomOrder)
        {
            // 직전 대사를 제외한 범위에서 선택
            int next = Random.Range(0, count - 1);
            if (next >= m_lineIndex && m_lineIndex >= 0)
            {
                next++;
            }

            m_lineIndex = next;
        }
        else
        {
            m_lineIndex = (m_lineIndex + 1) % count;
        }

        return m_activeLines[m_lineIndex];
    }

    private IEnumerator Fade(float from, float to)
    {
        if (m_canvasGroup == null || m_fadeDuration <= 0f)
        {
            SetBubbleVisible(to > 0f, to);
            yield break;
        }

        SetBubbleVisible(true, from);
        float elapsed = 0f;
        while (elapsed < m_fadeDuration)
        {
            elapsed += Time.deltaTime;
            m_canvasGroup.alpha = Mathf.Lerp(from, to, elapsed / m_fadeDuration);
            yield return null;
        }

        SetBubbleVisible(to > 0f, to);
    }

    private void SetBubbleVisible(bool visible, float alpha)
    {
        if (m_canvasGroup != null)
        {
            m_canvasGroup.alpha = alpha;
        }

        if (m_bubbleRoot != null && m_bubbleRoot.gameObject != gameObject)
        {
            m_bubbleRoot.gameObject.SetActive(visible);
        }
    }

    // 월드 아이콘은 말풍선 페이드와 무관하게 거리 판정으로만 토글
    private void SetWorldIconVisible(bool visible)
    {
        if (m_worldIcon != null && m_worldIcon.activeSelf != visible)
        {
            m_worldIcon.SetActive(visible);
        }
    }
}
