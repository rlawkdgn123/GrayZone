using System.Collections.Generic;
using UnityEditor;

/// <summary><c>NpcSpeechLine</c> 시트(대사 한 줄당 한 행)를 <see cref="NpcSpeechTableSO"/>에 반영합니다.</summary>
internal sealed class NpcSpeechLineSheetImporter : TableSheetImporter
{
    public override string SheetName => "NpcSpeechLine";

    public override void Import(CsvTable table)
    {
        table.RequireColumns("NpcId", "MinClearCount", "Line");

        List<NpcSpeechTableSO.LineEntry> rows = new List<NpcSpeechTableSO.LineEntry>();
        foreach (CsvRow row in table.Rows)
        {
            string npcId = row.GetString("NpcId");
            int minClearCount = row.GetInt("MinClearCount");
            // 엑셀 셀 안 줄바꿈(Alt+Enter)과 직접 적은 \n 둘 다 줄바꿈으로 받음
            string line = row.GetString("Line").Replace("\\n", "\n");

            if (string.IsNullOrEmpty(npcId))
                throw row.Error("NpcId", "NPC ID가 비어 있습니다.");
            if (minClearCount < 0)
                throw row.Error("MinClearCount", "클리어 횟수는 0 이상이어야 합니다.");
            if (string.IsNullOrEmpty(line))
                throw row.Error("Line", "대사가 비어 있습니다.");

            rows.Add(new NpcSpeechTableSO.LineEntry
            {
                NpcId = npcId,
                MinClearCount = minClearCount,
                Line = line,
            });
        }

        NpcSpeechTableSO asset = FindSingleAsset<NpcSpeechTableSO>(table);
        Undo.RecordObject(asset, "Import NpcSpeechLine Sheet");
        asset.EditorReplaceLines(rows);
        EditorUtility.SetDirty(asset);
    }
}
