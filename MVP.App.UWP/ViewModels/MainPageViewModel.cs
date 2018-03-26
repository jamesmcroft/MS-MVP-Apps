﻿namespace MVP.App.ViewModels
{
    using System;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.Net.Http;
    using System.Threading.Tasks;
    using System.Windows.Input;

    using GalaSoft.MvvmLight.Command;

    using MVP.Api;
    using MVP.Api.Models;
    using MVP.App.Events;
    using MVP.App.Models;
    using MVP.App.Services.MvpApi;
    using MVP.App.Services.MvpApi.DataContainers;
    using MVP.App.Views;

    using Windows.UI.Popups;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Media.Imaging;
    using Windows.UI.Xaml.Navigation;

    using WinUX;
    using WinUX.Diagnostics.Tracing;
    using WinUX.Messaging.Dialogs;
    using WinUX.MvvmLight.Xaml.Views;
    using WinUX.Networking;

    /// <summary>
    /// Defines the view-model for the <see cref="MainPage"/>
    /// </summary>
    public class MainPageViewModel : PageBaseViewModel
    {
        private MVPProfile profile;

        private readonly ApiClient apiClient;

        private readonly IProfileDataContainer profileData;

        private BitmapSource profileImage;

        private bool isRecentActivitiesVisible;

        private readonly IContributionSubmissionService contributionService;

        /// <summary>
        /// Initializes a new instance of the <see cref="MainPageViewModel"/> class.
        /// </summary>
        /// <param name="apiClient">
        /// The MVP API client.
        /// </param>
        /// <param name="profileData">
        /// The application's data.
        /// </param>
        public MainPageViewModel(
            ApiClient apiClient,
            IContributionSubmissionService contributionService,
            IProfileDataContainer profileData)
        {
            this.apiClient = apiClient;
            this.contributionService = contributionService;
            this.profileData = profileData;

            this.RecentContributions = new ObservableCollection<Contribution>();
            this.ContributionFlyoutViewModel = new EditableContributionFlyoutViewModel();

            this.ActivityClickedCommand =
                new RelayCommand<Contribution>(c => this.ContributionFlyoutViewModel.ShowEdit(c));

            this.SaveContributionCommand = new RelayCommand(async () => await this.SaveContributionAsync());

            this.MessengerInstance.Register<ProfileUpdatedMessage>(
                this,
                args =>
                    {
                        if (args != null)
                        {
                            this.OnProfileUpdated(args.Profile);
                        }
                    });

            this.MessengerInstance.Register<RefreshDataMessage>(this, this.RefreshProfileData);
        }

        private async Task SaveContributionAsync()
        {
            if (this.ContributionFlyoutViewModel != null && this.ContributionFlyoutViewModel.IsValid())
            {
                Contribution contribution = this.ContributionFlyoutViewModel.Item.Save();
                if (contribution != null)
                {
                    this.MessengerInstance.Send(new UpdateBusyIndicatorMessage(true, "Sending contribution...", true));

                    bool success = false;
                    bool isAuthenticated = true;

                    try
                    {
                        success = await this.contributionService.SubmitContributionAsync(contribution);
                    }
                    catch (HttpRequestException hre) when (hre.Message.Contains("401"))
                    {
                        isAuthenticated = false;
                    }
                    catch (Exception ex)
                    {
                        EventLogger.Current.WriteError(ex.ToString());
                    }

                    if (!isAuthenticated)
                    {
                        await MessageDialogManager.Current.ShowAsync(
                            "Not authorized",
                            "You are no longer authenticated.",
                            new UICommand(
                                "Ok",
                                async command =>
                                {
                                    await this.apiClient.LogOutAsync();
                                }));

                        Application.Current.Exit();
                    }

                    if (success)
                    {
                        this.ContributionFlyoutViewModel.Close();
                    }

                    this.MessengerInstance.Send(new UpdateBusyIndicatorMessage(false));

                    if (success)
                    {
                        this.MessengerInstance.Send(new RefreshDataMessage(RefreshDataMode.Contributions));
                    }
                }
            }
        }

        /// <summary>
        /// Gets the custom fly-out view model for the contributions.
        /// </summary>
        public EditableContributionFlyoutViewModel ContributionFlyoutViewModel { get; }

        /// <summary>
        /// Gets the recent activities of the current MVP profile.
        /// </summary>
        public ObservableCollection<Contribution> RecentContributions { get; }

        public ICommand SaveContributionCommand { get; }

        /// <summary>
        /// Gets or sets the current MVP profile.
        /// </summary>
        public MVPProfile Profile
        {
            get => this.profile;

            set => this.Set(() => this.Profile, ref this.profile, value);
        }

        /// <summary>
        /// Gets or sets the current MVP profile image.
        /// </summary>
        public BitmapSource ProfileImage
        {
            get => this.profileImage;

            set => this.Set(() => this.ProfileImage, ref this.profileImage, value);
        }

        /// <inheritdoc />
        public override void OnPageNavigatedTo(NavigationEventArgs args)
        {
            MVPProfile mvpProfile = args.Parameter as MVPProfile ?? this.profileData.Profile;
            this.OnProfileUpdated(mvpProfile);
        }

        /// <inheritdoc />
        public override void OnPageNavigatedFrom(NavigationEventArgs args)
        {
            this.ContributionFlyoutViewModel?.Close();
        }

        /// <inheritdoc />
        public override void OnPageNavigatingFrom(NavigatingCancelEventArgs args)
        {
        }

        private async void RefreshProfileData(RefreshDataMessage obj)
        {
            this.ContributionFlyoutViewModel.Close();

            if (NetworkStatusManager.Current.IsConnected())
            {
                if (obj.Mode == RefreshDataMode.All || obj.Mode == RefreshDataMode.Profile)
                {
                    bool isAuthenticated = true;

                    try
                    {
                        MVPProfile newProfile = await this.apiClient.GetMyProfileAsync();
                        if (newProfile != null)
                        {
                            await this.profileData.SetProfileAsync(newProfile);
                        }
                    }
                    catch (HttpRequestException hre) when (hre.Message.Contains("401"))
                    {
                        isAuthenticated = false;
                    }
                    catch (Exception ex)
                    {
                        EventLogger.Current.WriteError(ex.ToString());
                    }

                    if (!isAuthenticated)
                    {
                        await MessageDialogManager.Current.ShowAsync(
                            "Not authorized",
                            "You are no longer authenticated.",
                            new UICommand(
                                "Ok",
                                async command =>
                                {
                                    await this.apiClient.LogOutAsync();
                                }));

                        Application.Current.Exit();
                    }
                }
                else if (obj.Mode == RefreshDataMode.All || obj.Mode == RefreshDataMode.Contributions)
                {
                    await this.UpdateRecentContributionsAsync();
                }
            }

            this.UpdateSectionVisibility();

            this.MessengerInstance.Send(new UpdateBusyIndicatorMessage(false));
        }

        private void UpdateSectionVisibility()
        {
            this.IsRecentActivitiesVisible = this.RecentContributions != null && this.RecentContributions.Any();
        }

        /// <summary>
        /// Gets or sets a value indicating whether the recent activities section is visible.
        /// </summary>
        public bool IsRecentActivitiesVisible
        {
            get => this.isRecentActivitiesVisible;

            set => this.Set(() => this.IsRecentActivitiesVisible, ref this.isRecentActivitiesVisible, value);
        }

        public ICommand ActivityClickedCommand { get; }

        private async void OnProfileUpdated(MVPProfile profile)
        {
            if (profile != null)
            {
                this.Profile = profile;
                await this.UpdateProfilePicAsync();
                await this.UpdateRecentContributionsAsync();
            }

            this.UpdateSectionVisibility();
        }

        private async Task UpdateRecentContributionsAsync()
        {
            if (!NetworkStatusManager.Current.IsConnected())
            {
                return;
            }

            bool isAuthenticated = true;

            try
            {
                Contributions recentContributions = await this.apiClient.GetContributionsAsync(0, 10);
                if (recentContributions?.Items != null)
                {
                    this.RecentContributions.Clear();
                    this.RecentContributions.AddRange(recentContributions.Items);
                }
            }
            catch (HttpRequestException hre) when (hre.Message.Contains("401"))
            {
                isAuthenticated = false;
            }
            catch (Exception ex)
            {
                EventLogger.Current.WriteError(ex.ToString());
            }

            if (!isAuthenticated)
            {
                await MessageDialogManager.Current.ShowAsync(
                    "Not authorized",
                    "You are no longer authenticated.",
                    new UICommand(
                        "Ok",
                        async command =>
                        {
                            await this.apiClient.LogOutAsync();
                        }));

                Application.Current.Exit();
            }
        }

        private async Task UpdateProfilePicAsync()
        {
            if (!string.IsNullOrWhiteSpace(this.profileData.ProfileImage))
            {
                await this.SetProfileImageSourceAsync(this.profileData.ProfileImage);
            }
            else
            {
                if (!NetworkStatusManager.Current.IsConnected())
                {
                    return;
                }

                bool isAuthenticated = true;

                try
                {
                    string image = await this.apiClient.GetMyProfileImageAsync();
                    await this.SetProfileImageSourceAsync(image);
                    await this.profileData.SetProfileImageAsync(image);
                }
                catch (HttpRequestException hre) when (hre.Message.Contains("401"))
                {
                    isAuthenticated = false;
                }
                catch (Exception ex)
                {
                    EventLogger.Current.WriteError(ex.ToString());
                }

                if (!isAuthenticated)
                {
                    await MessageDialogManager.Current.ShowAsync(
                        "Not authorized",
                        "You are no longer authenticated.",
                        new UICommand(
                            "Ok",
                            async command =>
                            {
                                await this.apiClient.LogOutAsync();
                            }));

                    Application.Current.Exit();
                }
            }
        }

        private async Task SetProfileImageSourceAsync(string base64String)
        {
            this.ProfileImage = await base64String.ToImageSourceAsync();
        }
    }
}