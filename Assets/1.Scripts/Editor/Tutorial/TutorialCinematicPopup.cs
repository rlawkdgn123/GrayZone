using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 시네마틱 ID 칸을 씬의 <see cref="TutorialCinematicSettings"/> 목록 드롭다운으로 그립니다.
/// </summary>
/// <remarks>
/// 시퀀스는 에셋이라 씬이 열려 있지 않을 수도 있습니다. 열린 씬에 시네마틱 세팅이 없으면 저장된 ID만 보여 줍니다.
/// </remarks>
public static class TutorialCinematicPopup
{
    /// <summary>시네마틱 ID 드롭다운을 그립니다.</summary>
    public static void Draw(Rect rect, SerializedProperty idProp, GUIContent label)
    {
        TutorialCinematicSettings settings = FindSettings();
        string id = idProp.stringValue;
        if (settings == null)
        {
            string stored = string.IsNullOrEmpty(id) ? "(없음)" : id;
            EditorGUI.LabelField(rect, label, new GUIContent($"씬에 시네마틱 세팅 없음 · {stored}", "TutorialCinematicSettings가 있는 씬을 열면 목록에서 고를 수 있습니다."));
            return;
        }

        List<string> labels = new() { "(없음)" };
        List<string> ids = new() { string.Empty };
        IReadOnlyList<TutorialCinematic> cinematics = settings.Cinematics;
        for (int i = 0; i < cinematics.Count; i++)
        {
            if (cinematics[i] == null || string.IsNullOrEmpty(cinematics[i].Id))
            {
                continue;
            }

            labels.Add($"{i}. {cinematics[i].Name}");
            ids.Add(cinematics[i].Id);
        }

        int current = ids.IndexOf(id ?? string.Empty);
        if (current < 0)
        {
            labels.Add($"⚠ 목록에 없는 시네마틱 ({id})");
            ids.Add(id);
            current = ids.Count - 1;
        }

        int picked = EditorGUI.Popup(rect, label, current, labels.ConvertAll(text => new GUIContent(text)).ToArray());
        if (picked != current)
        {
            idProp.stringValue = ids[picked];
        }
    }

    /// <summary>ID에 해당하는 시네마틱 이름입니다. 열린 씬에서 찾지 못하면 null입니다.</summary>
    public static string GetName(string id)
    {
        TutorialCinematicSettings settings = FindSettings();
        TutorialCinematic cinematic = settings != null ? settings.Find(id) : null;
        return cinematic?.Name;
    }

    private static TutorialCinematicSettings FindSettings()
    {
        return Object.FindFirstObjectByType<TutorialCinematicSettings>(FindObjectsInactive.Include);
    }
}
