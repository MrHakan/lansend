using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LANSEND;

internal static class SmbConnection
{
    private const int ResourceGlobalNet = 2;
    private const int ResourceTypeDisk = 1;
    private const int ConnectTemporary = 4;

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(
        ref NetResource netResource,
        string? password,
        string? username,
        int flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2(
        string name,
        int flags,
        bool force);

    public static IDisposable Connect(string remoteName, SmbCredentials credentials)
    {
        var resource = new NetResource
        {
            Scope = ResourceGlobalNet,
            Type = ResourceTypeDisk,
            RemoteName = remoteName
        };
        var result = WNetAddConnection2(
            ref resource,
            credentials.Password,
            credentials.UserName,
            ConnectTemporary);
        if (result != 0)
        {
            throw new Win32Exception(result, $"SMB paylaşımına bağlanılamadı: {remoteName}");
        }

        return new ConnectionLease(remoteName);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public int Scope;
        public int Type;
        public int DisplayType;
        public int Usage;
        public string? LocalName;
        public string? RemoteName;
        public string? Comment;
        public string? Provider;
    }

    private sealed class ConnectionLease : IDisposable
    {
        private readonly string _remoteName;
        private int _disposed;

        public ConnectionLease(string remoteName)
        {
            _remoteName = remoteName;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                WNetCancelConnection2(_remoteName, 0, true);
            }
        }
    }
}
