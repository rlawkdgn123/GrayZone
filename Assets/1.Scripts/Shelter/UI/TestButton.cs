using System;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// 캐릭터 후보 목록에서 초상화와 부상 아이콘을 표시하는 임시 행 버튼
/// </summary>
public class TestButton : MonoBehaviour, ICharacterListItemView
{
    [Header("Catalogs")]
    [FormerlySerializedAs("npcCatalog")]
    [SerializeField] private CharacterPortraitCatalog characterCatalog;
    [SerializeField] private CharacterInjuryIconCatalog injuryIconCatalog;
    [Tooltip("선택: 캐릭터별 부상 이미지. 현재 부상 상태의 이미지가 있으면 기본 초상화 대신 표시합니다.")]
    [SerializeField] private CharacterInjuryPortraitCatalog characterInjuryCatalog;

    [Header("Images")]
    [SerializeField] private Image portraitImage;
    [SerializeField] private Image injuryImage;
    [SerializeField] private Button button;

    [Header("Frame (선택 — 없으면 무시)")]
    [Tooltip("후보 행의 배경 프레임 이미지. 부상 상태에 따라 아래 스프라이트로 바뀝니다.")]
    [SerializeField] private Image frameImage;
    [Tooltip("기본 프레임 스프라이트. 비우면 frameImage에 처음 들어 있던 스프라이트를 사용합니다.")]
    [SerializeField] private Sprite defaultFrameSprite;
    [Tooltip("치명상일 때 사용할 프레임 스프라이트")]
    [SerializeField] private Sprite criticalFrameSprite;

    [Header("Injury State Text (선택 — 없으면 무시)")]
    [Tooltip("부상 상태를 표시할 텍스트. 버튼 아래 등 원하는 위치에 둔 TMP를 연결합니다. 색은 Character Injury Catalog의 상태별 색을 사용합니다.")]
    [SerializeField] private TextMeshProUGUI injuryStateText;
    [SerializeField] private string injuryStateFormat = "{0}";

    private Sprite initialFrameSprite;
    private bool hasInitialFrameSprite;
    private Color defaultInjuryStateTextColor;
    private bool hasDefaultInjuryStateTextColor;
    private string runtimeId;
    private CharacterInjuryState injuryState;
    private Action<string> clicked;

    private void Awake()
    {
        CacheReferences();
    }

    private void Reset()
    {
        CacheReferences();
    }

    /// <summary>
    /// 캐릭터 데이터를 버튼 행에 표시하고 클릭 콜백을 연결
    /// </summary>
    /// <param name="character">표시할 셸터 캐릭터 런타임 데이터</param>
    /// <param name="onClicked">클릭 시 캐릭터 런타임 ID를 전달할 콜백</param>
    public void Bind(ShelterMemberRuntimeData character, Action<string> onClicked)
    {
        Bind(character, true, onClicked);
    }

    public void Bind(
        ShelterMemberRuntimeData character,
        bool isInteractable,
        Action<string> onClicked)
    {
        CacheReferences();

        if (character == null || string.IsNullOrWhiteSpace(character.RuntimeId))
        {
            Clear();
            return;
        }

        gameObject.SetActive(true);
        runtimeId = character.RuntimeId.Trim();
        injuryState = character.InjuryState;
        clicked = onClicked;

        Sprite portrait = characterInjuryCatalog != null
            ? characterInjuryCatalog.GetIcon(character.DefinitionId, injuryState)
            : null;
        if (portrait == null && characterCatalog != null)
            portrait = characterCatalog.GetPortrait(character.DefinitionId);
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

        ApplyFrame(injuryState);
        ApplyInjuryStateText(injuryState);

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(HandleClick);
            button.interactable = isInteractable && !string.IsNullOrWhiteSpace(runtimeId);
        }
    }

    /// <summary>
    /// 표시 중인 캐릭터 데이터와 버튼 클릭 상태를 비우고 비활성화
    /// </summary>
    public void Clear()
    {
        CacheReferences();

        runtimeId = null;
        injuryState = CharacterInjuryState.Normal;
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

        ApplyFrame(CharacterInjuryState.Normal);
        ClearInjuryStateText();

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.interactable = false;
        }

        gameObject.SetActive(false);
    }

    // 치명상이면 치명상 프레임, 그 외에는 기본 프레임을 넣는다.
    private void ApplyFrame(CharacterInjuryState state)
    {
        if (frameImage == null)
            return;

        if (!hasInitialFrameSprite)
        {
            initialFrameSprite = frameImage.sprite;
            hasInitialFrameSprite = true;
        }

        Sprite baseSprite = defaultFrameSprite != null ? defaultFrameSprite : initialFrameSprite;
        Sprite sprite = state == CharacterInjuryState.Critical && criticalFrameSprite != null
            ? criticalFrameSprite
            : baseSprite;

        if (frameImage.sprite != sprite)
            frameImage.sprite = sprite;
    }

    // 부상 상태 글자와 카탈로그 색을 넣는다. 텍스트가 버튼 밖(형제 등)에 있어도 보이도록 직접 켠다.
    private void ApplyInjuryStateText(CharacterInjuryState state)
    {
        if (injuryStateText == null)
            return;

        CacheDefaultInjuryStateTextColor();

        injuryStateText.text = string.Format(injuryStateFormat, CharacterInjuryStateText.ToWord(state));

        Color color = defaultInjuryStateTextColor;
        if (characterInjuryCatalog != null
            && characterInjuryCatalog.TryGetTextColor(state, out Color stateColor))
            color = stateColor;

        if (injuryStateText.color != color)
            injuryStateText.color = color;

        if (!injuryStateText.gameObject.activeSelf)
            injuryStateText.gameObject.SetActive(true);
    }

    // 빈 행이 되면 글자를 지우고 숨긴다. 텍스트가 버튼 밖에 있으면 버튼을 꺼도 함께 꺼지지 않기 때문이다.
    private void ClearInjuryStateText()
    {
        if (injuryStateText == null)
            return;

        CacheDefaultInjuryStateTextColor();
        injuryStateText.text = string.Empty;
        injuryStateText.color = defaultInjuryStateTextColor;

        if (injuryStateText.gameObject.activeSelf)
            injuryStateText.gameObject.SetActive(false);
    }

    private void CacheDefaultInjuryStateTextColor()
    {
        if (hasDefaultInjuryStateTextColor || injuryStateText == null)
            return;

        defaultInjuryStateTextColor = injuryStateText.color;
        hasDefaultInjuryStateTextColor = true;
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
