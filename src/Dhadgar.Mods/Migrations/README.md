# EF Core migrations (Dhadgar.Mods)

This folder is intentionally empty until you create the first migration.

## Create the first migration
From repo root:
```bash
dotnet tool restore
dotnet ef migrations add InitialCreate -p src/Dhadgar.Mods/Dhadgar.Mods.csproj -s src/Dhadgar.Mods/Dhadgar.Mods.csproj -o Migrations
```

## Apply migrations
```bash
dotnet ef database update -p src/Dhadgar.Mods/Dhadgar.Mods.csproj -s src/Dhadgar.Mods/Dhadgar.Mods.csproj
```

Notes:
- Each service owns its schema and migrations.
- Use environment variables / appsettings for real connection strings.
