# MewUI.Controls

Third-party controls and higher-level interaction surfaces for
[MewUI](https://github.com/IoTSharp/MewUI).

The public namespace is `IoTSharp.MewUI.Controls`.

This repository keeps extension controls out of the core MewUI repository so
the core project can stay easier to sync with upstream.

## MewUI X

The package includes an initial MewUI-native X surface, inspired by
AntDesign X:

- `XConversations` for session/conversation navigation.
- `XConversationPanel` for message bubbles, attachments, composer, status,
  and approval prompts.
- `XMessage`, `XAttachment`, `XContentBlock`, and `XConversationItem` data
  models.

The older `AiConversationPanel` API remains as a compatibility wrapper around
`XConversationPanel`; new applications should prefer the `X*` types.
