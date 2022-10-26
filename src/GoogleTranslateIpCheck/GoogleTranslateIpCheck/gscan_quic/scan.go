package main

import (
	"context"
	"log"
	"os"
	"os/signal"
	"sync"
	"sync/atomic"
	"time"
)

type ScanRecord struct {
	IP  string
	RTT time.Duration
}

type ScanRecords struct {
	recordMutex sync.RWMutex
	records     []*ScanRecord
	scanCounter int32
}

func (srs *ScanRecords) AddRecord(rec *ScanRecord) {
	srs.recordMutex.Lock()
	srs.records = append(srs.records, rec)
	srs.recordMutex.Unlock()
	log.Printf("Found a record: IP=%s, RTT=%s\n", rec.IP, rec.RTT.String())
}

func (srs *ScanRecords) IncScanCounter() {
	scanCount := atomic.AddInt32(&srs.scanCounter, 1)
	if scanCount%1000 == 0 {
		log.Printf("Scanned %d IPs, Found %d records\n", scanCount, srs.RecordSize())
	}
}

func (srs *ScanRecords) RecordSize() int {
	srs.recordMutex.RLock()
	defer srs.recordMutex.RUnlock()
	return len(srs.records)
}

func (srs *ScanRecords) ScanCount() int32 {
	return atomic.LoadInt32(&srs.scanCounter)
}

var testIPFunc func(ip string, config *ScanConfig, record *ScanRecord) bool

func testip(ip string, config *ScanConfig) *ScanRecord {
	record := new(ScanRecord)
	for i := 0; i < config.ScanCountPerIP; i++ {
		if !testIPFunc(ip, config, record) {
			return nil
		}
	}
	record.IP = ip
	record.RTT = record.RTT / time.Duration(config.ScanCountPerIP)
	return record
}

func testip_worker(ctx context.Context, ch chan string, gcfg *GScanConfig, cfg *ScanConfig, srs *ScanRecords, wg *sync.WaitGroup) {
	defer wg.Done()

	timer := time.NewTimer(cfg.ScanMaxRTT + 100*time.Millisecond)
	defer timer.Stop()

	ctx, cancal := context.WithCancel(ctx)
	defer cancal()

	for ip := range ch {
		srs.IncScanCounter()

		if gcfg.VerifyPing {
			start := time.Now()
			if err := Ping(ip, gcfg.ScanMaxPingRTT); err != nil {
				continue
			}
			if time.Since(start) < gcfg.ScanMinPingRTT {
				continue
			}
		}

		done := make(chan struct{}, 1)
		go func() {
			r := testip(ip, cfg)
			if r != nil {
				if srs.RecordSize() >= cfg.RecordLimit {
					close(done)
					return
				}
				srs.AddRecord(r)
			}
			done <- struct{}{}
		}()

		timer.Reset(cfg.ScanMaxRTT + 100*time.Millisecond)
		select {
		case <-ctx.Done():
			return
		case <-timer.C:
			log.Println(ip, "timeout")
		case <-done:
		}
	}
}

func StartScan(srs *ScanRecords, gcfg *GScanConfig, cfg *ScanConfig, ipqueue chan string) {
	var wg sync.WaitGroup
	wg.Add(gcfg.ScanWorker)

	interrupt := make(chan os.Signal, 1)
	signal.Notify(interrupt, os.Interrupt)

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	go func() {
		<-interrupt
		cancel()
	}()

	ch := make(chan string, 100)
	for i := 0; i < gcfg.ScanWorker; i++ {
		go testip_worker(ctx, ch, gcfg, cfg, srs, &wg)
	}

	for ip := range ipqueue {
		select {
		case ch <- ip:
		case <-ctx.Done():
			return
		}
		if srs.RecordSize() >= cfg.RecordLimit {
			break
		}
	}

	close(ch)
	wg.Wait()
}
