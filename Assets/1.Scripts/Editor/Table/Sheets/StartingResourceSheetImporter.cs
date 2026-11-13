using System;
using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// <c>StartingResource</c> 시트(자원 하나당 한 행)를 새 게임용 <see cref="DefaultSaveData"/>의 시작 자원에 반영합니다.
/// </summary>
/// <remarks>
/// <see cref="DefaultSaveData"/> 에셋이 프로젝트에 둘 이상 있어 씬이 실제로 참조하는 경로를 고정해 씁니다.
/// </remarks>
internal sealed class StartingResourceSheetImporter : TableSheetImporter
{
    private const string AssetPath = "Assets/5.Data/ScriptableObject/Save/DefaultSaveData.asset";

    public override string SheetName => "StartingResource";

    public override void Import(CsvTable table)
    {
        table.RequireColumns("ResourceId", "Amount");

        List<DefaultSaveData.StartingResourceAmount> rows = new List<DefaultSaveData.StartingResourceAmount>();
        HashSet<string> resourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (CsvRow row in table.Rows)
        {
            string resourceId = ResourceIds.Normalize(row.GetString("ResourceId"));
            int amount = row.GetInt("Amount");

            if (!ResourceIds.IsDefined(resourceId))
                throw row.Error("ResourceId", $"ResourceIds에 없는 자원입니다: \"{resourceId}\" (가능한 값: {string.Join(", ", ResourceIds.All)})");
            if (!resourceIds.Add(resourceId))
                throw row.Error("ResourceId", $"\"{resourceId}\" 행이 두 번 있습니다.");
            if (amount < 0)
                throw row.Error("Amount", "수량은 0 이상이어야 합니다.");

            rows.Add(new DefaultSaveData.StartingResourceAmount(resourceId, amount));
        }

        DefaultSaveData asset = LoadAssetAtPath<DefaultSaveData>(table, AssetPath);
        Undo.RecordObject(asset, $"Import {SheetName} Sheet");
        asset.EditorReplaceStartingResources(rows);
        EditorUtility.SetDirty(asset);
    }
}
