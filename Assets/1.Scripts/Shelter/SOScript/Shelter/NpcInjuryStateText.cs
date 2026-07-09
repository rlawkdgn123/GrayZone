using UnityEngine;

public static class NpcInjuryStateText
{
    public static string ToWord(NPCInjuryState s) => s switch
    {
        NPCInjuryState.Healthy => "건강",
        NPCInjuryState.LightInjury => "경상",
        NPCInjuryState.HeavyInjury => "중상",
        NPCInjuryState.NearDeath => "치명상",
    };
}
