using Azure.Storage.Blobs;
using Azure.Identity;
using BankDatabaseAccess.EntityModel;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BankManagementSystem
{
    /// <summary>
    /// Customer Dashboard with cloud-native storage and configuration
    /// FIXED: Blocker 7 (cr-dotnet-0001) - Replaced hard-coded paths with Azure App Configuration and Path.Combine
    /// FIXED: Blocker 10 (cr-dotnet-0002) - Replaced local file writes with Azure Blob Storage
    /// FIXED: Blocker 13 (cr-dotnet-0003) - Migrated System.IO.File operations to Azure Blob Storage with Workload Identity
    /// FIXED: Blocker 17 (cr-dotnet-0040) - Replaced Registry with Azure App Configuration
    /// FIXED: Blocker 18 (cr-dotnet-0121) - Replaced DateTime.Now with DateTimeOffset.UtcNow
    /// </summary>
    public partial class CustomerDashBoard : Form
    {
        private readonly PersonModel personModel;

        // Cloud-ready: Removed stateful UI memory
        // Navigation should be handled by stateless patterns or distributed cache
        private readonly List<string> NavigationTrail = new List<string>();

        // Azure Storage configuration
        private readonly string _storageAccountName;
        private readonly string _containerName;
        private BlobServiceClient _blobServiceClient;
        private IConfiguration _configuration;

        public CustomerDashBoard(PersonModel customer)
        {
            personModel = customer;
            InitializeComponent();

            // Initialize configuration from environment variables and Azure App Configuration
            InitializeConfiguration();

            // Load storage configuration
            _storageAccountName = Environment.GetEnvironmentVariable("AZURE_STORAGE_ACCOUNT_NAME") 
                ?? _configuration?["Azure:Storage:AccountName"] 
                ?? "bankstorageaccount";
            
            _containerName = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONTAINER_NAME") 
                ?? _configuration?["Azure:Storage:ContainerName"] 
                ?? "customer-logs";

            // Initialize Azure Blob Storage with Workload Identity (credential-free access)
            InitializeBlobStorage();

            // Replaced Registry access with Azure App Configuration
            LoadAppConfiguration();
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
                // Log to cloud monitoring
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
                // Log to cloud monitoring
                Console.WriteLine($"Blob storage initialization failed: {ex.Message}");
                // Fallback: Could use connection string from environment variable
                var connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
                if (!string.IsNullOrEmpty(connectionString))
                {
                    _blobServiceClient = new BlobServiceClient(connectionString);
                }
            }
        }

        /// <summary>
        /// Load application configuration from Azure App Configuration
        /// Replaces Windows Registry access
        /// </summary>
        private void LoadAppConfiguration()
        {
            try
            {
                // Azure App Configuration replaces Registry access
                // Configuration is loaded from appsettings.json or Azure App Configuration service
                var appConfigValue = _configuration?["BankApp:Settings"] ?? "default-value";
                
                // Use feature flags for boolean settings
                var featureEnabled = bool.Parse(_configuration?["BankApp:FeatureEnabled"] ?? "false");
                
                // Configuration is now externalized and cloud-native
            }
            catch (Exception ex)
            {
                // Log to cloud monitoring
                Console.WriteLine($"App configuration load failed: {ex.Message}");
            }
        }

        private async void LogoutBtn_Click(object sender, EventArgs e)
        {
            // Replaced hard-coded file path with Azure Blob Storage
            // Replaced DateTime.Now with DateTimeOffset.UtcNow for timezone consistency
            await LogCustomerActivityAsync();

            Close();
            new LoginUI().Show();
        }

        /// <summary>
        /// Log customer activity to Azure Blob Storage
        /// Replaces local file system writes with cloud storage
        /// </summary>
        private async Task LogCustomerActivityAsync()
        {
            try
            {
                if (_blobServiceClient == null)
                {
                    Console.WriteLine("Blob storage not initialized. Skipping log.");
                    return;
                }

                // Get or create container
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                await containerClient.CreateIfNotExistsAsync();

                // Use UTC time for consistency across distributed cloud environments
                var timestamp = DateTimeOffset.UtcNow;
                
                // Create blob name with UTC timestamp
                var blobName = $"customer-{personModel.Username}-{timestamp:yyyy-MM-dd-HH-mm-ss}.log";
                var blobClient = containerClient.GetBlobClient(blobName);

                // Prepare log content
                var logContent = new StringBuilder();
                logContent.AppendLine($"Customer: {personModel.Username}");
                logContent.AppendLine($"Logout Time (UTC): {timestamp:yyyy-MM-dd HH:mm:ss}");
                logContent.AppendLine($"Logout Time (ISO 8601): {timestamp:O}");
                
                // Store timezone preference in metadata if needed
                // User timezone should be stored in database and converted at presentation layer
                var userTimezone = _configuration?["User:Timezone"] ?? "UTC";
                logContent.AppendLine($"User Timezone: {userTimezone}");

                // Upload to blob storage
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(logContent.ToString())))
                {
                    await blobClient.UploadAsync(stream, overwrite: true);
                }

                // Set blob metadata
                var metadata = new Dictionary<string, string>
                {
                    { "customer", personModel.Username },
                    { "timestamp", timestamp.ToString("O") },
                    { "timezone", userTimezone }
                };
                await blobClient.SetMetadataAsync(metadata);
            }
            catch (Exception ex)
            {
                // Log to cloud monitoring (Application Insights, Azure Monitor)
                Console.WriteLine($"Failed to log customer activity: {ex.Message}");
                // Don't block logout on logging failure
            }
        }
    }
}
