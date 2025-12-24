# EF Core migrations (Dhadgar.Tasks)

This folder is intentionally empty until you create the first migration.

## Create the first migration
From repo root:
```bash
dotnet tool restore
dotnet ef migrations add InitialCreate -p src/Dhadgar.Tasks/Dhadgar.Tasks.csproj -s src/Dhadgar.Tasks/Dhadgar.Tasks.csproj -o Migrations
```

## Apply migrations
```bash
dotnet ef database update -p src/Dhadgar.Tasks/Dhadgar.Tasks.csproj -s src/Dhadgar.Tasks/Dhadgar.Tasks.csproj
```

Notes:
- Each service owns its schema and migrations.
- Use environment variables / appsettings for real connection strings.
