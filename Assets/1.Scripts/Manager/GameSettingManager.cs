using System.IO;
using UnityEngine;
using UnityEngine.Audio;

public class GameSettingManager : MonoBehaviour
{
    [System.Serializable]
    public class SettingSnapshot
    {
        public int gameplaySettingsVersion = 1;
        public int width = 1920;
        public int height = 1080;
        public bool fullscreen = true;
        // UnityEngine.FullScreenMode 값입니다. -1이면 fullscreen 값으로 정합니다(이 값이 없던 씬·파일 호환).
        public int screenMode = -1;
        public bool vSync;
        // 0 이하면 프레임 제한 없음입니다.
        public int targetFrameRate = -1;
        // 주사율(분자/분모)입니다. 분자가 0 이하면 지정하지 않고 Unity가 고릅니다(이 값이 없던 씬·파일 호환).
        public int refreshRateNumerator;
        public int refreshRateDenominator = 1;
        public float masterVolume = 1f;
        public float bgmVolume = 1f;
        public float sfxVolume = 1f;
        public float mouseSensitivity = 1f;
        public bool cameraKickEnabled = true;
    }

    public static GameSettingManager Instance { get; private set; }

    [Header("Display Settings")]
    [SerializeField] private SettingSnapshot currentSettings = new SettingSnapshot();

    [Header("Audio Mixer (선택)")]
    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private string masterVolumeParam = "MasterVolume";
    [SerializeField] private string bgmVolumeParam = "BgmVolume";
    [SerializeField] private string sfxVolumeParam = "SfxVolume";

    [Header("Crosshair")]
    [Tooltip("조준선 기본값 템플릿입니다. 설정 화면의 기본값 버튼과, 저장값이 없는 캐릭터의 첫 값으로 씁니다. 비어 있으면 Resources 기본 에셋을 사용합니다.")]
    [SerializeField] private DefaultCrosshairSettings m_defaultCrosshairSettings;

    // 사용자가 저장한 조준선 값입니다. 다른 설정과 같은 파일(SettingData.crosshair)에 들어갑니다.
    private SettingData.CrosshairSettingData m_crosshairSettings = new SettingData.CrosshairSettingData();

    // 저장 파일을 읽기 전, 씬에 지정된 처음 설정값입니다. 설정을 기본값으로 되돌릴 때 씁니다.
    private SettingSnapshot m_initialSettings;

    private const float MuteDb = -80f;
    private const float MaxDb = 0f;
    public const float MinMouseSensitivity = 0.01f;
    public const float MaxMouseSensitivity = 10f;

    public int ResolutionWidth => currentSettings.width;
    public int ResolutionHeight => currentSettings.height;
    public bool IsFullscreen => currentSettings.fullscreen;
    public FullScreenMode ScreenMode => ResolveScreenMode(currentSettings);
    public bool VSyncEnabled => currentSettings.vSync;
    public int TargetFrameRate => currentSettings.targetFrameRate;
    /// <summary>저장된 주사율입니다. 지정하지 않았으면 numerator가 0입니다.</summary>
    public RefreshRate RefreshRate => new RefreshRate { numerator = (uint)Mathf.Max(0, currentSettings.refreshRateNumerator), denominator = (uint)Mathf.Max(1, currentSettings.refreshRateDenominator) };
    public float MouseSensitivity => currentSettings.mouseSensitivity;
    public bool CameraKickEnabled => currentSettings.cameraKickEnabled;

    /// <summary>조준선 기본값 템플릿입니다. 씬에 지정하지 않았으면 Resources 기본 에셋을 읽습니다. 둘 다 없으면 null입니다.</summary>
    public DefaultCrosshairSettings DefaultCrosshairSettings
    {
        get
        {
            if (m_defaultCrosshairSettings == null)
            {
                m_defaultCrosshairSettings = DefaultCrosshairSettings.LoadFromResources();
            }

            return m_defaultCrosshairSettings;
        }
    }

    /// <summary>사용자가 저장한 조준선 값의 사본입니다.</summary>
    public SettingData.CrosshairSettingData GetCrosshairSettings()
    {
        return m_crosshairSettings.Clone();
    }

    /// <summary>조준선 값을 기록합니다. 파일에는 <see cref="SaveSettings"/>를 호출할 때 함께 저장됩니다.</summary>
    public void SetCrosshairSettings(SettingData.CrosshairSettingData settings)
    {
        m_crosshairSettings = settings != null ? settings.Clone() : new SettingData.CrosshairSettingData();
    }

    private void Awake()
    {
        if (TryRejectDuplicateOrInvalidRoot())
        {
            return;
        }

        Instance = this;
        NormalizeGameplaySettings(currentSettings);
        m_initialSettings = CreateSnapshot();

        if (!LoadSettings())
        {
            ApplySnapshot(currentSettings);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void SetResolution(int width, int height, bool fullscreen)
    {
        if (width <= 0 || height <= 0)
        {
            Debug.LogWarning($"[GameSettingManager] Invalid resolution: {width}x{height}");
            return;
        }

        currentSettings.width = width;
        currentSettings.height = height;
        SyncScreenModeWithFullscreen(fullscreen);

        ApplyDisplayMode(ResolveScreenMode(currentSettings));
    }

    public void SetFullscreen(bool isFullscreen)
    {
        SyncScreenModeWithFullscreen(isFullscreen);
        Screen.fullScreenMode = ResolveScreenMode(currentSettings);
    }

    /// <summary>해상도와 주사율을 함께 바꿉니다. 화면 모드는 그대로 둡니다.</summary>
    /// <remarks>
    /// 주사율은 배타적 전체 화면에서만 모니터에 그대로 적용됩니다. 테두리 없는 전체 화면과 창 모드는 바탕화면 주사율을 따릅니다.
    /// numerator가 0이면 주사율을 지정하지 않습니다.
    /// </remarks>
    public void SetDisplayMode(int width, int height, RefreshRate refreshRate)
    {
        if (width <= 0 || height <= 0)
        {
            Debug.LogWarning($"[GameSettingManager] Invalid resolution: {width}x{height}");
            return;
        }

        currentSettings.width = width;
        currentSettings.height = height;
        currentSettings.refreshRateNumerator = (int)refreshRate.numerator;
        currentSettings.refreshRateDenominator = refreshRate.denominator > 0 ? (int)refreshRate.denominator : 1;
        ApplyDisplayMode(ResolveScreenMode(currentSettings));
    }

    /// <summary>저장된 해상도·주사율과 주어진 화면 모드로 화면을 맞춥니다.</summary>
    private void ApplyDisplayMode(FullScreenMode mode)
    {
        if (currentSettings.refreshRateNumerator > 0)
        {
            Screen.SetResolution(currentSettings.width, currentSettings.height, mode, RefreshRate);
        }
        else
        {
            Screen.SetResolution(currentSettings.width, currentSettings.height, mode);
        }
    }

    /// <summary>화면 모드(전체 화면, 테두리 없는 전체 화면, 창 모드)를 바꿉니다. 해상도는 그대로 둡니다.</summary>
    public void SetScreenMode(FullScreenMode mode)
    {
        currentSettings.screenMode = (int)mode;
        currentSettings.fullscreen = mode != FullScreenMode.Windowed;
        ApplyDisplayMode(mode);
    }

    /// <summary>수직 동기화를 켜고 끕니다. 켜져 있으면 프레임 제한(<see cref="SetTargetFrameRate"/>)은 무시됩니다.</summary>
    public void SetVSync(bool enabled)
    {
        currentSettings.vSync = enabled;
        QualitySettings.vSyncCount = enabled ? 1 : 0;
    }

    /// <summary>최대 프레임을 정합니다. 0 이하면 제한하지 않습니다.</summary>
    public void SetTargetFrameRate(int frameRate)
    {
        currentSettings.targetFrameRate = frameRate > 0 ? frameRate : -1;
        Application.targetFrameRate = currentSettings.targetFrameRate;
    }

    /// <summary>
    /// 전체 화면 여부만 받는 기존 호출을 화면 모드와 맞춥니다. 창 모드로 바꾸거나, 창 모드에서 전체 화면으로 돌아오면
    /// 화면 모드를 그에 맞게 고칩니다. 이미 전체 화면 계열이면 고른 종류(전체 화면/테두리 없는 전체 화면)를 유지합니다.
    /// </summary>
    private void SyncScreenModeWithFullscreen(bool fullscreen)
    {
        currentSettings.fullscreen = fullscreen;
        if (!fullscreen)
        {
            currentSettings.screenMode = (int)FullScreenMode.Windowed;
        }
        else if (currentSettings.screenMode == (int)FullScreenMode.Windowed)
        {
            currentSettings.screenMode = -1;
        }
    }

    /// <summary>저장된 화면 모드를 돌려줍니다. 값이 없으면 전체 화면 여부로 정합니다(Unity 기본 전체 화면은 테두리 없는 전체 화면).</summary>
    private static FullScreenMode ResolveScreenMode(SettingSnapshot settings)
    {
        if (settings.screenMode >= 0 && System.Enum.IsDefined(typeof(FullScreenMode), settings.screenMode))
        {
            return (FullScreenMode)settings.screenMode;
        }

        return settings.fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
    }

    public void SetMasterVolume(float volume)
    {
        currentSettings.masterVolume = Mathf.Clamp01(volume);
        ApplyMixerVolume(masterVolumeParam, currentSettings.masterVolume);
    }

    public void SetBgmVolume(float volume)
    {
        currentSettings.bgmVolume = Mathf.Clamp01(volume);
        ApplyMixerVolume(bgmVolumeParam, currentSettings.bgmVolume);
    }

    public void SetSfxVolume(float volume)
    {
        currentSettings.sfxVolume = Mathf.Clamp01(volume);
        ApplyMixerVolume(sfxVolumeParam, currentSettings.sfxVolume);
    }

    public void SetMouseSensitivity(float sensitivity)
    {
        float clamped = Mathf.Clamp(sensitivity, MinMouseSensitivity, MaxMouseSensitivity);
        currentSettings.mouseSensitivity = Mathf.Round(clamped * 100f) / 100f;
        currentSettings.gameplaySettingsVersion = 1;
    }

    public void SetCameraKickEnabled(bool enabled)
    {
        currentSettings.cameraKickEnabled = enabled;
        currentSettings.gameplaySettingsVersion = 1;
    }

    // Applies all currently cached display/audio values.
    public void ApplySettings()
    {
        SetResolution(currentSettings.width, currentSettings.height, currentSettings.fullscreen);
        SetFullscreen(currentSettings.fullscreen);
        SetVSync(currentSettings.vSync);
        SetTargetFrameRate(currentSettings.targetFrameRate);
        SetMasterVolume(currentSettings.masterVolume);
        SetBgmVolume(currentSettings.bgmVolume);
        SetSfxVolume(currentSettings.sfxVolume);
        SetMouseSensitivity(currentSettings.mouseSensitivity);
        SetCameraKickEnabled(currentSettings.cameraKickEnabled);
    }

    /// <summary>
    /// 모든 설정을 처음 값으로 되돌리고 파일에 저장합니다. 설정 화면에서 기본값을 고르고 저장한 것과 같은 결과입니다.
    /// </summary>
    /// <remarks>
    /// 화면·소리·조작 값은 저장 파일을 읽기 전 씬에 지정된 값으로 되돌립니다. 조준선은 모든 캐릭터와 투척물에
    /// 기본값 템플릿(<see cref="DefaultCrosshairSettings"/>) 값을 채워 저장합니다. 템플릿에 없는 캐릭터는 저장하지 않아
    /// 다음 실행 때 씬 값을 기본값으로 씁니다. 데모 종료 뒤 처음 상태로 돌아갈 때 부릅니다.
    /// </remarks>
    public bool ResetToDefaultsAndSave()
    {
        ApplySnapshot(m_initialSettings ?? new SettingSnapshot());

        SettingData.CrosshairSettingData crosshair = new SettingData.CrosshairSettingData();
        DefaultCrosshairSettings defaults = DefaultCrosshairSettings;
        if (defaults != null)
        {
            foreach (PlayerbleCharacterId characterId in System.Enum.GetValues(typeof(PlayerbleCharacterId)))
            {
                if (characterId != PlayerbleCharacterId.Unknown
                    && defaults.TryGetCharacterStyle(characterId, out CrosshairStyle style))
                {
                    crosshair.characters.Add(new SettingData.CharacterCrosshairEntry { characterId = characterId, style = style });
                }
            }

            if (defaults.TryGetThrowableStyle(out CrosshairStyle throwable))
            {
                crosshair.hasThrowable = true;
                crosshair.throwable = throwable;
            }
        }

        m_crosshairSettings = crosshair;
        return SaveSettings();
    }

    // Saves only SettingData to disk.
    public bool SaveSettings()
    {
        SettingData data = CreateSettingData();
        string path = SaveFilePaths.GetSettingPath();

        try
        {
            SaveFilePaths.EnsureSaveDirectory();
            File.WriteAllText(path, JsonUtility.ToJson(data, true));
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[GameSettingManager] SaveSettings failed. Path: {path}. Error: {ex.Message}");
            return false;
        }
    }

    // Loads SettingData from disk. Returns false when file is missing or invalid.
    public bool LoadSettings()
    {
        string path = SaveFilePaths.GetSettingPath();
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            SettingData data = JsonUtility.FromJson<SettingData>(json);
            if (data == null)
            {
                return false;
            }

            ApplySettingData(data);
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[GameSettingManager] LoadSettings failed. Path: {path}. Error: {ex.Message}");
            return false;
        }
    }

    public SettingSnapshot CreateSnapshot()
    {
        return new SettingSnapshot
        {
            gameplaySettingsVersion = 1,
            width = currentSettings.width,
            height = currentSettings.height,
            fullscreen = currentSettings.fullscreen,
            screenMode = currentSettings.screenMode,
            vSync = currentSettings.vSync,
            targetFrameRate = currentSettings.targetFrameRate,
            refreshRateNumerator = currentSettings.refreshRateNumerator,
            refreshRateDenominator = currentSettings.refreshRateDenominator,
            masterVolume = currentSettings.masterVolume,
            bgmVolume = currentSettings.bgmVolume,
            sfxVolume = currentSettings.sfxVolume,
            mouseSensitivity = currentSettings.mouseSensitivity,
            cameraKickEnabled = currentSettings.cameraKickEnabled
        };
    }

    public SettingData CreateSettingData()
    {
        SettingData data = new SettingData();
        data.display.width = currentSettings.width;
        data.display.height = currentSettings.height;
        data.display.fullscreen = currentSettings.fullscreen;
        data.display.screenMode = currentSettings.screenMode;
        data.display.vSync = currentSettings.vSync;
        data.display.targetFrameRate = currentSettings.targetFrameRate;
        data.display.refreshRateNumerator = currentSettings.refreshRateNumerator;
        data.display.refreshRateDenominator = currentSettings.refreshRateDenominator;
        data.audio.masterVolume = currentSettings.masterVolume;
        data.audio.bgmVolume = currentSettings.bgmVolume;
        data.audio.sfxVolume = currentSettings.sfxVolume;
        data.gameplay.mouseSensitivity = currentSettings.mouseSensitivity;
        data.gameplay.cameraKickEnabled = currentSettings.cameraKickEnabled;
        data.crosshair = m_crosshairSettings.Clone();
        data.MarkUpdatedNow();
        return data;
    }

    public void ApplySnapshot(SettingSnapshot snapshot)
    {
        if (snapshot == null)
        {
            Debug.LogWarning("[GameSettingManager] Snapshot is null.");
            return;
        }

        NormalizeGameplaySettings(snapshot);

        // 전체 화면 종류는 SetResolution이 fullscreen 값과 맞춰 쓰므로 먼저 넣어 둡니다.
        currentSettings.screenMode = snapshot.screenMode;
        currentSettings.refreshRateNumerator = snapshot.refreshRateNumerator;
        currentSettings.refreshRateDenominator = snapshot.refreshRateDenominator;
        SetResolution(snapshot.width, snapshot.height, snapshot.fullscreen);
        SetFullscreen(snapshot.fullscreen);
        SetVSync(snapshot.vSync);
        SetTargetFrameRate(snapshot.targetFrameRate);
        SetMasterVolume(snapshot.masterVolume);
        SetBgmVolume(snapshot.bgmVolume);
        SetSfxVolume(snapshot.sfxVolume);
        SetMouseSensitivity(snapshot.mouseSensitivity);
        SetCameraKickEnabled(snapshot.cameraKickEnabled);
    }

    public void ApplySettingData(SettingData data)
    {
        if (data == null)
        {
            Debug.LogWarning("[GameSettingManager] SettingData is null.");
            return;
        }

        if (data.display == null)
        {
            data.display = new SettingData.DisplaySettingData();
        }

        if (data.audio == null)
        {
            data.audio = new SettingData.AudioSettingData();
        }

        bool hasGameplaySettings = data.schemaVersion >= 2 && data.gameplay != null;
        if (data.gameplay == null)
        {
            data.gameplay = new SettingData.GameplaySettingData();
        }

        SettingSnapshot snapshot = new SettingSnapshot
        {
            gameplaySettingsVersion = 1,
            width = data.display.width,
            height = data.display.height,
            fullscreen = data.display.fullscreen,
            screenMode = data.display.screenMode,
            vSync = data.display.vSync,
            targetFrameRate = data.display.targetFrameRate,
            refreshRateNumerator = data.display.refreshRateNumerator,
            refreshRateDenominator = data.display.refreshRateDenominator,
            masterVolume = data.audio.masterVolume,
            bgmVolume = data.audio.bgmVolume,
            sfxVolume = data.audio.sfxVolume,
            mouseSensitivity = hasGameplaySettings ? data.gameplay.mouseSensitivity : 1f,
            cameraKickEnabled = !hasGameplaySettings || data.gameplay.cameraKickEnabled
        };

        ApplySnapshot(snapshot);
        m_crosshairSettings = data.crosshair != null ? data.crosshair.Clone() : new SettingData.CrosshairSettingData();
    }

    private static void NormalizeGameplaySettings(SettingSnapshot snapshot)
    {
        if (snapshot == null || snapshot.gameplaySettingsVersion >= 1)
        {
            return;
        }

        // gameplaySettingsVersion이 없던 기존 Scene/Prefab 직렬화 값은 숫자와 bool이 0/false로 들어옵니다.
        // 새 옵션을 추가한 첫 실행에서 카메라 킥이 갑자기 꺼지지 않도록 명시적인 기본값으로 이관합니다.
        snapshot.gameplaySettingsVersion = 1;
        snapshot.mouseSensitivity = 1f;
        snapshot.cameraKickEnabled = true;
    }

    private void ApplyMixerVolume(string parameterName, float normalizedVolume)
    {
        if (audioMixer == null || string.IsNullOrWhiteSpace(parameterName))
        {
            return;
        }

        float clamped = Mathf.Clamp01(normalizedVolume);
        float db = clamped <= 0.0001f ? MuteDb : Mathf.Clamp(Mathf.Log10(clamped) * 20f, MuteDb, MaxDb);
        audioMixer.SetFloat(parameterName, db);
    }

    private bool TryRejectDuplicateOrInvalidRoot()
    {
        GameManager rootManager = GetComponentInParent<GameManager>();
        if (rootManager == null)
        {
            Debug.LogWarning("[GameSettingManager] Parent GameManager not found. Destroying duplicate/orphan instance.");
            Destroy(gameObject);
            return true;
        }

        if (GameManager.Instance != null && rootManager != GameManager.Instance)
        {
            Destroy(gameObject);
            return true;
        }

        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return true;
        }

        return false;
    }
}
