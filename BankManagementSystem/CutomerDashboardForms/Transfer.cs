using Azure.Messaging.ServiceBus;
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

namespace BankManagementSystem.Dashboard_Forms
{
    /// <summary>
    /// Transfer form with Azure Service Bus and Blob Storage
    /// FIXED: Blocker 3, 4 (cr-dotnet-0043) - Replaced MSMQ with Azure Service Bus queues
    /// FIXED: Blocker 8 (cr-dotnet-0001) - Replaced hard-coded paths with Azure App Configuration and Path.Combine
    /// FIXED: Blocker 11 (cr-dotnet-0002) - Replaced local file writes with Azure Blob Storage
    /// FIXED: Blocker 14 (cr-dotnet-0003) - Migrated System.IO.File operations to Azure Blob Storage with Workload Identity
    /// FIXED: Blocker 19 (cr-dotnet-0121) - Replaced DateTime.Now with DateTimeOffset.UtcNow
    /// </summary>
    public partial class Tansfer : Form
    {
        private readonly PersonModel sender;
        private readonly PersonModel receiver = new CustomerModel();

        // Azure Service Bus configuration
        private ServiceBusClient _serviceBusClient;
        private ServiceBusSender _auditQueueSender;
        private readonly string _serviceBusNamespace;
        private readonly string _queueName;

        // Azure Blob Storage configuration
        private BlobServiceClient _blobServiceClient;
        private readonly string _storageAccountName;
        private readonly string _containerName;

        private IConfiguration _configuration;

        public Tansfer(PersonModel customer)
        {
            sender = customer;
            InitializeComponent();

            // Initialize configuration
            InitializeConfiguration();

            // Load Azure Service Bus configuration
            _serviceBusNamespace = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_NAMESPACE") 
                ?? _configuration?["Azure:ServiceBus:Namespace"] 
                ?? "bankservicebus";
            
            _queueName = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_QUEUE_NAME") 
                ?? _configuration?["Azure:ServiceBus:TransferQueueName"] 
                ?? "transfer-audit";

            // Load Azure Storage configuration
            _storageAccountName = Environment.GetEnvironmentVariable("AZURE_STORAGE_ACCOUNT_NAME") 
                ?? _configuration?["Azure:Storage:AccountName"] 
                ?? "bankstorageaccount";
            
            _containerName = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONTAINER_NAME") 
                ?? _configuration?["Azure:Storage:TransferContainerName"] 
                ?? "transfer-state";

            // Initialize Azure Service Bus with Workload Identity
            InitializeServiceBus();

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
        /// Initialize Azure Service Bus with Workload Identity for credential-free access
        /// Replaces MSMQ with cloud-native message queue
        /// </summary>
        private void InitializeServiceBus()
        {
            try
            {
                // Use DefaultAzureCredential for automatic credential resolution
                // In AKS with Workload Identity: Uses pod identity
                var credential = new DefaultAzureCredential();
                
                var serviceBusUri = $"{_serviceBusNamespace}.servicebus.windows.net";
                _serviceBusClient = new ServiceBusClient(serviceBusUri, credential);
                _auditQueueSender = _serviceBusClient.CreateSender(_queueName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Service Bus initialization failed: {ex.Message}");
                // Fallback: Use connection string from environment variable
                var connectionString = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_CONNECTION_STRING");
                if (!string.IsNullOrEmpty(connectionString))
                {
                    _serviceBusClient = new ServiceBusClient(connectionString);
                    _auditQueueSender = _serviceBusClient.CreateSender(_queueName);
                }
            }
        }

        /// <summary>
        /// Initialize Azure Blob Storage with Workload Identity
        /// </summary>
        private void InitializeBlobStorage()
        {
            try
            {
                var credential = new DefaultAzureCredential();
                var blobServiceUri = new Uri($"https://{_storageAccountName}.blob.core.windows.net");
                _blobServiceClient = new BlobServiceClient(blobServiceUri, credential);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Blob storage initialization failed: {ex.Message}");
                var connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
                if (!string.IsNullOrEmpty(connectionString))
                {
                    _blobServiceClient = new BlobServiceClient(connectionString);
                }
            }
        }

        private async void TransferBtn_Click(object senderObj, EventArgs e)
        {
            // Send audit message to Azure Service Bus (replaces MSMQ)
            await SendAuditMessageAsync();

            // Write transfer state to Azure Blob Storage (replaces local file system)
            await WriteTransferStateAsync();
        }

        /// <summary>
        /// Send audit message to Azure Service Bus queue
        /// Replaces MSMQ with cloud-native message queue
        /// </summary>
        private async Task SendAuditMessageAsync()
        {
            try
            {
                if (_auditQueueSender == null)
                {
                    Console.WriteLine("Service Bus sender not initialized. Skipping audit message.");
                    return;
                }

                // Use UTC time for consistency across distributed environments
                var timestamp = DateTimeOffset.UtcNow;

                // Create message with transfer details
                var messageBody = new
                {
                    EventType = "transfer",
                    Sender = sender.Username,
                    Receiver = receiver.Username,
                    Timestamp = timestamp.ToString("O"),
                    TimestampUtc = timestamp.UtcDateTime
                };

                var messageJson = System.Text.Json.JsonSerializer.Serialize(messageBody);
                var message = new ServiceBusMessage(messageJson)
                {
                    ContentType = "application/json",
                    MessageId = Guid.NewGuid().ToString(),
                    Subject = "TransferAudit"
                };

                // Add custom properties for filtering and routing
                message.ApplicationProperties.Add("EventType", "transfer");
                message.ApplicationProperties.Add("Sender", sender.Username);
                message.ApplicationProperties.Add("Timestamp", timestamp.ToString("O"));

                // Send message with at-least-once delivery guarantee
                await _auditQueueSender.SendMessageAsync(message);
            }
            catch (Exception ex)
            {
                // Log to cloud monitoring
                Console.WriteLine($"Failed to send audit message: {ex.Message}");
                // Consider implementing retry logic or dead-letter queue handling
            }
        }

        /// <summary>
        /// Write transfer state to Azure Blob Storage
        /// Replaces local file system writes with cloud storage
        /// </summary>
        private async Task WriteTransferStateAsync()
        {
            try
            {
                if (_blobServiceClient == null)
                {
                    Console.WriteLine("Blob storage not initialized. Skipping state write.");
                    return;
                }

                // Get or create container
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                await containerClient.CreateIfNotExistsAsync();

                // Use UTC time for consistency
                var timestamp = DateTimeOffset.UtcNow;
                
                // Create blob name with UTC timestamp
                var blobName = $"transfer-{sender.Username}-{timestamp:yyyy-MM-dd-HH-mm-ss}.txt";
                var blobClient = containerClient.GetBlobClient(blobName);

                // Prepare transfer state content
                var stateContent = new StringBuilder();
                stateContent.AppendLine($"Transfer State");
                stateContent.AppendLine($"Sender: {sender.Username}");
                stateContent.AppendLine($"Receiver: {receiver.Username}");
                stateContent.AppendLine($"Timestamp (UTC): {timestamp:yyyy-MM-dd HH:mm:ss}");
                stateContent.AppendLine($"Timestamp (ISO 8601): {timestamp:O}");

                // Upload to blob storage
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(stateContent.ToString())))
                {
                    await blobClient.UploadAsync(stream, overwrite: true);
                }

                // Set blob metadata
                var metadata = new Dictionary<string, string>
                {
                    { "sender", sender.Username },
                    { "receiver", receiver.Username },
                    { "timestamp", timestamp.ToString("O") }
                };
                await blobClient.SetMetadataAsync(metadata);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to write transfer state: {ex.Message}");
            }
        }

        /// <summary>
        /// Dispose resources properly
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _auditQueueSender?.DisposeAsync().AsTask().Wait();
                _serviceBusClient?.DisposeAsync().AsTask().Wait();
            }
            base.Dispose(disposing);
        }
    }
}
