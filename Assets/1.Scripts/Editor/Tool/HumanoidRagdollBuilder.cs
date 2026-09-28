using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Humanoid 릭을 가진 캐릭터에 표준 래그돌 물리 골격(Rigidbody · Collider · CharacterJoint)을 구성합니다.
/// </summary>
/// <remarks>
/// Unity Ragdoll Wizard와 같은 역할을 하되, 표준 Humanoid 뼈 매핑만 사용하므로 모델의 Transform 이름에 의존하지 않습니다.
/// 같은 뼈에서 다시 실행하면 기존 Rigidbody · Collider · CharacterJoint를 재사용하고 값만 교정합니다.
///
/// 이 클래스는 화면 출력도 에셋 저장도 하지 않습니다. 진행 내역은 호출자가 넘긴 로그에만 쌓고,
/// 프리팹 열기/저장과 창 표시는 <see cref="HumanoidRagdollSetupWindow"/>가 담당합니다.
/// </remarks>
public static class HumanoidRagdollBuilder
{
    private static readonly string[] EnemyPrefabPaths =
    {
        "Assets/2.Prefabs/Enemy/Howler.prefab",
        "Assets/2.Prefabs/Enemy/Defense/Enemy/Scratcher(Defense_Player).prefab",
        "Assets/2.Prefabs/Enemy/Defense/Enemy/Stalker(Defense_Player).prefab",
        "Assets/2.Prefabs/Enemy/Defense/Enemy/Bloater(Defense_Player).prefab",
        "Assets/2.Prefabs/Enemy/Defense/Enemy/Crusher(Defense_Run_Player).prefab",
        "Assets/2.Prefabs/Enemy/Defense/Enemy/Howler(Defense_Player).prefab",
    };

    /// <summary>현재 Enemy 프리팹들을 각 Humanoid 골격 기준으로 다시 구성합니다.</summary>
    [MenuItem("GrayZone/Enemy/Enemy 래그돌 재구성")]
    public static void RebuildEnemyPrefabs()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Play Mode를 종료한 뒤 Enemy 래그돌을 재구성하십시오.");
            return;
        }

        foreach (string prefabPath in EnemyPrefabPaths)
        {
            RebuildPrefab(prefabPath);
        }
    }

    /// <summary>지정한 프리팹 하나의 래그돌을 현재 Humanoid Avatar에 맞춰 저장합니다.</summary>
    public static bool RebuildPrefab(string prefabPath)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
        {
            Debug.LogError($"Enemy 프리팹을 열지 못했습니다: {prefabPath}");
            return false;
        }

        List<string> log = new List<string>();
        try
        {
            if (!Build(root, log))
            {
                Debug.LogError("[Enemy Ragdoll] 구성 실패\n" + string.Join("\n", log));
                return false;
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Enemy Ragdoll] 구성 완료: {prefabPath}\n" + string.Join("\n", log));
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
    /// <summary>
    /// 래그돌 뼈 Collider를 올려 둘 레이어 이름입니다.
    /// </summary>
    /// <remarks>
    /// 시체는 바닥과 정적 지오메트리에만 닿아야 합니다. 뼈 Collider를 <c>Default</c>에 두면
    /// 무기 히트스캔 마스크에 걸려 시체가 총알을 막고, 살아 있는 개체의 이동도 가로막습니다
    /// (공용 `적 시스템` v0.2 §5.10.4는 사체가 총알을 먹지 않아야 한다고 규정합니다).
    /// 충돌 대상은 Physics 설정의 레이어 충돌 매트릭스가 정하며, 무기 쪽은 이 레이어를
    /// 히트스캔 마스크에서 빼는 것으로 처리합니다.
    /// </remarks>
    public const string CorpseLayerName = "Corpse";

    /// <summary>
    /// 바인드 포즈가 이보다 곧으면 굽힘 방향을 뽑을 수 없다고 보는 기준입니다.
    /// </summary>
    /// <remarks>
    /// 무릎·팔꿈치의 힌지 축은 바인드 포즈의 굽은 방향(위 마디 × 아래 마디)에서 뽑습니다.
    /// 완전히 곧게 편 릭은 그 외적이 0에 수렴해 방향을 정할 수 없으므로 대체 축으로 넘어갑니다.
    /// </remarks>
    private const float MinimumHingeCross = 0.02f;

    /// <summary>3축 회전이 모두 필요한 관절(고관절·어깨·허리·목)의 가동 범위입니다.</summary>
    private readonly struct BallLimits
    {
        /// <summary>마디 길이축 기준 비틀림 하한(도)입니다.</summary>
        public readonly float LowTwist;

        /// <summary>마디 길이축 기준 비틀림 상한(도)입니다.</summary>
        public readonly float HighTwist;

        /// <summary>주 흔들림 방향의 허용각(도)입니다. 좌우 대칭으로 적용됩니다.</summary>
        public readonly float Swing1;

        /// <summary>부 흔들림 방향의 허용각(도)입니다. 좌우 대칭으로 적용됩니다.</summary>
        public readonly float Swing2;

        public BallLimits(float lowTwist, float highTwist, float swing1, float swing2)
        {
            LowTwist = lowTwist;
            HighTwist = highTwist;
            Swing1 = swing1;
            Swing2 = swing2;
        }
    }

    /// <summary>한 방향으로만 접히는 관절(무릎·팔꿈치)의 가동 범위입니다.</summary>
    /// <remarks>
    /// 굽힘을 <c>swing</c>이 아니라 <c>twist</c>에 거는 것이 핵심입니다.
    /// <see cref="CharacterJoint.swing1Limit"/>은 좌우 대칭(±)이라 굽힘을 거기 걸면
    /// 무릎이 뒤로도 같은 각도만큼 꺾이는 역관절이 됩니다.
    /// <c>twist</c>는 하한·상한을 따로 줄 수 있어 한 방향 관절을 표현할 수 있습니다.
    /// </remarks>
    private readonly struct HingeLimits
    {
        /// <summary>굽힘 반대 방향(과신전) 허용각(도)입니다. 0에 가깝게 둡니다.</summary>
        public readonly float HyperExtend;

        /// <summary>굽힘 최대각(도)입니다.</summary>
        public readonly float MaxBend;

        /// <summary>힌지 축 자체가 흔들릴 수 있는 각(도)입니다. 작게 둘수록 순수한 경첩이 됩니다.</summary>
        public readonly float Wobble;

        public HingeLimits(float hyperExtend, float maxBend, float wobble)
        {
            HyperExtend = hyperExtend;
            MaxBend = maxBend;
            Wobble = wobble;
        }
    }

    private sealed class BoneBody
    {
        public readonly Transform Transform;
        public readonly Rigidbody Body;

        public BoneBody(Transform transform, Rigidbody body)
        {
            Transform = transform;
            Body = body;
        }
    }

    /// <summary>
    /// 지정한 캐릭터에 래그돌 골격을 구성합니다.
    /// </summary>
    /// <param name="root">Humanoid Animator를 가진 캐릭터 루트입니다.</param>
    /// <param name="log">진행 내역을 쌓을 로그입니다. null이면 기록하지 않습니다.</param>
    /// <returns>구성에 성공하면 true입니다. 필수 뼈가 없으면 아무것도 바꾸지 않고 false를 돌려줍니다.</returns>
    /// <remarks>
    /// 실패는 뼈를 하나라도 건드리기 전에 판정하므로, false를 받은 대상은 원래 상태 그대로입니다.
    /// </remarks>
    public static bool Build(GameObject root, List<string> log)
    {
        if (root == null)
        {
            Add(log, "대상이 비어 있습니다.");
            return false;
        }

        Animator animator = FindHumanoidAnimator(root);
        if (animator == null)
        {
            Add(log, $"'{root.name}'에서 필수 Humanoid 뼈가 연결된 Animator를 찾지 못했습니다. "
                + "모델 Import 설정의 Rig가 Humanoid인지 확인하세요.");
            return false;
        }

        return Build(root, animator, log);
    }

    /// <summary>
    /// 지정한 Humanoid Animator의 골격에 래그돌을 구성합니다.
    /// </summary>
    /// <param name="root">래그돌 컨트롤러를 소유할 캐릭터 루트입니다.</param>
    /// <param name="animator">래그돌을 구성할 모델의 Humanoid Animator입니다.</param>
    /// <param name="log">진행 내역을 쌓을 로그입니다. null이면 기록하지 않습니다.</param>
    /// <returns>대상과 필수 뼈가 유효하고 구성이 완료되면 true입니다.</returns>
    /// <remarks>
    /// 모델 교체 프리팹에는 구 모델과 새 모델의 Animator가 함께 남아 있을 수 있습니다.
    /// 이 오버로드는 호출자가 적용 대상을 명시해 계층 검색 순서에 따라 구 리그가 선택되는 일을 막습니다.
    /// 기존 <see cref="Build(GameObject, List{string})"/>는 호환성을 위해 자동 탐색을 유지합니다.
    /// </remarks>
    public static bool Build(GameObject root, Animator animator, List<string> log)
    {
        if (root == null)
        {
            Add(log, "대상이 비어 있습니다.");
            return false;
        }

        if (animator == null)
        {
            Add(log, "적용할 Animator가 비어 있습니다.");
            return false;
        }

        if (animator.transform != root.transform && !animator.transform.IsChildOf(root.transform))
        {
            Add(log, $"Animator '{animator.name}'가 대상 루트 '{root.name}'의 계층에 속하지 않습니다.");
            return false;
        }

        if (animator.avatar == null || !animator.avatar.isHuman || !animator.avatar.isValid)
        {
            Add(log, $"Animator '{animator.name}'의 Avatar가 유효한 Humanoid가 아닙니다.");
            return false;
        }

        Add(log, $"Humanoid Animator: {animator.name} (Avatar: {animator.avatar.name})");

        Dictionary<HumanBodyBones, Transform> bones = CollectRequiredBones(animator, log);
        if (bones == null)
        {
            return false;
        }

        Configure(root, bones, log);
        return true;
    }

    /// <summary>
    /// 지정한 캐릭터가 이미 래그돌 골격을 가지고 있는지 확인합니다.
    /// </summary>
    /// <remarks>재구성인지 최초 구성인지를 로그에 구분해 적기 위한 것입니다.</remarks>
    public static bool HasRagdoll(GameObject root)
    {
        return root != null && root.GetComponentInChildren<CharacterJoint>(true) != null;
    }

    private static void Add(List<string> log, string message)
    {
        log?.Add(message);
    }

    private static Animator FindHumanoidAnimator(GameObject root)
    {
        Animator[] animators = root.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            Animator animator = animators[i];
            if (animator.avatar == null || !animator.avatar.isHuman)
            {
                continue;
            }

            if (animator.GetBoneTransform(HumanBodyBones.Hips) != null
                && animator.GetBoneTransform(HumanBodyBones.Head) != null
                && animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg) != null
                && animator.GetBoneTransform(HumanBodyBones.RightUpperArm) != null)
            {
                return animator;
            }
        }

        return null;
    }

    private static Dictionary<HumanBodyBones, Transform> CollectRequiredBones(Animator animator, List<string> log)
    {
        HumanBodyBones[] required =
        {
            HumanBodyBones.Hips,
            HumanBodyBones.Spine,
            HumanBodyBones.Head,
            HumanBodyBones.LeftUpperLeg,
            HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg,
            HumanBodyBones.RightLowerLeg,
            HumanBodyBones.RightFoot,
            HumanBodyBones.LeftUpperArm,
            HumanBodyBones.LeftLowerArm,
            HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm,
            HumanBodyBones.RightLowerArm,
            HumanBodyBones.RightHand,
        };

        Dictionary<HumanBodyBones, Transform> bones = new Dictionary<HumanBodyBones, Transform>();
        List<string> missing = new List<string>();

        for (int i = 0; i < required.Length; i++)
        {
            Transform bone = animator.GetBoneTransform(required[i]);
            if (bone == null)
            {
                missing.Add(required[i].ToString());
            }
            else
            {
                bones.Add(required[i], bone);
            }
        }

        if (missing.Count > 0)
        {
            Add(log, "래그돌 구성에 필요한 Humanoid 뼈가 없습니다: " + string.Join(", ", missing));
            return null;
        }

        Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        if (chest == null)
        {
            chest = bones[HumanBodyBones.Spine];
            Add(log, "Chest 뼈가 없어 Spine을 흉부로 사용합니다.");
        }

        bones[HumanBodyBones.Chest] = chest;

        Transform neck = animator.GetBoneTransform(HumanBodyBones.Neck);
        if (neck != null)
        {
            bones[HumanBodyBones.Neck] = neck;
        }
        else
        {
            Add(log, "Neck 뼈가 없습니다. 흉부 Collider 상단은 흉부와 머리의 중간 지점으로 잡습니다.");
        }

        Add(log, $"필수 뼈 {required.Length}개 확인 완료 (흉부: {chest.name}).");
        return bones;
    }

    private static void Configure(GameObject root, Dictionary<HumanBodyBones, Transform> bones, List<string> log)
    {
        bool hadRagdoll = HasRagdoll(root);
        Add(log, hadRagdoll ? "기존 래그돌을 재구성합니다." : "래그돌을 새로 구성합니다.");

        EnsureRagdollController(root, log);

        int corpseLayer = LayerMask.NameToLayer(CorpseLayerName);
        if (corpseLayer < 0)
        {
            Add(log, $"경고: '{CorpseLayerName}' 레이어가 없어 뼈 Collider의 레이어를 바꾸지 못합니다. "
                + "이 상태로 두면 시체가 총알을 막습니다. Project Settings > Tags and Layers에 추가하세요.");
        }

        Dictionary<HumanBodyBones, BoneBody> bodies = new Dictionary<HumanBodyBones, BoneBody>();
        float totalMass = 0.0f;
        totalMass += AddBody(bodies, bones, HumanBodyBones.Hips, 3.0f);
        totalMass += AddBody(bodies, bones, HumanBodyBones.Chest, 2.5f);
        totalMass += AddBody(bodies, bones, HumanBodyBones.Head, 1.0f);
        totalMass += AddBody(bodies, bones, HumanBodyBones.LeftUpperLeg, 1.5f);
        totalMass += AddBody(bodies, bones, HumanBodyBones.LeftLowerLeg, 1.0f);
        totalMass += AddBody(bodies, bones, HumanBodyBones.RightUpperLeg, 1.5f);
        totalMass += AddBody(bodies, bones, HumanBodyBones.RightLowerLeg, 1.0f);
        totalMass += AddBody(bodies, bones, HumanBodyBones.LeftUpperArm, 0.75f);
        totalMass += AddBody(bodies, bones, HumanBodyBones.LeftLowerArm, 0.5f);
        totalMass += AddBody(bodies, bones, HumanBodyBones.RightUpperArm, 0.75f);
        totalMass += AddBody(bodies, bones, HumanBodyBones.RightLowerArm, 0.5f);
        Add(log, $"Rigidbody {bodies.Count}개 구성, 총 질량 {totalMass:F2}kg.");

        ConfigurePelvisCollider(bones, corpseLayer, log);
        ConfigureChestCollider(bones, corpseLayer, log);
        ConfigureHeadCollider(bones, corpseLayer, log);
        ConfigureLimbCollider(bones, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 0.22f, corpseLayer, log);
        ConfigureLimbCollider(bones, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.18f, corpseLayer, log);
        ConfigureLimbCollider(bones, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.22f, corpseLayer, log);
        ConfigureLimbCollider(bones, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.18f, corpseLayer, log);
        ConfigureLimbCollider(bones, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, 0.20f, corpseLayer, log);
        ConfigureLimbCollider(bones, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.17f, corpseLayer, log);
        ConfigureLimbCollider(bones, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 0.20f, corpseLayer, log);
        ConfigureLimbCollider(bones, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.17f, corpseLayer, log);

        ConnectBall(
            root.transform, bodies[HumanBodyBones.Chest], bodies[HumanBodyBones.Hips],
            bones[HumanBodyBones.Head], new BallLimits(-20.0f, 20.0f, 25.0f, 20.0f), "허리", log);
        ConnectBall(
            root.transform, bodies[HumanBodyBones.Head], bodies[HumanBodyBones.Chest],
            null, new BallLimits(-30.0f, 30.0f, 30.0f, 25.0f), "목", log);

        ConnectLeg(root.transform, bodies, bones, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, "왼", log);
        ConnectLeg(root.transform, bodies, bones, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, "오른", log);
        ConnectArm(root.transform, bodies, bones, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, "왼", log);
        ConnectArm(root.transform, bodies, bones, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, "오른", log);
    }

    private static void EnsureRagdollController(GameObject root, List<string> log)
    {
        if (root.GetComponent<RagdollController>() == null)
        {
            root.AddComponent<RagdollController>();
            Add(log, "RagdollController를 루트에 추가했습니다.");
        }
        else
        {
            Add(log, "RagdollController가 이미 있어 그대로 둡니다.");
        }
    }

    /// <summary>
    /// 지정한 뼈에 래그돌 Rigidbody를 만들거나 교정하고, 적용한 질량을 돌려줍니다.
    /// </summary>
    /// <remarks>
    /// <c>detectCollisions</c>는 건드리지 않습니다. 직렬화되지 않는 런타임 전용 값이라 프리팹에 남지 않고,
    /// 이 값을 끄면 뼈 Rigidbody에 소속된 피격 히트박스와 근접 판정 Collider까지 물리 씬에서 통째로 빠집니다.
    /// 평상시 물리를 재우는 것은 <see cref="RagdollController"/>가 <c>isKinematic</c>과 Collider 비활성으로만 처리합니다.
    /// </remarks>
    private static float AddBody(
        Dictionary<HumanBodyBones, BoneBody> bodies,
        Dictionary<HumanBodyBones, Transform> bones,
        HumanBodyBones boneType,
        float mass)
    {
        Transform bone = bones[boneType];
        Rigidbody body = bone.GetComponent<Rigidbody>();
        if (body == null)
        {
            body = bone.gameObject.AddComponent<Rigidbody>();
        }

        body.mass = mass;
        body.useGravity = true;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        body.linearDamping = 0.05f;
        body.angularDamping = 0.05f;
        // CharacterJoint 래그돌은 기본 반복 수로는 긴 팔·다리 체인에서 제약이 한 프레임에 다 풀리지
        // 않아 늘어날 수 있습니다. RagdollController도 런타임에 다시 보정하므로 기존 프리팹에도 적용됩니다.
        body.solverIterations = 12;
        body.solverVelocityIterations = 4;
        body.maxAngularVelocity = 12.0f;

        bodies.Add(boneType, new BoneBody(bone, body));
        return mass;
    }

    private static void ConfigurePelvisCollider(Dictionary<HumanBodyBones, Transform> bones, int corpseLayer, List<string> log)
    {
        Transform pelvis = bones[HumanBodyBones.Hips];
        BoxCollider collider = EnsureCollider<BoxCollider>(pelvis, corpseLayer);
        FitBox(
            collider,
            pelvis,
            bones[HumanBodyBones.LeftUpperLeg].position,
            bones[HumanBodyBones.RightUpperLeg].position,
            bones[HumanBodyBones.Chest].position);
        Add(log, $"골반 Box: size={Format(collider.size)}");
    }

    /// <summary>
    /// 흉부 Collider를 골반부터 목까지의 범위로 맞춥니다.
    /// </summary>
    /// <remarks>
    /// 상단 기준을 머리로 잡으면 안 됩니다. 흉부 Box가 머리 높이까지 자라 몸통이 통짜 판자가 되고,
    /// 시체가 쓰러져도 머리가 바닥에 닿지 못하며 팔뚝이 머리 근처로 오지 못합니다.
    /// Neck이 없는 릭은 흉부와 머리의 중간 지점을 대신 씁니다.
    /// </remarks>
    private static void ConfigureChestCollider(Dictionary<HumanBodyBones, Transform> bones, int corpseLayer, List<string> log)
    {
        Transform chest = bones[HumanBodyBones.Chest];
        Vector3 top = bones.TryGetValue(HumanBodyBones.Neck, out Transform neck)
            ? neck.position
            : Vector3.Lerp(chest.position, bones[HumanBodyBones.Head].position, 0.5f);

        BoxCollider collider = EnsureCollider<BoxCollider>(chest, corpseLayer);
        FitBox(
            collider,
            chest,
            bones[HumanBodyBones.Hips].position,
            bones[HumanBodyBones.LeftUpperArm].position,
            bones[HumanBodyBones.RightUpperArm].position,
            top);
        Add(log, $"흉부 Box: size={Format(collider.size)} (상단 기준 {(neck != null ? "Neck" : "흉부~머리 중간")})");
    }

    /// <summary>
    /// 머리 Collider를 머리 메시 크기에 맞춥니다.
    /// </summary>
    /// <remarks>
    /// 목 뼈와의 거리로 반지름을 잡으면 목 뼈가 촘촘히 나뉜 릭에서 머리가 터무니없이 작아집니다.
    /// 머리 뼈에 스킨된 메시 범위를 우선 쓰고, 구할 수 없을 때만 목 거리로 물러섭니다.
    /// </remarks>
    private static void ConfigureHeadCollider(Dictionary<HumanBodyBones, Transform> bones, int corpseLayer, List<string> log)
    {
        Transform head = bones[HumanBodyBones.Head];
        Transform reference = bones.TryGetValue(HumanBodyBones.Neck, out Transform neck)
            ? neck
            : bones[HumanBodyBones.Chest];

        Vector3 localReference = head.InverseTransformPoint(reference.position);
        float fallbackRadius = Mathf.Max(0.05f, localReference.magnitude * 0.75f);

        float radius = fallbackRadius;
        string source = "목까지의 거리";
        if (TryResolveHeadRadius(head, out float meshRadius) && meshRadius > fallbackRadius)
        {
            radius = meshRadius;
            source = "머리 메시 범위";
        }

        SphereCollider collider = EnsureCollider<SphereCollider>(head, corpseLayer);

        // 머리 뼈는 보통 목 쪽 끝에 있으므로, 구를 목 반대편(머리 중심)으로 밀어 줍니다.
        collider.center = localReference.sqrMagnitude > 0.000001f
            ? -localReference.normalized * radius * 0.35f
            : Vector3.zero;
        collider.radius = radius;
        Add(log, $"머리 Sphere: radius={radius:F3} ({source})");
    }

    /// <summary>
    /// 머리 뼈를 주로 따르는 정점 범위에서 머리 반지름을 추정합니다.
    /// </summary>
    /// <returns>추정에 성공하면 true이며, <paramref name="radius"/>는 머리 뼈 로컬 기준 반지름입니다.</returns>
    /// <remarks>
    /// <see cref="Mesh.isReadable"/>로 걸러내지 않습니다. 그 값은 <b>런타임</b>에 CPU 사본을 유지하는지를
    /// 나타내며, Editor는 에셋 데이터를 통째로 들고 있어 Read/Write가 꺼진 메시도 그대로 읽힙니다
    /// (실측: 이 프로젝트의 적 메시가 <c>isReadable=False</c>인데 정점 34,123개를 정상 반환).
    /// 그래서 모델 임포트 설정을 건드릴 필요도, 런타임 메모리를 더 쓸 필요도 없습니다.
    /// 이 클래스는 Editor 전용이므로 읽기 실패는 예외로만 방어합니다.
    /// </remarks>
    private static bool TryResolveHeadRadius(Transform head, out float radius)
    {
        radius = 0.0f;

        SkinnedMeshRenderer[] renderers = head.root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SkinnedMeshRenderer renderer = renderers[i];
            Mesh mesh = renderer.sharedMesh;
            if (mesh == null)
            {
                continue;
            }

            int boneIndex = System.Array.IndexOf(renderer.bones, head);
            if (boneIndex < 0)
            {
                continue;
            }

            BoneWeight[] weights;
            Vector3[] vertices;
            try
            {
                weights = mesh.boneWeights;
                vertices = mesh.vertices;
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[래그돌 구성] '{mesh.name}' 메시를 읽지 못해 머리 크기를 뼈 간격으로 추정합니다: {exception.Message}");
                continue;
            }

            if (weights.Length != vertices.Length)
            {
                continue;
            }

            float maxDistance = 0.0f;
            int counted = 0;
            for (int v = 0; v < weights.Length; v++)
            {
                BoneWeight weight = weights[v];
                bool dominant = (weight.boneIndex0 == boneIndex && weight.weight0 > 0.5f)
                    || (weight.boneIndex1 == boneIndex && weight.weight1 > 0.5f);
                if (!dominant)
                {
                    continue;
                }

                Vector3 world = renderer.transform.TransformPoint(vertices[v]);
                maxDistance = Mathf.Max(maxDistance, Vector3.Distance(world, head.position));
                counted++;
            }

            if (counted > 0 && maxDistance > 0.0f)
            {
                // 뼈 원점이 머리 아래쪽(목 부근)이라 최대 거리는 지름에 가깝습니다.
                radius = maxDistance * 0.55f / Mathf.Max(0.0001f, head.lossyScale.x);
                return true;
            }
        }

        return false;
    }

    private static void ConfigureLimbCollider(
        Dictionary<HumanBodyBones, Transform> bones,
        HumanBodyBones startBone,
        HumanBodyBones endBone,
        float radiusRatio,
        int corpseLayer,
        List<string> log)
    {
        Transform start = bones[startBone];
        Transform end = bones[endBone];
        Vector3 localEnd = start.InverseTransformPoint(end.position);
        float length = localEnd.magnitude;
        float radius = Mathf.Max(0.025f, length * radiusRatio);
        int direction = LargestAxis(localEnd);
        float axisLength = Mathf.Abs(GetAxis(localEnd, direction));

        CapsuleCollider collider = EnsureCollider<CapsuleCollider>(start, corpseLayer);
        collider.direction = direction;
        collider.center = localEnd * 0.5f;
        collider.radius = radius;
        collider.height = Mathf.Max(axisLength, radius * 2.0f);
        Add(log, $"{start.name} Capsule: height={collider.height:F3} radius={radius:F3}");
    }

    /// <summary>
    /// 뼈에 래그돌 Collider를 만들거나 재사용하고, 시체 레이어로 옮깁니다.
    /// </summary>
    /// <remarks>
    /// 피격 히트박스는 자기 GameObject에 따로 있으므로 이 레이어 변경에 휩쓸리지 않습니다.
    /// 레이어는 상속되지 않고 GameObject마다 개별이라, 뼈만 시체 레이어로 옮겨집니다.
    /// </remarks>
    private static T EnsureCollider<T>(Transform bone, int corpseLayer) where T : Collider
    {
        T collider = bone.GetComponent<T>();
        if (collider == null)
        {
            collider = bone.gameObject.AddComponent<T>();
        }

        collider.isTrigger = false;
        collider.enabled = false;

        if (corpseLayer >= 0)
        {
            bone.gameObject.layer = corpseLayer;
        }

        return collider;
    }

    /// <summary>
    /// 지정한 월드 지점들을 감싸도록 Box Collider를 맞춥니다.
    /// </summary>
    /// <remarks>
    /// 최소 두께는 만들어진 상자 자신의 최대 변을 기준으로 잡습니다.
    /// 뼈 원점에서 참조점까지의 거리를 기준으로 삼으면, 골반처럼 참조점이 모두 가까이 모인 부위에서
    /// 두께가 몇 cm짜리 판자로 나옵니다. 얇은 몸통은 시체가 옆으로 누울 때 바닥을 뚫습니다.
    /// </remarks>
    private static void FitBox(BoxCollider collider, Transform owner, params Vector3[] worldPoints)
    {
        Bounds bounds = new Bounds(owner.InverseTransformPoint(worldPoints[0]), Vector3.zero);
        for (int i = 1; i < worldPoints.Length; i++)
        {
            bounds.Encapsulate(owner.InverseTransformPoint(worldPoints[i]));
        }

        // 뼈 원점 자신도 몸통 안에 있어야 합니다. 참조점만으로 잡으면 상자가 원점을 비껴갈 수 있습니다.
        bounds.Encapsulate(Vector3.zero);

        Vector3 size = bounds.size;
        float span = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        float padding = Mathf.Max(span * 0.10f, 0.01f);
        size += Vector3.one * (padding * 2.0f);

        float minimumThickness = span * 0.45f;
        size.x = Mathf.Max(size.x, minimumThickness);
        size.y = Mathf.Max(size.y, minimumThickness);
        size.z = Mathf.Max(size.z, minimumThickness);

        collider.center = bounds.center;
        collider.size = size;
    }

    private static void ConnectLeg(
        Transform characterRoot,
        Dictionary<HumanBodyBones, BoneBody> bodies,
        Dictionary<HumanBodyBones, Transform> bones,
        HumanBodyBones upper,
        HumanBodyBones lower,
        HumanBodyBones foot,
        string side,
        List<string> log)
    {
        ConnectBall(
            characterRoot, bodies[upper], bodies[HumanBodyBones.Hips], bones[lower],
            new BallLimits(-25.0f, 35.0f, 45.0f, 30.0f), side + "쪽 고관절", log);
        ConnectHinge(
            characterRoot, bodies[lower], bodies[upper],
            bones[upper], bones[lower], bones[foot],
            new HingeLimits(5.0f, 110.0f, 8.0f), side + "쪽 무릎", log);
    }

    private static void ConnectArm(
        Transform characterRoot,
        Dictionary<HumanBodyBones, BoneBody> bodies,
        Dictionary<HumanBodyBones, Transform> bones,
        HumanBodyBones upper,
        HumanBodyBones lower,
        HumanBodyBones hand,
        string side,
        List<string> log)
    {
        ConnectBall(
            characterRoot, bodies[upper], bodies[HumanBodyBones.Chest], bones[lower],
            new BallLimits(-45.0f, 45.0f, 65.0f, 60.0f), side + "쪽 어깨", log);
        ConnectHinge(
            characterRoot, bodies[lower], bodies[upper],
            bones[upper], bones[lower], bones[hand],
            new HingeLimits(5.0f, 120.0f, 8.0f), side + "쪽 팔꿈치", log);
    }

    /// <summary>
    /// 3축 회전이 필요한 관절을 연결합니다. 비틀림 축은 마디의 길이 방향입니다.
    /// </summary>
    private static void ConnectBall(
        Transform characterRoot,
        BoneBody child,
        BoneBody parent,
        Transform axisTarget,
        BallLimits limits,
        string label,
        List<string> log)
    {
        CharacterJoint joint = EnsureJoint(child, parent);

        Vector3 worldAxis = axisTarget != null
            ? axisTarget.position - child.Transform.position
            : child.Transform.position - parent.Transform.position;
        if (worldAxis.sqrMagnitude < 0.000001f)
        {
            worldAxis = child.Transform.up;
        }

        Vector3 localAxis = child.Transform.InverseTransformDirection(worldAxis.normalized).normalized;
        Vector3 localForward = child.Transform.InverseTransformDirection(characterRoot.forward);
        Vector3 swingAxis = Vector3.ProjectOnPlane(localForward, localAxis);
        if (swingAxis.sqrMagnitude < 0.000001f)
        {
            Vector3 localUp = child.Transform.InverseTransformDirection(characterRoot.up);
            swingAxis = Vector3.ProjectOnPlane(localUp, localAxis);
        }

        joint.axis = localAxis;
        joint.swingAxis = swingAxis.normalized;
        joint.lowTwistLimit = Limit(limits.LowTwist);
        joint.highTwistLimit = Limit(limits.HighTwist);
        joint.swing1Limit = Limit(limits.Swing1);
        joint.swing2Limit = Limit(limits.Swing2);

        Add(log, $"{label}: 볼 관절 twist[{limits.LowTwist:F0}..{limits.HighTwist:F0}] "
            + $"swing({limits.Swing1:F0}/{limits.Swing2:F0})");
    }

    /// <summary>
    /// 한 방향으로만 접히는 관절을 연결합니다.
    /// </summary>
    /// <remarks>
    /// 굽힘 축은 바인드 포즈에서 위 마디와 아래 마디가 이루는 평면의 법선(외적)으로 잡습니다.
    /// 릭이 완전히 곧아 외적이 0에 수렴하면 방향을 정할 수 없으므로 캐릭터 오른쪽 축으로 물러서고 경고를 남깁니다.
    ///
    /// 굽힘은 <b>하한(lowTwistLimit)</b> 쪽입니다. 축을 <c>위 마디 × 아래 마디</c>로 잡으면
    /// 기하학적으로는 양의 회전이 "더 접히는" 방향처럼 보이지만, Unity/PhysX의 twist 부호는 그 반대입니다.
    /// <c>Physics.Simulate</c>로 시체를 떨어뜨려 실측한 결과 상한에 굽힘을 주면 무릎이 -87도까지
    /// 역관절로 꺾였고, 하한으로 옮기고 나서야 바인드 포즈와 같은 방향으로 접혔습니다.
    /// 부호를 바꿀 일이 생기면 추측하지 말고 같은 방법으로 다시 재십시오.
    /// </remarks>
    private static void ConnectHinge(
        Transform characterRoot,
        BoneBody child,
        BoneBody parent,
        Transform upperBone,
        Transform midBone,
        Transform lowerBone,
        HingeLimits limits,
        string label,
        List<string> log)
    {
        CharacterJoint joint = EnsureJoint(child, parent);

        Vector3 upperDirection = (midBone.position - upperBone.position).normalized;
        Vector3 lowerDirection = (lowerBone.position - midBone.position).normalized;
        Vector3 hingeWorld = Vector3.Cross(upperDirection, lowerDirection);
        float bendAngle = Vector3.Angle(upperDirection, lowerDirection);

        string source;
        if (hingeWorld.magnitude < MinimumHingeCross)
        {
            hingeWorld = characterRoot.right;
            source = $"경고: 바인드 포즈가 거의 곧아(굽힘 {bendAngle:F1}도) 굽힘 방향을 뽑지 못했습니다. "
                + "캐릭터 오른쪽 축으로 대체했으므로 접히는 방향을 눈으로 확인하세요";
        }
        else
        {
            source = $"바인드 포즈 굽힘 {bendAngle:F1}도에서 축 추출";
        }

        hingeWorld = hingeWorld.normalized;

        Vector3 localAxis = child.Transform.InverseTransformDirection(hingeWorld).normalized;
        Vector3 localLong = child.Transform.InverseTransformDirection(lowerDirection);
        Vector3 swingAxis = Vector3.ProjectOnPlane(localLong, localAxis);
        if (swingAxis.sqrMagnitude < 0.000001f)
        {
            swingAxis = Vector3.ProjectOnPlane(child.Transform.InverseTransformDirection(characterRoot.up), localAxis);
        }

        joint.axis = localAxis;
        joint.swingAxis = swingAxis.normalized;
        joint.lowTwistLimit = Limit(-Mathf.Abs(limits.MaxBend));
        joint.highTwistLimit = Limit(Mathf.Abs(limits.HyperExtend));
        joint.swing1Limit = Limit(limits.Wobble);
        joint.swing2Limit = Limit(limits.Wobble);

        Add(log, $"{label}: 경첩 관절 twist[{-limits.MaxBend:F0}..{limits.HyperExtend:F0}] "
            + $"(굽힘 {limits.MaxBend:F0}도 / 과신전 {limits.HyperExtend:F0}도) 흔들림 {limits.Wobble:F0} - {source}");
    }

    private static CharacterJoint EnsureJoint(BoneBody child, BoneBody parent)
    {
        CharacterJoint joint = child.Transform.GetComponent<CharacterJoint>();
        if (joint == null)
        {
            joint = child.Transform.gameObject.AddComponent<CharacterJoint>();
        }

        joint.connectedBody = parent.Body;
        joint.autoConfigureConnectedAnchor = true;
        joint.enableCollision = false;
        // 표준 트리형 래그돌은 전처리를 켜야 PhysX가 풀기 어려운 관절 제약을 사전에 정리합니다.
        // 끈 상태는 특별한 폐루프 관절에는 쓸 수 있지만, 이 구조에서는 회전 속도 누적과 관절 늘어짐을 키웁니다.
        joint.enablePreprocessing = true;
        joint.enableProjection = true;
        joint.projectionDistance = 0.02f;
        joint.projectionAngle = 5.0f;
        return joint;
    }

    private static SoftJointLimit Limit(float angle)
    {
        SoftJointLimit limit = new SoftJointLimit
        {
            limit = angle,
            bounciness = 0.0f,
            contactDistance = 0.0f,
        };
        return limit;
    }

    private static int LargestAxis(Vector3 value)
    {
        Vector3 absolute = new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        if (absolute.x > absolute.y && absolute.x > absolute.z)
        {
            return 0;
        }

        return absolute.y > absolute.z ? 1 : 2;
    }

    private static float GetAxis(Vector3 value, int axis)
    {
        switch (axis)
        {
            case 0:
                return value.x;
            case 1:
                return value.y;
            default:
                return value.z;
        }
    }

    private static string Format(Vector3 value)
    {
        return $"({value.x:F3}, {value.y:F3}, {value.z:F3})";
    }
}
