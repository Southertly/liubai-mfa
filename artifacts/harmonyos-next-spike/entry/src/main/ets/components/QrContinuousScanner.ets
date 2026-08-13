import { BusinessError } from '@kit.BasicServicesKit';
import { customScan, scanBarcode, scanCore } from '@kit.ScanKit';

@Component
export struct QrContinuousScanner {
  @State private scannerHint: string = '正在启动相机…';

  public onDetected: (value: string) => Promise<boolean> = async (_value: string): Promise<boolean> => true;
  public onError: (message: string) => void = (_message: string): void => {};

  private controller: XComponentController = new XComponentController();
  private scannerActive: boolean = false;
  private rescanTimerId: number = -1;

  private readonly options: scanBarcode.ScanOptions = {
    scanTypes: [scanCore.ScanType.QR_CODE],
    enableMultiMode: false,
    enableAlbum: false
  };

  private readonly scanCallback = async (
    error: BusinessError,
    results: Array<scanBarcode.ScanResult>
  ): Promise<void> => {
    if (!this.scannerActive) {
      return;
    }
    if (error) {
      this.onError(`code=${error.code}, message=${error.message}`);
      this.scheduleRescan();
      return;
    }
    if (results.length === 0) {
      this.scheduleRescan();
      return;
    }
    let shouldContinue = true;
    for (let index = 0; index < results.length && shouldContinue; index++) {
      const value = results[index].originalValue.trim();
      if (value.length > 0) {
        shouldContinue = await this.onDetected(value);
      }
    }
    if (shouldContinue) {
      this.scheduleRescan();
    } else {
      await this.stopScanner();
    }
  };

  private startScanner(): void {
    if (this.scannerActive) {
      return;
    }
    try {
      const surfaceId = this.controller.getXComponentSurfaceId();
      const rect = this.controller.getXComponentSurfaceRect();
      if (surfaceId.length === 0) {
        throw new Error('Scanner surface is unavailable.');
      }
      const width = rect.surfaceWidth > 0 ? rect.surfaceWidth : 1080;
      const height = rect.surfaceHeight > 0 ? rect.surfaceHeight : 1440;
      this.scannerActive = true;
      customScan.init(this.options);
      customScan.start({ width, height, surfaceId }, this.scanCallback);
      this.scannerHint = '保持镜头对准旧设备，三张二维码会自动收集';
    } catch (error) {
      this.scannerActive = false;
      this.onError(this.errorText(error));
    }
  }

  private scheduleRescan(): void {
    if (!this.scannerActive) {
      return;
    }
    if (this.rescanTimerId >= 0) {
      clearTimeout(this.rescanTimerId);
    }
    this.rescanTimerId = setTimeout(() => {
      this.rescanTimerId = -1;
      if (!this.scannerActive) {
        return;
      }
      try {
        customScan.rescan();
      } catch (error) {
        this.onError(this.errorText(error));
      }
    }, 500);
  }

  private async stopScanner(): Promise<void> {
    if (this.rescanTimerId >= 0) {
      clearTimeout(this.rescanTimerId);
      this.rescanTimerId = -1;
    }
    if (!this.scannerActive) {
      return;
    }
    this.scannerActive = false;
    try {
      await customScan.stop();
    } catch (error) {
      // The camera stream may already be stopped while the component is disappearing.
    }
    try {
      await customScan.release();
    } catch (error) {
      // Release is best effort after Scan Kit has already closed the stream.
    }
  }

  private errorText(error: Object): string {
    const maybeError = error as Error;
    const maybeBusinessError = error as Record<string, Object>;
    const code = maybeBusinessError['code'];
    const message = maybeBusinessError['message'] ?? maybeError.message;
    return `code=${code ?? 'unknown'}, message=${message ?? 'unknown error'}`;
  }

  aboutToDisappear(): void {
    void this.stopScanner();
  }

  build() {
    Stack({ alignContent: Alignment.Center }) {
      XComponent({
        id: 'mfa-qr-continuous-scanner',
        type: XComponentType.SURFACE,
        controller: this.controller
      })
        .width('100%')
        .height('100%')
        .onLoad(() => {
          this.startScanner();
        })
        .onDestroy(() => {
          void this.stopScanner();
        });

      Column() {
        Blank();
        Text(this.scannerHint)
          .fontSize(13)
          .fontColor('#FFFFFFFF')
          .textAlign(TextAlign.Center)
          .padding({ left: 12, right: 12, top: 8, bottom: 8 })
          .backgroundColor('#99000000')
          .borderRadius(8)
          .margin({ bottom: 16 });
      }
      .width('100%')
      .height('100%');

      Column()
        .width('72%')
        .aspectRatio(1)
        .border({ width: 3, color: '#FFFFFFFF' })
        .borderRadius(16);
    }
    .width('100%')
    .aspectRatio(0.75)
    .clip(true)
    .borderRadius(12)
    .backgroundColor('#FF000000');
  }
}
