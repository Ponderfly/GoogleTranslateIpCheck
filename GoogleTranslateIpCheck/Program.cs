using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Flurl.Http;

namespace GoogleTranslateIpCheck;

internal class Program
{
    private static async Task Main(string[] args)
    {
        const string configPath = "config.json";
        string ipPath = "ip.txt";
        const string HostApi = "translate.googleapis.com";
        const string HostSite = "translate.google.com";

        Console.WriteLine("支持 IPv6 请优先使用, 使用 -6 启动");
        var config = new Config( );
        if (File.Exists(configPath))
        {
            try
            {
                config = JsonSerializer.Deserialize(File.ReadAllText(configPath), MyJsonContext.Default.Config);
            }
            catch
            {
                Console.WriteLine("读取配置出错, 将采用默认配置");
            }
        }

        var autoSet = false;
        var isIPv6 = false;
        HashSet<string>? ips = null;
        if (args.Length > 0)
        {
            if (args.Any("-6".Equals))
            {
                isIPv6 = true;
                ipPath = "ipv6.txt";
            }
            if (args.Any(x => "-y".Equals(x, StringComparison.OrdinalIgnoreCase)))
                autoSet = true;
            if (args.Any(x => "-s".Equals(x, StringComparison.OrdinalIgnoreCase)))
                ips = await ScanIpAsync( );
        }
        ips ??= await ReadIpAsync( );
        if (ips is null || ips?.Count == 0)
            ips = await ScanIpAsync( );
        ConcurrentDictionary<string, long> ipTimes = new( );
        Console.WriteLine("\n开始检测响应时间\n");
        await Parallel.ForEachAsync(ips!, new ParallelOptions( )
        {
            MaxDegreeOfParallelism = config!.ScanSpeed
        }, async (ip, _) => await TestIpAsync(ip));

        if (ipTimes.IsEmpty)
        {
            Console.WriteLine("无可用IP, 删除 ip.txt 文件进入扫描模式");
            Console.ReadKey( );
            return;
        }
        Console.WriteLine("\n检测IP完毕, 按响应时间排序\n");
        var sortList = ipTimes.OrderByDescending(x => x.Value);
        foreach (var x in sortList)
            Console.WriteLine($"{x.Key} {x.Value} ms");
        var bestIp = sortList.Last( ).Key;
        Console.WriteLine($"最快 IP 为 {bestIp} {sortList.Last( ).Value} ms");
        await SaveIpFileAsync( );
        Console.WriteLine("设置 Host 文件需管理员权限 (macOS/Linux 使用 sudo 运行), 可能会被拦截, 建议手动复制以下文本");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Console.WriteLine(@"Host 文件路径为 C:\Windows\System32\drivers\etc\hosts (需去掉只读属性)");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            Console.WriteLine(@"Host 文件路径为 /etc/hosts ");
        Console.WriteLine($"\n{bestIp} {HostApi}");
        Console.WriteLine($"{bestIp} {HostSite}\n");
        if (!autoSet)
        {
            Console.WriteLine("是否设置到 Host 文件 (Y/N)");
            if (Console.ReadKey( ).Key != ConsoleKey.Y) return;
        }
        Console.WriteLine( );
        try
        {
            await SetHostFileAsync( );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"设置失败: {ex.Message}");
            Console.ReadKey( );
            return;
        }
        Console.WriteLine("设置成功");
        await FlushDns( );
        if (!autoSet) Console.ReadKey( );

        async Task TestIpAsync(string ip)
        {
            Stopwatch sw = new( );
            var time = 3000L;
            var flag = false;
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    sw.Start( );
                    await GetResultAsync(ip);
                    sw.Stop( );
                    if (sw.ElapsedMilliseconds < time)
                        time = sw.ElapsedMilliseconds;
                    sw.Reset( );
                    flag = false;
                }
                catch (Exception)
                {
                    flag = true;
                }
            }
            if (flag)
            {
                Console.WriteLine($"{ip}\t超时");
                return;
            }
            ipTimes.TryAdd(ip, time);
            Console.WriteLine($"{ip}\t{time} ms");
        }

        async Task<HashSet<string>?> ScanIpAsync( )
        {
            Console.WriteLine("扫描已开始, 时间较长请耐心等候");
            var listIp = new HashSet<string>( );
            CancellationTokenSource tokens = new( );
            tokens.Token.Register(( ) => Console.WriteLine($"找到 {config!.ScanLimit} 条 IP, 扫描结束"));
            var IpRange = !isIPv6 ? config!.IpRange : config!.IPV6Range;
            foreach (var ipRange in IpRange)
            {
                if (string.IsNullOrWhiteSpace(ipRange)) continue;
                IPNetwork ipnetwork = IPNetwork.Parse(ipRange);
                var _ips = new ConcurrentBag<string>( );
                try
                {
                    await
                        Parallel.ForEachAsync(
                        ipnetwork.ListIPAddress(FilterEnum.Usable),
                        new ParallelOptions( )
                        {
                            MaxDegreeOfParallelism = config!.ScanSpeed,
                            CancellationToken = tokens.Token
                        },
                        async (ip, ct) =>
                            {
                                try
                                {
                                    var result = await GetResultAsync(ip.ToString( ));
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
                    foreach (var ip in _ips)
                        listIp.Add(ip);
                }
            }
            Console.WriteLine($"扫描完成, 共 {listIp.Count} 条 IP");
            return listIp;
        }

        async Task<bool> GetResultAsync(string ip)
        {
            var str = !isIPv6 ? $"{ip}" : $"[{ip}]";
            var url = $@"https://{str}/translate_a/single?client=gtx&sl=zh-CN&tl=en&dt=t&q=你好";
            return (await url
                .WithHeader("host", HostApi)
                .WithTimeout(config!.ScanTimeout)
                .GetStringAsync( )).Contains("Hello");
        }

        async Task<HashSet<string>?> ReadIpAsync( )
        {
            string[]? lines;
            if (!File.Exists(ipPath))
            {
                Console.WriteLine("未能找到 IP 文件");
                Console.WriteLine("尝试从服务器获取 IP");
                lines = await ReadRemoteIpAsync( );
                if (lines is null) return null;
            }
            else
            {
                lines = File.ReadAllLines(ipPath);
                if (lines.Length < 1)
                {
                    lines = await ReadRemoteIpAsync( );
                    if (lines is null) return null;
                }
            }
            var listIp = new HashSet<string>( );
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.Contains(','))
                {
                    foreach (var ip in line.Split(','))
                        if (CheckIp(ip)) listIp.Add(ip.Trim( ));
                }
                else if (CheckIp(line)) listIp.Add(line.Trim( ));
            }
            Console.WriteLine($"找到 {listIp.Count} 条IP");
            return listIp;
        }

        async Task<string[]?> ReadRemoteIpAsync( )
        {
            try
            {
                var address = !isIPv6 ? config!.RemoteIp : config!.RemoteIPv6;
                var text = await address.WithTimeout(10).GetStringAsync( );
                return text.Split(new[] { '\n' },
                    StringSplitOptions.RemoveEmptyEntries);
            }
            catch
            {
                Console.WriteLine("获取服务器 IP 失败");
                return null;
            }
        }

        async Task SetHostFileAsync( )
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
                throw new NotSupportedException("暂不支持配置 Host 文件");

            var ip = $"{bestIp} {HostApi}";
            var ip2 = $"{bestIp} {HostSite}";
            File.SetAttributes(hostFile, FileAttributes.Normal);
            var lines = (await File.ReadAllLinesAsync(hostFile)).ToList( );
            void Update(string host)
            {
                for (var i = 0; i < lines!.Count; i++)
                {
                    if (lines[i].Contains(host))
                        lines[i] = $"{bestIp} {host}";
                }
            }
            void Add(string s)
            {
                lines.Add(Environment.NewLine);
                lines.Add(s);
            }
            if (lines.Any(s => s.Contains(HostApi)))
                Update(HostApi);
            else Add(ip);
            if (lines.Any(s => s.Contains(HostSite)))
                Update(HostSite);
            else Add(ip2);
            await File.WriteAllLinesAsync(hostFile, lines);
        }

        async Task SaveIpFileAsync( )
            => await File.WriteAllLinesAsync(ipPath, sortList.Reverse( ).Select(x => x.Key), Encoding.UTF8);

        async Task FlushDns( )
        {
            var sb = new StringBuilder( );
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
            else return;
            try
            {
                using var process = new Process( )
                {
                    StartInfo = new ProcessStartInfo( )
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
                sb.Append(await process.StandardOutput.ReadToEndAsync( ));
                sb.Append(await process.StandardError.ReadToEndAsync( ));
                await process.WaitForExitAsync( );
                process.Close( );
                Console.WriteLine(sb.ToString( ));
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        bool CheckIp(string ip)
        {
            if (!isIPv6) return RegexStuff.IpRegex( ).IsMatch(ip.Trim( ));
            else return RegexStuff.IPv6Regex( ).IsMatch(ip.Trim( ));
        }
    }
}

public partial class RegexStuff
{
    [GeneratedRegex(@"^(([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])\.){3}([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])$")]
    public static partial Regex IpRegex( );
    [GeneratedRegex(@"^(([0-9a-fA-F]{1,4}:){7,7}[0-9a-fA-F]{1,4}|([0-9a-fA-F]{1,4}:){1,7}:|([0-9a-fA-F]{1,4}:){1,6}:[0-9a-fA-F]{1,4}|([0-9a-fA-F]{1,4}:){1,5}(:[0-9a-fA-F]{1,4}){1,2}|([0-9a-fA-F]{1,4}:){1,4}(:[0-9a-fA-F]{1,4}){1,3}|([0-9a-fA-F]{1,4}:){1,3}(:[0-9a-fA-F]{1,4}){1,4}|([0-9a-fA-F]{1,4}:){1,2}(:[0-9a-fA-F]{1,4}){1,5}|[0-9a-fA-F]{1,4}:((:[0-9a-fA-F]{1,4}){1,6})|:((:[0-9a-fA-F]{1,4}){1,7}|:))$")]
    public static partial Regex IPv6Regex( );
}

public class Config
{
    public string RemoteIp { get; set; } = "https://ghproxy.com/https://raw.githubusercontent.com/Ponderfly/GoogleTranslateIpCheck/master/GoogleTranslateIpCheck/ip.txt";
    public string RemoteIPv6 { get; set; } = "https://ghproxy.com/https://raw.githubusercontent.com/Ponderfly/GoogleTranslateIpCheck/master/GoogleTranslateIpCheck/ipv6.txt";
    public int ScanLimit { get; set; } = 5;
    public int ScanTimeout { get; set; } = 4;
    public int ScanSpeed { get; set; } = 80;
    public string[] IpRange { get; set; } =
    """"
    142.250.0.0/15
    172.217.0.0/16
    172.253.0.0/16
    108.177.0.0/17
    72.14.192.0/18
    74.125.0.0/16
    216.58.192.0/19
    """".Split(Environment.NewLine);
    public string[] IPV6Range { get; set; } =
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
internal partial class MyJsonContext : JsonSerializerContext { }