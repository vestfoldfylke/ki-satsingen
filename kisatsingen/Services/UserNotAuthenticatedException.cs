namespace kisatsingen.Services;

public sealed class UserNotAuthenticatedException() : Exception(
    "No authenticated user or valid objectidentifier claim found.");
