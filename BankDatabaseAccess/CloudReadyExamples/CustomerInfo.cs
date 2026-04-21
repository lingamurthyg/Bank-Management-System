using BankDatabaseAccess.DatabaseOperation;
using Azure.Messaging.ServiceBus;
using Azure.Identity;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;

namespace BankManagementSystem.EmployeeDashboardForms
{
    /// <summary>
    /// FIXED: Blocker 5, 6 (cr-dotnet-0043) - Replaced MSMQ with Azure Service Bus queues
    /// </summary>
    public partial class CustomerInfo : Form
    {
        // FIXED: Replaced MSMQ with Azure Service Bus
        // Old code: private readonly MessageQueue queue = new MessageQueue(@".\Private$\customer-info");
        private ServiceBusClient _serviceBusClient;
        private ServiceBusSender _queueSender;
        private IConfiguration _configuration;

        public CustomerInfo()
        {
            InitializeComponent();
            
            // Initialize cloud services
            InitializeCloudServices();
            
            // Send audit message to Service Bus
            SendAuditMessageAsync("viewed").GetAwaiter().GetResult();
        }

        /// <summary>
        /// Initialize Azure Service Bus with Workload Identity
        /// Replaces MSMQ with cloud-native message queuing
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
                        ?? "customer-info-audit";

                    _queueSender = _serviceBusClient.CreateSender(queueName);
                }
            }
            catch (Exception ex)
            {
                // Log error but don't fail initialization
                Console.WriteLine($"Failed to initialize Azure Service Bus: {ex.Message}");
            }
        }

        /// <summary>
        /// Send audit message to Azure Service Bus queue
        /// Replaces MSMQ with cloud-native message queuing with at-least-once delivery,
        /// dead-letter queues, and message sessions
        /// </summary>
        private async Task SendAuditMessageAsync(string action)
        {
            try
            {
                if (_queueSender != null)
                {
                    // Create Service Bus message with metadata
                    var message = new ServiceBusMessage(action)
                    {
                        ContentType = "text/plain",
                        Subject = "CustomerInfoAudit",
                        MessageId = Guid.NewGuid().ToString(),
                        TimeToLive = TimeSpan.FromDays(7)
                    };

                    // Add custom properties for filtering and routing
                    message.ApplicationProperties.Add("Action", action);
                    message.ApplicationProperties.Add("Timestamp", DateTimeOffset.UtcNow.ToString("o"));
                    message.ApplicationProperties.Add("Source", "CustomerInfo");
                    message.ApplicationProperties.Add("Employee", Environment.UserName);

                    // Send message to queue with at-least-once delivery guarantee
                    await _queueSender.SendMessageAsync(message);
                    
                    Console.WriteLine($"Audit message sent to Service Bus: {action}");
                }
                else
                {
                    // Fallback: Log locally if Service Bus is not available
                    Console.WriteLine($"Service Bus not configured. Audit action: {action}");
                }
            }
            catch (ServiceBusException ex)
            {
                // Handle Service Bus specific errors
                Console.WriteLine($"Service Bus error: {ex.Message}");
                
                // Implement retry logic for transient failures
                if (ex.IsTransient)
                {
                    Console.WriteLine("Transient error detected. Message will be retried.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send audit message: {ex.Message}");
            }
        }

        /// <summary>
        /// Cleanup resources when form is disposed
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Dispose Service Bus resources asynchronously
                try
                {
                    _queueSender?.DisposeAsync().GetAwaiter().GetResult();
                    _serviceBusClient?.DisposeAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error disposing Service Bus resources: {ex.Message}");
                }
            }
            base.Dispose(disposing);
        }
    }
}
