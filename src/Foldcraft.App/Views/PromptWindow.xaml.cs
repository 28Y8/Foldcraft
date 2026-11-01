using System.Windows;
using System.Windows.Controls;

namespace Foldcraft.App.Views;

/// <summary>One choice in a prompt.</summary>
public sealed record PromptButton(string Label, bool IsPrimary = false, bool IsCancel = false);

/// <summary>A small themed dialog, because the system message box can't name its own buttons.</summary>
public partial class PromptWindow : Window
{
    public PromptWindow(string title, string message, IReadOnlyList<string>? items, IReadOnlyList<PromptButton> buttons, bool askForText = false)
    {
        ArgumentNullException.ThrowIfNull(buttons);

        InitializeComponent();

        Title = title;
        Heading.Text = title;
        Message.Text = message;

        if (askForText)
        {
            Answer.Visibility = Visibility.Visible;
            Loaded += (_, _) => Answer.Focus();
        }

        if (items is { Count: > 0 })
        {
            Items.ItemsSource = items;
            ItemsBox.Visibility = Visibility.Visible;
        }

        foreach (var choice in buttons)
        {
            var button = new Button
            {
                Content = choice.Label,
                IsDefault = choice.IsPrimary,
                IsCancel = choice.IsCancel,
                Margin = new Thickness(8, 0, 0, 0),
                MinWidth = 96,
            };

            if (choice.IsPrimary)
            {
                button.Style = (Style)FindResource("PrimaryButton");
            }

            button.Click += (_, _) =>
            {
                Choice = choice;
                DialogResult = true;
            };

            Buttons.Children.Add(button);
        }
    }

    /// <summary>The button pressed, or null if the window was closed another way.</summary>
    public PromptButton? Choice { get; private set; }

    /// <summary>What was typed, when the prompt asked for text.</summary>
    public string TypedText => Answer.Text;
}
