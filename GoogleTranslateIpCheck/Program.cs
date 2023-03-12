using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Flurl.Http;

namespace GoogleTranslateIpCheck;

[JsonSerializable(typeof(Config))]
public partial class MyJsonContext : JsonSerializerContext { }

public static class Program
{
    private static bool AutoSet;
    private static bool IsIPv6;
    private static bool ScanMode;
    private static string IpPath = "ip.txt";
    private static Config? NetConfig = new( );
    private const string ConfigPath = "config.json";
    private const string HostApi = "translate.googleapis.com";
    private const string HostSite = "translate.google.com";

    public static async Task Main(string[] args)
    {
        Console.WriteLine("支持 IPv6 请优先使用 -6 启动"); PraseArgs(args);
        HashSet<string>? ips = null;
        if (ScanMode)
            ips = ScanIp(NetConfig);
        ips ??= await GetIpAsync(NetConfig);
        if (ips is null || ips?.Count == 0)
            ips = ScanIp(NetConfig);
        ConcurrentDictionary<string, long> ipTimes = new( );
        Console.WriteLine("\n开始检测响应时间\n");
        await Parallel.ForEachAsync(ips!, new ParallelOptions( )
        {
            MaxDegreeOfParallelism = NetConfig!.ScanSpeed
        }, async (ip, _) => await GetDelayAsync(ip, NetConfig, ipTimes));

        if (ipTimes.IsEmpty)
        {
            Console.WriteLine("无可用 IP, 删除 ip.txt 文件进入扫描模式");
            Console.ReadKey( );
            return;
        }
        Console.WriteLine("\n检测 IP 完毕, 按响应时间排序\n");
        var sortList = ipTimes.OrderByDescending(x => x.Value);
        foreach (var x in sortList)
            Console.WriteLine($"{x.Key} {x.Value} ms");
        string bestIp = sortList.Last( ).Key;
        Console.WriteLine($"最快 IP 为 {bestIp} {sortList.Last( ).Value} ms");
        SaveIp(IpPath, sortList);
        Console.WriteLine("设置 Host 文件需管理员权限 (macOS/Linux 使用 sudo 运行), 可能会被拦截, 建议手动复制以下文本");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Console.WriteLine(@"Host 文件路径为 C:\Windows\System32\drivers\etc\hosts (需去掉只读属性)");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            Console.WriteLine(@"Host 文件路径为 /etc/hosts ");
        Console.WriteLine($"\n{bestIp} {HostApi}");
        Console.WriteLine($"{bestIp} {HostSite}\n");
        if (!AutoSet)
        {
            Console.WriteLine("是否设置到 Host 文件 (Y/N)");
            if (Console.ReadKey( ).Key != ConsoleKey.Y) return;
        }
        Console.WriteLine( );
        try
        {
            SetHost(bestIp);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"设置失败: {ex.Message}");
            Console.ReadKey( );
            return;
        }
        Console.WriteLine("设置成功");
        FlushDns( );
        if (!AutoSet) Console.ReadKey( );
    }

    private static void PraseArgs(string[] args)
    {
        if (File.Exists(ConfigPath))
        {
            try
            {
                NetConfig = JsonSerializer.Deserialize(File.ReadAllText(ConfigPath), MyJsonContext.Default.Config);
            }
            catch
            {
                Console.WriteLine("读取配置出错, 将采用默认配置");
            }
        }
        if (args.Length > 0)
        {
            if (args.Any("-6".Equals))
            {
                IsIPv6 = true;
                IpPath = "ipv6.txt";
            }
            if (args.Any(x => "-y".Equals(x, StringComparison.OrdinalIgnoreCase)))
                AutoSet = true;
            if (args.Any(x => "-s".Equals(x, StringComparison.OrdinalIgnoreCase)))
                ScanMode = true;
        }
    }

    private static async Task GetDelayAsync(
        string ip,
        Config? config,
        ConcurrentDictionary<string, long> IpDelays)
    {
        Stopwatch watch = new( );
        long time = 3000L;
        bool flag = false;
        for (int i = 0; i < 5; i++)
        {
            try
            {
                watch.Start( );
                await ConnectIp(ip, config);
                watch.Stop( );
                if (watch.ElapsedMilliseconds < time)
                    time = watch.ElapsedMilliseconds;
                watch.Reset( );
                flag = false;
            }
            catch (Exception)
            {
                flag = true;
            }
        }
        if (flag)
        {
            Console.WriteLine($"{ip}\t\t超时");
            return;
        }
        IpDelays.TryAdd(ip, time);
        Console.WriteLine($"{ip}\t\t{time} ms");
    }

    private static HashSet<string>? ScanIp(Config? config)
    {
        Console.WriteLine("扫描已开始, 时间较长请耐心等候");
        HashSet<string> listIp = new( );
        CancellationTokenSource tokens = new( );
        tokens.Token.Register(( ) => Console.WriteLine($"找到 {config!.ScanLimit} 条 IP, 扫描结束"));
        string[] IpRange = !IsIPv6 ? config!.IpRange : config!.IPV6Range;
        foreach (string ipRange in IpRange)
        {
            if (string.IsNullOrWhiteSpace(ipRange)) continue;
            IPNetwork ipNetwork = IPNetwork.Parse(ipRange);
            ConcurrentBag<string> _ips = new( );
            try
            {
                Parallel.ForEach(
                    ipNetwork.ListIPAddress(FilterEnum.Usable),
                    new ParallelOptions( )
                    {
                        MaxDegreeOfParallelism = config!.ScanSpeed,
                        CancellationToken = tokens.Token
                    },
                    async (ip, ct) =>
                    {
                        try
                        {
                            bool result = await ConnectIp(ip.ToString( ), config);
                            if (!result) return;
                            Console.WriteLine($"{ip}");
                            _ips.Add(ip.ToString( ));
                            if (listIp.Count + _ips.Count >= config!.ScanLimit)
                                tokens.Cancel( );
                        }
                        catch { }
                    });
            }
            catch { continue; }
            finally
            {
                foreach (string ip in _ips)
                    listIp.Add(ip);
            }
        }
        Console.WriteLine($"扫描完成, 共 {listIp.Count} 条 IP");
        return listIp;
    }

    private static async Task<bool> ConnectIp(string ip, Config? config)
    {
        string str = !IsIPv6 ? $"{ip}" : $"[{ip}]";
        string url = $@"https://{str}/translate_a/single?client=gtx&sl=zh-CN&tl=en&dt=t&q=你好";
        return (await url
            .WithHeader("host", HostApi)
            .WithTimeout(config!.ScanTimeout)
            .GetStringAsync( )).Contains("Hello");
    }

    private static async Task<HashSet<string>?> GetIpAsync(Config? config)
    {
        string[]? lines;
        if (!File.Exists(IpPath))
        {
            Console.WriteLine("未能找到 IP 文件");
            Console.WriteLine("尝试从服务器获取 IP");
            lines = await GetRemoteIpAsync(config);
            if (lines is null) return null;
        }
        else
        {
            lines = File.ReadAllLines(IpPath);
            if (lines.Length < 1)
            {
                lines = await GetRemoteIpAsync(config);
                if (lines is null) return null;
            }
        }
        HashSet<string> listIp = new( );
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.Contains(','))
            {
                foreach (string? ip in from ip in line.Split(',') where CheckIp(ip) select ip)
                {
                    listIp.Add(ip.Trim( ));
                }
            }
            else if (CheckIp(line)) listIp.Add(line.Trim( ));
        }
        Console.WriteLine($"找到 {listIp.Count} 条IP");
        return listIp;
    }

    private static async Task<string[]?> GetRemoteIpAsync(Config? config)
    {
        try
        {
            string address = !IsIPv6 ? config!.RemoteIp : config!.RemoteIPv6;
            string text = await address.WithTimeout(10).GetStringAsync( );
            return text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        catch
        {
            Console.WriteLine("获取服务器 IP 失败");
            return null;
        }
    }

    private static void SetHost(string ip)
    {
        string hostFile;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            hostFile = "/etc/hosts";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            hostFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"drivers\etc\hosts");
        }
        else throw new NotSupportedException("暂不支持配置 Host 文件");

        File.SetAttributes(hostFile, FileAttributes.Normal);
        List<string> hosts = File.ReadAllLines(hostFile).ToList( );
        void Update(string host)
        {
            for (int i = 0; i < hosts!.Count; i++)
            {
                if (hosts[i].Contains(host))
                    hosts[i] = $"{ip} {host}";
            }
        }
        void Add(string host)
        {
            hosts.Add(Environment.NewLine);
            hosts.Add($"{ip} {host}");
        }
        if (hosts.Any(s => s.Contains(HostApi)))
            Update(HostApi);
        else
            Add(HostApi);
        if (hosts.Any(s => s.Contains(HostSite)))
            Update(HostSite);
        else
            Add(HostSite);
        File.WriteAllLines(hostFile, hosts);
    }

    private static void SaveIp(string ipPath, IOrderedEnumerable<KeyValuePair<string, long>> sortList)
    {
        File.WriteAllLines(ipPath, sortList.Reverse( ).Select(x => x.Key), Encoding.UTF8);
    }

    private static bool CheckIp(string ip)
    {
        if (IsIPv6)
            return IpRegex.IPv6Regex( ).IsMatch(ip.Trim( ));
        else
            return IpRegex.IPv4Regex( ).IsMatch(ip.Trim( ));
    }

    private static void FlushDns( )
    {
        StringBuilder output = new( );
        Dictionary<OSPlatform, Tuple<string, string>> dnsCmdLine = new( )
            {
                {OSPlatform.Windows, new ("ipconfig.exe","/flushdns") },
                {OSPlatform.OSX, new ("killall", "-HUP mDNSResponder") },
                {OSPlatform.Linux, new ("systemctl", "restart systemd-resolved") },
            };
        string fileName = "", arguments = "";
        foreach (var item in dnsCmdLine)
        {
            if (RuntimeInformation.IsOSPlatform(item.Key))
            {
                fileName = item.Value.Item1;
                arguments = item.Value.Item2;
            }
        }
        try
        {
            using Process process = new( )
            {
                StartInfo = new( )
                {
                    FileName = fileName,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.Start( );
            output.Append(process.StandardOutput.ReadToEnd( ));
            output.Append(process.StandardError.ReadToEnd( ));
            process.WaitForExit( );
            process.Close( );
            Console.WriteLine(output.ToString( ));
        }
        catch (Exception e) { Console.WriteLine(e); }
    }
}