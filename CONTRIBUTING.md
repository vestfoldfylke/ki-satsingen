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

## Razor markup

No formatter handles Razor markup reliably, so it is formatted by hand:
- A tag that fits on one line stays on one line.
- Otherwise, one attribute per line, indented one level, with `>` on its own line.

```razor
<a class="ds-button" href="/new" data-variant="tertiary">Ny samtale</a>

<button
    type="button"
    class="ds-button sidebar-panel-close"
    data-variant="tertiary"
    aria-label="Lukk meny"
    popovertarget="@SidebarPanelId"
>
    <span class="material-symbols-outlined" aria-hidden="true">close</span>
</button>
```
