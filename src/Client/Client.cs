﻿using System;
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
        private readonly byte[] _credentials;
        private Task? _socketProcessTask;
        private ClientWebSocket? socket;

        private protected HttpClient HttpClient { get; }

        protected Client(string hostname, string username, string password, bool ignoreSslErrors)
        {
            HostName = hostname;
            _credentials = Encoding.UTF8.GetBytes($"{{\"password\":\"{password}\", \"username\":\"{username}\" }}");
            IgnoreSslErrors = ignoreSslErrors;

            var httpClientHandler = new HttpClientHandler();
            HttpClient = new HttpClient(httpClientHandler);
            if (IgnoreSslErrors)
            {
                httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) => message.RequestUri.Host == HostName;
            }
        }

        public string HostName { get; }

        public bool IgnoreSslErrors { get; }

        public Task OpenAsync(CancellationToken cancellationToken) => OpenAsync(cancellationToken, false);
        
        private async Task OpenAsync(CancellationToken cancellationToken, bool reconnect)
        {
            if(!reconnect && IsOpen)
            {
                return;
            }
            await SignIn().ConfigureAwait(false);
            await OnSignInCompleteAsync().ConfigureAwait(false);
            await ConnectWebSocketAsync().ConfigureAwait(false);
        }

        protected async Task SignIn()
        {
            HttpClient.DefaultRequestHeaders.Remove("Cookie");
            HttpClient.DefaultRequestHeaders.Remove("X-CSRF-Token");
            var content = new ByteArrayContent(_credentials);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            var loginResult = await HttpClient.PostAsync($"https://{HostName}/api/auth/login", content).ConfigureAwait(false);
            loginResult.EnsureSuccessStatusCode();

            var token = loginResult.Headers.GetValues("X-CSRF-Token").First();
            var cookie = loginResult.Headers.GetValues("Set-Cookie").First();
            HttpClient.DefaultRequestHeaders.Add("Cookie", cookie);
            HttpClient.DefaultRequestHeaders.Add("X-CSRF-Token", token);
        }

        private async Task ConnectWebSocketAsync()
        {
            if (socket != null)
            {
                if (socket.State != WebSocketState.Closed && socket.State != WebSocketState.None && socket.State != WebSocketState.Aborted)
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Restarting websocket", CancellationToken.None);
                socket.Dispose();
                socket = null;
            }
            socket = new ClientWebSocket();
            var cookie = HttpClient.DefaultRequestHeaders.GetValues("Cookie").First();
            var token = HttpClient.DefaultRequestHeaders.GetValues("X-CSRF-Token").First();
            socket.Options.SetRequestHeader("Cookie", cookie);
            socket.Options.SetRequestHeader("X-CSRF-Token", token);
#if NETSTANDARD2_1
            if (IgnoreSslErrors)
            {
                socket.Options.RemoteCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
            }
#endif

            await socket.ConnectAsync(GetWebSocketUri(), CancellationToken.None).ConfigureAwait(false);
            IsOpen = true;
            _socketProcessTask = ProcessWebSocket(socket);
            Debug.WriteLine("WebSocket connected");
            Connected?.Invoke(this, EventArgs.Empty);
        }

        private async Task ReconnectWebSocketAsync()
        {
            Reconnecting?.Invoke(this, EventArgs.Empty);
            try
            {
                if (_socketProcessTask != null)
                    await _socketProcessTask;
                await OpenAsync(CancellationToken.None, true);
            }
            catch(System.Exception ex)
            {
                Debug.WriteLine("Failed to reconnect to socket: " + ex.Message);
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
                    Debug.WriteLine($"Socket exception: {ex.Message} ErrorCode={ex.ErrorCode} WebSocketErrorCode={ex.WebSocketErrorCode} NativeErrorCode={ex.NativeErrorCode}\n\tAttempting reconnect");
                    socket.Dispose();
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
            socket.Dispose();
            Debug.WriteLine("WebSocket process exiting");
            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        protected abstract void ProcessWebSocketMessage(WebSocketMessageType messageType, byte[] buffer, int count);

        public event EventHandler? Disconnected;
        public event EventHandler? Reconnecting;
        public event EventHandler? Connected;

        internal static Stream Deflate(byte[] buffer, int start, int count)
        {
            using var zippedStream = new MemoryStream(buffer, start, count);
            zippedStream.Seek(2, SeekOrigin.Begin); // Skip past ZLib header
            return new DeflateStream(zippedStream, CompressionMode.Decompress);
            //var payload = new MemoryStream();
            //deflate.CopyTo(payload);
            //return payload;
        }
    }
}
