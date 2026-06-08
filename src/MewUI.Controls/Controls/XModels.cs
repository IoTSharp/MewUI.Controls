using System.Collections.ObjectModel;

using Aprillz.MewUI.Controls;

namespace IoTSharp.MewUI.Controls;

public sealed record XAttachment(string Name, string? Kind = null, string? Path = null, long? SizeBytes = null, string? Id = null);

public enum XContentBlockKind
{
    Paragraph,
    Code,
    Quote,
    Canvas
}

public sealed class XContentBlock
{
    public XContentBlockKind Kind { get; set; }

    public string Text { get; set; } = string.Empty;

    public Element? Canvas { get; set; }

    public string? Language { get; set; }
}

public sealed class XMessage
{
    public string Role { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    public bool IsAssistant => string.Equals(Role, "assistant", StringComparison.OrdinalIgnoreCase);

    public bool IsUser => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);

    public ObservableCollection<XContentBlock> Blocks { get; } = new();

    public ObservableCollection<XAttachment> Attachments { get; } = new();
}

public sealed class XConversationItem
{
    public string Key { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Group { get; set; }

    public string? Icon { get; set; }

    public int? Count { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public bool Disabled { get; set; }
}

public sealed record XConversationRenameRequest(string Key, string Title);
