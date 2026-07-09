using System;
using UnityEngine;

[CreateAssetMenu(menuName = "GrayZone/NPC Injury Icon Catalog")]
public class NpcInjuryIconCatalog : ScriptableObject
{
    [SerializeField] private Entry[] entries;

    public Sprite GetIcon(NPCInjuryState state)
    {
        foreach (Entry entry in entries)
        {
            if (entry.state == state)
                return entry.icon;
        }

        return null;
    }

    [Serializable]
    private struct Entry
    {
        public NPCInjuryState state;
        public Sprite icon;
    }
}
