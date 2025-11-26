# GoogleTranslateIpCheck
#### 扫描国内可用的谷歌翻译IP
#### 如果都不能使用可以删除 ip.txt 文件调用远程IP或进入扫描模式
#### 使用参数 -s 可以直接进入扫描模式  -y 自动写入Host文件   -6 进入IPv6模式(如果支持IPv6推荐优先使用)
##### Windows 需要使用管理员权限运行
##### Mac和Linux运行 需要在终端中导航到软件目录然后执行
```
chmod +x GoogleTranslateIpCheck
sudo ./GoogleTranslateIpCheck
```

#### 下载地址

##### Window
##### https://github.com/Ponderfly/GoogleTranslateIpCheck/releases/download/1.8/win-x64.zip
##### https://github.com/Ponderfly/GoogleTranslateIpCheck/releases/download/1.8/win-x86.zip
##### 推荐使用 底层基于 WinDivert 的 [TurboSyn](https://github.com/spartacus-soft/TurboSyn) 进行快速扫描,需管理员运行,只支持Windows系统,使用 -t 可进入自动扫描模式
##### 🌟 https://github.com/Ponderfly/GoogleTranslateIpCheck/releases/download/1.10/win-x64.TurboSyn.zip
 
##### Mac OS
##### https://github.com/Ponderfly/GoogleTranslateIpCheck/releases/download/1.8/osx-arm64.zip
##### https://github.com/Ponderfly/GoogleTranslateIpCheck/releases/download/1.8/osx-x64.zip
 
##### Linux
##### https://github.com/Ponderfly/GoogleTranslateIpCheck/releases/download/1.8/linux-x64.zip
##### https://github.com/Ponderfly/GoogleTranslateIpCheck/releases/download/1.8/linux-arm64.zip
##### 静态链接版，适合glibc过低的老系统 https://github.com/user-attachments/files/23772336/linux-musl-x64.zip

#### 常见问题
##### 1.如果所有IP都超时,请检查是否开了代理 
##### 2.Mac 中使用: 打开终端 输入cd 把解压后的文件夹拖进终端，点击回车 复制粘贴代码，点击回车
```
chmod +x GoogleTranslateIpCheck
sudo ./GoogleTranslateIpCheck
```
##### 3.Mac 提示来自不明身份: 系统偏好设置－－>安全性与隐私--->选择允许


#### 扫描逻辑参考 https://repo.or.cz/gscan_quic.git 项目,感谢大佬
