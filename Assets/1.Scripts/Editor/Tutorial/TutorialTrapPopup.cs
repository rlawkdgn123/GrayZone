using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 함정 경로 칸을 열린 씬의 함정 목록 드롭다운으로 그립니다. 함정 종류(스크립트)별로 나눠 보여 줍니다.
/// </summary>
/// <remarks>
/// 시퀀스는 에셋이라 씬 오브젝트를 저장할 수 없어서 하이어라키 경로를 저장합니다. 함정 이름이나 부모를 바꾸면 다시 골라야 합니다.
/// 오른쪽 칸에 씬의 함정 오브젝트를 끌어 놓아도 됩니다.
/// </remarks>
public static class TutorialTrapPopup
{
    private const float PickWidth = 80.0f;

    /// <summary>함정 경로 드롭다운을 그립니다.</summary>
    public static void Draw(Rect rect, SerializedProperty pathProp, GUIContent label)
    {
        Rect popupRect = new(rect.x, rect.y, rect.width - PickWidth - 2.0f, rect.height);
        Rect pickRect = new(popupRect.xMax + 2.0f, rect.y, PickWidth, rect.height);

        string path = pathProp.stringValue ?? string.Empty;
        List<string> labels = new() { "(없음)" };
        List<string> paths = new() { string.Empty };
        Dictionary<string, int> counts = new();
        foreach (Trap trap in Object.FindObjectsByType<Trap>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (EditorUtility.IsPersistent(trap))
            {
                continue;
            }

            string trapPath = TutorialEvents.GetHierarchyPath(trap.transform);
            counts[trapPath] = counts.TryGetValue(trapPath, out int count) ? count + 1 : 1;
            paths.Add(trapPath);
            labels.Add($"{trap.GetType().Name}/{trapPath.Replace("/", " › ")}");
        }

        // 1번째 항목(없음)은 정렬하지 않습니다.
        List<int> order = new();
        for (int i = 1; i < labels.Count; i++)
        {
            order.Add(i);
        }

        order.Sort((a, b) => string.CompareOrdinal(labels[a], labels[b]));
        List<string> sortedLabels = new() { labels[0] };
        List<string> sortedPaths = new() { paths[0] };
        foreach (int i in order)
        {
            sortedLabels.Add(counts[paths[i]] > 1 ? labels[i] + "  ⚠ 같은 경로 여러 개" : labels[i]);
            sortedPaths.Add(paths[i]);
        }

        int current = sortedPaths.IndexOf(path);
        if (current < 0)
        {
            sortedLabels.Add($"⚠ 씬에 없는 함정 ({path})");
            sortedPaths.Add(path);
            current = sortedPaths.Count - 1;
        }

        int picked = EditorGUI.Popup(popupRect, label, current, sortedLabels.ConvertAll(text => new GUIContent(text)).ToArray());
        if (picked != current)
        {
            pathProp.stringValue = sortedPaths[picked];
        }

        int indent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;
        Trap dropped = (Trap)EditorGUI.ObjectField(pickRect, GUIContent.none, null, typeof(Trap), true);
        EditorGUI.indentLevel = indent;
        GUI.Label(pickRect, new GUIContent(string.Empty, "씬의 함정 오브젝트를 끌어 놓으면 그 함정을 고릅니다."));
        if (dropped != null && !EditorUtility.IsPersistent(dropped))
        {
            pathProp.stringValue = TutorialEvents.GetHierarchyPath(dropped.transform);
        }
    }
}
