using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(VerticalLayoutGroup))]
public class AutoVerticalSpacing : MonoBehaviour
{
    [SerializeField] private RectTransform viewport;
    [SerializeField] private RectTransform itemPrefabRect;
    [SerializeField] private float minSpacing = 5f;
    [SerializeField] private float maxSpacing = 30f;

    private RectTransform content;
    private VerticalLayoutGroup layout;

    private bool dirty;

    private void OnTransformChildrenChanged()
    {
        dirty = true;
    }

    private void LateUpdate()
    {
        if (!dirty)
            return;

        dirty = false;
        Recalculate();
    }

    private void Awake()
    {
        content = GetComponent<RectTransform>();
        layout = GetComponent<VerticalLayoutGroup>();
    }

    private void OnEnable()
    {
        Recalculate();
    }

    private void OnRectTransformDimensionsChange()
    {
        Recalculate();
    }

    public void Recalculate()
    {
        if (viewport == null || itemPrefabRect == null)
            return;

        int count = transform.childCount;
        if (count <= 1)
            return;

        float viewportHeight = viewport.rect.height;
        float itemHeight = itemPrefabRect.rect.height;

        float available =
            viewportHeight
            - layout.padding.top
            - layout.padding.bottom
            - itemHeight * count;

        float spacing = available / (count - 1);
        layout.spacing = Mathf.Clamp(spacing, minSpacing, maxSpacing);

        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
    }
}