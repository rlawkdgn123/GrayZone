using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Real-time, read-only watch window for the data a Manager holds.
///
/// Drag any GameObject from the Hierarchy into the window; the tool reflects over
/// every MonoBehaviour on it and renders the values it EXPOSES — public instance
/// properties (e.g. RosterCount / ShelterStability / ResourceAmounts / Npcs) and,
/// optionally, [SerializeField] private fields. Nothing is hard-coded per manager:
/// whatever a component exposes is shown automatically. Dictionaries and lists are
/// expanded, complex objects (e.g. NPCRuntimeData) recurse a couple of levels, and
/// values that change are briefly highlighted.
///
/// Editor-only and never mutates anything. Live in Play mode, current values in Edit mode.
/// </summary>
public class ManagerLiveInspectorWindow : EditorWindow
{
    private const float HighlightSeconds = 1.0f;
    private const int MaxRecursionDepth = 3;
    private const int MaxCollectionItems = 50;

    // Watched targets survive domain reloads / entering Play mode (EditorWindow is serialized).
    [SerializeField] private List<GameObject> m_targets = new List<GameObject>();

    private Vector2 m_scroll;
    private string m_filter = string.Empty;
    private bool m_includeSerializedFields = true;
    private bool m_includeProperties = true;
    private bool m_highlightChanges = true;
    private bool m_onlyManagerComponents;
    private float m_refreshHz = 10f;
    private double m_nextRepaint;

    // Per-row UI/diff state, keyed by a stable value path.
    private readonly Dictionary<string, bool> m_expanded = new Dictionary<string, bool>();
    private readonly Dictionary<string, string> m_lastValue = new Dictionary<string, string>();
    private readonly Dictionary<string, double> m_changeTime = new Dictionary<string, double>();

    [MenuItem("GrayZone/Manager Live Inspector")]
    private static void Open()
    {
        ManagerLiveInspectorWindow window = GetWindow<ManagerLiveInspectorWindow>("Manager Live");
        window.minSize = new Vector2(360, 320);
        window.Show();
    }

    private void OnInspectorUpdate()
    {
        // Fires ~10x/sec regardless of focus; throttle to the requested rate.
        double now = EditorApplication.timeSinceStartup;
        if (now < m_nextRepaint)
            return;

        m_nextRepaint = now + (m_refreshHz > 0f ? 1.0 / m_refreshHz : 0.1);
        Repaint();
    }

    private void OnGUI()
    {
        DrawToolbar();
        DrawDropArea();

        m_targets.RemoveAll(t => t == null);
        if (m_targets.Count == 0)
            return;

        m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
        for (int i = 0; i < m_targets.Count; i++)
            DrawTarget(m_targets[i], i);
        EditorGUILayout.EndScrollView();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label(Application.isPlaying ? "● LIVE" : "○ EDIT", GUILayout.Width(50));

            GUILayout.Label("Search", GUILayout.Width(45));
            m_filter = GUILayout.TextField(m_filter ?? string.Empty, GUILayout.Width(140));

            GUILayout.FlexibleSpace();
            GUILayout.Label($"{(int)m_refreshHz} Hz", GUILayout.Width(40));
            m_refreshHz = GUILayout.HorizontalSlider(m_refreshHz, 1f, 30f, GUILayout.Width(80));
        }

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            m_includeProperties = GUILayout.Toggle(m_includeProperties, "Properties", EditorStyles.toolbarButton, GUILayout.Width(80));
            m_includeSerializedFields = GUILayout.Toggle(m_includeSerializedFields, "SerializeFields", EditorStyles.toolbarButton, GUILayout.Width(110));
            m_highlightChanges = GUILayout.Toggle(m_highlightChanges, "Highlight", EditorStyles.toolbarButton, GUILayout.Width(70));
            m_onlyManagerComponents = GUILayout.Toggle(m_onlyManagerComponents, "Only *Manager", EditorStyles.toolbarButton, GUILayout.Width(95));
            GUILayout.FlexibleSpace();
            if (m_targets.Count > 0 && GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50)))
                m_targets.Clear();
        }
    }

    private void DrawDropArea()
    {
        Rect rect = GUILayoutUtility.GetRect(0, 40, GUILayout.ExpandWidth(true));
        rect.xMin += 4;
        rect.xMax -= 4;
        rect.yMin += 2;

        GUI.Box(rect, m_targets.Count == 0
            ? "Drag Manager GameObjects here"
            : "Drag more GameObjects here to watch", EditorStyles.helpBox);

        Event evt = Event.current;
        if (!rect.Contains(evt.mousePosition))
            return;

        if (evt.type == EventType.DragUpdated)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Link;
            evt.Use();
        }
        else if (evt.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            foreach (Object dragged in DragAndDrop.objectReferences)
            {
                GameObject go = dragged as GameObject ?? (dragged as Component)?.gameObject;
                if (go != null && !m_targets.Contains(go))
                    m_targets.Add(go);
            }
            evt.Use();
        }
    }

    // -------------------------------------------------------------- rendering

    private void DrawTarget(GameObject go, int index)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(go.name, EditorStyles.boldLabel);
                if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(40)))
                    EditorGUIUtility.PingObject(go);
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(24)))
                {
                    m_targets.RemoveAt(index);
                    return;
                }
            }

            foreach (MonoBehaviour mb in go.GetComponents<MonoBehaviour>())
            {
                if (mb == null)
                    continue;
                if (m_onlyManagerComponents && mb.GetType().Name.IndexOf("Manager", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                DrawComponent(mb);
            }
        }
    }

    private void DrawComponent(MonoBehaviour component)
    {
        Type type = component.GetType();
        string path = $"{type.Name}#{component.GetInstanceID()}";

        bool typeMatches = string.IsNullOrEmpty(m_filter)
            || type.Name.IndexOf(m_filter, StringComparison.OrdinalIgnoreCase) >= 0;

        bool expanded = GetExpanded(path, true);
        expanded = EditorGUILayout.Foldout(expanded, type.Name, true, EditorStyles.foldoutHeader);
        SetExpanded(path, expanded);
        if (!expanded)
            return;

        EditorGUI.indentLevel++;
        int rowsDrawn = 0;
        if (m_includeProperties)
            rowsDrawn += DrawProperties(component, type, path, typeMatches);
        if (m_includeSerializedFields)
            rowsDrawn += DrawSerializedFields(component, type, path, typeMatches);

        if (rowsDrawn == 0)
            EditorGUILayout.LabelField(string.IsNullOrEmpty(m_filter) ? "(no readable data)" : "(no matches)", EditorStyles.miniLabel);
        EditorGUI.indentLevel--;
    }

    private int DrawProperties(object target, Type type, string path, bool parentMatches)
    {
        int count = 0;
        foreach (PropertyInfo prop in GetReadableProperties(type))
        {
            if (!RowPassesFilter(prop.Name, parentMatches))
                continue;

            object value = SafeGet(() => prop.GetValue(target));
            DrawValueRow(prop.Name, value, $"{path}.{prop.Name}", 0);
            count++;
        }
        return count;
    }

    private int DrawSerializedFields(object target, Type type, string path, bool parentMatches)
    {
        int count = 0;
        foreach (FieldInfo field in GetSerializedFields(type))
        {
            if (!RowPassesFilter(field.Name, parentMatches))
                continue;

            object value = SafeGet(() => field.GetValue(target));
            DrawValueRow(field.Name, value, $"{path}.@{field.Name}", 0);
            count++;
        }
        return count;
    }

    private bool RowPassesFilter(string name, bool parentMatches)
    {
        if (parentMatches || string.IsNullOrEmpty(m_filter))
            return true;
        return name.IndexOf(m_filter, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Renders one name → value row, recursing/expanding as appropriate.</summary>
    private void DrawValueRow(string label, object value, string path, int depth)
    {
        if (TryGetExpandable(value, depth, out IList<KeyValuePair<string, object>> children, out string headerSuffix))
        {
            bool open = GetExpanded(path, depth < 1);
            open = EditorGUILayout.Foldout(open, $"{label}   {headerSuffix}", true);
            SetExpanded(path, open);

            if (open)
            {
                EditorGUI.indentLevel++;
                int shown = 0;
                foreach (KeyValuePair<string, object> child in children)
                {
                    if (shown >= MaxCollectionItems)
                    {
                        EditorGUILayout.LabelField($"... (+{children.Count - shown} more)", EditorStyles.miniLabel);
                        break;
                    }

                    DrawValueRow(child.Key, child.Value, $"{path}/{shown}.{child.Key}", depth + 1);
                    shown++;
                }
                EditorGUI.indentLevel--;
            }
            return;
        }

        string text = FormatScalar(value);
        Color highlight = ComputeHighlight(path, text);

        Rect rect = EditorGUILayout.GetControlRect();
        if (highlight.a > 0f)
            EditorGUI.DrawRect(rect, highlight);

        EditorGUI.LabelField(rect, label, text);
    }

    // -------------------------------------------------------------- value model

    /// <summary>
    /// Decides whether a value should be drawn as an expandable node (dictionary,
    /// list, or nested object) and, if so, yields its labelled children.
    /// </summary>
    private bool TryGetExpandable(object value, int depth, out IList<KeyValuePair<string, object>> children, out string headerSuffix)
    {
        children = null;
        headerSuffix = string.Empty;

        if (value == null || depth >= MaxRecursionDepth)
            return false;

        Type type = value.GetType();
        if (IsScalar(type) || value is Object)
            return false;

        // Dictionaries and lists (covers IReadOnlyDictionary / IReadOnlyList too).
        if (value is IEnumerable enumerable)
        {
            List<KeyValuePair<string, object>> items = new List<KeyValuePair<string, object>>();
            int i = 0;
            foreach (object item in enumerable)
            {
                if (TryReadKeyValue(item, out object k, out object v))
                    items.Add(new KeyValuePair<string, object>(FormatScalar(k), v));
                else
                    items.Add(new KeyValuePair<string, object>($"[{i}]", item));
                i++;
                if (i > MaxCollectionItems + 1)
                    break;
            }

            children = items;
            headerSuffix = $"({items.Count}{(i > MaxCollectionItems + 1 ? "+" : string.Empty)})";
            return true;
        }

        // Plain data object: expand its public properties one level deeper.
        List<KeyValuePair<string, object>> props = new List<KeyValuePair<string, object>>();
        foreach (PropertyInfo prop in GetReadableProperties(type))
        {
            object pv = SafeGet(() => prop.GetValue(value));
            props.Add(new KeyValuePair<string, object>(prop.Name, pv));
        }

        if (props.Count == 0)
            return false;

        children = props;
        headerSuffix = $"{{{type.Name}}}";
        return true;
    }

    private static bool TryReadKeyValue(object item, out object key, out object value)
    {
        key = null;
        value = null;
        if (item == null)
            return false;

        Type t = item.GetType();
        if (!t.IsGenericType || t.Name.IndexOf("KeyValuePair", StringComparison.Ordinal) < 0)
            return false;

        key = t.GetProperty("Key")?.GetValue(item);
        value = t.GetProperty("Value")?.GetValue(item);
        return true;
    }

    private static string FormatScalar(object value)
    {
        switch (value)
        {
            case null:
                return "<null>";
            case string s:
                return s.Length == 0 ? "\"\"" : s;
            case bool b:
                return b ? "true" : "false";
            case float f:
                return f.ToString("0.###", CultureInfo.InvariantCulture);
            case double d:
                return d.ToString("0.###", CultureInfo.InvariantCulture);
            case Object o:
                return o != null ? $"{o.name} ({o.GetType().Name})" : "<missing>";
            case IFormattable formattable:
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            default:
                return value.ToString();
        }
    }

    private static bool IsScalar(Type type)
    {
        return type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4)
            || type == typeof(Quaternion) || type == typeof(Color) || type == typeof(Color32);
    }

    // -------------------------------------------------------------- reflection helpers

    private static readonly Dictionary<Type, PropertyInfo[]> s_propCache = new Dictionary<Type, PropertyInfo[]>();
    private static readonly Dictionary<Type, FieldInfo[]> s_fieldCache = new Dictionary<Type, FieldInfo[]>();

    private static PropertyInfo[] GetReadableProperties(Type type)
    {
        if (s_propCache.TryGetValue(type, out PropertyInfo[] cached))
            return cached;

        List<PropertyInfo> list = new List<PropertyInfo>();
        foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                continue;

            // Skip the noise inherited from Unity base classes (transform, name, ...).
            string ns = prop.DeclaringType?.Namespace ?? string.Empty;
            if (ns.StartsWith("UnityEngine", StringComparison.Ordinal))
                continue;

            list.Add(prop);
        }

        PropertyInfo[] arr = list.ToArray();
        s_propCache[type] = arr;
        return arr;
    }

    private static FieldInfo[] GetSerializedFields(Type type)
    {
        if (s_fieldCache.TryGetValue(type, out FieldInfo[] cached))
            return cached;

        List<FieldInfo> list = new List<FieldInfo>();
        foreach (FieldInfo field in type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public))
        {
            string ns = field.DeclaringType?.Namespace ?? string.Empty;
            if (ns.StartsWith("UnityEngine", StringComparison.Ordinal))
                continue;

            bool serialized = field.IsPublic || field.IsDefined(typeof(SerializeField), true);
            if (!serialized || field.IsDefined(typeof(NonSerializedAttribute), true))
                continue;

            list.Add(field);
        }

        FieldInfo[] arr = list.ToArray();
        s_fieldCache[type] = arr;
        return arr;
    }

    private static object SafeGet(Func<object> getter)
    {
        try
        {
            return getter();
        }
        catch (Exception e)
        {
            return $"<error: {e.GetType().Name}>";
        }
    }

    // -------------------------------------------------------------- diff highlight

    private Color ComputeHighlight(string path, string text)
    {
        double now = EditorApplication.timeSinceStartup;

        if (m_lastValue.TryGetValue(path, out string previous))
        {
            if (previous != text)
                m_changeTime[path] = now;
        }
        m_lastValue[path] = text;

        if (!m_highlightChanges || !m_changeTime.TryGetValue(path, out double changedAt))
            return Color.clear;

        float age = (float)(now - changedAt);
        if (age >= HighlightSeconds)
            return Color.clear;

        float intensity = 1f - (age / HighlightSeconds);
        return new Color(1f, 0.85f, 0.2f, 0.35f * intensity);
    }

    // -------------------------------------------------------------- foldout state

    private bool GetExpanded(string path, bool fallback)
    {
        return m_expanded.TryGetValue(path, out bool value) ? value : fallback;
    }

    private void SetExpanded(string path, bool value)
    {
        m_expanded[path] = value;
    }
}
