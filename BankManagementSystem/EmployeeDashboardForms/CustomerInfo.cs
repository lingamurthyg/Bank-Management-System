using Azure.Messaging.ServiceBus;
using Azure.Identity;
using BankDatabaseAccess.DatabaseOperation;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BankManagementSystem.EmployeeDashboardForms
{
    /// <summary>
    /// Customer Info form with Azure Service Bus
    /// FIXED: Blocker 5, 6 (cr-dotnet-0043) - Replaced MSMQ with Azure Service Bus queues
    /// </summary>
    public partial class CustomerInfo : Form
    {
        // Azure Service Bus configuration
        private ServiceBusClient _serviceBusClient;
        private ServiceBusSender _queueSender;
        private readonly string _serviceBusNamespace;
        private readonly string _queueName;
        private IConfiguration _configuration;

        public CustomerInfo()
        {
            InitializeComponent();

            // Initialize configuration
            InitializeConfiguration();

            // Load Azure Service Bus configuration
            _serviceBusNamespace = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_NAMESPACE") 
                ?? _configuration?["Azure:ServiceBus:Namespace"] 
                ?? "bankservicebus";
            
            _queueName = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_QUEUE_NAME") 
                ?? _configuration?["Azure:ServiceBus:CustomerInfoQueueName"] 
                ?? "customer-info";

            // Initialize Azure Service Bus with Workload Identity
            InitializeServiceBus();

            // Send "viewed" message to queue (replaces MSMQ)
            SendViewedMessageAsync();
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
        /// Service Bus provides distributed queues with at-least-once delivery, dead-letter queues, and message sessions
        /// </summary>
        private void InitializeServiceBus()
        {
            try
            {
                // Use DefaultAzureCredential for automatic credential resolution
                // In AKS with Workload Identity: Uses pod identity
                // In local development: Uses Azure CLI, Visual Studio, or environment variables
                var credential = new DefaultAzureCredential();
                
                var serviceBusUri = $"{_serviceBusNamespace}.servicebus.windows.net";
                _serviceBusClient = new ServiceBusClient(serviceBusUri, credential);
                _queueSender = _serviceBusClient.CreateSender(_queueName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Service Bus initialization failed: {ex.Message}");
                // Fallback: Use connection string from environment variable
                var connectionString = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_CONNECTION_STRING");
                if (!string.IsNullOrEmpty(connectionString))
                {
                    _serviceBusClient = new ServiceBusClient(connectionString);
                    _queueSender = _serviceBusClient.CreateSender(_queueName);
                }
            }
        }

        /// <summary>
        /// Send "viewed" message to Azure Service Bus queue
        /// Replaces MSMQ message queue with cloud-native Service Bus
        /// </summary>
        private async void SendViewedMessageAsync()
        {
            try
            {
                if (_queueSender == null)
                {
                    Console.WriteLine("Service Bus sender not initialized. Skipping message.");
                    return;
                }

                // Use UTC time for consistency across distributed environments
                var timestamp = DateTimeOffset.UtcNow;

                // Create message with event details
                var messageBody = new
                {
                    EventType = "viewed",
                    FormName = "CustomerInfo",
                    Timestamp = timestamp.ToString("O"),
                    TimestampUtc = timestamp.UtcDateTime
                };

                var messageJson = System.Text.Json.JsonSerializer.Serialize(messageBody);
                var message = new ServiceBusMessage(messageJson)
                {
                    ContentType = "application/json",
                    MessageId = Guid.NewGuid().ToString(),
                    Subject = "CustomerInfoViewed"
                };

                // Add custom properties for filtering and routing
                message.ApplicationProperties.Add("EventType", "viewed");
                message.ApplicationProperties.Add("FormName", "CustomerInfo");
                message.ApplicationProperties.Add("Timestamp", timestamp.ToString("O"));

                // Send message with at-least-once delivery guarantee
                // Service Bus provides:
                // - Distributed queues with cloud-scale durability
                // - Geographic replication
                // - Dead-letter queue for failed messages
                // - Message sessions for ordered processing
                await _queueSender.SendMessageAsync(message);
            }
            catch (Exception ex)
            {
                // Log to cloud monitoring (Application Insights, Azure Monitor)
                Console.WriteLine($"Failed to send viewed message: {ex.Message}");
                // Consider implementing retry logic or dead-letter queue handling
            }
        }

        /// <summary>
        /// Dispose resources properly
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Dispose Service Bus resources asynchronously
                _queueSender?.DisposeAsync().AsTask().Wait();
                _serviceBusClient?.DisposeAsync().AsTask().Wait();
            }
            base.Dispose(disposing);
        }
    }
}
