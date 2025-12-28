# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["src/Dhadgar.CodeReview/Dhadgar.CodeReview.csproj", "Dhadgar.CodeReview/"]
COPY ["Directory.Packages.props", "./"]
COPY ["Directory.Build.props", "./"]
RUN dotnet restore "Dhadgar.CodeReview/Dhadgar.CodeReview.csproj"

# Copy source code and build
COPY ["src/Dhadgar.CodeReview/", "Dhadgar.CodeReview/"]

# Copy agent definitions from repo root
COPY [".claude/agents/", "agents/"]

WORKDIR "/src/Dhadgar.CodeReview"
RUN dotnet build "Dhadgar.CodeReview.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "Dhadgar.CodeReview.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Install SQLite (if needed for tools)
RUN apt-get update && \
    apt-get install -y sqlite3 && \
    rm -rf /var/lib/apt/lists/*

# Copy published app
COPY --from=publish /app/publish .

# Copy agent definitions to app directory
COPY --from=build /src/agents /app/agents

# Create directories
RUN mkdir -p /app/secrets /app/logs /app/data

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ConnectionStrings__CodeReview="Data Source=/app/data/codereview.db"

EXPOSE 8080

ENTRYPOINT ["dotnet", "Dhadgar.CodeReview.dll"]
