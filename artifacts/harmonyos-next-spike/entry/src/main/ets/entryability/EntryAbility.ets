import { AbilityConstant, UIAbility, Want } from '@kit.AbilityKit';
import { hilog } from '@kit.PerformanceAnalysisKit';
import { window } from '@kit.ArkUI';

const DOMAIN = 0x0000;
const BACKGROUND_AT_KEY = 'mfa_verifier_background_at';
const SHOULD_LOCK_KEY = 'mfa_verifier_should_lock';
const APP_LOCK_ENABLED_KEY = 'mfa_verifier_app_lock_enabled';
const BACKGROUND_LOCK_TIMEOUT_MS = 60_000;

export default class EntryAbility extends UIAbility {
  private async enableWindowPrivacy(windowStage: window.WindowStage): Promise<void> {
    try {
      const mainWindow = await windowStage.getMainWindow();
      await mainWindow.setWindowPrivacyMode(true);
      hilog.info(DOMAIN, 'MfaVerifier', '%{public}s', 'Window privacy mode enabled');
    } catch (error) {
      hilog.error(DOMAIN, 'MfaVerifier', 'Failed to enable window privacy mode: %{public}s', JSON.stringify(error));
    }
  }

  onCreate(want: Want, launchParam: AbilityConstant.LaunchParam): void {
    AppStorage.setOrCreate<number>(BACKGROUND_AT_KEY, 0);
    AppStorage.setOrCreate<boolean>(SHOULD_LOCK_KEY, false);
    AppStorage.setOrCreate<boolean>(APP_LOCK_ENABLED_KEY, false);
    hilog.info(DOMAIN, 'MfaVerifier', '%{public}s', 'EntryAbility onCreate');
  }

  onDestroy(): void {
    hilog.info(DOMAIN, 'MfaVerifier', '%{public}s', 'EntryAbility onDestroy');
  }

  onWindowStageCreate(windowStage: window.WindowStage): void {
    void this.enableWindowPrivacy(windowStage);
    windowStage.loadContent('pages/Index', (err) => {
      if (err.code) {
        hilog.error(DOMAIN, 'MfaVerifier', 'Failed to load content: %{public}s', JSON.stringify(err));
        return;
      }
      hilog.info(DOMAIN, 'MfaVerifier', '%{public}s', 'Loaded Index page');
    });
  }

  onWindowStageDestroy(): void {
    hilog.info(DOMAIN, 'MfaVerifier', '%{public}s', 'EntryAbility onWindowStageDestroy');
  }

  onForeground(): void {
    const backgroundAt = AppStorage.get<number>(BACKGROUND_AT_KEY) ?? 0;
    const appLockEnabled = AppStorage.get<boolean>(APP_LOCK_ENABLED_KEY) ?? false;
    if (appLockEnabled && backgroundAt > 0 && Date.now() - backgroundAt >= BACKGROUND_LOCK_TIMEOUT_MS) {
      AppStorage.set<boolean>(SHOULD_LOCK_KEY, true);
    }
    AppStorage.set<number>(BACKGROUND_AT_KEY, 0);
    hilog.info(DOMAIN, 'MfaVerifier', '%{public}s', 'EntryAbility onForeground');
  }

  onBackground(): void {
    AppStorage.setOrCreate<number>(BACKGROUND_AT_KEY, Date.now());
    hilog.info(DOMAIN, 'MfaVerifier', '%{public}s', 'EntryAbility onBackground');
  }
}
