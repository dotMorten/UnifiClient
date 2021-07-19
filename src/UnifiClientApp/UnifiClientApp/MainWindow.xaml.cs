using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;


namespace UnifiClientApp
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        dotMorten.Unifi.ProtectClient protectClient;
        public MainWindow()
        {
            this.InitializeComponent();
            if(Windows.Storage.ApplicationData.Current.LocalSettings.Containers.ContainsKey("credentials"))
            {
                var container = Windows.Storage.ApplicationData.Current.LocalSettings.Containers["credentials"].Values;
                if (container.ContainsKey("hostname"))
                    tbHostname.Text = (string)container["hostname"];
                if (container.ContainsKey("username"))
                    tbUsername.Text = (string)container["username"];
                if (container.ContainsKey("password"))
                    pwdBox.Password = (string)container["password"];
                if (container.ContainsKey("cookie") && container.ContainsKey("token") && container.ContainsKey("hostname"))
                AutoSignin();
            }
        }

        private async void AutoSignin()
        {
            signinArea.Visibility = Visibility.Collapsed;
            var container = Windows.Storage.ApplicationData.Current.LocalSettings.CreateContainer("credentials", Windows.Storage.ApplicationDataCreateDisposition.Always);
            try
            {
                var client = await dotMorten.Unifi.ProtectClient.SigninWithToken(
                    (string)container.Values["hostname"], (string)container.Values["cookie"],
                    (string)container.Values["token"], (bool)container.Values["ignoreSsl"]);
                InitClient(client);
            }
            catch
            {
                signinArea.Visibility = Visibility.Visible;
            }
        }

        private async void SignIn_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;

            try
            {
                button.IsEnabled = false;
                progress.IsActive = true;
                var protectClient = new dotMorten.Unifi.ProtectClient(tbHostname.Text, tbUsername.Text, pwdBox.Password , true);
                await protectClient.OpenAsync(CancellationToken.None);
                signinArea.Visibility = Visibility.Collapsed;
                InitClient(protectClient);
            }
            catch(System.Exception ex)
            {
                ContentDialog cd = new ContentDialog()
                {
                    Content = new TextBlock() { Text = ex.Message, TextWrapping = TextWrapping.Wrap },
                    CloseButtonText = "OK",
                    Title = "Error signing in",
                    XamlRoot = LayoutRoot.XamlRoot
                };
                await cd.ShowAsync();
            }
            finally
            {
                button.IsEnabled = true;
                progress.IsActive = true;
            }
        }

        private void InitClient(dotMorten.Unifi.ProtectClient client)
        {
            var container = Windows.Storage.ApplicationData.Current.LocalSettings.CreateContainer("credentials", Windows.Storage.ApplicationDataCreateDisposition.Always);
            container.Values["hostname"] = client.HostName;
            container.Values["ignoreSsl"] = client.IgnoreSslErrors;
            container.Values["cookie"] = client.Cookie;
            container.Values["token"] = client.CsftToken;
            protectClient = client;
            protectClient.Ring += ProtectClient_Ring;
            protectClient.Motion += ProtectClient_Motion;
            protectClient.SmartDetectZone += ProtectClient_SmartDetectZone;
            protectClient.Disconnected += ProtectClient_Disconnected;
            status.Text = $"Connected to '{client.HostName}'\nFound {client.System.Cameras.Count} cameras:\n" +
                string.Join("", client.System.Cameras.Select(c => $" - {c.Name} ({c.Type}){ (c.IsConnected ? "" : " (disconnected)") }\n"));
        }

        private void ProtectClient_Disconnected(object sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
            {
                status.Text += "Disconnected\n";
            });
        }

        private void ProtectClient_SmartDetectZone(object sender, dotMorten.Unifi.CameraEventArgs e)
        {
            Debug.WriteLine($"{e.SmartDetectTypes[0]} detected on camera '{e.Camera.Name}'");
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
            {
                status.Text += $"{DateTimeOffset.Now} {e.SmartDetectTypes[0]} detected on camera '{e.Camera.Name}'\n";
            });
        }

        private void ProtectClient_Motion(object sender, dotMorten.Unifi.CameraEventArgs e)
        {
            Debug.WriteLine($"Motion detected on camera '{e.Camera.Name}'");
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
            {
                status.Text += $"{DateTimeOffset.Now} Motion detected on camera '{e.Camera.Name}'\n";
            });
        }

        private void ProtectClient_Ring(object sender, dotMorten.Unifi.CameraEventArgs e)
        {
            Debug.WriteLine($"Ring detected on camera '{e.Camera.Name}'");
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
            {
                status.Text += $"{DateTimeOffset.Now} Ring detected on camera '{e.Camera.Name}'\n";
            });
        }
    }
}
