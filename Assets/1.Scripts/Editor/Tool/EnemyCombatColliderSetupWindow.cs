using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 모델 교체형 적 프리팹에 피격 Hitbox, 근접 공격 판정, 래그돌을 일괄 구성하는 창입니다.
/// </summary>
public sealed class EnemyCombatColliderSetupWindow : EditorWindow
{
    private const string DefaultTargetPath =
        "Assets/2.Prefabs/Enemy/Defense/Howler/Howler(Defense_Player) 2.prefab";

    [SerializeField] private GameObject m_targetPrefab;
    [SerializeField] private EnemyCombatColliderBuildOptions m_options = new EnemyCombatColliderBuildOptions();
    [SerializeField] private string m_selectedAnimatorPath;
    [SerializeField] private int m_selectedLegacyIndex;

    private readonly List<string> m_log = new List<string>();
    private EnemyCombatColliderInspection m_inspection;
    private EnemyCombatSetupResult m_lastValidation;
    private Vector2 m_scroll;
    private Vector2 m_logScroll;

    [MenuItem("Tools/GrayZone/적 전투 콜라이더 구성")]
    private static void Open()
    {
        EnemyCombatColliderSetupWindow window = GetWindow<EnemyCombatColliderSetupWindow>("적 전투 콜라이더");
        window.minSize = new Vector2(620.0f, 680.0f);
        window.Show();
    }

    private void OnEnable()
    {
        m_options ??= new EnemyCombatColliderBuildOptions();
        if (m_targetPrefab == null)
        {
            m_targetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultTargetPath);
        }

        RefreshInspection();
    }

    private void OnGUI()
    {
        m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
        DrawHeader();
        DrawTarget();
        DrawAnimatorSelection();
        DrawLegacyRigs();
        DrawOptions();
        DrawActions();
        DrawResult();
        DrawLog();
        EditorGUILayout.EndScrollView();
    }

    private void DrawHeader()
    {
        EditorGUILayout.LabelField("적 전투 콜라이더 구성", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "새 모델로 교체한 적 프리팹의 적용 대상 Humanoid Animator를 명시적으로 선택합니다. "
            + "피격 Hitbox, 양손 Melee, 래그돌을 새 리그에 구성하며 잔존 구 리그는 자동 삭제하지 않습니다.",
            MessageType.None);

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorGUILayout.HelpBox("Play Mode를 종료한 뒤 사용하세요.", MessageType.Warning);
        }
    }

    private void DrawTarget()
    {
        EditorGUI.BeginChangeCheck();
        GameObject next = (GameObject)EditorGUILayout.ObjectField(
            "대상 프리팹",
            m_targetPrefab,
            typeof(GameObject),
            false);
        if (EditorGUI.EndChangeCheck())
        {
            m_targetPrefab = next;
            m_selectedAnimatorPath = null;
            m_selectedLegacyIndex = 0;
            m_lastValidation = null;
            m_log.Clear();
            RefreshInspection();
        }

        if (m_targetPrefab == null)
        {
            EditorGUILayout.HelpBox("프로젝트의 적 프리팹 에셋을 지정하세요.", MessageType.Info);
            return;
        }

        if (!PrefabUtility.IsPartOfPrefabAsset(m_targetPrefab))
        {
            EditorGUILayout.HelpBox("씬 인스턴스는 지원하지 않습니다. 원본 프리팹 에셋을 지정하세요.", MessageType.Error);
            return;
        }

        EditorGUILayout.LabelField("경로", AssetDatabase.GetAssetPath(m_targetPrefab));
    }

    private void DrawAnimatorSelection()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("1. 적용 대상 리그", EditorStyles.boldLabel);
        if (m_inspection == null || m_inspection.Animators.Count == 0)
        {
            EditorGUILayout.HelpBox("선택할 수 있는 Humanoid Animator가 없습니다.", MessageType.Error);
            return;
        }

        int current = m_inspection.Animators.FindIndex(candidate => candidate.AnimatorPath == m_selectedAnimatorPath);
        if (current < 0)
        {
            current = 0;
        }

        string[] labels = m_inspection.Animators
            .Select(candidate => $"{candidate.DisplayPath}  →  리그: {candidate.RigRootDisplayPath}")
            .ToArray();

        EditorGUI.BeginChangeCheck();
        int selected = EditorGUILayout.Popup("Humanoid Animator", current, labels);
        if (EditorGUI.EndChangeCheck())
        {
            m_selectedAnimatorPath = m_inspection.Animators[selected].AnimatorPath;
            m_selectedLegacyIndex = 0;
            m_lastValidation = null;
            RefreshInspection();
        }

        EnemyCombatAnimatorCandidate active = m_inspection.Animators
            .First(candidate => candidate.AnimatorPath == m_selectedAnimatorPath);
        EditorGUILayout.HelpBox(
            $"선택 Animator: {active.DisplayPath}\n실제 구성 범위: {active.RigRootDisplayPath}",
            MessageType.Info);
    }

    private void DrawLegacyRigs()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("2. 잔존 구 리그", EditorStyles.boldLabel);
        if (m_inspection == null || m_inspection.LegacyRigs.Count == 0)
        {
            EditorGUILayout.HelpBox("적용 대상 밖에서 전투 컴포넌트를 가진 잔존 리그를 찾지 못했습니다.", MessageType.Info);
            return;
        }

        m_selectedLegacyIndex = Mathf.Clamp(m_selectedLegacyIndex, 0, m_inspection.LegacyRigs.Count - 1);
        string[] labels = m_inspection.LegacyRigs
            .Select(candidate => candidate.DisplayPath)
            .ToArray();
        m_selectedLegacyIndex = EditorGUILayout.Popup("제거 후보", m_selectedLegacyIndex, labels);

        EnemyCombatLegacyRigCandidate selected = m_inspection.LegacyRigs[m_selectedLegacyIndex];
        EditorGUILayout.HelpBox(
            $"{selected.DisplayPath}\n감지 근거: {selected.Reason}\n"
            + "구성 적용 단계에서는 보존됩니다. 새 리그 검증 후 아래의 별도 제거 버튼을 사용하세요.",
            MessageType.Warning);
    }

    private void DrawOptions()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("3. 구성 범위", EditorStyles.boldLabel);
        m_options.BuildHitboxes = EditorGUILayout.ToggleLeft("피격 Hitbox 15개 구성", m_options.BuildHitboxes);

        using (new EditorGUI.DisabledScope(!m_options.BuildHitboxes))
        {
            int sizingMode = m_options.HitboxSizingMode == EnemyHitboxSizingMode.MeshInfluence ? 0 : 1;
            sizingMode = EditorGUILayout.Popup(
                "크기 계산 방식",
                sizingMode,
                new[] { "메시 외곽 기준", "뼈 비율" });
            m_options.HitboxSizingMode = sizingMode == 0
                ? EnemyHitboxSizingMode.MeshInfluence
                : EnemyHitboxSizingMode.BoneRatio;

            int forgiveness = m_options.HitboxForgiveness switch
            {
                EnemyHitboxForgiveness.Accurate => 0,
                EnemyHitboxForgiveness.Wide => 2,
                _ => 1,
            };
            forgiveness = EditorGUILayout.Popup(
                "피격 판정 여유",
                forgiveness,
                new[] { "정확", "보통", "넓게" });
            m_options.HitboxForgiveness = forgiveness switch
            {
                0 => EnemyHitboxForgiveness.Accurate,
                2 => EnemyHitboxForgiveness.Wide,
                _ => EnemyHitboxForgiveness.Standard,
            };
        }

        m_options.BuildMelee = EditorGUILayout.ToggleLeft("좌우 손 Melee 구성", m_options.BuildMelee);
        m_options.BuildRagdoll = EditorGUILayout.ToggleLeft("Ragdoll 구성", m_options.BuildRagdoll);
        m_options.PreserveExistingColliderSizes = EditorGUILayout.ToggleLeft(
            "기존 Collider 크기 유지",
            m_options.PreserveExistingColliderSizes);
        m_options.RefitHitDetectVolume = EditorGUILayout.ToggleLeft(
            "HitDetectVolume을 새 모델 Bounds에 다시 맞춤",
            m_options.RefitHitDetectVolume);

        EditorGUILayout.HelpBox(
            "메시 외곽 기준은 각 뼈에 가장 크게 영향을 받는 Skinned Mesh 정점으로 실제 두께를 계산합니다. "
            + "'보통'은 머리 5%, 몸통 10%, 팔다리 12%, 손발 15%의 판정 여유를 더합니다. "
            + "기존 Collider 크기 유지를 켜면 재실행 시 현재 수동 조정값을 보존하므로, 계산 방식을 다시 적용할 때는 꺼야 합니다.",
            MessageType.None);
    }

    private void DrawActions()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("4. 실행", EditorStyles.boldLabel);
        bool canAct = CanAct();
        using (new EditorGUI.DisabledScope(!canAct))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("구성 검사", GUILayout.Height(32.0f)))
                {
                    ValidateOnly();
                }

                if (GUILayout.Button("구성 적용", GUILayout.Height(32.0f)))
                {
                    ApplyConfiguration();
                }
            }
        }

        bool canCleanup = canAct
            && m_inspection != null
            && m_inspection.LegacyRigs.Count > 0
            && m_lastValidation != null
            && m_lastValidation.Status == EnemyCombatSetupStatus.CleanupRequired;
        using (new EditorGUI.DisabledScope(!canCleanup))
        {
            GUI.backgroundColor = new Color(1.0f, 0.72f, 0.55f);
            if (GUILayout.Button("선택한 잔존 구 리그 제거", GUILayout.Height(30.0f)))
            {
                RemoveSelectedLegacyRig();
            }
            GUI.backgroundColor = Color.white;
        }
    }

    private void DrawResult()
    {
        if (m_lastValidation == null)
        {
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"검사 결과: {m_lastValidation.Status}", EditorStyles.boldLabel);
        for (int i = 0; i < m_lastValidation.Issues.Count; i++)
        {
            EnemyCombatSetupIssue issue = m_lastValidation.Issues[i];
            EditorGUILayout.HelpBox(
                $"[{issue.Code}] {issue.Message}",
                ToMessageType(issue.Level));
        }
    }

    private void DrawLog()
    {
        if (m_log.Count == 0)
        {
            return;
        }

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"작업 로그 ({m_log.Count})", EditorStyles.boldLabel);
            if (GUILayout.Button("복사", GUILayout.Width(64.0f)))
            {
                EditorGUIUtility.systemCopyBuffer = string.Join("\n", m_log);
            }
        }

        m_logScroll = EditorGUILayout.BeginScrollView(m_logScroll, EditorStyles.helpBox, GUILayout.MinHeight(140.0f));
        for (int i = 0; i < m_log.Count; i++)
        {
            EditorGUILayout.LabelField(m_log[i], EditorStyles.wordWrappedLabel);
        }
        EditorGUILayout.EndScrollView();
    }

    private bool CanAct()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode
            && m_targetPrefab != null
            && PrefabUtility.IsPartOfPrefabAsset(m_targetPrefab)
            && m_inspection != null
            && m_inspection.Animators.Any(candidate => candidate.AnimatorPath == m_selectedAnimatorPath);
    }

    private void RefreshInspection()
    {
        if (m_targetPrefab == null || !PrefabUtility.IsPartOfPrefabAsset(m_targetPrefab))
        {
            m_inspection = null;
            return;
        }

        string path = AssetDatabase.GetAssetPath(m_targetPrefab);
        m_inspection = EnemyCombatColliderValidator.InspectPrefab(path, m_selectedAnimatorPath);
        m_selectedAnimatorPath = m_inspection.SelectedAnimatorPath;
        m_selectedLegacyIndex = Mathf.Clamp(m_selectedLegacyIndex, 0, Mathf.Max(0, m_inspection.LegacyRigs.Count - 1));
    }

    private void ValidateOnly()
    {
        m_log.Clear();
        string path = AssetDatabase.GetAssetPath(m_targetPrefab);
        m_lastValidation = EnemyCombatColliderBuilder.ValidatePrefab(path, m_selectedAnimatorPath);
        m_log.Add($"구성 검사 완료: {m_lastValidation.Status}");
    }

    private void ApplyConfiguration()
    {
        m_log.Clear();
        string path = AssetDatabase.GetAssetPath(m_targetPrefab);
        bool success = EnemyCombatColliderBuilder.BuildPrefab(
            path,
            m_selectedAnimatorPath,
            m_options,
            m_log,
            out m_lastValidation);

        if (success)
        {
            m_log.Add($"구성 적용 결과: {m_lastValidation.Status}");
        }
        else
        {
            m_log.Add("구성에 실패했습니다. 프리팹은 저장하지 않았습니다.");
        }

        RefreshInspection();
        Repaint();
    }

    private void RemoveSelectedLegacyRig()
    {
        EnemyCombatLegacyRigCandidate legacy = m_inspection.LegacyRigs[m_selectedLegacyIndex];
        bool confirmed = EditorUtility.DisplayDialog(
            "잔존 구 리그 제거",
            $"다음 오브젝트와 그 하위 계층을 프리팹에서 제거합니다.\n\n{legacy.DisplayPath}\n{legacy.Reason}\n\n"
            + "이 작업은 자동으로 실행 취소되지 않습니다. Git 또는 프리팹 백업으로 복구해야 합니다.",
            "제거",
            "취소");

        if (!confirmed)
        {
            return;
        }

        m_log.Clear();
        string path = AssetDatabase.GetAssetPath(m_targetPrefab);
        bool success = EnemyCombatColliderBuilder.RemoveLegacyRig(
            path,
            m_selectedAnimatorPath,
            legacy.RigRootPath,
            m_log,
            out m_lastValidation);

        if (!success)
        {
            m_log.Add("잔존 구 리그를 제거하지 못했습니다.");
        }

        RefreshInspection();
        Repaint();
    }

    private static MessageType ToMessageType(EnemyCombatIssueLevel level)
    {
        return level switch
        {
            EnemyCombatIssueLevel.Error => MessageType.Error,
            EnemyCombatIssueLevel.Warning => MessageType.Warning,
            EnemyCombatIssueLevel.CleanupRequired => MessageType.Warning,
            _ => MessageType.Info,
        };
    }
}
