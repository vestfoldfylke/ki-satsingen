# ki-satsingen

## Docs
- [Knowledge files in chat](docs/knowledge-files.md): how attached files become knowledge files the model can read

## Hosting
Host on Linux. Windows works for local development, but hosting on Windows is not tested or supported: the attachment temp folder relies on Unix file permissions and on one app process per temp folder.

## Entra App Registration permissions

This app requires the following `Microsoft Graph` permissions:
- Delegated
  - User.Read
- Application
  - User.Read.All

## Database cleanup

Database cleanup is performed at given time targets set in **DatabaseCleanup** configuration sections.

Both `TargetTime` and `Retention` needs to be defined and have valid values for a configuration section to be enabled.

### TargetTime

The `TargetTime` configuration is specified as "`hour`:`minute`:`second`", representing when the cleanup should start.<br />
Allowed value range: **>= *00:00:00* and <= *23:59:59***

### Retention

The `Retention` configuration is specified as "`days`.`hours`:`minutes`:`seconds`", representing how old an item can be before it will be deleted.<br />
Minimum value: **1.00:00:00** - Needs to be at least 1 day.<br />
Maximum value: Go nuts

#### ChatCleanup

Chat cleanup is performed at the given `TargetTime` set in **DatabaseCleanup.ChatCleanup.TargetTime** configuration.<br />
Chats older than the specified retention given by `Retention` set in **DatabaseCleanup.ChatCleanup.Retention** configuration, will be deleted at the given `TargetTime`.

## Dev
- First setup and run local dev-db with docker `docker compose up -d`
- create appsettings.Development.json
```json5
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "DatabaseCleanup": {
    "ChatCleanup": {
      "Retention": "30.00:00:00",
      "TargetTime": "03:00:00"
    }
  },
  "EntraAuthConfiguration": {
    "Instance": "https://login.microsoftonline.com",
    "TenantId": "<tenant-id>",
    "ClientId": "<client-id>",
    "ClientSecret": "<client-secret>",
    "CallbackPath": "/signin-oidc",
    "Audience": "<application-id-uri-from-appreg>",
    "SignedOutCallbackPath": "/signout-callback-oidc",
    "AppRoleAdministrator": "Administrator",
    "AppRoleUser": "User",
    "AppRoleMetrics": "Metrics",
    "AppRoleContributor": "Contributor"
  },
  "OpenAI": {
    "ApiKey": "sk-proj-...."
  },
  "Mistral": {
    "ApiKey": "kijoij..."
  },
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=kisatsingen_dev_db;Username=kisatsingen_web_app;Password=kisatsingen_web_app_local_dev_pass;SSL Mode=Prefer;Trust Server Certificate=true",
    "MigrationConnection": "Host=localhost;Port=5432;Database=kisatsingen_dev_db;Username=local_user;Password=local_password;SSL Mode=Prefer;Trust Server Certificate=true"
  },
  "KnowledgeFile": {
    "MaxEstimatedTokenCount": "500000"
  },
  // Optional (default value shown below)
  // "Attachments": {
  //   "MaxFileBytes": 52428800,              // 50 MiB
  //   "MaxPendingFilesPerUser": 10,
  //   "MaxPendingBytesPerUser": 209715200,   // 200 MiB
  //   "MaxPendingBytesPerInstance": 2097152000 // 2000 MiB
  // },
  // "KnowledgeFilePipeline": {
  //   "MaxConcurrentConversions": 2,         // default: the number of CPU cores
  //   "ConversionTimeout": "00:02:00"
  // },
  // "Graph": {
  //   "BaseUrl": "https://graph.microsoft.com/v1.0",
  //   "Scope": "https://graph.microsoft.com/.default"
  // }
}
```
- run app `dotnet watch --project kisatsingen`

### Formatting
Rules live in `.editorconfig`. `dotnet format` covers C# only; Razor markup is formatted by hand (see [CONTRIBUTING.md](CONTRIBUTING.md)).
- Check: `dotnet format --verify-no-changes`
- Fix: `dotnet format`

### Designsystemet theme
The theme (colours, font, border radius) is defined in `kisatsingen/browser-designsystemet/designsystemet.config.json`. The build generates the theme CSS from it, and the generated files are not committed.

To upgrade Designsystemet, set the same exact version on all three `@digdir` packages in `kisatsingen/browser-designsystemet/package.json`, then run `npm install` and build. The generated theme must match the tokens `@digdir/designsystemet-css` reads.

### Local db
#### Startup postgres
- `docker compose up -d`

#### Stop postgres
- `docker compose down`

#### How to reset local db (wipes data as well)
- `docker compose down -v`
- `docker compose up -d`
