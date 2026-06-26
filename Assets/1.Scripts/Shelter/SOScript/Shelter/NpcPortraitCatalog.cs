using System;
using UnityEngine;

[CreateAssetMenu(menuName = "GrayZone/NPC Portrait Catalog")]
public class NpcPortraitCatalog : ScriptableObject
{
    [SerializeField] private Entry[] entries;

    public Sprite GetPortrait(string definitionId)
    {
        foreach (Entry entry in entries)
        {
            if (entry.definitionId == definitionId)
                return entry.portrait;
        }

        return null;
    }

    [Serializable]
    private struct Entry
    {
        public string definitionId;
        public Sprite portrait;
    }
}
