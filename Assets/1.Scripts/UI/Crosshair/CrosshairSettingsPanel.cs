using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ESC 설정 화면의 조준선 페이지입니다. 캐릭터별 조준선 또는 공용 투척물 조준선의 내부(Main)/외부(Sub) 값을 편집하고 미리 보여 줍니다.
/// </summary>
/// <remarks>
/// 설정 화면 원칙(바꾸면 바로 적용, 파일 저장은 '변경사항 저장'을 눌러야 함)을 따릅니다. 편집할 때마다
/// <see cref="ApplyPendingValues"/>로 <see cref="CrosshairSettingsService"/>에 넘겨 HUD에 바로 적용하고, 파일 저장은 설정 화면의
/// 저장 버튼이 맡습니다. 되돌리기는 설정 화면이 조준선 값을 연 때의 값으로 바꾼 뒤 <see cref="RevertToCommittedValues"/>로
/// 서비스 값을 다시 읽습니다. 기본값 버튼은 씬 값으로 바꾸고 바로 적용합니다.
///
/// 미리보기는 <see cref="CrosshairController"/>의 배치 계산(링 기준 지름, 바깥쪽 두께 확장, 팔 위치)을 따라 텍스처에 그립니다.
/// 탄퍼짐에 따른 간격 변화는 반영하지 않고 기본 간격으로 그립니다.
/// </remarks>
public sealed class CrosshairSettingsPanel : MonoBehaviour
{
    private const float ControlsLeft = 430f;
    private const float ControlsWidth = 650f;
    private const float RowHeight = 50f;

    private enum Layer { Main, Sub }

    private sealed class Row
    {
        public GameObject Root;
        public Func<CrosshairStyle, bool> IsVisible;
        public Action<CrosshairStyle> Refresh;
    }

    private static readonly Color[] FillSwatches =
    {
        Color.white,
        new Color(0.95f, 0.18f, 0.18f, 1f),
        new Color(0.15f, 0.95f, 0.8f, 1f),
        new Color(1f, 0.84f, 0.18f, 1f),
        new Color(0.3f, 0.95f, 0.3f, 1f),
        new Color(0.35f, 0.65f, 1f, 1f),
    };

    private static readonly Color[] StrokeSwatches = { Color.black, new Color(0.25f, 0.25f, 0.25f, 1f), Color.white };
    private static readonly Color SelectedColor = new(0.95f, 0.08f, 0.08f, 0.88f);
    private static readonly Color IdleColor = new(1f, 1f, 1f, 0.08f);

    private readonly List<KeyValuePair<PlayerbleCharacterId, string>> m_characters = new();
    private readonly Dictionary<PlayerbleCharacterId, CrosshairStyle> m_workingStyles = new();
    private readonly List<Image> m_characterButtonImages = new();
    private readonly Image[] m_layerButtonImages = new Image[2];
    private readonly Image[] m_sideButtonImages = new Image[2];
    private readonly List<Row> m_mainRows = new();
    private readonly List<Row> m_subRows = new();
    private readonly List<Image> m_mainShapeButtons = new();
    private readonly List<Image> m_subShapeButtons = new();

    private TMP_FontAsset m_regularFont;
    private TMP_FontAsset m_boldFont;
    private bool m_isThrowableMode;
    private bool m_isBuilt;
    private bool m_isRefreshing;
    private int m_selectedCharacterIndex;
    private Layer m_selectedLayer;

    /// <summary>조준(ADS) 쪽을 편집하고 있는지 여부입니다. 거짓이면 비조준 쪽입니다. 동적 크로스헤어를 끈 경우에만 씁니다.</summary>
    private bool m_adsTab;
    private float m_layerTop;
    private Image m_dynamicCheck;
    private RectTransform m_sideRow;
    private RectTransform m_layerRow;
    private RectTransform m_controlsPanel;
    private CrosshairStyle m_workingThrowable;
    private RectTransform m_characterRow;
    private RectTransform m_mainRowsRoot;
    private RectTransform m_subRowsRoot;
    private TextMeshProUGUI m_unavailableText;
    private TextMeshProUGUI m_previewProfileLabel;
    private TextMeshProUGUI m_previewZoomLabel;
    private RawImage m_previewImage;
    private CrosshairHudPreview m_hudPreview;

    /// <summary>캐릭터별 조준선 편집 화면을 만듭니다.</summary>
    public void Build(RectTransform parent, TMP_FontAsset regularFont, TMP_FontAsset boldFont)
    {
        BuildInternal(parent, regularFont, boldFont, false);
    }

    /// <summary>모든 캐릭터가 같이 쓰는 투척물 조준선 편집 화면을 만듭니다.</summary>
    public void BuildSharedThrowable(RectTransform parent, TMP_FontAsset regularFont, TMP_FontAsset boldFont)
    {
        BuildInternal(parent, regularFont, boldFont, true);
    }

    /// <summary>편집 중인 값을 서비스에 넘겨 HUD에 적용합니다. 설정 파일 저장 전에 호출해야 파일에 들어갑니다.</summary>
    public void ApplyPendingValues()
    {
        CrosshairSettingsService service = CrosshairSettingsService.Instance;
        if (service == null || !m_isBuilt)
        {
            return;
        }

        if (m_isThrowableMode)
        {
            if (m_workingThrowable != null)
            {
                service.SetThrowableStyle(m_workingThrowable);
            }
        }
        else
        {
            service.SetCharacterStyles(m_workingStyles);
        }
    }

    /// <summary>저장이 끝난 뒤 호출합니다. 서비스의 확정 값으로 작업 사본을 맞춥니다.</summary>
    public void CommitCurrentValues()
    {
        RevertToCommittedValues();
    }

    /// <summary>저장하지 않은 편집을 버리고 서비스의 확정 값을 다시 읽습니다.</summary>
    public void RevertToCommittedValues()
    {
        if (!m_isBuilt)
        {
            return;
        }

        CrosshairSettingsService service = CrosshairSettingsService.Instance;
        m_workingStyles.Clear();
        m_workingThrowable = null;
        if (service != null)
        {
            if (m_isThrowableMode)
            {
                m_workingThrowable = service.GetThrowableStyle();
            }
            else
            {
                RebuildCharacterButtons(service);
                foreach (KeyValuePair<PlayerbleCharacterId, string> pair in m_characters)
                {
                    m_workingStyles[pair.Key] = service.GetCharacterStyle(pair.Key);
                }
            }
        }

        RefreshAll();
    }

    // 미리보기용 HUD 복제본은 이 화면이 보이는 동안만 둡니다. 다시 열리면 DrawPreview가 새로 만듭니다.
    private void OnDisable()
    {
        m_hudPreview?.Dispose();
        m_hudPreview = null;
    }

    private void OnEnable()
    {
        if (m_isBuilt)
        {
            RefreshAll();
        }
    }

    private void BuildInternal(RectTransform parent, TMP_FontAsset regularFont, TMP_FontAsset boldFont, bool throwableMode)
    {
        if (m_isBuilt || parent == null) return;

        m_isBuilt = true;
        m_isThrowableMode = throwableMode;
        m_regularFont = regularFont;
        m_boldFont = boldFont != null ? boldFont : regularFont;

        string title = throwableMode ? "투척물 조준선" : "캐릭터 조준선";
        string notice = throwableMode
            ? "모든 캐릭터가 같이 쓰는 투척 모드 조준선입니다. 저장하면 바로 적용됩니다."
            : "캐릭터마다 따로 저장되며, 조작 캐릭터를 바꾸면 그 캐릭터의 조준선으로 바뀝니다.";
        CreateText("Page Title", parent, new Vector2(ControlsLeft, -34f), new Vector2(500f, 50f),
            title, 32f, TextAlignmentOptions.Left, m_boldFont);
        CreateText("Notice", parent, new Vector2(ControlsLeft, -82f), new Vector2(700f, 32f),
            notice, 17f, TextAlignmentOptions.Left, m_regularFont, new Color(1f, 1f, 1f, 0.6f));

        float layerTop = -126f;
        if (!throwableMode)
        {
            CreateText("Character Label", parent, new Vector2(ControlsLeft, -126f), new Vector2(160f, 32f),
                "캐릭터", 20f, TextAlignmentOptions.Left, m_boldFont);
            m_characterRow = CreateTopLeftObject("Character Buttons", parent, new Vector2(ControlsLeft, -164f),
                new Vector2(ControlsWidth, 42f)).GetComponent<RectTransform>();
            layerTop = -220f;
        }

        CreateText("Layer Label", parent, new Vector2(ControlsLeft, layerTop), new Vector2(300f, 32f),
            "편집할 조준선", 20f, TextAlignmentOptions.Left, m_boldFont);
        m_layerTop = layerTop;

        if (!throwableMode)
        {
            // 동적 크로스헤어: 켜면 탄퍼짐·반동·조준에 따라 벌어지고 줄어들며(양은 고정값) 비조준·조준을 나누지 않습니다.
            // 끄면 미리보기 크기 그대로 고정되고, 아래 비조준/조준 버튼으로 두 쪽을 따로 정합니다.
            GameObject dynamicRow = CreateTopLeftObject("Dynamic Row", parent, new Vector2(ControlsLeft, layerTop - 38f),
                new Vector2(ControlsWidth, 36f));
            Button box = CreateButton("Dynamic Checkbox", dynamicRow.transform, Vector2.zero, new Vector2(34f, 34f), string.Empty, 15f, IdleColor);
            box.onClick.AddListener(ToggleDynamic);
            m_dynamicCheck = CreateImage("Check", box.transform, new Vector2(7f, -7f), new Vector2(20f, 20f), SelectedColor);
            m_dynamicCheck.raycastTarget = false;
            CreateText("Dynamic Label", dynamicRow.transform, new Vector2(46f, -1f), new Vector2(180f, 32f),
                "동적 크로스헤어", 18f, TextAlignmentOptions.Left, m_boldFont);
            CreateText("Dynamic Description", dynamicRow.transform, new Vector2(220f, -2f), new Vector2(430f, 32f),
                "반동·조준에 따라 벌어지고 줄어듭니다", 15f, TextAlignmentOptions.Left, m_regularFont, new Color(1f, 1f, 1f, 0.6f));

            m_sideRow = CreateTopLeftObject("Side Buttons", parent, new Vector2(ControlsLeft, layerTop - 84f),
                new Vector2(ControlsWidth, 42f)).GetComponent<RectTransform>();
            string[] sideLabels = { "비조준", "조준 (ADS)" };
            for (int i = 0; i < sideLabels.Length; i++)
            {
                int index = i;
                Button button = CreateButton($"Side {i}", m_sideRow, new Vector2(180f * i, 0f),
                    new Vector2(164f, 42f), sideLabels[i], 18f, IdleColor);
                button.onClick.AddListener(() =>
                {
                    m_adsTab = index == 1;
                    RefreshAll();
                });
                m_sideButtonImages[i] = button.GetComponent<Image>();
            }
        }

        // 내부/외부 버튼은 비조준/조준 아래 단계입니다. 줄 위치는 RefreshAll이 동적 여부에 맞춰 옮깁니다.
        m_layerRow = CreateTopLeftObject("Layer Buttons", parent, new Vector2(ControlsLeft, layerTop - 38f),
            new Vector2(ControlsWidth, 42f)).GetComponent<RectTransform>();
        string[] layerLabels = { "내부 (중앙)", "외부 (주변)" };
        for (int i = 0; i < layerLabels.Length; i++)
        {
            int index = i;
            Button button = CreateButton($"Layer {i}", m_layerRow, new Vector2(180f * i, 0f),
                new Vector2(164f, 42f), layerLabels[i], 18f, IdleColor);
            button.onClick.AddListener(() => SelectLayer((Layer)index));
            m_layerButtonImages[i] = button.GetComponent<Image>();
        }

        BuildControls(parent, layerTop - 100f);
        BuildPreview(parent);

        m_selectedLayer = Layer.Main;
        m_selectedCharacterIndex = 0;
        RevertToCommittedValues();
    }

    private void BuildControls(Transform parent, float top)
    {
        // 캐릭터 조준선은 위에 동적 크로스헤어·비조준/조준 줄이 더 있어 편집 칸이 그만큼 아래에서 시작하므로 짧게 둡니다.
        float height = m_isThrowableMode ? 700f : 600f;
        Image panel = CreateImage("Controls Panel", parent, new Vector2(ControlsLeft, top), new Vector2(ControlsWidth, height),
            new Color(0.035f, 0.035f, 0.035f, 0.92f));
        m_controlsPanel = panel.rectTransform;
        Outline outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.16f);
        outline.effectDistance = new Vector2(1f, -1f);
        RectTransform panelRect = panel.rectTransform;

        m_unavailableText = CreateText("Unavailable", panelRect, new Vector2(32f, -24f), new Vector2(586f, 60f),
            "전투 씬에서만 조준선을 설정할 수 있습니다.", 18f, TextAlignmentOptions.Left, m_regularFont,
            new Color(1f, 1f, 1f, 0.6f));

        m_mainRowsRoot = CreateStretchObject("Main Rows", panelRect).GetComponent<RectTransform>();
        m_subRowsRoot = CreateStretchObject("Sub Rows", panelRect).GetComponent<RectTransform>();
        BuildMainRows(m_mainRowsRoot);
        BuildSubRows(m_subRowsRoot);

        Button resetButton = CreateButton("Reset To Default", panelRect, new Vector2(ControlsWidth - 32f - 200f, -(height - 58f)),
            new Vector2(200f, 42f), m_isThrowableMode ? "기본값으로" : "이 캐릭터 기본값으로", 15f, new Color(1f, 1f, 1f, 0.12f));
        resetButton.onClick.AddListener(ResetSelectedToDefault);
    }

    private void BuildMainRows(RectTransform root)
    {
        AddShapeRow(root, m_mainRows, m_mainShapeButtons, new[] { "없음", "점", "원형" },
            index => Edit(s => s.mainShape = (CrosshairController.MainShape)index),
            s => (int)s.mainShape);
        AddSliderRow(root, m_mainRows, "점 크기", 0f, 12f, 0.5f, "0.0",
            s => s.mainSizePixels, (s, v) => s.mainSizePixels = v, s => s.mainShape == CrosshairController.MainShape.Dot);
        AddSliderRow(root, m_mainRows, "원 크기", 2f, 40f, 1f, "0",
            s => s.mainRingSizePixels, (s, v) => s.mainRingSizePixels = v, s => s.mainShape == CrosshairController.MainShape.Ring);
        AddSliderRow(root, m_mainRows, "원 두께", 1f, 6f, 0.5f, "0.0",
            s => s.mainRingThicknessPixels, (s, v) => s.mainRingThicknessPixels = v, s => s.mainShape == CrosshairController.MainShape.Ring);
        AddColorRows(root, m_mainRows, "색상", FillSwatches,
            s => s.mainColor, (s, c) => s.mainColor = c, HasMain);
        AddSliderRow(root, m_mainRows, "테두리 두께", 0f, 4f, 0.5f, "0.0",
            s => s.mainStrokeThicknessPixels, (s, v) => s.mainStrokeThicknessPixels = v, HasMain);
        AddColorRows(root, m_mainRows, "테두리 색", StrokeSwatches,
            s => s.mainStrokeColor, (s, c) => s.mainStrokeColor = c, s => HasMain(s) && s.mainStrokeThicknessPixels > 0f);
    }

    private void BuildSubRows(RectTransform root)
    {
        AddShapeRow(root, m_subRows, m_subShapeButtons, new[] { "없음", "둥근 십자", "사각 십자", "원형", "점" },
            index => Edit(s => s.subShape = (CrosshairController.SubShape)index),
            s => (int)s.subShape);
        AddSliderRow(root, m_subRows, "중앙 간격", 0f, 40f, 1f, "0",
            s => s.centerSpacePixels, (s, v) => s.centerSpacePixels = v, HasSub);
        AddSliderRow(root, m_subRows, "선 길이", 1f, 40f, 1f, "0",
            s => s.subWidthPixels, (s, v) => s.subWidthPixels = v, IsSubCross);
        AddSliderRow(root, m_subRows, "선 두께", 0.5f, 8f, 0.5f, "0.0",
            s => s.subThicknessPixels, (s, v) => s.subThicknessPixels = v, IsSubCross);
        AddSliderRow(root, m_subRows, "모서리 둥글기", 0f, 6f, 0.5f, "0.0",
            s => s.cornerRadiusPixels, (s, v) => s.cornerRadiusPixels = v, s => s.subShape == CrosshairController.SubShape.RoundedCross);
        AddSliderRow(root, m_subRows, "점 크기", 0.5f, 12f, 0.5f, "0.0",
            s => s.subSizePixels, (s, v) => s.subSizePixels = v, s => s.subShape == CrosshairController.SubShape.Dot);
        AddSliderRow(root, m_subRows, "원 크기", 2f, 60f, 1f, "0",
            s => s.subRingSizePixels, (s, v) => s.subRingSizePixels = v, s => s.subShape == CrosshairController.SubShape.Ring);
        AddSliderRow(root, m_subRows, "원 두께", 1f, 6f, 0.5f, "0.0",
            s => s.subRingThicknessPixels, (s, v) => s.subRingThicknessPixels = v, s => s.subShape == CrosshairController.SubShape.Ring);
        AddColorRows(root, m_subRows, "색상", FillSwatches,
            s => s.subColor, (s, c) => s.subColor = c, HasSub);
        AddSliderRow(root, m_subRows, "테두리 두께", 0f, 4f, 0.5f, "0.0",
            s => s.subStrokeThicknessPixels, (s, v) => s.subStrokeThicknessPixels = v, HasSub);
        AddColorRows(root, m_subRows, "테두리 색", StrokeSwatches,
            s => s.subStrokeColor, (s, c) => s.subStrokeColor = c, s => HasSub(s) && s.subStrokeThicknessPixels > 0f);
    }

    /// <summary>라벨과 버튼 여러 개로 하나를 고르는 줄을 추가합니다.</summary>
    private void AddChoiceRow(RectTransform root, List<Row> rows, string label, string[] labels,
        Action<int> onSelect, Func<CrosshairStyle, int> getIndex, Func<CrosshairStyle, bool> isVisible = null)
    {
        GameObject rowObject = CreateTopLeftObject($"{label} Row", root, Vector2.zero, new Vector2(ControlsWidth, RowHeight));
        CreateText("Label", rowObject.transform, new Vector2(32f, -8f), new Vector2(150f, 32f),
            label, 18f, TextAlignmentOptions.Left, m_boldFont);
        var buttonImages = new List<Image>();
        float buttonWidth = labels.Length > 2 ? 108f : 200f;
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i;
            Button button = CreateButton($"Choice {i}", rowObject.transform, new Vector2(182f + (buttonWidth + 6f) * i, -6f),
                new Vector2(buttonWidth, 36f), labels[i], 15f, IdleColor);
            button.onClick.AddListener(() => onSelect(index));
            buttonImages.Add(button.GetComponent<Image>());
        }

        rows.Add(new Row
        {
            Root = rowObject,
            IsVisible = isVisible ?? (_ => true),
            Refresh = style =>
            {
                int selected = getIndex(style);
                for (int i = 0; i < buttonImages.Count; i++)
                {
                    buttonImages[i].color = i == selected ? SelectedColor : IdleColor;
                }
            },
        });
    }

    private static bool HasMain(CrosshairStyle s) => s.mainShape != CrosshairController.MainShape.None;
    private static bool HasSub(CrosshairStyle s) => s.subShape != CrosshairController.SubShape.None;

    private static bool IsSubCross(CrosshairStyle s) =>
        s.subShape == CrosshairController.SubShape.RoundedCross || s.subShape == CrosshairController.SubShape.SquareCross;

    private void AddShapeRow(RectTransform root, List<Row> rows, List<Image> buttonImages, string[] labels,
        Action<int> onSelect, Func<CrosshairStyle, int> getIndex)
    {
        GameObject rowObject = CreateTopLeftObject("Shape Row", root, Vector2.zero, new Vector2(ControlsWidth, RowHeight));
        CreateText("Label", rowObject.transform, new Vector2(32f, -8f), new Vector2(150f, 32f),
            "모양", 18f, TextAlignmentOptions.Left, m_boldFont);
        float buttonWidth = labels.Length > 3 ? 82f : 108f;
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i;
            Button button = CreateButton($"Shape {i}", rowObject.transform, new Vector2(182f + (buttonWidth + 6f) * i, -6f),
                new Vector2(buttonWidth, 36f), labels[i], 15f, IdleColor);
            button.onClick.AddListener(() => onSelect(index));
            buttonImages.Add(button.GetComponent<Image>());
        }

        rows.Add(new Row
        {
            Root = rowObject,
            IsVisible = _ => true,
            Refresh = style =>
            {
                int selected = getIndex(style);
                for (int i = 0; i < buttonImages.Count; i++)
                {
                    buttonImages[i].color = i == selected ? SelectedColor : IdleColor;
                }
            },
        });
    }

    private void AddSliderRow(RectTransform root, List<Row> rows, string label, float min, float max, float step, string format,
        Func<CrosshairStyle, float> getter, Action<CrosshairStyle, float> setter, Func<CrosshairStyle, bool> isVisible,
        Func<float, string> valueText = null)
    {
        GameObject rowObject = CreateTopLeftObject($"{label} Row", root, Vector2.zero, new Vector2(ControlsWidth, RowHeight));
        CreateText("Label", rowObject.transform, new Vector2(32f, -8f), new Vector2(150f, 32f),
            label, 18f, TextAlignmentOptions.Left, m_regularFont);
        TextMeshProUGUI value = CreateText("Value", rowObject.transform, new Vector2(518f, -8f), new Vector2(100f, 32f),
            string.Empty, 18f, TextAlignmentOptions.Right, m_boldFont);
        Slider slider = CreateSlider(rowObject.transform, new Vector2(182f, -12f), new Vector2(320f, 24f), min, max);
        slider.onValueChanged.AddListener(raw =>
        {
            if (m_isRefreshing) return;
            float snapped = step > 0f ? Mathf.Round(raw / step) * step : raw;
            Edit(s => setter(s, snapped));
        });

        rows.Add(new Row
        {
            Root = rowObject,
            IsVisible = isVisible,
            Refresh = style =>
            {
                float v = getter(style);
                slider.SetValueWithoutNotify(v);
                value.text = valueText != null ? valueText(v) : $"{v.ToString(format)} px";
            },
        });
    }

    /// <summary>색 견본 줄과 불투명도 줄을 함께 추가합니다. 불투명도는 색의 알파 값입니다.</summary>
    private void AddColorRows(RectTransform root, List<Row> rows, string label, Color[] swatches,
        Func<CrosshairStyle, Color> getter, Action<CrosshairStyle, Color> setter, Func<CrosshairStyle, bool> isVisible)
    {
        GameObject rowObject = CreateTopLeftObject($"{label} Row", root, Vector2.zero, new Vector2(ControlsWidth, RowHeight));
        CreateText("Label", rowObject.transform, new Vector2(32f, -8f), new Vector2(150f, 32f),
            label, 18f, TextAlignmentOptions.Left, m_regularFont);
        var outlines = new List<Outline>();
        for (int i = 0; i < swatches.Length; i++)
        {
            Color swatch = swatches[i];
            Button button = CreateButton($"Swatch {i}", rowObject.transform, new Vector2(182f + 48f * i, -7f),
                new Vector2(36f, 36f), string.Empty, 1f, swatch);
            Outline swatchOutline = button.gameObject.AddComponent<Outline>();
            swatchOutline.effectColor = SelectedColor;
            swatchOutline.effectDistance = new Vector2(3f, -3f);
            swatchOutline.useGraphicAlpha = false;
            outlines.Add(swatchOutline);
            button.onClick.AddListener(() => Edit(s =>
            {
                Color current = getter(s);
                setter(s, new Color(swatch.r, swatch.g, swatch.b, current.a));
            }));
        }

        rows.Add(new Row
        {
            Root = rowObject,
            IsVisible = isVisible,
            Refresh = style =>
            {
                Color current = getter(style);
                for (int i = 0; i < swatches.Length; i++)
                {
                    outlines[i].enabled = Mathf.Abs(swatches[i].r - current.r) < 0.01f
                        && Mathf.Abs(swatches[i].g - current.g) < 0.01f
                        && Mathf.Abs(swatches[i].b - current.b) < 0.01f;
                }
            },
        });

        GameObject alphaRow = CreateTopLeftObject($"{label} Alpha Row", root, Vector2.zero, new Vector2(ControlsWidth, RowHeight));
        CreateText("Label", alphaRow.transform, new Vector2(32f, -8f), new Vector2(150f, 32f),
            $"{label.Replace(" 색", string.Empty)} 불투명도", 18f, TextAlignmentOptions.Left, m_regularFont);
        TextMeshProUGUI value = CreateText("Value", alphaRow.transform, new Vector2(518f, -8f), new Vector2(100f, 32f),
            string.Empty, 18f, TextAlignmentOptions.Right, m_boldFont);
        Slider slider = CreateSlider(alphaRow.transform, new Vector2(182f, -12f), new Vector2(320f, 24f), 0f, 1f);
        slider.onValueChanged.AddListener(raw =>
        {
            if (m_isRefreshing) return;
            float alpha = Mathf.Round(raw * 100f) / 100f;
            Edit(s =>
            {
                Color current = getter(s);
                current.a = alpha;
                setter(s, current);
            });
        });

        rows.Add(new Row
        {
            Root = alphaRow,
            IsVisible = isVisible,
            Refresh = style =>
            {
                float alpha = getter(style).a;
                slider.SetValueWithoutNotify(alpha);
                value.text = $"{alpha * 100f:0}%";
            },
        });
    }

    /// <summary>
    /// 편집 줄의 변경을 적용합니다. 외곽의 조준 탭이면 조준 외곽을 담은 보기용 사본을 고친 뒤 조준 외곽에 되돌려 씁니다.
    /// </summary>
    /// <remarks>편집 줄은 모두 비조준 외곽 필드를 읽고 쓰므로, 같은 줄로 조준 외곽을 고치려면 이 우회가 필요합니다.</remarks>
    private void Edit(Action<CrosshairStyle> change)
    {
        CrosshairStyle style = CurrentStyle;
        if (style == null)
        {
            return;
        }

        if (IsEditingAds)
        {
            CrosshairStyle view = style.CreateAdsView();
            change(view);
            style.StoreAdsView(view);
        }
        else
        {
            change(style);
        }

        ApplyPendingValues();
        RefreshAll();
    }

    /// <summary>지금 조준(ADS) 쪽 조준선(내부·외부)을 편집하고 있는지 여부입니다. 동적 크로스헤어를 켜면 항상 거짓입니다.</summary>
    private bool IsEditingAds => !m_isThrowableMode && m_adsTab
        && CurrentStyle != null && !CurrentStyle.subDynamic;

    /// <summary>편집 줄과 미리보기에 보여 줄 스타일입니다. 조준 쪽 편집 중이면 조준 내부·외부를 담은 보기용 사본입니다.</summary>
    private CrosshairStyle DisplayStyle
    {
        get
        {
            CrosshairStyle style = CurrentStyle;
            return style != null && IsEditingAds ? style.CreateAdsView() : style;
        }
    }

    private void SelectLayer(Layer layer)
    {
        m_selectedLayer = layer;
        RefreshAll();
    }

    private void SelectCharacter(int index)
    {
        m_selectedCharacterIndex = index;
        RefreshAll();
    }

    private void ResetSelectedToDefault()
    {
        CrosshairSettingsService service = CrosshairSettingsService.Instance;
        if (service == null)
        {
            return;
        }

        if (m_isThrowableMode)
        {
            m_workingThrowable = service.GetDefaultThrowableStyle();
        }
        else if (m_selectedCharacterIndex < m_characters.Count)
        {
            PlayerbleCharacterId id = m_characters[m_selectedCharacterIndex].Key;
            m_workingStyles[id] = service.GetDefaultCharacterStyle(id);
        }

        ApplyPendingValues();
        RefreshAll();
    }

    private CrosshairStyle CurrentStyle
    {
        get
        {
            if (m_isThrowableMode)
            {
                return m_workingThrowable;
            }

            if (m_selectedCharacterIndex < 0 || m_selectedCharacterIndex >= m_characters.Count)
            {
                return null;
            }

            return m_workingStyles.TryGetValue(m_characters[m_selectedCharacterIndex].Key, out CrosshairStyle style) ? style : null;
        }
    }

    private void RebuildCharacterButtons(CrosshairSettingsService service)
    {
        List<KeyValuePair<PlayerbleCharacterId, string>> characters = service.GetCharacters();
        bool same = characters.Count == m_characters.Count;
        for (int i = 0; same && i < characters.Count; i++)
        {
            same = characters[i].Key == m_characters[i].Key;
        }

        if (same)
        {
            return;
        }

        m_characters.Clear();
        m_characters.AddRange(characters);
        m_characterButtonImages.Clear();
        for (int i = m_characterRow.childCount - 1; i >= 0; i--)
        {
            Destroy(m_characterRow.GetChild(i).gameObject);
        }

        for (int i = 0; i < m_characters.Count; i++)
        {
            int index = i;
            Button button = CreateButton($"Character {m_characters[i].Key}", m_characterRow, new Vector2(180f * i, 0f),
                new Vector2(164f, 42f), m_characters[i].Value, 19f, IdleColor);
            button.onClick.AddListener(() => SelectCharacter(index));
            m_characterButtonImages.Add(button.GetComponent<Image>());
        }

        m_selectedCharacterIndex = Mathf.Clamp(m_selectedCharacterIndex, 0, Mathf.Max(0, m_characters.Count - 1));
    }

    /// <summary>동적 크로스헤어를 켜고 끕니다. 켜면 비조준/조준 구분이 없어지므로 비조준 쪽으로 돌아갑니다.</summary>
    private void ToggleDynamic()
    {
        CrosshairStyle style = CurrentStyle;
        if (style == null)
        {
            return;
        }

        style.subDynamic = !style.subDynamic;
        m_adsTab = false;
        ApplyPendingValues();
        RefreshAll();
    }

    /// <summary>
    /// 동적 크로스헤어 체크와 비조준/조준 버튼을 맞추고, 그 아래 내부/외부 버튼과 편집 칸을 위아래로 옮깁니다.
    /// 동적을 켜면 비조준/조준 줄이 사라지고 그 자리만큼 위로 붙습니다.
    /// </summary>
    private void LayoutHeader(CrosshairStyle style)
    {
        if (m_isThrowableMode)
        {
            return;
        }

        bool dynamic = style == null || style.subDynamic;
        m_dynamicCheck.enabled = dynamic;
        m_sideRow.gameObject.SetActive(!dynamic);
        for (int i = 0; i < m_sideButtonImages.Length; i++)
        {
            m_sideButtonImages[i].color = i == (m_adsTab ? 1 : 0) ? SelectedColor : IdleColor;
        }

        float layerY = m_layerTop - (dynamic ? 84f : 134f);
        m_layerRow.anchoredPosition = new Vector2(m_layerRow.anchoredPosition.x, layerY);
        m_controlsPanel.anchoredPosition = new Vector2(m_controlsPanel.anchoredPosition.x, layerY - 62f);
    }

    private void RefreshAll()
    {
        CrosshairStyle style = CurrentStyle;
        bool available = style != null;
        m_unavailableText.gameObject.SetActive(!available);

        for (int i = 0; i < m_characterButtonImages.Count; i++)
        {
            m_characterButtonImages[i].color = i == m_selectedCharacterIndex ? SelectedColor : IdleColor;
        }

        for (int i = 0; i < m_layerButtonImages.Length; i++)
        {
            m_layerButtonImages[i].color = i == (int)m_selectedLayer ? SelectedColor : IdleColor;
        }

        LayoutHeader(style);

        m_mainRowsRoot.gameObject.SetActive(available && m_selectedLayer == Layer.Main);
        m_subRowsRoot.gameObject.SetActive(available && m_selectedLayer == Layer.Sub);
        if (!available)
        {
            DrawPreview(null);
            return;
        }

        CrosshairStyle display = DisplayStyle;
        m_isRefreshing = true;
        LayoutRows(m_selectedLayer == Layer.Main ? m_mainRows : m_subRows, display);
        m_isRefreshing = false;
        DrawPreview(display);
    }

    /// <summary>현재 모양에 쓰이는 줄만 위에서부터 차례로 보이게 배치합니다.</summary>
    private static void LayoutRows(List<Row> rows, CrosshairStyle style)
    {
        float y = -16f;
        foreach (Row row in rows)
        {
            bool visible = row.IsVisible(style);
            row.Root.SetActive(visible);
            if (!visible) continue;

            ((RectTransform)row.Root.transform).anchoredPosition = new Vector2(0f, y);
            row.Refresh(style);
            y -= RowHeight;
        }
    }

    private void BuildPreview(Transform parent)
    {
        Image preview = CreateImage("Crosshair Preview", parent, new Vector2(1130f, -132f), new Vector2(650f, 684f),
            new Color(0.015f, 0.018f, 0.02f, 0.96f));
        Outline outline = preview.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.2f);
        outline.effectDistance = new Vector2(1f, -1f);
        RectTransform previewRect = preview.rectTransform;
        CreateText("Preview Title", previewRect, new Vector2(28f, -24f), new Vector2(320f, 34f),
            "조준선 미리보기", 20f, TextAlignmentOptions.Left, m_boldFont);
        m_previewProfileLabel = CreateText("Preview Profile", previewRect, new Vector2(350f, -24f), new Vector2(270f, 34f),
            string.Empty, 18f, TextAlignmentOptions.Right, m_regularFont, new Color(1f, 1f, 1f, 0.65f));

        Image field = CreateImage("Preview Field", previewRect, new Vector2(25f, -78f), new Vector2(600f, 560f),
            new Color(0.32f, 0.34f, 0.33f, 1f));
        GameObject imageObject = CreateStretchObject("Preview Image", field.transform);
        m_previewImage = imageObject.AddComponent<RawImage>();
        m_previewImage.raycastTarget = false;
        m_previewImage.enabled = false;

        m_previewZoomLabel = CreateText("Preview Zoom", previewRect, new Vector2(25f, -646f), new Vector2(600f, 24f),
            string.Empty, 16f, TextAlignmentOptions.Center, m_regularFont, new Color(1f, 1f, 1f, 0.5f));
    }

    /// <summary>
    /// 미리보기를 그립니다. 실제 조준선 HUD를 복제해 게임 화면과 같은 크기·간격으로 그립니다.
    /// 캐릭터 조준선은 그 캐릭터가 쏘지 않고 있을 때의 간격이고, 조준(ADS) 외곽을 고치는 중이면 조준을 마친 상태입니다.
    /// </summary>
    private void DrawPreview(CrosshairStyle style)
    {
        if (m_previewImage == null)
        {
            return;
        }

        m_previewProfileLabel.text = m_isThrowableMode
            ? "모든 캐릭터 공용"
            : m_selectedCharacterIndex < m_characters.Count ? m_characters[m_selectedCharacterIndex].Value : string.Empty;

        // 저장·되돌리기는 화면이 꺼져 있을 때도 값을 새로 그립니다. 그때 복제본을 만들면 OnDisable이 다시 오지 않아 남으므로 만들지 않습니다.
        CrosshairSettingsService service = CrosshairSettingsService.Instance;
        if (!isActiveAndEnabled || style == null || service == null || service.Crosshair == null)
        {
            m_hudPreview?.Clear();
            m_previewZoomLabel.text = string.Empty;
            return;
        }

        m_hudPreview ??= new CrosshairHudPreview(service.Crosshair, m_previewImage);
        CrosshairStyle stored = CurrentStyle;
        bool ads = IsEditingAds;
        AimController aim = !m_isThrowableMode && m_selectedCharacterIndex < m_characters.Count
            ? service.FindAimController(m_characters[m_selectedCharacterIndex].Key)
            : null;

        bool drawn = m_hudPreview.Render(clone =>
        {
            if (aim != null)
            {
                aim.ApplyCrosshairPreview(clone, stored, ads);
                return;
            }

            stored.ApplyTo(clone, true);
            clone.SetShotRecoilPulseEnabled(false);
            clone.ClearShotRecoilPulse();
            clone.SetSpread(0f, SpreadDistribution.Gaussian, 3f, 60f, true);
        });

        m_previewZoomLabel.text = !drawn
            ? string.Empty
            : m_isThrowableMode
                ? "게임 화면과 같은 크기입니다."
                : stored.subDynamic
                    ? "게임 화면과 같은 크기입니다. 쏘지 않을 때 모습이며 반동·조준에 따라 움직입니다."
                    : ads
                        ? "게임 화면과 같은 크기입니다. 조준 중에는 항상 이 모습입니다."
                        : "게임 화면과 같은 크기입니다. 비조준 중에는 항상 이 모습입니다.";
    }

    private Slider CreateSlider(Transform parent, Vector2 position, Vector2 size, float min, float max)
    {
        GameObject sliderObject = CreateTopLeftObject("Slider", parent, position, size);
        Slider slider = sliderObject.AddComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;

        Image background = CreateStretchImage("Background", sliderObject.transform, new Color(1f, 1f, 1f, 0.13f));
        background.rectTransform.offsetMin = new Vector2(0f, 8f);
        background.rectTransform.offsetMax = new Vector2(0f, -8f);

        GameObject fillArea = CreateStretchObject("Fill Area", sliderObject.transform);
        RectTransform fillAreaRect = (RectTransform)fillArea.transform;
        fillAreaRect.offsetMin = new Vector2(0f, 8f);
        fillAreaRect.offsetMax = new Vector2(-12f, -8f);
        Image fill = CreateStretchImage("Fill", fillArea.transform, new Color(0.95f, 0.08f, 0.08f, 1f));

        GameObject handleArea = CreateStretchObject("Handle Slide Area", sliderObject.transform);
        RectTransform handleAreaRect = (RectTransform)handleArea.transform;
        handleAreaRect.offsetMin = new Vector2(8f, 0f);
        handleAreaRect.offsetMax = new Vector2(-8f, 0f);
        Image handle = CreateCenteredImage("Handle", handleArea.transform);
        handle.rectTransform.sizeDelta = new Vector2(18f, 18f);
        handle.color = Color.white;

        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        return slider;
    }

    private Button CreateButton(string name, Transform parent, Vector2 position, Vector2 size, string label, float fontSize,
        Color backgroundColor)
    {
        GameObject buttonObject = CreateTopLeftObject(name, parent, position, size);
        Image image = buttonObject.AddComponent<Image>();
        image.color = backgroundColor;
        Button button = buttonObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.pressedColor = new Color(0.65f, 0.65f, 0.65f, 1f);
        colors.selectedColor = Color.white;
        button.colors = colors;
        if (!string.IsNullOrEmpty(label))
        {
            CreateStretchText("Label", buttonObject.transform, label, fontSize, TextAlignmentOptions.Center, m_regularFont);
        }

        return button;
    }

    private TextMeshProUGUI CreateText(string name, Transform parent, Vector2 position, Vector2 size, string value,
        float fontSize, TextAlignmentOptions alignment, TMP_FontAsset font, Color? color = null)
    {
        GameObject textObject = CreateTopLeftObject(name, parent, position, size);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        ConfigureText(text, value, fontSize, alignment, font, color ?? Color.white);
        return text;
    }

    private static TextMeshProUGUI CreateStretchText(string name, Transform parent, string value, float fontSize,
        TextAlignmentOptions alignment, TMP_FontAsset font)
    {
        GameObject textObject = CreateStretchObject(name, parent);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        ConfigureText(text, value, fontSize, alignment, font, Color.white);
        return text;
    }

    private static void ConfigureText(TextMeshProUGUI text, string value, float fontSize, TextAlignmentOptions alignment,
        TMP_FontAsset font, Color color)
    {
        text.text = value;
        text.font = font;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
    }

    private static Image CreateImage(string name, Transform parent, Vector2 position, Vector2 size, Color color)
    {
        GameObject imageObject = CreateTopLeftObject(name, parent, position, size);
        Image image = imageObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static Image CreateCenteredImage(string name, Transform parent)
    {
        GameObject imageObject = new(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)imageObject.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(4f, 4f);
        Image image = imageObject.AddComponent<Image>();
        image.raycastTarget = false;
        return image;
    }

    private static Image CreateStretchImage(string name, Transform parent, Color color)
    {
        GameObject imageObject = CreateStretchObject(name, parent);
        Image image = imageObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static GameObject CreateStretchObject(string name, Transform parent)
    {
        GameObject child = new(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)child.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        return child;
    }

    private static GameObject CreateTopLeftObject(string name, Transform parent, Vector2 position, Vector2 size)
    {
        GameObject child = new(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)child.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        return child;
    }
}
