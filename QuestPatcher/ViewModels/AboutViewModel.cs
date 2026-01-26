using QuestPatcher.Utils;

namespace QuestPatcher.ViewModels
{
    public class AboutViewModel: ViewModelBase
    {
        public ProgressViewModel ProgressView { get; }

        public AboutViewModel(ProgressViewModel progressView)
        {
            ProgressView = progressView;
        }
        
        public static void ShowTutorial()
        {
            Util.OpenWebpage("https://bs.wgzeyu.com/oq-guide-qp/");
        }
        
        public static void OpenSourcePage()
        {
            Util.OpenWebpage("https://github.com/BeatSaberCN/QuestPatcher");
        }
        
        public static void OpenOriginalSourcePage()
        {
            Util.OpenWebpage("https://github.com/Lauriethefish/QuestPatcher");
        }
        
        public static void OpenMbPage()
        {
            Util.OpenWebpage("https://space.bilibili.com/413164365");
        }

        public static void OpenSkyQePage()
        {
            Util.OpenWebpage("https://space.bilibili.com/3744764");
        }
    }
}
