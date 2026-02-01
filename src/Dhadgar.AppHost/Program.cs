var builder = DistributedApplication.CreateBuilder(args);

// ═══════════════════════════════════════════════════════════════════════════
// Infrastructure Resources
// ═══════════════════════════════════════════════════════════════════════════

// PostgreSQL - Primary database for all services
var postgres = builder.AddPostgres("postgres")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("dhadgar-postgres-data")
    .WithPgAdmin();

// Create databases for each service (database-per-service pattern)
var platformDb = postgres.AddDatabase("dhadgar-platform");
var identityDb = postgres.AddDatabase("dhadgar-identity");
var billingDb = postgres.AddDatabase("dhadgar-billing");

// Redis - Caching and session storage
var redis = builder.AddRedis("cache")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("dhadgar-redis-data")
    .WithRedisCommander();

// RabbitMQ - Message bus for async communication
var rabbitmq = builder.AddRabbitMQ("messaging")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("dhadgar-rabbitmq-data")
    .WithManagementPlugin();

// ═══════════════════════════════════════════════════════════════════════════
// Core Services (Production-Ready)
// ═══════════════════════════════════════════════════════════════════════════

// Gateway - API entry point (YARP reverse proxy)
var gateway = builder.AddProject<Projects.Dhadgar_Gateway>("gateway")
    .WithReference(redis)
    .WithExternalHttpEndpoints()
    .WaitFor(redis);

// Identity - User/org/role management
var identity = builder.AddProject<Projects.Dhadgar_Identity>("identity")
    .WithReference(identityDb)
    .WithReference(redis)
    .WithReference(rabbitmq)
    .WaitFor(postgres)
    .WaitFor(redis)
    .WaitFor(rabbitmq);

// Nodes - Agent enrollment, mTLS CA, heartbeats
var nodes = builder.AddProject<Projects.Dhadgar_Nodes>("nodes")
    .WithReference(platformDb)
    .WithReference(rabbitmq)
    .WaitFor(postgres)
    .WaitFor(rabbitmq);

// Secrets - Azure Key Vault integration (no database)
var secrets = builder.AddProject<Projects.Dhadgar_Secrets>("secrets")
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

// Note: BetterAuth is a Node.js project and not orchestrated via Aspire
// It runs separately via npm start

// Notifications - Email, webhook delivery
var notifications = builder.AddProject<Projects.Dhadgar_Notifications>("notifications")
    .WithReference(platformDb)
    .WithReference(rabbitmq)
    .WaitFor(postgres)
    .WaitFor(rabbitmq);

// Discord - Discord webhook integration
var discord = builder.AddProject<Projects.Dhadgar_Discord>("discord")
    .WithReference(platformDb)
    .WithReference(rabbitmq)
    .WaitFor(postgres)
    .WaitFor(rabbitmq);

// ═══════════════════════════════════════════════════════════════════════════
// Stub Services (Scaffolding)
// ═══════════════════════════════════════════════════════════════════════════

var billing = builder.AddProject<Projects.Dhadgar_Billing>("billing")
    .WithReference(billingDb)
    .WithReference(rabbitmq)
    .WaitFor(postgres)
    .WaitFor(rabbitmq);

var servers = builder.AddProject<Projects.Dhadgar_Servers>("servers")
    .WithReference(platformDb)
    .WithReference(rabbitmq)
    .WaitFor(postgres)
    .WaitFor(rabbitmq);

var tasks = builder.AddProject<Projects.Dhadgar_Tasks>("tasks")
    .WithReference(platformDb)
    .WithReference(rabbitmq)
    .WaitFor(postgres)
    .WaitFor(rabbitmq);

var console = builder.AddProject<Projects.Dhadgar_Console>("console")
    .WithReference(redis)
    .WithReference(rabbitmq)
    .WaitFor(redis)
    .WaitFor(rabbitmq);

var mods = builder.AddProject<Projects.Dhadgar_Mods>("mods")
    .WithReference(platformDb)
    .WithReference(rabbitmq)
    .WaitFor(postgres)
    .WaitFor(rabbitmq);

builder.Build().Run();
