using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace dotMorten.Unifi
{
    public abstract class Client : IDisposable
    {
        private readonly string? _username;

        private readonly string? _password;
        
        private Task? _socketProcessTask;
        
        private protected HttpClient HttpClient { get; }

        protected Client(string hostname, string username, string password, bool ignoreSslErrors)
        {
            HostName = hostname;
            _username = username;
            _password = password;
            IgnoreSslErrors = ignoreSslErrors;

            var httpClientHandler = new HttpClientHandler();
            if (IgnoreSslErrors)
                httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) => message.RequestUri.Host == HostName;
            HttpClient = new HttpClient(httpClientHandler);
        }

        public string HostName { get; }

        public bool IgnoreSslErrors { get; }

        public virtual async Task OpenAsync(CancellationToken cancellationToken)
        {
            var jsonCredentials = $"{{\"password\":\"{_password}\", \"username\":\"{_username}\" }}";
            var loginResult = await HttpClient.PostAsync($"https://{HostName}/api/auth/login", new StringContent(jsonCredentials, Encoding.UTF8, "application/json")).ConfigureAwait(false);
            loginResult.EnsureSuccessStatusCode();

            var csftToken = loginResult.Headers.GetValues("X-CSRF-Token").First();
            var cookie = loginResult.Headers.GetValues("Set-Cookie").First();
            HttpClient.DefaultRequestHeaders.Add("Cookie", cookie);
            HttpClient.DefaultRequestHeaders.Add("X-CSRF-Token", csftToken);

            await OnSignInCompleteAsync();
            await ConnectWebSocketAsync(cookie, csftToken);
            IsOpen = true;
        }

        private async Task ConnectWebSocketAsync(string cookie, string token)
        { 
            ClientWebSocket socket = new ClientWebSocket();
            if (IgnoreSslErrors)
                socket.Options.RemoteCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
            socket.Options.SetRequestHeader("Cookie", cookie);
            socket.Options.SetRequestHeader("X-CSRF-Token", token);
            await socket.ConnectAsync(GetWebSocketUri(), CancellationToken.None).ConfigureAwait(false);
            IsOpen = true;
            _socketProcessTask = ProcessWebSocket(socket);
            Debug.WriteLine("WebSocket connected");
        }

        private async Task ReconnectWebSocketAsync()
        {
            try
            {
                if (_socketProcessTask != null)
                    await _socketProcessTask;
                await OpenAsync(CancellationToken.None);
            }
            catch
            {
                Disconnected?.Invoke(this, EventArgs.Empty);
                await CloseAsync();
            }
        }

        protected virtual Task OnSignInCompleteAsync() => Task.CompletedTask;

        protected abstract Uri GetWebSocketUri();

        public virtual async Task CloseAsync()
        {
            IsOpen = false;
            if (_socketProcessTask != null)
                await _socketProcessTask.ConfigureAwait(false);
        }

        public bool IsOpen { get; private set; }

        public void Dispose()
        {
            Dispose(true);
            _ = CloseAsync();
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        private async Task ProcessWebSocket(ClientWebSocket socket)
        {
            var buffer = new byte[1024 * 256];
            WebSocketReceiveResult? result = null;
            while (IsOpen)
            {
                try
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).ConfigureAwait(false);
                }
                catch(WebSocketException ex)
                {
                    Debug.WriteLine($"Socket exception: {ex.Message}\n\tAttempting reconnect");
                    _ = ReconnectWebSocketAsync();
                    return;
                }
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Debug.WriteLine("Socket closed");
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).ConfigureAwait(false);
                    IsOpen = false;
                    break;
                }
                try
                {
                    ProcessWebSocketMessage(result.MessageType, buffer, result.Count);
                }
                catch (System.Exception ex)
                {
                    Debug.WriteLine("Failed to decode message: " + ex.Message);
                }
            }
            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        protected abstract void ProcessWebSocketMessage(WebSocketMessageType messageType, byte[] buffer, int count);

        public event EventHandler? Disconnected;

        internal static MemoryStream Deflate(byte[] buffer, int start, int count)
        {
            using var zippedStream = new MemoryStream(buffer, start, count);
            zippedStream.Seek(2, SeekOrigin.Begin); // Skip past ZLib header
            using var deflate = new DeflateStream(zippedStream, CompressionMode.Decompress);
            var payload = new MemoryStream();
            deflate.CopyTo(payload);
            return payload;
        }
    }
}
