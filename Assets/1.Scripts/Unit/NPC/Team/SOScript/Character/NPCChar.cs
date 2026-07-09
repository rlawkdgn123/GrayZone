using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "NPCChar", menuName = "Scriptable Objects/NPCChar")]
public class NPCChar : ScriptableObject
{
    [Min(1)] public int maxHP;
    public NPCType Type;
    public int likeability;
    public float InjuryGauge = 100f;
    [FormerlySerializedAs("maxInjugryGauge")]
    public float maxInjuryGauge = 100f;

    [field: SerializeField] public string DefinitionId { get; private set; }
    [field: SerializeField] public string Prefab { get; private set; }

    public int MaxHP => Mathf.Max(1, maxHP);
    public float MaxInjuryGauge => Mathf.Max(1f, maxInjuryGauge);

    private void OnValidate()
    {
        maxHP = Mathf.Max(1, maxHP);
        maxInjuryGauge = Mathf.Max(1f, maxInjuryGauge);
        InjuryGauge = Mathf.Clamp(InjuryGauge, 0f, maxInjuryGauge);
    }
}
