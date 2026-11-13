using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 튜토리얼 블록(<see cref="TutorialBlock"/>)을 종류에 맞는 칸만 보이게 그립니다.
/// </summary>
/// <remarks>
/// 접힌 줄에 블록 내용을 요약합니다. 트리거는 챕터 안 순번(#1, #2…)을 붙이고, 엔드포인트는 닫는 트리거 번호를 보여 줍니다.
/// 엔드포인트는 위쪽에서 아직 닫히지 않은 트리거를 체크해서 고르고, 여러 개를 함께 닫을 수 있습니다.
/// 종류 드롭다운에는 위쪽에 열린 트리거가 있을 때만 엔드포인트가 나옵니다. 연결은 진행기와 같은 <see cref="TutorialBlockLinks"/>로 셉니다.
/// </remarks>
[CustomPropertyDrawer(typeof(TutorialBlock))]
public sealed class TutorialBlockDrawer : PropertyDrawer
{
    private const float Spacing = 2.0f;
    private const int SummaryLength = 28;

    private static readonly string[] s_typeLabels = { "대사", "트리거", "엔드포인트", "시네마틱" };

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float line = EditorGUIUtility.singleLineHeight + Spacing;
        if (!property.isExpanded)
        {
            return line;
        }

        float height = line * 2;
        if (GetType(property) == TutorialBlockType.Endpoint)
        {
            Context context = GetContext(property);
            height += line * (1 + Mathf.Max(1, context.Open.Count));
        }

        foreach (SerializedProperty field in GetFields(property))
        {
            height += GetFieldHeight(field) + Spacing;
        }

        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        Context context = GetContext(property);
        TutorialBlockType type = GetType(property);
        if (type == TutorialBlockType.Trigger && context.Index >= 0 && context.Links.IsDuplicateId[context.Index])
        {
            // 새로 만들었거나 복제해서 ID가 비었거나 겹치면 새 ID를 붙입니다.
            property.FindPropertyRelative("m_triggerId").stringValue = System.Guid.NewGuid().ToString("N").Substring(0, 8);
            context = GetContext(property);
        }

        Rect row = new(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        string prefix = context.Index >= 0 ? $"{context.Index}." : label.text;
        property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, $"{prefix}  {BuildSummary(property, type, context)}", true);
        if (!property.isExpanded)
        {
            EditorGUI.EndProperty();
            return;
        }

        EditorGUI.indentLevel++;

        row.y += row.height + Spacing;
        DrawTypePopup(row, property, context);

        if (type == TutorialBlockType.Endpoint)
        {
            row = DrawCloseSelection(position, row, property, context);
        }

        foreach (SerializedProperty field in GetFields(property))
        {
            row = new Rect(position.x, row.yMax + Spacing, position.width, GetFieldHeight(field));
            if (field.name == "m_areaCenter")
            {
                TutorialActionDrawer.DrawPositionField(row, field, GetFieldLabel(property, field));
            }
            else if (field.name == "m_cinematicId")
            {
                TutorialCinematicPopup.Draw(row, field, GetFieldLabel(property, field));
            }
            else
            {
                EditorGUI.PropertyField(row, field, GetFieldLabel(property, field), true);
            }
        }

        EditorGUI.indentLevel--;
        EditorGUI.EndProperty();
    }

    /// <summary>
    /// 엔드포인트가 닫을 트리거를 체크 목록으로 고릅니다. 위쪽에서 아직 다른 엔드포인트가 닫지 않은 트리거만 나옵니다.
    /// </summary>
    private static Rect DrawCloseSelection(Rect position, Rect row, SerializedProperty property, Context context)
    {
        SerializedProperty ids = property.FindPropertyRelative("m_closeTriggerIds");
        row = new Rect(position.x, row.yMax + Spacing, position.width, EditorGUIUtility.singleLineHeight);
        EditorGUI.LabelField(row, new GUIContent("닫을 트리거", "고른 트리거가 모두 완료되면 닫힙니다. 트리거 하나는 엔드포인트 하나로만 닫힙니다."));

        EditorGUI.indentLevel++;
        if (context.Open.Count == 0)
        {
            row.y += row.height + Spacing;
            EditorGUI.LabelField(row, "위쪽에 열린 트리거가 없습니다.");
        }

        for (int i = 0; i < context.Open.Count; i++)
        {
            int trigger = context.Open[i];
            SerializedProperty triggerProp = context.List.GetArrayElementAtIndex(trigger);
            string id = triggerProp.FindPropertyRelative("m_triggerId").stringValue;
            row.y += row.height + Spacing;

            bool selected = IndexOf(ids, id) >= 0;
            string text = $"#{context.Links.Number[trigger]}  {DescribeTrigger(triggerProp)}";
            bool toggled = EditorGUI.ToggleLeft(row, text, selected);
            if (toggled && !selected)
            {
                ids.arraySize++;
                ids.GetArrayElementAtIndex(ids.arraySize - 1).stringValue = id;
            }
            else if (!toggled && selected)
            {
                ids.DeleteArrayElementAtIndex(IndexOf(ids, id));
            }
        }

        EditorGUI.indentLevel--;
        return row;
    }

    /// <summary>종류 드롭다운입니다. 위쪽에 열린 트리거가 없으면 엔드포인트를 고를 수 없습니다. 엔드포인트로 바꾸면 열린 트리거를 모두 골라 둡니다.</summary>
    private static void DrawTypePopup(Rect rect, SerializedProperty property, Context context)
    {
        SerializedProperty typeProp = property.FindPropertyRelative("m_type");
        bool canBeEndpoint = context.Open.Count > 0;
        bool showEndpoint = canBeEndpoint || typeProp.intValue == (int)TutorialBlockType.Endpoint;

        // 드롭다운 순서와 enum 값이 다를 수 있어(엔드포인트가 빠질 때) 값 목록을 따로 둡니다.
        List<string> labels = new();
        List<int> values = new();
        for (int i = 0; i < s_typeLabels.Length; i++)
        {
            if (i == (int)TutorialBlockType.Endpoint && !showEndpoint)
            {
                continue;
            }

            labels.Add(i == (int)TutorialBlockType.Endpoint && !canBeEndpoint ? s_typeLabels[i] + " (열린 트리거 없음)" : s_typeLabels[i]);
            values.Add(i);
        }

        int current = Mathf.Max(0, values.IndexOf(typeProp.intValue));
        int picked = values[EditorGUI.Popup(rect, "종류", current, labels.ToArray())];
        if (picked == typeProp.intValue)
        {
            return;
        }

        typeProp.intValue = picked;
        if (picked == (int)TutorialBlockType.Endpoint)
        {
            SerializedProperty ids = property.FindPropertyRelative("m_closeTriggerIds");
            ids.arraySize = 0;
            for (int i = 0; i < context.Open.Count; i++)
            {
                if (context.Links.ClosedBy[context.Open[i]] != TutorialBlockLinks.None)
                {
                    continue;
                }

                ids.arraySize++;
                ids.GetArrayElementAtIndex(ids.arraySize - 1).stringValue =
                    context.List.GetArrayElementAtIndex(context.Open[i]).FindPropertyRelative("m_triggerId").stringValue;
            }
        }
    }

    /// <summary>종류(트리거는 조건까지)에 맞춰 보여 줄 칸입니다. 엔드포인트의 닫을 트리거 목록은 따로 그립니다.</summary>
    private static IEnumerable<SerializedProperty> GetFields(SerializedProperty property)
    {
        switch (GetType(property))
        {
            case TutorialBlockType.Dialogue:
                yield return property.FindPropertyRelative("m_dialogue");
                break;

            case TutorialBlockType.Trigger:
                yield return property.FindPropertyRelative("m_actions");
                SerializedProperty condition = property.FindPropertyRelative("m_condition");
                yield return condition;
                switch ((TutorialConditionType)condition.intValue)
                {
                    case TutorialConditionType.Time:
                        yield return property.FindPropertyRelative("m_seconds");
                        break;

                    case TutorialConditionType.KillEnemy:
                        yield return property.FindPropertyRelative("m_enemyPrefab");
                        yield return property.FindPropertyRelative("m_killCount");
                        break;

                    case TutorialConditionType.ReachArea:
                        yield return property.FindPropertyRelative("m_areaCenter");
                        yield return property.FindPropertyRelative("m_areaRadius");
                        break;

                    case TutorialConditionType.Interact:
                        yield return property.FindPropertyRelative("m_targetId");
                        break;

                    case TutorialConditionType.SwitchCharacter:
                    case TutorialConditionType.UseSkill:
                        yield return property.FindPropertyRelative("m_memberNumber");
                        break;

                    case TutorialConditionType.HoldOrder:
                    case TutorialConditionType.RecallOrder:
                        yield return property.FindPropertyRelative("m_orderKey");
                        break;
                }

                break;

            case TutorialBlockType.Endpoint:
                yield return property.FindPropertyRelative("m_actions");
                break;

            case TutorialBlockType.Cinematic:
                yield return property.FindPropertyRelative("m_cinematicId");
                break;
        }
    }

    /// <summary>위치 칸은 한 줄로 그리므로(<see cref="TutorialActionDrawer.DrawPositionField"/>) 높이도 한 줄입니다.</summary>
    private static float GetFieldHeight(SerializedProperty field)
    {
        return field.name == "m_areaCenter" ? EditorGUIUtility.singleLineHeight : EditorGUI.GetPropertyHeight(field, true);
    }

    private static GUIContent GetFieldLabel(SerializedProperty block, SerializedProperty field)
    {
        string text = field.name switch
        {
            "m_dialogue" => "대사",
            "m_condition" => "완료 조건",
            "m_seconds" => "시간(초)",
            "m_enemyPrefab" => "적 프리팹",
            "m_killCount" => "처치 수",
            "m_areaCenter" => "도착 위치",
            "m_areaRadius" => "도착 반경(m)",
            "m_targetId" => "대상 ID",
            "m_memberNumber" => "캐릭터 번호",
            "m_orderKey" => "명령 키 팀원",
            "m_cinematicId" => "시네마틱",
            "m_actions" => GetType(block) == TutorialBlockType.Trigger ? "실행할 이벤트" : "완료 시 이벤트",
            _ => field.displayName,
        };

        return new GUIContent(text, field.tooltip);
    }

    private static string BuildSummary(SerializedProperty property, TutorialBlockType type, Context context)
    {
        switch (type)
        {
            case TutorialBlockType.Dialogue:
                SerializedProperty dialogue = property.FindPropertyRelative("m_dialogue");
                DialogueSpeaker speaker = dialogue.FindPropertyRelative("m_speaker").objectReferenceValue as DialogueSpeaker;
                string text = (dialogue.FindPropertyRelative("m_text").stringValue ?? string.Empty).Replace("\n", " ");
                if (text.Length > SummaryLength)
                {
                    text = text.Substring(0, SummaryLength) + "…";
                }

                string name = speaker != null && !string.IsNullOrWhiteSpace(speaker.DisplayName) ? speaker.DisplayName : "(화자 없음)";
                return string.IsNullOrEmpty(text) ? "[대사] (빈 대사)" : $"[대사] {name}: {text}";

            case TutorialBlockType.Trigger:
                int number = context.Index >= 0 ? context.Links.Number[context.Index] : 0;
                string trigger = $"[트리거 #{number}] {DescribeTrigger(property)}";
                bool closed = context.Index >= 0 && context.Links.ClosedBy[context.Index] != TutorialBlockLinks.None;
                return closed ? trigger : trigger + "   ⚠ 닫는 엔드포인트 없음";

            case TutorialBlockType.Endpoint:
                if (context.Index < 0)
                {
                    return "[엔드포인트]";
                }

                List<int> closes = context.Links.Closes[context.Index];
                if (closes.Count == 0)
                {
                    return "[엔드포인트] ⚠ 닫을 트리거 없음";
                }

                List<string> numbers = new();
                for (int i = 0; i < closes.Count; i++)
                {
                    numbers.Add($"#{context.Links.Number[closes[i]]}");
                }

                string endpoint = $"[엔드포인트] {string.Join(" + ", numbers)} 완료 시 닫음";
                return context.Links.HasBadReference[context.Index] ? endpoint + "   ⚠ 닫을 수 없는 트리거 포함" : endpoint;

            case TutorialBlockType.Cinematic:
                string cinematicId = property.FindPropertyRelative("m_cinematicId").stringValue;
                if (string.IsNullOrEmpty(cinematicId))
                {
                    return "[시네마틱] ⚠ 시네마틱 선택 안 함";
                }

                string cinematicName = TutorialCinematicPopup.GetName(cinematicId);
                return cinematicName != null ? $"[시네마틱] {cinematicName}" : $"[시네마틱] ({cinematicId})";

            default:
                return type.ToString();
        }
    }

    /// <summary>트리거 요약입니다. "이벤트 이름들 → 완료 조건"입니다.</summary>
    private static string DescribeTrigger(SerializedProperty trigger)
    {
        SerializedProperty actions = trigger.FindPropertyRelative("m_actions");
        List<string> names = new();
        for (int i = 0; i < actions.arraySize; i++)
        {
            string method = actions.GetArrayElementAtIndex(i).FindPropertyRelative("m_method").stringValue;
            System.Reflection.MethodInfo info = string.IsNullOrEmpty(method) ? null : TutorialEvents.FindMethod(method);
            if (info != null)
            {
                names.Add(System.Reflection.CustomAttributeExtensions.GetCustomAttribute<TutorialEventAttribute>(info).DisplayName);
            }
        }

        string events = names.Count > 0 ? string.Join(", ", names) : "이벤트 없음";
        return $"{events} → {DescribeCondition(trigger)}";
    }

    private static string DescribeCondition(SerializedProperty property)
    {
        switch ((TutorialConditionType)property.FindPropertyRelative("m_condition").intValue)
        {
            case TutorialConditionType.Time:
                return $"{property.FindPropertyRelative("m_seconds").floatValue:0.#}초";

            case TutorialConditionType.KillEnemy:
                Object prefab = property.FindPropertyRelative("m_enemyPrefab").objectReferenceValue;
                int kills = property.FindPropertyRelative("m_killCount").intValue;
                return prefab != null ? $"{prefab.name} {kills}마리 처치" : $"적 {kills}마리 처치";

            case TutorialConditionType.ReachArea:
                return $"도착 (반경 {property.FindPropertyRelative("m_areaRadius").floatValue:0.#}m)";

            case TutorialConditionType.Interact:
                string id = property.FindPropertyRelative("m_targetId").stringValue;
                return string.IsNullOrEmpty(id) ? "아무 상호작용" : $"'{id}' 상호작용";

            case TutorialConditionType.SwitchCharacter:
                int switchNumber = property.FindPropertyRelative("m_memberNumber").intValue;
                return switchNumber > 0 ? $"{switchNumber}번 캐릭터로 변경" : "다른 캐릭터로 변경";

            case TutorialConditionType.UseSkill:
                int skillNumber = property.FindPropertyRelative("m_memberNumber").intValue;
                return skillNumber > 0 ? $"{skillNumber}번 캐릭터 스킬 사용" : "스킬 사용";

            case TutorialConditionType.HoldOrder:
                return $"{DescribeOrderKey(property)} 위치 사수";

            case TutorialConditionType.RecallOrder:
                return $"{DescribeOrderKey(property)} 복귀";

            default:
                return "바로 완료";
        }
    }

    private static string DescribeOrderKey(SerializedProperty property)
    {
        return (TutorialOrderKey)property.FindPropertyRelative("m_orderKey").intValue switch
        {
            TutorialOrderKey.Q => "Q 팀원",
            TutorialOrderKey.E => "E 팀원",
            _ => "Q·E 팀원",
        };
    }

    private static TutorialBlockType GetType(SerializedProperty property)
    {
        return (TutorialBlockType)property.FindPropertyRelative("m_type").intValue;
    }

    private static int IndexOf(SerializedProperty stringArray, string value)
    {
        for (int i = 0; i < stringArray.arraySize; i++)
        {
            if (stringArray.GetArrayElementAtIndex(i).stringValue == value)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>같은 챕터 블록 목록에서 이 블록의 자리와 연결 정보입니다.</summary>
    private struct Context
    {
        public SerializedProperty List;
        public int Index;
        public TutorialBlockLinks Links;

        /// <summary>이 자리에서 닫을 수 있는 트리거 번호입니다(위쪽, 아직 닫히지 않았거나 이 블록 이후에서 닫힘).</summary>
        public List<int> Open;
    }

    private static Context GetContext(SerializedProperty property)
    {
        Context context = new() { Index = -1, Open = new List<int>() };
        string path = property.propertyPath;
        int arrayAt = path.LastIndexOf(".Array.data[", System.StringComparison.Ordinal);
        if (arrayAt < 0)
        {
            return context;
        }

        SerializedProperty list = property.serializedObject.FindProperty(path.Substring(0, arrayAt));
        if (list == null || !list.isArray || !int.TryParse(path.Substring(arrayAt + 12).TrimEnd(']'), out int index) || index >= list.arraySize)
        {
            return context;
        }

        int count = list.arraySize;
        TutorialBlockType[] types = new TutorialBlockType[count];
        string[] triggerIds = new string[count];
        IReadOnlyList<string>[] closeIds = new IReadOnlyList<string>[count];
        for (int i = 0; i < count; i++)
        {
            SerializedProperty element = list.GetArrayElementAtIndex(i);
            types[i] = (TutorialBlockType)element.FindPropertyRelative("m_type").intValue;
            triggerIds[i] = element.FindPropertyRelative("m_triggerId").stringValue;
            SerializedProperty ids = element.FindPropertyRelative("m_closeTriggerIds");
            string[] values = new string[ids.arraySize];
            for (int k = 0; k < values.Length; k++)
            {
                values[k] = ids.GetArrayElementAtIndex(k).stringValue;
            }

            closeIds[i] = values;
        }

        context.List = list;
        context.Index = index;
        context.Links = TutorialBlockLinks.Build(types, triggerIds, closeIds);
        context.Open = context.Links.OpenTriggersAt(index);

        // 이 엔드포인트가 이미 닫고 있는 트리거와 아직 아무도 닫지 않은 트리거만 고를 수 있습니다.
        if (types[index] == TutorialBlockType.Endpoint)
        {
            context.Open.RemoveAll(t => context.Links.ClosedBy[t] != TutorialBlockLinks.None && context.Links.ClosedBy[t] != index);
        }

        return context;
    }
}
