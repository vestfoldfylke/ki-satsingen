// On narrow screens the sidebar panel is a popover; on wide screens the page
// lays it out as a sidebar. An open popover stays on top of the page whatever
// the CSS says, so the panel is closed when the window widens past the
// breakpoint, and the wide layout takes over.

const SELECTOR_SIDEBAR_PANEL = '#sidebar-panel';

// The breakpoint of the media queries in NavMenu.razor.css and MainLayout.razor.css.
const WIDE_LAYOUT_QUERY = '(width > 640px)';

window.matchMedia(WIDE_LAYOUT_QUERY).addEventListener('change', (event) => {
    if (!event.matches) {
        return;
    }

    const panel = document.querySelector<HTMLElement>(SELECTOR_SIDEBAR_PANEL);
    if (panel?.matches(':popover-open')) {
        panel.hidePopover();
    }
});
