using System;
using System.Collections.Generic;
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
        private readonly string _username;

        private readonly string _password;
        
        private Task _socketProcessTask;

        public string CsftToken { get; private set; }
        
        public string Cookie { get; private set; }
        
        private protected HttpClient HttpClient { get; }

        protected Client(string hostname, bool ignoreSslErrors, string cookie, string csftToken) : this(hostname, null, null, ignoreSslErrors)
        {
            Cookie = cookie;
            CsftToken = csftToken;
        }

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
            if (CsftToken is null && Cookie is null)
            {
                var jsonCredentials = $"{{\"password\":\"{_password}\", \"username\":\"{_username}\" }}";
                var loginResult = await HttpClient.PostAsync($"https://{HostName}/api/auth/login", new StringContent(jsonCredentials, Encoding.UTF8, "application/json")).ConfigureAwait(false);
                loginResult.EnsureSuccessStatusCode();

                CsftToken = loginResult.Headers.GetValues("X-CSRF-Token").FirstOrDefault();
                Cookie = loginResult.Headers.GetValues("Set-Cookie").FirstOrDefault();
            }
            HttpClient.DefaultRequestHeaders.Add("Cookie", Cookie);
            HttpClient.DefaultRequestHeaders.Add("X-CSRF-Token", CsftToken);

            var socketUri = await OnSigninComplete();

            ClientWebSocket socket = new ClientWebSocket();
            if (IgnoreSslErrors)
                socket.Options.RemoteCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
            socket.Options.SetRequestHeader("Cookie", Cookie);
            socket.Options.SetRequestHeader("X-CSRF-Token", CsftToken);
            await socket.ConnectAsync(socketUri, cancellationToken).ConfigureAwait(false);

            IsOpen = true;
            _socketProcessTask = ProcessWebSocket(socket, cancellationToken);
        }

        protected abstract Task<Uri> OnSigninComplete();

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

        private async Task ProcessWebSocket(ClientWebSocket socket, CancellationToken cancellation)
        {
            var buffer = new byte[1024 * 256];
            while (!cancellation.IsCancellationRequested && IsOpen)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).ConfigureAwait(false);
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

        public event EventHandler Disconnected;


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
