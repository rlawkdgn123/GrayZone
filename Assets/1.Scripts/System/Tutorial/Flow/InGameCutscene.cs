/// <summary>
/// 인게임 컷신(시네마틱 카메라가 화면을 잡고 있는 구간)이 재생 중인지, 조작을 막아야 하는지 알려 줍니다.
/// </summary>
/// <remarks>
/// 컷신 중에는 Main Camera가 플레이어 카메라가 아니라 컷신 카메라를 따라갑니다. 그래서 카메라 각도로 계산하는
/// 조작 캐릭터의 상체 조준 값(<see cref="ThirdPersonController"/>의 LookX/LookY)이 컷신 카메라 쪽으로 틀어집니다.
/// 이 값을 보고 컷신 동안에는 카메라 기준 조준을 끕니다.
///
/// 조작을 막는 컷신이 도는 동안에는 로딩 화면 닫힘처럼 다른 곳에서 입력을 다시 켜려 해도
/// <see cref="PlayerInputController.SetInputGate"/>가 켜지지 않습니다. 컷신이 겹쳐도 되도록 시작·끝 횟수로 셉니다.
/// </remarks>
public static class InGameCutscene
{
    private static int s_depth;
    private static int s_inputLockDepth;

    /// <summary>컷신이 하나라도 재생 중이면 true입니다.</summary>
    public static bool IsPlaying => s_depth > 0;

    /// <summary>조작을 막는 컷신이 하나라도 재생 중이면 true입니다.</summary>
    public static bool IsInputLocked => s_inputLockDepth > 0;

    /// <summary>컷신 시작을 알립니다. 끝날 때 같은 <paramref name="lockInput"/>으로 <see cref="End"/>를 한 번 불러야 합니다.</summary>
    public static void Begin(bool lockInput)
    {
        s_depth++;
        if (lockInput)
        {
            s_inputLockDepth++;
        }
    }

    /// <summary>컷신 끝을 알립니다.</summary>
    public static void End(bool lockInput)
    {
        if (s_depth > 0)
        {
            s_depth--;
        }

        if (lockInput && s_inputLockDepth > 0)
        {
            s_inputLockDepth--;
        }
    }

    /// <summary>씬을 다시 불러올 때 남은 값을 지웁니다.</summary>
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnLoad()
    {
        s_depth = 0;
        s_inputLockDepth = 0;
    }
}
