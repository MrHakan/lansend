using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LANSEND;

internal sealed class LanDeviceDiscoveryService
{
    public async Task<IReadOnlyList<DeviceProfile>> ScanAsync(
        IReadOnlyCollection<DeviceProfile> knownProfiles,
        CancellationToken cancellationToken)
    {
        var localAddresses = GetLocalAddresses();
        var candidates = GetCandidateAddresses(knownProfiles, localAddresses);
        var discovered = new ConcurrentDictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);

        await Parallel.ForEachAsync(
            candidates,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = 32,
                CancellationToken = cancellationToken
            },
            async (address, token) =>
            {
                var result = await ProbeAsync(address, token);
                if (result is not null)
                {
                    discovered[address.ToString()] = result;
                }
            });

        return MergeProfiles(knownProfiles, discovered.Values, localAddresses);
    }

    private static async Task<DiscoveredDevice?> ProbeAsync(IPAddress address, CancellationToken cancellationToken)
    {
        using var client = new TcpClient(AddressFamily.InterNetwork);
        try
        {
            await client.ConnectAsync(address, AppConstants.SmbPort)
                .WaitAsync(TimeSpan.FromMilliseconds(AppConstants.SmbProbeTimeoutMilliseconds), cancellationToken);
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        var deviceName = address.ToString();
        try
        {
            var entry = await Dns.GetHostEntryAsync(address)
                .WaitAsync(TimeSpan.FromMilliseconds(600), cancellationToken);
            if (!string.IsNullOrWhiteSpace(entry.HostName))
            {
                deviceName = entry.HostName.TrimEnd('.');
            }
        }
        catch (SocketException)
        {
        }
        catch (TimeoutException)
        {
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        return new DiscoveredDevice(address.ToString(), deviceName);
    }

    private static IReadOnlyList<DeviceProfile> MergeProfiles(
        IReadOnlyCollection<DeviceProfile> knownProfiles,
        IEnumerable<DiscoveredDevice> discoveredDevices,
        IReadOnlySet<IPAddress> localAddresses)
    {
        var knownByIp = knownProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.IpAddress))
            .ToDictionary(profile => profile.IpAddress, StringComparer.OrdinalIgnoreCase);
        var knownByName = knownProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.DeviceName))
            .GroupBy(profile => profile.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, DeviceProfile>(StringComparer.OrdinalIgnoreCase);

        foreach (var discovered in discoveredDevices.OrderBy(device => device.IpAddress, StringComparer.OrdinalIgnoreCase))
        {
            if (!IPAddress.TryParse(discovered.IpAddress, out var address) || localAddresses.Contains(address))
            {
                continue;
            }

            DeviceProfile profile;
            if (knownByIp.TryGetValue(discovered.IpAddress, out var knownByAddress))
            {
                profile = knownByAddress;
            }
            else if (knownByName.TryGetValue(discovered.DeviceName, out var knownByDeviceName))
            {
                profile = knownByDeviceName;
                profile.IpAddress = discovered.IpAddress;
            }
            else
            {
                profile = new DeviceProfile
                {
                    DeviceName = discovered.DeviceName,
                    IpAddress = discovered.IpAddress,
                    ShareName = AppConstants.DefaultShareName
                };
            }

            if (string.IsNullOrWhiteSpace(profile.DeviceName) || profile.DeviceName == profile.IpAddress)
            {
                profile.DeviceName = discovered.DeviceName;
            }

            profile.IsOnline = true;
            result[profile.Id] = profile;
        }

        foreach (var profile in knownProfiles)
        {
            if (!result.ContainsKey(profile.Id))
            {
                profile.IsOnline = false;
                result[profile.Id] = profile;
            }
        }

        return result.Values
            .OrderBy(profile => profile.IsOnline ? 0 : 1)
            .ThenBy(profile => profile.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.IpAddress, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static HashSet<IPAddress> GetLocalAddresses()
    {
        var result = new HashSet<IPAddress>();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            try
            {
                foreach (var address in networkInterface.GetIPProperties().UnicastAddresses
                             .Where(item => item.Address.AddressFamily == AddressFamily.InterNetwork))
                {
                    result.Add(address.Address);
                }
            }
            catch (NetworkInformationException)
            {
            }
        }

        return result;
    }

    private static IReadOnlyList<IPAddress> GetCandidateAddresses(
        IReadOnlyCollection<DeviceProfile> knownProfiles,
        IReadOnlySet<IPAddress> localAddresses)
    {
        var result = new HashSet<IPAddress>();
        foreach (var profile in knownProfiles)
        {
            if (IPAddress.TryParse(profile.IpAddress, out var knownAddress) && !localAddresses.Contains(knownAddress))
            {
                result.Add(knownAddress);
            }
        }

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

            foreach (var unicast in properties.UnicastAddresses.Where(item =>
                         item.Address.AddressFamily == AddressFamily.InterNetwork &&
                         !IPAddress.IsLoopback(item.Address) &&
                         !item.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal)))
            {
                if (unicast.IPv4Mask is null)
                {
                    continue;
                }

                var ip = ToUInt32(unicast.Address);
                var mask = ToUInt32(unicast.IPv4Mask);
                var network = ip & mask;
                var broadcast = network | ~mask;
                var hostCount = (long)broadcast - network - 1;
                if (hostCount <= 0 || hostCount > AppConstants.MaxScanHosts)
                {
                    continue;
                }

                for (var value = network + 1; value < broadcast; value++)
                {
                    var candidate = FromUInt32(value);
                    if (!localAddresses.Contains(candidate))
                    {
                        result.Add(candidate);
                    }
                }
            }
        }

        return result.ToArray();
    }

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) |
               ((uint)bytes[1] << 16) |
               ((uint)bytes[2] << 8) |
               bytes[3];
    }

    private static IPAddress FromUInt32(uint value) => new(new byte[]
    {
        (byte)(value >> 24),
        (byte)(value >> 16),
        (byte)(value >> 8),
        (byte)value
    });

    private sealed record DiscoveredDevice(string IpAddress, string DeviceName);
}
