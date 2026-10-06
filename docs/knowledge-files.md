# Knowledge files in chat

A file attached in the composer is turned into Markdown (or plaintext) when the message is
sent and saved as a **knowledge file** in the chat. The model reads it with
file tools.

## Flow

1. **Upload.** A file picked in the composer is streamed to a temp file. Size,
   SHA-256 and image signature are checked on the way. A refused file shows
   its reason on its chip. The same content picked twice in one composer is
   refused. (`Services/Attachments`)
2. **Send.** The message is saved first, then the turn takes the composer's
   ready attachments. A file that finishes uploading after send stays for the
   next message.
3. **Already saved?** An attachment with the same content as a file already in
   the chat is available as that file, with the same `fileId`, and is not
   processed again. (`TurnAttachmentStep`)
4. **Processing.** The other attachments go through a bounded queue:
   conversion to Markdown, then a draft. Processing never saves.
   (`Services/KnowledgeFiles/Conversion`, `Processing`)
5. **Save.** The turn saves each draft as a knowledge file. The attachment is
   then **available** (`fileId`). A file that fails in conversion or saving is
   **unavailable** (with a reason), and the turn goes on.
6. **Attachment line.** The user message sent to the model names each file
   with its `fileId`, or its reason if unavailable, and says to read the file
   before answering about it. (`AttachmentLine`)
7. **Reading.** The model reads the files with `list_files`, `get_outline` and
   `read_file`. (`AIFunctions/FileTools`)

States of an attachment: Uploading → Ready → *(send)* Processing →
**Available** (`fileId`) or **Unavailable** (reason).

## Rules

- **Processed on send, not on upload.** Until then an attachment is only a
  temp file plus metadata. The turn waits for processing.
- **Markdown is the only stored form.** Originals are never kept. Each file
  records its `ContentOrigin` (`TextFile`, `ImagePlaceholder`), and the tools
  turn it into a note on how far to trust the text.
- **One row per file, written only when the file is ready.** Line count and
  token estimate are computed on save. The outline is computed on every read
  and never stored.
- **A file that fails never fails the turn.** It shows as unavailable, with
  its reason, in both the attachment line and the user's bubble.
- **The attachment line is a snapshot stored on the turn**
  (`ChatTurn.AttachmentsJson`). It replays unchanged even after a file is
  deleted.
- **Tools are bound per turn** to the owner and the chat. They address files
  by `fileId`, read by line numbers, and are capped by tokens, not lines. They
  are added only when the chat has knowledge files.

## Supported formats

- `.md` and `.txt`, used as they are.
- `.png` and `.jpg`, as a placeholder: the image's content is not read yet.
