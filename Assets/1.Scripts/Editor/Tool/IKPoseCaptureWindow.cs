using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.SceneManagement;

/// <summary>
/// Captures hand IK targets in weapon space while playing, then applies the captured
/// offsets to the restored Edit Mode objects or their outermost Prefab asset.
/// </summary>
public sealed class IKPoseCaptureWindow : EditorWindow
{
    private const string SessionKey = "GrayZone.IKPoseCaptureWindow.CapturedPose";
    private const string LeftHandRole = "Left Hand Target";
    private const string LeftHintRole = "Left Elbow Hint";
    private const string RightHandRole = "Right Hand Target";
    private const string RightHintRole = "Right Elbow Hint";

    [Serializable]
    private sealed class CapturedTransform
    {
        public string role;
        public string globalObjectId;
        public string hierarchyPath;
        public string displayPath;
        public string referenceGlobalObjectId;
        public string referenceHierarchyPath;
        public string referenceDisplayPath;
        public Vector3 referenceLocalPosition;
        public Quaternion referenceLocalRotation;
    }

    [Serializable]
    private sealed class CapturedPose
    {
        public string referenceGlobalObjectId;
        public string referenceHierarchyPath;
        public string referenceDisplayPath;
        public string rootGlobalObjectId;
        public string rootName;
        public string scenePath;
        public string capturedAt;
        public List<CapturedTransform> transforms = new List<CapturedTransform>();
    }

    [SerializeField] private Transform m_weaponReference;
    [SerializeField] private Transform m_leftHandTarget;
    [SerializeField] private Transform m_leftElbowHint;
    [SerializeField] private Transform m_rightHandTarget;
    [SerializeField] private Transform m_rightElbowHint;
    [SerializeField] private bool m_showRightHand;

    private CapturedPose m_capturedPose;
    private Vector2 m_scrollPosition;
    private string m_statusMessage;
    private MessageType m_statusType = MessageType.Info;

    [MenuItem("Tools/GrayZone/Animation/IK Pose Capture")]
    private static void Open()
    {
        ShowWindow();
    }

    /// <summary>선택한 캐릭터의 Hand IK 제약과 활성 무기를 찾아 캡처 창에 자동 연결합니다.</summary>
    public static void OpenFor(GameObject characterRoot)
    {
        IKPoseCaptureWindow window = ShowWindow();
        window.AutoBindCharacter(characterRoot);
        window.Repaint();
    }

    private static IKPoseCaptureWindow ShowWindow()
    {
        IKPoseCaptureWindow window = GetWindow<IKPoseCaptureWindow>("IK Pose Capture");
        window.minSize = new Vector2(430.0f, 470.0f);
        window.Show();
        return window;
    }

    private void AutoBindCharacter(GameObject characterRoot)
    {
        if (characterRoot == null)
        {
            SetStatus("자동 연결할 캐릭터가 없습니다.", MessageType.Warning);
            return;
        }

        m_leftHandTarget = null;
        m_leftElbowHint = null;
        m_rightHandTarget = null;
        m_rightElbowHint = null;
        m_weaponReference = null;

        TwoBoneIKConstraint[] constraints = characterRoot.GetComponentsInChildren<TwoBoneIKConstraint>(true);
        for (int i = 0; i < constraints.Length; i++)
        {
            TwoBoneIKConstraint constraint = constraints[i];
            if (constraint == null)
            {
                continue;
            }

            string constraintName = constraint.name;
            TwoBoneIKConstraintData data = constraint.data;
            if (constraintName.IndexOf("Left Hand", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                m_leftHandTarget = data.target;
                m_leftElbowHint = data.hint;
            }
            else if (constraintName.IndexOf("Right Hand", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                m_rightHandTarget = data.target;
                m_rightElbowHint = data.hint;
            }
        }

        Transform preferredTarget = m_leftHandTarget != null ? m_leftHandTarget : m_rightHandTarget;
        Gun[] guns = characterRoot.GetComponentsInChildren<Gun>(true);
        for (int i = 0; i < guns.Length; i++)
        {
            Gun gun = guns[i];
            if (gun == null)
            {
                continue;
            }

            if (preferredTarget != null && preferredTarget.IsChildOf(gun.transform))
            {
                m_weaponReference = gun.transform;
                break;
            }

            if (m_weaponReference == null && gun.gameObject.activeSelf)
            {
                m_weaponReference = gun.transform;
            }
        }

        bool hasHandTarget = m_leftHandTarget != null || m_rightHandTarget != null;
        if (m_weaponReference == null || !hasHandTarget)
        {
            SetStatus(
                $"{characterRoot.name}에서 활성 무기 또는 Hand IK Target을 완전히 찾지 못했습니다. 필요한 항목을 수동으로 확인해 주세요.",
                MessageType.Warning);
            return;
        }

        SetStatus($"{characterRoot.name}의 무기와 Hand IK를 자동 연결했습니다.", MessageType.Info);
    }

    private void OnEnable()
    {
        LoadCapturedPose();
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;

        if (!EditorApplication.isPlayingOrWillChangePlaymode && m_capturedPose != null)
        {
            EditorApplication.delayCall += ResolveCapturedBindings;
        }
    }

    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
    }

    private void OnGUI()
    {
        m_scrollPosition = EditorGUILayout.BeginScrollView(m_scrollPosition);
        DrawHeader();
        DrawBindings();
        DrawCaptureControls();
        DrawCapturedPose();
        DrawApplyControls();
        DrawStatus();
        EditorGUILayout.EndScrollView();
    }

    private void DrawHeader()
    {
        EditorGUILayout.LabelField("Play Mode IK Pose Capture", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Adjust IK in Play Mode, capture it in weapon space, stop Play Mode, then apply the captured pose. " +
            "The animation clip and skeleton transforms are not modified.",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label(EditorApplication.isPlaying ? "PLAY MODE" : "EDIT MODE", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label(m_capturedPose == null ? "No capture" : "Capture ready");
        }
    }

    private void DrawBindings()
    {
        EditorGUILayout.Space(6.0f);
        EditorGUILayout.LabelField("Bindings", EditorStyles.boldLabel);

        m_weaponReference = DrawTransformField(
            "Weapon Reference",
            m_weaponReference,
            "Stable weapon transform used as the coordinate reference.");

        EditorGUILayout.Space(2.0f);
        m_leftHandTarget = DrawTransformField("Left Hand Target", m_leftHandTarget, null);
        m_leftElbowHint = DrawTransformField("Left Elbow Hint", m_leftElbowHint, null);

        m_showRightHand = EditorGUILayout.Foldout(m_showRightHand, "Optional Right Hand", true);
        if (m_showRightHand)
        {
            EditorGUI.indentLevel++;
            m_rightHandTarget = DrawTransformField("Right Hand Target", m_rightHandTarget, null);
            m_rightElbowHint = DrawTransformField("Right Elbow Hint", m_rightElbowHint, null);
            EditorGUI.indentLevel--;
        }
    }

    private static Transform DrawTransformField(string label, Transform value, string tooltip)
    {
        GUIContent content = string.IsNullOrEmpty(tooltip)
            ? new GUIContent(label)
            : new GUIContent(label, tooltip);

        return (Transform)EditorGUILayout.ObjectField(content, value, typeof(Transform), true);
    }

    private void DrawCaptureControls()
    {
        EditorGUILayout.Space(8.0f);
        using (new EditorGUI.DisabledScope(!CanCapture(out _)))
        {
            if (GUILayout.Button("Capture Current IK Pose", GUILayout.Height(30.0f)))
            {
                CaptureCurrentPose();
            }
        }

        if (!EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox("Capture is available only in Play Mode.", MessageType.None);
        }
        else if (!CanCapture(out string reason))
        {
            EditorGUILayout.HelpBox(reason, MessageType.Warning);
        }
    }

    private void DrawCapturedPose()
    {
        if (m_capturedPose == null)
        {
            return;
        }

        EditorGUILayout.Space(8.0f);
        EditorGUILayout.LabelField("Captured Pose", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Captured", m_capturedPose.capturedAt);
        EditorGUILayout.LabelField("Weapon Reference", m_capturedPose.referenceDisplayPath);

        foreach (CapturedTransform captured in m_capturedPose.transforms)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(captured.role, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Object", captured.displayPath);
                EditorGUILayout.LabelField("Space", captured.referenceDisplayPath);

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Vector3Field("Reference Position", captured.referenceLocalPosition);
                    EditorGUILayout.Vector3Field("Reference Rotation", captured.referenceLocalRotation.eulerAngles);
                }
            }
        }
    }

    private void DrawApplyControls()
    {
        EditorGUILayout.Space(8.0f);
        EditorGUILayout.LabelField("Apply", EditorStyles.boldLabel);

        bool canApply = !EditorApplication.isPlaying && m_capturedPose != null;
        using (new EditorGUI.DisabledScope(!canApply))
        {
            if (GUILayout.Button("Apply To Edit Mode Instance", GUILayout.Height(28.0f)))
            {
                ApplyCapturedPose(false);
            }

            if (GUILayout.Button("Apply To Outermost Prefab...", GUILayout.Height(28.0f)))
            {
                ApplyCapturedPose(true);
            }

            if (GUILayout.Button("Clear Capture"))
            {
                ClearCapturedPose();
            }
        }

        EditorGUILayout.HelpBox(
            "Apply To Edit Mode Instance creates normal Prefab overrides. The Prefab button shows the destination path " +
            "and asks for confirmation before writing the overrides to the outermost Prefab or Prefab Variant.",
            MessageType.None);
    }

    private void DrawStatus()
    {
        if (string.IsNullOrEmpty(m_statusMessage))
        {
            return;
        }

        EditorGUILayout.Space(6.0f);
        EditorGUILayout.HelpBox(m_statusMessage, m_statusType);
    }

    private bool CanCapture(out string reason)
    {
        if (!EditorApplication.isPlaying)
        {
            reason = "Enter Play Mode before capturing.";
            return false;
        }

        if (m_weaponReference == null)
        {
            reason = "Assign a Weapon Reference.";
            return false;
        }

        if (m_leftHandTarget == null && m_rightHandTarget == null)
        {
            reason = "Assign at least one hand target.";
            return false;
        }

        if (m_leftHandTarget != null && !m_leftHandTarget.IsChildOf(m_weaponReference))
        {
            reason = "Left Hand Target must be a child of the Weapon Reference so the captured offset remains valid across animation poses.";
            return false;
        }

        if (m_rightHandTarget != null && !m_rightHandTarget.IsChildOf(m_weaponReference))
        {
            reason = "Right Hand Target must be a child of the Weapon Reference so the captured offset remains valid across animation poses.";
            return false;
        }

        reason = null;
        return true;
    }

    private void CaptureCurrentPose()
    {
        if (!CanCapture(out string reason))
        {
            SetStatus(reason, MessageType.Warning);
            return;
        }

        Transform root = FindCaptureRoot();
        if (root == null)
        {
            SetStatus("Could not find a common scene root for the assigned transforms.", MessageType.Error);
            return;
        }

        CapturedPose pose = new CapturedPose
        {
            referenceGlobalObjectId = GetGlobalObjectId(m_weaponReference),
            referenceHierarchyPath = GetRelativePath(root, m_weaponReference),
            referenceDisplayPath = GetHierarchyPath(m_weaponReference),
            rootGlobalObjectId = GetGlobalObjectId(root),
            rootName = root.name,
            scenePath = root.gameObject.scene.path,
            capturedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };

        AddCapturedTransform(pose, root, LeftHandRole, m_leftHandTarget, m_weaponReference);
        AddCapturedTransform(pose, root, LeftHintRole, m_leftElbowHint, m_leftElbowHint != null ? m_leftElbowHint.parent : null);
        AddCapturedTransform(pose, root, RightHandRole, m_rightHandTarget, m_weaponReference);
        AddCapturedTransform(pose, root, RightHintRole, m_rightElbowHint, m_rightElbowHint != null ? m_rightElbowHint.parent : null);

        m_capturedPose = pose;
        SaveCapturedPose();
        SetStatus($"Captured {pose.transforms.Count} transform(s) in weapon space.", MessageType.Info);
        Repaint();
    }

    private static void AddCapturedTransform(
        CapturedPose pose,
        Transform root,
        string role,
        Transform target,
        Transform reference)
    {
        if (target == null || reference == null)
        {
            return;
        }

        pose.transforms.Add(new CapturedTransform
        {
            role = role,
            globalObjectId = GetGlobalObjectId(target),
            hierarchyPath = GetRelativePath(root, target),
            displayPath = GetHierarchyPath(target),
            referenceGlobalObjectId = GetGlobalObjectId(reference),
            referenceHierarchyPath = GetRelativePath(root, reference),
            referenceDisplayPath = GetHierarchyPath(reference),
            referenceLocalPosition = reference.InverseTransformPoint(target.position),
            referenceLocalRotation = Quaternion.Inverse(reference.rotation) * target.rotation
        });
    }

    private void ApplyCapturedPose(bool applyToPrefab)
    {
        if (EditorApplication.isPlaying || m_capturedPose == null)
        {
            SetStatus("Stop Play Mode before applying the capture.", MessageType.Warning);
            return;
        }

        ResolveCapturedBindings();
        if (!TryGetApplyBindings(
                out List<(CapturedTransform capture, Transform reference, Transform target)> bindings,
                out string error))
        {
            SetStatus(error, MessageType.Error);
            return;
        }

        string prefabPath = null;
        if (applyToPrefab)
        {
            if (!TryGetOutermostPrefabPath(bindings, out prefabPath, out error))
            {
                SetStatus(error, MessageType.Error);
                return;
            }

            string objectList = string.Join("\n", bindings.ConvertAll(item => $"- {item.capture.role}: {GetHierarchyPath(item.target)}"));
            bool confirmed = EditorUtility.DisplayDialog(
                "Apply IK Pose To Prefab",
                $"The captured IK pose will be applied to:\n\n{prefabPath}\n\nProperties:\n{objectList}\n\nContinue?",
                "Apply",
                "Cancel");

            if (!confirmed)
            {
                return;
            }
        }

        Undo.SetCurrentGroupName("Apply Captured IK Pose");
        int undoGroup = Undo.GetCurrentGroup();

        foreach ((CapturedTransform capture, Transform reference, Transform target) in bindings)
        {
            ApplyRelativePose(reference, capture, target);
        }

        Undo.CollapseUndoOperations(undoGroup);

        if (applyToPrefab)
        {
            foreach ((_, _, Transform target) in bindings)
            {
                ApplyTransformPropertiesToPrefab(target, prefabPath);
            }

            AssetDatabase.SaveAssets();
            SetStatus($"Applied captured IK pose to Prefab: {prefabPath}", MessageType.Info);
        }
        else
        {
            SetStatus("Applied captured IK pose to the Edit Mode instance. Review and apply the Prefab overrides when ready.", MessageType.Info);
        }

        SceneView.RepaintAll();
    }

    private static void ApplyRelativePose(Transform reference, CapturedTransform capture, Transform target)
    {
        Vector3 worldPosition = reference.TransformPoint(capture.referenceLocalPosition);
        Quaternion worldRotation = reference.rotation * capture.referenceLocalRotation;

        Undo.RecordObject(target, "Apply Captured IK Pose");
        target.SetPositionAndRotation(worldPosition, worldRotation);
        PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        EditorUtility.SetDirty(target);

        Scene scene = target.gameObject.scene;
        if (scene.IsValid() && scene.isLoaded)
        {
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }

    private static void ApplyTransformPropertiesToPrefab(Transform target, string prefabPath)
    {
        SerializedObject serializedTarget = new SerializedObject(target);
        serializedTarget.Update();

        SerializedProperty localPosition = serializedTarget.FindProperty("m_LocalPosition");
        SerializedProperty localRotation = serializedTarget.FindProperty("m_LocalRotation");

        PrefabUtility.ApplyPropertyOverride(localPosition, prefabPath, InteractionMode.UserAction);
        PrefabUtility.ApplyPropertyOverride(localRotation, prefabPath, InteractionMode.UserAction);
    }

    private bool TryGetApplyBindings(
        out List<(CapturedTransform capture, Transform reference, Transform target)> bindings,
        out string error)
    {
        bindings = new List<(CapturedTransform capture, Transform reference, Transform target)>();

        if (m_weaponReference == null)
        {
            error = "The Edit Mode Weapon Reference could not be resolved. Assign it again; the captured pose is still available.";
            return false;
        }

        foreach (CapturedTransform capture in m_capturedPose.transforms)
        {
            Transform target = GetCurrentBinding(capture.role);
            if (target == null)
            {
                error = $"The Edit Mode object for '{capture.role}' could not be resolved. Assign it again; the captured pose is still available.";
                return false;
            }

            Transform reference = IsHandTargetRole(capture.role)
                ? m_weaponReference
                : target.parent;

            if (reference == null)
            {
                error = $"The reference transform for '{capture.role}' could not be resolved.";
                return false;
            }

            bindings.Add((capture, reference, target));
        }

        error = null;
        return true;
    }

    private static bool TryGetOutermostPrefabPath(
        List<(CapturedTransform capture, Transform reference, Transform target)> bindings,
        out string prefabPath,
        out string error)
    {
        prefabPath = null;

        foreach ((_, _, Transform target) in bindings)
        {
            GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(target.gameObject);
            if (root == null)
            {
                error = $"'{GetHierarchyPath(target)}' is not part of a Prefab instance. Apply to the scene instance instead.";
                return false;
            }

            string currentPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            if (string.IsNullOrEmpty(currentPath) || !currentPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                error = $"The outermost asset for '{GetHierarchyPath(target)}' is not an editable Prefab: {currentPath}";
                return false;
            }

            if (prefabPath == null)
            {
                prefabPath = currentPath;
            }
            else if (!string.Equals(prefabPath, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                error = "The assigned IK objects belong to different outermost Prefabs. Apply to the instance and review the overrides manually.";
                return false;
            }
        }

        error = null;
        return true;
    }

    private Transform FindCaptureRoot()
    {
        GameObject prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(m_weaponReference.gameObject);
        Transform root = prefabRoot != null ? prefabRoot.transform : m_weaponReference.root;

        foreach (Transform transform in GetAssignedTransforms())
        {
            if (transform != null && !transform.IsChildOf(root) && transform != root)
            {
                return null;
            }
        }

        return root;
    }

    private IEnumerable<Transform> GetAssignedTransforms()
    {
        yield return m_weaponReference;
        yield return m_leftHandTarget;
        yield return m_leftElbowHint;
        yield return m_rightHandTarget;
        yield return m_rightElbowHint;
    }

    private void ResolveCapturedBindings()
    {
        if (m_capturedPose == null || EditorApplication.isPlaying)
        {
            return;
        }

        Transform root = ResolveTransform(m_capturedPose.rootGlobalObjectId);
        if (root == null)
        {
            root = FindRootBySceneAndName(m_capturedPose.scenePath, m_capturedPose.rootName);
        }

        m_weaponReference = ResolveCapturedTransform(
            m_capturedPose.referenceGlobalObjectId,
            root,
            m_capturedPose.referenceHierarchyPath,
            m_weaponReference);

        foreach (CapturedTransform capture in m_capturedPose.transforms)
        {
            Transform current = GetCurrentBinding(capture.role);
            Transform resolved = ResolveCapturedTransform(capture.globalObjectId, root, capture.hierarchyPath, current);
            SetCurrentBinding(capture.role, resolved);
        }

        Repaint();
    }

    private static Transform ResolveCapturedTransform(string globalId, Transform root, string path, Transform fallback)
    {
        Transform resolved = ResolveTransform(globalId);
        if (resolved != null)
        {
            return resolved;
        }

        if (root != null)
        {
            resolved = string.IsNullOrEmpty(path) ? root : root.Find(path);
            if (resolved != null)
            {
                return resolved;
            }
        }

        return fallback;
    }

    private static Transform FindRootBySceneAndName(string scenePath, string rootName)
    {
        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            Scene scene = SceneManager.GetSceneAt(sceneIndex);
            if (!scene.isLoaded)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(scenePath) && !string.Equals(scene.path, scenePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (string.Equals(root.name, rootName, StringComparison.Ordinal))
                {
                    return root.transform;
                }
            }
        }

        return null;
    }

    private Transform GetCurrentBinding(string role)
    {
        switch (role)
        {
            case LeftHandRole:
                return m_leftHandTarget;
            case LeftHintRole:
                return m_leftElbowHint;
            case RightHandRole:
                return m_rightHandTarget;
            case RightHintRole:
                return m_rightElbowHint;
            default:
                return null;
        }
    }

    private static bool IsHandTargetRole(string role)
    {
        return role == LeftHandRole || role == RightHandRole;
    }

    private void SetCurrentBinding(string role, Transform value)
    {
        switch (role)
        {
            case LeftHandRole:
                m_leftHandTarget = value;
                break;
            case LeftHintRole:
                m_leftElbowHint = value;
                break;
            case RightHandRole:
                m_rightHandTarget = value;
                break;
            case RightHintRole:
                m_rightElbowHint = value;
                break;
        }
    }

    private void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.delayCall += ResolveCapturedBindings;
        }

        Repaint();
    }

    private void SaveCapturedPose()
    {
        string json = m_capturedPose == null ? string.Empty : JsonUtility.ToJson(m_capturedPose);
        SessionState.SetString(SessionKey, json);
    }

    private void LoadCapturedPose()
    {
        string json = SessionState.GetString(SessionKey, string.Empty);
        m_capturedPose = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<CapturedPose>(json);
    }

    private void ClearCapturedPose()
    {
        m_capturedPose = null;
        SessionState.EraseString(SessionKey);
        SetStatus("Cleared the captured IK pose.", MessageType.Info);
    }

    private void SetStatus(string message, MessageType type)
    {
        m_statusMessage = message;
        m_statusType = type;
        Repaint();
    }

    private static string GetGlobalObjectId(UnityEngine.Object target)
    {
        if (target == null)
        {
            return string.Empty;
        }

        return GlobalObjectId.GetGlobalObjectIdSlow(target).ToString();
    }

    private static Transform ResolveTransform(string globalId)
    {
        if (string.IsNullOrEmpty(globalId) || !GlobalObjectId.TryParse(globalId, out GlobalObjectId parsed))
        {
            return null;
        }

        return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed) as Transform;
    }

    private static string GetRelativePath(Transform root, Transform target)
    {
        if (root == null || target == null)
        {
            return string.Empty;
        }

        if (root == target)
        {
            return string.Empty;
        }

        Stack<string> parts = new Stack<string>();
        Transform current = target;
        while (current != null && current != root)
        {
            parts.Push(current.name);
            current = current.parent;
        }

        return current == root ? string.Join("/", parts) : string.Empty;
    }

    private static string GetHierarchyPath(Transform target)
    {
        if (target == null)
        {
            return "<missing>";
        }

        Stack<string> parts = new Stack<string>();
        Transform current = target;
        while (current != null)
        {
            parts.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", parts);
    }
}
