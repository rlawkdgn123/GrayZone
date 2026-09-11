using System;
using UnityEngine;

public class GameDateManager : MonoBehaviour
{
    public static GameDateManager Instance { get; private set; }

    [Header("Daily Resource Settlement")]
    [Min(0)][SerializeField] private int dailyFoodConsumption = 2;
    [Min(0)][SerializeField] private int dailyFuelConsumption = 2;
    [Min(0)][SerializeField] private int shortageStabilityDecrease = 10;

    public event Action<int, int> DayAdvanced;

    public int CurrentDay
    {
        get
        {
            if (ShelterSceneDataManager.Instance != null)
            {
                return ShelterSceneDataManager.Instance.CurrentDay;
            }

            return 1;
        }
    }

    private void Awake()
    {
        if (TryRejectDuplicate())
        {
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnValidate()
    {
        dailyFoodConsumption = Mathf.Max(0, dailyFoodConsumption);
        dailyFuelConsumption = Mathf.Max(0, dailyFuelConsumption);
        shortageStabilityDecrease = Mathf.Max(0, shortageStabilityDecrease);
    }

    public bool AdvanceDay(int days = 1)
    {
        if (days <= 0)
        {
            return false;
        }

        if (ShelterSceneDataManager.Instance == null)
        {
            Debug.LogWarning("[GameDateManager] ShelterSceneDataManager.Instance is null.");
            return false;
        }

        for (int i = 0; i < days; i++)
        {
            SettleDailyResources(ShelterSceneDataManager.Instance);

            int previousDay = CurrentDay;
            int nextDay = previousDay + 1;
            ShelterSceneDataManager.Instance.SetCurrentDay(nextDay);
            DayAdvanced?.Invoke(previousDay, nextDay);
        }

        return true;
    }

    private void SettleDailyResources(ShelterSceneDataManager shelterDataManager)
    {
        StorageFacility storage = shelterDataManager.Storage;
        bool foodShortage = !TryConsumeDailyResource(
            storage,
            ResourceIds.Food,
            dailyFoodConsumption);
        bool fuelShortage = !TryConsumeDailyResource(
            storage,
            ResourceIds.Fuel,
            dailyFuelConsumption);

        shelterDataManager.SetResourceShortagePenaltyState(
            foodShortage,
            fuelShortage);

        if (foodShortage || fuelShortage)
        {
            shelterDataManager.SetShelterStability(
                shelterDataManager.ShelterStability - shortageStabilityDecrease);
        }

        FacilityManager.Instance?.ApplyFuelShortagePenalty(fuelShortage);
    }

    private static bool TryConsumeDailyResource(
        StorageFacility storage,
        string resourceId,
        int amount)
    {
        if (amount <= 0)
            return true;

        ResourceCost cost = new ResourceCost(resourceId, amount);
        if (storage == null || !storage.CanSpendResource(cost))
            return false;

        return storage.TrySpendResource(cost);
    }

    public int GetRemainingDays(int completeDay)
    {
        return Mathf.Max(0, completeDay - CurrentDay);
    }

    private bool TryRejectDuplicate()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return true;
        }

        return false;
    }
}
