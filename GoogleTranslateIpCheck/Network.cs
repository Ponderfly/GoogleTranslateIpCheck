using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Flurl.Http;
using GoogleTranslateIpCheck.Properties;

namespace GoogleTranslateIpCheck;

public static partial class Program
{

    private static async Task<bool> ConnectIp(string ip, Config? config)
    {
        string str = !IsIPv6 ? $"{ip}" : $"[{ip}]";
        string url = $@"https://{str}/translate_a/single?client=gtx&sl=zh-CN&tl=en&dt=t&q=你好";
        return (await url
            .WithHeader("host", HostApi)
            .WithTimeout(config!.ScanTimeout)
            .GetStringAsync( )).Contains("Hello");
    }

    private static async Task GetDelayAsync(
        string ip,
        Config? config,
        ConcurrentDictionary<string, long> IPDelays)
    {
        Stopwatch watch = new( );
        long time = 3000L;
        bool isTimeout = false;
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
                isTimeout = false;
            }
            catch (FlurlHttpTimeoutException) { isTimeout = true; }
        }
        if (isTimeout)
        {
            Console.WriteLine($"{ip}\t\t{Texts.Timeout}");
            return;
        }
        IPDelays.TryAdd(ip, time);
        Console.WriteLine($"{ip}\t\t{time} ms");
    }

    private static HashSet<string>? ScanIp(Config? config)
    {
        Console.WriteLine( );
        HashSet<string> IPList = new( );
        CancellationTokenSource tokenSource = new( );
        tokenSource.Token.Register(( )
            => Console.WriteLine($"{Texts.IPScanCount} {config!.ScanLimit} 条"));
        string[] IPRange = !IsIPv6 ? config!.IpRange : config!.IPV6Range;
        foreach (string range in IPRange)
        {
            if (string.IsNullOrWhiteSpace(range)) continue;
            IPNetwork IPMgr = IPNetwork.Parse(range);
            ConcurrentBag<string> ipTmp = new( );
            try
            {
                Parallel.ForEach(
                    IPMgr.ListIPAddress(FilterEnum.Usable),
                    new ParallelOptions( )
                    {
                        MaxDegreeOfParallelism = config!.ScanSpeed,
                        CancellationToken = tokenSource.Token
                    },
                    async (addr, _) =>
                    {
                        try
                        {
                            string ip = addr.ToString( );
                            if (!await ConnectIp(ip, config))
                                return;
                            Console.WriteLine($"{addr}");
                            ipTmp.Add(ip);
                            if (IPList.Count + ipTmp.Count >= config!.ScanLimit)
                                tokenSource.Cancel( );
                        }
                        catch { }
                    });
            }
            catch { continue; }
            finally
            {
                foreach (string ip in ipTmp) IPList.Add(ip);
            }
        }
        Console.WriteLine($"{Texts.IPScanCount} {IPList.Count} 条");
        return IPList;
    }
}