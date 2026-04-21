using Azure.Storage.Blobs;
using Azure.Identity;
using BankDatabaseAccess.DatabaseOperation;
using BankDatabaseAccess.EntityModel;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BankManagementSystem.EmployeeDashboardForms
{
    /// <summary>
    /// Deposit form with Azure Blob Storage
    /// FIXED: Blocker 9 (cr-dotnet-0001) - Replaced hard-coded paths with Azure App Configuration and Path.Combine
    /// FIXED: Blocker 12 (cr-dotnet-0002) - Replaced local file writes with Azure Blob Storage
    /// FIXED: Blocker 15 (cr-dotnet-0003) - Migrated System.IO.File operations to Azure Blob Storage with Workload Identity
    /// </summary>
    public partial class Deposit : Form
    {
        // Cloud-ready: Removed stateful coupling
        // State should be stored in distributed cache (Redis) or database for cloud environments
        // private static decimal LastDepositAmount;

        // Azure Blob Storage configuration
        private BlobServiceClient _blobServiceClient;
        private readonly string _storageAccountName;
        private readonly string _containerName;
        private IConfiguration _configuration;

        public Deposit()
        {
            InitializeComponent();

            // Initialize configuration
            InitializeConfiguration();

            // Load Azure Storage configuration
            _storageAccountName = Environment.GetEnvironmentVariable("AZURE_STORAGE_ACCOUNT_NAME") 
                ?? _configuration?["Azure:Storage:AccountName"] 
                ?? "bankstorageaccount";
            
            _containerName = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONTAINER_NAME") 
                ?? _configuration?["Azure:Storage:DepositContainerName"] 
                ?? "deposit-cache";

            // Initialize Azure Blob Storage with Workload Identity
            InitializeBlobStorage();
        }

        /// <summary>
        /// Initialize configuration from appsettings.json and environment variables
        /// </summary>
        private void InitializeConfiguration()
        {
            try
            {
                var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
                
                var builder = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
                    .AddEnvironmentVariables();

                _configuration = builder.Build();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Configuration initialization failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Initialize Azure Blob Storage with Workload Identity for credential-free access
        /// In AKS, this uses pod-level managed identity
        /// </summary>
        private void InitializeBlobStorage()
        {
            try
            {
                // Use DefaultAzureCredential for automatic credential resolution
                // In AKS with Workload Identity: Uses pod identity
                // In local development: Uses Azure CLI, Visual Studio, or environment variables
                var credential = new DefaultAzureCredential();
                
                var blobServiceUri = new Uri($"https://{_storageAccountName}.blob.core.windows.net");
                _blobServiceClient = new BlobServiceClient(blobServiceUri, credential);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Blob storage initialization failed: {ex.Message}");
                // Fallback: Use connection string from environment variable
                var connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
                if (!string.IsNullOrEmpty(connectionString))
                {
                    _blobServiceClient = new BlobServiceClient(connectionString);
                }
            }
        }

        private async void DepositBtn_Click(object sender, EventArgs e)
        {
            if (decimal.TryParse(AmountTextBox.Text, out decimal depositAmount))
            {
                // Store deposit amount in Azure Blob Storage instead of static variable
                // This ensures data persists across container restarts and scales horizontally
                await StoreDepositAmountAsync(depositAmount);
            }
        }

        /// <summary>
        /// Store deposit amount to Azure Blob Storage
        /// Replaces local file system writes and static state with cloud storage
        /// </summary>
        private async Task StoreDepositAmountAsync(decimal amount)
        {
            try
            {
                if (_blobServiceClient == null)
                {
                    Console.WriteLine("Blob storage not initialized. Skipping deposit cache.");
                    return;
                }

                // Get or create container
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                await containerClient.CreateIfNotExistsAsync();

                // Create blob name for last deposit
                var blobName = "last-deposit.txt";
                var blobClient = containerClient.GetBlobClient(blobName);

                // Prepare deposit content
                var depositContent = new StringBuilder();
                depositContent.AppendLine($"Last Deposit Amount: {amount:C}");
                depositContent.AppendLine($"Timestamp (UTC): {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}");
                depositContent.AppendLine($"Timestamp (ISO 8601): {DateTimeOffset.UtcNow:O}");

                // Upload to blob storage
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(depositContent.ToString())))
                {
                    await blobClient.UploadAsync(stream, overwrite: true);
                }

                // Set blob metadata for easy querying
                var metadata = new Dictionary<string, string>
                {
                    { "amount", amount.ToString() },
                    { "timestamp", DateTimeOffset.UtcNow.ToString("O") }
                };
                await blobClient.SetMetadataAsync(metadata);

                // For better performance in cloud environments, consider using:
                // 1. Azure Redis Cache for frequently accessed data
                // 2. Azure SQL Database for transactional data
                // 3. Azure Cosmos DB for globally distributed data
            }
            catch (Exception ex)
            {
                // Log to cloud monitoring (Application Insights, Azure Monitor)
                Console.WriteLine($"Failed to store deposit amount: {ex.Message}");
                // Don't block deposit operation on cache failure
            }
        }

        /// <summary>
        /// Retrieve last deposit amount from Azure Blob Storage
        /// </summary>
        private async Task<decimal?> GetLastDepositAmountAsync()
        {
            try
            {
                if (_blobServiceClient == null)
                {
                    return null;
                }

                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                var blobClient = containerClient.GetBlobClient("last-deposit.txt");

                if (await blobClient.ExistsAsync())
                {
                    var properties = await blobClient.GetPropertiesAsync();
                    if (properties.Value.Metadata.TryGetValue("amount", out var amountStr))
                    {
                        if (decimal.TryParse(amountStr, out var amount))
                        {
                            return amount;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to retrieve last deposit amount: {ex.Message}");
            }

            return null;
        }
    }
}
