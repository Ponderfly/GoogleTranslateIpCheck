using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using GoogleTranslateIpCheck.Properties;

namespace GoogleTranslateIpCheck;

public static partial class Program
{
    private static bool AutoSet;
    private static bool IsIPv6;
    private static bool ScanMode;
    private static Config? MainConfig = new( );
    private static string IPFile = "";
    private const string ConfigPath = "config.json";
    private const string HostApi = "translate.googleapis.com";
    private const string HostSite = "translate.google.com";

    public static async Task Main(string[] args)
    {
        Console.WriteLine(Texts.SupportIPv6);
        PraseArgs(args);
        HashSet<string>? ips = null;
        if (ScanMode) ips = ScanIp(MainConfig);
        ips ??= await GetIP(MainConfig);
        if (ips is null || ips?.Count == 0)
            ips = ScanIp(MainConfig);
        ConcurrentDictionary<string, long> ipTimes = new( );
        Console.WriteLine(Texts.StartScan);
        await Parallel.ForEachAsync(ips!, new ParallelOptions( )
        {
            MaxDegreeOfParallelism = MainConfig!.ScanSpeed
        }, async (ip, _) => await GetDelayAsync(ip, MainConfig, ipTimes));
        if (ipTimes.IsEmpty)
        {
            Console.WriteLine(Texts.NoIPFile);
            Console.ReadKey( );
            return;
        }
        Console.WriteLine(Texts.ScanComplete);
        var sortList = ipTimes.OrderByDescending(x => x.Value);
        foreach (var x in sortList)
            Console.WriteLine($"{x.Key} {x.Value} ms");
        string bestIp = sortList.Last( ).Key;
        Console.WriteLine($"最快 IP 为 {bestIp} {sortList.Last( ).Value} ms");
        SaveIP(IPFile, sortList);
        Console.WriteLine(Texts.SetHostTip);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Console.WriteLine(Texts.WinHostPath);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            Console.WriteLine(Texts.UnixHostPath);
        Console.WriteLine($"\n{bestIp} {HostApi}");
        Console.WriteLine($"{bestIp} {HostSite}\n");
        if (!AutoSet)
        {
            Console.WriteLine(Texts.SetHostConfirm);
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
        Console.WriteLine(Texts.SetSuccess);
        FlushDNS( );
        if (!AutoSet) Console.ReadKey( );
    }

    private static void PraseArgs(string[] args)
    {
        if (File.Exists(ConfigPath))
        {
            try
            {
                MainConfig = JsonSerializer.Deserialize(File.ReadAllText(ConfigPath), _JSONContext.Default.Config);
            }
            catch (IOException)
            {
                Console.WriteLine(Texts.ReadConfigError);
            }
        }
        if (args.Length > 0)
        {
            IsIPv6 = args.Any("-6".Equals);
            AutoSet = args.Any(x => "-y".Equals(x, StringComparison.OrdinalIgnoreCase));
            ScanMode = args.Any(x => "-s".Equals(x, StringComparison.OrdinalIgnoreCase));
        }
        IPFile = !IsIPv6 ? "ip.txt" : "ipv6.txt";
    }
}