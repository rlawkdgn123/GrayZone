using System;
using UnityEngine;
using UnityEngine.UI;

public class TestButton : MonoBehaviour
{
    [SerializeField] private Image portraitImage;
    [SerializeField] private Image injuryImage;
    [SerializeField] private Button button;

    private string runtimeId;
    private Action<string> clicked;

    private void Awake()
    {
        CacheReferences();
    }

    private void Reset()
    {
        CacheReferences();
    }

    public void Bind(NPCRuntimeData character, Sprite portrait, Action<string> onClicked)
    {
        CacheReferences();

        if (character == null)
        {
            Clear();
            return;
        }

        gameObject.SetActive(true);
        runtimeId = character.RuntimeId;
        clicked = onClicked;

        if (portraitImage != null)
            portraitImage.sprite = portrait;

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(HandleClick);
            button.interactable = !string.IsNullOrWhiteSpace(runtimeId);
        }
    }

    public void Clear()
    {
        CacheReferences();

        runtimeId = null;
        clicked = null;

        if (portraitImage != null)
            portraitImage.sprite = null;

        if (injuryImage != null)
            injuryImage.sprite = null;

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.interactable = false;
        }

        gameObject.SetActive(false);
    }

    private void HandleClick()
    {
        if (!string.IsNullOrWhiteSpace(runtimeId))
            clicked?.Invoke(runtimeId);
    }

    private void CacheReferences()
    {
        if (button == null)
            button = GetComponent<Button>();

        if (portraitImage == null)
            portraitImage = GetComponent<Image>();
    }
}
