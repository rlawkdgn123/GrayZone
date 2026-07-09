using System;
using UnityEngine;
using UnityEngine.UI;

public class TestButton : MonoBehaviour, INPCListItemView
{
    [Header("Catalogs")]
    [SerializeField] private NpcPortraitCatalog npcCatalog;
    [SerializeField] private NpcInjuryIconCatalog injuryIconCatalog;

    [Header("Images")]
    [SerializeField] private Image portraitImage;
    [SerializeField] private Image injuryImage;
    [SerializeField] private Button button;

    private string definitionId;
    private NPCInjuryState injuryState;
    private Action<string> clicked;

    private void Awake()
    {
        CacheReferences();
    }

    private void Reset()
    {
        CacheReferences();
    }

    public void Bind(NPCRuntimeData npcData, Action<string> onClicked)
    {
        CacheReferences();

        if (npcData == null || string.IsNullOrWhiteSpace(npcData.DefinitionId))
        {
            Clear();
            return;
        }

        gameObject.SetActive(true);
        definitionId = npcData.DefinitionId.Trim();
        injuryState = npcData.GetCurrentInjuryState();
        clicked = onClicked;

        Sprite portrait = npcCatalog != null ? npcCatalog.GetPortrait(definitionId) : null;
        Sprite injuryIcon = injuryIconCatalog != null ? injuryIconCatalog.GetIcon(injuryState) : null;

        if (portraitImage != null)
        {
            portraitImage.sprite = portrait;
            portraitImage.enabled = portrait != null;
        }

        if (injuryImage != null)
        {
            injuryImage.sprite = injuryIcon;
            injuryImage.enabled = injuryIcon != null;
        }

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(HandleClick);
            button.interactable = !string.IsNullOrWhiteSpace(definitionId);
        }
    }

    public void Clear()
    {
        CacheReferences();

        definitionId = null;
        injuryState = NPCInjuryState.Healthy;
        clicked = null;

        if (portraitImage != null)
        {
            portraitImage.sprite = null;
            portraitImage.enabled = false;
        }

        if (injuryImage != null)
        {
            injuryImage.sprite = null;
            injuryImage.enabled = false;
        }

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.interactable = false;
        }

        gameObject.SetActive(false);
    }

    private void HandleClick()
    {
        if (!string.IsNullOrWhiteSpace(definitionId))
            clicked?.Invoke(definitionId);
    }

    private void CacheReferences()
    {
        if (button == null)
            button = GetComponent<Button>();

        if (portraitImage == null)
            portraitImage = GetComponent<Image>();
    }
}
