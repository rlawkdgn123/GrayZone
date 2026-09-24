using UnityEngine;

/// <summary>
/// 전투 씬 진입 시 전역 GameData의 Scramble 플래그를 읽어 슈터 오브젝트를 활성화합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ScrambleShooterActivator : MonoBehaviour
{
    [SerializeField] private GameObject m_shooter01;
    [SerializeField] private GameObject m_shooter02;

    private void Start()
    {
        Apply();
    }

    public void Apply()
    {
        GameDataManager gameData = GameDataManager.Instance;
        if (gameData == null)
        {
            Debug.LogWarning(
                "[ScrambleShooterActivator] GameDataManager is not available.",
                this);
        }

        if (m_shooter01 != null)
            m_shooter01.SetActive(gameData != null && gameData.Shooter01);
        if (m_shooter02 != null)
            m_shooter02.SetActive(gameData != null && gameData.Shooter02);
    }
}
