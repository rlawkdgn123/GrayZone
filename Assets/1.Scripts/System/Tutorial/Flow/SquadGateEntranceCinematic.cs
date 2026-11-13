using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Playables;

/// <summary>
/// 시네마틱 Timeline이 재생되는 동안 대문 두 짝을 열고, 분대원이 지금 대형 그대로 문 안쪽에서 원래 자리까지 걸어 나오면 문을 다시 닫습니다.
/// </summary>
/// <remarks>
/// 같은 오브젝트의 <see cref="PlayableDirector"/>가 재생을 시작하면 동작합니다. 분대원의 원래 자리는 재생을 시작한 순간의 위치이고,
/// 출발 위치는 그 대형의 가운데를 <see cref="m_startPoint"/>로 옮긴 자리입니다.
/// 걷는 동안에는 이동·AI 컴포넌트를 끄고 위치와 애니메이터 값을 직접 넣습니다. 다 걸으면 원래 자리와 방향으로 맞추고 컴포넌트를 되돌립니다.
/// Timeline이 먼저 멈추면(건너뛰기 등) 문을 닫고 분대원을 바로 도착 상태로 맞춥니다.
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayableDirector))]
public sealed class SquadGateEntranceCinematic : MonoBehaviour
{
    [Tooltip("걸어 나올 분대원입니다. 재생 시작 때 위치가 걸어 나온 뒤 설 자리입니다.")]
    [SerializeField] private List<Transform> m_members = new();

    [Tooltip("출발 대형의 가운데가 놓일 위치입니다.")]
    [SerializeField] private Transform m_startPoint;

    [Tooltip("첫 분대원을 따라가는 시네마틱 카메라입니다. 재생 시작 때 지금 놓인 자리에서 출발하도록 따라가는 거리를 맞춥니다. 비우면 건드리지 않습니다.")]
    [SerializeField] private Unity.Cinemachine.CinemachineFollow m_trackingCamera;

    [Header("Doors")]
    [Tooltip("y축 양의 방향으로 열리는 문입니다.")]
    [SerializeField] private Transform m_doorA;

    [Tooltip("y축 음의 방향으로 열리는 문입니다.")]
    [SerializeField] private Transform m_doorB;

    [Tooltip("문이 열리는 각도(도)입니다.")]
    [Range(0.0f, 180.0f)]
    [SerializeField] private float m_doorOpenAngle = 90.0f;

    [Tooltip("문이 다 열리는 데 걸리는 시간(초)입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_doorOpenSeconds = 2.5f;

    [Tooltip("분대원이 모두 도착한 뒤 문이 다시 닫히는 데 걸리는 시간(초)입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_doorCloseSeconds = 2.0f;

    [Header("Walk")]
    [Tooltip("문이 열리기 시작하고 걷기 시작할 때까지의 시간(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_walkDelay = 1.2f;

    [Tooltip("걷는 속도(m/s)입니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_walkSpeed = 1.4f;

    [Tooltip("도착한 뒤 원래 방향으로 몸을 돌리는 시간(초)입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_turnSeconds = 0.4f;

    private static readonly int MoveSpeedHash = Animator.StringToHash("MoveSpeed");
    private static readonly int MotionSpeedHash = Animator.StringToHash("MotionSpeed");
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveZHash = Animator.StringToHash("MoveZ");
    private static readonly int MoveStateHash = Animator.StringToHash("MoveState");
    private static readonly int IsMoveHash = Animator.StringToHash("IsMove");
    private static readonly int GroundedHash = Animator.StringToHash("IsGrounded");
    private static readonly int LookXHash = Animator.StringToHash("LookX");
    private static readonly int LookYHash = Animator.StringToHash("LookY");

    /// <summary>걷기 클립(R_StrafeWalk_F)이 한 번 재생될 때 앞으로 가는 속도(m/s)입니다. 애니메이션 재생 속도를 걷는 속도에 맞출 때 씁니다.</summary>
    private const float WalkClipSpeed = 1.42f;

    /// <summary>지면을 찾을 때 발 위에서 광선을 시작하는 높이(m)입니다.</summary>
    private const float GroundProbeUp = 0.5f;

    /// <summary>지면을 찾을 때 발 아래로 보는 거리(m)입니다.</summary>
    private const float GroundProbeDown = 1.5f;

    private PlayableDirector m_director;
    private Coroutine m_routine;
    private readonly List<Walker> m_walkers = new();
    private Quaternion m_doorAClosed;
    private Quaternion m_doorBClosed;

    /// <summary>걸어 나오는 분대원 한 명의 출발·도착 상태와 꺼 둔 컴포넌트입니다.</summary>
    private sealed class Walker
    {
        public Transform Transform;
        public Animator Animator;
        public Vector3 Start;
        public Vector3 End;
        public Quaternion EndRotation;
        public readonly List<Behaviour> Disabled = new();
        public CharacterController Controller;
        public bool ControllerWasEnabled;
        public NavMeshAgent Agent;
        public bool Arrived;
    }

    private void Awake()
    {
        m_director = GetComponent<PlayableDirector>();
    }

    private void OnEnable()
    {
        m_director.played += HandlePlayed;
        m_director.stopped += HandleStopped;
    }

    private void OnDisable()
    {
        m_director.played -= HandlePlayed;
        m_director.stopped -= HandleStopped;
        Finish();
    }

    private void HandlePlayed(PlayableDirector director)
    {
        if (!Application.isPlaying || m_routine != null)
        {
            return;
        }

        m_routine = StartCoroutine(Run());
    }

    private void HandleStopped(PlayableDirector director)
    {
        Finish();
    }

    private IEnumerator Run()
    {
        BeginWalkers();
        m_doorAClosed = m_doorA != null ? m_doorA.localRotation : Quaternion.identity;
        m_doorBClosed = m_doorB != null ? m_doorB.localRotation : Quaternion.identity;

        float elapsed = 0.0f;
        bool allArrived = false;
        while (!allArrived || elapsed < m_doorOpenSeconds)
        {
            float deltaTime = Time.unscaledDeltaTime;
            elapsed += deltaTime;
            SetDoorOpen(Mathf.SmoothStep(0.0f, 1.0f, elapsed / m_doorOpenSeconds));

            allArrived = true;
            for (int i = 0; i < m_walkers.Count; i++)
            {
                KeepMovementOff(m_walkers[i]);
                allArrived &= StepWalker(m_walkers[i], elapsed, deltaTime);
            }

            yield return null;
        }

        // 몸을 원래 방향으로 돌리면서 대문을 다시 닫습니다. 둘 다 끝날 때까지 기다립니다.
        float after = 0.0f;
        while (after < m_turnSeconds || after < m_doorCloseSeconds)
        {
            after += Time.unscaledDeltaTime;
            SetDoorOpen(1.0f - Mathf.SmoothStep(0.0f, 1.0f, after / m_doorCloseSeconds));
            for (int i = 0; i < m_walkers.Count; i++)
            {
                Walker walker = m_walkers[i];
                KeepMovementOff(walker);
                walker.Transform.rotation = Quaternion.Slerp(walker.Transform.rotation, walker.EndRotation, after / m_turnSeconds);
            }

            yield return null;
        }

        m_routine = null;
        Finish();
    }

    /// <summary>분대원의 도착 위치를 기억하고, 컴포넌트를 끈 뒤 출발 위치로 옮깁니다.</summary>
    private void BeginWalkers()
    {
        m_walkers.Clear();
        Vector3 center = Vector3.zero;
        int count = 0;
        for (int i = 0; i < m_members.Count; i++)
        {
            if (m_members[i] != null)
            {
                center += m_members[i].position;
                count++;
            }
        }

        if (count == 0)
        {
            return;
        }

        center /= count;
        Vector3 offset = m_startPoint != null ? m_startPoint.position - center : Vector3.zero;
        offset.y = 0.0f;

        for (int i = 0; i < m_members.Count; i++)
        {
            Transform member = m_members[i];
            if (member == null)
            {
                continue;
            }

            Walker walker = new()
            {
                Transform = member,
                Animator = member.GetComponent<Animator>(),
                EndRotation = member.rotation,
                Controller = member.GetComponent<CharacterController>(),
                Agent = member.GetComponent<NavMeshAgent>(),
            };

            // 씬에 놓인 높이가 지면에서 조금 떠 있으면, 연출이 끝나고 컨트롤러가 켜질 때 중력으로 내려앉아 튑니다.
            // 출발·도착 자리를 컨트롤러가 지면에 섰을 때의 높이로 맞춥니다.
            walker.End = SnapToGround(walker, member.position);
            walker.Start = SnapToGround(walker, member.position + offset);

            DisableBehaviour(walker, member.GetComponent<ThirdPersonController>());
            DisableBehaviour(walker, member.GetComponent<SquadAIController>());
            DisableBehaviour(walker, walker.Agent);
            if (walker.Controller != null)
            {
                walker.ControllerWasEnabled = walker.Controller.enabled;
                walker.Controller.enabled = false;
            }

            Vector3 direction = walker.End - walker.Start;
            direction.y = 0.0f;
            member.SetPositionAndRotation(
                walker.Start,
                direction.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(direction) : walker.EndRotation);
            SetWalkAnimation(walker, false);
            m_walkers.Add(walker);
        }

        // 카메라가 씬에 놓인 자리에서 출발하도록, 첫 분대원의 출발 위치 기준으로 따라가는 거리를 다시 잡습니다.
        if (m_trackingCamera != null && m_walkers.Count > 0)
        {
            m_trackingCamera.FollowOffset = m_trackingCamera.transform.position - m_walkers[0].Start;
        }
    }

    private static void DisableBehaviour(Walker walker, Behaviour behaviour)
    {
        if (behaviour != null && behaviour.enabled)
        {
            behaviour.enabled = false;
            if (!walker.Disabled.Contains(behaviour))
            {
                walker.Disabled.Add(behaviour);
            }
        }
    }

    /// <summary>
    /// 걷는 동안 이동 컴포넌트가 꺼져 있게 유지합니다.
    /// </summary>
    /// <remarks>
    /// 컷신은 씬 시작 직후에 재생되는데, 분대 초기화가 그 뒤에 조작 상태를 다시 적용하면서 이 컴포넌트들을 켭니다.
    /// 켜진 채 두면 위치를 직접 옮기는 것과 서로 싸우므로, 매 프레임 다시 끄고 끝날 때 되돌릴 목록에 넣습니다.
    /// </remarks>
    private static void KeepMovementOff(Walker walker)
    {
        if (walker.Transform == null)
        {
            return;
        }

        DisableBehaviour(walker, walker.Transform.GetComponent<ThirdPersonController>());
        DisableBehaviour(walker, walker.Transform.GetComponent<SquadAIController>());
        DisableBehaviour(walker, walker.Agent);
        if (walker.Controller != null && walker.Controller.enabled)
        {
            walker.ControllerWasEnabled = true;
            walker.Controller.enabled = false;
        }
    }

    /// <summary>분대원 한 명을 도착 위치 쪽으로 한 프레임만큼 걷게 합니다. 도착했으면 true입니다.</summary>
    private bool StepWalker(Walker walker, float elapsed, float deltaTime)
    {
        if (walker.Arrived)
        {
            return true;
        }

        if (elapsed < m_walkDelay)
        {
            SetWalkAnimation(walker, false);
            return false;
        }

        // 수평으로만 다가가고 높이는 지면을 따라갑니다. 출발점과 도착점의 지면 높이가 달라 떠 보이거나 묻히지 않게 합니다.
        Vector3 current = walker.Transform.position;
        Vector3 target = new Vector3(walker.End.x, current.y, walker.End.z);
        Vector3 next = Vector3.MoveTowards(current, target, m_walkSpeed * deltaTime);
        next = (next - target).sqrMagnitude <= 0.0001f ? walker.End : SnapToGround(walker, next);

        // 카메라나 입력이 아니라 걸어가는 방향을 정면으로 봅니다.
        Vector3 travel = next - walker.Transform.position;
        travel.y = 0.0f;
        Quaternion rotation = walker.Transform.rotation;
        if (travel.sqrMagnitude > 0.000001f)
        {
            rotation = Quaternion.RotateTowards(rotation, Quaternion.LookRotation(travel), 360.0f * deltaTime);
        }

        walker.Transform.SetPositionAndRotation(next, rotation);
        if ((walker.End - next).sqrMagnitude <= 0.0001f)
        {
            walker.Arrived = true;
            SetWalkAnimation(walker, false);
            return true;
        }

        SetWalkAnimation(walker, true);
        return false;
    }

    /// <summary>
    /// 위치의 높이를 그 자리 지면에 캐릭터 컨트롤러가 서 있을 때의 높이로 바꿉니다. 지면을 찾지 못하면 그대로 돌려줍니다.
    /// </summary>
    /// <remarks>분대원 자신의 콜라이더와 트리거는 지면으로 보지 않습니다.</remarks>
    private Vector3 SnapToGround(Walker walker, Vector3 position)
    {
        RaycastHit[] hits = Physics.RaycastAll(
            position + Vector3.up * GroundProbeUp,
            Vector3.down,
            GroundProbeUp + GroundProbeDown,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        float groundY = float.NegativeInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            if (!IsSquadCollider(hits[i].collider) && hits[i].point.y > groundY)
            {
                groundY = hits[i].point.y;
            }
        }

        if (float.IsNegativeInfinity(groundY))
        {
            return position;
        }

        // 컨트롤러는 캡슐 바닥이 지면에서 skinWidth만큼 떠서 섭니다. 캡슐 바닥은 피벗에서 center.y - height/2 위에 있습니다.
        CharacterController controller = walker.Controller;
        float footOffset = controller != null ? controller.skinWidth - (controller.center.y - controller.height * 0.5f) : 0.0f;
        position.y = groundY + footOffset;
        return position;
    }

    private bool IsSquadCollider(Collider collider)
    {
        for (int i = 0; i < m_members.Count; i++)
        {
            if (m_members[i] != null && collider.transform.IsChildOf(m_members[i]))
            {
                return true;
            }
        }

        return false;
    }

    private void SetWalkAnimation(Walker walker, bool moving)
    {
        Animator animator = walker.Animator;
        if (animator == null)
        {
            return;
        }

        animator.SetFloat(MoveSpeedHash, moving ? m_walkSpeed : 0.0f);
        animator.SetFloat(MotionSpeedHash, moving ? Mathf.Max(0.5f, m_walkSpeed / WalkClipSpeed) : 1.0f);
        animator.SetFloat(MoveXHash, 0.0f);
        animator.SetFloat(MoveZHash, moving ? 1.0f : 0.0f);
        // 1 = 걷기 단계입니다(0 웅크림, 1 걷기, 2 조깅, 3 달리기). 대문에서 걸어 나오는 장면이라 걷기 블렌드를 씁니다.
        animator.SetFloat(MoveStateHash, 1.0f);
        animator.SetBool(IsMoveHash, moving);
        animator.SetBool(GroundedHash, true);

        // 이동 컴포넌트를 끈 동안 상체 조준 값이 끄기 직전 카메라 각도에 남지 않게 정면으로 둡니다.
        animator.SetFloat(LookXHash, 0.0f);
        animator.SetFloat(LookYHash, 0.0f);
    }

    private void SetDoorOpen(float amount)
    {
        if (m_doorA != null)
        {
            m_doorA.localRotation = m_doorAClosed * Quaternion.Euler(0.0f, m_doorOpenAngle * amount, 0.0f);
        }

        if (m_doorB != null)
        {
            m_doorB.localRotation = m_doorBClosed * Quaternion.Euler(0.0f, -m_doorOpenAngle * amount, 0.0f);
        }
    }

    /// <summary>문을 닫힌 상태로, 분대원을 도착 위치와 방향으로 맞추고 꺼 둔 컴포넌트를 되돌립니다.</summary>
    private void Finish()
    {
        if (m_routine != null)
        {
            StopCoroutine(m_routine);
            m_routine = null;
        }

        if (m_walkers.Count == 0)
        {
            return;
        }

        SetDoorOpen(0.0f);
        for (int i = 0; i < m_walkers.Count; i++)
        {
            Walker walker = m_walkers[i];
            if (walker.Transform == null)
            {
                continue;
            }

            walker.Transform.SetPositionAndRotation(walker.End, walker.EndRotation);
            SetWalkAnimation(walker, false);
            if (walker.Controller != null)
            {
                walker.Controller.enabled = walker.ControllerWasEnabled;
            }

            for (int j = 0; j < walker.Disabled.Count; j++)
            {
                if (walker.Disabled[j] != null)
                {
                    walker.Disabled[j].enabled = true;
                }
            }

            if (walker.Agent != null && walker.Agent.enabled)
            {
                walker.Agent.Warp(walker.End);
            }
        }

        m_walkers.Clear();
    }
}
