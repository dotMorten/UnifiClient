using dotMorten.Unifi.DataModels;
using dotMorten.Unifi.Protect.DataModels;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace dotMorten.Unifi
{
    public class ProtectClient : Client
    {
        private const string bootstrapUrl = "/proxy/protect/api/bootstrap";
        private const int UPDATE_PACKET_HEADER_SIZE = 8; //  Update realtime API packet header size, in bytes.

        public ProtectClient(string hostname, string username, string password, bool ignoreSslErrors) : base(hostname, username, password, ignoreSslErrors)
        {
        }

        public ProtectSystemStatus? System { get; private set; }

        protected override async Task OnSignInCompleteAsync()
        {
            var result = await HttpClient.GetStringAsync($"https://{HostName}{bootstrapUrl}").ConfigureAwait(false);
            System = JsonConvert.DeserializeObject<ProtectSystemStatus>(result);
        }

        protected override Uri GetWebSocketUri()
        {
            if (System is null)
                throw new InvalidOperationException();
            return new Uri($"wss://{HostName}/proxy/protect/ws/updates?lastUpdateId?lastUpdateId={System.LastUpdateId}");
        }

        /// <summary>
        /// Gets latest snapshot from the camera. If the camera hasn't turned on direct snapshop capture, set <paramref name="useProxy"/> to <c>true</c>.
        /// </summary>
        /// <param name="camera">Camera</param>
        /// <param name="useProxy">Whether to use the controller as a proxy. This image is often cached and more out of date, but doesn't require the camera to have turned snapshots on.</param>
        /// <returns></returns>
        public Task<Stream> GetCameraSnapshot(Camera camera, bool useProxy)
        {
            if (useProxy)
                return GetStreamAsync($"https://{HostName}/proxy/protect/api/cameras/{camera.Id}/snapshot");
            else
                return HttpClient.GetStreamAsync($"http://{camera.Host}/snap.jpeg");
        }

        private async Task<Stream> GetStreamAsync(string url)
        {
            var content = await GetAsync(url).ConfigureAwait(false);
            return await content.ReadAsStreamAsync().ConfigureAwait(false);

        }
        private async Task<String> GetStringAsync(string url)
        {
            var content = await GetAsync(url).ConfigureAwait(false);
            return await content.ReadAsStringAsync().ConfigureAwait(false);
        }

        private async Task<HttpContent> GetAsync(string url)
        {
            var response = await HttpClient.GetAsync(url).ConfigureAwait(false);
            if (response.StatusCode == global::System.Net.HttpStatusCode.Unauthorized)
            {
                // Sign in again
                await SignIn();
                response = await HttpClient.GetAsync(url);
            }
            return response.EnsureSuccessStatusCode().Content;
        }

        protected override void ProcessWebSocketMessage(WebSocketMessageType type, byte[] buffer, int count)
        {
            Debug.Assert(System != null);
            if (type == WebSocketMessageType.Text)
            {
                var message = Encoding.UTF8.GetString(buffer, 0, count);
                Debug.WriteLine("Web socket received text message (TODO): " + message);
            }
            else if (type == WebSocketMessageType.Binary)
            {
                // Messages contains two parts, each with a header and a payload
                // The first part (Action frame) tells us what kind of action we're getting, either "update" or "add"
                // The second part (Data frame) has details about that action (ie properties to update, or info about the add).
                // The paylods might be compressed using zlib.

                using BinaryReader br = new BinaryReader(new MemoryStream(buffer, 0, count));
                // Load Action frame
                var header = UnifiHeader.Parse(br);
                MemoryStream payload = header.Deflated ? Deflate(buffer, (int)br.BaseStream.Position, header.PayloadSize) :
                     new MemoryStream(buffer, (int)br.BaseStream.Position, header.PayloadSize);
                // Load Data frame
                br.BaseStream.Seek(UPDATE_PACKET_HEADER_SIZE + header.PayloadSize, SeekOrigin.Begin);
                var header2 = UnifiHeader.Parse(br);
                MemoryStream payload2 = header2.Deflated ? Deflate(buffer, (int)br.BaseStream.Position, header2.PayloadSize) :
                    new MemoryStream(buffer, (int)br.BaseStream.Position, header2.PayloadSize);

                ActionFrame? action = null;
                if (header.PayloadFormat == PayloadFormat.Json)
                {
                    action = ActionFrame.FromJson(payload.GetBuffer(), 0, (int)payload.Position);
                }
                else if (header.PayloadFormat == PayloadFormat.NodeBuffer)
                {
                    Debug.WriteLine("\tAction Frame: NodeBuffer (TODO)");
                }
                else if (header.PayloadFormat == PayloadFormat.Utf8String)
                {
                    Debug.WriteLine($"\tAction Frame: Utf8String (TODO) = {Encoding.UTF8.GetString(payload.GetBuffer(), 0, (int)payload.Position)}");
                }

                if (header2.PayloadFormat == PayloadFormat.Json)
                {
                    var json = Encoding.UTF8.GetString(payload2.GetBuffer(), 0, (int)payload2.Position);
                    if (action != null)
                    {
                        if (action.Action == "update")
                        {
                            if (action.ModelKey == "camera")
                            {
                                var camera = System.Cameras.Where(c => c.Id == action.Id).FirstOrDefault();
                                if (camera != null)
                                {
                                    JsonConvert.PopulateObject(json, camera);
                                    CameraUpdated?.Invoke(this, camera);
                                }
                                if (!string.IsNullOrEmpty(action.NewUpdateId))
                                    System.LastUpdateId = action.NewUpdateId;
                            }
                            else if (action.ModelKey == "light")
                            {
                                var light = System.Lights.Where(c => c.Id == action.Id).FirstOrDefault();
                                if (light != null)
                                {
                                    var isOn = light.IsLightOn;
                                    JsonConvert.PopulateObject(json, light);
                                    LightUpdated?.Invoke(this, light);
                                    if (light.IsLightOn != isOn)
                                        LightIsOnChanged?.Invoke(this, light);
                                }
                                if (!string.IsNullOrEmpty(action.NewUpdateId))
                                    System.LastUpdateId = action.NewUpdateId;
                            }
                            else if (action.ModelKey == "bridge")
                            {
                                var bridge = System.Bridges.Where(c => c.Id == action.Id).FirstOrDefault();
                                if (bridge != null)
                                {
                                    JsonConvert.PopulateObject(json, bridge);
                                    BridgeUpdated?.Invoke(this, bridge);
                                }
                            }
                            else if (action.ModelKey == "group")
                            {
                                var group = System.Groups.Where(c => c.Id == action.Id).FirstOrDefault();
                                if (group != null)
                                {
                                    JsonConvert.PopulateObject(json, group);
                                    GroupUpdated?.Invoke(this, group);
                                }
                            }
                            else if (action.ModelKey == "liveview")
                            {
                                var view = System.LiveViews.Where(c => c.Id == action.Id).FirstOrDefault();
                                if (view != null)
                                {
                                    JsonConvert.PopulateObject(json, view);
                                    LiveViewUpdated?.Invoke(this, view);
                                }
                            }
                            else if (action.ModelKey == "nvr")
                            {
                                if (System.Nvr != null)
                                {
                                    JsonConvert.PopulateObject(json, System.Nvr);
                                    NvrUpdated?.Invoke(this, System.Nvr);
                                }
                            }
                            else if (action.ModelKey == "user")
                            {
                                var user = System.Users.Where(c => c.Id == action.Id).FirstOrDefault();
                                if (user != null)
                                {
                                    JsonConvert.PopulateObject(json, user);
                                    UserUpdated?.Invoke(this, user);
                                }
                            }
                            else // light, sensor, doorlock, viewer, display, 
                            {
                                // TODO
                            }
                        }
                        else if (action.Action == "add")
                        {
                            if (action.ModelKey == "event")
                            {
                                Debug.WriteLine("Event: " + json);
                                var addDataEvent = JsonConvert.DeserializeObject<AddDataFrame>(json);
                                if (addDataEvent != null)
                                {
                                    var camera = System.Cameras.Where(c => c.Id == addDataEvent.Camera).FirstOrDefault();
                                    if (camera != null)
                                    {
                                        if (addDataEvent.Type == "ring")
                                            Ring?.Invoke(this, new CameraEventArgs(addDataEvent, camera));
                                        else if (addDataEvent.Type == "motion")
                                            Motion?.Invoke(this, new CameraEventArgs(addDataEvent, camera));
                                        else if ((addDataEvent.Type == "smartDetectZone"))
                                        {
                                            // {"type":"smartDetectZone","start":1626580814723,"score":66,"smartDetectTypes":["person"],"smartDetectEvents":[],"camera":"5f3ec80903a2bf038700222e","partition":null,"id":"60f3a7520032cb0387001da8","modelKey":"event"}
                                            var handler = SmartDetectZone;
                                            SmartDetectZone?.Invoke(this, new CameraEventArgs(addDataEvent, camera));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                else if (header2.PayloadFormat == PayloadFormat.NodeBuffer)
                {
                    Debug.WriteLine("\tData Frame: NodeBuffer (TODO)");
                }
                else if (header2.PayloadFormat == PayloadFormat.Utf8String)
                {
                    Debug.WriteLine($"\tData Frame: Utf8String (TODO) = {Encoding.UTF8.GetString(payload2.GetBuffer(), 0, (int)payload2.Position)}");
                }
            }
        }

        /// <summary>
        /// Raised when a smart detect event occurs.
        /// </summary>
        public event EventHandler<CameraEventArgs>? SmartDetectZone;

        /// <summary>
        /// Raised when a doorbell camera rings.
        /// </summary>
        /// <seealso cref="Camera.LastRing"/>
        public event EventHandler<CameraEventArgs>? Ring;

        /// <summary>
        /// Raised when a camera detects motion.
        /// </summary>
        /// <remarks>
        /// Use the <see cref="CameraUpdated"/> event to track when the motion ends by looking at the camera's <see cref="Camera.IsMotionDetected"/> value.
        /// </remarks>
        /// <seealso cref="Camera.IsMotionDetected"/>
        public event EventHandler<CameraEventArgs>? Motion;

        /// <summary>
        /// Raised when the properties of a camera is updated.
        /// </summary>
        public event EventHandler<Camera>? CameraUpdated;

        /// <summary>
        /// Raised when the properties of a light is updated.
        /// </summary>
        public event EventHandler<Light>? LightUpdated;


        /// <summary>
        /// Raised when a light is turned on or off.
        /// </summary>
        public event EventHandler<Light>? LightIsOnChanged;        

        /// <summary>
        /// Raised when the properties of a user is updated.
        /// </summary>
        public event EventHandler<UserAccount>? UserUpdated;

        /// <summary>
        /// Raised when the properties of a bridge is updated.
        /// </summary>
        public event EventHandler<Bridge>? BridgeUpdated;

        /// <summary>
        /// Raised when the properties of the NVR is updated.
        /// </summary>
        public event EventHandler<Nvr>? NvrUpdated;

        /// <summary>
        /// Raised when the properties of a live view is updated.
        /// </summary>
        public event EventHandler<LiveView>? LiveViewUpdated;

        /// <summary>
        /// Raised when the properties of a group is updated.
        /// </summary>
        public event EventHandler<Group>? GroupUpdated;
    }

    public class CameraEventArgs : EventArgs
    {
        internal CameraEventArgs(AddDataFrame addDataEvent, Camera camera)
        {
            Camera = camera;
            Start = DateTimeOffset.FromUnixTimeMilliseconds(addDataEvent.Start).ToLocalTime();
            End =  addDataEvent.End.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(addDataEvent.End.Value).ToLocalTime() : null;
            Score = addDataEvent.Score;
            SmartDetectTypes = addDataEvent.SmartDetectTypes?.ToArray() ?? new string[] { };
            Id = addDataEvent.Id!;
        }
        public string Id { get; }
        public int Score { get; }
        public string[] SmartDetectTypes { get; }
        public Camera Camera { get; }
        public DateTimeOffset Start { get; }
        public DateTimeOffset? End { get; }
    }

}