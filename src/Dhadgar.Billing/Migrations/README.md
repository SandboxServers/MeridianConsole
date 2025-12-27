# EF Core migrations (Dhadgar.Billing)

This folder is intentionally empty until you create the first migration.

## Create the first migration
From repo root:
```bash
dotnet tool restore
dotnet ef migrations add InitialCreate -p src/Dhadgar.Billing/Dhadgar.Billing.csproj -s src/Dhadgar.Billing/Dhadgar.Billing.csproj -o Migrations
```

## Apply migrations
```bash
dotnet ef database update -p src/Dhadgar.Billing/Dhadgar.Billing.csproj -s src/Dhadgar.Billing/Dhadgar.Billing.csproj
```

Notes:
- Each service owns its schema and migrations.
- Use environment variables / appsettings for real connection strings.
