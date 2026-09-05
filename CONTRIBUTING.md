# Contributing

Coding conventions for this repo. Add new sections as decisions are made.

## Blazor components

- Use an inline `@code` block by default.
- Split into a `.razor.cs` code-behind when the component has any of:
  - Lifecycle overrides (`OnInitialized`, `OnParametersSet`, `OnAfterRender`, ...)
  - DI dependencies used in C# logic (not just referenced from markup)
  - More than ~30 lines of C#
- In a code-behind, inject via `[Inject] public required T Prop { get; set; }` — no `= default!`.
- Keep `@using` and `@inject` directives in the `.razor` file only when the markup itself needs them. Otherwise move them to the code-behind alongside the code that consumes them.
