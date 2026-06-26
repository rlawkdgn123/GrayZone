using System.IO;
using System.Text;
using UnityEngine;

public static class SaveFilePaths
{
    public const string SaveDirectoryName = "SaveData";
    public const string DefaultProfileId = "default";
    public const string SettingsFileName = "settings.json";

    //윈도우 기준 사용자의 AppData를 저장경로로 한다 (exe파일 기준으로도 가능하지만 권한문제가 생길수 있음 C:)
    public static string SaveDirectoryPath => Path.Combine(Application.persistentDataPath, SaveDirectoryName);

    public static void EnsureSaveDirectory()
    {
        if (!Directory.Exists(SaveDirectoryPath))
        {
            Directory.CreateDirectory(SaveDirectoryPath);
        }
    }

    public static string GetGameSavePath(string profileId)
    {
        string resolvedProfileId = string.IsNullOrWhiteSpace(profileId) ? DefaultProfileId : profileId;
        string safeProfileId = SanitizeFileName(resolvedProfileId);
        return Path.Combine(SaveDirectoryPath, $"game_save_{safeProfileId}.json");
    }

    public static string GetSettingPath()
    {
        return Path.Combine(SaveDirectoryPath, SettingsFileName);
    }

    private static string SanitizeFileName(string fileName)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        StringBuilder builder = new StringBuilder(fileName.Length);

        for (int i = 0; i < fileName.Length; i++)
        {
            char c = fileName[i];
            bool isInvalid = false;
            for (int j = 0; j < invalidChars.Length; j++)
            {
                if (c == invalidChars[j])
                {
                    isInvalid = true;
                    break;
                }
            }

            builder.Append(isInvalid ? '_' : c);
        }

        string sanitized = builder.ToString().Trim();
        return string.IsNullOrEmpty(sanitized) ? DefaultProfileId : sanitized;
    }
}
