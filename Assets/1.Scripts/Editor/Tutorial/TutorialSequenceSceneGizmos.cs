using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 튜토리얼 시퀀스(또는 그것을 쓰는 진행기)를 선택하면, 시퀀스에 저장된 위치와 반경을 Scene 뷰에 그립니다.
/// </summary>
/// <remarks>
/// 이벤트는 위치(Vector3) 파라미터가 있으면 그 위치에, 이름에 "radius"가 들어간 float 파라미터가 있으면 그 반경으로 원을 그립니다
/// (경로선 안내의 도착 반경, 적 생성의 흩뿌림 반경 등). 완료 조건이 위치 도착인 트리거는 도착 위치와 반경을 그립니다.
/// 라벨에는 챕터 이름, 트리거 번호(#n), 이벤트 이름이 붙습니다.
/// </remarks>
[InitializeOnLoad]
public static class TutorialSequenceSceneGizmos
{
    private static readonly Color s_arriveColor = new(0.2f, 1.0f, 0.4f, 1.0f);
    private static readonly Color s_actionColor = new(0.3f, 0.8f, 1.0f, 1.0f);
    private static readonly Color s_spawnColor = new(1.0f, 0.35f, 0.3f, 1.0f);

    private const float PointRadius = 0.4f;

    static TutorialSequenceSceneGizmos()
    {
        SceneView.duringSceneGui += OnSceneGui;
    }

    private static void OnSceneGui(SceneView view)
    {
        TutorialSequence sequence = ResolveSelectedSequence();
        if (sequence == null || Event.current.type != EventType.Repaint)
        {
            return;
        }

        IReadOnlyList<TutorialStep> steps = sequence.Steps;
        for (int s = 0; s < steps.Count; s++)
        {
            TutorialStep step = steps[s];
            IReadOnlyList<TutorialBlock> blocks = step.Blocks;
            TutorialBlockLinks links = TutorialBlockLinks.Build(blocks);
            string chapter = string.IsNullOrEmpty(step.Label) ? $"챕터 {s}" : step.Label;

            for (int b = 0; b < blocks.Count; b++)
            {
                TutorialBlock block = blocks[b];
                if (block == null || block.Type == TutorialBlockType.Dialogue)
                {
                    continue;
                }

                string group = links.Number[b] > 0 ? $" #{links.Number[b]}" : " 엔드포인트";
                if (block.Type == TutorialBlockType.Trigger && block.Condition == TutorialConditionType.ReachArea)
                {
                    DrawArea(block.AreaCenter, block.AreaRadius, s_arriveColor, $"{chapter}{group} 도착 조건 ({block.AreaRadius:0.#}m)");
                }

                IReadOnlyList<TutorialAction> actions = block.Actions;
                for (int a = 0; a < actions.Count; a++)
                {
                    DrawAction(actions[a], $"{chapter}{group}");
                }
            }
        }
    }

    private static void DrawAction(TutorialAction action, string prefix)
    {
        MethodInfo method = action != null && !string.IsNullOrEmpty(action.Method) ? TutorialEvents.FindMethod(action.Method) : null;
        if (method == null)
        {
            return;
        }

        ParameterInfo[] parameters = method.GetParameters();
        Vector3? position = null;
        float radius = 0.0f;
        string radiusName = null;
        for (int i = 0; i < parameters.Length; i++)
        {
            TutorialEventArg arg = i < action.Args.Count ? action.Args[i] : default;
            System.Type type = parameters[i].ParameterType;
            if (type == typeof(Vector3) && position == null)
            {
                position = (Vector3)arg.GetValue(type);
            }
            else if (type == typeof(float) && radiusName == null && parameters[i].Name.ToLowerInvariant().Contains("radius"))
            {
                radius = (float)arg.GetValue(type);
                radiusName = parameters[i].Name;
            }
        }

        if (position == null)
        {
            return;
        }

        string name = method.GetCustomAttribute<TutorialEventAttribute>().DisplayName;
        Color color = method.Name == nameof(TutorialEvents.SpawnEnemies) ? s_spawnColor
            : method.Name == nameof(TutorialEvents.ShowPathLine) ? s_arriveColor
            : s_actionColor;
        string radiusText = radiusName != null ? $" ({radius:0.#}m)" : string.Empty;
        DrawArea(position.Value, radius, color, $"{prefix} {name}{radiusText}");
    }

    private static void DrawArea(Vector3 center, float radius, Color color, string label)
    {
        Handles.color = color;
        Handles.DrawWireDisc(center, Vector3.up, PointRadius * 0.5f);
        Handles.DrawLine(center, center + Vector3.up * 2.0f);
        if (radius > 0.0f)
        {
            Handles.DrawWireDisc(center, Vector3.up, radius, 2.0f);
            Color fill = color;
            fill.a = 0.08f;
            Handles.color = fill;
            Handles.DrawSolidDisc(center, Vector3.up, radius);
        }

        GUIStyle style = new(EditorStyles.boldLabel) { normal = { textColor = color } };
        Handles.Label(center + Vector3.up * 2.2f, label, style);
    }

    /// <summary>선택한 시퀀스 에셋, 또는 선택한 진행기가 쓰는 시퀀스입니다.</summary>
    private static TutorialSequence ResolveSelectedSequence()
    {
        if (Selection.activeObject is TutorialSequence sequence)
        {
            return sequence;
        }

        GameObject selected = Selection.activeGameObject;
        TutorialFlowDirector director = selected != null ? selected.GetComponent<TutorialFlowDirector>() : null;
        if (director == null)
        {
            return null;
        }

        return new SerializedObject(director).FindProperty("m_sequence").objectReferenceValue as TutorialSequence;
    }
}
