using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 캐릭터 머리 위에 대사 한 줄을 타이핑 연출로 띄우는 말풍선입니다.
/// </summary>
/// <remarks>
/// 쉘터 NPC 말풍선(<see cref="NpcSpeechBubble"/>)의 타이핑, 페이드, 카메라 바라보기를 가져왔습니다.
/// 그쪽은 등록된 대사를 주기마다 스스로 돌리지만, 이쪽은 <see cref="DialoguePlayer"/>가 <see cref="Show"/>로 넘긴 줄만 띄웁니다.
/// 시간은 실제 시간으로 셉니다. 컷신이나 안내 중에 게임 시간이 멈춰도 글자가 이어서 나오게 하기 위해서입니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class DialogueSpeechBubble : MonoBehaviour
{
    [Header("References")]
    [Tooltip("말풍선 루트(World Space Canvas 또는 그 하위)입니다. 이 오브젝트를 켜고 끄는 것으로 표시를 전환합니다.")]
    [SerializeField] private RectTransform m_bubbleRoot;

    [Tooltip("페이드용 CanvasGroup입니다. 선택 항목이며, 비우면 페이드 없이 켜고 끕니다.")]
    [SerializeField] private CanvasGroup m_canvasGroup;

    [Tooltip("대사를 표시할 텍스트입니다.")]
    [SerializeField] private TMP_Text m_text;

    [Header("Timing")]
    [Min(0.0f)]
    [Tooltip("초당 표시 글자 수입니다. 0이면 한 번에 표시합니다.")]
    [SerializeField] private float m_charactersPerSecond = 20.0f;

    [Min(0.0f)]
    [Tooltip("페이드 인/아웃 시간(초)입니다.")]
    [SerializeField] private float m_fadeDuration = 0.15f;

    [Header("Billboard")]
    [Tooltip("켜면 말풍선이 항상 카메라를 바라봅니다.")]
    [SerializeField] private bool m_faceCamera = true;

    [Tooltip("바라볼 카메라입니다. 비우면 Camera.main을 씁니다.")]
    [SerializeField] private Camera m_targetCamera;

    private Coroutine m_routine;

    /// <summary>말풍선이 떠 있는지 여부입니다.</summary>
    public bool IsShown => m_bubbleRoot != null && m_bubbleRoot.gameObject.activeSelf;

    private void Reset()
    {
        m_text = GetComponentInChildren<TMP_Text>(true);
        m_canvasGroup = GetComponentInChildren<CanvasGroup>(true);
        if (m_canvasGroup != null)
        {
            m_bubbleRoot = m_canvasGroup.transform as RectTransform;
        }
    }

    private void Awake()
    {
        SetVisible(false, 0.0f);
    }

    private void OnDisable()
    {
        StopRoutine();
        SetVisible(false, 0.0f);
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

        // 카메라 정면과 평행하게 맞춰 화면에 반듯하게 보이도록 합니다.
        m_bubbleRoot.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
    }

    /// <summary>문구를 처음부터 타이핑해 띄웁니다. 이미 떠 있으면 내용만 바꿔 다시 타이핑합니다.</summary>
    public void Show(string line)
    {
        if (m_text == null)
        {
            return;
        }

        StopRoutine();
        m_routine = StartCoroutine(ShowRoutine(line));
    }

    /// <summary>말풍선을 페이드로 내립니다.</summary>
    public void Hide()
    {
        if (!IsShown)
        {
            return;
        }

        StopRoutine();
        if (!isActiveAndEnabled)
        {
            SetVisible(false, 0.0f);
            return;
        }

        m_routine = StartCoroutine(Fade(m_canvasGroup != null ? m_canvasGroup.alpha : 1.0f, 0.0f));
    }

    private IEnumerator ShowRoutine(string line)
    {
        // 꺼진 상태에서는 textInfo가 갱신되지 않아 먼저 켭니다.
        bool wasShown = IsShown;
        SetVisible(true, wasShown && m_canvasGroup != null ? m_canvasGroup.alpha : 0.0f);
        m_text.text = line;
        m_text.maxVisibleCharacters = 0;
        m_text.ForceMeshUpdate();
        int totalCharacters = m_text.textInfo.characterCount;

        if (!wasShown)
        {
            yield return Fade(0.0f, 1.0f);
        }
        else if (m_canvasGroup != null)
        {
            m_canvasGroup.alpha = 1.0f;
        }

        if (m_charactersPerSecond <= 0.0f)
        {
            m_text.maxVisibleCharacters = totalCharacters;
            m_routine = null;
            yield break;
        }

        float elapsed = 0.0f;
        while (m_text.maxVisibleCharacters < totalCharacters)
        {
            elapsed += Time.unscaledDeltaTime;
            m_text.maxVisibleCharacters = Mathf.Min(totalCharacters, Mathf.FloorToInt(elapsed * m_charactersPerSecond));
            yield return null;
        }

        m_routine = null;
    }

    private IEnumerator Fade(float from, float to)
    {
        if (m_canvasGroup == null || m_fadeDuration <= 0.0f)
        {
            SetVisible(to > 0.0f, to);
            yield break;
        }

        SetVisible(true, from);
        float elapsed = 0.0f;
        while (elapsed < m_fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            m_canvasGroup.alpha = Mathf.Lerp(from, to, elapsed / m_fadeDuration);
            yield return null;
        }

        SetVisible(to > 0.0f, to);
    }

    private void StopRoutine()
    {
        if (m_routine != null)
        {
            StopCoroutine(m_routine);
            m_routine = null;
        }
    }

    private void SetVisible(bool visible, float alpha)
    {
        if (m_canvasGroup != null)
        {
            m_canvasGroup.alpha = alpha;
        }

        if (m_bubbleRoot != null && m_bubbleRoot.gameObject != gameObject && m_bubbleRoot.gameObject.activeSelf != visible)
        {
            m_bubbleRoot.gameObject.SetActive(visible);
        }
    }
}
