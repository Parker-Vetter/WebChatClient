using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ClientWebChat.Services;

public class ChatService : IAsyncDisposable
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private Uri? _endpoint;
    private string? _username;

    /// <summary>
    /// Fires when a raw text message is received from the WebSocket server.
    /// </summary>
    public event Action<string>? MessageReceived;

    /// <summary>
    /// Fires when connection has closed (cleanly or due to error).
    /// </summary>
    public event Action? ConnectionClosed;

    public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open;

    /// <summary>
    /// Connect to the server and start the receive loop.
    /// </summary>
    public async Task ConnectAsync(Uri serverUri, string username, CancellationToken cancellationToken)
    {
        if (IsConnected) return;

        _endpoint = serverUri;
        _username = username;

        _ws = new ClientWebSocket();
        // Optionally set options: _ws.Options.SetRequestHeader(...)
        await _ws.ConnectAsync(serverUri, cancellationToken).ConfigureAwait(false);

        _receiveCts = new CancellationTokenSource();
        var linked = CancellationTokenSource.CreateLinkedTokenSource(_receiveCts.Token, cancellationToken);

        _receiveTask = Task.Run(() => ReceiveLoopAsync(linked.Token), linked.Token);
    }

    /// <summary>
    /// Send a message to the server. The message is wrapped into a JSON payload:
    /// { "user": "<username>", "text": "<message>" }
    /// </summary>
    public async Task SendMessageAsync(string text)
    {
        if (!IsConnected) throw new InvalidOperationException("Not connected.");

        var payload = new { user = _username, text = text };
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await _ws!.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];

        try
        {
            while (!ct.IsCancellationRequested && _ws != null && _ws.State == WebSocketState.Open)
            {
                var ms = new MemoryStream();
                WebSocketReceiveResult? result;
                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await CloseSocketAsync().ConfigureAwait(false);
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                ms.Seek(0, SeekOrigin.Begin);
                var msg = Encoding.UTF8.GetString(ms.ToArray());

                // Notify subscribers on threadpool; consumers should marshal to UI.
                MessageReceived?.Invoke(msg);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
        catch (Exception)
        {
            // Notify connection closed on error
            await CloseSocketAsync().ConfigureAwait(false);
        }
        finally
        {
            ConnectionClosed?.Invoke();
        }
    }

    /// <summary>
    /// Gracefully close the socket.
    /// </summary>
    public async Task DisconnectAsync()
    {
        await CloseSocketAsync().ConfigureAwait(false);
    }

    private async Task CloseSocketAsync()
    {
        try
        {
            _receiveCts?.Cancel();
            if (_ws != null)
            {
                if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived)
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", CancellationToken.None).ConfigureAwait(false);
                }
                _ws.Dispose();
                _ws = null;
            }
        }
        catch
        {
            // ignore
        }
        finally
        {
            ConnectionClosed?.Invoke();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _receiveCts?.Cancel();
            if (_ws != null)
            {
                _ws.Dispose();
                _ws = null;
            }
        }
        catch
        {
            // ignore
        }
        finally
        {
            _sendLock.Dispose();
        }
    }
}
