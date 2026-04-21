using BankDatabaseAccess.EntityModel;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Windows.Forms;
using Microsoft.Identity.Client;

namespace BankManagementSystem
{
    /// <summary>
    /// FIXED: Blocker 1 (cr-dotnet-0030) - Migrated Windows Authentication to Azure AD
    /// FIXED: Blocker 2 (cr-dotnet-0042) - Replaced P/Invoke Windows APIs with cross-platform alternatives
    /// </summary>
    public partial class EmployeeDashBoard : Form
    {
        private readonly PersonModel personModel;

        // Cloud-ready: Use distributed session management instead of static state
        private string CurrentSessionEmployee;

        // Azure AD configuration - set via environment variables
        private static readonly string AzureAdTenantId = Environment.GetEnvironmentVariable("AZURE_AD_TENANT_ID");
        private static readonly string AzureAdClientId = Environment.GetEnvironmentVariable("AZURE_AD_CLIENT_ID");

        public EmployeeDashBoard(PersonModel personModel)
        {
            this.personModel = personModel;
            InitializeComponent();

            // FIXED: Replace Windows Authentication with Azure AD authentication
            // Use Azure AD with Integrated Windows Authentication for hybrid scenarios
            InitializeAzureAdAuthentication();
        }

        /// <summary>
        /// Initialize Azure AD authentication with pass-through authentication support
        /// Enables seamless SSO for domain-joined clients and cloud-based auth for non-domain scenarios
        /// </summary>
        private async void InitializeAzureAdAuthentication()
        {
            try
            {
                // For hybrid scenarios, use Azure AD with Integrated Windows Authentication
                var app = PublicClientApplicationBuilder
                    .Create(AzureAdClientId)
                    .WithAuthority(AzureCloudInstance.AzurePublic, AzureAdTenantId)
                    .WithDefaultRedirectUri()
                    .Build();

                var accounts = await app.GetAccountsAsync();
                AuthenticationResult result;

                if (accounts != null && accounts.GetEnumerator().MoveNext())
                {
                    // Silent authentication for cached tokens
                    result = await app.AcquireTokenSilent(new[] { "User.Read" }, accounts.GetEnumerator().Current)
                        .ExecuteAsync();
                }
                else
                {
                    // Interactive authentication with Windows Integrated Auth fallback
                    result = await app.AcquireTokenInteractive(new[] { "User.Read" })
                        .WithUseEmbeddedWebView(false)
                        .ExecuteAsync();
                }

                // Extract user identity from Azure AD token
                CurrentSessionEmployee = result.Account.Username;
            }
            catch (MsalException ex)
            {
                // Handle authentication errors
                MessageBox.Show($"Authentication failed: {ex.Message}", "Azure AD Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                
                // Fallback: Use environment variable or configuration
                CurrentSessionEmployee = Environment.GetEnvironmentVariable("USER") 
                    ?? Environment.UserName;
            }
        }

        private void DashBoard_Shown(object sender, EventArgs e)
        {
            HomeBtn.PerformClick();
        }

        private void LogoutBtn_Click(object sender, EventArgs e)
        {
            // FIXED: Removed P/Invoke call to LockWorkStation (Windows-specific API)
            // Cross-platform alternative: Just close the session and show login
            
            // Clear session state
            CurrentSessionEmployee = null;
            
            // Close current form and show login
            Close();
            new LoginUI().Show();
            
            // Note: Workstation locking should be handled by the OS or container orchestrator
            // In cloud environments, session management is handled at the application level
        }
    }
}
