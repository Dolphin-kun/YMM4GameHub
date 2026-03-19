using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin;

namespace YMM4GameHub
{
    internal class YMM4GameHubSettings : SettingsBase<YMM4GameHubSettings>
    {
        public override SettingsCategory Category => SettingsCategory.Tool;
        public override string Name => "YMM4GameHub";

        public override bool HasSettingView => false;
        public override object? SettingView => null;


        private UserSettings userSettings = new();
        public UserSettings UserSettings { get => userSettings; set => Set(ref userSettings, value); }

        public override void Initialize()
        {
        }
    }

    public class UserSettings : Bindable
    {
        private string userId = Guid.NewGuid().ToString();
        public string UserId { get => userId; set => Set(ref userId, value); }

        private string userName = "Player";
        public string UserName { get => userName; set => Set(ref userName, value); }

        // レベルデータ（AES+Base64で難読化して保存）
        private string encodedLevelData = "";
        public string EncodedLevelData { get => encodedLevelData; set => Set(ref encodedLevelData, value); }
    }
}
