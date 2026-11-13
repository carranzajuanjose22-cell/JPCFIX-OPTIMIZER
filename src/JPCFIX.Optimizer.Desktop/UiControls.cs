using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation.Peers;
namespace JPCFIX.Optimizer.Desktop;

public sealed class AppIcon : Control
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(AppIcon), new FrameworkPropertyMetadata("Info", FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public AppIcon() { Width = Height = 22; Focusable = false; IsHitTestVisible = false; SetResourceReference(ForegroundProperty, "AccentBrush"); }
    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        if (TryFindResource("Icon" + Kind) is not Geometry geometry) return;
        var scale = Math.Min(ActualWidth, ActualHeight) / 24;
        context.PushTransform(new TranslateTransform((ActualWidth - 24 * scale) / 2, (ActualHeight - 24 * scale) / 2)); context.PushTransform(new ScaleTransform(scale, scale));
        context.DrawGeometry(null, new Pen(Foreground, 1.65) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, geometry); context.Pop(); context.Pop();
    }
}
public sealed class MetricCard : Control
{
    public MetricCard() { Focusable = false; IsTabStop = false; }
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(MetricCard), new PropertyMetadata(""));
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(string), typeof(MetricCard), new PropertyMetadata("—"));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(MetricCard), new PropertyMetadata("Sin datos"));
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(string), typeof(MetricCard), new PropertyMetadata("Info"));
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    protected override AutomationPeer OnCreateAutomationPeer() => new MetricCardPeer(this);
    private sealed class MetricCardPeer : FrameworkElementAutomationPeer
    {
        public MetricCardPeer(MetricCard owner) : base(owner) { }
        protected override string GetNameCore() { var card = (MetricCard)Owner; return $"{card.Title}: {card.Value}. {card.Description}"; }
        protected override string GetClassNameCore() => nameof(MetricCard);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
    }
}
public sealed class AdaptivePanel : Panel
{
    public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(nameof(MinColumnWidth), typeof(double), typeof(AdaptivePanel), new FrameworkPropertyMetadata(360d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(nameof(MaxColumns), typeof(int), typeof(AdaptivePanel), new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(nameof(Gap), typeof(double), typeof(AdaptivePanel), new FrameworkPropertyMetadata(16d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public double MinColumnWidth { get => (double)GetValue(MinColumnWidthProperty); set => SetValue(MinColumnWidthProperty, value); }
    public int MaxColumns { get => (int)GetValue(MaxColumnsProperty); set => SetValue(MaxColumnsProperty, value); }
    public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }
    private int Columns(double width) { var count = Math.Clamp((int)((width + Gap) / Math.Max(1, MinColumnWidth + Gap)), 1, Math.Max(1, MaxColumns)); return MaxColumns == 4 && count == 3 ? 2 : count; }
    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? 800 : available.Width; var columns = Columns(width); var cell = Math.Max(0, (width - Gap * (columns - 1)) / columns); double height = 0, row = 0;
        for (int i = 0; i < InternalChildren.Count; i++) { InternalChildren[i].Measure(new(cell, double.PositiveInfinity)); row = Math.Max(row, InternalChildren[i].DesiredSize.Height); if (i % columns == columns - 1 || i == InternalChildren.Count - 1) { height += row + (i >= columns ? Gap : 0); row = 0; } }
        return new(width, height);
    }
    protected override Size ArrangeOverride(Size final)
    {
        var columns = Columns(final.Width); var cell = Math.Max(0, (final.Width - Gap * (columns - 1)) / columns); double y = 0;
        for (int start = 0; start < InternalChildren.Count; start += columns) { var count = Math.Min(columns, InternalChildren.Count - start); double height = 0; for (int c = 0; c < count; c++) height = Math.Max(height, InternalChildren[start + c].DesiredSize.Height); for (int c = 0; c < count; c++) InternalChildren[start + c].Arrange(new(c * (cell + Gap), y, cell, height)); y += height + Gap; }
        return final;
    }
}
