using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 캐릭터별 조준선과 공용 투척물 조준선의 사용자 설정을 실제 HUD에 적용합니다. 조작 캐릭터가 바뀌면 그 캐릭터의 값으로 바꿉니다.
/// </summary>
/// <remarks>
/// 값의 출처는 세 단계입니다.
/// 1) 사용자가 저장한 값: <see cref="GameSettingManager"/>가 settings.json의 crosshair 섹션으로 관리합니다.
/// 2) 기본값 템플릿: <see cref="DefaultCrosshairSettings"/> 에셋입니다. 기본값 버튼과, 저장값이 없는 캐릭터의 첫 값이 여기서 옵니다.
/// 3) 템플릿에 항목이 없을 때만 씬 값: 조준선 HUD 값에 그 캐릭터 <see cref="AimController"/>의 외부 형태·중앙 간격·링 지름을 얹은 값,
///    투척물은 <see cref="ExplosiveProjectileShooter"/> 값입니다.
///
/// 외부 형태·중앙 간격·링 지름은 AimController가 캐릭터마다 소유하고 조작 중 매 프레임 HUD에 다시 맞추므로, 이 세 값은
/// AimController에 씁니다. 나머지는 HUD에 직접 씁니다. 투척 모드가 HUD를 덮고 있으면 화면은 그대로 두고 끝난 뒤 돌아갈 값만 바꿉니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class CrosshairSettingsService : MonoBehaviour
{
    [Header("References")]
    [Tooltip("캐릭터들이 같이 쓰는 조준선 HUD입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private CrosshairController m_crosshair;

    [Tooltip("조작 캐릭터를 제공하는 SquadManager입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private SquadManager m_squadManager;

    private readonly Dictionary<PlayerbleCharacterId, CrosshairStyle> m_sceneCharacterStyles =
        new Dictionary<PlayerbleCharacterId, CrosshairStyle>();
    private CrosshairStyle m_sceneHudStyle;
    private CrosshairStyle m_sceneThrowableStyle;
    private PlayerbleCharacterId? m_appliedCharacter;

    private static bool s_sceneHookRegistered;

    /// <summary>현재 씬의 서비스입니다. 조준선 HUD가 없는 씬에서는 null입니다.</summary>
    public static CrosshairSettingsService Instance { get; private set; }

    // 씬에 따로 배치하지 않아도 조준선 HUD가 있는 모든 씬에서 동작하도록, 씬을 불러온 뒤 HUD 오브젝트에 붙입니다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachOnStartup()
    {
        AttachToLoadedScene();
        if (!s_sceneHookRegistered)
        {
            s_sceneHookRegistered = true;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, _) => AttachToLoadedScene();
        }
    }

    private static void AttachToLoadedScene()
    {
        if (Instance != null)
        {
            return;
        }

        CrosshairController crosshair = FindFirstObjectByType<CrosshairController>(FindObjectsInactive.Include);
        if (crosshair != null && crosshair.GetComponent<CrosshairSettingsService>() == null)
        {
            crosshair.gameObject.AddComponent<CrosshairSettingsService>();
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[CrosshairSettingsService] 씬에 이미 인스턴스가 있어 이 컴포넌트를 끕니다.", this);
            enabled = false;
            return;
        }

        Instance = this;
        ResolveReferences();
        CaptureSceneFallbacks();
    }

    private void Start()
    {
        // GameSettingManager는 Awake에서 파일을 읽습니다. 실행 순서에 기대지 않도록 적용은 Start에서 합니다.
        ApplyCharacterStylesToAimControllers();
        ApplyThrowableStyleToShooters();
        m_appliedCharacter = null;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void LateUpdate()
    {
        PlayerbleUnitData data = m_squadManager != null ? m_squadManager.PlayerSquadMemberData : null;
        if (data != null && m_appliedCharacter != data.CharacterId)
        {
            ApplyCharacterStyle(data.CharacterId);
        }
    }

    /// <summary>설정 화면에 보여줄 캐릭터 목록(식별자와 표시 이름)입니다. 스쿼드 순서를 따릅니다.</summary>
    public List<KeyValuePair<PlayerbleCharacterId, string>> GetCharacters()
    {
        var result = new List<KeyValuePair<PlayerbleCharacterId, string>>();
        if (m_squadManager == null || m_squadManager.PlayerDataSources == null)
        {
            return result;
        }

        foreach (PlayerbleUnitData data in m_squadManager.PlayerDataSources)
        {
            if (data != null && data.CharacterId != PlayerbleCharacterId.Unknown)
            {
                result.Add(new KeyValuePair<PlayerbleCharacterId, string>(data.CharacterId, data.DisplayName));
            }
        }

        return result;
    }

    /// <summary>캐릭터 기본 조준선입니다. 기본값 템플릿을 매번 다시 읽고, 템플릿에 없으면 씬 값입니다.</summary>
    public CrosshairStyle GetDefaultCharacterStyle(PlayerbleCharacterId characterId)
    {
        DefaultCrosshairSettings defaults = ResolveDefaults();
        if (defaults != null && defaults.TryGetCharacterStyle(characterId, out CrosshairStyle style))
        {
            return style;
        }

        return m_sceneCharacterStyles.TryGetValue(characterId, out CrosshairStyle sceneStyle)
            ? sceneStyle.Clone()
            : m_sceneHudStyle.Clone();
    }

    /// <summary>투척물 기본 조준선입니다. 기본값 템플릿을 매번 다시 읽고, 템플릿에 없으면 씬 값입니다.</summary>
    public CrosshairStyle GetDefaultThrowableStyle()
    {
        DefaultCrosshairSettings defaults = ResolveDefaults();
        return defaults != null && defaults.TryGetThrowableStyle(out CrosshairStyle style)
            ? style
            : m_sceneThrowableStyle.Clone();
    }

    /// <summary>캐릭터의 현재 조준선입니다. 저장값이 없으면 기본값입니다.</summary>
    public CrosshairStyle GetCharacterStyle(PlayerbleCharacterId characterId)
    {
        SettingData.CrosshairSettingData saved = ReadSaved();
        if (saved != null && saved.characters != null)
        {
            foreach (SettingData.CharacterCrosshairEntry entry in saved.characters)
            {
                if (entry != null && entry.style != null && entry.characterId == characterId)
                {
                    // 벌어짐 방식은 설정 화면에서 고치지 않는 캐릭터 고유값이라 저장값이 아니라 기본값을 따릅니다.
                    // 예전 저장 파일에 남은 값(예: 청솔 WeaponMaxSpread)이 기본값 변경을 가리지 않게 합니다.
                    CrosshairStyle style = entry.style.Clone();
                    style.subSpreadMode = GetDefaultCharacterStyle(characterId).subSpreadMode;
                    return style;
                }
            }
        }

        return GetDefaultCharacterStyle(characterId);
    }

    /// <summary>투척물 조준선의 현재 값입니다. 저장값이 없으면 기본값입니다.</summary>
    public CrosshairStyle GetThrowableStyle()
    {
        SettingData.CrosshairSettingData saved = ReadSaved();
        return saved != null && saved.hasThrowable && saved.throwable != null
            ? saved.throwable.Clone()
            : GetDefaultThrowableStyle();
    }

    /// <summary>
    /// 캐릭터 조준선 값을 확정하고 바로 적용합니다. 파일 저장은 <see cref="GameSettingManager.SaveSettings"/>가 맡습니다.
    /// </summary>
    public void SetCharacterStyles(IReadOnlyDictionary<PlayerbleCharacterId, CrosshairStyle> styles)
    {
        GameSettingManager manager = GameSettingManager.Instance;
        if (styles == null || manager == null)
        {
            return;
        }

        SettingData.CrosshairSettingData saved = manager.GetCrosshairSettings();
        foreach (KeyValuePair<PlayerbleCharacterId, CrosshairStyle> pair in styles)
        {
            if (pair.Value == null) continue;

            saved.characters.RemoveAll(entry => entry == null || entry.characterId == pair.Key);
            saved.characters.Add(new SettingData.CharacterCrosshairEntry { characterId = pair.Key, style = pair.Value.Clone() });
        }

        manager.SetCrosshairSettings(saved);
        ApplyCharacterStylesToAimControllers();
        m_appliedCharacter = null;
    }

    /// <summary>
    /// 투척물 조준선 값을 확정하고 모든 대원의 투척 모드에 적용합니다. 파일 저장은 <see cref="GameSettingManager.SaveSettings"/>가 맡습니다.
    /// </summary>
    public void SetThrowableStyle(CrosshairStyle style)
    {
        GameSettingManager manager = GameSettingManager.Instance;
        if (style == null || manager == null)
        {
            return;
        }

        SettingData.CrosshairSettingData saved = manager.GetCrosshairSettings();
        saved.hasThrowable = true;
        saved.throwable = style.Clone();
        manager.SetCrosshairSettings(saved);
        ApplyThrowableStyleToShooters();
    }

    private void ApplyCharacterStyle(PlayerbleCharacterId characterId)
    {
        m_appliedCharacter = characterId;
        if (m_crosshair == null)
        {
            return;
        }

        CrosshairStyle style = GetCharacterStyle(characterId);
        if (!ExplosiveProjectileShooter.TryReplaceCrosshairBaseline(m_crosshair, style))
        {
            style.ApplyTo(m_crosshair, false);
        }
    }

    /// <summary>
    /// 캐릭터마다 AimController가 소유한 세 값(외부 형태, 중앙 간격, 링 지름)을 현재 값으로 바꿉니다.
    /// AimController가 조작 중일 때 이 값을 HUD에 다시 맞추므로, HUD에만 쓰면 다음 프레임에 되돌아갑니다.
    /// </summary>
    /// <summary>
    /// <see cref="GameSettingManager"/>에 들어 있는 조준선 값(캐릭터별, 투척물)을 다시 HUD와 대원에 적용합니다.
    /// 설정 화면의 '되돌리기'가 조준선 값을 통째로 바꾼 뒤 부릅니다.
    /// </summary>
    public void ReapplyFromSettings()
    {
        ApplyCharacterStylesToAimControllers();
        ApplyThrowableStyleToShooters();
        m_appliedCharacter = null;
    }

    private void ApplyCharacterStylesToAimControllers()
    {
        foreach (KeyValuePair<PlayerbleCharacterId, string> pair in GetCharacters())
        {
            AimController aim = FindAimController(pair.Key);
            if (aim == null) continue;

            CrosshairStyle style = GetCharacterStyle(pair.Key);
            aim.CrosshairSubShape = style.subShape;
            aim.CrosshairCenterSpacePixels = style.centerSpacePixels;
            aim.CrosshairSubRingBaseDiameterPixels = style.subRingSizePixels;

            // 비조준/조준 조준선(내부·외부)과 동적 여부는 AimController가 조준 진행도에 따라 골라 HUD에 적용합니다.
            aim.SetCrosshairStyle(style);
        }
    }

    private void ApplyThrowableStyleToShooters()
    {
        CrosshairStyle style = GetThrowableStyle();
        foreach (ExplosiveProjectileShooter shooter in FindObjectsByType<ExplosiveProjectileShooter>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            shooter.SetThrowCrosshairStyle(style);
        }
    }

    /// <summary>템플릿에 항목이 없을 때 쓸 씬 값을, 이 서비스가 HUD를 바꾸기 전에 잡아 둡니다.</summary>
    private void CaptureSceneFallbacks()
    {
        m_sceneHudStyle = m_crosshair != null ? CrosshairStyle.Capture(m_crosshair) : new CrosshairStyle();
        ExplosiveProjectileShooter shooter = FindFirstObjectByType<ExplosiveProjectileShooter>(FindObjectsInactive.Include);
        m_sceneThrowableStyle = shooter != null && shooter.ThrowCrosshairStyle != null
            ? shooter.ThrowCrosshairStyle
            : m_sceneHudStyle.Clone();

        m_sceneCharacterStyles.Clear();
        foreach (KeyValuePair<PlayerbleCharacterId, string> pair in GetCharacters())
        {
            CrosshairStyle style = m_sceneHudStyle.Clone();
            AimController aim = FindAimController(pair.Key);
            if (aim != null)
            {
                style.subShape = aim.CrosshairSubShape;
                style.centerSpacePixels = aim.CrosshairCenterSpacePixels;
                style.subRingSizePixels = aim.CrosshairSubRingBaseDiameterPixels;
                style.subSpreadMode = aim.CrosshairSpreadModeSetting;
            }

            m_sceneCharacterStyles[pair.Key] = style;
        }
    }

    /// <summary>캐릭터들이 같이 쓰는 실제 조준선 HUD입니다. 설정 화면 미리보기가 이것을 복제합니다.</summary>
    public CrosshairController Crosshair => m_crosshair;

    /// <summary>캐릭터의 AimController입니다. 설정 화면 미리보기가 그 캐릭터의 간격 계산을 빌려 씁니다.</summary>
    public AimController FindAimController(PlayerbleCharacterId characterId)
    {
        if (m_squadManager == null || m_squadManager.PlayerDataSources == null)
        {
            return null;
        }

        foreach (PlayerbleUnitData data in m_squadManager.PlayerDataSources)
        {
            if (data != null && data.CharacterId == characterId)
            {
                return data.GetComponentInChildren<AimController>(true);
            }
        }

        return null;
    }

    private static SettingData.CrosshairSettingData ReadSaved()
    {
        return GameSettingManager.Instance != null ? GameSettingManager.Instance.GetCrosshairSettings() : null;
    }

    private static DefaultCrosshairSettings ResolveDefaults()
    {
        return GameSettingManager.Instance != null
            ? GameSettingManager.Instance.DefaultCrosshairSettings
            : DefaultCrosshairSettings.LoadFromResources();
    }

    private void ResolveReferences()
    {
        if (m_crosshair == null)
        {
            m_crosshair = GetComponentInChildren<CrosshairController>(true);
        }

        if (m_crosshair == null)
        {
            m_crosshair = FindFirstObjectByType<CrosshairController>(FindObjectsInactive.Include);
        }

        if (m_squadManager == null)
        {
            m_squadManager = FindFirstObjectByType<SquadManager>(FindObjectsInactive.Include);
        }
    }
}
