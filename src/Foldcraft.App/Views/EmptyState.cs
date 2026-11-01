using System.Windows;
using System.Windows.Controls;

namespace Foldcraft.App.Views;

/// <summary>What a list shows while it has nothing in it: a symbol, a line saying why and what to do next.</summary>
public sealed class EmptyState : Control
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(
        nameof(Detail), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty));

    public EmptyState()
    {
        // It only ever explains, so a drop or a click goes straight through to the list behind it.
        IsHitTestVisible = false;
        Focusable = false;
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Detail
    {
        get => (string)GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }
}
