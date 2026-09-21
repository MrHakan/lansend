using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace LANSEND;

internal sealed class LanDeviceDiscoveryService
{
    private static readonly Regex ArpAddressPattern = new(
        @"(?m)^\s*(?<ip>\d{1,3}(?:\.\d{1,3}){3})\s+[0-9a-fA-F-]{11,17}\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<IReadOnlyList<DeviceProfile>> ScanAsync(
        IReadOnlyCollection<DeviceProfile> knownProfiles,
        CancellationToken cancellationToken)
    {
        var localAddresses = GetLocalAddresses();
        var arpBefore = await ReadArpTableAsync(cancellationToken);
        var candidates = GetCandidateAddresses(knownProfiles, localAddresses, arpBefore);
        var discovered = new ConcurrentDictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);

        await Parallel.ForEachAsync(
            candidates,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = AppConstants.ScanParallelism,
                CancellationToken = cancellationToken
            },
            async (address, token) =>
            {
                var result = await ProbeAsync(address, arpBefore.Contains(address), token);
                if (result is not null)
                {
                    discovered[address.ToString()] = result;
                }
            });

        // Even devices that block ICMP/TCP usually answer ARP on the local segment.
        // The sweep above warms the Windows neighbour table, so read it once more.
        var arpAfter = await ReadArpTableAsync(cancellationToken);
        foreach (var address in arpAfter)
        {
            if (!candidates.Contains(address) || localAddresses.Contains(address))
            {
                continue;
            }

            discovered.TryAdd(
                address.ToString(),
                new DiscoveredDevice(address.ToString(), address.ToString(), false, "ARP"));
        }

        return await MergeProfilesAsync(knownProfiles, discovered.Values, localAddresses, cancellationToken);
    }

    private static async Task<DiscoveredDevice?> ProbeAsync(
        IPAddress address,
        bool knownFromArp,
        CancellationToken cancellationToken)
    {
        var pingTask = ProbePingAsync(address, cancellationToken);
        var portTasks = AppConstants.DiscoveryPorts.ToDictionary(
            port => port,
            port => ProbeTcpPortAsync(address, port, cancellationToken));

        await Task.WhenAll(portTasks.Values.Append(pingTask));

        var pingSucceeded = await pingTask;
        var openPorts = portTasks
            .Where(pair => pair.Value.Result)
            .Select(pair => pair.Key)
            .ToArray();
        if (!knownFromArp && !pingSucceeded && openPorts.Length == 0)
        {
            return null;
        }

        var smbAvailable = openPorts.Contains(AppConstants.SmbPort);
        var method = smbAvailable
            ? "SMB"
            : pingSucceeded
                ? "Ping"
                : openPorts.Length > 0
                    ? $"TCP {openPorts[0]}"
                    : "ARP";

        return new DiscoveredDevice(
            address.ToString(),
            address.ToString(),
            smbAvailable,
            method);
    }

    private static async Task<bool> ProbePingAsync(IPAddress address, CancellationToken cancellationToken)
    {
        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(address, AppConstants.PingTimeoutMilliseconds)
                .WaitAsync(cancellationToken);
            return reply.Status == IPStatus.Success;
        }
        catch (PingException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task<bool> ProbeTcpPortAsync(
        IPAddress address,
        int port,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient(AddressFamily.InterNetwork);
        try
        {
            await client.ConnectAsync(address, port)
                .WaitAsync(TimeSpan.FromMilliseconds(AppConstants.PortProbeTimeoutMilliseconds), cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task<IReadOnlyList<DeviceProfile>> MergeProfilesAsync(
        IReadOnlyCollection<DeviceProfile> knownProfiles,
        IEnumerable<DiscoveredDevice> discoveredDevices,
        IReadOnlySet<IPAddress> localAddresses,
        CancellationToken cancellationToken)
    {
        var knownByIp = knownProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.IpAddress))
            .GroupBy(profile => profile.IpAddress, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, DeviceProfile>(StringComparer.OrdinalIgnoreCase);

        foreach (var discovered in discoveredDevices)
        {
            if (!IPAddress.TryParse(discovered.IpAddress, out var address) || localAddresses.Contains(address))
            {
                continue;
            }

            var profile = knownByIp.TryGetValue(discovered.IpAddress, out var known)
                ? known
                : new DeviceProfile
                {
                    DeviceName = discovered.IpAddress,
                    IpAddress = discovered.IpAddress,
                    ShareName = AppConstants.DefaultShareName
                };

            profile.DeviceName = await ResolveDeviceNameAsync(
                address,
                string.IsNullOrWhiteSpace(profile.DeviceName) ? discovered.DeviceName : profile.DeviceName,
                cancellationToken);
            profile.IsOnline = true;
            profile.IsSmbAvailable = discovered.IsSmbAvailable;
            profile.DiscoveryMethod = discovered.DiscoveryMethod;
            result[profile.Id] = profile;
        }

        foreach (var profile in knownProfiles)
        {
            if (!result.ContainsKey(profile.Id))
            {
                profile.IsOnline = false;
                profile.IsSmbAvailable = false;
                profile.DiscoveryMethod = string.Empty;
                result[profile.Id] = profile;
            }
        }

        return result.Values
            .OrderBy(profile => profile.IsOnline ? 0 : 1)
            .ThenBy(profile => profile.IsSmbAvailable ? 0 : 1)
            .ThenBy(profile => profile.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.IpAddress, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<string> ResolveDeviceNameAsync(
        IPAddress address,
        string fallback,
        CancellationToken cancellationToken)
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(address)
                .WaitAsync(TimeSpan.FromMilliseconds(AppConstants.DnsLookupTimeoutMilliseconds), cancellationToken);
            if (!string.IsNullOrWhiteSpace(entry.HostName))
            {
                return entry.HostName.TrimEnd('.');
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

        return string.IsNullOrWhiteSpace(fallback) ? address.ToString() : fallback;
    }

    private static async Task<HashSet<IPAddress>> ReadArpTableAsync(CancellationToken cancellationToken)
    {
        var result = new HashSet<IPAddress>();
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "arp.exe",
                Arguments = "-a",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (process is null)
            {
                return result;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask;
            foreach (Match match in ArpAddressPattern.Matches(output))
            {
                if (IPAddress.TryParse(match.Groups["ip"].Value, out var address) &&
                    address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address) &&
                    !address.Equals(IPAddress.Broadcast))
                {
                    result.Add(address);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }

        return result;
    }

    private static HashSet<IPAddress> GetLocalAddresses()
    {
        var result = new HashSet<IPAddress>();
        foreach (var networkInterface in GetUsableNetworkInterfaces())
        {
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

    private static HashSet<IPAddress> GetCandidateAddresses(
        IReadOnlyCollection<DeviceProfile> knownProfiles,
        IReadOnlySet<IPAddress> localAddresses,
        IReadOnlySet<IPAddress> arpAddresses)
    {
        var result = new HashSet<IPAddress>();

        foreach (var profile in knownProfiles)
        {
            if (IPAddress.TryParse(profile.IpAddress, out var knownAddress) && !localAddresses.Contains(knownAddress))
            {
                result.Add(knownAddress);
            }
        }

        foreach (var address in arpAddresses.Where(address => !localAddresses.Contains(address)))
        {
            result.Add(address);
        }

        foreach (var networkInterface in GetUsableNetworkInterfaces())
        {
            IPInterfaceProperties properties;
            try
            {
                properties = networkInterface.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            foreach (var gateway in properties.GatewayAddresses
                         .Select(item => item.Address)
                         .Where(address => address.AddressFamily == AddressFamily.InterNetwork))
            {
                result.Add(gateway);
            }

            foreach (var unicast in properties.UnicastAddresses.Where(item =>
                         item.Address.AddressFamily == AddressFamily.InterNetwork &&
                         !IPAddress.IsLoopback(item.Address) &&
                         !item.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal)))
            {
                AddSubnetCandidates(result, unicast.Address, unicast.IPv4Mask, localAddresses);
            }
        }

        result.ExceptWith(localAddresses);
        return result.Take(AppConstants.MaxTotalScanHosts).ToHashSet();
    }

    private static void AddSubnetCandidates(
        ISet<IPAddress> result,
        IPAddress address,
        IPAddress? subnetMask,
        IReadOnlySet<IPAddress> localAddresses)
    {
        if (subnetMask is null)
        {
            return;
        }

        var ip = ToUInt32(address);
        var mask = ToUInt32(subnetMask);
        var network = ip & mask;
        var broadcast = network | ~mask;
        var hostCount = (long)broadcast - network - 1;

        // On very large corporate/ship networks, scan the local /24 instead of
        // silently skipping the adapter or flooding thousands of addresses.
        if (hostCount > AppConstants.MaxScanHostsPerAdapter)
        {
            network = ip & 0xFFFFFF00u;
            broadcast = network | 0x000000FFu;
            hostCount = 254;
        }

        if (hostCount <= 0)
        {
            return;
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

    private static IEnumerable<NetworkInterface> GetUsableNetworkInterfaces()
    {
        return NetworkInterface.GetAllNetworkInterfaces().Where(networkInterface =>
            networkInterface.OperationalStatus == OperationalStatus.Up &&
            networkInterface.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel);
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

    private sealed record DiscoveredDevice(
        string IpAddress,
        string DeviceName,
        bool IsSmbAvailable,
        string DiscoveryMethod);
}
