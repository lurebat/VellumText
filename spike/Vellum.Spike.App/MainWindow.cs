using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;

namespace Vellum.Spike.App;

public sealed class MainWindow : Window
{
    public MainWindow()
    {
        Title = "Vellum spike — Increment 0";
        Width = 900;
        Height = 800;

        var diag = new TextBlock
        {
            FontFamily = new FontFamily("Consolas, Menlo, monospace"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8),
            Background = Brushes.WhiteSmoke,
            MinHeight = 54,
            Text = "Click and drag over any paragraph below. Arrow keys and Backspace move the caret."
        };

        var panel = new StackPanel { Spacing = 4 };

        panel.Children.Add(Header("A2 — custom ITextSource with DrawableTextRun embeds (drag to select across an embed)"));
        panel.Children.Add(Live(Scenarios.EmbedText, w =>
        {
            var (source, _) = Scenarios.BuildEmbedSource();
            return new TextLayout(source, new SpikeParagraphProperties(Scenarios.Default()),
                TextTrimming.None, w, double.PositiveInfinity, int.MaxValue);
        }, diag));

        panel.Children.Add(Header("C1 — the same styling via textStyleOverrides; the U+FFFC renders as tofu, not an embed"));
        panel.Children.Add(Live(Scenarios.StyledTextWithObj, w => new TextLayout(
            text: Scenarios.StyledTextWithObj,
            typeface: Typeface.Default,
            fontSize: 16,
            foreground: Brushes.Black,
            textAlignment: TextAlignment.Left,
            textWrapping: TextWrapping.Wrap,
            textTrimming: TextTrimming.None,
            textDecorations: null,
            flowDirection: FlowDirection.LeftToRight,
            maxWidth: w,
            maxHeight: double.PositiveInfinity,
            lineHeight: double.NaN,
            letterSpacing: 0,
            maxLines: 0,
            fontFeatures: null,
            textStyleOverrides: Scenarios.StyleOverrides(Scenarios.StyledTextWithObj)), diag));

        panel.Children.Add(Header("Bidi — select across the Hebrew/Latin boundary; the selection should split into disjoint rects"));
        panel.Children.Add(Live(Scenarios.BidiText, w => Simple(Scenarios.BidiText, w), diag));

        panel.Children.Add(Header("Grapheme clusters — family ZWJ sequence and a combining acute; try arrow keys vs Backspace"));
        panel.Children.Add(Live(Scenarios.ClusterText, w => Simple(Scenarios.ClusterText, w), diag));

        Content = new DockPanel
        {
            Children =
            {
                Dock(diag, Avalonia.Controls.Dock.Bottom),
                new ScrollViewer { Content = panel },
            }
        };
    }

    private static TextLayout Simple(string text, double width) => new(
        text: text,
        typeface: Typeface.Default,
        fontSize: 20,
        foreground: Brushes.Black,
        textAlignment: TextAlignment.Left,
        textWrapping: TextWrapping.Wrap,
        textTrimming: TextTrimming.None,
        textDecorations: null,
        flowDirection: FlowDirection.LeftToRight,
        maxWidth: width,
        maxHeight: double.PositiveInfinity,
        lineHeight: double.NaN,
        letterSpacing: 0,
        maxLines: 0,
        fontFeatures: null,
        textStyleOverrides: null);

    private static Control Dock(Control c, Dock dock)
    {
        DockPanel.SetDock(c, dock);
        return c;
    }

    private static Control Header(string text) => new TextBlock
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        FontSize = 12,
        Margin = new Thickness(8, 12, 8, 0),
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brushes.DimGray,
    };

    private static Control Live(string text, Func<double, TextLayout> build, TextBlock diag)
    {
        var control = new LiveTextControl(text, build);
        control.Diagnostics += s => diag.Text = s;
        return new Border
        {
            BorderBrush = Brushes.Gainsboro,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(8, 0, 8, 0),
            Child = control,
        };
    }
}
