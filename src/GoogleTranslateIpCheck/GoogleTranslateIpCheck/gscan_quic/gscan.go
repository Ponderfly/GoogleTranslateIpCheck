package main

import (
	"bytes"
	"flag"
	"fmt"
	"io/ioutil"
	"log"
	"math/rand"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"sort"
	"strings"
	"time"
)

type ScanConfig struct {
	ScanCountPerIP   int
	ServerName       []string
	HTTPVerifyHosts  []string
	VerifyCommonName string
	HTTPPath         string
	ValidStatusCode  int
	HandshakeTimeout time.Duration
	ScanMinRTT       time.Duration
	ScanMaxRTT       time.Duration
	RecordLimit      int
	InputFile        string
	OutputFile       string
	OutputSeparator  string
	Level            int
}

type GScanConfig struct {
	ScanWorker     int
	VerifyPing     bool
	ScanMinPingRTT time.Duration
	ScanMaxPingRTT time.Duration
	DisablePause   bool
	EnableBackup   bool
	BackupDir      string

	ScanMode string
	Ping     ScanConfig
	Quic     ScanConfig
	Tls      ScanConfig
	Sni      ScanConfig
}

func init() {
	rand.Seed(time.Now().Unix())

	log.SetFlags(log.LstdFlags | log.Lshortfile)
}

func initConfig(cfgfile, execFolder string) *GScanConfig {
	if strings.HasPrefix(cfgfile, "./") {
		cfgfile = filepath.Join(execFolder, cfgfile)
	}

	gcfg := new(GScanConfig)
	if err := readJsonConfig(cfgfile, gcfg); err != nil {
		log.Panicln(err)
	}

	if gcfg.EnableBackup {
		if strings.HasPrefix(gcfg.BackupDir, "./") {
			gcfg.BackupDir = filepath.Join(execFolder, gcfg.BackupDir)
		}
		if _, err := os.Stat(gcfg.BackupDir); os.IsNotExist(err) {
			if err := os.MkdirAll(gcfg.BackupDir, 0755); err != nil {
				log.Println(err)
			}
		}
	}

	gcfg.ScanMode = strings.ToLower(gcfg.ScanMode)
	if gcfg.ScanMode == "ping" {
		gcfg.VerifyPing = false
	}

	gcfg.ScanMinPingRTT = gcfg.ScanMinPingRTT * time.Millisecond
	gcfg.ScanMaxPingRTT = gcfg.ScanMaxPingRTT * time.Millisecond

	cfgs := []*ScanConfig{&gcfg.Quic, &gcfg.Tls, &gcfg.Sni, &gcfg.Ping}
	for _, c := range cfgs {
		if strings.HasPrefix(c.InputFile, "./") {
			c.InputFile = filepath.Join(execFolder, c.InputFile)
		} else {
			c.InputFile, _ = filepath.Abs(c.InputFile)
		}
		if strings.HasPrefix(c.OutputFile, "./") {
			c.OutputFile = filepath.Join(execFolder, c.OutputFile)
		} else {
			c.OutputFile, _ = filepath.Abs(c.OutputFile)
		}
		if _, err := os.Stat(c.InputFile); os.IsNotExist(err) {
			os.OpenFile(c.InputFile, os.O_CREATE|os.O_TRUNC|os.O_RDWR, 0644)
		}

		c.ScanMinRTT *= time.Millisecond
		c.ScanMaxRTT *= time.Millisecond
		c.HandshakeTimeout *= time.Millisecond
	}
	return gcfg
}

func main() {
	var disablePause bool
	defer func() {
		if r := recover(); r != nil {
			fmt.Println("panic:", r)
		}
		fmt.Println()
		if !disablePause {
			if runtime.GOOS == "windows" {
				cmd := exec.Command("cmd", "/C", "pause")
				cmd.Stdout = os.Stdout
				cmd.Stdin = os.Stdin
				// 改为 start, 程序可以正常退出, 这样一些程序监视工具可以正常测到程序结束了
				cmd.Start()
			} else {
				fmt.Println("Press [Enter] to exit...")
				fmt.Scanln()
			}
		}
	}()

	var cfgfile string
	flag.StringVar(&cfgfile, "Config File", "./config.json", "Config file, json format")
	flag.Parse()

	var execFolder = "./"
	if e, err := os.Executable(); err != nil {
		log.Panicln(err)
	} else {
		execFolder = filepath.Dir(e)
	}
	// execFolder = "./"

	gcfg := initConfig(cfgfile, execFolder)
	disablePause = gcfg.DisablePause

	var cfg *ScanConfig
	scanMode := gcfg.ScanMode
	switch scanMode {
	case "quic":
		cfg = &gcfg.Quic
		testIPFunc = testQuic
	case "tls":
		cfg = &gcfg.Tls
		testIPFunc = testTls
	case "sni":
		cfg = &gcfg.Sni
		testIPFunc = testSni
	case "ping":
		cfg = &gcfg.Ping
		testIPFunc = testPing
	case "socks5":
		// testIPFunc = testSocks5
	default:
	}

	iprangeFile := cfg.InputFile
	if _, err := os.Stat(iprangeFile); os.IsNotExist(err) {
		log.Panicln(err)
	}

	srs := &ScanRecords{}

	log.Printf("Start loading IP Range file: %s\n", iprangeFile)
	ipqueue, err := parseIPRangeFile(iprangeFile)
	if err != nil {
		log.Panicln(err)
	}

	log.Printf("Start scanning available IP\n")
	startTime := time.Now()
	StartScan(srs, gcfg, cfg, ipqueue)
	log.Printf("Scanned %d IP in %s, found %d records\n", srs.ScanCount(), time.Since(startTime).String(), len(srs.records))

	if records := srs.records; len(records) > 0 {
		sort.Slice(records, func(i, j int) bool {
			return records[i].RTT < records[j].RTT
		})
		a := make([]string, len(records))
		for i, r := range records {
			a[i] = r.IP
		}
		b := new(bytes.Buffer)
		if cfg.OutputSeparator == "gop" {
			out := strings.Join(a, `", "`)
			b.WriteString(`"` + out + `",`)
		} else {
			out := strings.Join(a, cfg.OutputSeparator)
			b.WriteString(out)
		}

		if err := ioutil.WriteFile(cfg.OutputFile, b.Bytes(), 0644); err != nil {
			log.Printf("Failed to write output file:%s for reason:%v\n", cfg.OutputFile, err)
		} else {
			log.Printf("All results writed to %s\n", cfg.OutputFile)
		}
		if gcfg.EnableBackup {
			filename := fmt.Sprintf("%s_%s_lv%d.txt", scanMode, time.Now().Format("20060102_150405"), cfg.Level)
			bakfilename := filepath.Join(gcfg.BackupDir, filename)
			if err := ioutil.WriteFile(bakfilename, b.Bytes(), 0644); err != nil {
				log.Printf("Failed to write output file:%s for reason:%v\n", bakfilename, err)
			} else {
				log.Printf("All results writed to %s\n", bakfilename)
			}
		}
	}
}
