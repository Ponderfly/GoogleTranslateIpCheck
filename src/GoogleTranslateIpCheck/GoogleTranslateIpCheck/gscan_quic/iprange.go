package main

import (
	"bufio"
	"math/rand"
	"net"
	"os"
	"sort"
	"strings"
	"time"

	"github.com/mikioh/ipaddr"
)

// 从每组地址中分离出起始IP以及结束IP
// 参考自 moonshawdo 的代码
func splitIP(strline string) []ipaddr.Prefix {
	var begin, end string
	if strings.Contains(strline, "-") && strings.Contains(strline, "/") {
		ss := strings.Split(strline, "-")
		if len(ss) == 2 {
			iprange1, iprange2 := ss[0], ss[1]
			// "1.9.22.0/24-1.9.22.0"
			if strings.Contains(iprange1, "/") && !strings.Contains(iprange2, "/") {
				begin = iprange1[:strings.Index(iprange1, "/")]
				if begin == iprange2 {
					iprange2 = iprange1
				}
			} else if strings.Contains(iprange2, "/") {
				// 1.9.22.0/24-1.9.22.0/24
				begin = iprange1[:strings.Index(iprange1, "/")]
			} else {
				// 1.9.22.0-1.9.23.0/24
				begin = iprange1
			}
			// c, err := ipaddr.Parse(begin + "," + iprange2)
			// if err != nil {
			// 	panic(err)
			// }
			// return ipaddr.Aggregate(c.List())

			if c, err := ipaddr.Parse(iprange2); err == nil {
				end = c.Last().IP.String()
			}
		}
	} else if strings.Contains(strline, "-") {
		num_regions := strings.Split(strline, ".")
		if len(num_regions) == 4 {
			// "xxx.xxx.xxx-xxx.xxx-xxx"
			for _, region := range num_regions {
				if strings.Contains(region, "-") {
					a := strings.Split(region, "-")
					s, e := a[0], a[1]
					begin += "." + s
					end += "." + e
				} else {
					begin += "." + region
					end += "." + region
				}
			}
			begin = begin[1:]
			end = end[1:]
		} else {
			// "xxx.xxx.xxx.xxx-xxx.xxx.xxx.xxx"
			a := strings.Split(strline, "-")
			begin, end = a[0], a[1]
			if 1 <= len(end) && len(end) <= 3 {
				prefix := begin[0:strings.LastIndex(begin, ".")]
				end = prefix + "." + end
			}
		}
	} else if strings.HasSuffix(strline, ".") {
		// "xxx.xxx.xxx."
		begin = strline + "0"
		end = strline + "255"
	} else if strings.Contains(strline, "/") {
		// "xxx.xxx.xxx.xxx/xx"
		if c, err := ipaddr.Parse(strline); err == nil {
			return c.List()
		}
		return nil
	} else {
		// "xxx.xxx.xxx.xxx"
		// 如果IP带有端口, 那么就分离端口
		if i := strings.LastIndex(strline, ":"); i != -1 {
			if c, err := ipaddr.Parse(strline[:i]); err == nil {
				return c.List()
			}
		}
		begin = strline
		end = strline
	}

	return ipaddr.Summarize(net.ParseIP(begin), net.ParseIP(end))
}

var sepReplacer = strings.NewReplacer(`","`, ",", `", "`, ",", "|", ",")

func parseIPRangeFile(file string) (chan string, error) {
	f, err := os.Open(file)
	if err != nil {
		return nil, err
	}
	defer f.Close()

	ipranges := make([]ipaddr.Prefix, 0)
	scanner := bufio.NewScanner(f)
	// 一行最大 4MB
	buf := make([]byte, 1024*1024*4)
	scanner.Buffer(buf, 1024*1024*4)

	for scanner.Scan() {
		line := strings.TrimFunc(scanner.Text(), func(r rune) bool {
			switch r {
			case ',', '|', '"', '\'':
				return true
			case '\t', '\n', '\v', '\f', '\r', ' ', 0x85, 0xA0:
				return true
			}
			return false
		})
		if line == "" || strings.HasPrefix(line, "#") {
			continue
		}

		// 支持 gop 的 "xxx","xxx" 和 goa 的 xxx|xxx 格式
		if s := sepReplacer.Replace(line); strings.Contains(s, ",") {
			if c, err := ipaddr.Parse(s); err == nil {
				ipranges = append(ipranges, c.List()...)
			}
		} else {
			ipranges = append(ipranges, splitIP(line)...)
		}
	}

	/*	IP段去重	(此描述对当前算法不适用-2017/09/21)

		"1.9.22.0-255"
		"1.9.0.0/16"
		"1.9.22.0-255"
		"1.9.22.0/24"
		"1.9.22.0-255"
		"1.9.22.0-1.9.22.100"
		"1.9.22.0-1.9.22.255"
		"1.9.0.0/16"
		"3.3.3.0/24"
		"3.3.0.0/16"
		"3.3.3.0-255"
		"1.1.1.0/24"
		"1.9.0.0/16"
		"2001:db8::1/128"
			  |
			  |
			  v
		[1.9.0.0/16 3.3.0.0/16 1.1.1.0/24 203.0.113.0/24 2001:db8::1/128]
	*/

	out := make(chan string, 200)
	go func() {
		defer close(out)
		if len(ipranges) > 0 {
			sort.Slice(ipranges, func(i int, j int) bool {
				return strings.Compare(ipranges[i].String(), ipranges[j].String()) == -1
			})
			ipranges = dedup(ipranges)

			// 打乱IP段扫描顺序
			rand.Seed(time.Now().Unix())
			perm := rand.Perm(len(ipranges))
			for _, v := range perm {
				c := ipaddr.NewCursor([]ipaddr.Prefix{ipranges[v]})
				for ip := c.First(); ip != nil; ip = c.Next() {
					out <- ip.IP.String()
				}
			}

		}
	}()
	return out, nil
}

func dedup(s []ipaddr.Prefix) []ipaddr.Prefix {
	out := s[:1]
	t := s[0]
	for _, s := range s[1:] {
		if !t.Contains(&s) && !t.Equal(&s) {
			out = append(out, s)
			t = s
		}
	}
	return out
}
