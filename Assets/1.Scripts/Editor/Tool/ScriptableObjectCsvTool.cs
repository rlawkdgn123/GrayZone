using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generic CSV &lt;-&gt; ScriptableObject sync tool for ANY ScriptableObject type
/// in the project. One CSV per type (columns = serialized fields). Assets are
/// identified by GUID so the same file round-trips back onto the same assets.
///
/// Simple fields (numbers, bool, string, enum, Vector*, Color, Object refs) are
/// written as human-readable cells; complex fields (nested structs, arrays of
/// structs, etc.) fall back to JSON encoded inside a single cell.
///
/// Editor-only. All writes go through SerializedObject (Undo / dirty / save safe).
/// No runtime code is touched.
/// </summary>
public class ScriptableObjectCsvWindow : EditorWindow
{
    // Reserved (non-field) columns.
    private const string ColType = "__Type";
    private const string ColGuid = "__Guid";
    private const string ColPath = "__Path";

    // CSV files live here: export destination + auto-import watch folder.
    public const string CsvFolder = "Assets/5.Other/CsvData";

    // New .asset files are created here when an import row has no matching asset.
    private const string NewAssetFolder = "Assets/5.Other/SOData";

    private readonly List<TypeEntry> m_types = new List<TypeEntry>();
    private int m_selectedIndex;

    private struct TypeEntry
    {
        public Type type;
        public int count;
        public string label;
    }

    [MenuItem("GrayZone/SO CSV Tool")]
    private static void Open()
    {
        ScriptableObjectCsvWindow window = GetWindow<ScriptableObjectCsvWindow>("SO CSV Tool");
        window.minSize = new Vector2(420, 220);
        window.ScanTypes();
        window.Show();
    }

    private void OnEnable()
    {
        if (m_types.Count == 0)
            ScanTypes();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("ScriptableObject ↔ CSV", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            $"Export writes one CSV per type into {CsvFolder}. Any CSV changed in that folder is auto-imported (baked) into its SO assets. Import matches assets by __Guid (then __Path); unmatched rows create new assets. Complex fields are JSON-encoded inside a cell.",
            MessageType.Info);

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"Detected SO types: {m_types.Count}", GUILayout.Width(220));
            if (GUILayout.Button("Rescan", GUILayout.Width(90)))
                ScanTypes();
        }

        if (m_types.Count == 0)
        {
            EditorGUILayout.HelpBox("No ScriptableObject assets found under Assets/.", MessageType.Warning);
        }
        else
        {
            string[] labels = new string[m_types.Count];
            for (int i = 0; i < m_types.Count; i++)
                labels[i] = m_types[i].label;

            m_selectedIndex = Mathf.Clamp(m_selectedIndex, 0, m_types.Count - 1);
            m_selectedIndex = EditorGUILayout.Popup("Type", m_selectedIndex, labels);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Export Selected Type → CsvData", GUILayout.Height(28)))
                    ExportType(m_types[m_selectedIndex].type);

                if (GUILayout.Button("Export ALL Types → CsvData", GUILayout.Height(28)))
                    ExportAllTypes();
            }
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Import CSV → SO (auto-detect type)", GUILayout.Height(28)))
            ImportCsv();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Reserved columns: __Type, __Guid, __Path", EditorStyles.miniLabel);
    }

    // ---------------------------------------------------------------- scanning

    private void ScanTypes()
    {
        m_types.Clear();
        Dictionary<Type, int> counts = new Dictionary<Type, int>();

        foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ScriptableObject so = AssetDatabase.LoadMainAssetAtPath(path) as ScriptableObject;
            if (so == null)
                continue;

            Type type = so.GetType();
            counts.TryGetValue(type, out int c);
            counts[type] = c + 1;
        }

        foreach (KeyValuePair<Type, int> kv in counts)
        {
            m_types.Add(new TypeEntry
            {
                type = kv.Key,
                count = kv.Value,
                label = $"{kv.Key.FullName}  ({kv.Value})"
            });
        }

        m_types.Sort((a, b) => string.CompareOrdinal(a.type.FullName, b.type.FullName));
        Repaint();
    }

    // ---------------------------------------------------------------- export

    private void ExportType(Type type)
    {
        EnsureFolder(CsvFolder);
        string path = $"{CsvFolder}/{type.Name}.csv";
        int count = WriteCsvForType(type, path);
        AssetDatabase.ImportAsset(path);

        Debug.Log($"[SoCsv] Exported {count} '{type.Name}' asset(s) to {path}");
        PingAsset(path);
        EditorUtility.DisplayDialog("SO CSV", $"Exported {count} '{type.Name}' asset(s) to:\n{path}", "OK");
    }

    private void ExportAllTypes()
    {
        EnsureFolder(CsvFolder);
        int files = 0;
        int total = 0;
        foreach (TypeEntry entry in m_types)
        {
            string path = $"{CsvFolder}/{entry.type.Name}.csv";
            total += WriteCsvForType(entry.type, path);
            AssetDatabase.ImportAsset(path);
            files++;
        }

        AssetDatabase.Refresh();
        Debug.Log($"[SoCsv] Exported {total} asset(s) across {files} type file(s) to {CsvFolder}");
        EditorUtility.DisplayDialog("SO CSV", $"Exported {total} asset(s) into {files} CSV file(s):\n{CsvFolder}", "OK");
    }

    /// <summary>
    /// Exports one ScriptableObject type without showing editor UI.
    /// Used by reverse-sync tools after they update a balance asset.
    /// </summary>
    public static (int count, string path) ExportTypeSilently(Type type)
    {
        if (type == null || !typeof(ScriptableObject).IsAssignableFrom(type))
        {
            Debug.LogError("[SoCsv] Export requires a ScriptableObject type.");
            return (-1, string.Empty);
        }

        EnsureFolder(CsvFolder);
        string path = $"{CsvFolder}/{type.Name}.csv";
        int count = WriteCsvForType(type, path);
        AssetDatabase.ImportAsset(path);
        return (count, path);
    }

    private static int WriteCsvForType(Type type, string filePath)
    {
        List<ScriptableObject> assets = LoadAllOfType(type);

        List<string> columns = CollectColumns(assets);
        StringBuilder sb = new StringBuilder();

        List<string> header = new List<string> { ColType, ColGuid, ColPath };
        header.AddRange(columns);
        sb.AppendLine(JoinCsv(header));

        foreach (ScriptableObject asset in assets)
        {
            string assetPath = AssetDatabase.GetAssetPath(asset);
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            SerializedObject so = new SerializedObject(asset);

            List<string> row = new List<string>
            {
                type.FullName,
                guid,
                assetPath
            };

            foreach (string column in columns)
            {
                SerializedProperty prop = so.FindProperty(column);
                row.Add(prop != null ? SoCsvCodec.EncodeCell(prop) : string.Empty);
            }

            sb.AppendLine(JoinCsv(row));
        }

        File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
        return assets.Count;
    }

    private static List<string> CollectColumns(List<ScriptableObject> assets)
    {
        List<string> ordered = new List<string>();
        HashSet<string> seen = new HashSet<string>();

        foreach (ScriptableObject asset in assets)
        {
            SerializedObject so = new SerializedObject(asset);
            SerializedProperty it = so.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.name == "m_Script")
                    continue;
                if (seen.Add(it.name))
                    ordered.Add(it.name);
            }
        }

        return ordered;
    }

    // ---------------------------------------------------------------- import

    private void ImportCsv()
    {
        string path = EditorUtility.OpenFilePanel("Import CSV", FullFolder(CsvFolder), "csv");
        if (string.IsNullOrEmpty(path))
            return;

        if (!EditorUtility.DisplayDialog(
                "SO CSV",
                $"Import from:\n{path}\n\nAssets are matched by __Guid (then __Path) and updated; unmatched rows create new assets. Supports Undo.",
                "Import",
                "Cancel"))
        {
            return;
        }

        (int updated, int created, int skipped) = ImportFile(path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string summary = $"Updated: {updated}\nCreated: {created}\nSkipped: {skipped}";
        EditorUtility.DisplayDialog("SO CSV", summary, "OK");
        ScanTypes();
    }

    /// <summary>
    /// Reads one CSV file and bakes its rows into ScriptableObject assets.
    /// Shared by the manual Import button and the auto-import post-processor.
    /// Performs no UI; returns the operation counts.
    /// </summary>
    public static (int updated, int created, int skipped) ImportFile(string filePath)
    {
        int updated = 0, created = 0, skipped = 0;

        if (!File.Exists(filePath))
        {
            Debug.LogWarning($"[SoCsv] CSV not found: {filePath}");
            return (updated, created, skipped);
        }

        List<List<string>> rows = SoCsvText.ParseCsv(File.ReadAllText(filePath, Encoding.UTF8));
        if (rows.Count < 2)
        {
            Debug.LogWarning($"[SoCsv] '{filePath}': needs a header and at least one data row.");
            return (updated, created, skipped);
        }

        Dictionary<string, int> col = SoCsvText.BuildColumnMap(rows[0]);
        if (!col.ContainsKey(ColType))
        {
            Debug.LogWarning($"[SoCsv] '{filePath}': missing required column '{ColType}'. File skipped.");
            return (updated, created, skipped);
        }

        AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = 1; i < rows.Count; i++)
            {
                List<string> row = rows[i];
                string typeName = SoCsvText.Cell(row, col, ColType).Trim();
                Type type = SoTypeResolver.Resolve(typeName);
                if (type == null)
                {
                    Debug.LogWarning($"[SoCsv] Row {i + 1} skipped: unknown type '{typeName}'.");
                    skipped++;
                    continue;
                }

                string guid = SoCsvText.Cell(row, col, ColGuid).Trim();
                string assetPath = SoCsvText.Cell(row, col, ColPath).Trim();

                ScriptableObject asset = ResolveAsset(guid, assetPath, type, out bool isNew);
                if (asset == null)
                {
                    skipped++;
                    continue;
                }

                if (isNew) created++; else updated++;

                ApplyRow(asset, type, row, col);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
        }

        Debug.Log($"[SoCsv] Imported '{Path.GetFileName(filePath)}' → Updated:{updated} Created:{created} Skipped:{skipped}");
        return (updated, created, skipped);
    }

    private static void ApplyRow(ScriptableObject asset, Type type, List<string> row, Dictionary<string, int> col)
    {
        Undo.RecordObject(asset, "Import ScriptableObject CSV");
        SerializedObject so = new SerializedObject(asset);

        foreach (KeyValuePair<string, int> kv in col)
        {
            string column = kv.Key;
            if (column == ColType || column == ColGuid || column == ColPath)
                continue;

            SerializedProperty prop = so.FindProperty(column);
            if (prop == null)
            {
                Debug.LogWarning($"[SoCsv] '{type.Name}': no field '{column}'. Cell ignored.", asset);
                continue;
            }

            string cell = kv.Value < row.Count ? row[kv.Value] : string.Empty;
            SoCsvCodec.DecodeCell(prop, cell, $"{type.Name}.{column}");
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(asset);
    }

    private static ScriptableObject ResolveAsset(string guid, string assetPath, Type type, out bool isNew)
    {
        isNew = false;

        // 1) by GUID
        if (!string.IsNullOrEmpty(guid))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(p))
            {
                ScriptableObject existing = AssetDatabase.LoadAssetAtPath(p, type) as ScriptableObject;
                if (existing != null)
                    return existing;
            }
        }

        // 2) by path
        if (!string.IsNullOrEmpty(assetPath))
        {
            ScriptableObject existing = AssetDatabase.LoadAssetAtPath(assetPath, type) as ScriptableObject;
            if (existing != null)
                return existing;
        }

        // 3) create new
        string targetPath = !string.IsNullOrEmpty(assetPath) ? assetPath : $"{NewAssetFolder}/{type.Name}.asset";
        string folder = Path.GetDirectoryName(targetPath)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(folder))
        {
            Debug.LogError($"[SoCsv] Could not resolve target folder for new '{type.Name}' asset.");
            return null;
        }

        EnsureFolder(folder);

        ScriptableObject created = ScriptableObject.CreateInstance(type);
        string unique = AssetDatabase.GenerateUniqueAssetPath(targetPath);
        AssetDatabase.CreateAsset(created, unique);
        Undo.RegisterCreatedObjectUndo(created, "Create ScriptableObject");
        Debug.Log($"[SoCsv] Created new asset: {unique}", created);
        isNew = true;
        return created;
    }

    // ---------------------------------------------------------------- helpers

    private static List<ScriptableObject> LoadAllOfType(Type type)
    {
        List<ScriptableObject> list = new List<ScriptableObject>();
        foreach (string guid in AssetDatabase.FindAssets($"t:{type.Name}"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ScriptableObject so = AssetDatabase.LoadAssetAtPath(path, type) as ScriptableObject;
            // FindAssets("t:Name") can over-match by short name; keep only exact type.
            if (so != null && so.GetType() == type)
                list.Add(so);
        }
        list.Sort((a, b) => string.CompareOrdinal(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b)));
        return list;
    }

    private static string FullFolder(string assetFolder)
    {
        return AssetDatabase.IsValidFolder(assetFolder) ? Path.GetFullPath(assetFolder) : Path.GetFullPath("Assets");
    }

    /// <summary>Creates an "Assets/..." folder (and any missing parents) if it does not exist.</summary>
    private static void EnsureFolder(string assetFolder)
    {
        assetFolder = assetFolder.Replace('\\', '/').TrimEnd('/');
        if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder))
            return;

        string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
        string leaf = Path.GetFileName(assetFolder);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
            return;

        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    private static void PingAsset(string assetPath)
    {
        UnityEngine.Object obj = AssetDatabase.LoadMainAssetAtPath(assetPath);
        if (obj != null)
            EditorGUIUtility.PingObject(obj);
    }

    private static string JoinCsv(List<string> cells)
    {
        for (int i = 0; i < cells.Count; i++)
            cells[i] = SoCsvText.Escape(cells[i]);
        return string.Join(",", cells);
    }
}

// ===================================================================== codec

/// <summary>Encodes/decodes a single SerializedProperty to/from a CSV cell.</summary>
internal static class SoCsvCodec
{
    public static string EncodeCell(SerializedProperty p)
    {
        switch (p.propertyType)
        {
            case SerializedPropertyType.Integer: return p.longValue.ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.Boolean: return p.boolValue ? "TRUE" : "FALSE";
            case SerializedPropertyType.Float: return p.doubleValue.ToString("R", CultureInfo.InvariantCulture);
            case SerializedPropertyType.String: return p.stringValue ?? string.Empty;
            case SerializedPropertyType.Enum: return EnumName(p);
            case SerializedPropertyType.ObjectReference: return EncodeObjectRef(p.objectReferenceValue);
            case SerializedPropertyType.Vector2: return V(p.vector2Value.x, p.vector2Value.y);
            case SerializedPropertyType.Vector3: return V(p.vector3Value.x, p.vector3Value.y, p.vector3Value.z);
            case SerializedPropertyType.Vector4: return V(p.vector4Value.x, p.vector4Value.y, p.vector4Value.z, p.vector4Value.w);
            case SerializedPropertyType.Quaternion: return V(p.quaternionValue.x, p.quaternionValue.y, p.quaternionValue.z, p.quaternionValue.w);
            case SerializedPropertyType.Color: return V(p.colorValue.r, p.colorValue.g, p.colorValue.b, p.colorValue.a);
            default:
                object tree = SoCsvTree.Read(p);
                return tree == null ? string.Empty : MiniJson.Serialize(tree);
        }
    }

    public static void DecodeCell(SerializedProperty p, string cell, string context)
    {
        cell ??= string.Empty;
        try
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: p.longValue = ParseLong(cell); break;
                case SerializedPropertyType.Boolean: p.boolValue = ParseBool(cell); break;
                case SerializedPropertyType.Float: p.doubleValue = ParseDouble(cell); break;
                case SerializedPropertyType.String: p.stringValue = cell; break;
                case SerializedPropertyType.Enum: SetEnumByName(p, cell.Trim()); break;
                case SerializedPropertyType.ObjectReference: p.objectReferenceValue = DecodeObjectRef(cell); break;
                case SerializedPropertyType.Vector2: { float[] f = Floats(cell, 2); p.vector2Value = new Vector2(f[0], f[1]); break; }
                case SerializedPropertyType.Vector3: { float[] f = Floats(cell, 3); p.vector3Value = new Vector3(f[0], f[1], f[2]); break; }
                case SerializedPropertyType.Vector4: { float[] f = Floats(cell, 4); p.vector4Value = new Vector4(f[0], f[1], f[2], f[3]); break; }
                case SerializedPropertyType.Quaternion: { float[] f = Floats(cell, 4); p.quaternionValue = new Quaternion(f[0], f[1], f[2], f[3]); break; }
                case SerializedPropertyType.Color: { float[] f = Floats(cell, 4); p.colorValue = new Color(f[0], f[1], f[2], f[3]); break; }
                default:
                    if (!string.IsNullOrWhiteSpace(cell))
                        SoCsvTree.Write(p, MiniJson.Deserialize(cell));
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SoCsv] Failed to decode '{context}' from cell '{cell}': {e.Message}");
        }
    }

    // ----- object references stored as "guid:localId" -----

    public static string EncodeObjectRef(UnityEngine.Object obj)
    {
        if (obj == null)
            return string.Empty;
        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long localId))
            return $"{guid}:{localId}";
        return string.Empty;
    }

    public static UnityEngine.Object DecodeObjectRef(string value)
    {
        value = (value ?? string.Empty).Trim();
        if (value.Length == 0)
            return null;

        string guid = value;
        long localId = 0;
        int colon = value.IndexOf(':');
        if (colon > 0)
        {
            guid = value.Substring(0, colon);
            long.TryParse(value.Substring(colon + 1), out localId);
        }

        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path))
            return null;

        if (localId != 0)
        {
            foreach (UnityEngine.Object candidate in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (candidate != null
                    && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate, out _, out long id)
                    && id == localId)
                    return candidate;
            }
        }

        return AssetDatabase.LoadMainAssetAtPath(path);
    }

    // ----- primitives -----

    private static string EnumName(SerializedProperty p)
    {
        return p.enumValueIndex >= 0 && p.enumValueIndex < p.enumNames.Length
            ? p.enumNames[p.enumValueIndex]
            : p.intValue.ToString(CultureInfo.InvariantCulture);
    }

    public static void SetEnumByName(SerializedProperty p, string name)
    {
        int idx = Array.IndexOf(p.enumNames, name);
        if (idx < 0 && int.TryParse(name, out int raw) && raw >= 0 && raw < p.enumNames.Length)
            idx = raw;
        p.enumValueIndex = idx >= 0 ? idx : 0;
    }

    private static string V(params float[] xs)
    {
        string[] s = new string[xs.Length];
        for (int i = 0; i < xs.Length; i++)
            s[i] = xs[i].ToString("R", CultureInfo.InvariantCulture);
        return string.Join(";", s);
    }

    private static float[] Floats(string cell, int n)
    {
        float[] result = new float[n];
        string[] parts = (cell ?? string.Empty).Split(';');
        for (int i = 0; i < n; i++)
            result[i] = i < parts.Length && float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0f;
        return result;
    }

    public static long ParseLong(string s) => long.TryParse((s ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : 0;
    public static double ParseDouble(string s) => double.TryParse((s ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;

    public static bool ParseBool(string s)
    {
        s = (s ?? string.Empty).Trim();
        return s.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || s == "1"
            || s.Equals("Y", StringComparison.OrdinalIgnoreCase) || s.Equals("YES", StringComparison.OrdinalIgnoreCase);
    }
}

// ============================================================ property <-> tree

/// <summary>
/// Recursively converts a SerializedProperty subtree to/from plain objects
/// (Dictionary / List / scalar) for JSON fallback of complex fields.
/// </summary>
internal static class SoCsvTree
{
    public static object Read(SerializedProperty p)
    {
        if (p.isArray && p.propertyType == SerializedPropertyType.Generic)
        {
            List<object> list = new List<object>(p.arraySize);
            for (int i = 0; i < p.arraySize; i++)
                list.Add(Read(p.GetArrayElementAtIndex(i)));
            return list;
        }

        switch (p.propertyType)
        {
            case SerializedPropertyType.Integer: return p.longValue;
            case SerializedPropertyType.Boolean: return p.boolValue;
            case SerializedPropertyType.Float: return p.doubleValue;
            case SerializedPropertyType.String: return p.stringValue ?? string.Empty;
            case SerializedPropertyType.Enum: return p.enumValueIndex >= 0 && p.enumValueIndex < p.enumNames.Length ? p.enumNames[p.enumValueIndex] : (object)p.intValue;
            case SerializedPropertyType.ObjectReference: return SoCsvCodec.EncodeObjectRef(p.objectReferenceValue);
            case SerializedPropertyType.Vector2: return new List<object> { (double)p.vector2Value.x, (double)p.vector2Value.y };
            case SerializedPropertyType.Vector3: return new List<object> { (double)p.vector3Value.x, (double)p.vector3Value.y, (double)p.vector3Value.z };
            case SerializedPropertyType.Vector4: return new List<object> { (double)p.vector4Value.x, (double)p.vector4Value.y, (double)p.vector4Value.z, (double)p.vector4Value.w };
            case SerializedPropertyType.Quaternion: return new List<object> { (double)p.quaternionValue.x, (double)p.quaternionValue.y, (double)p.quaternionValue.z, (double)p.quaternionValue.w };
            case SerializedPropertyType.Color: return new List<object> { (double)p.colorValue.r, (double)p.colorValue.g, (double)p.colorValue.b, (double)p.colorValue.a };
            case SerializedPropertyType.Generic:
                Dictionary<string, object> dict = new Dictionary<string, object>();
                SerializedProperty end = p.GetEndProperty();
                SerializedProperty it = p.Copy();
                bool enter = true;
                while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
                {
                    enter = false;
                    dict[it.name] = Read(it.Copy());
                }
                return dict;
            default:
                return null; // AnimationCurve/Gradient/etc. are not round-tripped.
        }
    }

    public static void Write(SerializedProperty p, object value)
    {
        if (p.isArray && p.propertyType == SerializedPropertyType.Generic)
        {
            List<object> list = value as List<object> ?? new List<object>();
            p.arraySize = list.Count;
            for (int i = 0; i < list.Count; i++)
                Write(p.GetArrayElementAtIndex(i), list[i]);
            return;
        }

        switch (p.propertyType)
        {
            case SerializedPropertyType.Integer: p.longValue = ToLong(value); break;
            case SerializedPropertyType.Boolean: p.boolValue = ToBool(value); break;
            case SerializedPropertyType.Float: p.doubleValue = ToDouble(value); break;
            case SerializedPropertyType.String: p.stringValue = value?.ToString() ?? string.Empty; break;
            case SerializedPropertyType.Enum: SoCsvCodec.SetEnumByName(p, value?.ToString() ?? string.Empty); break;
            case SerializedPropertyType.ObjectReference: p.objectReferenceValue = SoCsvCodec.DecodeObjectRef(value?.ToString()); break;
            case SerializedPropertyType.Vector2: { float[] f = Vec(value, 2); p.vector2Value = new Vector2(f[0], f[1]); break; }
            case SerializedPropertyType.Vector3: { float[] f = Vec(value, 3); p.vector3Value = new Vector3(f[0], f[1], f[2]); break; }
            case SerializedPropertyType.Vector4: { float[] f = Vec(value, 4); p.vector4Value = new Vector4(f[0], f[1], f[2], f[3]); break; }
            case SerializedPropertyType.Quaternion: { float[] f = Vec(value, 4); p.quaternionValue = new Quaternion(f[0], f[1], f[2], f[3]); break; }
            case SerializedPropertyType.Color: { float[] f = Vec(value, 4); p.colorValue = new Color(f[0], f[1], f[2], f[3]); break; }
            case SerializedPropertyType.Generic:
                Dictionary<string, object> dict = value as Dictionary<string, object>;
                if (dict == null)
                    break;
                SerializedProperty end = p.GetEndProperty();
                SerializedProperty it = p.Copy();
                bool enter = true;
                while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
                {
                    enter = false;
                    if (dict.TryGetValue(it.name, out object child))
                        Write(it.Copy(), child);
                }
                break;
        }
    }

    private static float[] Vec(object value, int n)
    {
        float[] result = new float[n];
        if (value is List<object> list)
        {
            for (int i = 0; i < n && i < list.Count; i++)
                result[i] = (float)ToDouble(list[i]);
        }
        return result;
    }

    private static long ToLong(object v) => v is long l ? l : v is double d ? (long)d : long.TryParse(v?.ToString(), out long r) ? r : 0;
    private static double ToDouble(object v) => v is double d ? d : v is long l ? l : double.TryParse(v?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double r) ? r : 0;
    private static bool ToBool(object v) => v is bool b ? b : SoCsvCodec.ParseBool(v?.ToString());
}

// ===================================================================== type resolver

internal static class SoTypeResolver
{
    private static Dictionary<string, Type> s_cache;

    public static Type Resolve(string fullName)
    {
        if (string.IsNullOrEmpty(fullName))
            return null;

        if (s_cache == null)
        {
            s_cache = new Dictionary<string, Type>();
            foreach (Type t in TypeCache.GetTypesDerivedFrom<ScriptableObject>())
            {
                if (!string.IsNullOrEmpty(t.FullName))
                    s_cache[t.FullName] = t;
            }
        }

        return s_cache.TryGetValue(fullName, out Type type) ? type : null;
    }
}

// ===================================================================== CSV text

internal static class SoCsvText
{
    public static string Escape(string value)
    {
        value ??= string.Empty;
        bool quote = value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r");
        return quote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    public static Dictionary<string, int> BuildColumnMap(List<string> header)
    {
        Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < header.Count; i++)
        {
            string key = header[i].Trim();
            if (key.Length > 0 && !map.ContainsKey(key))
                map[key] = i;
        }
        return map;
    }

    public static string Cell(List<string> row, Dictionary<string, int> columns, string name)
    {
        return columns.TryGetValue(name, out int i) && i < row.Count ? row[i] : string.Empty;
    }

    public static List<List<string>> ParseCsv(string text)
    {
        List<List<string>> rows = new List<List<string>>();
        if (string.IsNullOrEmpty(text))
            return rows;

        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text.Substring(1);

        List<string> current = new List<string>();
        StringBuilder field = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }

            switch (c)
            {
                case '"': inQuotes = true; break;
                case ',': current.Add(field.ToString()); field.Clear(); break;
                case '\r':
                    if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                    EndRow(rows, ref current, field);
                    break;
                case '\n':
                    EndRow(rows, ref current, field);
                    break;
                default: field.Append(c); break;
            }
        }

        if (field.Length > 0 || current.Count > 0)
        {
            current.Add(field.ToString());
            if (!IsEmpty(current)) rows.Add(current);
        }

        return rows;
    }

    private static void EndRow(List<List<string>> rows, ref List<string> current, StringBuilder field)
    {
        current.Add(field.ToString());
        field.Clear();
        if (!IsEmpty(current)) rows.Add(current);
        current = new List<string>();
    }

    private static bool IsEmpty(List<string> row)
    {
        foreach (string c in row)
            if (!string.IsNullOrWhiteSpace(c)) return false;
        return true;
    }
}

// ===================================================================== MiniJson

/// <summary>Minimal JSON serializer/parser for the tree types used by SoCsvTree.</summary>
internal static class MiniJson
{
    public static string Serialize(object value)
    {
        StringBuilder sb = new StringBuilder();
        Write(sb, value);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, object v)
    {
        switch (v)
        {
            case null: sb.Append("null"); break;
            case bool b: sb.Append(b ? "true" : "false"); break;
            case string s: WriteString(sb, s); break;
            case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
            case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
            case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
            case float f: sb.Append(((double)f).ToString("R", CultureInfo.InvariantCulture)); break;
            case IDictionary<string, object> dict:
                sb.Append('{');
                bool firstD = true;
                foreach (KeyValuePair<string, object> kv in dict)
                {
                    if (!firstD) sb.Append(',');
                    firstD = false;
                    WriteString(sb, kv.Key);
                    sb.Append(':');
                    Write(sb, kv.Value);
                }
                sb.Append('}');
                break;
            case IEnumerable list:
                sb.Append('[');
                bool firstL = true;
                foreach (object item in list)
                {
                    if (!firstL) sb.Append(',');
                    firstL = false;
                    Write(sb, item);
                }
                sb.Append(']');
                break;
            default: WriteString(sb, v.ToString()); break;
        }
    }

    private static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    public static object Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        int index = 0;
        object result = ParseValue(json, ref index);
        return result;
    }

    private static object ParseValue(string s, ref int i)
    {
        SkipWhitespace(s, ref i);
        if (i >= s.Length) return null;
        char c = s[i];
        switch (c)
        {
            case '{': return ParseObject(s, ref i);
            case '[': return ParseArray(s, ref i);
            case '"': return ParseString(s, ref i);
            case 't': i += 4; return true;
            case 'f': i += 5; return false;
            case 'n': i += 4; return null;
            default: return ParseNumber(s, ref i);
        }
    }

    private static Dictionary<string, object> ParseObject(string s, ref int i)
    {
        Dictionary<string, object> dict = new Dictionary<string, object>();
        i++; // {
        while (true)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length) break;
            if (s[i] == '}') { i++; break; }
            string key = ParseString(s, ref i);
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ':') i++;
            object value = ParseValue(s, ref i);
            dict[key] = value;
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == '}') { i++; break; }
        }
        return dict;
    }

    private static List<object> ParseArray(string s, ref int i)
    {
        List<object> list = new List<object>();
        i++; // [
        while (true)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length) break;
            if (s[i] == ']') { i++; break; }
            list.Add(ParseValue(s, ref i));
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == ']') { i++; break; }
        }
        return list;
    }

    private static string ParseString(string s, ref int i)
    {
        StringBuilder sb = new StringBuilder();
        i++; // opening quote
        while (i < s.Length)
        {
            char c = s[i++];
            if (c == '"') break;
            if (c == '\\' && i < s.Length)
            {
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 <= s.Length)
                        {
                            sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                            i += 4;
                        }
                        break;
                    default: sb.Append(e); break;
                }
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static object ParseNumber(string s, ref int i)
    {
        int start = i;
        bool isFloat = false;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '-' || c == '+' || (c >= '0' && c <= '9')) { i++; }
            else if (c == '.' || c == 'e' || c == 'E') { isFloat = true; i++; }
            else break;
        }
        string num = s.Substring(start, i - start);
        if (!isFloat && long.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
            return l;
        double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double d);
        return d;
    }

    private static void SkipWhitespace(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }
}
