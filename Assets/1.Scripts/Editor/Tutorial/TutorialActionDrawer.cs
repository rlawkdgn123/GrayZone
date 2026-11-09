using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 튜토리얼 이벤트(<see cref="TutorialAction"/>)를 버튼 OnClick처럼 그립니다. 메서드를 드롭다운에서 고르고, 그 메서드의 파라미터 칸만 보여 줍니다.
/// </summary>
/// <remarks>
/// 드롭다운 목록은 <see cref="TutorialEvents"/>에서 <see cref="TutorialEventAttribute"/>가 붙은 메서드입니다.
/// 메서드를 바꾸면 값 목록 길이를 파라미터 수에 맞춥니다. 앞쪽 값은 남기므로 같은 타입이면 그대로 이어 씁니다.
/// </remarks>
[CustomPropertyDrawer(typeof(TutorialAction))]
public sealed class TutorialActionDrawer : PropertyDrawer
{
    private const float Spacing = 2.0f;

    private static MethodInfo[] s_methods;
    private static string[] s_methodLabels;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float line = EditorGUIUtility.singleLineHeight + Spacing;
        if (!property.isExpanded)
        {
            return line;
        }

        MethodInfo method = FindMethod(property.FindPropertyRelative("m_method").stringValue);
        int paramCount = method != null ? method.GetParameters().Length : 0;
        return line * (2 + paramCount);
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EnsureMethods();
        EditorGUI.BeginProperty(position, label, property);

        SerializedProperty methodProp = property.FindPropertyRelative("m_method");
        SerializedProperty argsProp = property.FindPropertyRelative("m_args");
        MethodInfo method = FindMethod(methodProp.stringValue);

        Rect row = new(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        string header = method != null ? GetLabel(method)
            : string.IsNullOrEmpty(methodProp.stringValue) ? "(이벤트 없음)"
            : $"(없는 이벤트: {methodProp.stringValue})";
        property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, $"{label.text}  {header}", true);
        if (!property.isExpanded)
        {
            EditorGUI.EndProperty();
            return;
        }

        EditorGUI.indentLevel++;

        row.y += row.height + Spacing;
        int current = method != null ? Array.IndexOf(s_methods, method) + 1 : 0;
        int picked = EditorGUI.Popup(row, "이벤트", current, s_methodLabels);
        if (picked != current)
        {
            method = picked > 0 ? s_methods[picked - 1] : null;
            methodProp.stringValue = method != null ? method.Name : string.Empty;
        }

        ParameterInfo[] parameters = method != null ? method.GetParameters() : Array.Empty<ParameterInfo>();
        if (argsProp.arraySize != parameters.Length)
        {
            argsProp.arraySize = parameters.Length;
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            row.y += row.height + Spacing;
            DrawArg(row, argsProp.GetArrayElementAtIndex(i), parameters[i]);
        }

        EditorGUI.indentLevel--;
        EditorGUI.EndProperty();
    }

    private static void DrawArg(Rect rect, SerializedProperty arg, ParameterInfo parameter)
    {
        Type type = parameter.ParameterType;
        GUIContent label = new(ObjectNames.NicifyVariableName(parameter.Name), type.Name);

        if (type == typeof(string) && parameter.GetCustomAttribute<TutorialCinematicIdAttribute>() != null)
        {
            TutorialCinematicPopup.Draw(rect, arg.FindPropertyRelative("m_string"), label);
        }
        else if (type == typeof(string))
        {
            SerializedProperty p = arg.FindPropertyRelative("m_string");
            p.stringValue = EditorGUI.TextField(rect, label, p.stringValue);
        }
        else if (type == typeof(float))
        {
            SerializedProperty p = arg.FindPropertyRelative("m_float");
            p.floatValue = EditorGUI.FloatField(rect, label, p.floatValue);
        }
        else if (type == typeof(int))
        {
            SerializedProperty p = arg.FindPropertyRelative("m_int");
            p.intValue = EditorGUI.IntField(rect, label, p.intValue);
        }
        else if (type == typeof(bool))
        {
            SerializedProperty p = arg.FindPropertyRelative("m_bool");
            p.boolValue = EditorGUI.Toggle(rect, label, p.boolValue);
        }
        else if (type == typeof(Vector3))
        {
            DrawPositionField(rect, arg.FindPropertyRelative("m_vector"), label);
        }
        else if (type.IsEnum)
        {
            SerializedProperty p = arg.FindPropertyRelative("m_int");
            Enum value = (Enum)Enum.ToObject(type, p.intValue);
            Enum changed = type.IsDefined(typeof(FlagsAttribute), false)
                ? EditorGUI.EnumFlagsField(rect, label, value)
                : EditorGUI.EnumPopup(rect, label, value);
            p.intValue = Convert.ToInt32(changed);
        }
        else if (typeof(UnityEngine.Object).IsAssignableFrom(type))
        {
            SerializedProperty p = arg.FindPropertyRelative("m_object");
            p.objectReferenceValue = EditorGUI.ObjectField(rect, label, p.objectReferenceValue, type, false);
        }
        else
        {
            EditorGUI.LabelField(rect, label.text, $"{type.Name} 파라미터는 받을 수 없습니다.");
        }
    }

    /// <summary>
    /// 위치 칸입니다. 오른쪽 칸에 씬 오브젝트를 끌어 놓으면 그 위치를 가져옵니다.
    /// </summary>
    /// <remarks>
    /// 시퀀스는 에셋이라 씬 오브젝트를 저장할 수 없어서, 오브젝트가 아니라 놓는 순간의 위치만 저장합니다.
    /// 오브젝트를 옮겼으면 다시 끌어 놓습니다. 저장된 위치는 시퀀스를 선택하면 Scene 뷰에 표시됩니다(<see cref="TutorialSequenceSceneGizmos"/>).
    /// </remarks>
    public static void DrawPositionField(Rect rect, SerializedProperty vector, GUIContent label)
    {
        const float pickWidth = 80.0f;
        Rect fieldRect = new(rect.x, rect.y, rect.width - pickWidth - Spacing, rect.height);
        Rect pickRect = new(fieldRect.xMax + Spacing, rect.y, pickWidth, rect.height);

        // 좁은 Inspector에서 Vector3 칸이 두 줄로 내려가지 않게 한 줄로 그립니다.
        bool wideMode = EditorGUIUtility.wideMode;
        EditorGUIUtility.wideMode = true;
        vector.vector3Value = EditorGUI.Vector3Field(fieldRect, label, vector.vector3Value);
        EditorGUIUtility.wideMode = wideMode;

        int indent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;
        GameObject picked = (GameObject)EditorGUI.ObjectField(pickRect, GUIContent.none, null, typeof(GameObject), true);
        EditorGUI.indentLevel = indent;
        GUI.Label(pickRect, new GUIContent(string.Empty, "씬 오브젝트를 끌어 놓으면 그 위치를 가져옵니다."));
        if (picked != null)
        {
            vector.vector3Value = picked.transform.position;
        }
    }

    private static void EnsureMethods()
    {
        if (s_methods != null)
        {
            return;
        }

        s_methods = TutorialEvents.GetEventMethods().ToArray();
        List<string> labels = new() { "(없음)" };
        labels.AddRange(s_methods.Select(GetLabel));
        s_methodLabels = labels.ToArray();
    }

    private static MethodInfo FindMethod(string name)
    {
        return string.IsNullOrEmpty(name) ? null : TutorialEvents.FindMethod(name);
    }

    private static string GetLabel(MethodInfo method)
    {
        return method.GetCustomAttribute<TutorialEventAttribute>().DisplayName;
    }
}
