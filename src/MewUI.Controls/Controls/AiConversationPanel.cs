using System.Collections.ObjectModel;

using Aprillz.MewUI.Controls;

namespace IoTSharp.MewUI.Controls;

[Obsolete("Use XAttachment for new MewUI X surfaces.")]
public sealed record AiAttachment(string Name, string? Kind = null, string? Path = null, long? SizeBytes = null, string? Id = null)
{
    public XAttachment ToXAttachment() => new(Name, Kind, Path, SizeBytes, Id);
}

[Obsolete("Use XContentBlockKind for new MewUI X surfaces.")]
public enum AiContentBlockKind
{
    Paragraph,
    Code,
    Quote,
    Canvas
}

[Obsolete("Use XContentBlock for new MewUI X surfaces.")]
public sealed class AiContentBlock
{
    public AiContentBlockKind Kind { get; set; }

    public string Text { get; set; } = string.Empty;

    public Element? Canvas { get; set; }

    public string? Language { get; set; }

    public XContentBlock ToXContentBlock() => new()
    {
        Kind = Kind switch
        {
            AiContentBlockKind.Code => XContentBlockKind.Code,
            AiContentBlockKind.Quote => XContentBlockKind.Quote,
            AiContentBlockKind.Canvas => XContentBlockKind.Canvas,
            _ => XContentBlockKind.Paragraph,
        },
        Text = Text,
        Canvas = Canvas,
        Language = Language,
    };
}

[Obsolete("Use XMessage for new MewUI X surfaces.")]
public sealed class AiConversationMessage
{
    public string Role { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    public bool IsAssistant => string.Equals(Role, "assistant", StringComparison.OrdinalIgnoreCase);

    public bool IsUser => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);

    public ObservableCollection<AiContentBlock> Blocks { get; } = new();

    public ObservableCollection<AiAttachment> Attachments { get; } = new();

    public XMessage ToXMessage()
    {
        var message = new XMessage
        {
            Role = Role,
            Author = Author,
            Content = Content,
            Timestamp = Timestamp,
        };

        foreach (var block in Blocks)
        {
            message.Blocks.Add(block.ToXContentBlock());
        }

        foreach (var attachment in Attachments)
        {
            message.Attachments.Add(attachment.ToXAttachment());
        }

        return message;
    }
}

[Obsolete("Use XConversationPanel for new MewUI X surfaces.")]
public class AiConversationPanel : XConversationPanel
{
    public void SetMessages(IEnumerable<AiConversationMessage> messages)
    {
        base.SetMessages(messages.Select(message => message.ToXMessage()));
    }

    public void SetDraftAttachments(IEnumerable<AiAttachment> attachments)
    {
        base.SetDraftAttachments(attachments.Select(attachment => attachment.ToXAttachment()));
    }

    public void AddDraftAttachment(AiAttachment attachment)
    {
        base.AddDraftAttachment(attachment.ToXAttachment());
    }

    public XMessage AddMessage(
        string role,
        string author,
        string content,
        IEnumerable<AiAttachment>? attachments = null,
        IEnumerable<AiContentBlock>? blocks = null)
    {
        return base.AddMessage(
            role,
            author,
            content,
            attachments?.Select(attachment => attachment.ToXAttachment()),
            blocks?.Select(block => block.ToXContentBlock()));
    }
}
