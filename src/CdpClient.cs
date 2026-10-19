using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace BgClicker
{
    public sealed class CdpTarget
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Url { get; set; }
        public string WsUrl { get; set; }
        public string Type { get; set; }

        public override string ToString()
        {
            string title = string.IsNullOrEmpty(Title) ? "(без названия)" : Title;
            if (title.Length > 46)
            {
                title = title.Substring(0, 43) + "...";
            }

            string url = Url ?? "";
            if (url.Length > 60)
            {
                url = url.Substring(0, 57) + "...";
            }

            return title + "  —  " + url;
        }
    }

    internal static class CdpScripts
    {
        // Injects a full-page overlay, waits for one left click and resolves with
        // viewport (CSS pixel) coordinates. Esc resolves with null.
        public const string PickPoint = @"
new Promise(function (resolve) {
  try { window.focus(); } catch (e) { }
  var old = document.getElementById('__bgclicker_overlay__');
  if (old) { old.parentNode.removeChild(old); }
  var layer = document.createElement('div');
  layer.id = '__bgclicker_overlay__';
  layer.style.cssText = 'position:fixed;left:0;top:0;right:0;bottom:0;z-index:2147483647;cursor:crosshair;background:rgba(0,0,0,0.15);';
  var tip = document.createElement('div');
  tip.style.cssText = 'position:fixed;left:50%;top:16px;transform:translateX(-50%);background:#111;color:#0f0;font:14px Consolas,monospace;padding:8px 14px;border:2px solid #0f0;z-index:2147483647;pointer-events:none;';
  tip.textContent = 'Кликните в нужную точку. Esc — отмена';
  var done = false;
  function cleanup() {
    if (layer.parentNode) { layer.parentNode.removeChild(layer); }
    if (tip.parentNode) { tip.parentNode.removeChild(tip); }
    document.removeEventListener('keydown', onKey, true);
  }
  function onKey(e) {
    if (e.key === 'Escape') { done = true; cleanup(); resolve(null); }
  }
  document.addEventListener('keydown', onKey, true);
  layer.addEventListener('mousedown', function (e) {
    if (e.button !== 0 || done) { return; }
    e.preventDefault();
    e.stopPropagation();
    done = true;
    var x = e.clientX;
    var y = e.clientY;
    cleanup();
    resolve({ x: x, y: y });
  }, true);
  document.documentElement.appendChild(layer);
  document.documentElement.appendChild(tip);
})";
    }

    public sealed class CdpConnection : IDisposable
    {
        private static readonly JavaScriptSerializer Serializer =
            new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };

        private readonly ConcurrentDictionary<int, TaskCompletionSource<Dictionary<string, object>>> _pending =
            new ConcurrentDictionary<int, TaskCompletionSource<Dictionary<string, object>>>();

        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private ClientWebSocket _socket;
        private CancellationTokenSource _cancellation;
        private Task _receiveTask;
        private int _nextId;
        private volatile bool _closed = true;

        public bool IsConnected
        {
            get { return !_closed && _socket != null && _socket.State == WebSocketState.Open; }
        }

        public static async Task<List<CdpTarget>> ListTargetsAsync(int port)
        {
            var result = new List<CdpTarget>();
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) })
            {
                string json = await http
                    .GetStringAsync("http://127.0.0.1:" + port + "/json/list")
                    .ConfigureAwait(false);

                object[] items = Serializer.DeserializeObject(json) as object[];
                if (items == null)
                {
                    return result;
                }

                foreach (object item in items)
                {
                    var map = item as Dictionary<string, object>;
                    if (map == null)
                    {
                        continue;
                    }

                    string type = Str(map, "type");
                    string ws = Str(map, "webSocketDebuggerUrl");
                    if (type != "page" || string.IsNullOrEmpty(ws))
                    {
                        continue;
                    }

                    result.Add(new CdpTarget
                    {
                        Id = Str(map, "id"),
                        Title = Str(map, "title"),
                        Url = Str(map, "url"),
                        WsUrl = ws,
                        Type = type
                    });
                }
            }

            return result;
        }

        public async Task ConnectAsync(CdpTarget target)
        {
            if (target == null || string.IsNullOrEmpty(target.WsUrl))
            {
                throw new ArgumentException("Не выбрана вкладка браузера");
            }

            Dispose();

            _socket = new ClientWebSocket();
            _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
            _cancellation = new CancellationTokenSource();
            _closed = false;

            await _socket.ConnectAsync(new Uri(target.WsUrl), _cancellation.Token).ConfigureAwait(false);
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_cancellation.Token));
        }

        public async Task<Dictionary<string, object>> SendAsync(
            string method,
            Dictionary<string, object> parameters,
            int timeoutMs = 5000)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Нет соединения с браузером");
            }

            int id = Interlocked.Increment(ref _nextId);
            var completion = new TaskCompletionSource<Dictionary<string, object>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = completion;

            try
            {
                await SendRawAsync(id, method, parameters).ConfigureAwait(false);

                using (var timeout = new CancellationTokenSource(timeoutMs))
                using (timeout.Token.Register(delegate
                {
                    completion.TrySetException(new TimeoutException("Таймаут команды " + method));
                }))
                {
                    return await completion.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                TaskCompletionSource<Dictionary<string, object>> ignored;
                _pending.TryRemove(id, out ignored);
            }
        }

        /// <summary>
        /// Sends a CDP command without waiting for its response. Used by the click
        /// loop to avoid a full round trip per click.
        /// </summary>
        public async Task SendNoWaitAsync(string method, Dictionary<string, object> parameters)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Нет соединения с браузером");
            }

            await SendRawAsync(Interlocked.Increment(ref _nextId), method, parameters).ConfigureAwait(false);
        }

        public async Task ClickAsync(double x, double y)
        {
            var moved = new Dictionary<string, object>
            {
                { "type", "mouseMoved" },
                { "x", x },
                { "y", y },
                { "button", "none" },
                { "buttons", 0 }
            };
            var pressed = new Dictionary<string, object>
            {
                { "type", "mousePressed" },
                { "x", x },
                { "y", y },
                { "button", "left" },
                { "buttons", 1 },
                { "clickCount", 1 }
            };
            var released = new Dictionary<string, object>
            {
                { "type", "mouseReleased" },
                { "x", x },
                { "y", y },
                { "button", "left" },
                { "buttons", 0 },
                { "clickCount", 1 }
            };

            await SendNoWaitAsync("Input.dispatchMouseEvent", moved).ConfigureAwait(false);
            await SendNoWaitAsync("Input.dispatchMouseEvent", pressed).ConfigureAwait(false);
            await SendNoWaitAsync("Input.dispatchMouseEvent", released).ConfigureAwait(false);
        }

        public void ClickSync(double x, double y)
        {
            ClickAsync(x, y).GetAwaiter().GetResult();
        }

        public async Task<object> EvaluateAsync(string expression, bool awaitPromise, int timeoutMs)
        {
            var parameters = new Dictionary<string, object>
            {
                { "expression", expression },
                { "returnByValue", true },
                { "awaitPromise", awaitPromise },
                { "userGesture", true }
            };

            Dictionary<string, object> message = await SendAsync(
                "Runtime.evaluate", parameters, timeoutMs).ConfigureAwait(false);

            var result = GetMap(message, "result");
            if (result == null)
            {
                throw new InvalidOperationException("Пустой ответ браузера");
            }

            if (result.ContainsKey("exceptionDetails"))
            {
                var details = GetMap(result, "exceptionDetails");
                string text = details != null ? Str(details, "text") : "неизвестная ошибка";
                throw new InvalidOperationException("Ошибка на странице: " + text);
            }

            var inner = GetMap(result, "result");
            if (inner == null)
            {
                return null;
            }

            object value;
            return inner.TryGetValue("value", out value) ? value : null;
        }

        private async Task SendRawAsync(int id, string method, Dictionary<string, object> parameters)
        {
            var payload = new Dictionary<string, object>
            {
                { "id", id },
                { "method", method },
                { "params", parameters ?? new Dictionary<string, object>() }
            };

            byte[] bytes = Encoding.UTF8.GetBytes(Serializer.Serialize(payload));

            await _sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token))
                {
                    linked.CancelAfter(3000);
                    await _socket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        true,
                        linked.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[16 * 1024];
            var accumulated = new MemoryStream();

            try
            {
                while (!cancellationToken.IsCancellationRequested && _socket.State == WebSocketState.Open)
                {
                    accumulated.SetLength(0);
                    WebSocketReceiveResult received;

                    do
                    {
                        received = await _socket.ReceiveAsync(
                            new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);

                        if (received.MessageType == WebSocketMessageType.Close)
                        {
                            return;
                        }

                        accumulated.Write(buffer, 0, received.Count);
                    }
                    while (!received.EndOfMessage);

                    string text = Encoding.UTF8.GetString(accumulated.ToArray());
                    var message = Serializer.DeserializeObject(text) as Dictionary<string, object>;
                    if (message == null)
                    {
                        continue;
                    }

                    object idValue;
                    if (!message.TryGetValue("id", out idValue) || idValue == null)
                    {
                        continue; // event, not a command response
                    }

                    int id = Convert.ToInt32(idValue);
                    TaskCompletionSource<Dictionary<string, object>> completion;
                    if (_pending.TryRemove(id, out completion))
                    {
                        completion.TrySetResult(message);
                    }
                }
            }
            catch (Exception ex)
            {
                FailAll(ex);
            }
            finally
            {
                _closed = true;
            }
        }

        private void FailAll(Exception error)
        {
            foreach (var pair in _pending)
            {
                TaskCompletionSource<Dictionary<string, object>> completion;
                if (_pending.TryRemove(pair.Key, out completion))
                {
                    completion.TrySetException(error);
                }
            }
        }

        private static string Str(Dictionary<string, object> map, string key)
        {
            object value;
            return map.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value)
                : null;
        }

        private static Dictionary<string, object> GetMap(Dictionary<string, object> map, string key)
        {
            object value;
            return map.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }

        public void Dispose()
        {
            _closed = true;

            try
            {
                if (_cancellation != null)
                {
                    _cancellation.Cancel();
                }
            }
            catch
            {
            }

            try
            {
                if (_socket != null)
                {
                    _socket.Dispose();
                }
            }
            catch
            {
            }

            _socket = null;
            _pending.Clear();
        }
    }
}
