using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
// NPC 대화, 캐릭터 회전, 시설 UI 전환 연결
public sealed class NpcFacilityDialogueInteractable : MonoBehaviour, IInteractable
{
    private static readonly int TurnLeftHash = Animator.StringToHash("Turn_Left");
    private static readonly int TurnRightHash = Animator.StringToHash("Turn_Right");

    [Header("Facility")]
    [Tooltip("시설 UI 열기 요청 대상")]
    [SerializeField] private UIManager m_uiManager;
    [Tooltip("대화 종료 후 열 시설. 캐릭터와 다른 오브젝트 지정 가능")]
    [SerializeField] private FacilityInteractionPoint m_facilityInteractionPoint;

    [Header("Dialogue")]
    [Tooltip("대화 문장 표시 대상")]
    [SerializeField] private TMP_Text m_dialogueText;
    [Tooltip("클릭으로 대화 완료 처리")]
    [SerializeField] private Button m_dialogueButton;
    [Min(1f)]
    [Tooltip("초당 표시 글자 수")]
    [SerializeField] private float m_charactersPerSecond = 30f;

    [Header("Character")]
    [FormerlySerializedAs("m_animator")]
    [Tooltip("좌우 회전 Trigger 실행 대상")]
    [SerializeField] private Animator m_characterAnimator;
    [FormerlySerializedAs("m_rotationRoot")]
    [Tooltip("플레이어 방향으로 실제 회전할 Transform")]
    [SerializeField] private Transform m_characterRoot;
    [Min(0f)]
    [Tooltip("이 값보다 작은 회전은 Trigger 생략")]
    [SerializeField] private float m_minimumTurnAngle = 3f;
    [Min(0.01f)]
    [Tooltip("목표 방향까지 회전 시간")]
    [SerializeField] private float m_turnDuration = 1f;

    private PlayerMove m_playerMove;
    private CameraLook m_cameraLook;
    private Coroutine m_revealRoutine;
    private Coroutine m_turnRoutine;
    private string m_dialogueLine;
    private bool m_dialogueActive;
    private int m_dialogueOpenedFrame = -1;

    public float HoldDuration => 0f;

    private void Reset()
    {
        // 같은 오브젝트 구성 기준 자동 연결
        m_facilityInteractionPoint = GetComponent<FacilityInteractionPoint>();
        m_characterAnimator = GetComponentInChildren<Animator>();
        m_characterRoot = transform;
    }

    private void Awake()
    {
        CacheReferences();

        if (m_dialogueText != null)
        {
            m_dialogueLine = m_dialogueText.text;
            m_dialogueText.gameObject.SetActive(false);
        }
    }

    private void OnEnable()
    {
        if (m_dialogueButton != null)
            m_dialogueButton.onClick.AddListener(HandleDialogueClicked);
    }

    private void Update()
    {
        // 첫 상호작용 입력과 같은 프레임의 즉시 종료 방지
        if (!m_dialogueActive || Time.frameCount <= m_dialogueOpenedFrame)
            return;

        if (WasDialogueCompletePressed())
            TryCompleteDialogue();
    }

    private void OnDisable()
    {
        if (m_dialogueButton != null)
            m_dialogueButton.onClick.RemoveListener(HandleDialogueClicked);

        // 비활성화 중 남은 코루틴, 대화창, 입력 잠금 정리
        StopActiveRoutines();
        HideDialogue();
        m_dialogueOpenedFrame = -1;

        if (m_uiManager == null || !m_uiManager.HasOpenBlockingUI)
            SetPlayerControlLocked(false);
    }

    public bool CanInteract(GameObject interactor)
    {
        return isActiveAndEnabled
            && !m_dialogueActive
            && interactor != null
            && m_uiManager != null
            && m_facilityInteractionPoint != null
            && m_facilityInteractionPoint.InteractionEnabled
            && !m_uiManager.HasOpenBlockingUI;
    }

    public string GetPrompt() => "Talk";

    public void Interact(GameObject interactor)
    {
        if (!CanInteract(interactor))
            return;

        CacheReferences();
        CachePlayerControls(interactor);

        if (m_uiManager == null
            || m_facilityInteractionPoint == null
            || m_dialogueText == null
            || m_dialogueButton == null)
        {
            Debug.LogWarning("[NpcFacilityDialogueInteractable] Required references are missing.", this);
            return;
        }

        // 대화 시작: 조작 잠금, 회전, 타이핑 순서
        m_dialogueActive = true;
        m_dialogueOpenedFrame = Time.frameCount;
        m_uiManager.HideInteraction();
        SetPlayerControlLocked(true);
        BeginTurning(interactor.transform);
        ShowDialogue();
    }

    private void CacheReferences()
    {
        // Inspector 미할당 항목을 같은 오브젝트 기준으로 보완
        if (m_uiManager == null)
            m_uiManager = FindFirstObjectByType<UIManager>();

        if (m_facilityInteractionPoint == null)
            m_facilityInteractionPoint = GetComponent<FacilityInteractionPoint>();

        if (m_characterAnimator == null)
            m_characterAnimator = GetComponentInChildren<Animator>();

        if (m_characterRoot == null)
            m_characterRoot = transform;
    }

    private void CachePlayerControls(GameObject interactor)
    {
        // 상호작용 플레이어의 이동 및 시점 제어 캐시
        m_playerMove = interactor.GetComponentInParent<PlayerMove>();
        if (m_playerMove == null)
            m_playerMove = FindFirstObjectByType<PlayerMove>();

        m_cameraLook = FindFirstObjectByType<CameraLook>();
    }

    private void ShowDialogue()
    {
        // 저장된 원문을 처음부터 타이핑 표시
        m_dialogueText.text = m_dialogueLine;
        m_dialogueText.maxVisibleCharacters = 0;
        m_dialogueText.gameObject.SetActive(true);

        if (m_revealRoutine != null)
            StopCoroutine(m_revealRoutine);

        m_revealRoutine = StartCoroutine(RevealDialogue());
    }

    private IEnumerator RevealDialogue()
    {
        m_dialogueText.ForceMeshUpdate();
        int characterCount = m_dialogueText.textInfo.characterCount;
        float visibleCharacterCount = 0f;

        while (m_dialogueText.maxVisibleCharacters < characterCount)
        {
            visibleCharacterCount += m_charactersPerSecond * Time.unscaledDeltaTime;
            m_dialogueText.maxVisibleCharacters = Mathf.Min(
                characterCount,
                Mathf.FloorToInt(visibleCharacterCount));
            yield return null;
        }

        m_revealRoutine = null;
    }

    private void HandleDialogueClicked()
    {
        TryCompleteDialogue();
    }

    private bool TryCompleteDialogue()
    {
        // 클릭과 다음 입력의 공통 완료 경로
        if (!m_dialogueActive || m_uiManager == null)
            return false;

        // 지정 시설 UI가 열린 뒤에만 대화창 종료
        if (!m_uiManager.TryOpenTargetUI(m_facilityInteractionPoint.gameObject))
        {
            Debug.LogWarning("[NpcFacilityDialogueInteractable] Facility UI could not be opened.", this);
            return false;
        }

        if (m_revealRoutine != null)
        {
            StopCoroutine(m_revealRoutine);
            m_revealRoutine = null;
        }

        m_dialogueText.maxVisibleCharacters = int.MaxValue;
        HideDialogue();
        m_dialogueActive = false;
        m_dialogueOpenedFrame = -1;
        return true;
    }

    private bool WasDialogueCompletePressed()
    {
        // 대화 중 아무 키보드 입력 또는 마우스 좌클릭 감지
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            foreach (var key in keyboard.allKeys)
            {
                if (key.wasPressedThisFrame)
                    return true;
            }
        }

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            return true;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.anyKeyDown || Input.GetMouseButtonDown(0))
            return true;
#endif

        return false;
    }

    private void HideDialogue()
    {
        if (m_dialogueText != null)
            m_dialogueText.gameObject.SetActive(false);
    }

    private void BeginTurning(Transform interactor)
    {
        if (m_characterRoot == null || interactor == null)
            return;

        Vector3 direction = interactor.position - m_characterRoot.position;
        direction.y = 0f;

        if (direction.sqrMagnitude <= Mathf.Epsilon)
            return;

        // 수평 방향만 사용해 좌우 Trigger 선택
        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        float signedAngle = Vector3.SignedAngle(
            m_characterRoot.forward,
            direction,
            Vector3.up);

        if (m_characterAnimator != null && Mathf.Abs(signedAngle) >= m_minimumTurnAngle)
        {
            m_characterAnimator.ResetTrigger(TurnLeftHash);
            m_characterAnimator.ResetTrigger(TurnRightHash);
            m_characterAnimator.SetTrigger(signedAngle < 0f ? TurnLeftHash : TurnRightHash);
        }

        if (m_turnRoutine != null)
            StopCoroutine(m_turnRoutine);

        m_turnRoutine = StartCoroutine(RotateTo(targetRotation));
    }

    private IEnumerator RotateTo(Quaternion targetRotation)
    {
        // 회전 애니메이션과 같은 시간대에 캐릭터 루트 보간
        Quaternion startRotation = m_characterRoot.rotation;
        float elapsed = 0f;

        while (elapsed < m_turnDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / m_turnDuration);
            float smoothedProgress = progress * progress * (3f - 2f * progress);
            m_characterRoot.rotation = Quaternion.Slerp(
                startRotation,
                targetRotation,
                smoothedProgress);
            yield return null;
        }

        m_characterRoot.rotation = targetRotation;
        m_turnRoutine = null;
    }

    private void StopActiveRoutines()
    {
        if (m_revealRoutine != null)
        {
            StopCoroutine(m_revealRoutine);
            m_revealRoutine = null;
        }

        if (m_turnRoutine != null)
        {
            StopCoroutine(m_turnRoutine);
            m_turnRoutine = null;
        }
    }

    private void SetPlayerControlLocked(bool locked)
    {
        // 대화 중 플레이어 이동 및 시점 입력 잠금
        if (m_playerMove != null)
            m_playerMove.SetMoveLocked(locked);

        if (m_cameraLook != null)
            m_cameraLook.SetLookLocked(locked);
    }
}
