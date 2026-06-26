using System;
using UnityEngine;
using UnityEngine.UI;

public class MedicalPatientSlotView : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image slotImage;
    [SerializeField] private Sprite unlockedSprite;
    [SerializeField] private Sprite lockedSprite;

    private int slotIndex;
    private Action<int> clicked;

    private void Awake()
    {
        CacheReferences();
    }

    private void Reset()
    {
        CacheReferences();
    }

    public void Bind(int index, bool isUnlocked, Action<int> onClicked)
    {
        CacheReferences();
        slotIndex = index;
        clicked = onClicked;

        if (slotImage != null)
            slotImage.sprite = isUnlocked ? unlockedSprite : lockedSprite;

        if (button != null)
        {
            button.interactable = isUnlocked;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(HandleClick);
        }
    }

    private void HandleClick()
    {
        clicked?.Invoke(slotIndex);
    }

    private void CacheReferences()
    {
        if (button == null)
            button = GetComponent<Button>();

        if (slotImage == null)
            slotImage = GetComponent<Image>();
    }
}
