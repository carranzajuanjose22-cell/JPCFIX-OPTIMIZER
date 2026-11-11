using System.Windows;
using JPCFIX.Optimizer.Application;
using JPCFIX.Optimizer.Infrastructure;

namespace JPCFIX.Optimizer.Desktop;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Explicit constructor injection keeps the composition root dependency-free.
        var power = new WindowsPowerService();
        var store = new JsonChangeStore(JsonChangeStore.DefaultDirectory);
        var vm = new MainViewModel(new WindowsDiagnostics(), new PowerOptimization(power, store,
            WindowsCompatibility.IsSupported), store, new RunnerClient());
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        window.Loaded += async (_, _) => await vm.InitializeAsync();
        window.Show();
    }
}
