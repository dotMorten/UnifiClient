﻿using dotMorten.Unifi.DataModels;
using dotMorten.Unifi.Protect.DataModels;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
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

        public ProtectSystemStatus System { get; private set; }

        protected override async Task<Uri> OnSigninComplete()
        {
            var result = await HttpClient.GetStringAsync($"https://{HostName}{bootstrapUrl}").ConfigureAwait(false);
            System = JsonConvert.DeserializeObject<ProtectSystemStatus>(result);
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
                return HttpClient.GetStreamAsync($"https://{HostName}/proxy/protect/api/cameras/{camera.Id}/snapshot");
            else
                return HttpClient.GetStreamAsync($"http://{camera.Host}/snap.jpeg");
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

                ActionFrame action = null;
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
                                var addDataEvent = JsonConvert.DeserializeObject<AddDataFrame>(json);
                                Debug.WriteLine("Got event: " + addDataEvent.Type);
                                var camera = System.Cameras.Where(c => c.Id == addDataEvent.Camera).FirstOrDefault();
                                if (addDataEvent.Type == "ring")
                                    Ring?.Invoke(this, camera);
                                else if (addDataEvent.Type == "motion")
                                    Motion?.Invoke(this, camera);
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
        /// Raised when a doorbell camera rings.
        /// </summary>
        /// <seealso cref="Camera.LastRing"/>
        public event EventHandler<Camera> Ring;

        /// <summary>
        /// Raised when a camera detects motion.
        /// </summary>
        /// <remarks>
        /// Use the <see cref="CameraUpdated"/> event to track when the motion ends by looking at the camera's <see cref="Camera.IsMotionDetected"/> value.
        /// </remarks>
        /// <seealso cref="Camera.IsMotionDetected"/>
        public event EventHandler<Camera> Motion;

        /// <summary>
        /// Raised when the properties of a camera is updated.
        /// </summary>
        public event EventHandler<Camera> CameraUpdated;
 
        /// <summary>
        /// Raised when the properties of a user is updated.
        /// </summary>
        public event EventHandler<UserAccount> UserUpdated;

        /// <summary>
        /// Raised when the properties of a bridge is updated.
        /// </summary>
        public event EventHandler<Bridge> BridgeUpdated;

        /// <summary>
        /// Raised when the properties of the NVR is updated.
        /// </summary>
        public event EventHandler<Nvr> NvrUpdated;

        /// <summary>
        /// Raised when the properties of a live view is updated.
        /// </summary>
        public event EventHandler<LiveView> LiveViewUpdated;

        /// <summary>
        /// Raised when the properties of a group is updated.
        /// </summary>
        public event EventHandler<Group> GroupUpdated;
    }
}