using dotMorten.Unifi.DataModels;
using dotMorten.Unifi.Protect.DataModels;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace dotMorten.Unifi
{
    public class NetworkClient : Client
    {
        public NetworkClient(string hostname, string username, string password, bool ignoreSslErrors) : base(hostname, username, password, ignoreSslErrors)
        {
        }

        protected override Task<Uri> OnSigninComplete()
        {
            return Task.FromResult(new Uri($"wss://{HostName}/proxy/network/wss/s/default/events"));
        }
        
        protected override void ProcessWebSocketMessage(WebSocketMessageType type, byte[] buffer, int count)
        {
            if (type == WebSocketMessageType.Text)
            {
                var message = Encoding.UTF8.GetString(buffer, 0, count);
                Debug.WriteLine("Web socket received text message (TODO): " + message);
            }
            else if (type == WebSocketMessageType.Binary)
            {
                Debug.WriteLine("Web socket received binary message (TODO): " + count + " bytes");
            }
        }
    }
}