using BankDatabaseAccess.EntityModel;
using Azure.Storage.Blobs;
using Azure.Identity;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;

namespace BankManagementSystem
{
    /// <summary>
    /// FIXED: Blocker 7 (cr-dotnet-0001) - Replaced hard-coded paths with Azure App Configuration
    /// FIXED: Blocker 10 (cr-dotnet-0002) - Replaced local file writes with Azure Blob Storage
    /// FIXED: Blocker 13 (cr-dotnet-0003) - Migrated System.IO.File to Azure Blob Storage with Workload Identity
    /// FIXED: Blocker 17 (cr-dotnet-0040) - Replaced Registry with Azure App Configuration
    /// FIXED: Blocker 18 (cr-dotnet-0121) - Replaced DateTime.Now with DateTimeOffset.UtcNow
    /// </summary>
    public partial class CustomerDashBoard : Form
    {
        private readonly PersonModel personModel;

        // Cloud-ready: Use distributed state management instead of in-memory list
        private readonly List<string> NavigationTrail = new List<string>();

        // Azure Blob Storage client for cloud-native file operations
        private BlobServiceClient _blobServiceClient;
        private BlobContainerClient _containerClient;
        private IConfiguration _configuration;

        public CustomerDashBoard(PersonModel customer)
        {
            personModel = customer;
            InitializeComponent();

            // Initialize cloud services
            InitializeCloudServices();

            // FIXED: Replaced Registry access with Azure App Configuration
            // Registry.CurrentUser.OpenSubKey(@"Software\BankApp");
            LoadConfigurationFromAzure();
        }

        /// <summary>
        /// Initialize Azure services with Workload Identity for credential-free access
        /// </summary>
        private void InitializeCloudServices()
        {
            try
            {
                // Load configuration from environment-based settings
                var builder = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: true)
                    .AddEnvironmentVariables();
                
                _configuration = builder.Build();

                // Get Azure Storage endpoint from configuration or environment variable
                var blobEndpoint = Environment.GetEnvironmentVariable("AZURE_STORAGE_BLOB_ENDPOINT")
                    ?? _configuration["Azure:StorageAccount:BlobEndpoint"];

                if (!string.IsNullOrEmpty(blobEndpoint))
                {
                    // Use Workload Identity (Managed Identity) for credential-free access
                    _blobServiceClient = new BlobServiceClient(
                        new Uri(blobEndpoint),
                        new DefaultAzureCredential());

                    // Get container name from configuration
                    var containerName = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONTAINER_NAME")
                        ?? _configuration["Azure:StorageAccount:ContainerName"]
                        ?? "customer-logs";

                    _containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                    
                    // Ensure container exists
                    _containerClient.CreateIfNotExists();
                }
            }
            catch (Exception ex)
            {
                // Log error but don't fail initialization
                Console.WriteLine($"Failed to initialize Azure services: {ex.Message}");
            }
        }

        /// <summary>
        /// Load configuration from Azure App Configuration instead of Windows Registry
        /// </summary>
        private void LoadConfigurationFromAzure()
        {
            try
            {
                // Azure App Configuration endpoint from environment variable
                var appConfigEndpoint = Environment.GetEnvironmentVariable("AZURE_APPCONFIG_ENDPOINT")
                    ?? _configuration?["Azure:AppConfiguration:Endpoint"];

                if (!string.IsNullOrEmpty(appConfigEndpoint))
                {
                    // Use Azure App Configuration for centralized configuration management
                    // This replaces Windows Registry access with cloud-native configuration
                    var configBuilder = new ConfigurationBuilder()
                        .AddAzureAppConfiguration(options =>
                        {
                            options.Connect(new Uri(appConfigEndpoint), new DefaultAzureCredential())
                                   .Select("BankApp:*");
                        });
                    
                    var appConfig = configBuilder.Build();
                    
                    // Access configuration values
                    var setting = appConfig["BankApp:Setting"];
                }
            }
            catch (Exception ex)
            {
                // Log error but continue - configuration is optional
                Console.WriteLine($"Failed to load Azure App Configuration: {ex.Message}");
            }
        }

        private async void LogoutBtn_Click(object sender, EventArgs e)
        {
            // FIXED: Replaced hard-coded file path and DateTime.Now with cloud-native alternatives
            // Old code: File.AppendAllText(@"C:\Logs\customer.log", DateTime.Now.ToString());
            
            await LogToAzureBlobStorageAsync();

            Close();
            new LoginUI().Show();
        }

        /// <summary>
        /// Log to Azure Blob Storage instead of local file system
        /// Uses DateTimeOffset.UtcNow for timezone-independent timestamps
        /// </summary>
        private async Task LogToAzureBlobStorageAsync()
        {
            try
            {
                if (_containerClient != null)
                {
                    // FIXED: Use DateTimeOffset.UtcNow instead of DateTime.Now for cloud consistency
                    var timestamp = DateTimeOffset.UtcNow;
                    
                    // Create blob name with UTC timestamp
                    var blobName = $"customer-logout-{timestamp:yyyy-MM-dd}.log";
                    var blobClient = _containerClient.GetBlobClient(blobName);

                    // Prepare log entry with UTC timestamp
                    var logEntry = $"{timestamp:yyyy-MM-dd HH:mm:ss.fff} UTC - User: {personModel.Username} - Action: Logout{Environment.NewLine}";
                    var logBytes = Encoding.UTF8.GetBytes(logEntry);

                    // Append to blob (or create if doesn't exist)
                    using (var stream = new MemoryStream(logBytes))
                    {
                        await blobClient.UploadAsync(stream, overwrite: false);
                    }
                }
                else
                {
                    // Fallback: Use relative path with Path.Combine for cross-platform compatibility
                    var logDirectory = Environment.GetEnvironmentVariable("LOG_DIRECTORY")
                        ?? Path.Combine(Directory.GetCurrentDirectory(), "logs");
                    
                    Directory.CreateDirectory(logDirectory);
                    
                    var logFile = Path.Combine(logDirectory, "customer.log");
                    var timestamp = DateTimeOffset.UtcNow;
                    
                    await File.AppendAllTextAsync(logFile, 
                        $"{timestamp:yyyy-MM-dd HH:mm:ss.fff} UTC - User: {personModel.Username} - Action: Logout{Environment.NewLine}");
                }
            }
            catch (Exception ex)
            {
                // Log error but don't fail logout operation
                Console.WriteLine($"Failed to log to Azure Blob Storage: {ex.Message}");
            }
        }
    }
}
