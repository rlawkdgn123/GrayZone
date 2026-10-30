using System;
using System.Collections.Generic;
using UnityEditor;

/// <summary><c>FacilityStageLine</c> 시트(대사 한 줄당 한 행)를 <see cref="FacilityStageLineTableSO"/>에 반영합니다.</summary>
internal sealed class FacilityStageLineSheetImporter : TableSheetImporter
{
    // 시설 ID → 그 시설 UI가 보내는 상황 enum. 상황을 쓰는 시설을 추가하면 여기에 한 줄 추가
    private static readonly Dictionary<string, Type> ConditionTypes =
        new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            { "Medical", typeof(MedicalCondition) },
        };

    public override string SheetName => "FacilityStageLine";

    public override void Import(CsvTable table)
    {
        table.RequireColumns("FacilityId", "Stage", "Condition", "MinClearCount", "Line");

        List<FacilityStageLineTableSO.LineEntry> rows = new List<FacilityStageLineTableSO.LineEntry>();
        foreach (CsvRow row in table.Rows)
        {
            string facilityId = row.GetString("FacilityId");
            FacilityUIStage stage = row.GetEnum<FacilityUIStage>("Stage");
            int minClearCount = row.GetInt("MinClearCount");
            // 엑셀 셀 안 줄바꿈(Alt+Enter)과 직접 적은 \n 둘 다 줄바꿈으로 받음
            string line = row.GetString("Line").Replace("\\n", "\n");

            if (string.IsNullOrEmpty(facilityId))
                throw row.Error("FacilityId", "시설 ID가 비어 있습니다.");
            if (stage == FacilityUIStage.None)
                throw row.Error("Stage", "None 단계에는 대사를 둘 수 없습니다. (Menu, Main, Upgrade 중 하나)");
            if (minClearCount < 0)
                throw row.Error("MinClearCount", "클리어 횟수는 0 이상이어야 합니다.");
            if (string.IsNullOrEmpty(line))
                throw row.Error("Line", "대사가 비어 있습니다.");

            rows.Add(new FacilityStageLineTableSO.LineEntry
            {
                FacilityId = facilityId,
                Stage = stage,
                Condition = ReadCondition(row, facilityId),
                MinClearCount = minClearCount,
                Line = line,
            });
        }

        FacilityStageLineTableSO asset = FindSingleAsset<FacilityStageLineTableSO>(table);
        Undo.RecordObject(asset, "Import FacilityStageLine Sheet");
        asset.EditorReplaceLines(rows);
        EditorUtility.SetDirty(asset);
    }

    // 빈 값은 기본 상황. 값이 있으면 그 시설의 상황 enum 이름인지 확인하고 정식 표기로 맞춤
    private static string ReadCondition(CsvRow row, string facilityId)
    {
        string condition = row.GetString("Condition");
        if (condition.Length == 0)
            return string.Empty;

        if (!ConditionTypes.TryGetValue(facilityId, out Type conditionType))
            throw row.Error("Condition", $"{facilityId} 시설은 상황 값을 쓰지 않습니다. 비워 두세요.");

        foreach (string name in Enum.GetNames(conditionType))
        {
            if (string.Equals(name, condition, StringComparison.OrdinalIgnoreCase))
                return name;
        }

        throw row.Error("Condition", $"{conditionType.Name}에 없는 값입니다: \"{condition}\" (가능한 값: {string.Join(", ", Enum.GetNames(conditionType))})");
    }
}
