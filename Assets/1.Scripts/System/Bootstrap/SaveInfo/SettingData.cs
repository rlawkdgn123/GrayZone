using System;
using System.Collections.Generic;

[Serializable]
public class SettingData
{
    public const int CurrentSchemaVersion = 2;

    public int schemaVersion = CurrentSchemaVersion;
    public long updatedAtUnixTimeUtc;

    public DisplaySettingData display = new DisplaySettingData();
    public AudioSettingData audio = new AudioSettingData();
    public GameplaySettingData gameplay = new GameplaySettingData();

    // 이전 파일에는 이 섹션이 없습니다. 없으면 빈 값으로 읽히고, 그때는 기본값 템플릿(DefaultCrosshairSettings)을 씁니다.
    public CrosshairSettingData crosshair = new CrosshairSettingData();

    // JSON string payload for future custom settings.
    public List<CustomSettingEntry> customData = new List<CustomSettingEntry>();

    public void MarkUpdatedNow()
    {
        updatedAtUnixTimeUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    [Serializable]
    public class DisplaySettingData
    {
        public int width = 1920;
        public int height = 1080;
        public bool fullscreen = true;

        // 아래 값은 이전 파일에 없습니다. 없으면 이 기본값으로 읽힙니다.
        // screenMode는 UnityEngine.FullScreenMode 값이고, -1이면 fullscreen 값으로 정합니다(이전 파일 호환).
        public int screenMode = -1;
        public bool vSync;
        // 0 이하면 프레임 제한 없음입니다.
        public int targetFrameRate = -1;
        // 주사율(분자/분모)입니다. 분자가 0이면 지정하지 않습니다.
        public int refreshRateNumerator;
        public int refreshRateDenominator = 1;
    }

    [Serializable]
    public class AudioSettingData
    {
        public float masterVolume = 1f;
        public float bgmVolume = 1f;
        public float sfxVolume = 1f;
    }

    [Serializable]
    public class GameplaySettingData
    {
        public float mouseSensitivity = 1f;
        public bool cameraKickEnabled = true;
    }

    /// <summary>
    /// 사용자가 바꿔 저장한 조준선 값입니다. 바꾼 적이 없는 캐릭터나 투척물 조준선은 항목이 없고, 기본값 템플릿을 씁니다.
    /// </summary>
    [Serializable]
    public class CrosshairSettingData
    {
        public List<CharacterCrosshairEntry> characters = new List<CharacterCrosshairEntry>();
        public bool hasThrowable;
        public CrosshairStyle throwable = new CrosshairStyle();

        /// <summary>값이 같은 독립 사본을 만듭니다. 런타임 값과 파일 직렬화 대상이 서로 영향을 주지 않게 합니다.</summary>
        public CrosshairSettingData Clone()
        {
            var copy = new CrosshairSettingData
            {
                hasThrowable = hasThrowable,
                throwable = throwable != null ? throwable.Clone() : new CrosshairStyle(),
            };

            if (characters != null)
            {
                foreach (CharacterCrosshairEntry entry in characters)
                {
                    if (entry != null && entry.style != null)
                    {
                        copy.characters.Add(new CharacterCrosshairEntry { characterId = entry.characterId, style = entry.style.Clone() });
                    }
                }
            }

            return copy;
        }
    }

    [Serializable]
    public class CharacterCrosshairEntry
    {
        public PlayerbleCharacterId characterId;
        public CrosshairStyle style = new CrosshairStyle();
    }

    [Serializable]
    public class CustomSettingEntry
    {
        public string key = string.Empty;
        public string json = "{}";
    }
}
