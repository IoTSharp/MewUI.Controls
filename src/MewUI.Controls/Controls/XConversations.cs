using System.Collections.ObjectModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace IoTSharp.MewUI.Controls;

public sealed class XConversations : UserControl
{
    private readonly ObservableCollection<XConversationItem> _items = new();
    private readonly ObservableValue<string> _title = new("Conversations");
    private readonly ObservableValue<string> _extra = new(string.Empty);
    private string? _activeKey;
    private StackPanel _itemsPanel = null!;

    public XConversations()
    {
        Build();
    }

    public ObservableCollection<XConversationItem> Items => _items;

    public string? ActiveKey
    {
        get => _activeKey;
        set
        {
            if (string.Equals(_activeKey, value, StringComparison.Ordinal))
            {
                return;
            }

            _activeKey = value;
            RefreshItems();
        }
    }

    public event Action<string>? ActiveKeyChanged;

    public event Action<XConversationItem>? SelectRequested;

    public event Action? NewRequested;

    public event Action<XConversationItem>? DeleteRequested;

    public event Action<XConversationRenameRequest>? RenameRequested;

    public void SetTitle(string title, string? extra = null)
    {
        _title.Value = string.IsNullOrWhiteSpace(title) ? "Conversations" : title;
        _extra.Value = extra ?? string.Empty;
    }

    public void SetItems(IEnumerable<XConversationItem> items, string? activeKey = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        _items.Clear();
        foreach (var item in items)
        {
            _items.Add(item);
        }

        _activeKey = activeKey ?? _activeKey;
        RefreshItems();
    }

    protected override Element? OnBuild()
    {
        _itemsPanel = new StackPanel()
            .Vertical()
            .Spacing(8);

        RefreshItems();

        return new DockPanel()
            .LastChildFill()
            .Children(
                new StackPanel()
                    .DockTop()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new DockPanel()
                            .Children(
                                new TextBlock()
                                    .BindText(_title)
                                    .FontSize(14)
                                    .SemiBold()
                                    .DockLeft(),
                                new TextBlock()
                                    .BindText(_extra)
                                    .FontSize(11)
                                    .DockRight()
                                    .WithTheme((theme, text) => text.Foreground(theme.Palette.PlaceholderText))),
                        new Button()
                            .Content("新建会话")
                            .Height(34)
                            .OnClick(() => NewRequested?.Invoke())),
                new ScrollViewer()
                    .VerticalScroll(ScrollMode.Auto)
                    .Content(_itemsPanel));
    }

    private void RefreshItems()
    {
        if (_itemsPanel is null)
        {
            return;
        }

        _itemsPanel.Clear();
        foreach (var item in _items)
        {
            _itemsPanel.Add(BuildItem(item));
        }
    }

    private Element BuildItem(XConversationItem item)
    {
        var selected = string.Equals(item.Key, _activeKey, StringComparison.Ordinal);
        var title = string.IsNullOrWhiteSpace(item.Title) ? item.Key : item.Title;
        var description = item.Description;
        if (string.IsNullOrWhiteSpace(description) && item.UpdatedAt is not null)
        {
            description = item.UpdatedAt.Value.ToLocalTime().ToString("MM-dd HH:mm");
        }

        var border = new Border()
            .BorderThickness(1)
            .CornerRadius(8)
            .Padding(10, 9)
            .WithTheme((theme, view) =>
            {
                view.Background(selected ? theme.Palette.Accent.Lerp(theme.Palette.WindowBackground, 0.88) : theme.Palette.ControlBackground);
                view.BorderBrush(selected ? theme.Palette.Accent.Lerp(theme.Palette.ControlBorder, 0.35) : theme.Palette.ControlBorder);
            })
            .Child(
                new DockPanel()
                    .Children(
                        new StackPanel()
                            .DockRight()
                            .Horizontal()
                            .Spacing(4)
                            .Children(
                                new Button()
                                    .Content("改")
                                    .Width(32)
                                    .Height(26)
                                    .OnClick(() => RenameRequested?.Invoke(new XConversationRenameRequest(item.Key, title))),
                                new Button()
                                    .Content("删")
                                    .Width(32)
                                    .Height(26)
                                    .OnClick(() => DeleteRequested?.Invoke(item))),
                        new StackPanel()
                            .Vertical()
                            .Spacing(4)
                            .Children(
                                new TextBlock()
                                    .Text(title)
                                    .SemiBold()
                                    .TextTrimming(TextTrimming.CharacterEllipsis),
                                new TextBlock()
                                    .Text(description ?? string.Empty)
                                    .FontSize(11)
                                    .TextTrimming(TextTrimming.CharacterEllipsis)
                                    .WithTheme((theme, text) => text.Foreground(theme.Palette.PlaceholderText)))));

        border.IsEnabled = !item.Disabled;
        border.OnMouseDown(args =>
        {
            if (item.Disabled)
            {
                return;
            }

            args.Handled = true;
            _activeKey = item.Key;
            ActiveKeyChanged?.Invoke(item.Key);
            SelectRequested?.Invoke(item);
            RefreshItems();
        });

        return border;
    }
}
