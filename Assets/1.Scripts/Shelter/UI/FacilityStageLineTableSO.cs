using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 시설 UI 단계(Menu/Main/Upgrade)별 담당자 대사를 방어전 클리어 횟수별로 모아 둔 표입니다.
/// </summary>
/// <remarks>
/// 정본은 테이블 시트 <c>Assets/5.Data/DataSheet/TableData/FacilityStageLine.csv</c>입니다.
/// 대사 한 줄당 한 행이며, Inspector에서 고친 값은 다음 시트 가져오기에서 덮어씁니다.
///
/// 한 시설·단계·상황에 대해 클리어 횟수 이하인 <c>MinClearCount</c> 중 가장 큰 묶음을 사용합니다.
/// 상황(Condition) 대사가 없으면 같은 단계의 기본(빈 Condition) 대사로 되돌아갑니다.
/// 행 순서가 같은 묶음 안의 대사 순서입니다.
/// </remarks>
[CreateAssetMenu(fileName = "FacilityStageLineTable", menuName = "GrayZone/Shelter/Facility Stage Line Table")]
public sealed class FacilityStageLineTableSO : ScriptableObject
{
    /// <summary><c>FacilityStageLine</c> 시트 한 행입니다.</summary>
    [Serializable]
    public struct LineEntry
    {
        [Tooltip("FacilityStageDialogue의 시설 ID와 같은 값입니다.")]
        public string FacilityId;
        [Tooltip("대사를 표시할 시설 UI 단계입니다.")]
        public FacilityUIStage Stage;
        [Tooltip("시설별 상황 이름 (예: MedicalCondition). 비우면 해당 단계의 기본 대사입니다.")]
        public string Condition;
        [Tooltip("이 대사를 사용하기 시작하는 방어전 클리어 횟수입니다. 0 = 첫 방어전 전")]
        [Min(0)] public int MinClearCount;
        [TextArea(1, 3)] public string Line;
    }

    private sealed class LineGroup
    {
        public int MinClearCount;
        public string[] Lines;
    }

    [SerializeField] private List<LineEntry> m_lines = new List<LineEntry>();

    // "FacilityId|Stage|Condition" → MinClearCount 오름차순 묶음. 호출마다 같은 배열을 돌려주도록 캐시
    [NonSerialized] private Dictionary<string, List<LineGroup>> m_groupsByKey;

    /// <summary>
    /// 클리어 횟수에 맞는 대사 묶음을 찾습니다. (시설, 단계, 상황) → (시설, 단계, 기본) 순서로 찾고,
    /// 둘 다 없으면 false입니다.
    /// </summary>
    public bool TryGetLines(string facilityId, FacilityDialogueContext context, int clearCount, out string[] lines)
    {
        lines = null;
        if (string.IsNullOrWhiteSpace(facilityId) || context.Stage == FacilityUIStage.None)
            return false;

        BuildGroups();
        string id = facilityId.Trim();
        string condition = context.Condition?.Trim() ?? string.Empty;

        if (condition.Length > 0 && TryGetGroupLines(MakeKey(id, context.Stage, condition), clearCount, out lines))
            return true;

        return TryGetGroupLines(MakeKey(id, context.Stage, string.Empty), clearCount, out lines);
    }

    private bool TryGetGroupLines(string key, int clearCount, out string[] lines)
    {
        lines = null;
        if (!m_groupsByKey.TryGetValue(key, out List<LineGroup> groups))
            return false;

        for (int i = groups.Count - 1; i >= 0; i--)
        {
            if (groups[i].MinClearCount <= clearCount)
            {
                lines = groups[i].Lines;
                return true;
            }
        }

        return false;
    }

    private static string MakeKey(string facilityId, FacilityUIStage stage, string condition) =>
        $"{facilityId}|{stage}|{condition}";

    private void BuildGroups()
    {
        if (m_groupsByKey != null)
            return;

        Dictionary<string, SortedDictionary<int, List<string>>> collected =
            new Dictionary<string, SortedDictionary<int, List<string>>>(StringComparer.OrdinalIgnoreCase);
        foreach (LineEntry entry in m_lines)
        {
            if (string.IsNullOrWhiteSpace(entry.FacilityId)
                || entry.Stage == FacilityUIStage.None
                || string.IsNullOrEmpty(entry.Line))
            {
                continue;
            }

            string key = MakeKey(entry.FacilityId.Trim(), entry.Stage, entry.Condition?.Trim() ?? string.Empty);
            if (!collected.TryGetValue(key, out SortedDictionary<int, List<string>> byCount))
                collected.Add(key, byCount = new SortedDictionary<int, List<string>>());

            int minClearCount = Mathf.Max(0, entry.MinClearCount);
            if (!byCount.TryGetValue(minClearCount, out List<string> lines))
                byCount.Add(minClearCount, lines = new List<string>());

            lines.Add(entry.Line);
        }

        m_groupsByKey = new Dictionary<string, List<LineGroup>>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, SortedDictionary<int, List<string>>> pair in collected)
        {
            List<LineGroup> groups = new List<LineGroup>(pair.Value.Count);
            foreach (KeyValuePair<int, List<string>> group in pair.Value)
                groups.Add(new LineGroup { MinClearCount = group.Key, Lines = group.Value.ToArray() });

            m_groupsByKey.Add(pair.Key, groups);
        }
    }

    private void OnValidate()
    {
        m_groupsByKey = null;
    }

#if UNITY_EDITOR
    /// <summary>시트 임포터 전용입니다. 검증을 마친 <c>FacilityStageLine</c> 시트 행으로 통째로 바꿉니다.</summary>
    public void EditorReplaceLines(List<LineEntry> rows)
    {
        m_lines = rows;
        m_groupsByKey = null;
    }
#endif
}
