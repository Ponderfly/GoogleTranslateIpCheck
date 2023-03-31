using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Flurl.Http;
using GoogleTranslateIpCheck.Properties;

namespace GoogleTranslateIpCheck;
public static partial class Program
{
    private static async Task<HashSet<string>?> GetIP(Config? config)
    {
        string[]? lines;
        if (!File.Exists(IPFile))
        {
            Console.WriteLine(Texts.IPNotFind);
            lines = await GetRemoteIP(config);
            if (lines is null) return null;
        }
        else
        {
            lines = File.ReadAllLines(IPFile);
            if (lines.Length < 1)
            {
                lines = await GetRemoteIP(config);
                if (lines is null) return null;
            }
        }
        HashSet<string> listIp = new( );
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.Contains(',') && CheckIP(line))
                listIp.Add(line.Trim( ));
            else
            {
                foreach (var ip in line.Split(',').Where(ip => CheckIP(ip)))
                {
                    listIp.Add(ip.Trim( ));
                }
            }
        }
        Console.WriteLine($"找到 {listIp.Count} 条IP");
        return listIp;
    }

    private static async Task<string[]?> GetRemoteIP(Config? config)
    {
        try
        {
            string address = !IsIPv6 ? config!.RemoteIP : config!.RemoteIPv6;
            string text = await address.WithTimeout(10).GetStringAsync( );
            return text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        catch (FlurlHttpException)
        {
            Console.WriteLine(Texts.RemoteIPFail);
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
        else Add(HostApi);
        if (hosts.Any(s => s.Contains(HostSite)))
            Update(HostSite);
        else Add(HostSite);
        File.WriteAllLines(hostFile, hosts);
    }

    private static void SaveIP(string ipPath, IOrderedEnumerable<KeyValuePair<string, long>> sortList)
    {
        File.WriteAllLines(
            ipPath,
            sortList.Reverse( ).Select(x => x.Key),
            Encoding.UTF8);
    }

    private static bool CheckIP(string ip)
    {
        if (IsIPv6) return IPRegex.IPv6Regex( ).IsMatch(ip.Trim( ));
        else return IPRegex.IPv4Regex( ).IsMatch(ip.Trim( ));
    }

    private static void FlushDNS( )
    {
        StringBuilder output = new( );
        Dictionary<OSPlatform, Tuple<string, string>> dnsCmdLine = new( )
        {
            { OSPlatform.Windows, new ("ipconfig.exe","/flushdns") },
            { OSPlatform.OSX, new ("killall", "-HUP mDNSResponder") },
            { OSPlatform.Linux, new ("systemctl", "restart systemd-resolved") },
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
        catch (Exception ex) { Console.WriteLine(ex); }
    }
}
