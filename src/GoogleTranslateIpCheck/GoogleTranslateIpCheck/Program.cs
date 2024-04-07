using Flurl.Http;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

const string configFile = "config.json";
Console.WriteLine("如果支持IPv6推荐优先使用,使用参数 -6 启动");
var config = new Config();
if (File.Exists(configFile))
{
    try
    {
        var json = File.ReadAllText(configFile);
        config = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.Config);
    }
    catch
    {
        Console.WriteLine("读取配置出错,将采用默认配置");
    }
}

var autoSet = false;
var isIPv6 = false;
var ipFile = "ip.txt";
HashSet<string>? ips = null;
if (args.Length > 0)
{
    if (args.Any(x => "-6".Equals(x)))
    {
        isIPv6 = true;
        ipFile = "IPv6.txt";
    }

    if (args.Any(x => "-y".Equals(x, StringComparison.OrdinalIgnoreCase)))
        autoSet = true;
    if (args.Any(x => "-s".Equals(x, StringComparison.OrdinalIgnoreCase)))
        ips = await ScanIpAsync();
}

ips ??= await ReadIpAsync();
if (ips is null || ips?.Count == 0)
    ips = await ScanIpAsync();
ConcurrentDictionary<string, long> times = new();
Console.WriteLine();
Console.WriteLine("开始检测IP响应时间");
Console.WriteLine();
await Parallel.ForEachAsync(ips!, new ParallelOptions()
{
    MaxDegreeOfParallelism = config!.扫描并发数
}, async (ip, _) =>
{
    {
        await TestIpAsync(ip);
    }
});

if (times.IsEmpty)
{
    Console.WriteLine("未找到可用IP,可删除 ip.txt 文件直接进入扫描模式");
    Console.ReadKey();
    return;
}

Console.WriteLine();
Console.WriteLine("检测IP完毕,按照响应时间排序结果");
Console.WriteLine();
var sortList = times.OrderByDescending(x => x.Value);
foreach (var x in sortList)
    Console.WriteLine($"{x.Key}: 响应时间 {x.Value} ms");
var bestIp = sortList.Last().Key;
Console.WriteLine($"最佳IP为: {bestIp} 响应时间 {sortList.Last().Value} ms");
await SaveIpFileAsync();
Console.WriteLine("设置Host文件需要管理员权限(Mac,Linux使用sudo运行),可能会被安全软件拦截,建议手工复制以下文本到Host文件");
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    Console.WriteLine(@"Host文件路径为 C:\Windows\System32\drivers\etc\hosts (需去掉只读属性)");
if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    Console.WriteLine(@"Host文件路径为 /etc/hosts ");
Console.WriteLine();
foreach (var host in config!.Hosts)
    Console.WriteLine($"{bestIp} {host}");
Console.WriteLine();
if (!autoSet)
{
    Console.WriteLine("是否设置到Host文件(Y:设置)");
    if (Console.ReadKey().Key != ConsoleKey.Y)
        return;
}

Console.WriteLine();
try
{
    await SetHostFileAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"设置失败:{ex.Message}");
    Console.ReadKey();
    return;
}

Console.WriteLine("设置成功");
await FlushDns();
if (!autoSet)
    Console.ReadKey();

async Task TestIpAsync(string ip)
{
    Stopwatch sw = new();
    var time = 3000L;
    var flag = false;
    for (int i = 0; i < 5; i++)
    {
        try
        {
            sw.Start();
            _ = await GetResultAsync(ip);
            sw.Stop();
            if (sw.ElapsedMilliseconds < time)
                time = sw.ElapsedMilliseconds;
            sw.Reset();
            flag = false;
        }
        catch (Exception)
        {
            flag = true;
        }
    }

    if (flag)
    {
        Console.WriteLine($"{ip}: 超时");
        return;
    }

    times.TryAdd(ip, time);
    Console.WriteLine($"{ip}: 响应时间 {time} ms");
}

async Task<HashSet<string>?> ScanIpAsync()
{
    Console.WriteLine("开始扫描可用IP,时间较长,请耐心等候");
    var listIp = new HashSet<string>();
    CancellationTokenSource cts = new();
    cts.Token.Register(() => { Console.WriteLine($"已经找到 {config!.IP扫描限制数量} 条IP,结束扫描"); });
    var IP段 = !isIPv6 ? config!.IP段 : config!.IPv6段;
    foreach (var ipRange in IP段)
    {
        if (string.IsNullOrWhiteSpace(ipRange)) continue;
        var _ips = new ConcurrentBag<string>();
        try
        {
            var ipnetwork = IPNetwork2.Parse(ipRange);
            await
                Parallel.ForEachAsync(
                    ipnetwork.ListIPAddress(FilterEnum.Usable),
                    new ParallelOptions()
                    {
                        MaxDegreeOfParallelism = config!.扫描并发数,
                        CancellationToken = cts.Token
                    },
                    async (ip, ct) =>
                    {
                        try
                        {
                            var result = await GetResultAsync(ip.ToString());
                            if (!result)
                                return;
                            Console.WriteLine($"{ip}");
                            _ips.Add(ip.ToString());
                            if (listIp.Count + _ips.Count >= config!.IP扫描限制数量)
                                cts.Cancel();
                        }
                        catch
                        {
                        }
                    });
        }
        catch
        {
            continue;
        }
        finally
        {
            foreach (var ip in _ips)
            {
                listIp.Add(ip);
            }
        }
    }

    Console.WriteLine($"扫描完成,找到 {listIp.Count} 条IP");
    return listIp;
}

async Task<bool> GetResultAsync(string ip)
{
    var str = !isIPv6 ? $"{ip}" : $"[{ip}]";
    var url = $@"https://{str}/translate_a/single?client=gtx&sl=zh-CN&tl=en&dt=t&q=你好";
    return (await url
        .WithHeader("host", config!.Hosts[0])
        .WithTimeout(config!.扫描超时)
        .GetStringAsync()).Contains("Hello");
}

async Task<HashSet<string>?> ReadIpAsync()
{
    string[]? lines;
    if (!File.Exists(ipFile))
    {
        Console.WriteLine("未能找到IP文件");
        Console.WriteLine("尝试从服务器获取IP");
        lines = await ReadRemoteIpAsync();
        if (lines is null)
            return null;
    }
    else
    {
        lines = File.ReadAllLines(ipFile);
        if (lines.Length < 1)
        {
            lines = await ReadRemoteIpAsync();
            if (lines is null)
                return null;
        }
    }

    var listIp = new HashSet<string>();
    foreach (var line in lines)
    {
        if (string.IsNullOrWhiteSpace(line))
            continue;
        if (line.Contains(','))
        {
            foreach (var ip in line.Split(','))
            {
                if (CheckIp(ip))
                    listIp.Add(ip.Trim());
            }
        }
        else
        {
            if (CheckIp(line))
                listIp.Add(line.Trim());
        }
    }

    Console.WriteLine($"找到 {listIp.Count} 条IP");
    return listIp;
}

async Task<string[]?> ReadRemoteIpAsync()
{
    try
    {
        var address = !isIPv6 ? config!.远程IP文件 : config!.远程IPv6文件;
        var text = await address.WithTimeout(10).GetStringAsync();
        return text.Split(new[] { '\n' },
            StringSplitOptions.RemoveEmptyEntries);
    }
    catch
    {
        Console.WriteLine("获取服务器IP失败");
        return null;
    }
}

async Task SetHostFileAsync()
{
    string hostFile;
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        hostFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"drivers\etc\hosts");
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
             || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        hostFile = "/etc/hosts";
    else
        throw new Exception("暂不支持配置HOST文件");

    File.SetAttributes(hostFile, FileAttributes.Normal);
    var lines = (await File.ReadAllLinesAsync(hostFile)).ToList();
    foreach (var host in config!.Hosts)
    {
        var hostLine = $"{bestIp} {host}";
        var existingLineIndex = lines.FindIndex(line => line.Contains(host));
        if (existingLineIndex >= 0)
        {
            lines[existingLineIndex] = hostLine;
        }
        else
        {
            lines.Add(hostLine);
        }
    }

    await File.WriteAllLinesAsync(hostFile, lines);
    return;
}

async Task SaveIpFileAsync()
{
    await File.WriteAllLinesAsync(ipFile, sortList.Reverse().Select(x => x.Key), Encoding.UTF8);
}

async Task FlushDns()
{
    var sb = new StringBuilder();
    string fileName;
    string arguments;
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        fileName = "ipconfig.exe";
        arguments = "/flushdns";
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    {
        fileName = "killall";
        arguments = "-HUP mDNSResponder";
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
        fileName = "systemctl";
        arguments = "restart systemd-resolved";
    }
    else
    {
        return;
    }

    try
    {
        using var process = new Process()
        {
            StartInfo = new ProcessStartInfo()
            {
                FileName = fileName,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.Start();
        sb.Append(await process.StandardOutput.ReadToEndAsync());
        sb.Append(await process.StandardError.ReadToEndAsync());
        await process.WaitForExitAsync();
        process.Close();
        Console.WriteLine(sb.ToString());
    }
    catch (Exception e)
    {
        Console.WriteLine(e);
    }
}

bool CheckIp(string ip)
{
    if (!isIPv6)
        return RegexStuff.IpRegex().IsMatch(ip.Trim());
    else
        return RegexStuff.IPv6Regex().IsMatch(ip.Trim());
}

public partial class RegexStuff
{
    [GeneratedRegex(@"^(([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])\.){3}([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])$")]
    public static partial Regex IpRegex();

    [GeneratedRegex(@"^(([0-9a-fA-F]{1,4}:){7,7}[0-9a-fA-F]{1,4}|([0-9a-fA-F]{1,4}:){1,7}:|([0-9a-fA-F]{1,4}:){1,6}:[0-9a-fA-F]{1,4}|([0-9a-fA-F]{1,4}:){1,5}(:[0-9a-fA-F]{1,4}){1,2}|([0-9a-fA-F]{1,4}:){1,4}(:[0-9a-fA-F]{1,4}){1,3}|([0-9a-fA-F]{1,4}:){1,3}(:[0-9a-fA-F]{1,4}){1,4}|([0-9a-fA-F]{1,4}:){1,2}(:[0-9a-fA-F]{1,4}){1,5}|[0-9a-fA-F]{1,4}:((:[0-9a-fA-F]{1,4}){1,6})|:((:[0-9a-fA-F]{1,4}){1,7}|:))$")]
    public static partial Regex IPv6Regex();
}

public class Config
{
    public string 远程IP文件 { get; set; } = "https://mirror.ghproxy.com/https://raw.githubusercontent.com/Ponderfly/GoogleTranslateIpCheck/master/src/GoogleTranslateIpCheck/GoogleTranslateIpCheck/ip.txt";
    public string 远程IPv6文件 { get; set; } = "https://mirror.ghproxy.com/https://raw.githubusercontent.com/Ponderfly/GoogleTranslateIpCheck/master/src/GoogleTranslateIpCheck/GoogleTranslateIpCheck/IPv6.txt";
    public int IP扫描限制数量 { get; set; } = 5;
    public int 扫描超时 { get; set; } = 4;
    public int 扫描并发数 { get; set; } = 80;
    public string[] Hosts { get; set; } =
        """
            translate.googleapis.com
            translate.google.com
            translate-pa.googleapis.com
            """.Split(Environment.NewLine);
    public string[] IP段 { get; set; } =
        """"
            142.250.0.0/15
            172.217.0.0/16
            172.253.0.0/16
            108.177.0.0/17
            72.14.192.0/18
            74.125.0.0/16
            216.58.192.0/19
            """".Split(Environment.NewLine);
    public string[] IPv6段 { get; set; } =
        """"
            2404:6800:4008:c15::0/112
            2a00:1450:4001:802::0/112
            2a00:1450:4001:803::0/112
            2a00:1450:4001:809::0/112
            2a00:1450:4001:811::0/112
            2a00:1450:4001:827::0/112
            2a00:1450:4001:828::0/112
            """".Split(Environment.NewLine);
}

[JsonSerializable(typeof(Config))]
internal partial class ConfigJsonContext : JsonSerializerContext
{
}