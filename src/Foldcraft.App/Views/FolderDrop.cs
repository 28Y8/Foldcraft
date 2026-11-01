using System.Windows;
using System.Windows.Input;

namespace Foldcraft.App.Views;

/// <summary>Attached behavior: passes the paths dropped on an element to a command.</summary>
public static class FolderDrop
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command",
        typeof(ICommand),
        typeof(FolderDrop),
        new PropertyMetadata(null, OnCommandChanged));

    public static ICommand? GetCommand(DependencyObject element) => (ICommand?)element.GetValue(CommandProperty);

    public static void SetCommand(DependencyObject element, ICommand? value) => element.SetValue(CommandProperty, value);

    private static void OnCommandChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not UIElement target)
        {
            return;
        }

        target.DragEnter -= OnDrag;
        target.DragOver -= OnDrag;
        target.Drop -= OnDrop;
        target.AllowDrop = e.NewValue is not null;

        if (e.NewValue is not null)
        {
            target.DragEnter += OnDrag;
            target.DragOver += OnDrag;
            target.Drop += OnDrop;
        }
    }

    private static void OnDrag(object sender, DragEventArgs e)
    {
        var accepted = e.Data.GetDataPresent(DataFormats.FileDrop) &&
            GetCommand((DependencyObject)sender)?.CanExecute(null) == true;

        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            GetCommand((DependencyObject)sender)?.Execute(paths);
        }
    }
}
