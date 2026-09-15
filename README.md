# ki-satsingen

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
  "EntraConfiguration": {
    "Instance": "https://login.microsoftonline.com",
    "TenantId": "<tenant-id>",
    "ClientId": "<client-id>",
    "ClientSecret": "<client-secret>",
    "CallbackPath": "/signin-oidc",
    "Audience": "<application-id-uri-from-appreg>",
    "AppRoleAdministrator": "Administrator",
    "AppRoleUser": "User",
    "AppRoleMetrics": "Metrics",
    "AppRoleContributor": "Contributor"
  },
  "OpenAI": {
    "ApiKey": "sk-proj-...."
  },
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=kisatsingen_dev_db;Username=kisatsingen_web_app;Password=kisatsingen_web_app_local_dev_pass;SSL Mode=Prefer;Trust Server Certificate=true",
    "MigrationConnection": "Host=localhost;Port=5432;Database=kisatsingen_dev_db;Username=local_user;Password=local_password;SSL Mode=Prefer;Trust Server Certificate=true"
  }
}
```
- run app `dotnet watch --project kisatsingen`

### Local db
#### Startup postgres
- `docker compose up -d`

#### Stop postgres
- `docker compose down`

#### How to reset local db (wipes data as well)
- `docker compose down -v`
- `docker compose up -d`