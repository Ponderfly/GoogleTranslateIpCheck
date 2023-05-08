# GoogleTranslateIpCheck
![](https://img.shields.io/github/actions/workflow/status/ponderfly/googletranslateipcheck/build-win.yml) ![](https://img.shields.io/github/license/ponderfly/googletranslateipcheck) ![](https://img.shields.io/github/v/release/ponderfly/googletranslateipcheck) ![](https://img.shields.io/github/downloads/ponderfly/googletranslateipcheck/total)

 扫描并应用国内可用的谷歌翻译 IP 地址

## 使用方法

| 参数 |             作用              |
| :--: | :--------------------------: |
|  -s  |       直接进入扫描模式        |
|  -y  |      自动写入 Host 文件       |
|  -6  | IPv6 模式 (若支持请优先使用) |

+ 删除`ip.txt`和`ipv6.txt`文件调用远程 IP 地址或进入扫描模式
+ Windows 需要使用**管理员权限**运行
+ macOS 和 Linux 需要在终端中导航到软件目录然后执行

> macOS 和 Linux 添加运行权限
> ```bash
> chmod +x GoogleTranslateIpCheck
> sudo ./GoogleTranslateIpCheck
> ```

## 下载地址

|      Windows      |      macOS      |      Linux      |
| :---------------: | :-------------: | :-------------: |
| [稳定版][Windows] | [稳定版][macOS] | [稳定版][Linux] |
| [测试版][Actions] |        -        |        -        |

## 常见问题

1. 如果所有 IP 都超时,请检查是否开了代理 
2. macOS 提示来自不明身份: 系统偏好设置 -> 安全性与隐私 -> 允许


## 致谢
扫描逻辑参考 [gscan-quic](https://repo.or.cz/gscan_quic.git) 项目，感谢大佬。

[macOS]: https://github.com/Ponderfly/GoogleTranslateIpCheck/releases/download/1.6/GoogleTranslateIpCheck-mac-x64.zip
[Windows]: https://github.com/Ponderfly/GoogleTranslateIpCheck/releases/download/1.6/GoogleTranslateIpCheck-win-x64.zip
[Linux]: https://github.com/Ponderfly/GoogleTranslateIpCheck/releases/download/1.6/GoogleTranslateIpCheck-linux-x64.zip
[Actions]: https://github.com/Ponderfly/GoogleTranslateIpCheck/actions