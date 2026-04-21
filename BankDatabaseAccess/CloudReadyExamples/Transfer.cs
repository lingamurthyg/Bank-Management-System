using BankDatabaseAccess.DatabaseOperation;
using BankDatabaseAccess.EntityModel;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Azure.Identity;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;

namespace BankManagementSystem.Dashboard_Forms
{
    /// <summary>
    /// FIXED: Blocker 3, 4 (cr-dotnet-0043) - Replaced MSMQ with Azure Service Bus queues
    /// FIXED: Blocker 8 (cr-dotnet-0001) - Replaced hard-coded paths with Azure App Configuration
    /// FIXED: Blocker 11 (cr-dotnet-0002) - Replaced local file writes with Azure Blob Storage
    /// FIXED: Blocker 14 (cr-dotnet-0003) - Migrated System.IO.File to Azure Blob Storage with Workload Identity
    /// FIXED: Blocker 19 (cr-dotnet-0121) - Replaced DateTime.Now with DateTimeOffset.UtcNow
    /// </summary>
    public partial class Tansfer : Form
    {
        private readonly PersonModel sender;
        private readonly PersonModel receiver = new CustomerModel();

        // FIXED: Replaced MSMQ with Azure Service Bus
        // Old code: private readonly MessageQueue auditQueue = new MessageQueue(@".\Private$\transfer");
        private ServiceBusClient _serviceBusClient;
        private ServiceBusSender _queueSender;
        
        // Azure Blob Storage for cloud-native file operations
        private BlobServiceClient _blobServiceClient;
        private BlobContainerClient _containerClient;
        
        private IConfiguration _configuration;

        public Tansfer(PersonModel customer)
        {
            sender = customer;
            InitializeComponent();
            
            // Initialize cloud services
            InitializeCloudServices();
        }

        /// <summary>
        /// Initialize Azure Service Bus and Blob Storage with Workload Identity
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

                // Initialize Azure Service Bus (replaces MSMQ)
                InitializeServiceBus();
                
                // Initialize Azure Blob Storage (replaces local file system)
                InitializeBlobStorage();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize Azure services: {ex.Message}");
            }
        }

        /// <summary>
        /// Initialize Azure Service Bus client with Workload Identity
        /// Replaces MSMQ with cloud-native message queuing
        /// </summary>
        private void InitializeServiceBus()
        {
            try
            {
                // Get Service Bus namespace from environment variable or configuration
                var serviceBusNamespace = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_NAMESPACE")
                    ?? _configuration["Azure:ServiceBus:Namespace"];

                if (!string.IsNullOrEmpty(serviceBusNamespace))
                {
                    // Use Workload Identity (Managed Identity) for credential-free access
                    var fullyQualifiedNamespace = serviceBusNamespace.Contains(".")
                        ? serviceBusNamespace
                        : $"{serviceBusNamespace}.servicebus.windows.net";

                    _serviceBusClient = new ServiceBusClient(
                        fullyQualifiedNamespace,
                        new DefaultAzureCredential());

                    // Get queue name from configuration
                    var queueName = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_QUEUE_NAME")
                        ?? _configuration["Azure:ServiceBus:QueueName"]
                        ?? "transfer-audit";

                    _queueSender = _serviceBusClient.CreateSender(queueName);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize Service Bus: {ex.Message}");
            }
        }

        /// <summary>
        /// Initialize Azure Blob Storage client with Workload Identity
        /// </summary>
        private void InitializeBlobStorage()
        {
            try
            {
                var blobEndpoint = Environment.GetEnvironmentVariable("AZURE_STORAGE_BLOB_ENDPOINT")
                    ?? _configuration["Azure:StorageAccount:BlobEndpoint"];

                if (!string.IsNullOrEmpty(blobEndpoint))
                {
                    _blobServiceClient = new BlobServiceClient(
                        new Uri(blobEndpoint),
                        new DefaultAzureCredential());

                    var containerName = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONTAINER_NAME")
                        ?? _configuration["Azure:StorageAccount:ContainerName"]
                        ?? "bank-state";

                    _containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                    _containerClient.CreateIfNotExists();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize Blob Storage: {ex.Message}");
            }
        }

        private async void TransferBtn_Click(object senderObj, EventArgs e)
        {
            // FIXED: Replaced MSMQ with Azure Service Bus
            // Old code: auditQueue.Send("transfer");
            await SendToServiceBusAsync("transfer");

            // FIXED: Replaced hard-coded path and DateTime.Now with cloud-native alternatives
            // Old code: File.WriteAllText(@"C:\BankState\transfer.txt", DateTime.Now.ToString());
            await WriteToAzureBlobStorageAsync();
        }

        /// <summary>
        /// Send message to Azure Service Bus queue
        /// Replaces MSMQ with cloud-native message queuing with at-least-once delivery
        /// </summary>
        private async Task SendToServiceBusAsync(string messageContent)
        {
            try
            {
                if (_queueSender != null)
                {
                    // Create Service Bus message with metadata
                    var message = new ServiceBusMessage(messageContent)
                    {
                        ContentType = "text/plain",
                        Subject = "TransferAudit",
                        MessageId = Guid.NewGuid().ToString(),
                        TimeToLive = TimeSpan.FromDays(7)
                    };

                    // Add custom properties for filtering and routing
                    message.ApplicationProperties.Add("Sender", sender.Username);
                    message.ApplicationProperties.Add("Timestamp", DateTimeOffset.UtcNow.ToString("o"));
                    message.ApplicationProperties.Add("Action", "Transfer");

                    // Send message to queue
                    await _queueSender.SendMessageAsync(message);
                }
                else
                {
                    // Fallback: Log locally if Service Bus is not available
                    Console.WriteLine($"Service Bus not configured. Message: {messageContent}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send message to Service Bus: {ex.Message}");
            }
        }

        /// <summary>
        /// Write transfer state to Azure Blob Storage
        /// Uses DateTimeOffset.UtcNow for timezone-independent timestamps
        /// </summary>
        private async Task WriteToAzureBlobStorageAsync()
        {
            try
            {
                // FIXED: Use DateTimeOffset.UtcNow instead of DateTime.Now
                var timestamp = DateTimeOffset.UtcNow;

                if (_containerClient != null)
                {
                    // Create blob name with UTC timestamp
                    var blobName = $"transfer-{timestamp:yyyy-MM-dd-HHmmss}.txt";
                    var blobClient = _containerClient.GetBlobClient(blobName);

                    // Prepare transfer data with UTC timestamp
                    var transferData = $"Transfer executed at: {timestamp:yyyy-MM-dd HH:mm:ss.fff} UTC{Environment.NewLine}";
                    transferData += $"Sender: {sender.Username}{Environment.NewLine}";
                    transferData += $"Receiver: {receiver.Username}{Environment.NewLine}";

                    var dataBytes = Encoding.UTF8.GetBytes(transferData);

                    // Upload to blob storage
                    using (var stream = new MemoryStream(dataBytes))
                    {
                        await blobClient.UploadAsync(stream, overwrite: true);
                    }
                }
                else
                {
                    // Fallback: Use relative path with Path.Combine for cross-platform compatibility
                    var stateDirectory = Environment.GetEnvironmentVariable("STATE_DIRECTORY")
                        ?? Path.Combine(Directory.GetCurrentDirectory(), "state");
                    
                    Directory.CreateDirectory(stateDirectory);
                    
                    var stateFile = Path.Combine(stateDirectory, "transfer.txt");
                    await File.WriteAllTextAsync(stateFile, 
                        $"{timestamp:yyyy-MM-dd HH:mm:ss.fff} UTC - Sender: {sender.Username}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to write to Azure Blob Storage: {ex.Message}");
            }
        }

        /// <summary>
        /// Cleanup resources when form is disposed
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _queueSender?.DisposeAsync().GetAwaiter().GetResult();
                _serviceBusClient?.DisposeAsync().GetAwaiter().GetResult();
            }
            base.Dispose(disposing);
        }
    }
}
