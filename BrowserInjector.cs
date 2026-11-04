using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace SurfCoreAutoStart;

internal static class BrowserInjector
{
    public static async Task<bool> OpenConsoleAsync(string url, string token, Action<string> status)
    {
        try
        {
            var edge = FindEdge();
            if (edge == null)
            {
                status("未找到 Edge，使用默认浏览器打开控制台");
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return false;
            }

            var profileRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SurfCore Auto-start", "EdgeProfiles");
            Directory.CreateDirectory(profileRoot);
            var profile = Path.Combine(profileRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(profile);
            var port = FreePort();

            var args = $"--user-data-dir=\"{profile}\" --remote-debugging-port={port} --remote-allow-origins=* " +
                       "--no-first-run --no-default-browser-check --disable-sync " +
                       "--disable-features=msEdgeFirstRunExperience,msEdgeSyncPromo " +
                       $"--new-window \"{url}\"";
            status("正在打开 SnowLuma 控制台…");
            var psi = new ProcessStartInfo(edge, args)
            {
                UseShellExecute = false,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            };
            Process.Start(psi)?.Dispose();

            if (string.IsNullOrEmpty(token))
            {
                CleanupProfiles(profileRoot, profile);
                return false;
            }

            var wsUrl = await WaitForPageTargetAsync(port, url, TimeSpan.FromSeconds(18));
            if (wsUrl == null)
            {
                status("无法连接浏览器调试接口，控制台已打开，请手动登录");
                CleanupProfiles(profileRoot, profile);
                return false;
            }

            var origin = new Uri(url).GetLeftPart(UriPartial.Authority);
            var ok = await InjectTokenAsync(wsUrl, token, origin);
            status(ok ? "控制台已自动登录" : "自动登录未完成，请在浏览器中手动登录");
            CleanupProfiles(profileRoot, profile);
            return ok;
        }
        catch (Exception ex)
        {
            Log.Error("BrowserInjector 失败", ex);
            status("打开控制台失败：" + ex.Message);
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
            return false;
        }
    }

    private static string FindEdge()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "Application", "msedge.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<string> WaitForPageTargetAsync(int port, string url, TimeSpan timeout)
    {
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + timeout;
        var origin = new Uri(url).GetLeftPart(UriPartial.Authority);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var json = await http.GetStringAsync($"http://127.0.0.1:{port}/json/list");
                using var doc = JsonDocument.Parse(json);
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var type = item.TryGetProperty("type", out var t) ? t.GetString() : "";
                    var pageUrl = item.TryGetProperty("url", out var u) ? u.GetString() : "";
                    var ws = item.TryGetProperty("webSocketDebuggerUrl", out var w) ? w.GetString() : "";
                    if (type == "page" && pageUrl != null && pageUrl.StartsWith(origin, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(ws))
                        return ws;
                }
            }
            catch { }
            await Task.Delay(400);
        }
        return null;
    }

    private static async Task<bool> InjectTokenAsync(string wsUrl, string token, string origin)
    {
        try
        {
            using var ws = new ClientWebSocket();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await ws.ConnectAsync(new Uri(wsUrl), cts.Token);
            var tokenJson = JsonSerializer.Serialize(token);
            var originJson = JsonSerializer.Serialize(origin);
            // Edge 的目标页会在导航完成前就出现在 /json/list 里。此时 document 还是
            // 初始 about:blank，访问 localStorage 会抛 SecurityError。必须等页面
            // 真正完成加载、origin 正确后再写入，并检查写入结果。
            var expression =
                "(function(){" +
                "try{" +
                $"if(location.origin!=={originJson})return 'wait-origin';" +
                "if(document.readyState!=='complete')return 'wait-ready';" +
                $"localStorage.setItem('snowluma_token',{tokenJson});" +
                $"if(localStorage.getItem('snowluma_token')!=={tokenJson})return 'mismatch';" +
                "return 'ok';" +
                "}catch(e){return 'err:'+e.name+':'+e.message;}" +
                "})()";

            for (var attempt = 0; attempt < 14; attempt++)
            {
                var id = attempt + 1;
                var payload = JsonSerializer.Serialize(new
                {
                    id,
                    method = "Runtime.evaluate",
                    @params = new { expression, awaitPromise = false, returnByValue = true }
                });
                var bytes = Encoding.UTF8.GetBytes(payload);
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);

                string result = null;
                var buffer = new byte[8192];
                using (var recvCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token))
                {
                    recvCts.CancelAfter(TimeSpan.FromSeconds(3));
                    try
                    {
                        while (!recvCts.IsCancellationRequested)
                        {
                            var received = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), recvCts.Token);
                            if (received.MessageType == WebSocketMessageType.Close) break;
                            var text = Encoding.UTF8.GetString(buffer, 0, received.Count);
                            if (!text.Contains($"\"id\":{id}")) continue;
                            try
                            {
                                using var doc = JsonDocument.Parse(text);
                                if (doc.RootElement.TryGetProperty("result", out var res) &&
                                    res.TryGetProperty("result", out var inner) &&
                                    inner.TryGetProperty("value", out var value) &&
                                    value.ValueKind == JsonValueKind.String)
                                    result = value.GetString();
                                if (doc.RootElement.TryGetProperty("result", out var res2) &&
                                    res2.TryGetProperty("exceptionDetails", out _))
                                    result = "exception";
                            }
                            catch { }
                            break;
                        }
                    }
                    catch (OperationCanceledException) { }
                }

                if (result == "ok")
                {
                    // 写入成功后再刷新，让前端从 localStorage 读取 token。
                    try
                    {
                        var reload = JsonSerializer.Serialize(new
                        {
                            id = id + 100,
                            method = "Runtime.evaluate",
                            @params = new { expression = "location.reload()", awaitPromise = false }
                        });
                        await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(reload)),
                            WebSocketMessageType.Text, true, cts.Token);
                    }
                    catch { }
                    return true;
                }

                Log.Warn($"CDP 注入第 {attempt + 1} 次未完成: {result ?? "无响应"}");
                await Task.Delay(700, cts.Token);
            }

            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); } catch { }
            return false;
        }
        catch (Exception ex)
        {
            Log.Warn("CDP 注入失败: " + ex.Message);
            return false;
        }
    }

    private static void CleanupProfiles(string root, string keep)
    {
        try
        {
            var dirs = Directory.GetDirectories(root).OrderByDescending(Directory.GetCreationTime).ToList();
            for (var i = 3; i < dirs.Count; i++)
            {
                if (string.Equals(dirs[i], keep, StringComparison.OrdinalIgnoreCase)) continue;
                try { Directory.Delete(dirs[i], true); } catch { }
            }
        }
        catch { }
    }
}
