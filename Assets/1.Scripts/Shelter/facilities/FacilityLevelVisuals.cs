using UnityEngine;

// 레벨별 건물 비주얼을 미리 배치해두고, 현재 레벨에 해당하는 것만 켠다.
// 업그레이드/도메인 로직을 전혀 모르는 재사용 부품 — 각 시설이 조합(composition)으로 사용한다.
// levelRoots[i] = 레벨 i(0-based)에서 보여줄 건물 오브젝트. 인스펙터에서 미리 배치 후 연결.
public class FacilityLevelVisuals : MonoBehaviour
{
    [SerializeField] private GameObject[] levelRoots;

    public int LevelCount => levelRoots != null ? levelRoots.Length : 0;

    // 지정한 레벨의 비주얼만 켜고 나머지는 끈다. 범위를 벗어나면 클램프한다.
    public void ShowLevel(int level)
    {
        if (levelRoots == null || levelRoots.Length == 0)
            return;

        int target = Mathf.Clamp(level, 0, levelRoots.Length - 1);
        for (int i = 0; i < levelRoots.Length; i++)
        {
            GameObject root = levelRoots[i];
            if (root == null)
                continue;

            bool active = (i == target);
            if (root.activeSelf != active)
                root.SetActive(active);
        }
    }

    // 모든 레벨 비주얼을 끈다(잠금 상태 등에서 사용).
    public void HideAll()
    {
        if (levelRoots == null)
            return;

        foreach (GameObject root in levelRoots)
        {
            if (root != null && root.activeSelf)
                root.SetActive(false);
        }
    }
}
