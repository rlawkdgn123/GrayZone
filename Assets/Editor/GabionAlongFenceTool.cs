// Places SM_Gabion_A prefabs along the OUTER face of the BarbedWire_Fence_Redline panels.
// "Outer" is resolved per fence run: the side of the panel that faces away from the
// centre of the marked perimeter (that is the direction the yellow arrows point).
//
// The fences themselves are only ever READ - no transform of a BarbedWire_Fence* object
// is touched by anything in this file.
//
//   Tools > GrayZone > Gabion Wall > Place Along Barbed Wire Fence
//
// GabionAutoPlaceOnce at the bottom runs the same placement exactly once, automatically,
// the first time this script compiles with the target scene open. It never saves the
// scene - the result is left dirty so it can be reviewed, undone (one Ctrl+Z) or kept.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GrayZone.LevelTools
{
    public enum OutwardMode
    {
        PerRunVote,
        PerFenceVote,
        ForceLocalForward,
        ForceLocalBack,
    }

    [System.Serializable]
    public class GabionWallSettings
    {
        public const string DefaultGabionPrefabPath =
            "Assets/3.Resources/ThirdParty/SurvivorBase/Prefabs/Base/SM_Gabion_A.prefab";
        public const string DefaultRootName = "Gabion_OuterWall_Generated";
        public const string RedlineFilter = "BarbedWire_Fence_Redline";

        public string nameFilter = RedlineFilter;
        public bool includePlainBarbedWireFences;
        public GameObject gabionPrefab;
        public OutwardMode outwardMode = OutwardMode.PerRunVote;
        public bool flipAll;

        public int gabionsPerPanel = 2;
        public float spanFactor = 1f;
        public float extraGap = 0.05f;
        public float lateralOffsetOverride;
        public int rows = 1;
        public float rowInset = 0.05f;
        public float mergeDistance = 0.7f;

        public bool useCollidersForSize = true;
        public bool snapToGround = true;
        public float groundProbeHeight = 5f;
        public LayerMask groundMask = ~0;

        public float yawJitter;
        public float positionJitter;
        public float scaleJitter;
        public int randomSeed = 20260923;

        public string generatedRootName = DefaultRootName;

        public void EnsurePrefab()
        {
            if (gabionPrefab == null)
                gabionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultGabionPrefabPath);
        }
    }

    public static class GabionWallBuilder
    {
        static readonly Regex PlainFencePattern = new Regex(@"^BarbedWire_Fence( \(\d+\))?$");

        static readonly Vector3[] Signs =
        {
            new Vector3(-1, -1, -1), new Vector3(-1, -1, 1), new Vector3(-1, 1, -1), new Vector3(-1, 1, 1),
            new Vector3( 1, -1, -1), new Vector3( 1, -1, 1), new Vector3( 1, 1, -1), new Vector3( 1, 1, 1),
        };

        public class Fence
        {
            public Transform T;
            public Vector3 CentreWorld;     // footprint centre at the panel base
            public Vector3 Along;           // normalised world direction along the panel
            public Vector3 Outward;         // normalised world direction, resolved
            public float Width;             // world size along the panel
            public float Thickness;         // world size across the panel
            public float Vote;              // >0 when local +Z points away from the centre
        }

        public class Run
        {
            public string Key;
            public Transform Parent;
            public readonly List<Fence> Fences = new List<Fence>();
            public float Vote;
            public bool Flip;
        }

        public struct Slot
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public float Scale;
            public Run Owner;
        }

        // ----------------------------------------------------------- analysis
        public static List<Run> Analyze(GabionWallSettings s, out Vector3 centre, out string report)
        {
            var runs = new List<Run>();
            centre = Vector3.zero;

            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                report = "Close the prefab stage first - this tool works on the open scene.";
                return runs;
            }

            var fences = new List<Fence>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    Collect(s, root.transform, fences);
            }

            if (fences.Count == 0)
            {
                report = "No fence matched \"" + s.nameFilter + "\".";
                return runs;
            }

            foreach (var f in fences) centre += f.CentreWorld;
            centre /= fences.Count;

            var byKey = new Dictionary<Transform, Run>();
            foreach (var f in fences)
            {
                var toFence = f.CentreWorld - centre;
                toFence.y = 0f;
                f.Vote = Vector3.Dot(Flatten(f.T.forward), toFence);

                // Transform cannot be a dictionary key when it is null, so a scene-root
                // fence keys off its own root instead of a (missing) parent.
                var parent = f.T.parent;
                var key = parent != null ? parent : f.T.root;
                if (!byKey.TryGetValue(key, out var run))
                {
                    run = new Run { Key = parent != null ? parent.name : "(scene root)", Parent = parent };
                    byKey.Add(key, run);
                    runs.Add(run);
                }
                run.Fences.Add(f);
            }

            var sb = new StringBuilder();
            sb.AppendLine(fences.Count + " fence panels in " + runs.Count + " run(s).");
            sb.AppendLine(string.Format("Perimeter centre: ({0:F1}, {1:F1})", centre.x, centre.z));

            int total = 0;
            foreach (var run in runs)
            {
                run.Vote = run.Fences.Sum(f => f.Vote);
                run.Flip = s.flipAll ^ (run.Vote < 0f);
                ApplyOutward(s, run);

                int agree = run.Fences.Count(f => (f.Vote >= 0f) == (run.Vote >= 0f));
                total += run.Fences.Count * s.gabionsPerPanel * s.rows;
                sb.AppendLine("  " + run.Key + ": " + run.Fences.Count + " panels, outward = "
                              + (run.Flip ? "-" : "+") + "localZ, "
                              + agree + "/" + run.Fences.Count + " panels agree");
            }

            if (s.gabionPrefab != null && Measure(s, s.gabionPrefab.transform, out _, out var g))
                sb.AppendLine(string.Format("Gabion footprint: {0:F2} x {1:F2} m, height {2:F2} m",
                    g.x, g.z, g.y));

            sb.AppendLine("Planned gabions (before merge): " + total);
            report = sb.ToString();
            return runs;
        }

        static void Collect(GabionWallSettings s, Transform t, List<Fence> into)
        {
            if (Matches(s, t.name))
            {
                var fence = Build(s, t);
                if (fence != null)
                {
                    into.Add(fence);
                    return; // do not descend into a fence
                }
            }

            for (int i = 0; i < t.childCount; i++)
                Collect(s, t.GetChild(i), into);
        }

        static bool Matches(GabionWallSettings s, string goName)
        {
            if (!string.IsNullOrEmpty(s.nameFilter) && goName.Contains(s.nameFilter)) return true;
            if (s.includePlainBarbedWireFences && PlainFencePattern.IsMatch(goName.Trim())) return true;
            return false;
        }

        static Fence Build(GabionWallSettings s, Transform t)
        {
            if (!Measure(s, t, out var localCentre, out var worldSize)) return null;

            return new Fence
            {
                T = t,
                CentreWorld = t.TransformPoint(new Vector3(localCentre.x, 0f, localCentre.z)),
                Along = Flatten(t.right),
                Outward = Flatten(t.forward),
                Width = worldSize.x,
                Thickness = worldSize.z,
            };
        }

        public static void ApplyOutward(GabionWallSettings s, Run run)
        {
            foreach (var f in run.Fences)
            {
                bool flip;
                switch (s.outwardMode)
                {
                    case OutwardMode.PerFenceVote: flip = s.flipAll ^ (f.Vote < 0f); break;
                    case OutwardMode.ForceLocalForward: flip = s.flipAll; break;
                    case OutwardMode.ForceLocalBack: flip = !s.flipAll; break;
                    default: flip = run.Flip; break;
                }
                f.Outward = Flatten(flip ? -f.T.forward : f.T.forward);
            }
        }

        /// <summary>Footprint of the transform in its own local axes, scaled to world size.</summary>
        static bool Measure(GabionWallSettings s, Transform t, out Vector3 localCentre, out Vector3 worldSize)
        {
            localCentre = Vector3.zero;
            worldSize = Vector3.zero;

            var bounds = new Bounds();
            bool any = false;

            if (s.useCollidersForSize)
            {
                foreach (var box in t.GetComponentsInChildren<BoxCollider>(true))
                    Encapsulate(t, box.transform, box.center, box.size * 0.5f, ref bounds, ref any);
            }

            if (!any)
            {
                foreach (var mf in t.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var b = mf.sharedMesh.bounds;
                    Encapsulate(t, mf.transform, b.center, b.extents, ref bounds, ref any);
                }
            }

            if (!any) return false;

            localCentre = bounds.center;
            var scale = t.lossyScale;
            worldSize = new Vector3(
                bounds.size.x * Mathf.Abs(scale.x),
                bounds.size.y * Mathf.Abs(scale.y),
                bounds.size.z * Mathf.Abs(scale.z));
            return worldSize.x > 0.001f && worldSize.z > 0.001f;
        }

        static void Encapsulate(Transform root, Transform owner, Vector3 centre, Vector3 extents,
                                ref Bounds bounds, ref bool any)
        {
            foreach (var sign in Signs)
            {
                var world = owner.TransformPoint(centre + Vector3.Scale(extents, sign));
                var local = root.InverseTransformPoint(world);
                if (!any) { bounds = new Bounds(local, Vector3.zero); any = true; }
                else bounds.Encapsulate(local);
            }
        }

        static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized;
        }

        // ---------------------------------------------------------- placement
        public static List<Slot> BuildSlots(GabionWallSettings s, List<Run> runs, out float usedOffset)
        {
            usedOffset = 0f;
            var slots = new List<Slot>();
            if (s.gabionPrefab == null) return slots;
            if (!Measure(s, s.gabionPrefab.transform, out _, out var gabion)) return slots;

            var rng = new System.Random(s.randomSeed);
            float gabionDepth = gabion.z;
            float gabionHeight = gabion.y;

            foreach (var run in runs)
            {
                foreach (var fence in run.Fences)
                {
                    float offset = s.lateralOffsetOverride > 0.0001f
                        ? s.lateralOffsetOverride
                        : fence.Thickness * 0.5f + gabionDepth * 0.5f + s.extraGap;
                    usedOffset = offset;

                    float span = fence.Width * s.spanFactor;
                    for (int i = 0; i < s.gabionsPerPanel; i++)
                    {
                        float t = s.gabionsPerPanel == 1 ? 0f : (i + 0.5f) / s.gabionsPerPanel - 0.5f;
                        var basePos = fence.CentreWorld
                                    + fence.Along * (t * span)
                                    + fence.Outward * offset;

                        basePos += fence.Along * Jitter(rng, s.positionJitter)
                                 + fence.Outward * Jitter(rng, s.positionJitter * 0.5f);

                        if (s.snapToGround) basePos.y = GroundY(s, basePos, fence.CentreWorld.y);

                        float scale = 1f + Jitter(rng, s.scaleJitter);
                        var facing = Quaternion.LookRotation(fence.Outward, Vector3.up);

                        for (int r = 0; r < s.rows; r++)
                        {
                            slots.Add(new Slot
                            {
                                Position = basePos
                                         + Vector3.up * (r * gabionHeight * scale)
                                         - fence.Outward * (r * s.rowInset),
                                Rotation = facing * Quaternion.Euler(0f, Jitter(rng, s.yawJitter), 0f),
                                Scale = scale,
                                Owner = run,
                            });
                        }
                    }
                }
            }

            if (s.mergeDistance > 0.001f) Merge(s.mergeDistance, slots);
            RemoveFootprintOverlaps(slots, gabion.x, gabion.z, 0.02f);
            return slots;
        }

        // Rejects candidates whose rotated X/Z footprints intersect.  The earlier
        // centre-distance merge is useful for near-identical corner samples, but it
        // cannot detect the long-axis overlap of two differently rotated gabions.
        // A small clearance keeps meshes from z-fighting even when their colliders
        // would only touch.
        static void RemoveFootprintOverlaps(List<Slot> slots, float width, float depth,
                                            float clearance)
        {
            var kept = new List<Slot>(slots.Count);
            foreach (var candidate in slots)
            {
                bool clash = false;
                foreach (var accepted in kept)
                {
                    if (Mathf.Abs(candidate.Position.y - accepted.Position.y) > 0.4f)
                        continue;
                    if (FootprintsOverlap(candidate, accepted, width, depth, clearance))
                    {
                        clash = true;
                        break;
                    }
                }
                if (!clash) kept.Add(candidate);
            }
            slots.Clear();
            slots.AddRange(kept);
        }

        static bool FootprintsOverlap(Slot a, Slot b, float width, float depth,
                                      float clearance)
        {
            var delta = b.Position - a.Position;
            delta.y = 0f;

            Vector3 ax = Flatten(a.Rotation * Vector3.right);
            Vector3 az = Flatten(a.Rotation * Vector3.forward);
            Vector3 bx = Flatten(b.Rotation * Vector3.right);
            Vector3 bz = Flatten(b.Rotation * Vector3.forward);

            float ahx = width * a.Scale * 0.5f;
            float ahz = depth * a.Scale * 0.5f;
            float bhx = width * b.Scale * 0.5f;
            float bhz = depth * b.Scale * 0.5f;

            return OverlapsOnAxis(delta, ax, ax, az, ahx, ahz, bx, bz, bhx, bhz, clearance)
                && OverlapsOnAxis(delta, az, ax, az, ahx, ahz, bx, bz, bhx, bhz, clearance)
                && OverlapsOnAxis(delta, bx, ax, az, ahx, ahz, bx, bz, bhx, bhz, clearance)
                && OverlapsOnAxis(delta, bz, ax, az, ahx, ahz, bx, bz, bhx, bhz, clearance);
        }

        static bool OverlapsOnAxis(Vector3 delta, Vector3 axis,
                                   Vector3 ax, Vector3 az, float ahx, float ahz,
                                   Vector3 bx, Vector3 bz, float bhx, float bhz,
                                   float clearance)
        {
            float centreDistance = Mathf.Abs(Vector3.Dot(delta, axis));
            float radiusA = ahx * Mathf.Abs(Vector3.Dot(ax, axis))
                          + ahz * Mathf.Abs(Vector3.Dot(az, axis));
            float radiusB = bhx * Mathf.Abs(Vector3.Dot(bx, axis))
                          + bhz * Mathf.Abs(Vector3.Dot(bz, axis));
            return centreDistance < radiusA + radiusB + clearance;
        }

        static void Merge(float distance, List<Slot> slots)
        {
            float sqr = distance * distance;
            var kept = new List<Slot>(slots.Count);
            foreach (var slot in slots)
            {
                bool clash = false;
                for (int i = kept.Count - 1; i >= 0 && !clash; i--)
                {
                    var d = kept[i].Position - slot.Position;
                    if (Mathf.Abs(d.y) > 0.4f) continue;   // different stack row
                    d.y = 0f;
                    if (d.sqrMagnitude < sqr) clash = true;
                }
                if (!clash) kept.Add(slot);
            }
            slots.Clear();
            slots.AddRange(kept);
        }

        static float Jitter(System.Random rng, float amount)
        {
            return amount <= 0f ? 0f : (float)(rng.NextDouble() * 2.0 - 1.0) * amount;
        }

        static float GroundY(GabionWallSettings s, Vector3 at, float fallback)
        {
            var terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                var local = at - terrain.transform.position;
                var size = terrain.terrainData.size;
                if (local.x >= 0f && local.z >= 0f && local.x <= size.x && local.z <= size.z)
                    return terrain.SampleHeight(at) + terrain.transform.position.y;
            }

            var origin = at + Vector3.up * s.groundProbeHeight;
            if (Physics.Raycast(origin, Vector3.down, out var hit,
                    s.groundProbeHeight * 4f, s.groundMask, QueryTriggerInteraction.Ignore))
                return hit.point.y;

            return fallback;
        }

        public static int Place(GabionWallSettings s, List<Run> runs, out string report)
        {
            report = "";
            if (runs == null || runs.Count == 0) { report = "Nothing analysed."; return 0; }
            if (s.gabionPrefab == null) { report = "Assign the SM_Gabion_A prefab first."; return 0; }

            var slots = BuildSlots(s, runs, out float offset);
            if (slots.Count == 0) { report = "Nothing to place."; return 0; }

            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Place gabions along barbed wire");

            var scene = runs[0].Fences[0].T.gameObject.scene;
            var existing = FindGeneratedRoot(s.generatedRootName);
            GameObject root;
            var reusable = new List<GameObject>();
            if (existing != null)
            {
                root = existing.gameObject;
                reusable = existing.GetComponentsInChildren<Transform>(true)
                    .Where(t => t != existing && t.name.StartsWith("SM_Gabion_A_Wall_"))
                    .Select(t => t.gameObject)
                    .ToList();
            }
            else
            {
                root = new GameObject(s.generatedRootName);
                Undo.RegisterCreatedObjectUndo(root, "Create gabion wall root");
                var anchor = runs[0].Parent != null ? runs[0].Parent.parent : null;
                if (anchor != null) Undo.SetTransformParent(root.transform, anchor, "Parent gabion wall");
                else if (root.scene != scene) EditorSceneManager.MoveGameObjectToScene(root, scene);
            }

            var groups = new Dictionary<Run, Transform>();
            var flags = GameObjectUtility.GetStaticEditorFlags(s.gabionPrefab);
            int index = 0;

            foreach (var slot in slots)
            {
                if (!groups.TryGetValue(slot.Owner, out var parent))
                {
                    string holderName = "Gabion_" + slot.Owner.Key;
                    var holderTransform = root.transform.Find(holderName);
                    GameObject holder;
                    if (holderTransform != null)
                    {
                        holder = holderTransform.gameObject;
                        if (!holder.activeSelf)
                        {
                            Undo.RecordObject(holder, "Enable gabion group");
                            holder.SetActive(true);
                        }
                    }
                    else
                    {
                        holder = new GameObject(holderName);
                        Undo.RegisterCreatedObjectUndo(holder, "Create gabion group");
                        holder.transform.SetParent(root.transform, false);
                    }
                    parent = holder.transform;
                    groups.Add(slot.Owner, parent);
                }

                GameObject instance;
                if (index < reusable.Count)
                {
                    instance = reusable[index];
                    Undo.RecordObject(instance, "Reuse gabion");
                    Undo.RecordObject(instance.transform, "Move gabion");
                    if (!instance.activeSelf) instance.SetActive(true);
                }
                else
                {
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(s.gabionPrefab, scene);
                    Undo.RegisterCreatedObjectUndo(instance, "Create gabion");
                }
                Undo.SetTransformParent(instance.transform, parent, "Parent gabion");
                instance.transform.SetPositionAndRotation(slot.Position, slot.Rotation);
                instance.transform.localScale = Vector3.one * slot.Scale;
                instance.name = "SM_Gabion_A_Wall_" + index.ToString("000");
                GameObjectUtility.SetStaticEditorFlags(instance, flags);
                index++;
            }

            int disabled = 0;
            for (int i = index; i < reusable.Count; i++)
            {
                if (!reusable[i].activeSelf) continue;
                Undo.RecordObject(reusable[i], "Disable unused gabion");
                reusable[i].SetActive(false);
                disabled++;
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);

            int reusedCount = Mathf.Min(index, reusable.Count);
            int createdCount = Mathf.Max(0, index - reusable.Count);
            report = string.Format("Placed {0} gabions in {1} group(s) under \"{2}\" " +
                                   "({3} reused, {4} created, {5} unused disabled). " +
                                   "Lateral offset used: {6:F2} m.",
                                   index, groups.Count, root.name, reusedCount, createdCount,
                                   disabled, offset);
            return index;
        }

        public static Transform FindGeneratedRoot(string wanted)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    var found = FindByName(root.transform, wanted);
                    if (found != null) return found;
                }
            }
            return null;
        }

        static Transform FindByName(Transform t, string wanted)
        {
            if (t.name == wanted) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var found = FindByName(t.GetChild(i), wanted);
                if (found != null) return found;
            }
            return null;
        }
    }

    public class GabionAlongFenceWindow : EditorWindow
    {
        [SerializeField] GabionWallSettings settings = new GabionWallSettings();

        List<GabionWallBuilder.Run> _runs = new List<GabionWallBuilder.Run>();
        Vector3 _centre;
        string _report = "";
        Vector2 _scroll;
        bool _previewInScene = true;

        [MenuItem("Tools/GrayZone/Gabion Wall/Place Along Barbed Wire Fence")]
        static void Open()
        {
            var window = GetWindow<GabionAlongFenceWindow>(false, "Gabion Wall", true);
            window.minSize = new Vector2(420, 560);
            window.Show();
        }

        void OnEnable()
        {
            settings.EnsurePrefab();
            SceneView.duringSceneGui += OnSceneGui;
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGui;
        }

        void OnGUI()
        {
            var s = settings;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Source fences", EditorStyles.boldLabel);
            s.nameFilter = EditorGUILayout.TextField(
                new GUIContent("Name contains", "Matched against the GameObject name."), s.nameFilter);
            s.includePlainBarbedWireFences = EditorGUILayout.Toggle(
                new GUIContent("Also BarbedWire_Fence (n)",
                    "The road-approach fences south of the compound are named BarbedWire_Fence (n) " +
                    "rather than ..._Redline_..., but the reference arrows cover them too."),
                s.includePlainBarbedWireFences);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Gabion", EditorStyles.boldLabel);
            s.gabionPrefab = (GameObject)EditorGUILayout.ObjectField(
                "Prefab", s.gabionPrefab, typeof(GameObject), false);
            s.gabionsPerPanel = Mathf.Max(1, EditorGUILayout.IntField(
                new GUIContent("Gabions per panel", "3 gives a continuous wall on a 3 m fence panel."),
                s.gabionsPerPanel));
            s.spanFactor = EditorGUILayout.Slider(
                new GUIContent("Span factor", "Fraction of the panel width the row covers."),
                s.spanFactor, 0.2f, 1.2f);
            s.extraGap = EditorGUILayout.FloatField(
                new GUIContent("Extra gap (m)", "Added to the auto offset. Negative pushes into the fence."),
                s.extraGap);
            s.lateralOffsetOverride = EditorGUILayout.FloatField(
                new GUIContent("Offset override (m)", "0 = auto (half fence thickness + half gabion depth)."),
                s.lateralOffsetOverride);
            s.rows = Mathf.Clamp(EditorGUILayout.IntField("Stack rows", s.rows), 1, 4);
            if (s.rows > 1)
                s.rowInset = EditorGUILayout.FloatField("Row inset (m)", s.rowInset);
            s.mergeDistance = EditorGUILayout.FloatField(
                new GUIContent("Merge distance (m)", "Drops gabions that land on top of each other at corners."),
                s.mergeDistance);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Outward side", EditorStyles.boldLabel);
            s.outwardMode = (OutwardMode)EditorGUILayout.EnumPopup("Mode", s.outwardMode);
            s.flipAll = EditorGUILayout.Toggle("Flip all", s.flipAll);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Fitting", EditorStyles.boldLabel);
            s.useCollidersForSize = EditorGUILayout.Toggle(
                new GUIContent("Measure from colliders", "Off = measure from mesh renderers."),
                s.useCollidersForSize);
            s.snapToGround = EditorGUILayout.Toggle("Snap to ground", s.snapToGround);
            if (s.snapToGround)
            {
                s.groundProbeHeight = EditorGUILayout.FloatField("Probe height (m)", s.groundProbeHeight);
                int mask = UnityEditorInternal.InternalEditorUtility
                    .LayerMaskToConcatenatedLayersMask(s.groundMask);
                mask = EditorGUILayout.MaskField("Ground layers", mask,
                    UnityEditorInternal.InternalEditorUtility.layers);
                s.groundMask = UnityEditorInternal.InternalEditorUtility
                    .ConcatenatedLayersMaskToLayerMask(mask);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Variation", EditorStyles.boldLabel);
            s.yawJitter = EditorGUILayout.Slider("Yaw jitter (deg)", s.yawJitter, 0f, 25f);
            s.positionJitter = EditorGUILayout.Slider("Position jitter (m)", s.positionJitter, 0f, 0.3f);
            s.scaleJitter = EditorGUILayout.Slider("Scale jitter", s.scaleJitter, 0f, 0.2f);
            s.randomSeed = EditorGUILayout.IntField("Seed", s.randomSeed);

            EditorGUILayout.Space();
            s.generatedRootName = EditorGUILayout.TextField("Output root", s.generatedRootName);
            _previewInScene = EditorGUILayout.Toggle("Preview in Scene view", _previewInScene);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Analyze", GUILayout.Height(28)))
                {
                    _runs = GabionWallBuilder.Analyze(s, out _centre, out _report);
                    SceneView.RepaintAll();
                }
                using (new EditorGUI.DisabledScope(_runs.Count == 0 || s.gabionPrefab == null))
                {
                    if (GUILayout.Button("Place", GUILayout.Height(28)))
                    {
                        GabionWallBuilder.Place(s, _runs, out _report);
                        var placed = GabionWallBuilder.FindGeneratedRoot(s.generatedRootName);
                        if (placed != null) Selection.activeGameObject = placed.gameObject;
                        SceneView.RepaintAll();
                    }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select generated")) SelectGenerated();
                if (GUILayout.Button("Remove generated")) RemoveGenerated();
            }

            if (_runs.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Runs", EditorStyles.boldLabel);
                foreach (var run in _runs)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(
                            run.Key + "  (" + run.Fences.Count + " panels)", GUILayout.MinWidth(220));
                        bool flipped = GUILayout.Toggle(run.Flip, "flip", "Button", GUILayout.Width(46));
                        if (flipped != run.Flip)
                        {
                            run.Flip = flipped;
                            GabionWallBuilder.ApplyOutward(s, run);
                            SceneView.RepaintAll();
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(_report))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(_report, MessageType.None);
            }

            EditorGUILayout.EndScrollView();
        }

        void SelectGenerated()
        {
            var root = GabionWallBuilder.FindGeneratedRoot(settings.generatedRootName);
            if (root == null)
            {
                _report = "No \"" + settings.generatedRootName + "\" in the scene.";
                return;
            }
            Selection.activeGameObject = root.gameObject;
            EditorGUIUtility.PingObject(root.gameObject);
        }

        void RemoveGenerated()
        {
            var root = GabionWallBuilder.FindGeneratedRoot(settings.generatedRootName);
            if (root == null)
            {
                _report = "No \"" + settings.generatedRootName + "\" in the scene.";
                return;
            }
            var scene = root.gameObject.scene;
            Undo.DestroyObjectImmediate(root.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            _report = "Removed \"" + settings.generatedRootName + "\".";
            SceneView.RepaintAll();
        }

        void OnSceneGui(SceneView view)
        {
            if (!_previewInScene || _runs.Count == 0) return;

            Handles.color = new Color(1f, 0.92f, 0.16f, 1f);
            foreach (var run in _runs)
            {
                foreach (var fence in run.Fences)
                {
                    var from = fence.CentreWorld + Vector3.up * 2f;
                    var to = from + fence.Outward * 2.5f;
                    Handles.DrawAAPolyLine(4f, from, to);
                    Handles.ConeHandleCap(0, to, Quaternion.LookRotation(fence.Outward),
                        0.7f, EventType.Repaint);
                }
            }

            var slots = GabionWallBuilder.BuildSlots(settings, _runs, out _);
            Handles.color = new Color(0.25f, 1f, 0.45f, 0.9f);
            foreach (var slot in slots)
            {
                var m = Matrix4x4.TRS(slot.Position, slot.Rotation, Vector3.one * slot.Scale);
                using (new Handles.DrawingScope(m))
                    Handles.DrawWireCube(new Vector3(0f, 0.66f, 0f), new Vector3(1.27f, 1.33f, 1.28f));
            }
        }
    }

}
