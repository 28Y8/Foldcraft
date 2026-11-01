using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Foldcraft.App.ViewModels;

namespace Foldcraft.App.Views;

/// <summary>
/// The rows are rebuilt on every refresh, which destroys the button that was just pressed and
/// would drop a keyboard user at the top of the window. This puts focus back where they were.
/// </summary>
public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();

        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is HistoryViewModel old)
            {
                old.FocusRequested -= OnFocusRequested;
            }

            if (e.NewValue is HistoryViewModel history)
            {
                history.FocusRequested += OnFocusRequested;
            }
        };
    }

    private void OnFocusRequested(object? sender, Guid? batchId) =>
        Dispatcher.InvokeAsync(() => MoveFocus(batchId), DispatcherPriority.Loaded);

    private void MoveFocus(Guid? batchId)
    {
        if (!IsVisible || DataContext is not HistoryViewModel history)
        {
            return;
        }

        var entry = history.Entries.FirstOrDefault(e => e.BatchId == batchId);

        var target = (entry is null ? null : FirstControlIn(RowFor(history, entry)))
            ?? (Usable(DismissButton) ? DismissButton : null)
            ?? (history.Entries.Count > 0 ? FirstControlIn(RowFor(history, history.Entries[0])) : null)
            ?? (Usable(RestoreAllButton) ? RestoreAllButton : null);

        if (target is not null)
        {
            target.BringIntoView();

            // Set the scope's focus too, so it's right when the window is next activated.
            FocusManager.SetFocusedElement(FocusManager.GetFocusScope(target), target);
            target.Focus();
        }
    }

    /// <summary>The list is virtualized, so a row far down may need to be brought into being first.</summary>
    private DependencyObject? RowFor(HistoryViewModel history, HistoryEntryViewModel entry)
    {
        if (EntryList.ItemContainerGenerator.ContainerFromItem(entry) is { } row)
        {
            return row;
        }

        if (FindPanel(EntryList) is { } panel)
        {
            panel.BringIndexIntoViewPublic(history.Entries.IndexOf(entry));
            EntryList.UpdateLayout();
        }

        return EntryList.ItemContainerGenerator.ContainerFromItem(entry);
    }

    private static VirtualizingStackPanel? FindPanel(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if ((child as VirtualizingStackPanel ?? FindPanel(child)) is { } panel)
            {
                return panel;
            }
        }

        return null;
    }

    /// <summary>The Undo button when the row still has one, otherwise its Folders expander.</summary>
    private static Control? FirstControlIn(DependencyObject? root)
    {
        if (root is null)
        {
            return null;
        }

        if (root is ButtonBase button && Usable(button))
        {
            return button;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FirstControlIn(VisualTreeHelper.GetChild(root, i)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static bool Usable(UIElement element) => element.IsVisible && element.IsEnabled && element.Focusable;
}
