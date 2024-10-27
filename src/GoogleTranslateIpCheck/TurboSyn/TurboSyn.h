#include <stdint.h>

// 扫描状态
typedef enum {
	// 成功扫描出一个IP
	SUCCESS = 0,
	// 扫描器任务已取消
	CANCELLED = 1,
	// 扫描器任务已完成
	COMPLETED = 2,
} TurboSynScanState;

// 扫描成功或完成的结果
typedef struct {
	// 扫描状态
	TurboSynScanState State;
	// TCP端口
	int32_t Port;
	// 当前IP内容长度
	int32_t IPLength;
	// 当前IP的内容
	uint8_t IPAddress[16];
} TurboSynScanResult;

// 扫描进度
typedef struct {
	// 当前IP数量
	uint64_t CurrentCount;
	// 要扫描的IP总数量
	uint64_t TotalCount;
	// 当前扫描的IP内容长度
	int32_t IPLength;
	// 当前扫描的IP的内容
	uint8_t IPAddress[16];
} TurboSynScanProgress;

// 扫描器
typedef void* TurboSynScanner;

// 扫描成功回调
typedef void (*TurboSynScanResultCallback)(
	// 扫描结果
	TurboSynScanResult scanResult,
	// 用户自定义参数
	void* userParam);

// 扫描进度回调
typedef void (*TurboSynScanProgressCallback)(
	// 扫描进度
	TurboSynScanProgress scanProgress,
	// 用户自定义参数
	void* userParam);

// 创建扫描器
// 需要 admin 权限
// 失败则返回 NULL
extern "C" TurboSynScanner TurboSynCreateScanner(
	// CIDR或IP地址文本内容，一行一条记录
	const char* content);

// 开始扫描
extern "C" bool TurboSynStartScan(
	// 扫描器
	TurboSynScanner scanner,
	// TCP端口
	int32_t port,
	// 结果回调
	TurboSynScanResultCallback resultCallback,
	// 进度回调
	TurboSynScanProgressCallback progressCallback,
	// callback 的用户自定义参数
	void* userParam);

// 取消扫描器的所有扫描任务
extern "C" bool TurboSynCancelScan(
	// 扫描器
	TurboSynScanner scanner);

// 释放扫描器
extern "C" void TurboSynFreeScanner(
	// 扫描器
	TurboSynScanner scanner);