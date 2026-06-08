using System.Collections.ObjectModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace IoTSharp.MewUI.Controls;

/// <summary>
/// DockPanelSuite-style docking state for <see cref="DockWorkspace"/>.
/// </summary>
public enum DockState
{
    Unknown,
    Document,
    DockLeft,
    DockRight,
    DockTop,
    DockBottom,
    AutoHideLeft,
    AutoHideRight,
    AutoHideTop,
    AutoHideBottom,
    Float,
    Hidden
}

/// <summary>
/// Allowed docking targets for a workspace content item.
/// </summary>
[Flags]
public enum DockAreas
{
    None = 0,
    Document = 1 << 0,
    DockLeft = 1 << 1,
    DockRight = 1 << 2,
    DockTop = 1 << 3,
    DockBottom = 1 << 4,
    Float = 1 << 5,
    All = Document | DockLeft | DockRight | DockTop | DockBottom | Float
}

/// <summary>
/// Metadata and visual content for a dockable workspace item.
/// </summary>
public sealed class DockContent
{
    public DockContent(string key, string title, Element content)
    {
        Key = string.IsNullOrWhiteSpace(key) ? Guid.NewGuid().ToString("N") : key;
        Title = string.IsNullOrWhiteSpace(title) ? Key : title;
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    public string Key { get; }

    public string Title { get; set; }

    public string? Description { get; set; }

    public string? Icon { get; set; }

    public Element Content { get; }

    public DockState DockState { get; set; } = DockState.Document;

    public DockAreas DockAreas { get; set; } = DockAreas.All;

    public bool CanClose { get; set; } = true;

    public bool CanAutoHide { get; set; } = true;

    public bool IsSelected { get; internal set; }

    public bool IsVisible => DockState != DockState.Hidden;
}

/// <summary>
/// Serializable layout entry for <see cref="DockWorkspace"/>.
/// </summary>
public sealed record DockLayoutItem(string Key, DockState DockState, int Order, bool IsSelected);

/// <summary>
/// Serializable layout snapshot for <see cref="DockWorkspace"/>.
/// </summary>
public sealed record DockLayoutSnapshot(IReadOnlyList<DockLayoutItem> Items);

/// <summary>
/// Event args used when a dock content item changes state.
/// </summary>
public sealed class DockContentEventArgs : EventArgs
{
    public DockContentEventArgs(DockContent content)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    public DockContent Content { get; }
}

/// <summary>
/// MewUI docking workspace with document tabs and dockable tool panes.
/// </summary>
public sealed class DockWorkspace : UserControl
{
    private readonly ObservableCollection<DockContent> _contents = new();
    private readonly Dictionary<DockState, DockContent?> _selectedByState = new();
    private readonly TabControl _documentTabs = new();
    private readonly TabControl _leftTabs = new();
    private readonly TabControl _rightTabs = new();
    private readonly TabControl _topTabs = new();
    private readonly TabControl _bottomTabs = new();
    private readonly StackPanel _autoHideLeft = new();
    private readonly StackPanel _autoHideRight = new();
    private readonly StackPanel _autoHideTop = new();
    private readonly StackPanel _autoHideBottom = new();
    private readonly StackPanel _floatPanel = new();
    private Element _emptyContent = CreateDefaultEmptyContent();

    public DockWorkspace()
    {
        _selectedByState[DockState.Document] = null;
        _selectedByState[DockState.DockLeft] = null;
        _selectedByState[DockState.DockRight] = null;
        _selectedByState[DockState.DockTop] = null;
        _selectedByState[DockState.DockBottom] = null;
        Build();
    }

    public ObservableCollection<DockContent> Contents => _contents;

    public DockContent? ActiveContent { get; private set; }

    public DockContent? ActiveDocument => _selectedByState.TryGetValue(DockState.Document, out DockContent? item) ? item : null;

    public Element EmptyContent
    {
        get => _emptyContent;
        set
        {
            _emptyContent = value ?? throw new ArgumentNullException(nameof(value));
            Rebuild();
        }
    }

    public event EventHandler<DockContentEventArgs>? ContentAdded;

    public event EventHandler<DockContentEventArgs>? ContentRemoved;

    public event EventHandler<DockContentEventArgs>? ContentClosed;

    public event EventHandler<DockContentEventArgs>? ActiveContentChanged;

    public event EventHandler<DockContentEventArgs>? DockStateChanged;

    public DockContent AddDocument(string key, string title, Element content, bool activate = true)
        => AddContent(new DockContent(key, title, content), DockState.Document, activate);

    public DockContent AddTool(string key, string title, Element content, DockState dockState, bool activate = false)
        => AddContent(new DockContent(key, title, content), NormalizeToolState(dockState), activate);

    public DockContent AddContent(DockContent content, DockState dockState, bool activate = true)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (_contents.Any(item => string.Equals(item.Key, content.Key, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Dock content key already exists: {content.Key}");
        }

        content.DockState = EnsureAllowedState(content, NormalizeState(dockState));
        _contents.Add(content);
        ContentAdded?.Invoke(this, new DockContentEventArgs(content));

        if (activate)
        {
            Activate(content);
        }

        Rebuild();
        return content;
    }

    public bool Remove(string key)
    {
        DockContent? content = Find(key);
        return content != null && Remove(content);
    }

    public bool Remove(DockContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!_contents.Remove(content))
        {
            return false;
        }

        if (ReferenceEquals(ActiveContent, content))
        {
            ActiveContent = null;
        }

        foreach (var state in _selectedByState.Keys.ToArray())
        {
            if (ReferenceEquals(_selectedByState[state], content))
            {
                _selectedByState[state] = FindFirstVisible(state);
            }
        }

        ContentRemoved?.Invoke(this, new DockContentEventArgs(content));
        Rebuild();
        return true;
    }

    public bool Close(string key)
    {
        DockContent? content = Find(key);
        return content != null && Close(content);
    }

    public bool Close(DockContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!content.CanClose)
        {
            Hide(content);
            return false;
        }

        bool removed = Remove(content);
        if (removed)
        {
            ContentClosed?.Invoke(this, new DockContentEventArgs(content));
        }

        return removed;
    }

    public void Hide(string key)
    {
        DockContent? content = Find(key);
        if (content != null)
        {
            Hide(content);
        }
    }

    public void Hide(DockContent content)
        => SetDockState(content, DockState.Hidden, activate: false);

    public void Show(string key, DockState dockState, bool activate = true)
    {
        DockContent? content = Find(key);
        if (content != null)
        {
            SetDockState(content, dockState, activate);
        }
    }

    public void SetDockState(DockContent content, DockState dockState, bool activate = true)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!_contents.Contains(content))
        {
            throw new InvalidOperationException("Dock content has not been added to this workspace.");
        }

        DockState next = EnsureAllowedState(content, NormalizeState(dockState));
        if (content.DockState == next)
        {
            if (activate)
            {
                Activate(content);
            }

            return;
        }

        DockState previous = content.DockState;
        content.DockState = next;
        if (_selectedByState.TryGetValue(previous, out DockContent? selected) && ReferenceEquals(selected, content))
        {
            _selectedByState[previous] = FindFirstVisible(previous);
        }

        if (activate)
        {
            Activate(content);
        }

        DockStateChanged?.Invoke(this, new DockContentEventArgs(content));
        Rebuild();
    }

    public void Activate(string key)
    {
        DockContent? content = Find(key);
        if (content != null)
        {
            Activate(content);
        }
    }

    public void Activate(DockContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!_contents.Contains(content) || content.DockState == DockState.Hidden)
        {
            return;
        }

        ActiveContent = content;
        foreach (var item in _contents)
        {
            item.IsSelected = ReferenceEquals(item, content);
        }

        DockState selectedState = ToVisibleSelectionState(content.DockState);
        if (_selectedByState.ContainsKey(selectedState))
        {
            _selectedByState[selectedState] = content;
        }

        ActiveContentChanged?.Invoke(this, new DockContentEventArgs(content));
        Rebuild();
    }

    public DockContent? Find(string key)
        => _contents.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));

    public DockLayoutSnapshot SaveLayout()
    {
        var items = _contents
            .Select((content, index) => new DockLayoutItem(content.Key, content.DockState, index, ReferenceEquals(content, ActiveContent)))
            .ToArray();
        return new DockLayoutSnapshot(items);
    }

    public void RestoreLayout(DockLayoutSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var order = snapshot.Items.ToDictionary(item => item.Key, item => item);
        var reordered = _contents
            .OrderBy(item => order.TryGetValue(item.Key, out var layout) ? layout.Order : int.MaxValue)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        _contents.Clear();
        foreach (var content in reordered)
        {
            if (order.TryGetValue(content.Key, out var layout))
            {
                content.DockState = EnsureAllowedState(content, NormalizeState(layout.DockState));
            }

            _contents.Add(content);
        }

        DockContent? selected = snapshot.Items
            .Where(item => item.IsSelected)
            .Select(item => Find(item.Key))
            .FirstOrDefault(item => item != null);
        if (selected != null)
        {
            Activate(selected);
        }
        else
        {
            Rebuild();
        }
    }

    protected override Element? OnBuild()
    {
        return new Grid()
            .Rows("Auto,*,Auto")
            .Columns("Auto,*,Auto")
            .Children(
                BuildAutoHideStrip(_autoHideTop, DockState.AutoHideTop).GridPosition(0, 1),
                BuildAutoHideStrip(_autoHideLeft, DockState.AutoHideLeft).GridPosition(1, 0),
                BuildCenterWorkspace().GridPosition(1, 1),
                BuildAutoHideStrip(_autoHideRight, DockState.AutoHideRight).GridPosition(1, 2),
                BuildAutoHideStrip(_autoHideBottom, DockState.AutoHideBottom).GridPosition(2, 1));
    }

    private UIElement BuildCenterWorkspace()
    {
        UIElement center = BuildDocumentArea();
        center = WithOptionalPane(center, _leftTabs, DockState.DockLeft, first: true, Orientation.Horizontal, 240);
        center = WithOptionalPane(center, _rightTabs, DockState.DockRight, first: false, Orientation.Horizontal, 280);
        center = WithOptionalPane(center, _topTabs, DockState.DockTop, first: true, Orientation.Vertical, 180);
        center = WithOptionalPane(center, _bottomTabs, DockState.DockBottom, first: false, Orientation.Vertical, 220);

        if (_contents.Any(item => item.DockState == DockState.Float))
        {
            return new DockPanel()
                .LastChildFill()
                .Children(
                    BuildFloatingPanel().DockRight(),
                    center);
        }

        return center;
    }

    private UIElement WithOptionalPane(UIElement center, TabControl tabs, DockState state, bool first, Orientation orientation, double pixels)
    {
        if (!_contents.Any(item => item.DockState == state))
        {
            return center;
        }

        tabs.MinWidth = state is DockState.DockLeft or DockState.DockRight ? 160 : 0;
        tabs.MinHeight = state is DockState.DockTop or DockState.DockBottom ? 120 : 0;

        var split = new SplitPanel
        {
            Orientation = orientation,
            SplitterThickness = 6,
            MinFirst = 120,
            MinSecond = 160,
            FirstLength = first ? GridLength.Pixels(pixels) : GridLength.Star,
            SecondLength = first ? GridLength.Star : GridLength.Pixels(pixels)
        };

        if (first)
        {
            split.First = WrapDockPane(tabs, state);
            split.Second = center;
        }
        else
        {
            split.First = center;
            split.Second = WrapDockPane(tabs, state);
        }

        return split;
    }

    private UIElement BuildDocumentArea()
    {
        if (!_contents.Any(item => item.DockState == DockState.Document))
        {
            return new Border()
                .BorderThickness(1)
                .CornerRadius(0)
                .WithTheme((theme, border) =>
                {
                    border.Background = theme.Palette.WindowBackground;
                    border.BorderBrush = theme.Palette.ControlBorder;
                })
                .Child((UIElement)_emptyContent);
        }

        return WrapDockPane(_documentTabs, DockState.Document);
    }

    private UIElement WrapDockPane(TabControl tabs, DockState state)
    {
        return new Border()
            .BorderThickness(1)
            .CornerRadius(0)
            .WithTheme((theme, border) =>
            {
                border.Background = theme.Palette.WindowBackground;
                border.BorderBrush = theme.Palette.ControlBorder;
            })
            .Child(tabs);
    }

    private Element BuildFloatingPanel()
    {
        _floatPanel.Clear();
        _floatPanel.Vertical().Spacing(8).Width(260);

        foreach (var content in _contents.Where(item => item.DockState == DockState.Float))
        {
            _floatPanel.Add(BuildFloatingCard(content));
        }

        return new Border()
            .Width(280)
            .Padding(8)
            .BorderThickness(1)
            .WithTheme((theme, border) =>
            {
                border.Background = theme.Palette.ControlBackground;
                border.BorderBrush = theme.Palette.ControlBorder;
            })
            .Child(new ScrollViewer()
                .VerticalScroll(ScrollMode.Auto)
                .Content(_floatPanel));
    }

    private Element BuildFloatingCard(DockContent content)
    {
        return new Border()
            .BorderThickness(1)
            .CornerRadius(8)
            .Padding(8)
            .WithTheme((theme, border) =>
            {
                border.Background = theme.Palette.WindowBackground;
                border.BorderBrush = content.IsSelected ? theme.Palette.Accent : theme.Palette.ControlBorder;
            })
            .Child(new DockPanel()
                .LastChildFill()
                .Children(
                    BuildToolHeader(content, DockState.Float).DockTop(),
                    content.Content));
    }

    private Element BuildAutoHideStrip(StackPanel panel, DockState state)
    {
        panel.Clear();
        panel.Spacing(4);
        panel.Orientation = state is DockState.AutoHideTop or DockState.AutoHideBottom
            ? Orientation.Horizontal
            : Orientation.Vertical;

        foreach (var content in _contents.Where(item => item.DockState == state))
        {
            panel.Add(new Button()
                .Content(content.Title, accessKey: false)
                .MinWidth(34)
                .Height(28)
                .OnClick(() => SetDockState(content, ToDockedState(state), activate: true)));
        }

        return panel;
    }

    private void Rebuild()
    {
        EnsureSelections();
        RefreshTabs(_documentTabs, DockState.Document);
        RefreshTabs(_leftTabs, DockState.DockLeft);
        RefreshTabs(_rightTabs, DockState.DockRight);
        RefreshTabs(_topTabs, DockState.DockTop);
        RefreshTabs(_bottomTabs, DockState.DockBottom);
        Build();
    }

    private void RefreshTabs(TabControl tabs, DockState state)
    {
        var items = _contents.Where(item => item.DockState == state).ToArray();
        tabs.ClearTabs();
        foreach (var item in items)
        {
            tabs.AddTab(new TabItem
            {
                Header = state == DockState.Document ? BuildDocumentHeader(item) : BuildToolHeader(item, state),
                Content = item.Content,
                IsEnabled = true
            });
        }

        DockContent? selected = _selectedByState.TryGetValue(state, out DockContent? value) ? value : null;
        int selectedIndex = selected == null ? -1 : Array.IndexOf(items, selected);
        tabs.SelectedIndex = selectedIndex >= 0 ? selectedIndex : items.Length > 0 ? 0 : -1;
        tabs.SelectionChanged -= OnTabSelectionChanged;
        tabs.SelectionChanged += OnTabSelectionChanged;

        void OnTabSelectionChanged(object? _)
        {
            int index = tabs.SelectedIndex;
            if (index >= 0 && index < items.Length)
            {
                Activate(items[index]);
            }
        }
    }

    private Element BuildDocumentHeader(DockContent content)
    {
        return new DockPanel()
            .LastChildFill()
            .Children(
                new Button()
                    .DockRight()
                    .Width(24)
                    .Height(22)
                    .Content("x", accessKey: false)
                    .OnClick(() => Close(content)),
                new TextBlock()
                    .Text(content.Title)
                    .FontSize(12)
                    .TextTrimming(TextTrimming.CharacterEllipsis)
                    .Margin(8, 0, 6, 0));
    }

    private Element BuildToolHeader(DockContent content, DockState state)
    {
        return new DockPanel()
            .LastChildFill()
            .Children(
                BuildToolHeaderButtons(content, state).DockRight(),
                new TextBlock()
                    .Text(content.Title)
                    .FontSize(12)
                    .SemiBold()
                    .TextTrimming(TextTrimming.CharacterEllipsis)
                    .Margin(8, 0, 6, 0));
    }

    private Element BuildToolHeaderButtons(DockContent content, DockState state)
    {
        var buttons = new StackPanel().Horizontal().Spacing(2);
        if (content.CanAutoHide && state is DockState.DockLeft or DockState.DockRight or DockState.DockTop or DockState.DockBottom)
        {
            buttons.Add(new Button()
                .Width(28)
                .Height(22)
                .Content("钉", accessKey: false)
                .OnClick(() => SetDockState(content, ToAutoHideState(state), activate: false)));
        }

        if (content.DockAreas.HasFlag(DockAreas.Float) && state != DockState.Float)
        {
            buttons.Add(new Button()
                .Width(28)
                .Height(22)
                .Content("浮", accessKey: false)
                .OnClick(() => SetDockState(content, DockState.Float, activate: true)));
        }

        buttons.Add(new Button()
            .Width(28)
            .Height(22)
            .Content(content.CanClose ? "x" : "-", accessKey: false)
            .OnClick(() =>
            {
                if (content.CanClose)
                {
                    Close(content);
                }
                else
                {
                    Hide(content);
                }
            }));

        return buttons;
    }

    private void EnsureSelections()
    {
        foreach (var state in _selectedByState.Keys.ToArray())
        {
            DockContent? selected = _selectedByState[state];
            if (selected == null || selected.DockState != state || !_contents.Contains(selected))
            {
                _selectedByState[state] = FindFirstVisible(state);
            }
        }

        if (ActiveContent == null || !_contents.Contains(ActiveContent) || ActiveContent.DockState == DockState.Hidden)
        {
            ActiveContent = ActiveDocument
                ?? _selectedByState.Values.FirstOrDefault(item => item is { DockState: not DockState.Hidden });
        }

        foreach (var item in _contents)
        {
            item.IsSelected = ReferenceEquals(item, ActiveContent);
        }
    }

    private DockContent? FindFirstVisible(DockState state)
        => _contents.FirstOrDefault(item => item.DockState == state);

    private static DockState NormalizeState(DockState state)
        => state == DockState.Unknown ? DockState.Document : state;

    private static DockState NormalizeToolState(DockState state)
        => state == DockState.Document || state == DockState.Unknown ? DockState.DockLeft : NormalizeState(state);

    private static DockState ToVisibleSelectionState(DockState state)
    {
        return state switch
        {
            DockState.AutoHideLeft => DockState.DockLeft,
            DockState.AutoHideRight => DockState.DockRight,
            DockState.AutoHideTop => DockState.DockTop,
            DockState.AutoHideBottom => DockState.DockBottom,
            _ => state
        };
    }

    private static DockState ToDockedState(DockState state)
    {
        return state switch
        {
            DockState.AutoHideLeft => DockState.DockLeft,
            DockState.AutoHideRight => DockState.DockRight,
            DockState.AutoHideTop => DockState.DockTop,
            DockState.AutoHideBottom => DockState.DockBottom,
            _ => state
        };
    }

    private static DockState ToAutoHideState(DockState state)
    {
        return state switch
        {
            DockState.DockLeft => DockState.AutoHideLeft,
            DockState.DockRight => DockState.AutoHideRight,
            DockState.DockTop => DockState.AutoHideTop,
            DockState.DockBottom => DockState.AutoHideBottom,
            _ => state
        };
    }

    private static DockState EnsureAllowedState(DockContent content, DockState requested)
    {
        if (requested == DockState.Hidden)
        {
            return requested;
        }

        DockAreas area = requested switch
        {
            DockState.Document => DockAreas.Document,
            DockState.DockLeft or DockState.AutoHideLeft => DockAreas.DockLeft,
            DockState.DockRight or DockState.AutoHideRight => DockAreas.DockRight,
            DockState.DockTop or DockState.AutoHideTop => DockAreas.DockTop,
            DockState.DockBottom or DockState.AutoHideBottom => DockAreas.DockBottom,
            DockState.Float => DockAreas.Float,
            _ => DockAreas.Document
        };

        if (content.DockAreas.HasFlag(area))
        {
            return requested;
        }

        if (content.DockAreas.HasFlag(DockAreas.Document))
        {
            return DockState.Document;
        }

        if (content.DockAreas.HasFlag(DockAreas.DockLeft))
        {
            return DockState.DockLeft;
        }

        if (content.DockAreas.HasFlag(DockAreas.DockRight))
        {
            return DockState.DockRight;
        }

        if (content.DockAreas.HasFlag(DockAreas.DockTop))
        {
            return DockState.DockTop;
        }

        if (content.DockAreas.HasFlag(DockAreas.DockBottom))
        {
            return DockState.DockBottom;
        }

        return content.DockAreas.HasFlag(DockAreas.Float) ? DockState.Float : DockState.Hidden;
    }

    private static Element CreateDefaultEmptyContent()
    {
        return new Border()
            .Padding(24)
            .Child(new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new TextBlock()
                        .Text("没有打开的文档")
                        .FontSize(16)
                        .SemiBold(),
                    new TextBlock()
                        .Text("打开会话、工具窗口或恢复布局后，这里会显示文档内容。")
                        .FontSize(12)
                        .WithTheme((theme, text) => text.Foreground(theme.Palette.PlaceholderText))));
    }
}
