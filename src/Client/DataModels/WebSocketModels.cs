using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


/// Types used for parsing Unifi web socket data
namespace dotMorten.Unifi.DataModels
{
    internal enum PackageType : byte
    {
        Action = 1,
        Payload = 2
    }

    internal enum PayloadFormat : byte
    {
        Json = 1,
        Utf8String = 2,
        NodeBuffer = 3
    }

    /// <summary>
    /// The action frame identifies what the action and category that the update contains:
    /// </summary>
    internal class ActionFrame
    {
        /// <summary>
        /// 	What action is being taken. Known actions are add and update.
        /// </summary>
        public string? Action { get; set; }
        /// <summary>
        /// The identifier for the device we're updating.
        /// </summary>
        public string? Id { get; set; }
        /// <summary>
        /// The device model category that we're updating.
        /// </summary>
        public string? ModelKey { get; set; }
        /// <summary>
        /// A new UUID generated on a per-update basis.
        /// </summary>
        public string? NewUpdateId { get; set; }

        public static ActionFrame? FromJson(string json) => Newtonsoft.Json.JsonConvert.DeserializeObject<ActionFrame>(json);

        public static ActionFrame? FromJson(byte[] buffer, int index, int count) => Newtonsoft.Json.JsonConvert.DeserializeObject<ActionFrame>(Encoding.UTF8.GetString(buffer, 0, count));
        public static ActionFrame? FromJson(System.IO.Stream stream)
        {
            using var sr = new System.IO.StreamReader(stream);
            return JsonConvert.DeserializeObject<ActionFrame>(sr.ReadToEnd());
        }
    }

    internal class AddDataFrame
    {
        public string? Type { get; set; }
        public long Start { get; set; }
        public long? End { get; set; }
        public int Score { get; set; }
        public string? Camera { get; set; }
        public string? Id { get; set; }
        public string? ModelKey { get; set; }
        public List<string>? SmartDetectTypes { get; set; }
        public AddDataFrameMetadata? Metadata { get; set; }
        //public List<string>? SmartDetectEvents { get; set; }
        public static AddDataFrame? FromJson(string json) => Newtonsoft.Json.JsonConvert.DeserializeObject<AddDataFrame>(json);
        public static AddDataFrame? FromJson(byte[] buffer, int index, int count) => Newtonsoft.Json.JsonConvert.DeserializeObject<AddDataFrame>(Encoding.UTF8.GetString(buffer, 0, count));

        // {"smartDetectEvents":[],"metadata":{"sensorId":{"text":"644afde20118e403e4001d84"},"sensorName":{"text":"Smart Sensor:9B8C"},"type":{"text":"UFP-SENSE"}},}
    }

    internal class AddDataFrameMetadata
    {
        public AddDataFrameMetadataText? SensorId { get; set; }
        public AddDataFrameMetadataText? SensorName { get; set; }
        public AddDataFrameMetadataText? Type { get; set; }
        public AddDataFrameMetadataText? MountType { get; set; }        
        public string? ClientPlatform { get; set; }
        
    }
    internal class AddDataFrameMetadataText
    {
        public string Text { get; set; }
    }

    internal struct UnifiHeader
    {
        public PackageType PacketType { get; set; }
        public PayloadFormat PayloadFormat { get; set; }
        public bool Deflated { get; set; }
        public byte Unknown { get; set; }
        public int PayloadSize { get; set; }

        public static UnifiHeader Parse(System.IO.BinaryReader br)
        {
            return new UnifiHeader()
            {
                PacketType = (PackageType)br.ReadByte(),
                PayloadFormat = (PayloadFormat)br.ReadByte(),
                Deflated = br.ReadByte() == 1,
                Unknown = br.ReadByte(), // Skip - always 0
                PayloadSize = (int)BitConverter.ToUInt32(br.ReadBytes(4).Reverse().ToArray(), 0)
            };
        }
    }
}
