using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using Jabsco.Core.Config;
using Jabsco.Ui.ViewModels;
using Jabsco.Ui.Views;

namespace Jabsco.Ui;

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
            var config = ConfigLoader.Load();
            var mainVm = new MainWindowViewModel(config.Features);
            desktop.MainWindow = new MainWindow { DataContext = mainVm };
            _ = mainVm.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}