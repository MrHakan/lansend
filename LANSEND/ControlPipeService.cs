using System.IO.Pipes;
using System.Text.Json;

namespace LANSEND;

internal sealed class ControlPipeService : IDisposable
{
    private CancellationTokenSource? _cancellation;

    public event EventHandler<ControlRequest>? RequestReceived;

    public void Start()
    {
        if (_cancellation is not null)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        _ = ListenLoopAsync(_cancellation.Token);
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await using var server = new NamedPipeServerStream(
                    AppConstants.ControlPipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken);

                using var reader = new StreamReader(server);
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var request = JsonSerializer.Deserialize<ControlRequest>(line, AppConstants.JsonOptions);
                    if (request is not null)
                    {
                        RequestReceived?.Invoke(this, request);
                    }
                }
                catch (JsonException)
                {
                    // Ignore malformed messages from a stale launcher.
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public static async Task<bool> TryForwardAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var request = new ControlRequest
        {
            Command = paths.Count > 0 ? "send" : "open",
            Paths = paths.ToList()
        };
        var payload = JsonSerializer.Serialize(request, AppConstants.JsonOptions) + "\n";

        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                await using var client = new NamedPipeClientStream(
                    ".",
                    AppConstants.ControlPipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous);
                await client.ConnectAsync(400, cancellationToken);
                await using var writer = new StreamWriter(client) { AutoFlush = true };
                await writer.WriteAsync(payload);
                return true;
            }
            catch (TimeoutException)
            {
                await Task.Delay(200, cancellationToken);
            }
            catch (IOException)
            {
                await Task.Delay(200, cancellationToken);
            }
        }

        return false;
    }

    public void Dispose()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }
}
