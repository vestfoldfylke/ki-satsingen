namespace kisatsingen.Services.Chat;

// Our own stable identifier for a catalogued model — "fast", never
// "gpt-4o-mini-2024-07-18".
//
// Wrapped rather than left a bare string because three different model strings
// pass through this code and all three are interchangeable to the compiler:
//
//   ChatModelKey        ours, stable, persisted            "fast"
//   ChatModel.ModelId   the id we ask the provider for     "gpt-4o-mini"
//   ChatMessage.ModelId the id the provider says it served "gpt-4o-mini-2024-07-18"
//
// Persisted, so the value has to outlive every rename of the model behind it.
// Changing one silently orphans the stored rows that carry it.
public readonly record struct ChatModelKey(string Value)
{
    public override string ToString() => Value;
}
