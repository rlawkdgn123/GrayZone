/// <summary>
/// 게임 난이도입니다. 타이틀에서 새 게임을 시작할 때 고르고, 저장 데이터에 함께 남습니다.
/// </summary>
/// <remarks>
/// 저장 파일에는 숫자로 들어갑니다. 값을 바꾸거나 순서를 바꾸면 기존 저장의 난이도가 달라지므로 뒤에만 추가합니다.
/// </remarks>
public enum GameDifficulty
{
    /// <summary>중간입니다. 현재 밸런스 SO가 기준입니다.</summary>
    Normal = 0,

    /// <summary>어려움입니다. 어려움용 밸런스 SO가 지정된 곳은 그 SO를 씁니다.</summary>
    Hard = 1
}

/// <summary>
/// 현재 난이도에 맞는 밸런스 SO를 고릅니다.
/// </summary>
/// <remarks>
/// 난이도별 수치는 코드에서 배율을 곱하지 않고 SO를 따로 둡니다. 기획이 SO 값만 보고 난이도별 수치를 확인하고 고칠 수 있게 하기 위해서입니다.
/// </remarks>
public static class GameDifficultyBalance
{
    /// <summary>현재 판의 난이도입니다. 전역 데이터가 없으면(씬 단독 실행) 중간으로 봅니다.</summary>
    public static GameDifficulty Current => GameDataManager.Instance != null
        ? GameDataManager.Instance.Difficulty
        : GameDifficulty.Normal;

    /// <summary>
    /// 어려움이면 <paramref name="hard"/>를, 아니면 <paramref name="normal"/>을 돌려줍니다.
    /// </summary>
    /// <remarks>어려움이어도 <paramref name="hard"/>가 비어 있으면 <paramref name="normal"/>을 씁니다.</remarks>
    public static T Select<T>(T normal, T hard) where T : UnityEngine.Object
    {
        return Current == GameDifficulty.Hard && hard != null ? hard : normal;
    }

    /// <summary>화면에 보일 난이도 이름입니다.</summary>
    public static string DisplayName(GameDifficulty difficulty)
    {
        return difficulty == GameDifficulty.Hard ? "어려움" : "중간";
    }
}
