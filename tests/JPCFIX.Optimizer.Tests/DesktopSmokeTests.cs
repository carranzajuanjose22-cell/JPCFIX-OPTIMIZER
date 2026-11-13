using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using System.Windows.Threading;
using JPCFIX.Optimizer.Application;
using JPCFIX.Optimizer.Desktop;
using JPCFIX.Optimizer.Infrastructure;
using Xunit;

namespace JPCFIX.Optimizer.Tests;

public sealed class DesktopSmokeTests
{
    [Fact] public void SmartBackupViewLoadsResourcesAndInitialBindingsWithoutShowingWindow()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            System.Windows.Application? app = null; MainWindow? window = null;
            try
            {
                app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
                { Source = new Uri("/JPCFIX.Optimizer.Desktop;component/Styles.xaml", UriKind.Relative) });
                // Constructor-only composition: never initialize diagnostics, run UAC, or analyze user files.
                var store = new JsonChangeStore(Path.Combine(Path.GetTempPath(), "JPCFIX-ui-unused-" + Guid.NewGuid().ToString("N")));
                var backup = new BackupViewModel(new SmartBackupService(new WindowsBackupEnvironment()));
                var vm = new MainViewModel(new WindowsDiagnostics(), new PowerOptimization(new WindowsPowerService(), store, () => true), store, new RunnerClient(), backup) { Page = 5 };
                window = new MainWindow { DataContext = vm };
                window.ApplyTemplate();
                window.Measure(new System.Windows.Size(1180, 780));
                window.Arrange(new System.Windows.Rect(0, 0, 1180, 780));
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                var root = Assert.IsType<System.Windows.Controls.Grid>(window.Content);
                root.Measure(new System.Windows.Size(1180, 780));
                root.Arrange(new System.Windows.Rect(0, 0, 1180, 780));
                root.UpdateLayout();
                var pages = Assert.IsType<ContentControl>(window.FindName("Pages"));
                var navigation = Assert.IsType<ListBox>(window.FindName("Navigation"));
                Assert.Equal(new[] { 0, 1, 2, 3, 5, 4 }, vm.Navigation.Select(n => n.Index));
                Assert.Equal(6, vm.Navigation.Count);
                Assert.Equal(5, pages.Content);
                var view = FindVisual<BackupView>(pages);
                Assert.NotNull(view);
                Assert.Same(backup, view.DataContext);
                var profilePicker = FindVisual<ComboBox>(pages);
                Assert.NotNull(profilePicker);
                Assert.NotNull(profilePicker.ItemTemplateSelector);
                Assert.Same(profilePicker.ItemTemplateSelector, FindVisual<ContentPresenter>(profilePicker)?.ContentTemplateSelector);
                Assert.NotNull(app.Resources[typeof(Button)]);
                Assert.False(backup.CreateCommand.CanExecute(null));
                Assert.False(backup.RestoreCommand.CanExecute(null));
                Assert.False(backup.CancelCommand.CanExecute(null));
                Assert.True(backup.NewCommand.CanExecute(null));
                Assert.Empty(backup.Sources); // Loading the module does not inspect personal content.
                // Equivalent DIP viewports for the requested physical resolutions and Windows scales.
                foreach (var physical in new[] { new System.Windows.Size(1366, 768), new System.Windows.Size(1920, 1080) })
                foreach (var scale in new[] { 1.25, 1.5, 2.0 })
                {
                    var viewport = new System.Windows.Size(physical.Width / scale, physical.Height / scale);
                    window.Width = viewport.Width; window.Height = viewport.Height;
                    foreach (var page in vm.Navigation)
                    {
                        vm.Page = page.Index;
                        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                        root.Measure(viewport); root.Arrange(new System.Windows.Rect(viewport)); root.UpdateLayout();
                        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                        Assert.Equal(page.Index, pages.Content);
                        Assert.Equal(page.Index, navigation.SelectedValue);
                        var buttons = Visuals(pages).OfType<Button>().Where(b => b.GetType() == typeof(Button)).ToArray();
                        Assert.NotEmpty(buttons);
                        Assert.All(buttons, button => Assert.NotNull(button.Command));
                        Assert.True(double.IsFinite(window.DesiredSize.Width));
                    }
                }
            }
            catch (Exception ex) { failure = ex; }
            finally { window?.Close(); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(40)), "La inicialización y navegación WPF no deben bloquearse.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static T? FindVisual<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        if (root is T match) return match;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindVisual<T>(System.Windows.Media.VisualTreeHelper.GetChild(root, i)) is { } child) return child;
        return null;
    }
    private static IEnumerable<System.Windows.DependencyObject> Visuals(System.Windows.DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Visuals(System.Windows.Media.VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
