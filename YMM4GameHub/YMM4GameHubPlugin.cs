using YMM4GameHub.ViewModels;
using YMM4GameHub.Views;
using YukkuriMovieMaker.Plugin;

namespace YMM4GameHub
{
    public class YMM4GameHubPlugin : IToolPlugin
    {
        public string Name => "YMM4GameHub";
        public Type ViewModelType => typeof(YMM4GameHubViewModel);
        public Type ViewType => typeof(YMM4GameHubView);
    }
}
