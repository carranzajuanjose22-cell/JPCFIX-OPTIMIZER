namespace JPCFIX.Optimizer.Desktop;
public partial class MainWindow : System.Windows.Window
{
    public MainWindow()
    {
        InitializeComponent();
        Width = System.Math.Min(Width, System.Windows.SystemParameters.WorkArea.Width);
        Height = System.Math.Min(Height, System.Windows.SystemParameters.WorkArea.Height);
    }
}
