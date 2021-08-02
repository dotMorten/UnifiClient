# UnifiClient
A .NET Library for the Ubiquity Unifi REST and Websocket APIs


## Usage
Monitoring cameras:

```cs
string host = "192.168.1.1"; // host name of your gateway
string username = "your_username"; // Must be local account
string password = "your_password";
var client = new dotMorten.Unifi.ProtectClient(host, username, password, ignoreSslErrors: true);
client.Ring += (sender, camera) => Debug.WriteLine($"Someone rang doorbell {camera.Name}");
client.Motion += (sender, camera) => Debug.WriteLine($"Motion detected on {camera.Name}");
await client.OpenAsync(CancellationToken.None);
foreach(var camera in client.System.Camera)
{ 
   // Iterate cameras
}
client.CameraUpdated += (sender, e) += Debug.WriteLine($"Properties on {e.Camera.Name} changed");
client.Motion += (sender, e) += Debug.WriteLine($"Motion on {e.Camera.Name} detected");
client.SmartDetectZone += (sender, e) += Debug.WriteLine($"{e.SmartDetectTypes[0]} detected on {e.Camera.Name}.");
client.Ring += (sender, e) += Debug.WriteLine("Somebody rang the doorbell.");
foreach(var light in client.System.Lights)
{ 
   // Iterate lights
}
client.LightChanged (sender, e) += Debug.WriteLine($"Properties on {e.Light.Name} changed");
```

Monitoring network (work in progress):

```cs
string host = "192.168.1.1"; // host name of your gateway
string username = "your_username"; // Must be local account
string password = "your_password";
var client = new dotMorten.Unifi.NetworkClient(host, username, password, ignoreSslErrors: true);
// todo...
await client.OpenAsync(CancellationToken.None);
```
