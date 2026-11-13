#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityCliConnector;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UIElements;
using UIImage = UnityEngine.UI.Image;

namespace GrayZone.EditorTools
{
    /// <summary>
    /// unity-cli 커스텀 명령입니다. 이미 컴파일된 상태로 실행되므로 <c>exec</c>와 달리 Play Mode에서도
    /// 컴파일 지연 없이 즉시 결과를 반환합니다.
    /// </summary>
    /// <remarks>
    /// 호출: <c>unity-cli --project . gz_dump --params '{"what":"sceneui"}'</c>
    /// what: <c>combat</c>(플레이어 장전/조준/무기 상태), <c>sceneui</c>(캔버스·UIDocument·Image·SpriteRenderer),
    /// <c>go</c>(name 파라미터로 지정한 GameObject의 컴포넌트/활성/위치),
    /// <c>anim</c>(Animator 레이어별 현재/다음 스테이트, 전이 진행도, 파라미터 값),
    /// <c>ragdoll</c>(변이체 래그돌 전환 상태와 뼈 속도),
    /// <c>squad</c>(스쿼드 단위 상태와 AI 팀원 목적지 간격. 개인 상태는 <c>combat</c>이 봅니다).
    ///
    /// <c>killenemy</c>는 조회가 아니라 <b>상태를 바꾸는 디버그 동작</b>입니다. 변이체 하나를 즉사시켜
    /// 사망 연출을 검증합니다. 이름이 dump인 도구에 동작이 섞여 있으므로 호출 시 유의하십시오.
    /// Play Mode에서 <c>exec</c>이 컴파일 지연으로 멈춰 검증 진입점이 필요해 여기에 두었습니다.
    /// </remarks>
    [UnityCliTool(Name = "gz_dump", Group = "GrayZone",
        Description = "GrayZone 런타임/씬 상태를 한 번에 덤프합니다. what: combat | sceneui | go | revive | anim | ragdoll | killenemy | noise | squad | inspectenemyrig | runtimeenemyrig | forcelod.")]
    public static class GZDebugDump
    {
        public class Parameters
        {
            [ToolParameter("덤프 종류: combat | sceneui | go | revive | anim | ragdoll | killenemy | noise | squad | inspectenemyrig | runtimeenemyrig | forcelod", Required = true)]
            public string What { get; set; }

            [ToolParameter("what=go 일 때 조회할 GameObject 이름(부분 일치). what=anim 에서는 대상 필터로 쓰입니다.")]
            public string Name { get; set; }

            [ToolParameter("what=forcelod일 때 강제로 표시할 LOD 번호입니다. -1은 자동 선택으로 복귀합니다.")]
            public int Lod { get; set; }

            [ToolParameter("what=killenemy일 때 true면 시체를 풀에 반환하지 않고 Play Mode 동안 유지합니다.")]
            public bool Hold { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            var p = new ToolParams(@params ?? new JObject());
            string what = p.Get("what", "").ToLowerInvariant();
            int lod = int.TryParse(p.Get("lod", "-1"), out int parsedLod) ? parsedLod : -1;
            bool hold = bool.TryParse(p.Get("hold", "false"), out bool parsedHold) && parsedHold;

            switch (what)
            {
                case "combat": return DumpCombat();
                case "sceneui": return DumpSceneUI();
                case "go": return DumpGameObject(p.Get("name", ""));
                case "revive": return DumpRevive();
                case "anim": return DumpAnimator(p.Get("name", ""));
                case "ragdoll": return DumpRagdoll(p.Get("name", ""));
                case "killenemy": return KillEnemy(p.Get("name", ""), hold);
                case "noise": return DumpNoise(p.Get("name", ""));
                case "squad": return DumpSquad();
                case "strandai": return StrandAiMember(p.Get("name", ""));
                case "freezeai": return FreezeAiMember(p.Get("name", ""));
                case "testshot": return TestShotAtPart(p.Get("name", ""));
                case "testaim": return TestAimParallax(p.Get("name", ""));
                case "inspectenemyrig": return InspectEnemyRigAssets();
                case "runtimeenemyrig": return DumpRuntimeEnemyRig(p.Get("name", ""));
                case "forcelod": return ForceEnemyLod(p.Get("name", ""), lod);
                default:
                    return new ErrorResponse(
                        "what 파라미터가 필요합니다: combat | sceneui | go | revive | anim | ragdoll | killenemy | noise | squad | strandai | freezeai | inspectenemyrig | runtimeenemyrig | forcelod");
            }
        }

        /// <summary>Enemy 모델 폴더의 모든 리그에 대해 메시·본·Avatar 연결 정보를 덤프합니다.</summary>
        /// <remarks>
        /// 모델을 인스턴스화하거나 씬을 변경하지 않고, AssetDatabase에서 읽은 서브 에셋만 조사합니다.
        /// 모델 파일명을 하드코딩하지 않고 폴더를 훑으므로, 아티스트가 FBX를 교체·개명해도 그대로 동작합니다.
        /// 백업 폴더는 제외합니다.
        /// </remarks>
        private static object InspectEnemyRigAssets()
        {
            const string EnemyModelFolder = "Assets/3.Resources/Model/Enemy";
            string[] modelPaths = UnityEditor.AssetDatabase
                .FindAssets("t:Model", new[] { EnemyModelFolder })
                .Select(UnityEditor.AssetDatabase.GUIDToAssetPath)
                .Where(path => !path.Contains("_BackUp/"))
                .Distinct()
                .OrderBy(path => path)
                .ToArray();

            if (modelPaths.Length == 0)
            {
                return new ErrorResponse($"Enemy 모델을 찾지 못했습니다: {EnemyModelFolder}");
            }

            var results = new List<object>();
            for (int modelIndex = 0; modelIndex < modelPaths.Length; modelIndex++)
            {
                string modelPath = modelPaths[modelIndex];
                GameObject model = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                UnityEditor.ModelImporter importer = UnityEditor.AssetImporter.GetAtPath(modelPath)
                    as UnityEditor.ModelImporter;
                Avatar avatar = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(modelPath)
                    .OfType<Avatar>()
                    .FirstOrDefault();

                if (model == null)
                {
                    results.Add(new { path = modelPath, error = "GameObject 모델 서브에셋을 찾지 못했습니다." });
                    continue;
                }

                SkinnedMeshRenderer[] renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var rendererResults = new List<object>();
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    SkinnedMeshRenderer renderer = renderers[rendererIndex];
                    Transform[] bones = renderer.bones;
                    int nullBones = bones.Count(bone => bone == null);
                    rendererResults.Add(new
                    {
                        path = GetRelativeTransformPath(renderer.transform, model.transform),
                        mesh = renderer.sharedMesh != null ? renderer.sharedMesh.name : null,
                        bindposeCount = renderer.sharedMesh != null ? renderer.sharedMesh.bindposes.Length : 0,
                        boneCount = bones.Length,
                        nullBoneCount = nullBones,
                        rootBone = renderer.rootBone != null ? renderer.rootBone.name : null,
                        firstBones = bones.Where(bone => bone != null).Take(8).Select(bone => bone.name).ToArray(),
                    });
                }

                results.Add(new
                {
                    path = modelPath,
                    animationType = importer != null ? importer.animationType.ToString() : null,
                    avatarSetup = importer != null ? importer.avatarSetup.ToString() : null,
                    avatarIsHuman = avatar != null && avatar.isHuman,
                    avatarIsValid = avatar != null && avatar.isValid,
                    rendererCount = renderers.Length,
                    renderers = rendererResults,
                });
            }

            return new SuccessResponse("Enemy 모델 폴더의 SkinnedMeshRenderer 본 연결을 읽었습니다.", results);
        }

        /// <summary>플레이 중 Enemy 프리팹 인스턴스가 실제로 물고 있는 메시와 본 배열을 조사합니다.</summary>
        /// <remarks>래그돌 상태가 아닌 생존 개체만 보며, Transform이나 렌더러 상태를 변경하지 않습니다.</remarks>
        private static object DumpRuntimeEnemyRig(string nameFilter)
        {
            if (!Application.isPlaying)
            {
                return new ErrorResponse("runtimeenemyrig는 실제 프리팹 인스턴스를 보기 위해 Play Mode에서 실행해야 합니다.");
            }

            bool useFilter = !string.IsNullOrWhiteSpace(nameFilter);
            var results = new List<object>();
            foreach (EnemyController controller in Object.FindObjectsByType<EnemyController>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (useFilter && !controller.name.Contains(nameFilter))
                {
                    continue;
                }

                RagdollController ragdoll = controller.GetComponent<RagdollController>();
                if (ragdoll != null && ragdoll.IsRagdollActive)
                {
                    continue;
                }

                Animator animator = controller.Animator;
                SkinnedMeshRenderer[] renderers = controller.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var rendererResults = new List<object>();
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    SkinnedMeshRenderer renderer = renderers[rendererIndex];
                    Transform[] bones = renderer.bones;
                    float maxParentDistance = 0.0f;
                    float maxLossyScale = 0.0f;
                    int outsideControllerHierarchy = 0;

                    for (int boneIndex = 0; boneIndex < bones.Length; boneIndex++)
                    {
                        Transform bone = bones[boneIndex];
                        if (bone == null)
                        {
                            continue;
                        }

                        if (!bone.IsChildOf(controller.transform))
                        {
                            outsideControllerHierarchy++;
                        }

                        if (bone.parent != null)
                        {
                            maxParentDistance = Mathf.Max(maxParentDistance, Vector3.Distance(bone.position, bone.parent.position));
                        }

                        maxLossyScale = Mathf.Max(
                            maxLossyScale,
                            Mathf.Max(bone.lossyScale.x, Mathf.Max(bone.lossyScale.y, bone.lossyScale.z)));
                    }

                    rendererResults.Add(new
                    {
                        path = GetRelativeTransformPath(renderer.transform, controller.transform),
                        mesh = renderer.sharedMesh != null ? renderer.sharedMesh.name : null,
                        bindposeCount = renderer.sharedMesh != null ? renderer.sharedMesh.bindposes.Length : 0,
                        boneCount = bones.Length,
                        nullBoneCount = bones.Count(bone => bone == null),
                        outsideControllerHierarchy,
                        rootBone = renderer.rootBone != null
                            ? GetRelativeTransformPath(renderer.rootBone, controller.transform)
                            : null,
                        maxParentDistance,
                        maxLossyScale,
                        firstBones = bones.Where(bone => bone != null)
                            .Take(8)
                            .Select(bone => GetRelativeTransformPath(bone, controller.transform))
                            .ToArray(),
                    });
                }

                results.Add(new
                {
                    go = controller.name,
                    animatorAvatar = animator != null && animator.avatar != null ? animator.avatar.name : null,
                    animatorAvatarIsHuman = animator != null && animator.avatar != null && animator.avatar.isHuman,
                    hips = animator != null && animator.isHuman && animator.GetBoneTransform(HumanBodyBones.Hips) != null
                        ? GetRelativeTransformPath(animator.GetBoneTransform(HumanBodyBones.Hips), controller.transform)
                        : null,
                    renderers = rendererResults,
                });
            }

            return new SuccessResponse($"runtimeenemyrig: {results.Count} enemy(s).", results);
        }

        /// <summary>플레이 중 Enemy 하나의 LOD를 고정하거나 자동 선택으로 되돌립니다.</summary>
        private static object ForceEnemyLod(string nameFilter, int lodIndex)
        {
            if (!Application.isPlaying)
            {
                return new ErrorResponse("forcelod는 실제 렌더링을 확인하기 위해 Play Mode에서 실행해야 합니다.");
            }

            foreach (EnemyController controller in Object.FindObjectsByType<EnemyController>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!string.IsNullOrWhiteSpace(nameFilter) && !controller.name.Contains(nameFilter))
                {
                    continue;
                }

                LODGroup group = controller.GetComponentInChildren<LODGroup>(true);
                if (group == null)
                {
                    continue;
                }

                if (lodIndex < -1 || lodIndex >= group.lodCount)
                {
                    return new ErrorResponse($"LOD 번호가 범위를 벗어났습니다: {lodIndex} (허용: -1~{group.lodCount - 1})");
                }

                group.ForceLOD(lodIndex);
                LOD[] lods = group.GetLODs();
                string[] rendererNames = lodIndex >= 0
                    ? lods[lodIndex].renderers.Where(renderer => renderer != null).Select(renderer => renderer.name).ToArray()
                    : new string[0];

                return new SuccessResponse(
                    lodIndex < 0
                        ? $"'{controller.name}'의 LOD를 자동 선택으로 되돌렸습니다."
                        : $"'{controller.name}'의 LOD를 {lodIndex}로 고정했습니다.",
                    new
                    {
                        enemy = controller.name,
                        forcedLod = lodIndex,
                        lodCount = group.lodCount,
                        renderers = rendererNames,
                    });
            }

            return new ErrorResponse("조건에 맞는 활성 Enemy와 LODGroup을 찾지 못했습니다.");
        }

        /// <summary>에셋 루트를 기준으로 Transform 경로를 만듭니다.</summary>
        private static string GetRelativeTransformPath(Transform target, Transform root)
        {
            var segments = new List<string>();
            for (Transform current = target; current != null; current = current.parent)
            {
                segments.Add(current.name);
                if (current == root)
                {
                    break;
                }
            }

            segments.Reverse();
            return string.Join("/", segments);
        }


        // ── combat ───────────────────────────────────────────────────────────
        private static object DumpCombat()
        {
            var members = new List<object>();
            foreach (var m in Object.FindObjectsByType<SquadMemberController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var tpc = m.GetComponent<ThirdPersonController>();
                var aim = m.GetComponent<AimController>();
                var wc = m.GetComponentInChildren<Gun>();
                var inp = m.GetComponent<PlayerInputController>();
                var ai = m.GetComponent<SquadAIController>();
                var tg = ai != null ? ai.Targeting : null;

                members.Add(new
                {
                    name = m.MemberName,
                    go = m.name,
                    playerSquadMember = m.IsPlayerSquadMember,
                    alive = m.IsAlive,
                    down = m.IsDown,
                    tpc = tpc == null ? null : new
                    {
                        enabled = tpc.enabled,
                        isReload = tpc.IsReload,
                        isCombatStance = tpc.IsCombatStance,
                        viewContext = tpc.CurrentViewContext.ToString(),
                        viewMode = tpc.ViewMode.ToString(),
                    },
                    weapon = wc == null ? null : new
                    {
                        enabled = wc.enabled,
                        isReloading = wc.IsReloading,
                        canShoot = wc.CanShoot,
                        canReload = wc.CanReload,
                        allowFullMagReload = wc.AllowFullMagReload,
                        bullet = wc.CurrentBullet,
                        maxBullet = wc.MaxBullet,
                    },
                    aim = aim == null ? null : new
                    {
                        enabled = aim.enabled,
                        inCombatStance = GetPrivate<bool>(aim, "m_inCombatStance"),
                        isAds = GetPrivate<bool>(aim, "m_isAds"),
                        hipfireTimer = GetPrivate<float>(aim, "m_hipfireTimer"),
                    },
                    input = inp == null ? null : new
                    {
                        enabled = inp.enabled,
                        aim = inp.Aim,
                        shoot = inp.Shoot,
                        reload = inp.Reload,
                    },
                    // AI 개인 대상 판단(§9). 유예 중이면 겨누되 쏘지 않는 상태입니다.
                    targeting = tg == null ? null : new
                    {
                        aiEnabled = ai.enabled,
                        target = tg.CurrentTarget != null ? tg.CurrentTarget.name : null,
                        holdingLost = tg.IsHoldingLostTarget,
                        graceLeft = tg.GraceRemaining.ToString("F1"),
                        canFire = tg.CanFireAtCurrentTarget,
                        aiming = ai.TryGetCurrentAimPoint(out Vector3 aimPt),
                        aimPoint = aimPt.ToString("F1"),
                        aimAngleError = ai.AimAngleError.ToString("F1"),
                        aimAligned = ai.IsAimAligned,
                        bursting = ai.IsBursting,
                        allowFiring = ai.AllowFiring,
                    },
                    // AI 행동 판정(§18.1). step은 이 결정을 확정한 문서 우선순위 번호입니다.
                    // 컴포넌트가 꺼져 있으면 Update가 돌지 않아 판정도 없습니다. 그때 마지막 값을 그대로
                    // 내보내면 조작 멤버가 판정을 내린 것처럼 읽히므로 null로 비웁니다.
                    decision = (ai == null || !ai.enabled) ? null : new
                    {
                        kind = ai.CurrentDecision.Kind.ToString(),
                        step = ai.CurrentDecision.Step.ToString(),
                        aim = ai.CurrentDecision.Aim,
                        fire = ai.CurrentDecision.Fire,
                        sprint = ai.CurrentDecision.Sprint,
                        reload = ai.CurrentDecision.Reload.ToString(),
                        holdPosition = ai.CurrentDecision.HoldPosition,
                        joining = ai.IsJoining,
                        repositioning = ai.IsRepositioning,
                        // 자동 구조(§16). 대상과 기립 홀드 진행도입니다.
                        rescueTarget = ai.RescueTarget != null ? ai.RescueTarget.name : null,
                        rescueHold = ai.RescueHoldProgress01.ToString("F2"),
                    },
                });
            }

            // 스쿼드 공유 전투 상태(§4.2). 교전 중인 변이체가 하나라도 있으면 전투입니다.
            var sm = SquadManager.Instance;
            var engagement = sm != null ? sm.Engagement : null;
            // 스쿼드 공용 적 정보(§8). 교전 적별로 지금 실시간 위치를 아는지와 마지막 확인 위치를 봅니다.
            var intel = sm != null ? sm.EnemyIntel : null;
            var intelRows = new List<object>();
            if (intel != null)
            {
                foreach (var e in intel.All)
                {
                    intelRows.Add(new
                    {
                        enemy = e.Enemy != null ? e.Enemy.name : "(destroyed)",
                        live = e.HasLivePosition,
                        knownPos = e.KnownPosition.ToString("F1"),
                        lastKnownAge = e.LastKnownTime > 0f ? (Time.time - e.LastKnownTime).ToString("F1") : "-",
                        attackerHold = Mathf.Max(0f, e.AttackerHoldExpireTime - Time.time).ToString("F1"),
                    });
                }
            }

            var squad = new
            {
                inCombat = engagement != null ? (bool?)engagement.IsInCombat : null,
                engagedEnemyCount = engagement != null ? (int?)engagement.EngagedEnemyCount : null,
                playerSquadMemberIndex = sm != null ? (int?)sm.PlayerSquadMemberIndex : null,
                intelTracked = intel != null ? (int?)intel.TrackedCount : null,
                intelLive = intel != null ? (int?)intel.LiveCount : null,
                intel = intelRows,
            };

            return new SuccessResponse(
                $"combat: {members.Count} member(s), inCombat={squad.inCombat}, engaged={squad.engagedEnemyCount}.",
                new { squad = squad, members = members });
        }

        // ── sceneui ──────────────────────────────────────────────────────────
        private static object DumpSceneUI()
        {
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(c => c.gameObject.activeInHierarchy)
                .Select(c => (object)new { name = c.name, renderMode = c.renderMode.ToString(), path = Path(c.transform) })
                .ToList();

            var documents = Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(d => d.gameObject.activeInHierarchy)
                .Select(d => (object)new { name = d.name, panel = d.panelSettings != null ? d.panelSettings.name : null, path = Path(d.transform) })
                .ToList();

            // Image: 특히 type=Filled + fillAmount 이 "부분 링/호" 의 정체일 가능성이 큽니다.
            var images = Object.FindObjectsByType<UIImage>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(i => i.gameObject.activeInHierarchy)
                .Select(i => (object)new
                {
                    name = i.name,
                    sprite = i.sprite != null ? i.sprite.name : null,
                    type = i.type.ToString(),
                    fillMethod = i.type == UIImage.Type.Filled ? i.fillMethod.ToString() : null,
                    fillAmount = i.type == UIImage.Type.Filled ? (float?)i.fillAmount : null,
                    color = ColorHex(i.color),
                    path = Path(i.transform),
                })
                .ToList();

            var sprites = Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(s => s.gameObject.activeInHierarchy)
                .Select(s => (object)new { name = s.name, sprite = s.sprite != null ? s.sprite.name : null, path = Path(s.transform) })
                .ToList();

            return new SuccessResponse(
                $"sceneui: canvases={canvases.Count} docs={documents.Count} images={images.Count} sprites={sprites.Count}.",
                new { canvases, documents, images, sprites });
        }

        // ── go ───────────────────────────────────────────────────────────────
        private static object DumpGameObject(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return new ErrorResponse("go 조회에는 name 파라미터가 필요합니다.");

            var matches = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(t => t.name.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(20)
                .Select(t => (object)new
                {
                    name = t.name,
                    path = Path(t),
                    activeSelf = t.gameObject.activeSelf,
                    activeInHierarchy = t.gameObject.activeInHierarchy,
                    position = new { t.position.x, t.position.y, t.position.z },
                    components = t.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name).ToArray(),
                })
                .ToList();

            return new SuccessResponse($"go '{name}': {matches.Count} match(es).", matches);
        }

        // ── revive ───────────────────────────────────────────────────────────
        private static object DumpRevive()
        {
            var sm = Object.FindFirstObjectByType<SquadManager>();
            var cam = Camera.main;

            SquadMemberController playerSquadMember = null;
            foreach (var m in Object.FindObjectsByType<SquadMemberController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (m.IsPlayerSquadMember) { playerSquadMember = m; break; }
            }

            var ic = playerSquadMember != null ? playerSquadMember.GetComponent<InteractionController>() : null;
            var pud = playerSquadMember != null ? playerSquadMember.GetComponent<PlayerbleUnitData>() : null;
            Vector3 origin = playerSquadMember != null ? playerSquadMember.transform.position : Vector3.zero;
            Vector3 facing = cam != null ? cam.transform.forward : Vector3.forward;

            var targets = new List<object>();
            foreach (var dai in Object.FindObjectsByType<DownedAllyInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var member = dai.TargetMember;
                var health = dai.TargetHealth;
                Vector3 to = dai.transform.position - origin;
                float dist = to.magnitude;
                float angle = to.sqrMagnitude > 0.0001f ? Vector3.Angle(facing, to) : -1f;
                var col = dai.GetComponentInChildren<Collider>();

                targets.Add(new
                {
                    go = dai.name,
                    canInteract = playerSquadMember != null && dai.CanInteract(playerSquadMember.gameObject),
                    holdDuration = dai.HoldDuration,
                    holdActive = dai.IsReviveHoldActive,
                    holdProgress = dai.ReviveHoldProgress01,
                    isSelf = playerSquadMember != null && member == playerSquadMember,
                    memberIsAlive = member != null && member.IsAlive,
                    memberIsDown = member != null && member.IsDown,
                    healthIsDowned = health != null && health.IsDowned,
                    healthCurrentHp = health != null ? health.CurrentHP : -1,
                    healthIsDead = health != null && health.IsDead,
                    distance = dist,
                    angleFromCamera = angle,
                    colliderEnabled = col != null && col.enabled,
                    colliderType = col != null ? col.GetType().Name : "none",
                });
            }

            return new SuccessResponse("revive diagnostic", new
            {
                playerSquadMember = playerSquadMember != null ? playerSquadMember.name : "none",
                interactPressed = playerSquadMember != null && playerSquadMember.GetComponent<PlayerInputController>() != null && playerSquadMember.GetComponent<PlayerInputController>().Interact,
                holdProgress01 = ic != null ? ic.HoldProgress01 : -1f,
                interactionRadius = ic != null ? GetPrivate<float>(ic, "m_radius") : -1f,
                interactionMaxAngle = ic != null ? GetPrivate<float>(ic, "m_maxAngle") : -1f,
                currentTarget = ic != null && ic.Current is Component cc ? cc.name : "null",
                hasReviveTarget = pud != null && pud.HasReviveInteractionTarget,
                playerSquadMemberData = sm != null && sm.PlayerSquadMemberData != null ? sm.PlayerSquadMemberData.name : "none",
                cameraForward = cam != null ? facing.ToString("F2") : "noCam",
                targetCount = targets.Count,
                targets,
            });
        }

        // ── anim ─────────────────────────────────────────────────────────────
        /// <summary>
        /// Animator의 레이어별 현재/다음 스테이트와 전이 진행도, 파라미터 값을 덤프합니다.
        /// </summary>
        /// <param name="nameFilter">비면 스쿼드 멤버가 붙은 오브젝트만, 값이 있으면 이름 부분 일치로 고릅니다.</param>
        /// <remarks>
        /// 런타임 <c>AnimatorStateInfo</c>는 스테이트 해시만 주므로 이름을 알 수 없습니다.
        /// 에디터 전용 코드이므로 <see cref="AnimatorController"/>에서 해시→이름 표를 만들어 이름까지 붙입니다.
        /// <c>isInTransition</c>이 계속 true이고 다음 스테이트가 현재와 같으면 조건 없는 자기 전이가
        /// 매 프레임 재시작되고 있다는 신호입니다.
        /// </remarks>
        private static object DumpAnimator(string nameFilter)
        {
            bool useFilter = !string.IsNullOrWhiteSpace(nameFilter);

            var animators = Object.FindObjectsByType<Animator>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(a => useFilter
                    ? a.name.IndexOf(nameFilter, System.StringComparison.OrdinalIgnoreCase) >= 0
                    : a.GetComponentInParent<SquadMemberController>() != null)
                .Take(8)
                .ToList();

            var dumps = new List<object>();
            foreach (var animator in animators)
            {
                var nameByHash = BuildStateNameMap(animator);

                var layers = new List<object>();
                for (int i = 0; i < animator.layerCount; i++)
                {
                    AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(i);
                    bool inTransition = animator.IsInTransition(i);
                    AnimatorStateInfo next = inTransition ? animator.GetNextAnimatorStateInfo(i) : default;

                    layers.Add(new
                    {
                        layer = animator.GetLayerName(i),
                        weight = animator.GetLayerWeight(i),
                        currentState = StateLabel(current, nameByHash),
                        currentNormalizedTime = current.normalizedTime,
                        currentLength = current.length,
                        isInTransition = inTransition,
                        nextState = inTransition ? StateLabel(next, nameByHash) : null,
                        nextNormalizedTime = inTransition ? (float?)next.normalizedTime : null,
                        transitionProgress = inTransition
                            ? (float?)animator.GetAnimatorTransitionInfo(i).normalizedTime
                            : null,
                        clips = animator.GetCurrentAnimatorClipInfo(i)
                            .Select(c => (object)new { clip = c.clip != null ? c.clip.name : null, weight = c.weight })
                            .ToArray(),
                    });
                }

                var parameters = new List<object>();
                foreach (var parameter in animator.parameters)
                {
                    object value = parameter.type switch
                    {
                        AnimatorControllerParameterType.Bool => animator.GetBool(parameter.nameHash),
                        AnimatorControllerParameterType.Trigger => animator.GetBool(parameter.nameHash),
                        AnimatorControllerParameterType.Int => animator.GetInteger(parameter.nameHash),
                        AnimatorControllerParameterType.Float => animator.GetFloat(parameter.nameHash),
                        _ => null,
                    };

                    parameters.Add(new { name = parameter.name, type = parameter.type.ToString(), value });
                }

                dumps.Add(new
                {
                    go = animator.name,
                    path = Path(animator.transform),
                    enabled = animator.enabled,
                    controller = animator.runtimeAnimatorController != null
                        ? animator.runtimeAnimatorController.name
                        : null,
                    applyRootMotion = animator.applyRootMotion,
                    layers,
                    parameters,
                });
            }

            return new SuccessResponse(
                $"anim: {dumps.Count} animator(s){(useFilter ? $" matching '{nameFilter}'" : " on squad members")}.",
                dumps);
        }

        // ── ragdoll ──────────────────────────────────────────────────────────
        /// <summary>
        /// 변이체의 래그돌 전환 상태를 덤프합니다. 즉시 래그돌 사망 연출 검증용입니다.
        /// </summary>
        /// <param name="nameFilter">대상 GameObject 이름 필터입니다. 비우면 전부 봅니다.</param>
        /// <remarks>
        /// 확인 대상은 세 가지입니다. Animator가 꺼졌는지(래그돌이 주도권을 가졌는지),
        /// 뼈가 동역학으로 전환됐는지(<c>isKinematic</c>), 그리고 속도 인계가 실제로 들어갔는지입니다.
        /// 인계가 실패하면 전환 직후 속도가 모두 0으로 찍힙니다.
        /// </remarks>
        private static object DumpRagdoll(string nameFilter)
        {
            bool useFilter = !string.IsNullOrWhiteSpace(nameFilter);

            var dumps = new List<object>();
            foreach (var controller in Object.FindObjectsByType<EnemyController>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (useFilter && !controller.name.Contains(nameFilter))
                {
                    continue;
                }

                var ragdoll = controller.GetComponent<RagdollController>();
                var animator = controller.Animator;
                var bodies = controller.GetComponentsInChildren<Rigidbody>(true);
                var settings = CombatSceneManager.Instance != null ? CombatSceneManager.Instance.EnemyManager : null;
                var corpseSettings = settings != null ? settings.Resolve(controller.EnemyType) : null;
                bool visible = controller.GetComponentsInChildren<Renderer>(true).Any(r => r.isVisible);

                int kinematic = 0;
                float maxSpeed = 0.0f;
                float maxAngular = 0.0f;
                foreach (var body in bodies)
                {
                    if (body.isKinematic)
                    {
                        kinematic++;
                        continue;
                    }

                    maxSpeed = Mathf.Max(maxSpeed, body.linearVelocity.magnitude);
                    maxAngular = Mathf.Max(maxAngular, body.angularVelocity.magnitude);
                }

                dumps.Add(new
                {
                    go = controller.name,
                    enemyType = controller.EnemyType.ToString(),
                    hp = controller.CurrentHP,
                    state = controller.Current != null ? controller.Current.GetType().Name : null,
                    ragdollConfigured = ragdoll != null && ragdoll.IsConfigured,
                    ragdollActive = ragdoll != null && ragdoll.IsRagdollActive,
                    animatorEnabled = animator != null && animator.enabled,
                    animatorCulling = animator != null ? animator.cullingMode.ToString() : null,
                    rendererVisible = visible,
                    boneCount = bodies.Length,
                    kinematicBones = kinematic,
                    maxBoneSpeed = maxSpeed,
                    maxBoneAngularSpeed = maxAngular,
                    measuredMaxBoneSpeed = ragdoll != null ? ragdoll.MeasuredMaxBoneSpeed : 0.0f,
                    measuredMaxBoneAngularSpeed = ragdoll != null ? ragdoll.MeasuredMaxBoneAngularSpeed : 0.0f,
                    destroyCorpse = corpseSettings != null && corpseSettings.DestroyCorpse,
                    corpseLifetime = corpseSettings != null ? corpseSettings.CorpseLifetime : -1.0f,
                    useRagdoll = corpseSettings != null && corpseSettings.UseRagdoll,
                    rootY = controller.transform.position.y,
                });
            }

            return new SuccessResponse($"ragdoll: {dumps.Count} enemy(s).", dumps);
        }

        // ── noise ────────────────────────────────────────────────────────────
        /// <summary>
        /// 변이체별 소음 감지 상태와 차폐 판정 결과를 덤프합니다.
        /// </summary>
        /// <param name="nameFilter">대상 GameObject 이름 필터입니다. 비우면 전부 덤프합니다.</param>
        /// <remarks>
        /// 차폐가 실제로 걸렸는지는 콘솔로 알 수 없습니다. 소음이 조용히 약해지는 것이 전부라
        /// 에러도 경고도 나지 않습니다. 그래서 통과 비율과 인지 게이지를 함께 봐야 판별됩니다.
        ///
        /// <c>transmission</c>은 "직전에 판정한 소음"의 값입니다. 지금 추적 중인 소음의 것이 아닙니다.
        /// 여러 소음이 섞이는 상황에서는 마지막 하나만 보이므로, 총성 한 발씩 끊어 확인하는 편이 낫습니다.
        /// </remarks>
        private static object DumpNoise(string nameFilter)
        {
            bool useFilter = !string.IsNullOrWhiteSpace(nameFilter);

            var noiseManager = CombatSceneManager.Instance != null ? CombatSceneManager.Instance.NoiseManager : null;

            var dumps = new List<object>();
            foreach (var controller in Object.FindObjectsByType<EnemyController>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (useFilter && !controller.name.Contains(nameFilter))
                {
                    continue;
                }

                var sensor = controller.Sensor;
                if (sensor == null)
                {
                    continue;
                }

                bool hasNoise = sensor.TryGetNoisePosition(out Vector3 noisePosition);

                dumps.Add(new
                {
                    go = controller.name,
                    state = controller.Current != null ? controller.Current.GetType().Name : null,
                    hasNoise,
                    noisePos = hasNoise ? $"{noisePosition.x:F2},{noisePosition.y:F2},{noisePosition.z:F2}" : null,
                    transmission = sensor.LastNoiseTransmission,
                    awareness = sensor.NoiseAwareness,
                    awareness01 = sensor.NoiseAwareness01,
                    awarenessFull = sensor.IsNoiseAwarenessFull,
                    alert = sensor.IsNoiseAlert,
                });
            }

            var table = noiseManager != null ? noiseManager.OcclusionTable : null;

            return new SuccessResponse(
                $"noise: {dumps.Count} enemy(s), noiseManager={(noiseManager != null ? "present" : "MISSING")}, " +
                $"table={(table != null ? table.name : "MISSING")}, " +
                $"defaultOcclusion={(table != null ? table.DefaultOcclusion.ToString("F2") : "-")}, " +
                $"occluderMask={(noiseManager != null ? noiseManager.OccluderMask.ToString() : "-")}, " +
                $"tags={Object.FindObjectsByType<SurfaceMaterialTag>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length}, " +
                $"listeners={NoiseSystem.ListenerCount}.",
                dumps);
        }

        // ── killenemy ────────────────────────────────────────────────────────
        /// <summary>
        /// 변이체 하나를 즉사시켜 사망 연출을 검증할 수 있게 합니다.
        /// </summary>
        /// <param name="nameFilter">대상 GameObject 이름 필터입니다. 비우면 첫 생존 개체를 씁니다.</param>
        /// <remarks>
        /// 정상 피해 경로(<c>TakeDamage</c>)를 그대로 타므로 사망 이벤트 체인이 실제와 같습니다.
        /// Play Mode에서 <c>exec</c>이 컴파일 지연으로 멈추기 때문에 검증용 진입점을 여기에 둡니다.
        /// </remarks>
        private static object KillEnemy(string nameFilter, bool holdCorpse)
        {
            bool useFilter = !string.IsNullOrWhiteSpace(nameFilter);

            foreach (var controller in Object.FindObjectsByType<EnemyController>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (useFilter && !controller.name.Contains(nameFilter))
                {
                    continue;
                }

                var health = controller.Health;
                if (health == null || controller.CurrentHP <= 0)
                {
                    continue;
                }

                if (holdCorpse)
                {
                    controller.OnCorpseLifetimeElapsed += KeepCorpseForDebug;

                    Renderer focusRenderer = controller.GetComponentsInChildren<Renderer>(true)
                        .FirstOrDefault(renderer => renderer.bounds.size.sqrMagnitude > 0.0f);
                    if (focusRenderer != null && UnityEditor.SceneView.lastActiveSceneView != null)
                    {
                        UnityEditor.SceneView.lastActiveSceneView.LookAt(
                            focusRenderer.bounds.center,
                            Quaternion.Euler(10.0f, 180.0f, 0.0f),
                            3.0f,
                            false);
                    }
                }

                health.TakeDamage(DamageInfo.Debug(int.MaxValue, "GZDebugDump"));
                var ragdoll = controller.GetComponent<RagdollController>();
                var animator = controller.Animator;
                var bodies = controller.GetComponentsInChildren<Rigidbody>(true);
                var joints = controller.GetComponentsInChildren<CharacterJoint>(true);
                int kinematicBodies = 0;
                float maximumLinearSpeed = 0.0f;
                float maximumAngularSpeed = 0.0f;
                float maximumAngularVelocityLimit = 0.0f;
                int preprocessingEnabled = 0;
                int projectionEnabled = 0;

                foreach (var body in bodies)
                {
                    if (body.isKinematic)
                    {
                        kinematicBodies++;
                        continue;
                    }

                    maximumLinearSpeed = Mathf.Max(maximumLinearSpeed, body.linearVelocity.magnitude);
                    maximumAngularSpeed = Mathf.Max(maximumAngularSpeed, body.angularVelocity.magnitude);
                    maximumAngularVelocityLimit = Mathf.Max(maximumAngularVelocityLimit, body.maxAngularVelocity);
                }

                foreach (var joint in joints)
                {
                    if (joint.enablePreprocessing)
                    {
                        preprocessingEnabled++;
                    }

                    if (joint.enableProjection)
                    {
                        projectionEnabled++;
                    }
                }

                return new SuccessResponse(
                    $"killenemy: '{controller.name}' 처치 요청 완료.",
                    new
                    {
                        go = controller.name,
                        hp = controller.CurrentHP,
                        state = controller.Current != null ? controller.Current.GetType().Name : null,
                        ragdollActive = ragdoll != null && ragdoll.IsRagdollActive,
                        animatorEnabled = animator != null && animator.enabled,
                        boneCount = bodies.Length,
                        kinematicBones = kinematicBodies,
                        maxBoneSpeed = maximumLinearSpeed,
                        maxBoneAngularSpeed = maximumAngularSpeed,
                        maxAngularVelocityLimit = maximumAngularVelocityLimit,
                        characterJointCount = joints.Length,
                        preprocessingEnabled = preprocessingEnabled,
                        projectionEnabled = projectionEnabled,
                        holdCorpse,
                        timeScale = Time.timeScale,
                    });
            }

            return new ErrorResponse("처치할 생존 변이체를 찾지 못했습니다.");
        }

        /// <summary>래그돌을 오래 관찰하는 디버그 요청에서 풀 반환 처리를 소비합니다.</summary>
        private static bool KeepCorpseForDebug()
        {
            return true;
        }

        /// <summary>스테이트 해시를 사람이 읽을 이름으로 바꿉니다. 표에 없으면 해시를 그대로 씁니다.</summary>
        private static string StateLabel(AnimatorStateInfo info, Dictionary<int, string> nameByHash)
        {
            return nameByHash.TryGetValue(info.shortNameHash, out string name)
                ? name
                : $"#{info.shortNameHash}";
        }

        /// <summary>
        /// AnimatorController를 훑어 스테이트 해시→이름 표를 만듭니다.
        /// </summary>
        /// <remarks>하위 스테이트 머신도 재귀로 포함합니다. Override 컨트롤러면 원본 컨트롤러를 따라갑니다.</remarks>
        private static Dictionary<int, string> BuildStateNameMap(Animator animator)
        {
            var map = new Dictionary<int, string>();

            RuntimeAnimatorController runtime = animator.runtimeAnimatorController;
            if (runtime is AnimatorOverrideController overrideController)
            {
                runtime = overrideController.runtimeAnimatorController;
            }

            if (runtime is not AnimatorController controller)
            {
                return map;
            }

            foreach (var layer in controller.layers)
            {
                CollectStateNames(layer.stateMachine, map);
            }

            return map;
        }

        private static void CollectStateNames(AnimatorStateMachine machine, Dictionary<int, string> map)
        {
            if (machine == null)
            {
                return;
            }

            foreach (var child in machine.states)
            {
                if (child.state != null)
                {
                    map[child.state.nameHash] = child.state.name;
                }
            }

            foreach (var child in machine.stateMachines)
            {
                CollectStateNames(child.stateMachine, map);
            }
        }

        // ── squad ────────────────────────────────────────────────────────────
        /// <summary>
        /// 스쿼드 전체에 걸리는 상태와 AI 팀원 간격을 한 번에 잽니다.
        /// </summary>
        /// <remarks>
        /// <b>combat 덤프와 겹치지 않게 나눈 축</b>: combat은 멤버 <i>개인</i>의 무기·조준·판정을 봅니다.
        /// 여기서 보는 것은 <see cref="SquadManager"/>가 소유한 <i>스쿼드 단위</i> 상태(간격·사격 허용·
        /// 목적지 예약대장)와 그것을 멤버 전원에게 순회 적용했는지입니다. 결함이 났던 지점이 항상
        /// "조작 멤버 한 명에게만 걸렸다"였으므로 전원 값을 나란히 놓고 봐야 드러납니다.
        ///
        /// <para>
        /// <b>목적지 거리를 재는 이유</b>: 현재 위치만 보면 두 AI가 아직 멀리서 오는 중일 때 겹침이
        /// 드러나지 않습니다. 도착한 뒤에야 붙어 있는 것이 보입니다(실측 0.31m, 캡슐 반지름 각 0.30m).
        /// 그래서 <c>destDist</c>가 판정의 주 지표이고 <c>posDist</c>는 참고입니다.
        /// </para>
        ///
        /// <para>
        /// 예약대장은 <see cref="SquadManager"/>의 private 필드입니다. 검증 목적으로 게임플레이 쪽에
        /// 공개 접근자를 새로 뚫는 것은 직렬화·API 경계를 건드리므로, Editor 전용인 이 파일에서
        /// 리플렉션으로만 읽습니다.
        /// </para>
        /// </remarks>
        private static object DumpSquad()
        {
            var manager = Object.FindFirstObjectByType<SquadManager>();
            if (manager == null)
            {
                return new ErrorResponse("씬에 SquadManager가 없습니다.");
            }

            float spacing = manager.AiMemberSpacing;

            var claims = GetPrivate<Dictionary<SquadMemberController, Vector3>>(manager, "m_destinationClaims");

            // 쌍별 비교를 위해 AI 멤버만 따로 모읍니다. 조작 멤버는 AI 목적지를 잡지 않으므로
            // 간격 판정 대상이 아닙니다(IsDestinationClaimedByOther도 같은 기준으로 제외합니다).
            var aiMembers = new List<SquadMemberController>();
            var dumps = new List<object>();

            foreach (var member in manager.SquadMembers)
            {
                if (member == null)
                {
                    continue;
                }

                var input = member.GetComponent<PlayerInputController>();
                var tpc = member.GetComponent<ThirdPersonController>();
                var ai = member.GetComponent<SquadAIController>();

                // out 변수를 미리 초기화합니다. && 단축 평가 때문에 컴파일러가 대입을 증명하지 못합니다.
                Vector3 destination = Vector3.zero;
                bool hasClaim = claims != null && claims.TryGetValue(member, out destination);

                if (!member.IsPlayerSquadMember)
                {
                    aiMembers.Add(member);
                }

                dumps.Add(new
                {
                    go = member.name,
                    playerSquadMember = member.IsPlayerSquadMember,
                    alive = member.IsAlive,
                    down = member.IsDown,
                    pos = member.transform.position.ToString("F2"),
                    hasClaim,
                    dest = hasClaim ? destination.ToString("F2") : null,
                    // 제자리를 지키기로 했으면 예약을 놓아야 정상입니다. holdPosition이 true인데
                    // hasClaim도 true면 서 있지도 않은 자리를 계속 막고 있는 상태입니다.
                    holdPosition = ai != null && ai.enabled ? (bool?)ai.CurrentDecision.HoldPosition : null,
                    kind = ai != null && ai.enabled ? ai.CurrentDecision.Kind.ToString() : null,
                    step = ai != null && ai.enabled ? ai.CurrentDecision.Step.ToString() : null,
                    // §10.2 전투 위치 조정입니다. §17 재배치와 다른 축이므로 이름을 섞지 마십시오.
                    combatRepositioning = ai != null && ai.enabled ? (bool?)ai.IsRepositioning : null,
                    allowFiring = ai != null ? (bool?)ai.AllowFiring : null,
                    // §17 합류 실패와 재배치. joinFailed가 계속 켜져 있으면 재배치할 유효 위치를
                    // 못 찾고 있다는 뜻입니다(강제 재배치는 문서가 금지합니다).
                    joining = ai != null && ai.enabled ? (bool?)ai.IsJoining : null,
                    joinFailed = ai != null ? (bool?)ai.IsJoinFailed : null,
                    joinRepath = ai != null ? (int?)ai.JoinRepathCount : null,
                    joinStall = ai != null ? ai.JoinStallTimer.ToString("F2") : null,
                    // 입력 게이트와 카메라 잠금은 컴포넌트가 꺼져도 값이 남습니다. 그래서
                    // 전원 순회 적용(ApplyInputModeToSquad)이 실제로 걸렸는지는 이 두 값으로만 확인됩니다.
                    inputGate = input != null ? (bool?)GetPrivate<bool>(input, "m_isInputEnabled") : null,
                    lockCamera = tpc != null ? (bool?)tpc.LockCameraPosition : null,
                });
            }

            var pairs = new List<object>();
            int spacingViolations = 0;

            for (int i = 0; i < aiMembers.Count; i++)
            {
                for (int j = i + 1; j < aiMembers.Count; j++)
                {
                    SquadMemberController a = aiMembers[i];
                    SquadMemberController b = aiMembers[j];

                    float posDist = FlatDistance(a.transform.position, b.transform.position);

                    Vector3 da = Vector3.zero;
                    Vector3 db = Vector3.zero;
                    bool bothClaimed = claims != null
                        && claims.TryGetValue(a, out da)
                        && claims.TryGetValue(b, out db);

                    float destDist = bothClaimed ? FlatDistance(da, db) : -1f;

                    // 죽거나 다운된 멤버는 예약 판정에서 제외되므로 위반으로 세지 않습니다.
                    bool bothActive = a.IsAlive && !a.IsDown && b.IsAlive && !b.IsDown;
                    bool violates = bothClaimed && bothActive && destDist < spacing;

                    if (violates)
                    {
                        spacingViolations++;
                    }

                    pairs.Add(new
                    {
                        a = a.name,
                        b = b.name,
                        posDist = posDist.ToString("F2"),
                        destDist = bothClaimed ? destDist.ToString("F2") : null,
                        violates,
                    });
                }
            }

            // 집계는 메시지가 아니라 데이터에 담습니다. CLI가 데이터 페이로드만 출력하므로
            // 메시지에만 두면 spacing이나 위반 건수가 화면에 아예 나오지 않습니다(실측).
            var summary = new
            {
                members = dumps.Count,
                ai = aiMembers.Count,
                spacing = spacing.ToString("F2"),
                firingAllowed = manager.AiFiringAllowed,
                claims = claims != null ? claims.Count : -1,
                spacingViolations,
            };

            return new SuccessResponse(
                $"squad: ai={aiMembers.Count}, spacing={spacing:F2}, spacingViolations={spacingViolations}.",
                new { summary, members = dumps, pairs });
        }

        // ── strandai ─────────────────────────────────────────────────────────
        /// <summary>
        /// AI 팀원 하나를 NavMesh 밖으로 옮겨 합류 경로를 끊습니다. §17 검증용입니다.
        /// </summary>
        /// <param name="nameFilter">대상 GameObject 이름 필터입니다. 비우면 첫 AI 팀원을 씁니다.</param>
        /// <remarks>
        /// <b>조회가 아니라 상태를 바꾸는 디버그 동작입니다</b>(<c>killenemy</c>와 같은 부류).
        ///
        /// <para>
        /// §17은 "단순히 거리가 멀다는 이유만으로 합류 실패를 확정하지 않는다"고 못박습니다. 그래서 멀리
        /// 보내는 것으로는 실패 경로를 밟을 수 없고, <b>경로가 실제로 없는</b> 상태를 만들어야 합니다.
        /// 공중으로 띄우면 Agent가 NavMesh를 벗어나 경로 계산이 실패하므로 가장 확실합니다.
        /// </para>
        ///
        /// <para>
        /// 지형에 따라 막힌 방 같은 것을 찾는 방법은 씬마다 달라 재현되지 않습니다. 높이는 그런 의존이 없습니다.
        /// </para>
        ///
        /// <para>
        /// Play Mode에서 <c>exec</c>이 멈추고, 에디트 모드에서 씬 오브젝트를 옮기면 사용자의 미저장 작업을
        /// 위험에 빠뜨리므로 검증 진입점을 여기에 둡니다. 이 동작은 씬을 저장하지 않습니다.
        /// </para>
        /// </remarks>
        private static object StrandAiMember(string nameFilter)
        {
            if (!Application.isPlaying)
            {
                return new ErrorResponse(
                    "strandai는 Play Mode에서만 씁니다. 에디트 모드에서는 씬 오브젝트 위치를 바꾸게 되어 미저장 작업이 위험합니다.");
            }

            var manager = Object.FindFirstObjectByType<SquadManager>();
            if (manager == null)
            {
                return new ErrorResponse("씬에 SquadManager가 없습니다.");
            }

            bool useFilter = !string.IsNullOrWhiteSpace(nameFilter);

            foreach (var member in manager.SquadMembers)
            {
                if (member == null || member.IsPlayerSquadMember || !member.IsAlive || member.IsDown)
                {
                    continue;
                }

                if (useFilter && !member.name.Contains(nameFilter))
                {
                    continue;
                }

                Vector3 before = member.transform.position;
                Vector3 stranded = before + Vector3.up * StrandHeight;

                var agent = member.GetComponent<UnityEngine.AI.NavMeshAgent>();

                // Agent를 끈 상태에서 옮겨야 NavMesh 위로 되끌려가지 않습니다. 다시 켜면 off-mesh로 남습니다.
                bool wasEnabled = agent != null && agent.enabled;
                if (wasEnabled)
                {
                    agent.enabled = false;
                }

                member.transform.position = stranded;

                if (wasEnabled)
                {
                    agent.enabled = true;
                }

                return new SuccessResponse(
                    $"strandai: '{member.name}'를 {StrandHeight}m 띄워 경로를 끊었습니다. squad 덤프의 joinStall/joinRepath를 지켜보십시오.",
                    new
                    {
                        go = member.name,
                        before = before.ToString("F2"),
                        after = member.transform.position.ToString("F2"),
                        agentEnabled = agent != null && agent.enabled,
                        isOnNavMesh = agent != null && agent.isOnNavMesh,
                    });
            }

            return new ErrorResponse("경로를 끊을 AI 팀원을 찾지 못했습니다.");
        }

        // ── freezeai ─────────────────────────────────────────────────────────
        /// <summary>
        /// AI 팀원의 Agent를 꺼서 경로는 있는데 이동만 못 하는 상태를 만듭니다. §17 검증용입니다.
        /// </summary>
        /// <param name="nameFilter">대상 GameObject 이름 필터입니다. 비우면 첫 AI 팀원을 씁니다.</param>
        /// <remarks>
        /// <b>조회가 아니라 상태를 바꾸는 디버그 동작입니다.</b>
        ///
        /// <para>
        /// <c>strandai</c>와 <b>다른 분기를 겨냥합니다</b>. §17은 실패 조건을 두 가지로 나눕니다 -
        /// "유효한 이동 경로를 찾지 못하거나" / "경로가 있는데도 일정 시간 실제 이동 진척이 없으면".
        /// <c>strandai</c>는 앞쪽(경로 없음)을 만들고, 이것은 뒤쪽(진척 없음)을 만듭니다.
        /// </para>
        ///
        /// <para>
        /// Agent를 끄는 방식이 통하는 이유: 경로상 거리를 재는 <c>CalculatePathDistance</c>가
        /// Agent가 아니라 정적 <c>NavMesh.CalculatePath</c>를 씁니다. 그래서 Agent를 꺼도 경로는
        /// 계속 유효하게 계산되고, 캐릭터만 제자리에 남습니다. Agent 속도를 0으로 두는 방식은
        /// 매 갱신마다 속도를 다시 쓰는 경로가 있어 덮여 버립니다.
        /// </para>
        /// </remarks>
        private static object FreezeAiMember(string nameFilter)
        {
            if (!Application.isPlaying)
            {
                return new ErrorResponse("freezeai는 Play Mode에서만 씁니다.");
            }

            var manager = Object.FindFirstObjectByType<SquadManager>();
            if (manager == null)
            {
                return new ErrorResponse("씬에 SquadManager가 없습니다.");
            }

            bool useFilter = !string.IsNullOrWhiteSpace(nameFilter);

            foreach (var member in manager.SquadMembers)
            {
                if (member == null || member.IsPlayerSquadMember || !member.IsAlive || member.IsDown)
                {
                    continue;
                }

                if (useFilter && !member.name.Contains(nameFilter))
                {
                    continue;
                }

                var agent = member.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent == null)
                {
                    continue;
                }

                if (!agent.enabled)
                {
                    return new ErrorResponse($"'{member.name}'의 Agent가 이미 꺼져 있습니다.");
                }

                agent.enabled = false;

                return new SuccessResponse(
                    $"freezeai: '{member.name}'의 Agent를 껐습니다. 경로는 유효하고 이동만 멈춥니다. squad 덤프의 joinStall/joinRepath를 지켜보십시오.",
                    new
                    {
                        go = member.name,
                        pos = member.transform.position.ToString("F2"),
                        agentEnabled = agent.enabled,
                    });
            }

            return new ErrorResponse("멈출 AI 팀원을 찾지 못했습니다.");
        }

        // ── testshot ─────────────────────────────────────────────────────────
        /// <summary>
        /// 지정한 부위를 정확히 겨눈 사격 판정을 프로그램으로 재현합니다. 2차·3차만 시험합니다.
        /// </summary>
        /// <param name="partFilter">겨눌 부위 이름 필터입니다. 비우면 <c>COL_HandHitbox_R</c>을 씁니다.</param>
        /// <remarks>
        /// <b>사람의 조준을 변수에서 제거하기 위한 도구입니다.</b> "팔·손을 쏴도 안 맞는다"는 보고에서
        /// 원인이 조준인지 판정인지 가릴 수 없었습니다. 여기서는 부위 콜라이더의 중심을 목표로 삼아
        /// 반드시 통과하는 탄도를 만들고, 그 탄도로 3차와 같은 레이를 쏴 무엇이 먼저 맞는지 나열합니다.
        ///
        /// <para>
        /// 1차(HitDetectVolume 통과)는 건너뜁니다. 1차의 역할은 "어느 대상의 부위를 켤지" 고르는 것뿐이고,
        /// 여기서는 대상을 직접 지정하므로 <see cref="HitboxGroup.SetHitboxesEnabled"/>를 직접 부릅니다.
        /// 그래서 이 결과는 <b>1차를 통과했다고 가정했을 때 3차가 부위를 맞힐 수 있는가</b>만 답합니다.
        /// </para>
        ///
        /// <para>
        /// 켠 부위는 끝나면 되돌립니다. 이 도구는 피해를 주지 않습니다 - 무엇이 맞았는지만 보고합니다.
        /// </para>
        /// </remarks>
        private static object TestShotAtPart(string partFilter)
        {
            if (!Application.isPlaying)
            {
                return new ErrorResponse("testshot은 Play Mode에서만 유효합니다. 에디트 모드에서는 HitboxGroup이 Awake를 거치지 않아 부위 배열이 비어 있습니다.");
            }

            // "적이름/부위이름" 형태를 받습니다. 대상을 지정하지 않으면 사격 지점에서 가장 가까운 대상을
            // 고릅니다. 먼 대상을 고르면 중간 지형에 막혀 판정 자체를 시험할 수 없습니다(실측).
            string enemyFilter = string.Empty;
            string wanted = partFilter;
            int slash = partFilter != null ? partFilter.IndexOf('/') : -1;
            if (slash >= 0)
            {
                enemyFilter = partFilter.Substring(0, slash);
                wanted = partFilter.Substring(slash + 1);
            }

            if (string.IsNullOrWhiteSpace(wanted))
            {
                wanted = "COL_HandHitbox_R";
            }

            // 사격 원점은 실제 총구를 씁니다. 카메라를 쓰면 총구와의 시차가 결과에 섞입니다.
            Gun gun = null;
            foreach (var candidate in Object.FindObjectsByType<Gun>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                gun = candidate;
                break;
            }

            if (gun == null || gun.FirePos == null)
            {
                return new ErrorResponse("발사 지점을 가진 Gun을 찾지 못했습니다.");
            }

            var maskField = typeof(Gun).GetField("m_hitscanLayerMask", BindingFlags.NonPublic | BindingFlags.Instance);
            int mask = ((LayerMask)maskField.GetValue(gun)).value;

            HitboxGroup group = null;
            Collider target = null;
            float bestDistance = float.MaxValue;

            foreach (var candidate in Object.FindObjectsByType<HitboxGroup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!candidate.IsUsable)
                {
                    continue;
                }

                if (enemyFilter.Length > 0 && !candidate.transform.root.name.Contains(enemyFilter))
                {
                    continue;
                }

                Collider found = null;
                foreach (var hb in candidate.GetComponentsInChildren<Hitbox>(true))
                {
                    if (!hb.gameObject.name.Contains(wanted))
                    {
                        continue;
                    }

                    if (hb.TryGetComponent(out Collider c))
                    {
                        found = c;
                        break;
                    }
                }

                if (found == null)
                {
                    continue;
                }

                float d = Vector3.Distance(gun.FirePos.position, candidate.transform.position);
                if (d >= bestDistance)
                {
                    continue;
                }

                bestDistance = d;
                group = candidate;
                target = found;
            }

            if (group != null && target != null)
            {
                int before = group.EnabledHitboxCount;
                group.SetHitboxesEnabled(true);
                Physics.SyncTransforms();
                int opened = group.EnabledHitboxCount;

                Vector3 origin = gun.FirePos.position;
                Vector3 aimAt = target.bounds.center;
                Vector3 direction = (aimAt - origin).normalized;
                float distance = Vector3.Distance(origin, aimAt) + 1.0f;

                var hits = Physics.RaycastAll(origin, direction, distance, mask, QueryTriggerInteraction.Collide);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

                var ordered = new List<object>();
                bool reached = false;
                foreach (var h in hits)
                {
                    bool isTarget = h.collider == target;
                    if (isTarget)
                    {
                        reached = true;
                    }

                    ordered.Add(new
                    {
                        collider = h.collider.name,
                        layer = LayerMask.LayerToName(h.collider.gameObject.layer),
                        dist = h.distance.ToString("F2"),
                        isTarget,
                        isHitbox = h.collider.GetComponent<Hitbox>() != null,
                    });
                }

                group.SetHitboxesEnabled(false);

                return new SuccessResponse(
                    $"testshot: '{group.transform.root.name}'의 '{target.name}'을 겨눔. 켠 부위 {opened}/{group.HitboxCount}, 맞은 것 {hits.Length}개, 목표 도달={reached}.",
                    new
                    {
                        shooter = gun.transform.root.name,
                        enemy = group.transform.root.name,
                        part = target.name,
                        enabledBefore = before,
                        enabledDuring = opened,
                        hitboxCount = group.HitboxCount,
                        mask,
                        origin = origin.ToString("F2"),
                        aimAt = aimAt.ToString("F2"),
                        reachedTarget = reached,
                        hits = ordered,
                    });
            }

            return new ErrorResponse($"'{wanted}' 부위를 가진 살아 있는 대상을 찾지 못했습니다.");
        }

        // ── testaim ──────────────────────────────────────────────────────────
        /// <summary>
        /// 크로스헤어를 부위에 정확히 얹었다고 가정하고, 총구에서 나가는 실제 탄도가 그 부위를 지나는지 봅니다.
        /// </summary>
        /// <param name="partFilter">"적이름/부위이름" 형태입니다. 비우면 가장 가까운 대상의 오른손을 씁니다.</param>
        /// <remarks>
        /// <c>testshot</c>이 답하지 못하는 것을 답합니다. <c>testshot</c>은 총구에서 부위로 <b>직선을 그어</b>
        /// 판정이 성립하는지 봤고, 그 결과 2차·3차는 정상이었습니다. 그러나 실제 사격의 방향은
        /// <see cref="AimController"/>가 <b>카메라</b>에서 조준점을 구한 뒤 <b>총구</b>에서 그 점으로 쏘는
        /// 방식이라, 카메라와 총구의 위치 차이만큼 탄도가 어긋납니다(TPS 시차).
        ///
        /// <para>
        /// 그 어긋남이 부위 반지름(손 0.06m)보다 크면 <b>크로스헤어가 맞고 있는데도 총알은 빗나갑니다</b>.
        /// 그러면 1차 감지가 0개로 나오고 부위는 아예 켜지지 않으므로, 피해가 들어갈 길이 없습니다.
        /// </para>
        ///
        /// <para>
        /// 재현 절차: 부위를 켠 뒤 카메라에서 부위 중심으로 레이를 쏴 조준점을 구하고(<see cref="Gun.TryTraceAimPoint"/>와
        /// 같은 마스크·같은 상태), 그 조준점을 향해 총구에서 1차 마스크로 쏘아 <c>HitDetectVolume</c>을 지나는지,
        /// 이어서 3차 마스크로 쏘아 무엇에 먼저 맞는지 확인합니다.
        /// </para>
        ///
        /// <para>
        /// <b>이 진단은 게임의 조준 경로와 같은 규칙을 써야 의미가 있습니다.</b> 조준 마스크에 감지 마스크를
        /// 섞으면 조준점이 감지 볼륨 앞면에 찍혀, 실제 게임에는 이미 없는 시차를 보고합니다.
        /// </para>
        /// </remarks>
        private static object TestAimParallax(string partFilter)
        {
            if (!Application.isPlaying)
            {
                return new ErrorResponse("testaim은 Play Mode에서만 유효합니다.");
            }

            string enemyFilter = string.Empty;
            string wanted = partFilter;
            int slash = partFilter != null ? partFilter.IndexOf('/') : -1;
            if (slash >= 0)
            {
                enemyFilter = partFilter.Substring(0, slash);
                wanted = partFilter.Substring(slash + 1);
            }

            if (string.IsNullOrWhiteSpace(wanted))
            {
                wanted = "COL_HandHitbox_R";
            }

            Gun gun = null;
            foreach (var candidate in Object.FindObjectsByType<Gun>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                gun = candidate;
                break;
            }

            var camera = Camera.main;
            if (gun == null || gun.FirePos == null || camera == null)
            {
                return new ErrorResponse("Gun/FirePos 또는 메인 카메라를 찾지 못했습니다.");
            }

            var hitMaskField = typeof(Gun).GetField("m_hitscanLayerMask", BindingFlags.NonPublic | BindingFlags.Instance);
            var detMaskField = typeof(Gun).GetField("m_hitDetectLayerMask", BindingFlags.NonPublic | BindingFlags.Instance);
            int hitMask = ((LayerMask)hitMaskField.GetValue(gun)).value;
            int detMask = ((LayerMask)detMaskField.GetValue(gun)).value;

            HitboxGroup group = null;
            Collider target = null;
            float best = float.MaxValue;

            foreach (var candidate in Object.FindObjectsByType<HitboxGroup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!candidate.IsUsable)
                {
                    continue;
                }

                if (enemyFilter.Length > 0 && !candidate.transform.root.name.Contains(enemyFilter))
                {
                    continue;
                }

                foreach (var hb in candidate.GetComponentsInChildren<Hitbox>(true))
                {
                    if (!hb.gameObject.name.Contains(wanted) || !hb.TryGetComponent(out Collider c))
                    {
                        continue;
                    }

                    float d = Vector3.Distance(gun.FirePos.position, candidate.transform.position);
                    if (d < best)
                    {
                        best = d;
                        group = candidate;
                        target = c;
                    }

                    break;
                }
            }

            if (group == null || target == null)
            {
                return new ErrorResponse($"'{wanted}' 부위를 가진 살아 있는 대상을 찾지 못했습니다.");
            }

            group.SetHitboxesEnabled(true);
            Physics.SyncTransforms();

            Vector3 partCenter = target.bounds.center;
            Vector3 camOrigin = camera.transform.position;
            Vector3 camDir = (partCenter - camOrigin).normalized;

            // 1) 카메라가 부위를 정확히 겨눈 상태에서 조준점을 구합니다.
            // 마스크는 감지 마스크를 **섞지 않습니다**. 위에서 이미 부위를 켜고 SyncTransforms까지 했으므로
            // 이 트레이스는 Gun.TryTraceAimPoint(부위를 켠 뒤 히트스캔 마스크로 재트레이스)와 같은 상태를
            // 재현합니다. 감지 마스크를 섞으면 조준점이 부위가 아니라 감지 볼륨 앞면에 찍혀, 이 진단이
            // 게임에 이미 없는 시차를 보고하게 됩니다(옛 조준 공식).
            int aimMask = hitMask;
            Vector3 aimPoint = camOrigin + camDir * 1000f;
            string aimHitName = "(없음)";
            if (Physics.Raycast(camOrigin, camDir, out RaycastHit aimHit, 1000f, aimMask, QueryTriggerInteraction.Collide))
            {
                aimPoint = aimHit.point;
                aimHitName = aimHit.collider.name;
            }

            // 2) 총구에서 그 조준점으로 나가는 실제 탄도.
            Vector3 muzzle = gun.FirePos.position;
            Vector3 shotDir = (aimPoint - muzzle).normalized;
            float shotDist = Vector3.Distance(muzzle, aimPoint) + 1f;

            // 3) 1차: 그 탄도가 HitDetectVolume을 지나는가. 여기서 0이면 부위는 절대 켜지지 않습니다.
            var detHits = Physics.RaycastAll(muzzle, shotDir, shotDist, detMask, QueryTriggerInteraction.Collide);
            bool crossesOwnVolume = false;
            foreach (var h in detHits)
            {
                if (h.collider.GetComponentInParent<HitboxGroup>() == group)
                {
                    crossesOwnVolume = true;
                    break;
                }
            }

            // 4) 3차: 그 탄도로 무엇에 먼저 맞는가.
            var shotHits = Physics.RaycastAll(muzzle, shotDir, shotDist, hitMask, QueryTriggerInteraction.Collide);
            System.Array.Sort(shotHits, (a, b) => a.distance.CompareTo(b.distance));
            var ordered = new List<object>();
            bool hitTargetPart = false;
            foreach (var h in shotHits)
            {
                bool isTarget = h.collider == target;
                if (isTarget)
                {
                    hitTargetPart = true;
                }

                ordered.Add(new
                {
                    collider = h.collider.name,
                    layer = LayerMask.LayerToName(h.collider.gameObject.layer),
                    dist = h.distance.ToString("F2"),
                    isTarget,
                });
            }

            // 시차: 총구에서 부위로 그은 이상적 방향과 실제 탄도의 각도 차이, 그리고 부위 거리에서의 빗나감.
            Vector3 idealDir = (partCenter - muzzle).normalized;
            float angleError = Vector3.Angle(idealDir, shotDir);
            float partDistance = Vector3.Distance(muzzle, partCenter);
            float missDistance = Mathf.Tan(angleError * Mathf.Deg2Rad) * partDistance;

            // 손·머리는 SphereCollider입니다. 캡슐만 보면 반지름이 0으로 나와 판정이 무조건 "빗나감"이 됩니다.
            float partScale = target.transform.lossyScale.x;
            float partRadius =
                target is CapsuleCollider capsulePart ? capsulePart.radius * partScale
                : target is SphereCollider spherePart ? spherePart.radius * partScale
                : 0f;

            group.SetHitboxesEnabled(false);

            return new SuccessResponse(
                $"testaim: '{group.transform.root.name}/{target.name}' 조준. 1차통과={crossesOwnVolume}, 3차부위명중={hitTargetPart}, 시차오차={angleError:F2}도(부위거리에서 {missDistance:F3}m, 부위반지름 {partRadius:F3}m).",
                new
                {
                    enemy = group.transform.root.name,
                    part = target.name,
                    cameraAimHit = aimHitName,
                    aimPoint = aimPoint.ToString("F2"),
                    muzzle = muzzle.ToString("F2"),
                    partCenter = partCenter.ToString("F2"),
                    stage1CrossesDetectVolume = crossesOwnVolume,
                    stage1DetectHits = detHits.Length,
                    stage3HitsTargetPart = hitTargetPart,
                    parallaxAngleDeg = angleError.ToString("F3"),
                    missAtPartDistance = missDistance.ToString("F4"),
                    partRadius = partRadius.ToString("F4"),
                    verdict = missDistance > partRadius ? "시차가 부위 반지름보다 큼 - 크로스헤어가 맞아도 빗나감" : "시차는 부위 안",
                    stage3Hits = ordered,
                });
        }

        // ── helpers ──────────────────────────────────────────────────────────
        /// <summary>§17 검증에서 AI를 띄우는 높이입니다. NavMesh를 확실히 벗어날 만큼 둡니다.</summary>
        private const float StrandHeight = 40.0f;

        /// <summary>높이를 무시한 두 지점 사이의 거리입니다.</summary>
        /// <remarks>
        /// 간격 판정은 수평 거리로만 합니다. 캐릭터 발밑과 NavMesh 표본 높이가 조금씩 달라
        /// y를 넣으면 그 차이만큼 항상 더 멀다고 나옵니다. 판정 쪽(<c>IsDestinationClaimedByOther</c>,
        /// <c>IsPositionClear</c>)도 같은 방식으로 y를 버리므로 지표를 일치시킵니다.
        /// </remarks>
        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            Vector3 delta = a - b;
            delta.y = 0f;
            return delta.magnitude;
        }

        private static T GetPrivate<T>(object target, string field)
        {
            var fi = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            return fi != null && fi.GetValue(target) is T v ? v : default;
        }

        private static string Path(Transform t)
        {
            var s = t.name;
            while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }

        private static string ColorHex(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);
    }
}
#endif
