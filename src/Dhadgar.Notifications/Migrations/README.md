# EF Core migrations (Dhadgar.Notifications)

This folder is intentionally empty until you create the first migration.

## Create the first migration
From repo root:
```bash
dotnet tool restore
dotnet ef migrations add InitialCreate -p src/Dhadgar.Notifications/Dhadgar.Notifications.csproj -s src/Dhadgar.Notifications/Dhadgar.Notifications.csproj -o Migrations
```

## Apply migrations
```bash
dotnet ef database update -p src/Dhadgar.Notifications/Dhadgar.Notifications.csproj -s src/Dhadgar.Notifications/Dhadgar.Notifications.csproj
```

Notes:
- Each service owns its schema and migrations.
- Use environment variables / appsettings for real connection strings.
