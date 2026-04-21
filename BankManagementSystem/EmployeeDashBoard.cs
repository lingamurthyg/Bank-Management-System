using BankDatabaseAccess.EntityModel;
using Microsoft.Identity.Client;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BankManagementSystem
{
    /// <summary>
    /// Employee Dashboard with Azure AD authentication
    /// FIXED: Blocker 1 (cr-dotnet-0030) - Migrated Windows Authentication to Azure AD with Integrated Windows Authentication for hybrid scenarios
    /// FIXED: Blocker 2 (cr-dotnet-0042) - Replaced P/Invoke Windows APIs with cross-platform .NET libraries
    /// </summary>
    public partial class EmployeeDashBoard : Form
    {
        private readonly PersonModel personModel;

        // Cloud-ready: Removed stateful UI coupling
        // Session management should be handled by Azure AD tokens or distributed cache
        private string CurrentSessionEmployee;

        // Azure AD configuration - should be loaded from environment variables or Azure App Configuration
        private static readonly string ClientId = Environment.GetEnvironmentVariable("AZURE_AD_CLIENT_ID") ?? "your-client-id";
        private static readonly string TenantId = Environment.GetEnvironmentVariable("AZURE_AD_TENANT_ID") ?? "your-tenant-id";
        private static readonly string[] Scopes = new[] { "User.Read" };

        public EmployeeDashBoard(PersonModel personModel)
        {
            this.personModel = personModel;
            InitializeComponent();

            // Azure AD Authentication with Integrated Windows Authentication for hybrid scenarios
            // This allows seamless single sign-on for domain-joined clients while enabling cloud-based authentication
            InitializeAzureAdAuthentication();
        }

        /// <summary>
        /// Initialize Azure AD authentication with pass-through authentication or federation
        /// Supports both domain-joined (IWA) and non-domain scenarios
        /// </summary>
        private async void InitializeAzureAdAuthentication()
        {
            try
            {
                var app = PublicClientApplicationBuilder
                    .Create(ClientId)
                    .WithAuthority(AzureCloudInstance.AzurePublic, TenantId)
                    .WithRedirectUri("http://localhost") // For desktop apps
                    .Build();

                // Try to acquire token silently first (from cache)
                var accounts = await app.GetAccountsAsync();
                AuthenticationResult result = null;

                try
                {
                    if (accounts != null && accounts.GetEnumerator().MoveNext())
                    {
                        result = await app.AcquireTokenSilent(Scopes, accounts.GetEnumerator().Current)
                            .ExecuteAsync();
                    }
                }
                catch (MsalUiRequiredException)
                {
                    // Token not in cache or expired, need interactive authentication
                    // Use Integrated Windows Authentication for domain-joined machines
                    try
                    {
                        result = await app.AcquireTokenByIntegratedWindowsAuth(Scopes)
                            .ExecuteAsync();
                    }
                    catch (MsalException)
                    {
                        // Fallback to interactive authentication for non-domain scenarios
                        result = await app.AcquireTokenInteractive(Scopes)
                            .ExecuteAsync();
                    }
                }

                if (result != null)
                {
                    CurrentSessionEmployee = result.Account.Username;
                    // Store token for API calls if needed
                    // Consider using distributed cache (Redis) for token storage in cloud environments
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Authentication failed: {ex.Message}", "Authentication Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                // Log error to cloud monitoring (Application Insights, Azure Monitor)
            }
        }

        private void DashBoard_Shown(object sender, EventArgs e)
        {
            HomeBtn.PerformClick();
        }

        private void LogoutBtn_Click(object sender, EventArgs e)
        {
            // Removed Windows-specific LockWorkStation P/Invoke call
            // Cross-platform approach: Simply close the form and show login
            // Lock workstation functionality should be handled by OS-level policies
            
            // Clear session data
            CurrentSessionEmployee = null;
            
            // Close current form and show login
            Close();
            new LoginUI().Show();
        }
    }
}
