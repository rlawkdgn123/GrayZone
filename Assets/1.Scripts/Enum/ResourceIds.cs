using System;
using System.Collections.Generic;

/// <summary>
/// 저장과 런타임에서 사용하는 안정적인 셸터 자원 ID 모음입니다.
/// 표시 이름이나 배열 순서와 독립적이며 저장된 ID는 변경하지 않습니다.
/// </summary>
public static class ResourceIds
{
    public const string UpgradeMaterial = "upgrade_material";
    public const string CraftingMaterial = "crafting_material";
    public const string TrapMaterial = "trap_material";
    public const string TrapUpgradeMaterial = "trap_upgrade_material";

    private static readonly string[] s_all =
    {
        UpgradeMaterial,
        CraftingMaterial,
        TrapMaterial,
        TrapUpgradeMaterial
    };

    public static IReadOnlyList<string> All => s_all;

    public static string Normalize(string resourceId)
    {
        return string.IsNullOrWhiteSpace(resourceId)
            ? string.Empty
            : resourceId.Trim();
    }

    public static bool IsDefined(string resourceId)
    {
        string normalizedId = Normalize(resourceId);
        for (int i = 0; i < s_all.Length; i++)
        {
            if (string.Equals(
                    s_all[i],
                    normalizedId,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

}
