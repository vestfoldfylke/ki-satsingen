namespace kisatsingen.Services;

public sealed class UserNotAuthenticatedException() : Exception(
    "No authenticated user or objectidentifier claim found.");
