﻿namespace GoogleTranslateIpCheck;

public class Config
{
    public string RemoteIp { get; set; } = 
        "https://ghproxy.com/https://raw.githubusercontent.com/Ponderfly/GoogleTranslateIpCheck/master/GoogleTranslateIpCheck/ip.txt";
    public string RemoteIPv6 { get; set; } = 
        "https://ghproxy.com/https://raw.githubusercontent.com/Ponderfly/GoogleTranslateIpCheck/master/GoogleTranslateIpCheck/ipv6.txt";
    public int ScanLimit { get; set; } = 5;
    public int ScanTimeout { get; set; } = 4;
    public int ScanSpeed { get; set; } = 80;
    public string[] IpRange { get; set; } =
    {
        "142.250.0.0/15",
        "172.217.0.0/16",
        "172.253.0.0/16",
        "108.177.0.0/17",
        "72.14.192.0/18",
        "74.125.0.0/16",
        "216.58.192.0/19"
    };
    public string[] IPV6Range { get; set; } =
    {
        "2404:6800:4008:c15::0/112",
        "2a00:1450:4001:802::0/112",
        "2a00:1450:4001:803::0/112",
        "2a00:1450:4001:809::0/112",
        "2a00:1450:4001:811::0/112",
        "2a00:1450:4001:827::0/112",
        "2a00:1450:4001:828::0/112",
    };
}
