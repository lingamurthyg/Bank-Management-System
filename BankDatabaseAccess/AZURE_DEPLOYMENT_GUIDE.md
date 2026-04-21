# Azure Deployment Configuration Guide

## Prerequisites

1. Azure subscription with appropriate permissions
2. Azure CLI installed and configured
3. .NET Framework 4.7.2 or higher
4. Visual Studio 2019 or higher (for building)

## Step 1: Create Azure Resources

### 1.1 Create Resource Group
```bash
az group create \
  --name bank-app-rg \
  --location eastus
```

### 1.2 Create Azure SQL Database
```bash
# Create SQL Server
az sql server create \
  --name bank-sql-server \
  --resource-group bank-app-rg \
  --location eastus \
  --admin-user sqladmin \
  --admin-password 'YourSecurePassword123!'

# Create Database
az sql db create \
  --resource-group bank-app-rg \
  --server bank-sql-server \
  --name BankDB \
  --service-objective S0

# Configure firewall (allow Azure services)
az sql server firewall-rule create \
  --resource-group bank-app-rg \
  --server bank-sql-server \
  --name AllowAzureServices \
  --start-ip-address 0.0.0.0 \
  --end-ip-address 0.0.0.0
```

### 1.3 Create Azure Storage Account
```bash
az storage account create \
  --name bankstorageacct \
  --resource-group bank-app-rg \
  --location eastus \
  --sku Standard_LRS \
  --kind StorageV2

# Create blob containers
az storage container create \
  --name bank-data \
  --account-name bankstorageacct

az storage container create \
  --name customer-logs \
  --account-name bankstorageacct

az storage container create \
  --name deposit-cache \
  --account-name bankstorageacct
```

### 1.4 Create Azure Service Bus
```bash
# Create Service Bus namespace
az servicebus namespace create \
  --name bank-servicebus \
  --resource-group bank-app-rg \
  --location eastus \
  --sku Standard

# Create queues
az servicebus queue create \
  --name transfer-audit \
  --namespace-name bank-servicebus \
  --resource-group bank-app-rg

az servicebus queue create \
  --name customer-info-audit \
  --namespace-name bank-servicebus \
  --resource-group bank-app-rg
```

### 1.5 Create Azure App Configuration
```bash
az appconfig create \
  --name bank-appconfig \
  --resource-group bank-app-rg \
  --location eastus \
  --sku Standard
```

### 1.6 Create Azure AD App Registration
```bash
# Create app registration
az ad app create \
  --display-name "Bank Management System" \
  --sign-in-audience AzureADMyOrg

# Note the Application (client) ID and Directory (tenant) ID
```

## Step 2: Configure Managed Identity

### 2.1 Create User-Assigned Managed Identity
```bash
az identity create \
  --name bank-app-identity \
  --resource-group bank-app-rg \
  --location eastus

# Get the identity details
az identity show \
  --name bank-app-identity \
  --resource-group bank-app-rg
```

### 2.2 Assign Permissions to Managed Identity

#### SQL Database Access
```bash
# Get the managed identity principal ID
IDENTITY_PRINCIPAL_ID=$(az identity show \
  --name bank-app-identity \
  --resource-group bank-app-rg \
  --query principalId -o tsv)

# Assign SQL DB Contributor role
az role assignment create \
  --assignee $IDENTITY_PRINCIPAL_ID \
  --role "SQL DB Contributor" \
  --scope /subscriptions/{subscription-id}/resourceGroups/bank-app-rg/providers/Microsoft.Sql/servers/bank-sql-server
```

#### Storage Account Access
```bash
# Assign Storage Blob Data Contributor role
az role assignment create \
  --assignee $IDENTITY_PRINCIPAL_ID \
  --role "Storage Blob Data Contributor" \
  --scope /subscriptions/{subscription-id}/resourceGroups/bank-app-rg/providers/Microsoft.Storage/storageAccounts/bankstorageacct
```

#### Service Bus Access
```bash
# Assign Azure Service Bus Data Owner role
az role assignment create \
  --assignee $IDENTITY_PRINCIPAL_ID \
  --role "Azure Service Bus Data Owner" \
  --scope /subscriptions/{subscription-id}/resourceGroups/bank-app-rg/providers/Microsoft.ServiceBus/namespaces/bank-servicebus
```

#### App Configuration Access
```bash
# Assign App Configuration Data Reader role
az role assignment create \
  --assignee $IDENTITY_PRINCIPAL_ID \
  --role "App Configuration Data Reader" \
  --scope /subscriptions/{subscription-id}/resourceGroups/bank-app-rg/providers/Microsoft.AppConfiguration/configurationStores/bank-appconfig
```

## Step 3: Configure Environment Variables

### 3.1 Get Connection Details
```bash
# SQL Connection String
az sql db show-connection-string \
  --client ado.net \
  --server bank-sql-server \
  --name BankDB

# Storage Blob Endpoint
az storage account show \
  --name bankstorageacct \
  --resource-group bank-app-rg \
  --query primaryEndpoints.blob -o tsv

# Service Bus Namespace
az servicebus namespace show \
  --name bank-servicebus \
  --resource-group bank-app-rg \
  --query serviceBusEndpoint -o tsv

# App Configuration Endpoint
az appconfig show \
  --name bank-appconfig \
  --resource-group bank-app-rg \
  --query endpoint -o tsv
```

### 3.2 Set Environment Variables (for local testing)
```bash
export SQL_CONNECTION_STRING="Server=tcp:bank-sql-server.database.windows.net,1433;Database=BankDB;..."
export AZURE_STORAGE_BLOB_ENDPOINT="https://bankstorageacct.blob.core.windows.net"
export AZURE_STORAGE_CONTAINER_NAME="bank-data"
export AZURE_SERVICEBUS_NAMESPACE="bank-servicebus.servicebus.windows.net"
export AZURE_SERVICEBUS_QUEUE_NAME="transfer-audit"
export AZURE_APPCONFIG_ENDPOINT="https://bank-appconfig.azconfig.io"
export AZURE_AD_TENANT_ID="your-tenant-id"
export AZURE_AD_CLIENT_ID="your-client-id"
export ASPNETCORE_ENVIRONMENT="Production"
```

## Step 4: Deploy to Azure Kubernetes Service (AKS)

### 4.1 Create AKS Cluster
```bash
az aks create \
  --resource-group bank-app-rg \
  --name bank-aks-cluster \
  --node-count 2 \
  --enable-managed-identity \
  --enable-workload-identity \
  --enable-oidc-issuer \
  --generate-ssh-keys
```

### 4.2 Configure Workload Identity
```bash
# Get AKS OIDC issuer URL
AKS_OIDC_ISSUER=$(az aks show \
  --name bank-aks-cluster \
  --resource-group bank-app-rg \
  --query oidcIssuerProfile.issuerUrl -o tsv)

# Create federated identity credential
az identity federated-credential create \
  --name bank-app-federated-identity \
  --identity-name bank-app-identity \
  --resource-group bank-app-rg \
  --issuer $AKS_OIDC_ISSUER \
  --subject system:serviceaccount:default:bank-app-sa
```

### 4.3 Create Kubernetes Service Account
```yaml
# bank-app-serviceaccount.yaml
apiVersion: v1
kind: ServiceAccount
metadata:
  name: bank-app-sa
  namespace: default
  annotations:
    azure.workload.identity/client-id: <MANAGED_IDENTITY_CLIENT_ID>
```

```bash
kubectl apply -f bank-app-serviceaccount.yaml
```

### 4.4 Create ConfigMap for Environment Variables
```yaml
# bank-app-configmap.yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: bank-app-config
  namespace: default
data:
  AZURE_STORAGE_BLOB_ENDPOINT: "https://bankstorageacct.blob.core.windows.net"
  AZURE_STORAGE_CONTAINER_NAME: "bank-data"
  AZURE_SERVICEBUS_NAMESPACE: "bank-servicebus.servicebus.windows.net"
  AZURE_SERVICEBUS_QUEUE_NAME: "transfer-audit"
  AZURE_APPCONFIG_ENDPOINT: "https://bank-appconfig.azconfig.io"
  ASPNETCORE_ENVIRONMENT: "Production"
```

```bash
kubectl apply -f bank-app-configmap.yaml
```

### 4.5 Create Secret for Sensitive Data
```bash
kubectl create secret generic bank-app-secrets \
  --from-literal=SQL_CONNECTION_STRING="Server=tcp:bank-sql-server.database.windows.net,1433;..." \
  --from-literal=AZURE_AD_TENANT_ID="your-tenant-id" \
  --from-literal=AZURE_AD_CLIENT_ID="your-client-id"
```

## Step 5: Deploy to Azure Container Apps (Alternative)

### 5.1 Create Container Apps Environment
```bash
az containerapp env create \
  --name bank-app-env \
  --resource-group bank-app-rg \
  --location eastus
```

### 5.2 Deploy Container App
```bash
az containerapp create \
  --name bank-app \
  --resource-group bank-app-rg \
  --environment bank-app-env \
  --image your-registry/bank-app:latest \
  --target-port 80 \
  --ingress external \
  --user-assigned bank-app-identity \
  --env-vars \
    AZURE_STORAGE_BLOB_ENDPOINT="https://bankstorageacct.blob.core.windows.net" \
    AZURE_STORAGE_CONTAINER_NAME="bank-data" \
    AZURE_SERVICEBUS_NAMESPACE="bank-servicebus.servicebus.windows.net" \
    AZURE_SERVICEBUS_QUEUE_NAME="transfer-audit" \
    ASPNETCORE_ENVIRONMENT="Production"
```

## Step 6: Verify Deployment

### 6.1 Test Database Connection
```bash
# Connect to SQL Database and verify schema
sqlcmd -S bank-sql-server.database.windows.net -d BankDB -U sqladmin -P 'YourSecurePassword123!'
```

### 6.2 Test Storage Access
```bash
# List containers
az storage container list --account-name bankstorageacct
```

### 6.3 Test Service Bus
```bash
# Send test message
az servicebus queue send \
  --namespace-name bank-servicebus \
  --name transfer-audit \
  --body "Test message"
```

### 6.4 Monitor Application Logs
```bash
# For AKS
kubectl logs -l app=bank-app --tail=100

# For Container Apps
az containerapp logs show \
  --name bank-app \
  --resource-group bank-app-rg \
  --tail 100
```

## Step 7: Security Hardening

### 7.1 Enable Azure Key Vault
```bash
az keyvault create \
  --name bank-keyvault \
  --resource-group bank-app-rg \
  --location eastus

# Store secrets
az keyvault secret set \
  --vault-name bank-keyvault \
  --name sql-connection-string \
  --value "Server=tcp:bank-sql-server.database.windows.net,1433;..."
```

### 7.2 Configure Network Security
```bash
# Enable private endpoints for SQL Database
az sql server vnet-rule create \
  --server bank-sql-server \
  --name AllowAKS \
  --resource-group bank-app-rg \
  --vnet-name aks-vnet \
  --subnet aks-subnet
```

### 7.3 Enable Azure Monitor
```bash
# Create Log Analytics workspace
az monitor log-analytics workspace create \
  --resource-group bank-app-rg \
  --workspace-name bank-app-logs

# Enable Container Insights for AKS
az aks enable-addons \
  --resource-group bank-app-rg \
  --name bank-aks-cluster \
  --addons monitoring \
  --workspace-resource-id /subscriptions/{subscription-id}/resourceGroups/bank-app-rg/providers/Microsoft.OperationalInsights/workspaces/bank-app-logs
```

## Troubleshooting

### Common Issues

1. **Authentication Failures**
   - Verify Managed Identity is assigned correct roles
   - Check AZURE_CLIENT_ID environment variable
   - Ensure Workload Identity is properly configured

2. **Connection Timeouts**
   - Verify firewall rules on SQL Server
   - Check network security groups
   - Ensure private endpoints are configured correctly

3. **Storage Access Denied**
   - Verify Storage Blob Data Contributor role assignment
   - Check storage account firewall settings
   - Ensure container exists

4. **Service Bus Errors**
   - Verify Azure Service Bus Data Owner role
   - Check queue names match configuration
   - Ensure namespace is accessible

## Monitoring and Maintenance

### Set Up Alerts
```bash
# Create alert for failed requests
az monitor metrics alert create \
  --name high-error-rate \
  --resource-group bank-app-rg \
  --scopes /subscriptions/{subscription-id}/resourceGroups/bank-app-rg \
  --condition "avg Percentage CPU > 80" \
  --description "Alert when CPU usage is high"
```

### Regular Maintenance Tasks
1. Review and rotate secrets monthly
2. Update NuGet packages for security patches
3. Monitor Azure costs and optimize resources
4. Review access logs and audit trails
5. Test disaster recovery procedures

## Cost Optimization

1. Use Azure Reserved Instances for predictable workloads
2. Enable auto-scaling for AKS or Container Apps
3. Use Azure Hybrid Benefit for Windows licenses
4. Implement lifecycle policies for blob storage
5. Monitor and optimize SQL Database DTU usage

## Support and Documentation

- Azure Documentation: https://docs.microsoft.com/azure
- AKS Documentation: https://docs.microsoft.com/azure/aks
- Container Apps Documentation: https://docs.microsoft.com/azure/container-apps
- Workload Identity: https://docs.microsoft.com/azure/aks/workload-identity-overview
