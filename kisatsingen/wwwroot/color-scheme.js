{
    const scheme = localStorage.getItem("color-scheme");
    if (scheme) {
        document.documentElement.setAttribute("data-color-scheme", scheme);
    }
}
