using CliWrap;
using CliWrap.EventStream;
using Flurl.Http;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

//var ip = "142.250.101.90";
var ips = ReadIp();
if (ips == null || ips?.Count == 0)
{
    Console.WriteLine("ip.txt 格式为每行一条IP");
    Console.WriteLine("172.253.114.90");
    Console.WriteLine("172.253.113.90");
    Console.WriteLine("或者为 , 分割");
    Console.WriteLine("172.253.114.90,172.253.113.90");
    Console.ReadKey();
    return;
}
Dictionary<string, long> times = new();
foreach (var ip in ips!)
{
    TestIp(ip);
}
var bestIp = times.MinBy(x => x.Value).Key;
Console.WriteLine($"最佳IP为: {bestIp} 响应时间 {times.MinBy(x => x.Value).Value} ms");
Console.WriteLine("是否设置到Host文件(Y:设置)");
if (Console.ReadKey().Key != ConsoleKey.Y)
    return;
Console.WriteLine();
try { SetHostFile(); }
catch (Exception ex) { Console.WriteLine($"设置失败:{ex.Message}"); }
Console.WriteLine("设置成功");
Console.ReadKey();

void TestIp(string ip)
{
    try
    {
        var url = $@"http://{ip}/translate_a/single?client=gtx&sl=en&tl=fr&q=a";
        Stopwatch sw = new();
        var time = 3000L;
        for (int i = 0; i < 3; i++)
        {
            sw.Start();
            var r = url
            .WithHeader("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9")
            .WithHeader("accept-encoding", "gzip, deflate, br")
            .WithHeader("accept-language", "zh-CN,zh;q=0.9")
            .WithHeader("sec-ch-ua", "\"Chromium\";v=\"106\", \"Not;A=Brand\";v=\"99\", \"Google Chrome\";v=\"106.0.5249.119\"")
            .WithHeader("host", "translate.googleapis.com")
            .WithHeader("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/106.0.0.0 Safari/537.36")
            .WithTimeout(3)
            .GetStringAsync().GetAwaiter().GetResult();
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

async Task ScanIpAsync()
{
    var stdOutBuffer = new StringBuilder();
    var stdErrBuffer = new StringBuilder();
    var cmd = Cli.Wrap("gscan_quic.exe")
    .WithWorkingDirectory(@"gscan_quic")
    .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdOutBuffer))
    .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stdErrBuffer));

    await foreach (var cmdEvent in cmd.ListenAsync())
    {
        switch (cmdEvent)
        {
            case StartedCommandEvent started:
                Console.WriteLine($"Process started; ID: {started.ProcessId}");
                break;
            case StandardOutputCommandEvent stdOut:
                Console.WriteLine($"Out> {stdOut.Text}");
                break;
            case StandardErrorCommandEvent stdErr:
                Console.WriteLine($"Err> {stdErr.Text}");
                break;
            case ExitedCommandEvent exited:
                Console.WriteLine($"Process exited; Code: {exited.ExitCode}");
                break;
        }
    }
    var result = await cmd.ExecuteAsync();
    var exitCode = result.ExitCode;

}


List<string>? ReadIp()
{
    if (!File.Exists("ip.txt"))
    {
        Console.WriteLine("未能找到IP文件");
        return null;
    }
    var lines = File.ReadAllLines("ip.txt");
    if (lines.Length < 0) return null;
    var ips = new List<string>();
    foreach (var line in lines)
    {
        if (string.IsNullOrWhiteSpace(line))
            continue;
        if (line.Contains(','))
        {
            foreach (var ip in line.Split(','))
            {
                if (JudgeIPFormat(ip.Trim()))
                    ips.Add(ip.Trim());
            }
        }
        else
        {
            if (JudgeIPFormat(line.Trim()))
                ips.Add(line.Trim());
        }
    }
    return ips;
}

void SetHostFile()
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
    var host = "translate.googleapis.com";
    var ip = $"{bestIp} {host}";
    File.SetAttributes(hostFile, FileAttributes.Normal);
    var lines = File.ReadAllLines(hostFile);
    if (lines.Any(s => s.Contains("translate.googleapis.com")))
    {
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("translate.googleapis.com"))
                lines[i] = ip;
        }
        File.WriteAllLines(hostFile, lines);
    }
    else if (!lines.Contains(ip))
    {
        File.AppendAllLines(hostFile, new String[] { Environment.NewLine });
        File.AppendAllLines(hostFile, new String[] { ip });
    }
}

bool JudgeIPFormat(string strJudgeString)
{
    bool blnTest = false;
    bool _Result = true;

    Regex regex = new Regex("^[0-9]{1,3}.[0-9]{1,3}.[0-9]{1,3}.[0-9]{1,3}$");
    blnTest = regex.IsMatch(strJudgeString);
    if (blnTest == true)
    {
        string[] strTemp = strJudgeString.Split(new char[] { '.' });
        int nDotCount = strTemp.Length - 1;
        if (3 == nDotCount)
        {
            for (int i = 0; i < strTemp.Length; i++)
            {
                if (Convert.ToInt32(strTemp[i]) > 255)
                {
                    _Result = false;
                }
            }
        }
        else
        {
            _Result = false;
        }
    }
    else
    {
        _Result = false;
    }
    return _Result;
}