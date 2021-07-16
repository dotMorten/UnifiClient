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
client.CameraUpdated += (sender, camera) += Debug.WriteLine($"Properties on {camera.Name} changed");
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
