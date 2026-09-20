using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace LANSEND;

internal sealed class PeerDiscoveryService : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, PeerInfo> _peers = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private UdpClient? _socket;
    private CancellationTokenSource? _cancellation;
    private Task? _receiveTask;
    private Task? _broadcastTask;

    public event EventHandler? PeersChanged;

    public void Start()
    {
        if (_cancellation is not null)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        var socket = new UdpClient(AddressFamily.InterNetwork)
        {
            EnableBroadcast = true
        };
        socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.Client.Bind(new IPEndPoint(IPAddress.Any, AppConstants.DiscoveryPort));
        _socket = socket;

        _receiveTask = ReceiveLoopAsync(socket, _cancellation.Token);
        _broadcastTask = BroadcastLoopAsync(socket, _cancellation.Token);
    }

    public IReadOnlyList<PeerInfo> GetPeers()
    {
        RemoveExpiredPeers();
        lock (_gate)
        {
            return _peers.Values
                .OrderBy(peer => peer.DeviceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(peer => peer.UserName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private async Task ReceiveLoopAsync(UdpClient socket, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(cancellationToken);
                DiscoveryMessage? message;

                try
                {
                    message = JsonSerializer.Deserialize<DiscoveryMessage>(result.Buffer, AppConstants.JsonOptions);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (message is null || message.Protocol != AppConstants.Protocol || message.InstanceId == _instanceId)
                {
                    continue;
                }

                var peer = new PeerInfo
                {
                    Id = message.InstanceId,
                    DeviceName = string.IsNullOrWhiteSpace(message.DeviceName) ? "Bilinmeyen cihaz" : message.DeviceName,
                    UserName = string.IsNullOrWhiteSpace(message.UserName) ? "Bilinmeyen kullanıcı" : message.UserName,
                    IpAddress = result.RemoteEndPoint.Address.ToString(),
                    TransferPort = message.TransferPort is > 0 and <= ushort.MaxValue
                        ? message.TransferPort
                        : AppConstants.TransferPort,
                    LastSeenUtc = DateTime.UtcNow
                };

                var changed = false;
                lock (_gate)
                {
                    if (!_peers.TryGetValue(peer.Id, out var previous) ||
                        previous.DeviceName != peer.DeviceName ||
                        previous.UserName != peer.UserName ||
                        previous.IpAddress != peer.IpAddress ||
                        previous.TransferPort != peer.TransferPort)
                    {
                        changed = true;
                    }

                    _peers[peer.Id] = peer;
                }

                if (changed)
                {
                    PeersChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException)
        {
        }
    }

    private async Task BroadcastLoopAsync(UdpClient socket, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = new DiscoveryMessage
                {
                    InstanceId = _instanceId,
                    DeviceName = Environment.MachineName,
                    UserName = Environment.UserName,
                    TransferPort = AppConstants.TransferPort
                };
                var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, AppConstants.JsonOptions));

                foreach (var endpoint in GetBroadcastEndpoints())
                {
                    try
                    {
                        await socket.SendAsync(payload, endpoint, cancellationToken);
                    }
                    catch (SocketException)
                    {
                        // A disconnected adapter should not stop discovery on other adapters.
                    }
                }

                RemoveExpiredPeers();
                await Task.Delay(AppConstants.DiscoveryIntervalMilliseconds, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private IEnumerable<IPEndPoint> GetBroadcastEndpoints()
    {
        var addresses = new Dictionary<string, IPEndPoint>(StringComparer.OrdinalIgnoreCase)
        {
            [IPAddress.Broadcast.ToString()] = new IPEndPoint(IPAddress.Broadcast, AppConstants.DiscoveryPort)
        };

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            IPInterfaceProperties properties;
            try
            {
                properties = networkInterface.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            foreach (var unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null)
                {
                    continue;
                }

                var addressBytes = unicast.Address.GetAddressBytes();
                var maskBytes = unicast.IPv4Mask.GetAddressBytes();
                var broadcastBytes = new byte[4];
                for (var index = 0; index < broadcastBytes.Length; index++)
                {
                    broadcastBytes[index] = (byte)(addressBytes[index] | (byte)~maskBytes[index]);
                }

                var broadcast = new IPAddress(broadcastBytes);
                addresses.TryAdd(broadcast.ToString(), new IPEndPoint(broadcast, AppConstants.DiscoveryPort));
            }
        }

        return addresses.Values;
    }

    private void RemoveExpiredPeers()
    {
        var threshold = DateTime.UtcNow.AddSeconds(-AppConstants.PeerTimeoutSeconds);
        var removed = false;
        lock (_gate)
        {
            foreach (var key in _peers.Where(pair => pair.Value.LastSeenUtc < threshold).Select(pair => pair.Key).ToArray())
            {
                removed |= _peers.Remove(key);
            }
        }

        if (removed)
        {
            PeersChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        _cancellation?.Cancel();
        _socket?.Dispose();
        _cancellation?.Dispose();
        _cancellation = null;
        _socket = null;
    }
}
