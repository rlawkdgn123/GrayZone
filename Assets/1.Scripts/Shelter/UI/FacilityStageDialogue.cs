using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
// 시설 UI 단계(Menu/Main/Upgrade)마다 다른 대사를 타이핑 연출로 표시
public sealed class FacilityStageDialogue : MonoBehaviour
{
    [Serializable]
    private sealed class StageLines
    {
        [Tooltip("대사를 표시할 단계")]
        public FacilityUIStage Stage = FacilityUIStage.Menu;
        [Tooltip("이 단계에 들어올 때 표시할 대사 목록")]
        [TextArea(1, 3)]
        public string[] Lines = Array.Empty<string>();
    }

    [Header("Source")]
    [Tooltip("단계를 알려 줄 시설 UI (ManufacturingUI, MedicalUI, ScrambleFacilityUI). 비우면 부모에서 자동 탐색")]
    [SerializeField] private MonoBehaviour m_stageSource;

    [Header("Lines")]
    [Tooltip("시설 단계별 대사 표 (FacilityStageLine 시트). 비우면 아래 기본 대사 사용")]
    [SerializeField] private FacilityStageLineTableSO m_lineTable;
    [Tooltip("대사 표에서 이 시설의 대사를 찾을 ID (시트의 FacilityId)")]
    [SerializeField] private string m_facilityId;
    [Tooltip("기본 단계별 대사. 대사 표에 맞는 대사가 없을 때 사용하며, 둘 다 없는 단계에서는 대사창 숨김")]
    [SerializeField] private StageLines[] m_stageLines = Array.Empty<StageLines>();
    [Tooltip("체크 시 단계 대사 중 무작위 선택 (같은 대사 연속 방지), 해제 시 순서대로 반복")]
    [SerializeField] private bool m_randomOrder = true;

    [Header("View")]
    [Tooltip("대사 표시 대상")]
    [SerializeField] private TMP_Text m_text;
    [Tooltip("대사가 없을 때 함께 숨길 루트 (말풍선 배경 등). 이 컴포넌트의 자식이어야 하며, 비우면 텍스트만 비움")]
    [SerializeField] private GameObject m_root;
    [Min(0f)]
    [Tooltip("초당 표시 글자 수. 0이면 한 번에 표시")]
    [SerializeField] private float m_charactersPerSecond = 30f;

    // 대사 묶음(배열 참조)별 직전 대사 인덱스
    private readonly Dictionary<string[], int> m_lastLineIndices = new Dictionary<string[], int>();

    private IFacilityStageSource m_source;
    private Coroutine m_revealRoutine;
    private string[] m_shownLines;

    private void Reset()
    {
        m_text = GetComponentInChildren<TMP_Text>(true);
        m_stageSource = GetComponentInParent<IFacilityStageSource>(true) as MonoBehaviour;
    }

    private void OnValidate()
    {
        if (m_stageSource != null && m_stageSource is not IFacilityStageSource)
        {
            Debug.LogWarning($"[FacilityStageDialogue] {name}: {m_stageSource.GetType().Name}은(는) 시설 단계를 알려 주는 UI가 아닙니다.", this);
            m_stageSource = null;
        }

        if (m_lineTable != null && string.IsNullOrWhiteSpace(m_facilityId))
            Debug.LogWarning($"[FacilityStageDialogue] {name}: 대사 표를 쓰려면 시설 ID를 지정해야 합니다.", this);
    }

    private void OnEnable()
    {
        m_source = ResolveSource();
        if (m_source == null)
        {
            Debug.LogWarning("[FacilityStageDialogue] Stage source is not found.", this);
            Hide();
            return;
        }

        m_source.ContextChanged += HandleContextChanged;
        HandleContextChanged(m_source.CurrentContext);
    }

    private void OnDisable()
    {
        if (m_source != null)
            m_source.ContextChanged -= HandleContextChanged;

        m_source = null;
        Hide();
    }

    private IFacilityStageSource ResolveSource()
    {
        if (m_stageSource is IFacilityStageSource source)
            return source;

        return GetComponentInParent<IFacilityStageSource>(true);
    }

    private void HandleContextChanged(FacilityDialogueContext context)
    {
        string[] lines = context.Stage == FacilityUIStage.None ? null : ResolveLines(context);
        if (lines == null || lines.Length == 0)
        {
            Hide();
            return;
        }

        // 상황이 바뀌어도 같은 대사 묶음으로 되돌아가면(기본 대사 폴백 등) 다시 뽑지 않음
        if (lines == m_shownLines)
            return;

        m_shownLines = lines;
        Show(lines[NextLineIndex(lines)]);
    }

    // 대사 표 (시설, 단계, 상황) → (시설, 단계, 기본) → Inspector 기본 대사 순서로 선택
    private string[] ResolveLines(FacilityDialogueContext context)
    {
        if (m_lineTable != null && !string.IsNullOrWhiteSpace(m_facilityId))
        {
            GameDataManager gameData = GameDataManager.Instance;
            int clearCount = gameData != null ? gameData.DefenseClearCount : 0;
            if (m_lineTable.TryGetLines(m_facilityId.Trim(), context, clearCount, out string[] tableLines))
                return tableLines;
        }

        if (m_stageLines == null)
            return null;

        foreach (StageLines entry in m_stageLines)
        {
            if (entry != null && entry.Stage == context.Stage && entry.Lines != null && entry.Lines.Length > 0)
                return entry.Lines;
        }

        return null;
    }

    // 묶음별로 직전 대사 인덱스를 기억해 반복 또는 무작위(연속 중복 제외) 선택
    private int NextLineIndex(string[] lines)
    {
        int count = lines.Length;
        int last = m_lastLineIndices.TryGetValue(lines, out int stored) && stored < count ? stored : -1;
        int next;
        if (count == 1)
        {
            next = 0;
        }
        else if (m_randomOrder)
        {
            next = UnityEngine.Random.Range(0, count - 1);
            if (last >= 0 && next >= last)
                next++;
        }
        else
        {
            next = (last + 1) % count;
        }

        m_lastLineIndices[lines] = next;
        return next;
    }

    private void Show(string line)
    {
        if (m_text == null)
            return;

        StopReveal();
        SetRootActive(true);
        m_text.gameObject.SetActive(true);
        m_text.text = line;

        if (m_charactersPerSecond <= 0f || !isActiveAndEnabled)
        {
            m_text.maxVisibleCharacters = int.MaxValue;
            return;
        }

        m_text.maxVisibleCharacters = 0;
        m_revealRoutine = StartCoroutine(Reveal());
    }

    private IEnumerator Reveal()
    {
        // 비활성 상태에서는 textInfo 갱신이 안 되므로 활성화 후 글자 수 계산
        m_text.ForceMeshUpdate();
        int characterCount = m_text.textInfo.characterCount;
        float visibleCharacterCount = 0f;

        while (m_text.maxVisibleCharacters < characterCount)
        {
            visibleCharacterCount += m_charactersPerSecond * Time.unscaledDeltaTime;
            m_text.maxVisibleCharacters = Mathf.Min(characterCount, Mathf.FloorToInt(visibleCharacterCount));
            yield return null;
        }

        m_revealRoutine = null;
    }

    private void Hide()
    {
        StopReveal();
        m_shownLines = null;

        if (m_text != null)
            m_text.text = string.Empty;

        SetRootActive(false);
    }

    private void StopReveal()
    {
        if (m_revealRoutine == null)
            return;

        StopCoroutine(m_revealRoutine);
        m_revealRoutine = null;
    }

    private void SetRootActive(bool active)
    {
        // 자신이나 부모를 끄면 단계 구독이 끊기므로 그런 루트는 건드리지 않고 텍스트만 비움
        if (m_root == null || transform.IsChildOf(m_root.transform) || m_root.activeSelf == active)
            return;

        m_root.SetActive(active);
    }
}
