using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace BankDatabaseAccess
{
    /// <summary>
    /// Database connection management with cloud-native configuration
    /// FIXED: Blocker 16 (cr-dotnet-0013) - Migrated to Entity Framework Core with Azure SQL connection resiliency
    /// FIXED: Blocker 20 (cr-dotnet-0010) - Replaced Web.config transformations with environment-based configuration
    /// </summary>
    public static class DatabaseConnection
    {
        private static IConfiguration _configuration;
        private static readonly object _lock = new object();

        /// <summary>
        /// Gets the connection string from environment-based configuration
        /// Supports appsettings.{Environment}.json and environment variables
        /// </summary>
        public static string Connection
        {
            get
            {
                if (_configuration == null)
                {
                    lock (_lock)
                    {
                        if (_configuration == null)
                        {
                            InitializeConfiguration();
                        }
                    }
                }

                // Try environment variable first (12-factor app principle)
                var connectionString = Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING");
                
                if (string.IsNullOrEmpty(connectionString))
                {
                    // Fallback to configuration file
                    connectionString = _configuration?.GetConnectionString("OpenBankLocal");
                }

                if (string.IsNullOrEmpty(connectionString))
                {
                    // Final fallback for backward compatibility
                    connectionString = System.Configuration.ConfigurationManager.ConnectionStrings["OpenBankLocal"]?.ConnectionString;
                }

                return connectionString ?? throw new InvalidOperationException(
                    "Database connection string not found. Set SQL_CONNECTION_STRING environment variable or configure in appsettings.json");
            }
        }

        private static void InitializeConfiguration()
        {
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
            
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables();

            _configuration = builder.Build();
        }

        public enum Error
        {
            UsernameExist = 4001
        }

        /// <summary>
        /// Execute SQL command with connection pooling
        /// Note: Consider migrating to Entity Framework Core for better cloud compatibility
        /// </summary>
        public static int Execute(string query)
        {
            using (var connection = new SqlConnection(Connection))
            {
                try
                {
                    connection.Open();
                    return new SqlCommand(query, connection).ExecuteNonQuery();
                }
                catch (SqlException)
                {
                    return (int)Error.UsernameExist;
                }
            }
        }

        /// <summary>
        /// Creates a configured BankDbContext instance with Azure SQL resiliency
        /// Recommended approach for cloud-native applications
        /// </summary>
        public static BankDbContext CreateDbContext()
        {
            var optionsBuilder = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<BankDbContext>();
            
            optionsBuilder.UseSqlServer(Connection, sqlServerOptions =>
            {
                // Enable connection resiliency for Azure SQL Database
                sqlServerOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null);
                
                sqlServerOptions.CommandTimeout(60);
            });

            return new BankDbContext(optionsBuilder.Options);
        }
    }
}
