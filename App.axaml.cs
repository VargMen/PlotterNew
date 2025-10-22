using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace PlotterNew
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainVm = new UI.ViewModels.MainViewModel();
                desktop.MainWindow = new MainView()
                {
                    DataContext = mainVm
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}