using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PlotterNew.UI.ViewModels;

namespace PlotterNew
{
    public partial class App : Application
    {
        public static MainViewModel MainVM { get; } = new MainViewModel();
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainView()
                {
                    DataContext = MainVM
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}