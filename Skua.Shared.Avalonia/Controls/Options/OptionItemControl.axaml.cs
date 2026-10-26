using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Linq;
using Skua.Shared.Avalonia.ViewModels.Options;

namespace Skua.Shared.Avalonia.Controls.Options;

public partial class OptionItemControl : UserControl
{
    private const double StandardOptionHeight = 34;
    private const double StandardOptionFontSize = 12;

    public OptionItemControl()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => RebuildContent();
    }

    private void RebuildContent()
    {
        if (DataContext is not DisplayOptionItemViewModelBase vm)
        {
            OptionHost.Content = null;
            return;
        }

        OptionHost.Content = CreateDefaultContent(vm);
    }

    public static Control CreateDefaultContent(DisplayOptionItemViewModelBase vm)
    {
        if (vm is CBOBoolChoiceOptionItemViewModel boolChoiceVm)
            return BuildCBOBoolChoiceItem(boolChoiceVm);

        if (vm is CBOChoiceOptionItemViewModel choiceVm)
            return BuildCBOChoiceItem(choiceVm);

        if (vm is CBOBoolOptionItemViewModel cboBoolVm && !string.IsNullOrEmpty(cboBoolVm.Description))
            return BuildCBOBoolItem(cboBoolVm);

        return vm.Tag switch
        {
            "1" => BuildBooleanItem(vm),
            "2" => BuildTextItem(vm),
            "3" => BuildNumberItem(vm),
            "4" => BuildActionItem(vm),
            _ => BuildFallbackItem(vm)
        };
    }

    private static Control BuildCBOBoolChoiceItem(CBOBoolChoiceOptionItemViewModel vm)
    {
        Grid grid = new()
        {
            Margin = new Thickness(0, 3),
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto")
        };

        TextBlock title = new()
        {
            Text = vm.Content,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = StandardOptionFontSize,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(4, 0, 8, 0)
        };
        if (!string.IsNullOrEmpty(vm.Description))
            ToolTip.SetTip(title, vm.Description);

        ToggleButton firstButton = new()
        {
            Content = vm.FirstChoice,
            Classes = { "option-chip" },
            Height = StandardOptionHeight,
            Margin = new Thickness(2, 0)
        };

        ToggleButton secondButton = new()
        {
            Content = vm.SecondChoice,
            Classes = { "option-chip" },
            Height = StandardOptionHeight,
            Margin = new Thickness(2, 0)
        };

        // The pair behaves like radio buttons: exactly one is checked, and clicking the checked one keeps it.
        void SyncChoice()
        {
            bool first = vm.Value is true;
            firstButton.IsChecked = first;
            secondButton.IsChecked = !first;
        }
        SyncChoice();
        firstButton.Click += (_, _) => { vm.Value = true; SyncChoice(); };
        secondButton.Click += (_, _) => { vm.Value = false; SyncChoice(); };
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DisplayOptionItemViewModelBase.Value))
                SyncChoice();
        };

        grid.Children.Add(title);
        Grid.SetColumn(title, 0);
        grid.Children.Add(firstButton);
        Grid.SetColumn(firstButton, 1);
        grid.Children.Add(secondButton);
        Grid.SetColumn(secondButton, 2);

        return grid;
    }

    private static Control BuildCBOChoiceItem(CBOChoiceOptionItemViewModel vm)
    {
        Grid grid = new()
        {
            Margin = new Thickness(0, 3),
            ColumnDefinitions = new ColumnDefinitions("*,180")
        };

        TextBlock title = new()
        {
            Text = vm.Content,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = StandardOptionFontSize,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(4, 0, 8, 0)
        };
        if (!string.IsNullOrEmpty(vm.Description))
            ToolTip.SetTip(title, vm.Description);

        ComboBox comboBox = new()
        {
            ItemsSource = vm.Options,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = StandardOptionHeight
        };
        comboBox[!ComboBox.SelectedIndexProperty] = new Binding(nameof(DisplayOptionItemViewModelBase.Value))
        {
            Source = vm,
            Mode = BindingMode.TwoWay
        };

        grid.Children.Add(title);
        Grid.SetColumn(title, 0);
        grid.Children.Add(comboBox);
        Grid.SetColumn(comboBox, 1);

        return grid;
    }

    private static Control BuildCBOBoolItem(CBOBoolOptionItemViewModel vm)
    {
        Grid grid = new()
        {
            Margin = new Thickness(0, 3),
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };

        TextBlock title = new()
        {
            Text = vm.Content,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = StandardOptionFontSize,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(4, 0, 8, 0)
        };
        if (!string.IsNullOrEmpty(vm.Description))
            ToolTip.SetTip(title, vm.Description);

        ToggleButton toggleButton = CreateBoundToggleButton(vm);
        toggleButton.Classes.Add("option-chip");
        toggleButton.Height = StandardOptionHeight;
        toggleButton.MinWidth = 76;
        toggleButton.HorizontalAlignment = HorizontalAlignment.Right;

        void UpdateToggleText()
        {
            toggleButton.Content = toggleButton.IsChecked == true ? "Enabled" : "Disabled";
        }
        UpdateToggleText();
        toggleButton.IsCheckedChanged += (_, _) => UpdateToggleText();

        grid.Children.Add(title);
        Grid.SetColumn(title, 0);
        grid.Children.Add(toggleButton);
        Grid.SetColumn(toggleButton, 1);

        return grid;
    }

    public static ToggleButton CreateBoundToggleButton(DisplayOptionItemViewModelBase vm, object? content = null)
    {
        ToggleButton toggleButton = new()
        {
            Content = content ?? vm.Content
        };
        BindValue(toggleButton, vm);

        if (vm is CommandOptionItemViewModel commandVm)
        {
            toggleButton.IsCheckedChanged += (_, _) => commandVm.Command.Execute(toggleButton.IsChecked == true);
        }

        return toggleButton;
    }

    public static TextBox CreateBoundTextBox(DisplayOptionItemViewModelBase vm, bool numericOnly = false)
    {
        TextBox textBox = new();
        BindValue(textBox, vm);

        if (numericOnly)
        {
            textBox.TextInput += (_, e) =>
            {
                if (!char.IsDigit(e.Text?[0] ?? '\0'))
                    e.Handled = true;
            };
            textBox.TextChanged += (_, _) =>
            {
                string original = textBox.Text ?? string.Empty;
                string sanitized = SanitizeDigits(original);
                if (!string.Equals(original, sanitized, StringComparison.Ordinal))
                {
                    textBox.Text = sanitized;
                    textBox.CaretIndex = sanitized.Length;
                }
            };
        }

        return textBox;
    }

    public static Button CreateBoundButton(DisplayOptionItemViewModelBase vm, object? content = null, Func<string?>? commandArgumentFactory = null)
    {
        Button button = new()
        {
            Content = content ?? vm.Content
        };
        ToolTip.SetTip(button, vm.Content);
        button.Classes.Add("skua-button");

        if (vm is CommandOptionItemViewModel commandVm)
        {
            if (commandArgumentFactory is null)
            {
                button.Command = commandVm.Command;
            }
            else
            {
                button.Click += (_, _) => commandVm.Command.Execute(commandArgumentFactory());
            }
        }

        return button;
    }

    private static Control BuildBooleanItem(DisplayOptionItemViewModelBase vm)
    {
        Grid grid = new()
        {
            Margin = new Thickness(0, 2),
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };

        TextBlock title = new()
        {
            Text = vm.Content,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = StandardOptionFontSize,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(4, 0, 8, 0)
        };
        if (!string.IsNullOrEmpty(vm.Description))
            ToolTip.SetTip(title, vm.Description);

        ToggleButton toggleButton = CreateBoundToggleButton(vm);
        toggleButton.Classes.Add("option-chip");
        toggleButton.Height = StandardOptionHeight;
        toggleButton.MinWidth = 76;
        toggleButton.HorizontalAlignment = HorizontalAlignment.Right;

        void UpdateToggleText()
        {
            toggleButton.Content = toggleButton.IsChecked == true ? "Enabled" : "Disabled";
        }
        UpdateToggleText();
        toggleButton.IsCheckedChanged += (_, _) => UpdateToggleText();

        grid.Children.Add(title);
        Grid.SetColumn(title, 0);
        grid.Children.Add(toggleButton);
        Grid.SetColumn(toggleButton, 1);

        return grid;
    }

    private static Control BuildTextItem(DisplayOptionItemViewModelBase vm)
    {
        Grid grid = new()
        {
            Margin = new Thickness(0, 2),
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto")
        };

        TextBlock title = new()
        {
            Text = vm.Content,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = StandardOptionFontSize,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(4, 0, 8, 0)
        };
        if (!string.IsNullOrEmpty(vm.Description))
            ToolTip.SetTip(title, vm.Description);

        TextBox textBox = CreateBoundTextBox(vm);
        textBox.Height = 32;
        textBox.Width = 240;
        textBox.MaxWidth = 240;
        textBox.CornerRadius = new CornerRadius(6);
        textBox.BorderThickness = new Thickness(1);
        textBox.FontSize = StandardOptionFontSize;
        textBox.Padding = new Thickness(10, 0);
        textBox.VerticalContentAlignment = VerticalAlignment.Center;
        textBox.Margin = new Thickness(0, 0, 6, 0);

        void UpdateTooltip()
        {
            if (!string.IsNullOrWhiteSpace(textBox.Text))
                ToolTip.SetTip(textBox, textBox.Text);
            else if (!string.IsNullOrEmpty(vm.Description))
                ToolTip.SetTip(textBox, vm.Description);
        }
        UpdateTooltip();
        textBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
                UpdateTooltip();
        };

        Button button = CreateBoundButton(vm, "Set", () => textBox.Text ?? string.Empty);
        button.Classes.Add("skua-button");
        button.Height = 32;
        button.Padding = new Thickness(12, 0);
        button.MinWidth = 50;

        grid.Children.Add(title);
        Grid.SetColumn(title, 0);
        grid.Children.Add(textBox);
        Grid.SetColumn(textBox, 1);
        grid.Children.Add(button);
        Grid.SetColumn(button, 2);

        return grid;
    }

    private static Control BuildNumberItem(DisplayOptionItemViewModelBase vm)
    {
        Grid grid = new()
        {
            Margin = new Thickness(0, 2),
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto")
        };

        TextBlock title = new()
        {
            Text = vm.Content,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = StandardOptionFontSize,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(4, 0, 8, 0)
        };
        if (!string.IsNullOrEmpty(vm.Description))
            ToolTip.SetTip(title, vm.Description);

        TextBox textBox = CreateBoundTextBox(vm, numericOnly: true);
        textBox.Height = 32;
        textBox.Width = 90;
        textBox.CornerRadius = new CornerRadius(6);
        textBox.BorderThickness = new Thickness(1);
        textBox.FontSize = StandardOptionFontSize;
        textBox.HorizontalContentAlignment = HorizontalAlignment.Center;
        textBox.VerticalContentAlignment = VerticalAlignment.Center;
        textBox.Padding = new Thickness(6, 0);
        textBox.Margin = new Thickness(0, 0, 6, 0);

        grid.Children.Add(title);
        Grid.SetColumn(title, 0);
        grid.Children.Add(textBox);
        Grid.SetColumn(textBox, 1);

        int nextCol = 2;
        if (!string.IsNullOrWhiteSpace(vm.SuffixText))
        {
            TextBlock suffix = new()
            {
                Text = vm.SuffixText,
                VerticalAlignment = VerticalAlignment.Center,
                Classes = { "option-pill-suffix" },
                Margin = new Thickness(0, 0, 6, 0)
            };
            grid.Children.Add(suffix);
            Grid.SetColumn(suffix, nextCol++);
        }

        Button button = CreateBoundButton(vm, "Set", () => textBox.Text ?? string.Empty);
        button.Classes.Add("skua-button");
        button.Height = 32;
        button.Padding = new Thickness(12, 0);
        button.MinWidth = 50;
        grid.Children.Add(button);
        Grid.SetColumn(button, nextCol);

        return grid;
    }

    private static Control BuildActionItem(DisplayOptionItemViewModelBase vm)
    {
        Button button = CreateBoundButton(vm);
        button.Classes.Remove("skua-button");
        button.Classes.Add("option-action-button");
        button.Margin = new Thickness(2);
        button.Height = StandardOptionHeight;
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        return button;
    }

    private static Control BuildFallbackItem(DisplayOptionItemViewModelBase vm)
    {
        if (vm.DisplayType == typeof(bool))
            return BuildBooleanItem(vm);

        if (vm.DisplayType == typeof(string))
            return BuildTextItem(vm);

        if (IsNumericDisplayType(vm.DisplayType))
            return BuildNumberItem(vm);

        return BuildActionItem(vm);
    }

    private static void BindValue(TextBox textBox, DisplayOptionItemViewModelBase vm)
    {
        textBox[!TextBox.TextProperty] = new Binding(nameof(DisplayOptionItemViewModelBase.Value))
        {
            Source = vm,
            Mode = BindingMode.TwoWay
        };
        textBox.GotFocus += (_, _) => textBox.SelectAll();
    }

    private static void BindValue(ToggleButton toggleButton, DisplayOptionItemViewModelBase vm)
    {
        toggleButton[!ToggleButton.IsCheckedProperty] = new Binding(nameof(DisplayOptionItemViewModelBase.Value))
        {
            Source = vm,
            Mode = BindingMode.TwoWay
        };
    }

    private static bool IsNumericDisplayType(Type displayType)
    {
        return displayType == typeof(byte)
            || displayType == typeof(sbyte)
            || displayType == typeof(short)
            || displayType == typeof(ushort)
            || displayType == typeof(int)
            || displayType == typeof(uint)
            || displayType == typeof(long)
            || displayType == typeof(ulong);
    }

    private static string SanitizeDigits(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return new string(value.Where(char.IsDigit).ToArray());
    }

    private static Control BuildOptionPill(Control input, Button action, Control? middle = null, bool fillAction = false)
    {
        Border border = new()
        {
            Classes = { "option-pill-container" },
            Margin = new Thickness(2)
        };

        string columns;
        if (middle is null)
            columns = fillAction ? "Auto,*" : "*,Auto";
        else
            columns = fillAction ? "Auto,Auto,*" : "*,Auto,Auto";
        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions(columns)
        };

        grid.Children.Add(input);
        Grid.SetColumn(input, 0);

        if (middle is not null)
        {
            grid.Children.Add(middle);
            Grid.SetColumn(middle, 1);
            grid.Children.Add(action);
            Grid.SetColumn(action, 2);
        }
        else
        {
            grid.Children.Add(action);
            Grid.SetColumn(action, 1);
        }

        border.Child = grid;
        return border;
    }
}
