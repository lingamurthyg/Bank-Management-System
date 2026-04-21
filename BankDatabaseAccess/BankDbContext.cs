using BankDatabaseAccess.EntityModel;
using Microsoft.EntityFrameworkCore;
using System;

namespace BankDatabaseAccess
{
    /// <summary>
    /// Entity Framework Core DbContext for Bank Database
    /// Replaces direct SqlConnection usage with connection pooling and Azure SQL resiliency
    /// </summary>
    public class BankDbContext : DbContext
    {
        public BankDbContext(DbContextOptions<BankDbContext> options) : base(options)
        {
        }

        public DbSet<CustomerModel> Customers { get; set; }
        public DbSet<EmployeeModel> Employees { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                // Fallback configuration if not configured via DI
                var connectionString = Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING") 
                    ?? DatabaseConnection.Connection;
                
                optionsBuilder.UseSqlServer(connectionString, sqlServerOptions =>
                {
                    // Enable connection resiliency for Azure SQL Database
                    sqlServerOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorNumbersToAdd: null);
                    
                    // Command timeout for long-running queries
                    sqlServerOptions.CommandTimeout(60);
                });
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Customer entity
            modelBuilder.Entity<CustomerModel>(entity =>
            {
                entity.ToTable("Customers", "dbo");
                entity.HasKey(e => e.Username);
                entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
                entity.Property(e => e.FullName).HasMaxLength(200);
                entity.Property(e => e.Email).HasMaxLength(200);
                entity.Property(e => e.Phone).HasMaxLength(50);
                entity.Property(e => e.Nid).HasMaxLength(50);
                entity.Property(e => e.Address).HasMaxLength(500);
                entity.Property(e => e.Balance).HasColumnType("decimal(18,2)");
            });

            // Configure Employee entity
            modelBuilder.Entity<EmployeeModel>(entity =>
            {
                entity.ToTable("Employee", "dbo");
                entity.HasKey(e => e.Username);
                entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
                entity.Property(e => e.FullName).HasMaxLength(200);
                entity.Property(e => e.Email).HasMaxLength(200);
                entity.Property(e => e.Phone).HasMaxLength(50);
                entity.Property(e => e.Nid).HasMaxLength(50);
                entity.Property(e => e.Address).HasMaxLength(500);
            });
        }
    }
}
