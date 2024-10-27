#include <stdint.h>

// ɨ��״̬
typedef enum {
	// �ɹ�ɨ���һ��IP
	SUCCESS = 0,
	// ɨ����������ȡ��
	CANCELLED = 1,
	// ɨ�������������
	COMPLETED = 2,
} TurboSynScanState;

// ɨ��ɹ�����ɵĽ��
typedef struct {
	// ɨ��״̬
	TurboSynScanState State;
	// TCP�˿�
	int32_t Port;
	// ��ǰIP���ݳ���
	int32_t IPLength;
	// ��ǰIP������
	uint8_t IPAddress[16];
} TurboSynScanResult;

// ɨ�����
typedef struct {
	// ��ǰIP����
	uint64_t CurrentCount;
	// Ҫɨ���IP������
	uint64_t TotalCount;
	// ��ǰɨ���IP���ݳ���
	int32_t IPLength;
	// ��ǰɨ���IP������
	uint8_t IPAddress[16];
} TurboSynScanProgress;

// ɨ����
typedef void* TurboSynScanner;

// ɨ��ɹ��ص�
typedef void (*TurboSynScanResultCallback)(
	// ɨ����
	TurboSynScanResult scanResult,
	// �û��Զ������
	void* userParam);

// ɨ����Ȼص�
typedef void (*TurboSynScanProgressCallback)(
	// ɨ�����
	TurboSynScanProgress scanProgress,
	// �û��Զ������
	void* userParam);

// ����ɨ����
// ��Ҫ admin Ȩ��
// ʧ���򷵻� NULL
extern "C" TurboSynScanner TurboSynCreateScanner(
	// CIDR��IP��ַ�ı����ݣ�һ��һ����¼
	const char* content);

// ��ʼɨ��
extern "C" bool TurboSynStartScan(
	// ɨ����
	TurboSynScanner scanner,
	// TCP�˿�
	int32_t port,
	// ����ص�
	TurboSynScanResultCallback resultCallback,
	// ���Ȼص�
	TurboSynScanProgressCallback progressCallback,
	// callback ���û��Զ������
	void* userParam);

// ȡ��ɨ����������ɨ������
extern "C" bool TurboSynCancelScan(
	// ɨ����
	TurboSynScanner scanner);

// �ͷ�ɨ����
extern "C" void TurboSynFreeScanner(
	// ɨ����
	TurboSynScanner scanner);