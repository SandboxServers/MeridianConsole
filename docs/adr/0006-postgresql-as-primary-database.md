# ADR-0006: PostgreSQL as Primary Database

## Status

Accepted

## Context

Each microservice needs persistent storage. Options considered:

1. **SQL Server** - Microsoft's flagship RDBMS, excellent .NET integration
2. **PostgreSQL** - Open-source, feature-rich, widely deployed
3. **MySQL/MariaDB** - Popular open-source, less feature-rich
4. **MongoDB** - Document database, different paradigm
5. **Mix per service** - Choose best fit per domain

## Decision

Use PostgreSQL as the primary database for all services.

Reasoning:
- **Cost**: No licensing fees, important for self-hosted (KiP) edition
- **Features**: JSONB columns, full-text search, excellent extension ecosystem
- **Cloud options**: Available as managed service on all major clouds
- **Performance**: Comparable to SQL Server for our workloads
- **EF Core support**: Npgsql provider is mature and well-maintained

Configuration patterns:
- `xmin` system column for optimistic concurrency (no explicit version columns)
- JSONB for flexible metadata storage
- Database-per-service isolation
- Connection pooling via PgBouncer in production

## Consequences

### Positive

- Zero licensing cost, important for self-hosted deployments
- Rich feature set (JSONB, arrays, full-text search)
- Excellent managed options (Azure Database for PostgreSQL, AWS RDS, etc.)
- Strong community and tooling ecosystem
- Works well with EF Core migrations

### Negative

- Less native integration with Azure than SQL Server
- Some .NET developers less familiar with PostgreSQL
- Different SQL dialect from T-SQL (minor learning curve)

### Neutral

- InMemory and SQLite providers used for testing
- `DhadgarDbContext` base class handles provider-specific differences
