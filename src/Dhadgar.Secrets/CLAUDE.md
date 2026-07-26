# Secrets Service

Secret storage and rotation.

## Tech Stack
- ASP.NET Core Minimal API
- Azure Key Vault integration (optional)

## Port
5110

## Status
Implemented — production-grade (24 endpoints, 143 tests).

## Implemented Features
- Azure Key Vault-backed secret read/write/rotate/delete (+ batch, OAuth set)
- Certificate and Key Vault management endpoints
- Claims-based authorization with permission hierarchy and break-glass access
- SIEM-compatible audit logging
- Tiered rate limiting (read/write/rotate)

## Known Gaps
- Deleted-vault purge not implemented (`AzureKeyVaultManager.cs:365`)
- `/api/v1/keyvaults` and `/api/v1/certificates` prefixes are not routed through the Gateway (only `/api/v1/secrets/*` is)

## Dependencies
- Dhadgar.Contracts
- Dhadgar.ServiceDefaults
