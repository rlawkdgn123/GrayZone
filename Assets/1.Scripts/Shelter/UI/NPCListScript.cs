using System;
using System.Collections.Generic;
using UnityEngine;

public interface INPCListItemView
{
    void Bind(NPCRuntimeData npcData, Action<string> onClicked);
    void Clear();
}

public class NPCListScript : MonoBehaviour
{
    [SerializeField] private Transform contentRoot;
    [SerializeField] private GameObject rowPrefab;

    private readonly List<GameObject> spawnedRows = new List<GameObject>();

    public void Bind(IReadOnlyList<NPCRuntimeData> characters, Action<string> onClicked)
    {
        Clear();

        if (characters == null || rowPrefab == null)
            return;

        Transform parent = ResolveContentRoot();
        for (int i = 0; i < characters.Count; i++)
        {
            NPCRuntimeData character = characters[i];
            if (character == null || string.IsNullOrWhiteSpace(character.DefinitionId))
                continue;

            GameObject rowObject = Instantiate(rowPrefab, parent);
            if (!TryGetItemView(rowObject, out INPCListItemView rowView))
            {
                Debug.LogWarning($"[{nameof(NPCListScript)}] Row prefab must contain a component implementing {nameof(INPCListItemView)}.", this);
                Destroy(rowObject);
                continue;
            }

            rowObject.SetActive(true);
            rowView.Bind(character, onClicked);
            spawnedRows.Add(rowObject);
        }
    }

    public void Clear()
    {
        for (int i = spawnedRows.Count - 1; i >= 0; i--)
        {
            if (spawnedRows[i] != null)
                Destroy(spawnedRows[i]);
        }

        spawnedRows.Clear();
    }

    private Transform ResolveContentRoot()
    {
        return contentRoot != null ? contentRoot : transform;
    }

    private static bool TryGetItemView(GameObject rowObject, out INPCListItemView itemView)
    {
        itemView = null;
        if (rowObject == null)
            return false;

        MonoBehaviour[] components = rowObject.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] is INPCListItemView view)
            {
                itemView = view;
                return true;
            }
        }

        return false;
    }
}
