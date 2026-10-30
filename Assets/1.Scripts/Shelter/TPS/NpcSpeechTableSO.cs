using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 셸터 NPC 말풍선 대사를 방어전 클리어 횟수별로 모아 둔 표입니다.
/// </summary>
/// <remarks>
/// 정본은 테이블 시트 <c>Assets/5.Data/DataSheet/TableData/NpcSpeechLine.csv</c>입니다.
/// 대사 한 줄당 한 행이며, Inspector에서 고친 값은 다음 시트 가져오기에서 덮어씁니다.
///
/// 한 NPC에 대해 클리어 횟수 이하인 <c>MinClearCount</c> 중 가장 큰 묶음을 사용합니다.
/// 예를 들어 0과 1 묶음이 있으면 첫 방어전 클리어 전에는 0, 클리어 후에는 1 묶음이 나옵니다.
/// 행 순서가 같은 묶음 안의 대사 순서입니다.
/// </remarks>
[CreateAssetMenu(fileName = "NpcSpeechTable", menuName = "GrayZone/Shelter/NPC Speech Table")]
public sealed class NpcSpeechTableSO : ScriptableObject
{
    /// <summary><c>NpcSpeechLine</c> 시트 한 행입니다.</summary>
    [Serializable]
    public struct LineEntry
    {
        [Tooltip("NpcSpeechBubble의 NPC ID와 같은 값입니다.")]
        public string NpcId;
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

    // NpcId → MinClearCount 오름차순 묶음. 호출마다 같은 배열을 돌려주도록 캐시
    [NonSerialized] private Dictionary<string, List<LineGroup>> m_groupsByNpc;

    /// <summary>클리어 횟수에 맞는 대사 묶음을 찾습니다. 해당 NPC의 행이 없거나 맞는 묶음이 없으면 false입니다.</summary>
    public bool TryGetLines(string npcId, int clearCount, out string[] lines)
    {
        lines = null;
        if (string.IsNullOrEmpty(npcId))
            return false;

        BuildGroups();
        if (!m_groupsByNpc.TryGetValue(npcId, out List<LineGroup> groups))
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

    private void BuildGroups()
    {
        if (m_groupsByNpc != null)
            return;

        Dictionary<string, SortedDictionary<int, List<string>>> collected =
            new Dictionary<string, SortedDictionary<int, List<string>>>(StringComparer.OrdinalIgnoreCase);
        foreach (LineEntry entry in m_lines)
        {
            if (string.IsNullOrWhiteSpace(entry.NpcId) || string.IsNullOrEmpty(entry.Line))
                continue;

            string npcId = entry.NpcId.Trim();
            if (!collected.TryGetValue(npcId, out SortedDictionary<int, List<string>> byCount))
                collected.Add(npcId, byCount = new SortedDictionary<int, List<string>>());

            int minClearCount = Mathf.Max(0, entry.MinClearCount);
            if (!byCount.TryGetValue(minClearCount, out List<string> lines))
                byCount.Add(minClearCount, lines = new List<string>());

            lines.Add(entry.Line);
        }

        m_groupsByNpc = new Dictionary<string, List<LineGroup>>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, SortedDictionary<int, List<string>>> pair in collected)
        {
            List<LineGroup> groups = new List<LineGroup>(pair.Value.Count);
            foreach (KeyValuePair<int, List<string>> group in pair.Value)
                groups.Add(new LineGroup { MinClearCount = group.Key, Lines = group.Value.ToArray() });

            m_groupsByNpc.Add(pair.Key, groups);
        }
    }

    private void OnValidate()
    {
        m_groupsByNpc = null;
    }

#if UNITY_EDITOR
    /// <summary>시트 임포터 전용입니다. 검증을 마친 <c>NpcSpeechLine</c> 시트 행으로 통째로 바꿉니다.</summary>
    public void EditorReplaceLines(List<LineEntry> rows)
    {
        m_lines = rows;
        m_groupsByNpc = null;
    }
#endif
}
