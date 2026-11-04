using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SurfCoreAutoStart;

internal sealed class LaunchResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string Token { get; set; } = "";
    public bool Injected { get; set; }
    public int InjectedPid { get; set; }
}

internal static class LaunchService
{
    private const int LogTailLimit = 1200;

    public static async Task<LaunchResult> RunAsync(AppConfig cfg, Action<string> status)
    {
        var result = new LaunchResult();
        try
        {
            var surfCoreDir = !string.IsNullOrEmpty(cfg.SurfCoreDir) && Directory.Exists(cfg.SurfCoreDir)
                ? cfg.SurfCoreDir
                : PathResolver.SurfCoreDir();
            var snowLumaDir = string.IsNullOrEmpty(cfg.SnowLumaDir) ? PathResolver.SnowLumaDir(surfCoreDir) : cfg.SnowLumaDir;
            var node = PathResolver.NodeExe(snowLumaDir);
            if (string.IsNullOrEmpty(node)) node = FindNode();

            // 官方一键启动顺序：先让 Harness 真正就绪，再启动 SurfCore，最后启动 SnowLuma。
            status("正在检查 DeepSeek Harness…");
            if (!await EnsureDshAsync(cfg, node, surfCoreDir, status))
            {
                result.Message = "DeepSeek Harness 未就绪，已停止启动 SurfCore/SnowLuma。请先手动启动 Harness 后重试。";
                status(result.Message);
                return result;
            }

            await EnsureSurfCoreAsync(cfg, node, surfCoreDir, status);
            await EnsureSnowLumaAsync(cfg, snowLumaDir, node, status);

            if (!await WaitPortAsync(cfg.SnowLumaPort, TimeSpan.FromSeconds(25)))
            {
                result.Message = $"SnowLuma 控制台端口 {cfg.SnowLumaPort} 未就绪";
                status(result.Message);
                return result;
            }

            // SnowLuma 先起来，再等 OneBot（3000/3001）报告 QQ 已登录，最后才查询进程列表。
            status("正在等待 QQ 登录与 OneBot 就绪…");
            var oneBotReady = await WaitPortAsync(3000, TimeSpan.FromSeconds(45));
            if (oneBotReady) oneBotReady = await WaitPortAsync(3001, TimeSpan.FromSeconds(10));
            if (!oneBotReady)
                status("OneBot 尚未就绪：请确认 QQ 已登录，并开启 QQ 的开机自启与自动登录");

            if (string.IsNullOrWhiteSpace(cfg.SnowLumaPassword))
            {
                status("未填写 SnowLuma 控制台令牌，打开控制台后请手动登录");
                await BrowserInjector.OpenConsoleAsync($"http://127.0.0.1:{cfg.SnowLumaPort}/", "", status);
                result.Message = "已打开控制台（未配置控制台令牌）";
                result.Success = true;
                return result;
            }

            status("正在登录 SnowLuma 控制台…");
            var (token, loginMessage) = await LoginAsync(cfg.SnowLumaPort, cfg.SnowLumaPassword);
            if (string.IsNullOrEmpty(token))
            {
                result.Message = loginMessage;
                status(loginMessage);
                await BrowserInjector.OpenConsoleAsync($"http://127.0.0.1:{cfg.SnowLumaPort}/", "", status);
                return result;
            }
            result.Token = token;

            if (!string.IsNullOrWhiteSpace(cfg.QqNumber))
            {
                status($"正在等待 QQ {cfg.QqNumber} 进程并注入…");
                var (ok, message, pid) = await InjectProcessAsync(cfg.SnowLumaPort, token, cfg.QqNumber, oneBotReady);
                result.Injected = ok;
                result.InjectedPid = pid;
                status(message);
                result.Message = message;
            }

            status("正在打开 SnowLuma 控制台…");
            await BrowserInjector.OpenConsoleAsync($"http://127.0.0.1:{cfg.SnowLumaPort}/", token, status);
            result.Success = true;
            if (string.IsNullOrEmpty(result.Message)) result.Message = "SurfCore 已启动";
            return result;
        }
        catch (Exception ex)
        {
            Log.Error("LaunchService 失败", ex);
            result.Message = "启动失败：" + ex.Message;
            status(result.Message);
            return result;
        }
    }

    private static async Task<bool> EnsureDshAsync(AppConfig cfg, string node, string surfCoreDir, Action<string> status)
    {
        try
        {
            if (Process.GetProcessesByName("DeepSeek Harness").Length == 0)
            {
                var exe = !string.IsNullOrEmpty(cfg.DshPath) && File.Exists(cfg.DshPath) ? cfg.DshPath : PathResolver.DshExe();
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                {
                    status("未找到 DeepSeek Harness");
                    return false;
                }
                status("正在启动 DeepSeek Harness…");
                Process.Start(new ProcessStartInfo(exe)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? ""
                });
            }
            else
            {
                status("正在确认 DeepSeek Harness 就绪…");
            }

            // 不能只看进程名：必须等到 Harness 的 Web 服务真正可访问。
            for (var attempt = 0; attempt < 25; attempt++)
            {
                var url = await DiscoverDshUrlAsync(node, surfCoreDir);
                if (!string.IsNullOrEmpty(url))
                {
                    await SyncDshPortAsync(node, surfCoreDir);
                    if (await HttpOkAsync(url))
                    {
                        Log.Info("DSH 已就绪: " + url);
                        return true;
                    }
                }
                await Task.Delay(1500);
            }
            status("DeepSeek Harness 已在运行，但 Web 服务尚未就绪");
            return false;
        }
        catch (Exception ex)
        {
            Log.Warn("启动 DSH 失败: " + ex.Message);
            status("DeepSeek Harness 启动失败：" + ex.Message);
            return false;
        }
    }

    private static async Task<string> DiscoverDshUrlAsync(string node, string surfCoreDir)
    {
        if (string.IsNullOrEmpty(node) || !Directory.Exists(surfCoreDir)) return "";
        var script = Path.Combine(surfCoreDir, "scripts", "sync-dsh-port.mjs");
        if (!File.Exists(script)) return "";
        var output = await RunNodeCaptureAsync(node, surfCoreDir, "\"scripts\\sync-dsh-port.mjs\" --print", 8000);
        var match = Regex.Match(output, @"https?://127\.0\.0\.1:\d+");
        if (!match.Success)
            Log.Warn("DSH URL 未解析，原始输出: " + output.Replace("\r", " ").Replace("\n", " "));
        return match.Success ? match.Value : "";
    }

    private static async Task SyncDshPortAsync(string node, string surfCoreDir)
    {
        if (string.IsNullOrEmpty(node) || !Directory.Exists(surfCoreDir)) return;
        var script = Path.Combine(surfCoreDir, "scripts", "sync-dsh-port.mjs");
        if (!File.Exists(script)) return;
        await RunNodeCaptureAsync(node, surfCoreDir, "\"scripts\\sync-dsh-port.mjs\"", 15000);
    }

    private static async Task<bool> HttpOkAsync(string url)
    {
        try
        {
            using var handler = new HttpClientHandler { UseProxy = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
            using var resp = await http.GetAsync(url);
            Log.Info($"DSH HTTP {(int)resp.StatusCode} {url}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("DSH HTTP 探测失败: " + ex.Message);
            return false;
        }
    }

    private static async Task EnsureSurfCoreAsync(AppConfig cfg, string node, string dir, Action<string> status)
    {
        try
        {
            if (await PortOpenAsync(cfg.SurfCorePort)) return;
            if (string.IsNullOrEmpty(node) || !Directory.Exists(dir))
            {
                status("未找到 SurfCore 或 Node.js，跳过 bridge");
                return;
            }
            status("正在启动 SurfCore bridge…");
            StartNode(node, dir, "\"src\\bridge.js\"");
            if (!await WaitPortAsync(cfg.SurfCorePort, TimeSpan.FromSeconds(25)))
                status("SurfCore bridge 未在端口响应，继续尝试控制台");
        }
        catch (Exception ex)
        {
            Log.Warn("启动 SurfCore 失败: " + ex.Message);
            status("SurfCore 启动失败：" + ex.Message);
        }
    }

    private static async Task EnsureSnowLumaAsync(AppConfig cfg, string dir, string node, Action<string> status)
    {
        try
        {
            if (await PortOpenAsync(cfg.SnowLumaPort)) return;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir) || string.IsNullOrEmpty(node))
            {
                status("未找到 SnowLuma 目录或 Node.js");
                return;
            }
            status("正在启动 SnowLuma…");
            StartNode(node, dir, "\"index.mjs\"");
            if (!await WaitPortAsync(cfg.SnowLumaPort, TimeSpan.FromSeconds(30)))
                status("SnowLuma 启动超时，仍尝试打开控制台");
        }
        catch (Exception ex)
        {
            Log.Warn("启动 SnowLuma 失败: " + ex.Message);
            status("SnowLuma 启动失败：" + ex.Message);
        }
    }

    private static async Task<(string token, string message)> LoginAsync(int port, string password)
    {
        try
        {
            using var http = NewHttp();
            var body = JsonSerializer.Serialize(new { password });
            using var resp = await http.PostAsync($"http://127.0.0.1:{port}/api/login",
                new StringContent(body, Encoding.UTF8, "application/json"));
            var text = await resp.Content.ReadAsStringAsync();
            if (resp.StatusCode == HttpStatusCode.Unauthorized)
                return ("", "SnowLuma 控制台令牌错误");
            if ((int)resp.StatusCode == 429)
                return ("", "登录尝试过多，SnowLuma 已临时锁定，请稍后再试");
            if (!resp.IsSuccessStatusCode)
                return ("", $"登录失败（HTTP {(int)resp.StatusCode}）");
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.True &&
                root.TryGetProperty("token", out var tokenEl))
            {
                var token = tokenEl.GetString() ?? "";
                if (!string.IsNullOrEmpty(token)) return (token, "登录成功");
            }
            return ("", "SnowLuma 登录返回异常");
        }
        catch (Exception ex)
        {
            Log.Warn("SnowLuma 登录失败: " + ex.Message);
            return ("", "SnowLuma 未就绪或拒绝连接");
        }
    }

    private static async Task<(bool ok, string message, int pid)> InjectProcessAsync(int port, string token, string qq, bool oneBotReady)
    {
        try
        {
            using var http = NewHttp();
            http.DefaultRequestHeaders.TryAddWithoutValidation("x-snowluma-token", token);

            int pid = 0;
            var injected = false;
            var loggedIn = false;
            // 进程列表要等 SnowLuma 启动、QQ 登录之后才完整；最多等 60 秒。
            for (var attempt = 0; attempt < 60; attempt++)
            {
                var json = await http.GetStringAsync($"http://127.0.0.1:{port}/api/processes");
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array)
                    return (false, "SnowLuma 未返回进程列表", 0);

                foreach (var item in list.EnumerateArray())
                {
                    var uin = GetString(item, "uin");
                    var accountUin = "";
                    if (item.TryGetProperty("account", out var account) && account.ValueKind == JsonValueKind.Object)
                        accountUin = GetString(account, "uin");
                    if (uin != qq && accountUin != qq) continue;
                    pid = item.TryGetProperty("pid", out var p) && p.TryGetInt32(out var pv) ? pv : 0;
                    injected = item.TryGetProperty("injected", out var inj) && inj.ValueKind == JsonValueKind.True;
                    loggedIn = item.TryGetProperty("loggedIn", out var li) && li.ValueKind == JsonValueKind.True;
                    if (pid > 0) break;
                }

                if (pid > 0) break;
                await Task.Delay(1000);
            }

            if (pid == 0)
            {
                return oneBotReady
                    ? (false, $"未找到 QQ {qq} 的进程；请确认 QQ 已登录并允许注入", 0)
                    : (false, $"未找到 QQ {qq} 的进程；OneBot 未就绪，请设置 QQ 开机自启与自动登录", 0);
            }
            if (injected && loggedIn)
                return (true, $"QQ {qq} 进程已注入", pid);

            using var resp = await http.PostAsync($"http://127.0.0.1:{port}/api/processes/{pid}/load", new StringContent(""));
            var text = await resp.Content.ReadAsStringAsync();
            var success = resp.IsSuccessStatusCode;
            try
            {
                using var rd = JsonDocument.Parse(text);
                if (rd.RootElement.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.False) success = false;
            }
            catch { }
            return success
                ? (true, $"已注入 QQ {qq}（PID {pid}）", pid)
                : (false, $"注入 PID {pid} 失败", pid);
        }
        catch (Exception ex)
        {
            Log.Warn("注入进程失败: " + ex.Message);
            return (false, "注入进程失败：" + ex.Message, 0);
        }
    }

    private static string GetString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.ToString(),
            _ => ""
        };
    }

    private static HttpClient NewHttp() => new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromSeconds(6)
    };

    private static async Task<bool> PortOpenAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(600);
            await client.ConnectAsync(IPAddress.Loopback, port, cts.Token);
            return client.Connected;
        }
        catch { return false; }
    }

    private static async Task<bool> WaitPortAsync(int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await PortOpenAsync(port)) return true;
            await Task.Delay(500);
        }
        return false;
    }

    private static void StartNode(string node, string workdir, string args)
    {
        var psi = new ProcessStartInfo(node, args)
        {
            WorkingDirectory = workdir,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        Process.Start(psi)?.Dispose();
        Log.Info($"已启动 node {args} (cwd={workdir})");
    }

    private static async Task<string> RunNodeCaptureAsync(string node, string workdir, string args, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo(node, args)
            {
                WorkingDirectory = workdir,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using var p = Process.Start(psi);
            if (p == null) return "";
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { }
            }
            var text = (await stdout + await stderr).Trim();
            if (!string.IsNullOrEmpty(text)) Log.Info("node " + args + ": " + (text.Length > LogTailLimit ? text[^LogTailLimit..] : text));
            return text;
        }
        catch (Exception ex)
        {
            Log.Warn("运行 node 脚本失败: " + ex.Message);
            return "";
        }
    }

    private static string FindNode()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var part in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var p = Path.Combine(part.Trim(), "node.exe");
                if (File.Exists(p)) return p;
            }
            catch { }
        }
        return "";
    }
}
