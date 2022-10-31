using Flurl.Http;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

const string Host = "translate.googleapis.com";
const string RemoteIp = @"https://raw.githubusercontent.com/hcfyapp/google-translate-cn-ip/main/ips.txt";
const int IpLimit = 5;
const int Timeout = 4;
const int MaxDegreeOfParallelism = 80;
//const string HTTPVerifyHosts = "about.google";
var ipRanges =
    """"
	142.250.0.0/15
	172.217.0.0/16
	172.253.0.0/16
	108.177.0.0/17
	72.14.192.0/18
	74.125.0.0/16
	216.58.192.0/19
	"""".Split(Environment.NewLine);

HashSet<string>? ips = null;
if (args.Length > 0)
{
    if ("-s".Equals(args[0], StringComparison.OrdinalIgnoreCase))
    {
        ips = await ScanIpAsync();
    }
}
ips ??= await ReadIpAsync();
if (ips is null || ips?.Count == 0)
{
    ips = await ScanIpAsync();
    //Console.WriteLine("ip.txt 格式为每行一条IP");
    //Console.WriteLine("172.253.114.90");
    //Console.WriteLine("172.253.113.90");
    //Console.WriteLine("或者为 , 分割");
    //Console.WriteLine("172.253.114.90,172.253.113.90");
    //Console.ReadKey();
    //return;
}
Dictionary<string, long> times = new();
Console.WriteLine("开始检测IP响应时间");
foreach (var ip in ips!)
{
    await TestIpAsync(ip);
}
if (times.Count == 0)
{
    Console.WriteLine("未找到可用IP,可删除 ip.txt 文件直接进入扫描模式");
    return;
}
var bestIp = times.MinBy(x => x.Value).Key;
Console.WriteLine($"最佳IP为: {bestIp} 响应时间 {times.MinBy(x => x.Value).Value} ms");
await SaveIpFileAsync();
Console.WriteLine("设置Host文件需要管理员权限,可能会被安全软件拦截,建议手工复制以下文本到Host文件");
Console.WriteLine($"{bestIp} {Host}");
Console.WriteLine("是否设置到Host文件(Y:设置)");
if (Console.ReadKey().Key != ConsoleKey.Y)
    return;
Console.WriteLine();
try { await SetHostFileAsync(); }
catch (Exception ex) { Console.WriteLine($"设置失败:{ex.Message}"); }
Console.WriteLine("设置成功");
Console.ReadKey();

async Task TestIpAsync(string ip)
{
    try
    {
        var url = $@"http://{ip}/translate_a/single?client=gtx&sl=en&tl=fr&q=a";
        Stopwatch sw = new();
        var time = 3000L;
        for (int i = 0; i < 3; i++)
        {
            sw.Start();
            _ = await url
            .WithHeader("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9")
            .WithHeader("accept-encoding", "gzip, deflate, br")
            .WithHeader("accept-language", "zh-CN,zh;q=0.9")
            .WithHeader("sec-ch-ua", "\"Chromium\";v=\"106\", \"Not;A=Brand\";v=\"99\", \"Google Chrome\";v=\"106.0.5249.119\"")
            .WithHeader("host", "translate.googleapis.com")
            .WithHeader("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/106.0.0.0 Safari/537.36")
            .WithTimeout(3)
            .GetStringAsync();
            sw.Stop();
            if (sw.ElapsedMilliseconds < time)
                time = sw.ElapsedMilliseconds;
            //Console.WriteLine($"{ip}: 响应时间 {sw.ElapsedMilliseconds} ms");
            sw.Reset();
        }
        times.Add(ip, time);
        Console.WriteLine($"{ip}: 响应时间 {time} ms");
    }
    catch (Exception)
    {
        Console.WriteLine($"{ip}: 超时");
    }
}

async Task<HashSet<string>?> ScanIpAsync()
{
    Console.WriteLine("开始扫描可用IP,时间较长,请耐心等候");
    var listIp = new HashSet<string>();
    CancellationTokenSource cts = new();
    cts.Token.Register(() => { Console.WriteLine($"已经找到 {IpLimit} 条IP,结束扫描"); });
    foreach (var ipRange in ipRanges)
    {
        if (string.IsNullOrWhiteSpace(ipRange)) continue;
        IPNetwork ipnetwork = IPNetwork.Parse(ipRange);
        var _ips = new ConcurrentBag<string>();
        //Console.WriteLine(ipRange);
        try
        {
            await
                Parallel.ForEachAsync(
                ipnetwork.ListIPAddress(FilterEnum.Usable),
                new ParallelOptions()
                {
                    MaxDegreeOfParallelism = MaxDegreeOfParallelism,
                    CancellationToken = cts.Token
                },
                    async (ip, ct) =>
                    {
                        try
                        {
                            var result = await $"http://{ip}/translate_a/single?client=gtx&sl=en&tl=fr&q=a/"
                            .WithTimeout(Timeout)
                            .WithHeader("host", Host)
                            .GetStringAsync();
                            if (!result.Equals("[null,null,\"en\",null,null,null,null,[]]"))
                                return;
                            Console.WriteLine($"找到IP: {ip}");
                            _ips.Add(ip.ToString());
                            if (listIp.Count + _ips.Count >= IpLimit)
                                cts.Cancel();
                        }
                        catch { }
                    });
        }
        catch { break; }
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


async Task<HashSet<string>?> ReadIpAsync()
{
    string[]? lines;
    if (!File.Exists("ip.txt"))
    {
        Console.WriteLine("未能找到IP文件");
        Console.WriteLine("尝试从服务器获取IP");
        lines = await ReadRemoteIpAsync();
        if (lines is null)
            return null;
    }
    else
    {
        lines = File.ReadAllLines("ip.txt");
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
                if (RegexStuff.IpRegex().IsMatch(ip.Trim()))
                    listIp.Add(ip.Trim());
            }
        }
        else
        {
            if (RegexStuff.IpRegex().IsMatch(line.Trim()))
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
        var text = await RemoteIp.WithTimeout(10).GetStringAsync();
        return text.Trim().Split(new[] { Environment.NewLine },
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
    //string hostFile = string.Empty;
    //if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
    //    hostFile = Path.Combine(
    //        Environment.GetFolderPath(Environment.SpecialFolder.System),
    //        @"drivers\etc\hosts");
    //else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
    //    hostFile = "/etc/hosts";
    //else
    //    throw new Exception("暂不支持");
    var hostFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"drivers\etc\hosts");

    var ip = $"{bestIp} {Host}";
    File.SetAttributes(hostFile, FileAttributes.Normal);
    var lines = await File.ReadAllLinesAsync(hostFile);
    if (lines.Any(s => s.Contains(Host)))
    {
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(Host))
                lines[i] = ip;
        }
        await File.WriteAllLinesAsync(hostFile, lines);
    }
    else if (!lines.Contains(ip))
    {
        await File.AppendAllLinesAsync(hostFile, new[] { Environment.NewLine });
        await File.AppendAllLinesAsync(hostFile, new[] { ip });
    }
}

async Task SaveIpFileAsync()
{
    await File.WriteAllLinesAsync("ip.txt", ips, Encoding.UTF8);
}

public partial class RegexStuff
{
    [GeneratedRegex(@"^(([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])\.){3}([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])$")]
    public static partial Regex IpRegex();
}
