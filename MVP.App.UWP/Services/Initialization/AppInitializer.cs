﻿namespace MVP.App.Services.Initialization
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using GalaSoft.MvvmLight.Messaging;

    using MVP.Api;
    using MVP.Api.Models;
    using MVP.Api.Models.MicrosoftAccount;
    using MVP.App.Common;
    using MVP.App.Events;
    using MVP.App.Services.Data;
    using MVP.App.Services.MvpApi.DataContainers;

    using Windows.Security.Authentication.Web;

    using WinUX;
    using WinUX.Diagnostics.Tracing;
    using WinUX.Messaging.Dialogs;
    using WinUX.Networking;

    /// <summary>
    /// Defines a service for initializing an application.
    /// </summary>
    public class AppInitializer : IAppInitializer
    {
        private readonly IMessenger messenger;

        private readonly ApiClient apiClient;

        private readonly IProfileDataContainer profileData;

        private readonly IDataContainerManager containerManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="AppInitializer"/> class.
        /// </summary>
        /// <param name="messenger">
        /// The MvvmLight messenger.
        /// </param>
        /// <param name="apiClient">
        /// The MVP API client.
        /// </param>
        /// <param name="profileData">
        /// The cached profile data.
        /// </param>
        public AppInitializer(IMessenger messenger, ApiClient apiClient, IDataContainerManager containerManager, IProfileDataContainer profileData)
        {
            this.messenger = messenger;
            this.apiClient = apiClient;
            this.containerManager = containerManager;
            this.profileData = profileData;
        }

        /// <inheritdoc />
        public async Task<bool> InitializeAsync()
        {
            bool isSuccess = true;

            this.SendLoadingProgress("Attempting login...");
            if (!await this.AttemptAuthenticationAsync())
            {
                isSuccess = false;
            }

            this.SendLoadingProgress("Loading cached data...");
            await this.containerManager.LoadAsync();

            // If authentication wasn't successful due to any reason, we don't want to attempt to update data as we know this will fail.
            if (isSuccess && this.containerManager.RequiresUpdate)
            {
                this.SendLoadingProgress("Updating cached data...");
                await this.containerManager.UpdateAsync();
            }

            this.SendLoadingProgress("Done!");
            return isSuccess;
        }

        public async Task<AuthenticationMessage> AuthenticateAsync()
        {
            bool success = true;
            string errorMessage = string.Empty;

            if (!NetworkStatusManager.Current.IsConnected())
            {
                return new AuthenticationMessage(false, "There appears to be no network connection!");
            }

            try
            {
                List<MSAScope> scopes = new List<MSAScope>
                                 {
                                     MSAScope.Basic,
                                     MSAScope.Emails,
                                     MSAScope.OfflineAccess,
                                     MSAScope.SignIn
                                 };

                string authUri = this.apiClient.RetrieveAuthenticationUri(scopes);

                WebAuthenticationResult result = await WebAuthenticationBroker.AuthenticateAsync(
                                 WebAuthenticationOptions.None,
                                 new Uri(authUri),
                                 new Uri(ApiClient.RedirectUri));

                if (result.ResponseStatus == WebAuthenticationStatus.Success)
                {
                    if (!string.IsNullOrWhiteSpace(result.ResponseData))
                    {
                        Uri responseUri = new Uri(result.ResponseData);
                        if (responseUri.LocalPath.StartsWith("/oauth20_desktop.srf", StringComparison.OrdinalIgnoreCase))
                        {
                            string error = responseUri.ExtractQueryValue("error");

                            if (string.IsNullOrWhiteSpace(error))
                            {
                                string authCode = responseUri.ExtractQueryValue("code");

                                MSACredentials msa = await this.apiClient.ExchangeAuthCodeAsync(authCode);
                                if (msa != null)
                                {
                                    await this.profileData.SetAccountAsync(msa);
                                }
                            }
                            else
                            {
                                errorMessage = error;
                            }
                        }
                    }
                }
                else
                {
                    if (result.ResponseStatus != WebAuthenticationStatus.UserCancel)
                    {
                        errorMessage = "Sign in was not successful. Please try again.";
                    }

                    await this.profileData.ClearAsync();

                    success = false;
                }
            }
            catch (Exception ex)
            {
                EventLogger.Current.WriteWarning(ex.ToString());
                success = false;
            }

            return new AuthenticationMessage(success, errorMessage);
        }

        private async Task<bool> AttemptAuthenticationAsync()
        {
            await this.profileData.LoadAsync();

            if (this.profileData.Account == null)
            {
                return false;
            }

            this.apiClient.Credentials = this.profileData.Account;

            // Check network status.
            if (NetworkStatusManager.Current.CurrentConnectionType != NetworkConnectionType.Disconnected
                || NetworkStatusManager.Current.CurrentConnectionType != NetworkConnectionType.Unknown)
            {
                MVPProfile profile = await this.TestApiEndpointAsync();
                if (profile == null)
                {
                    // Attempt refresh token exchange.
                    bool exchangeErrored = false;

                    try
                    {
                        await this.apiClient.ExchangeRefreshTokenAsync();
                    }
                    catch (ApiException aex)
                    {
                        EventLogger.Current.WriteWarning(aex.ToString());
                        exchangeErrored = true;
                    }
                    catch (Exception ex)
                    {
#if DEBUG
                        System.Diagnostics.Debug.WriteLine(ex.ToString());
#endif
                        exchangeErrored = true;
                    }

                    if (exchangeErrored)
                    {
                        await this.apiClient.LogOutAsync();
                        return false;
                    }

                    profile = await TimeoutAction.ExecuteAsync(this.TestApiEndpointAsync());
                    if (profile == null)
                    {
                        await this.apiClient.LogOutAsync();
                        await this.profileData.ClearAsync();

                        await MessageDialogManager.Current.ShowAsync(
                            "Profile error",
                            "There seems to be an issue getting your MVP profile.");

                        return false;
                    }

                    await this.profileData.SetProfileAsync(profile);
                }
                else
                {
                    await this.profileData.SetProfileAsync(profile);
                }
            }

            await this.profileData.SetAccountAsync(this.apiClient.Credentials);

            return true;
        }

        private async Task<MVPProfile> TestApiEndpointAsync()
        {
            MVPProfile profile = null;
            try
            {
                profile = await this.apiClient.GetMyProfileAsync();
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine(ex.ToString());
#endif
            }

            return profile;
        }

        private void SendLoadingProgress(string message)
        {
            this.messenger.Send(new AppInitializerMessage(message));
        }
    }
}