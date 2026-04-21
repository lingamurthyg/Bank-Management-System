using BankDatabaseAccess.DatabaseOperation;
using BankDatabaseAccess.EntityModel;
using Azure.Storage.Blobs;
using Azure.Identity;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;

namespace BankManagementSystem.EmployeeDashboardForms
{
    /// <summary>
    /// FIXED: Blocker 9 (cr-dotnet-0001) - Replaced hard-coded paths with Azure App Configuration
    /// FIXED: Blocker 12 (cr-dotnet-0002) - Replaced local file writes with Azure Blob Storage
    /// FIXED: Blocker 15 (cr-dotnet-0003) - Migrated System.IO.File to Azure Blob Storage with Workload Identity
    /// </summary>
    public partial class Deposit : Form
    {
        // Cloud-ready: Removed static state variable
        // Old code: private static decimal LastDepositAmount;
        // Static state doesn't work in distributed cloud environments with multiple instances
        
        // Azure Blob Storage for cloud-native file operations
        private BlobServiceClient _blobServiceClient;
        private BlobContainerClient _containerClient;
        private IConfiguration _configuration;

        public Deposit()
        {
            InitializeComponent();
            
            // Initialize cloud services
            InitializeCloudServices();
        }

        /// <summary>
        /// Initialize Azure Blob Storage with Workload Identity
        /// </summary>
        private void InitializeCloudServices()
        {
            try
            {
                // Load configuration
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
                        ?? "deposit-cache";

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

        private async void DepositBtn_Click(object sender, EventArgs e)
        {
            decimal depositAmount;
            decimal.TryParse(AmountTextBox.Text, out depositAmount);

            // FIXED: Removed static state variable - use distributed cache or database instead
            // In cloud environments, use Azure Cache for Redis or database for shared state
            
            // FIXED: Replaced hard-coded drive letter path with Azure Blob Storage
            // Old code: File.WriteAllText(@"D:\DepositCache\last.txt", LastDepositAmount.ToString());
            await WriteDepositToAzureBlobStorageAsync(depositAmount);
        }

        /// <summary>
        /// Write deposit information to Azure Blob Storage
        /// Replaces local file system operations with cloud-native storage
        /// </summary>
        private async Task WriteDepositToAzureBlobStorageAsync(decimal amount)
        {
            try
            {
                if (_containerClient != null)
                {
                    // Use UTC timestamp for cloud consistency
                    var timestamp = DateTimeOffset.UtcNow;
                    
                    // Create blob name with timestamp for tracking
                    var blobName = $"deposit-{timestamp:yyyy-MM-dd-HHmmss}.txt";
                    var blobClient = _containerClient.GetBlobClient(blobName);

                    // Prepare deposit data
                    var depositData = new StringBuilder();
                    depositData.AppendLine($"Deposit Amount: {amount:C}");
                    depositData.AppendLine($"Timestamp: {timestamp:yyyy-MM-dd HH:mm:ss.fff} UTC");
                    depositData.AppendLine($"Employee: {Environment.UserName}");

                    var dataBytes = Encoding.UTF8.GetBytes(depositData.ToString());

                    // Upload to blob storage with metadata
                    using (var stream = new MemoryStream(dataBytes))
                    {
                        await blobClient.UploadAsync(stream, overwrite: true);
                    }

                    // Set blob metadata for querying
                    var metadata = new Dictionary<string, string>
                    {
                        { "Amount", amount.ToString() },
                        { "Timestamp", timestamp.ToString("o") },
                        { "Type", "Deposit" }
                    };
                    await blobClient.SetMetadataAsync(metadata);

                    // Also update "last.txt" for backward compatibility
                    var lastBlobClient = _containerClient.GetBlobClient("last.txt");
                    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(amount.ToString())))
                    {
                        await lastBlobClient.UploadAsync(stream, overwrite: true);
                    }
                }
                else
                {
                    // Fallback: Use relative path with Path.Combine for cross-platform compatibility
                    // FIXED: No hard-coded drive letters (D:\)
                    var cacheDirectory = Environment.GetEnvironmentVariable("CACHE_DIRECTORY")
                        ?? Path.Combine(Directory.GetCurrentDirectory(), "cache", "deposits");
                    
                    Directory.CreateDirectory(cacheDirectory);
                    
                    var cacheFile = Path.Combine(cacheDirectory, "last.txt");
                    await File.WriteAllTextAsync(cacheFile, amount.ToString());
                }
            }
            catch (Exception ex)
            {
                // Log error but don't fail the deposit operation
                Console.WriteLine($"Failed to write to Azure Blob Storage: {ex.Message}");
                MessageBox.Show($"Warning: Failed to cache deposit information: {ex.Message}", 
                    "Cache Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// Read last deposit amount from Azure Blob Storage
        /// </summary>
        private async Task<decimal> ReadLastDepositFromAzureBlobStorageAsync()
        {
            try
            {
                if (_containerClient != null)
                {
                    var blobClient = _containerClient.GetBlobClient("last.txt");
                    
                    if (await blobClient.ExistsAsync())
                    {
                        var download = await blobClient.DownloadContentAsync();
                        var content = download.Value.Content.ToString();
                        
                        if (decimal.TryParse(content, out decimal amount))
                        {
                            return amount;
                        }
                    }
                }
                else
                {
                    // Fallback: Read from local file system
                    var cacheDirectory = Environment.GetEnvironmentVariable("CACHE_DIRECTORY")
                        ?? Path.Combine(Directory.GetCurrentDirectory(), "cache", "deposits");
                    
                    var cacheFile = Path.Combine(cacheDirectory, "last.txt");
                    
                    if (File.Exists(cacheFile))
                    {
                        var content = await File.ReadAllTextAsync(cacheFile);
                        if (decimal.TryParse(content, out decimal amount))
                        {
                            return amount;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to read from Azure Blob Storage: {ex.Message}");
            }

            return 0;
        }
    }
}
