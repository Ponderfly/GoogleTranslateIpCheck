using Flurl.Http;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Runtime.Versioning;
using System.ComponentModel;
using System.Security.Principal;

if (!OperatingSystem.IsWindows()) return;
if (!IsRunningAsAdministrator())
{
    Console.WriteLine("请以管理员身份运行此程序。");
    Console.ReadKey();
    return;
}
const string configFile = "config.json";
const string version = "1.10";
PrintHelp();
Console.WriteLine("如果支持IPv6推荐优先使用,使用参数 -6 启动");
var config = new Config();
if (File.Exists(configFile))
{
    try
    {
        var json = File.ReadAllText(configFile);
        var options = new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        var context = new ConfigJsonContext(options);
        config = JsonSerializer.Deserialize(json, context.Config);
    }
    catch
    {
        Console.WriteLine("读取配置出错,将采用默认配置");
    }
}
else
{
    var options = new JsonSerializerOptions { WriteIndented = true };
    var context = new ConfigJsonContext(options);
    var json = JsonSerializer.Serialize(config, context.Config);
    File.WriteAllText(configFile, json);
}
var ctsTest = new CancellationTokenSource();
var ctsScan = new CancellationTokenSource();
bool isScan = false, scanRunning = false, testRunning = false;
var autoSet = false;
var isIPv6 = false;
var isIntervalMode = false;
var readRemoteIp = true;
var ipFile = "ip.txt";
HashSet<string>? ips = null;
ConcurrentDictionary<string, long> times = new();

if (args.Length > 0)
{
    if (args.Any(x => "-6".Equals(x)))
    {
        isIPv6 = true;
        ipFile = "IPv6.txt";
    }
    if (args.Any(x => "-v".Equals(x, StringComparison.OrdinalIgnoreCase)))
    {
        Console.WriteLine($"版本号: {version} TurboSyn");
        return;
    }
    if (args.Any(x => "-y".Equals(x, StringComparison.OrdinalIgnoreCase)))
        autoSet = true;
    if (args.Any(x => "-s".Equals(x, StringComparison.OrdinalIgnoreCase)))
    {
        isScan = true;
    }
    if (args.Any(x => "-t".Equals(x, StringComparison.OrdinalIgnoreCase)))
    {
        isIntervalMode = true;
        autoSet = true;
        isScan = true;
        Console.WriteLine("自动扫描模式启动");
    }
    if (args.Any(x => "-h".Equals(x, StringComparison.OrdinalIgnoreCase)) || args.Any(x => "-?".Equals(x)))
    {
        PrintHelp();
        Environment.Exit(0);
    }
}

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    if (isIntervalMode)
    {
        Environment.Exit(0);
    }
    else if (scanRunning)
    {
        ctsScan.Cancel();
        Console.WriteLine("扫描操作已取消!");
    }
    else if(testRunning)
    {
        ctsTest.Cancel();
        Console.WriteLine("检测操作已取消!");
    }
    else
    {
        Environment.Exit(0);
    }
};

if (isScan)
    ips = await ScanIpAsync();
Start:
if(ctsScan.IsCancellationRequested || ctsTest.IsCancellationRequested)
{
    if (times.IsEmpty)
        Environment.Exit(0);
    else
        goto end;

}
    
ips ??= await ReadIpAsync();
if (ips is null || ips?.Count == 0)
        ips = await ScanIpAsync();

Console.WriteLine();
Console.WriteLine("开始检测IP响应时间");
Console.WriteLine();
testRunning = true;
try
{
    await Parallel.ForEachAsync(ips!, new ParallelOptions()
    {
        MaxDegreeOfParallelism = config!.扫描并发数,
        CancellationToken =  ctsTest.Token
    }, async (ip, ct) =>
    {

        await TestIpAsync(ip);
    });
}
catch
{
    // ignored
}
testRunning = false;
if (times.IsEmpty)
{
    Console.WriteLine("未找到可用IP,进入扫描模式");
    ips = null;
    await File.WriteAllLinesAsync(ipFile, [], Encoding.UTF8);
    goto Start;
}
end:
Console.WriteLine();
Console.WriteLine("检测IP完毕,按照响应时间排序结果");
Console.WriteLine();
var sortList = times.OrderByDescending(x => x.Value).ToList();
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
else if(isIntervalMode)
{
    Console.WriteLine("扫描等待中...");
    await Task.Delay(TimeSpan.FromMinutes(config.间隔扫描时间));
    goto Start;
}
return;

async Task TestIpAsync(string ip)
{
    Stopwatch sw = new();
    var time = (long)TimeSpan.FromSeconds(config.扫描超时).TotalMilliseconds;
    var flag = false;
    for (var i = 0; i < 3; i++)
    {
        try
        {
            if(ctsTest.IsCancellationRequested)
                return;
            sw.Start();
            _ = await GetResultAsync(ip,ctsTest);
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
        Console.Title = $"{ip}: 超时";
        return;
    }

    times.TryAdd(ip, time);
    Console.WriteLine($"{ip}: 响应时间 {time} ms");
    if (times.Count >= config!.IP扫描限制数量)
        ctsTest.Cancel();
}

async Task<HashSet<string>?> ScanIpAsync()
{
    scanRunning = true;
    Console.WriteLine("开始扫描可用IP,时间较长,请耐心等候");
    var listIp = new HashSet<string>();
    var IP段 = !isIPv6 ? config!.IP段 : config!.IPv6段;
    foreach (var ipRange in IP段)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ipRange)) continue;
            var results = TurboSyn.ScanAsync(ipRange, 443, progress =>
            {
                Console.Title = progress.ToString();
            }, ctsScan.Token).WithCancellation(ctsScan.Token);

            await foreach (var address in results)
            {
                Console.WriteLine(address);
                listIp.Add(address.ToString());
            }
        }
        catch
        {
            // ignored
        }
    }
    Console.WriteLine($"快速扫描完成,找到 {listIp.Count} 条IP");
    Console.Title = $"快速扫描完成,找到 {listIp.Count} 条IP,开始检测IP响应时间";
    scanRunning = false;
    return listIp;
}

async Task<bool> GetResultAsync(string ip,CancellationTokenSource cts)
{
    var str = !isIPv6 ? $"{ip}" : $"[{ip}]";
    var url = $@"https://{str}/translate_a/single?client=gtx&sl=zh-CN&tl=en&dt=t&q=你好";
    return (await url
        .WithHeader("host", config!.Hosts[0])
        .WithTimeout(config!.扫描超时)
        .GetStringAsync(cancellationToken : cts.Token)).Contains("Hello");
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
        if (lines.Length < 1 && readRemoteIp)
        {
            lines = await ReadRemoteIpAsync();
            if (lines is null)
            {
                return null;
            }
            readRemoteIp = false;
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
        return text.Split(['\n'],
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
    if (OperatingSystem.IsWindows())
        hostFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"drivers\etc\hosts");
    else if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
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
}

async Task SaveIpFileAsync()
{
    sortList.Reverse();
    await File.WriteAllLinesAsync(ipFile, sortList.Select(x => x.Key), Encoding.UTF8);
}

async Task FlushDns()
{
    var sb = new StringBuilder();
    string fileName;
    string arguments;
    if (OperatingSystem.IsWindows())
    {
        fileName = "ipconfig.exe";
        arguments = "/flushdns";
    }
    else if (OperatingSystem.IsMacOS())
    {
        fileName = "killall";
        arguments = "-HUP mDNSResponder";
    }
    else if (OperatingSystem.IsLinux())
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
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo()
        {
            FileName = fileName,
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
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
    return !isIPv6 ? RegexStuff.IpRegex().IsMatch(ip.Trim()) : RegexStuff.IPv6Regex().IsMatch(ip.Trim());
}

bool IsRunningAsAdministrator()
{
    using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
    {
        WindowsPrincipal principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}

void PrintHelp()
{
    Console.WriteLine("用法:");
    Console.WriteLine("  -6       使用IPv6地址");
    Console.WriteLine("  -v       显示版本信息");
    Console.WriteLine("  -y       自动设置Host文件");
    Console.WriteLine("  -s       扫描IP");
    Console.WriteLine("  -t       自动扫描模式");
    Console.WriteLine("  -h, -?   显示帮助信息");
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
    public int IP扫描限制数量 { get; set; } = 5; // 扫描到多少个可用IP后停止
    public int 扫描超时 { get; set; } = 4; // 秒
    public int 扫描并发数 { get; set; } = 500;
    public int 间隔扫描时间 { get; set; } = 10; // 分钟

    public string[] Hosts { get; set; } =
        """
            translate.googleapis.com
            translate.google.com
            translate-pa.googleapis.com
            "jnn-pa.googleapis.com"
            """.Split(Environment.NewLine);
    public string[] IP段 { get; set; } =
        """
            142.250.0.0/15
            172.217.0.0/16
            172.253.0.0/16
            108.177.0.0/17
            72.14.192.0/18
            74.125.0.0/16
            216.58.192.0/19
            """.Split(Environment.NewLine);
    public string[] IPv6段 { get; set; } =
        """
            2404:6800:4008:c15::0/112
            2a00:1450:4001:802::0/112
            2a00:1450:4001:803::0/112
            2a00:1450:4001:809::0/112
            2a00:1450:4001:811::0/112
            2a00:1450:4001:827::0/112
            2a00:1450:4001:828::0/112
            """.Split(Environment.NewLine);
}

[JsonSerializable(typeof(Config))]
internal partial class ConfigJsonContext : JsonSerializerContext
{
}

public static partial class TurboSyn
{
    /// <summary>
    /// 扫描进度
    /// </summary>
    /// <param name="CurrentCount">当前数量</param>
    /// <param name="TotalCount">总数量</param>
    /// <param name="IPAddress">当前扫描的IP地址</param>
    public record struct ScanProgress(ulong CurrentCount, ulong TotalCount, IPAddress IPAddress);

    private record UserParam(ChannelWriter<IPAddress> ChannelWriter, Action<ScanProgress>? ProgressChanged);

    /// <summary>
    /// 异步扫描
    /// </summary>
    /// <param name="content">CIDR或IP内容，一行一条记录</param>
    /// <param name="port">扫描的TCP端口</param>
    /// <param name="progressChanged">进度变化委托</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns></returns>
    /// <exception cref="Win32Exception"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    [SupportedOSPlatform("windows")]
    public static async IAsyncEnumerable<IPAddress> ScanAsync(
        string? content,
        int port,
        Action<ScanProgress>? progressChanged = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(port, 0);

        var hContent = Marshal.StringToHGlobalAnsi(content);
        var hScanner = TurboSynCreateScanner(hContent);
        if (hScanner <= nint.Zero)
        {
            Marshal.FreeHGlobal(hContent);
            throw new Win32Exception();
        }

        var channel = Channel.CreateUnbounded<IPAddress>();
        var userParam = new UserParam(channel.Writer, progressChanged);
        var userParamGCHandle = GCHandle.Alloc(userParam);

        try
        {
            var results = ScanAsync(hScanner, port, channel.Reader, userParamGCHandle, cancellationToken);
            await foreach (var address in results)
            {
                yield return address;
            }
        }
        finally
        {
            userParamGCHandle.Free();
            Marshal.FreeHGlobal(hContent);
            TurboSynFreeScanner(hScanner);
        }
    }

    private static async IAsyncEnumerable<IPAddress> ScanAsync(
        nint hScanner,
        int port,
        ChannelReader<IPAddress> channelReader,
        GCHandle userParamGCHandle,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using (cancellationToken.Register(() => TurboSynCancelScan(hScanner)))
        {
            UnsafeTurboSynStartScan();
            await foreach (var address in channelReader.ReadAllAsync(cancellationToken))
            {
                yield return address;
            }
        }

        unsafe void UnsafeTurboSynStartScan()
        {
            var userParam = GCHandle.ToIntPtr(userParamGCHandle);
            TurboSynStartScan(hScanner, port, &OnResult, &OnProgress, userParam);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnResult(TurboSynScanResult result, nint param)
    {
        var userParamGCHandle = GCHandle.FromIntPtr(param);
        if (userParamGCHandle.Target is UserParam userParam)
        {
            if (result.State == TurboSynScanState.Success)
            {
                var address = new IPAddress(result.IPAddressByteSpan);
                userParam.ChannelWriter.TryWrite(address);
            }
            else if (result.State == TurboSynScanState.Cancelled)
            {
                userParam.ChannelWriter.Complete(new OperationCanceledException());
            }
            else
            {
                userParam.ChannelWriter.Complete();
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnProgress(TurboSynScanProgress progress, nint param)
    {
        var userParamGCHandle = GCHandle.FromIntPtr(param);
        if (userParamGCHandle.Target is UserParam userParam && userParam.ProgressChanged != null)
        {
            var address = new IPAddress(progress.IPAddressByteSpan);
            var scanProgress = new ScanProgress(progress.CurrentCount, progress.TotalCount, address);
            userParam.ProgressChanged(scanProgress);
        }
    }
}
        
partial class TurboSyn
{
    private enum TurboSynScanState
    {
        Success = 0,
        Cancelled = 1,
        Completed = 2,
    }

    private struct TurboSynScanResult
    {
        public TurboSynScanState State;
        public int Port;
        public int IPLength;
        public Byte16 IPAddress;
        public ReadOnlySpan<byte> IPAddressByteSpan => MemoryMarshal.CreateReadOnlySpan(ref IPAddress.Element0, IPLength);
    }

    private struct TurboSynScanProgress
    {
        public ulong CurrentCount;
        public ulong TotalCount;
        public int IPLength;
        public Byte16 IPAddress;
        public ReadOnlySpan<byte> IPAddressByteSpan => MemoryMarshal.CreateReadOnlySpan(ref IPAddress.Element0, IPLength);
    }

    [InlineArray(16)]
    private struct Byte16
    {
        public byte Element0;
    }


    private const string TurboSynLib = "TurboSyn.dll";

    [LibraryImport(TurboSynLib, SetLastError = true)]
    private static partial nint TurboSynCreateScanner(
        nint content);

    [LibraryImport(TurboSynLib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool TurboSynStartScan(
        nint scanner,
        int port,
        delegate* unmanaged[Cdecl]<TurboSynScanResult, nint, void> resultCallback,
        delegate* unmanaged[Cdecl]<TurboSynScanProgress, nint, void> progressCallback,
        nint userParam);

    [LibraryImport(TurboSynLib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TurboSynCancelScan(
        nint scanner);

    [LibraryImport(TurboSynLib, SetLastError = true)]
    private static partial void TurboSynFreeScanner(
        nint scanner);
}

