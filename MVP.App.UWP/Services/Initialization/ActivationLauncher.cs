﻿namespace MVP.App.Services.Initialization
{
    using System;
    using System.Threading.Tasks;

    using MVP.App.Common;
    using MVP.App.Models;
    using MVP.App.Views;

    using Windows.ApplicationModel.Activation;

    using WinUX;
    using WinUX.Input.Speech;
    using WinUX.Mvvm.Services;

    public static class ActivationLauncher
    {
        public static async Task<bool> RunActivationProcedureAsync(ActivationArgs activationArgs)
        {
            if (activationArgs == null)
            {
                return false;
            }

            switch (activationArgs.ActivationKind)
            {
                case ActivationKind.Protocol:
                    return await ActivateForProtocolAsync(activationArgs.ProtocolUri);
                case ActivationKind.VoiceCommand:
                    return await ActivateForVoiceAsync(activationArgs.SpeechCommand);
            }

            return false;
        }

        private static async Task<bool> ActivateForVoiceAsync(SpeechCommand activationSpeechCommand)
        {
            await Task.Delay(1);

            if (activationSpeechCommand != null)
            {
                return true;
            }

            return false;

        }

        private static async Task<bool> ActivateForProtocolAsync(Uri activationProtocolUri)
        {
            await Task.Delay(1);

            if (activationProtocolUri != null)
            {
                string assistanceLaunchQuery = string.Empty;

                if (activationProtocolUri.Scheme.Equals("windows.personalassistantlaunch"))
                {
                    assistanceLaunchQuery = activationProtocolUri.ExtractQueryValue("LaunchContext");
                }

                if (activationProtocolUri.Host.Equals("contribution") || assistanceLaunchQuery.Equals("contribution"))
                {
                    ContributionViewModel contribution = new ContributionViewModel();
                    contribution.Populate(activationProtocolUri);

                    return NavigationService.Current.Navigate(typeof(ContributionsPage), contribution);
                }
            }

            return false;
        }
    }
}