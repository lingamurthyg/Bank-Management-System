# Bank Management System - Cloud Readiness Configuration

## Overview
This document describes the cloud-native configurations and environment variables required for deploying the Bank Management System to Azure.

## Cloud Readiness Fixes Applied

### 1. Windows Authentication → Azure AD Authentication
- **Blocker**: cr-dotnet-0030
- **Fix**: Replaced Windows Authentication (NTLM/Kerberos) with Azure AD authentication using Microsoft Identity Client
- **Configuration Required**:
  - `AZURE_AD_CLIENT_ID`: Azure AD application client ID
  - `AZURE_AD_TENANT_ID`: Azure AD tenant ID

### 2. P/Invoke Windows APIs → Cross-Platform .NET
- **Blocker**: cr-dotnet-0042
- **Fix**: Removed P/Invoke calls to user32.dll (LockWorkStation) and replaced with cross-platform alternatives

### 3. MSMQ → Azure Service Bus
- **Blocker**: cr-dotnet-0043
- **Fix**: Replaced System.Messaging.MessageQueue with Azure.Messaging.ServiceBus
- **Configuration Required**:
  - `AZURE_SERVICEBUS_NAMESPACE`: Service Bus namespace (e.g., "bankservicebus")
  - `AZURE_SERVICEBUS_CONNECTION_STRING`: (Optional) Connection string for fallback
  - Queue names configured in appsettings.json

### 4. Hard-coded File Paths → Azure Blob Storage
- **Blockers**: cr-dotnet-0001, cr-dotnet-0002, cr-dotnet-0003
- **Fix**: Replaced hard-coded Windows paths (C:\, D:\) with Azure Blob Storage using BlobClient
- **Configuration Required**:
  - `AZURE_STORAGE_ACCOUNT_NAME`: Storage account name
  - `AZURE_STORAGE_CONNECTION_STRING`: (Optional) Connection string for fallback
  - Container names configured in appsettings.json

### 5. Windows Registry → Azure App Configuration
- **Blocker**: cr-dotnet-0040
- **Fix**: Replaced Microsoft.Win32.Registry with configuration-based approach using Azure App Configuration
- **Configuration Required**:
  - Configuration values in appsettings.json or Azure App Configuration service

### 6. DateTime.Now → DateTimeOffset.UtcNow
- **Blocker**: cr-dotnet-0121
- **Fix**: Replaced DateTime.Now with DateTimeOffset.UtcNow for timezone consistency across distributed environments
- **Note**: User timezone preferences should be stored in database and converted at presentation layer

### 7. SqlConnection → Entity Framework Core
- **Blocker**: cr-dotnet-0013
- **Fix**: Migrated to Entity Framework Core with Azure SQL connection resiliency
- **Configuration Required**:
  - `SQL_CONNECTION_STRING`: Azure SQL Database connection string

### 8. Web.config Transformations → Environment-based Configuration
- **Blocker**: cr-dotnet-0010
- **Fix**: Replaced Web.config transformations with appsettings.{Environment}.json files
- **Configuration Required**:
  - `ASPNETCORE_ENVIRONMENT`: Environment name (Development, Production, etc.)

## Environment Variables

### Required for Production Deployment

```bash
# Azure AD Authentication
AZURE_AD_CLIENT_ID=<your-azure-ad-client-id>
AZURE_AD_TENANT_ID=<your-azure-ad-tenant-id>

# Azure SQL Database
SQL_CONNECTION_STRING=Server=tcp:<server>.database.windows.net,1433;Database=BankDB;User ID=<user>;Password=<password>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;

# Azure Storage Account
AZURE_STORAGE_ACCOUNT_NAME=<storage-account-name>
# Optional fallback:
# AZURE_STORAGE_CONNECTION_STRING=<connection-string>

# Azure Service Bus
AZURE_SERVICEBUS_NAMESPACE=<servicebus-namespace>
# Optional fallback:
# AZURE_SERVICEBUS_CONNECTION_STRING=<connection-string>

# Environment
ASPNETCORE_ENVIRONMENT=Production
```

### Optional Configuration

```bash
# Azure App Configuration
AZURE_APP_CONFIG_ENDPOINT=https://<appconfig-name>.azconfig.io

# Custom container names (override appsettings.json)
AZURE_STORAGE_CONTAINER_NAME=customer-logs
AZURE_SERVICEBUS_QUEUE_NAME=transfer-audit
```

## Azure Resources Required

### 1. Azure AD Application Registration
- Register application in Azure AD
- Configure redirect URIs for desktop application
- Grant necessary API permissions (User.Read)

### 2. Azure Storage Account
- Create storage account with blob containers:
  - `customer-logs`: Customer activity logs
  - `transfer-state`: Transfer transaction state
  - `deposit-cache`: Deposit amount cache

### 3. Azure Service Bus Namespace
- Create Service Bus namespace
- Create queues:
  - `transfer-audit`: Transfer audit messages
  - `customer-info`: Customer info view events

### 4. Azure SQL Database
- Create Azure SQL Database
- Configure firewall rules
- Enable connection resiliency

### 5. Azure Kubernetes Service (AKS) - Optional
- Configure Workload Identity for pod-level managed identity
- No connection strings or keys needed in code

## Workload Identity Configuration (AKS)

When deploying to AKS, configure Workload Identity for credential-free access:

1. Create managed identity
2. Assign RBAC roles:
   - Storage Blob Data Contributor (for Blob Storage)
   - Azure Service Bus Data Owner (for Service Bus)
   - SQL DB Contributor (for Azure SQL)
3. Configure pod identity binding
4. No connection strings needed - DefaultAzureCredential handles authentication

## Configuration Files

### appsettings.json
Base configuration with default values

### appsettings.Development.json
Development environment overrides

### appsettings.Production.json
Production environment overrides

## Migration Notes

### Stateful Components Removed
- Static variables replaced with distributed storage
- Session state should use Redis Cache or database
- File system dependencies eliminated

### Cross-Platform Compatibility
- All Windows-specific APIs removed
- Compatible with Linux containers
- No P/Invoke dependencies

### 12-Factor App Compliance
- Configuration externalized via environment variables
- Stateless application design
- Cloud-native storage patterns
- Distributed message queuing

## Testing Locally

### Prerequisites
1. Install Azure CLI: `az login`
2. Install Azure Storage Emulator or use Azurite
3. Configure local environment variables

### Local Development
```bash
# Set environment to Development
export ASPNETCORE_ENVIRONMENT=Development

# Use local SQL Server
export SQL_CONNECTION_STRING="Server=(localdb)\\mssqllocaldb;Database=BankDB_Dev;Trusted_Connection=True"

# Use Azure Storage Emulator
export AZURE_STORAGE_CONNECTION_STRING="UseDevelopmentStorage=true"

# Use Azure Service Bus (requires Azure subscription)
export AZURE_SERVICEBUS_CONNECTION_STRING="<your-dev-connection-string>"
```

## Deployment Checklist

- [ ] Azure AD application registered
- [ ] Azure Storage account created with containers
- [ ] Azure Service Bus namespace created with queues
- [ ] Azure SQL Database created and configured
- [ ] Environment variables configured
- [ ] Workload Identity configured (for AKS)
- [ ] Connection strings secured in Azure Key Vault
- [ ] Application Insights configured for monitoring
- [ ] RBAC roles assigned to managed identity

## Monitoring and Logging

### Application Insights
Configure Application Insights for:
- Exception tracking
- Performance monitoring
- Custom telemetry
- Distributed tracing

### Azure Monitor
Monitor:
- Storage account metrics
- Service Bus queue depth
- SQL Database performance
- Container health (AKS)

## Security Best Practices

1. **Never commit secrets**: Use Azure Key Vault or environment variables
2. **Use Managed Identity**: Prefer Workload Identity over connection strings
3. **Enable encryption**: Use HTTPS, TLS 1.2+, encrypted storage
4. **Implement RBAC**: Least privilege access to Azure resources
5. **Audit logging**: Enable diagnostic logs for all Azure resources

## Support

For issues or questions:
- Review Azure documentation
- Check Application Insights for errors
- Verify environment variables are set correctly
- Ensure RBAC permissions are configured
