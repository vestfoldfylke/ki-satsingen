namespace kisatsingen.Security;

// Every directive that doesn't fall back to default-src (frame-ancestors,
// base-uri, form-action) is set explicitly; object-src is stricter than the
// fallback.
public static class ContentSecurityPolicy
{
    public static string Build(bool isDevelopment)
    {
        string[] scriptSources = isDevelopment
            ? ["'self'", "'unsafe-inline'"]                          // dotnet-watch injects an inline bootstrapper
            : ["'self'"];
        string[] connectSources = isDevelopment
            ? ["'self'", "ws://localhost:*", "wss://localhost:*"]    // browser-refresh WebSocket on a random port
            : ["'self'"];

        (string Directive, string[] Sources)[] directives =
        [
            ("default-src", ["'self'"]),
            ("script-src", scriptSources),
            // Radzen and Blazor set style attributes, which CSP treats as inline styles.
            ("style-src", ["'self'", "'unsafe-inline'"]),
            ("img-src", ["'self'", "data:"]),
            ("connect-src", connectSources),
            ("frame-ancestors", ["'none'"]),
            ("base-uri", ["'self'"]),
            ("form-action", ["'self'"]),
            ("object-src", ["'none'"]),
        ];

        return string.Join("; ", directives.Select(d => $"{d.Directive} {string.Join(' ', d.Sources)}"));
    }
}
