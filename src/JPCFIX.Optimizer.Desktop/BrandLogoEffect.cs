using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
namespace JPCFIX.Optimizer.Desktop;

// Presentation only: retains the original embedded PNG and its exact silhouette.
public sealed class BrandLogoEffect : ShaderEffect
{
    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty(nameof(Input), typeof(BrandLogoEffect), 0);
    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    public BrandLogoEffect()
    {
        PixelShader = new PixelShader { UriSource = new Uri("pack://application:,,,/JPCFIX.Optimizer.Desktop;component/Assets/BrandLogo.ps", UriKind.Absolute) };
        UpdateShaderValue(InputProperty);
    }
}
