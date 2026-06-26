public interface IRecoveryComponent
{
    bool TryCompleteShelterRecovery(CharacterManager characterManager, string runtimeId, out CharacterActionFailure failure);
}

public class RecoveryComponent : IRecoveryComponent
{
    public bool TryCompleteShelterRecovery(CharacterManager characterManager, string runtimeId, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (characterManager == null)
        {
            failure = CharacterActionFailure.DataSourceUnavailable;
            return false;
        }

        return characterManager.TryCompleteRecovery(runtimeId, out failure);
    }
}
