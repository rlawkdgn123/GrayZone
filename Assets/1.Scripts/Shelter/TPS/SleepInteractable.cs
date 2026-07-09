using UnityEngine;

// 잠자기 상호작용 — 하루를 넘긴다(→ GameDateManager.DayAdvanced → 각 시설 OnDayAdvanced).
// ToDo(임시 테스트): 지금은 E 누르면 즉시 하루 넘김. 추후 "주무시겠습니까?" 확인 팝업 →
//                    확인 콜백에서 AdvanceDay 호출하도록 변경, 암전/페이드 연출 + 전환 중 입력 잠금 추가.
public class SleepInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private int daysToAdvance = 1;

    public void Interact(GameObject interactor)
    {
        // ToDo: 확인 팝업 후 호출로 교체 (현재는 테스트용 즉시 진행)
        if (GameDateManager.Instance == null)
        {
            Debug.LogWarning("[SleepInteractable] GameDateManager.Instance is null.", this);
            return;
        }

        GameDateManager.Instance.AdvanceDay(daysToAdvance);
    }
}
