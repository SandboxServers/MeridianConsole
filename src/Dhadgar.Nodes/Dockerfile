# Dockerfile for Dhadgar.Nodes
# Build context: Solution root (MeridianConsole/)
# Build command: docker build -f src/Dhadgar.Nodes/Dockerfile -t dhadgar/nodes:latest .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution files
COPY ["Dhadgar.sln", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]
COPY ["global.json", "./"]

# Copy project files
COPY ["src/Dhadgar.Nodes/Dhadgar.Nodes.csproj", "src/Dhadgar.Nodes/"]
COPY ["src/Shared/Dhadgar.Contracts/Dhadgar.Contracts.csproj", "src/Shared/Dhadgar.Contracts/"]
COPY ["src/Shared/Dhadgar.Shared/Dhadgar.Shared.csproj", "src/Shared/Dhadgar.Shared/"]
COPY ["src/Shared/Dhadgar.Messaging/Dhadgar.Messaging.csproj", "src/Shared/Dhadgar.Messaging/"]
COPY ["src/Shared/Dhadgar.ServiceDefaults/Dhadgar.ServiceDefaults.csproj", "src/Shared/Dhadgar.ServiceDefaults/"]

# Restore
RUN dotnet restore "src/Dhadgar.Nodes/Dhadgar.Nodes.csproj"

# Copy source
COPY ["src/Dhadgar.Nodes/", "src/Dhadgar.Nodes/"]
COPY ["src/Shared/", "src/Shared/"]

# Build and publish
WORKDIR /src/src/Dhadgar.Nodes
RUN dotnet publish "Dhadgar.Nodes.csproj"     -c Release     -o /app/publish     --no-restore     /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

RUN apk add --no-cache curl

RUN addgroup -S appuser && adduser -S appuser -G appuser
COPY --from=build /app/publish .
RUN chown -R appuser:appuser /app
USER appuser

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3     CMD curl -f http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "Dhadgar.Nodes.dll"]
