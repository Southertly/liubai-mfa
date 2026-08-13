import { abilityAccessCtrl, common, ConfigurationConstant, Permissions } from '@kit.AbilityKit';
import { util } from '@kit.ArkTS';
import { preferences, uniformTypeDescriptor } from '@kit.ArkData';
import { asset } from '@kit.AssetStoreKit';
import { pasteboard } from '@kit.BasicServicesKit';
import { fileIo, picker } from '@kit.CoreFileKit';
import { cryptoFramework } from '@kit.CryptoArchitectureKit';
import { hilog } from '@kit.PerformanceAnalysisKit';
import { scanBarcode, scanCore } from '@kit.ScanKit';
import { systemShare } from '@kit.ShareKit';
import { userAuth } from '@kit.UserAuthenticationKit';
import {
  decryptString,
  encryptString,
  formatRecoveryKey,
  generateRecoveryKey,
  isValidRecoveryKey,
  normalizeRecoveryKey,
  PortableAccount,
  SecureBackupPayload,
  SecureEnvelope,
  verifyMobileV2Compatibility
} from '../utils/BackupCrypto';
import {
  assembleTransferPackage,
  buildPairingQr,
  encodeTransferPackage,
  EncryptedTransferPackage,
  parsePairingQr,
  parseTransferChunk,
  PairingQr,
  QR_TRANSFER_SESSION_MS,
  TransferChunk
} from '../utils/QrTransfer';
import {
  clearQrReceiveSession,
  createQrReceiveSession,
  QrReceiveSession,
  unwrapQrTransferKey,
  wrapQrTransferKey
} from '../utils/QrTransferCrypto';
import {
  AppColors,
  colorsFor,
  LABEL_COLORS,
  labelColor,
  LabelColorOption
} from '../ui/ProductTheme';
import { QrContinuousScanner } from '../components/QrContinuousScanner';

interface AuthSelection {
  authTypes: userAuth.UserAuthType[];
  authTrustLevel: userAuth.AuthTrustLevel;
  details: string;
}

interface ServiceBadge {
  label: string;
  foreground: string;
  background: string;
}

interface QrImportResult {
  imported: number;
  skipped: number;
}

const QR_TRANSFER_LOG_DOMAIN = 0x0000;

// Google Identity 官方 80x80 中性圆形品牌图标（PNG @2x）。
const GOOGLE_ICON_DATA_URI: string = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAFAAAABQCAYAAACOEfKtAAAACXBIWXMAABYlAAAWJQFJUiTwAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAOdEVYdFNvZnR3YXJlAEZpZ21hnrGWYwAADTlJREFUeAHtXW2MXFUZfs69d2Z2Z2e/aJdCK3SLYiKFtP1hBArY6i9Dk34o+INEICExNWpt/AJDLDVKQgIB9AdKNEUNCWBD27QYjdKuP6BYf1AFrISvgS0UWtr9mtnZ+bj3+Lzn3pnOTvdrZu+ddnd5yOGc+9E79z593vc95z3n3iqcR2itu0ZGRtax7lVKreKuXmnLMW731pw+yGNp7h9kO832vy3LOppKpY4G+84LFJoIISyTyaxmc6PneZsmIKnR6wqJR9nc19HRsRdNRFMIDFR2O5ubWLoQLUSNe0no79vb2/sQMSIjMFDbNtbfQ/SkTYY0y06q8glEhNAJvECIq4X4zEc7OzsfQcgIlcDh4WEx0YdZenFhIo2QFRkKgblcrrdYLO5icx3mBvbGYrHtra2tacwSFmYJMVeS9zLmDnmCTXLPtJg7MEs0rMDA1+0IfN2cBaP1I4zW29EgGiJQTLZUKu0heasxDyD9yHg8vrkRk66bwMDfHcKFGygaRZp+cX29JNZF4Dwmr4y6SZwxgQuAvDLqInFGBC4g8sqYMYkz6sZIwMDCIU/QWygU9gwMDEw7kpqWQCYCHp4v0bYesHuz2nGcHdOeN9XBoKO5CwsYFM/2qcbQkxIY+D0ZYVwoCYHzhUH6wzWT+cNJTZjkSVJgoZMn6ArG+RNiQgID092ET1DGuiDTdA4mNGGe/A6aHHXt116B9dprsNJvw0nz57NZKBaBbmuDvngJdLIN7sprWK5mWYkmI+267pru7u5x8y/nECjZFc5XhJ54nAjq1Ek4fc8jdmB/hazqG2MkFCcujWCv5R9RNvSSpYbI/Fc3Q/csQjPAe9nJgHIfau5zHJqhPjWaRfyPj8M5dBDweAs6uA0NnyySZvYEbVMrIU+KwyrG7Th32YbM4o3XIb/l5mYQOUgVrqhW4TgCm9FtiR18FvFnn+SP5dhDJyGuFZBYvqMqAlG1D0IWibOFuBa2hUj58x6PefAWd6GwZQOKX7wBUaJWhbUERqq+xL5fIf73PdB5klGkGbIYEkmg0sqIzb8rdfbGzE6eQ7Upu4U8C4EJsw3P5fEiS4ltFu2icMsW5G+5BRFikFMC3eWNShSWqUdERJ7KjyD5xFbEDj8F7fChTaFybO0X4/AQmKpPna6wKVbq+z7tlf0h/7ybJ2ljQaGaNbd1wfjUWn8aMroCrgycciOYt40Eyd13wTrxFnSCqhGmxOrE73lB21ZGRDBH/cChyv5PTnVJmCGRJ3sF3x9q11eeqV05C15PD0bv+5mJ2lEi4KpP2tX9wEj6fS3/uh9W5lWgtQjVygdN8KETrGMe3ZiokMWS4ivRV16VZzGK4z6SqEiU8miyrqhOFBiYr6YP7FmM0Z0/ZyC5GE3ApnKiwSgw6CSGPuqI9e9G7N3fUYJOhSAD4Uj76lOekOarUbcmUfrKRtM98XpXVJQkJmmxb2il04g/dwAWuz/BRcxx72Iqb+cvmkWeoIuJBkmw9JVNeCNChpXvR/z4g0Bb0VeZ7Ri9q8DfmTgrxGkLursHxQ23o3jTlye8lhDpd56vRvHmDYj1HUT8T0/BOnnSN9vmkmcga3tY9RlN0Cm+HHbKquWd7yD20TNAgX5vTAoJzLFkWUbj0Fl2SbIJFFesR+Hr95hRRj0QFSZ2/RZjd97VdPICpBmNVyixZdu2BxAirGI/2l5fwy6K8rspedZ5xydx1IFH4rwMyevdgtLN92Kugp3qbkdsubrLEAbiHz9A7xr4PBMk2HBK7MIwUjLiCqeFRZ8D5jB5AgpvnUNbXq1UuGuM7Pxhv9sR9DxMAGHE1SSyxC5ILnYRrGsfbO7ixAggi0EliITq++zci7Dc477qBEobIoU8j+SNcSThXv4N2MlLMdchq2od/m85QoQ9dtgfZumgmyEqDBICLmLIx1qRuGQD5gl6hcCuMH2gXfgv/P5K+ZratDW7K64dg9X9JViJmavvob/ZMjYx11HGreqgoNLy1Nm9WvmJiWBAWDl/SbvGbdeG6+shBJYXdYcF5X6AswOc8pP4owvN1JOV+jzqwXP/a6l0wHXVsLmaCq2CRES1UzWi18EtaLQnhMAcwob4wFBHIFbp+Nk8XuWJ/HyeBCun5dOoB66qGm1OFXXUudtGp8H+IQ6hM+xOpRKhqrB31usDa6G8IYzXB3z+uM9SkoVJIWzMJJrLHWUKCB0OwobW44Tnuy/fZzF3zBxoO8LQQPnSteY81blRIHQFaislA0WTITEJT/htzUyKRQJReBWNoFZluqaeSoVRkScQAtMIEZoK83vPAXEkUciT2mP2WBXfQyPQ0xzTNSeWxwaqqqTiCBuDoSvQc646S5zMVbD3V+J2kaXA3F0xc6Cu62k/+zqlxFQVu6omYpfJbWPwCDmAyEgkLT4wjRBT+cXW62BlD5jEqGTghUYx5BLbTIEiP/YfdDLQKKtzRtd75vZR/2YxsRmXSfrLsRh2HYkH+/y+oK76A5e2h2/I8o6e9APfDXMs7CVWGsWJboS8opbik5djnfUGkD/zGJYsvntG17tkhg9+YsTMzTHSq8pMaHV36jM9LiJAWkz4KEKE13oDCqodYyRxlDef5f0Psx5kfYb1adbHTv8a2QZ94UQQ8va/QfqY8fEsKfyrYwJDzF/LKIj/rV4WPoHmjVGqL42QUezaSuI0RmjDQ1XkneGxjynNE6VhPH/8uwgLjx0toODkOFM6hqKVJ4FFv8h8gfKHkquXlRA25HVbi0nBPoQM+6KtGDHK4whA+69PDrA+zXmP0wz8Z1heyRzB0/0/xGyx6/URPJ0eYIosQxJHUXTyTILnUVQ+iQxhWHtFccauoB6USqWjVrBMIY0QIQHC6f4WZEwixA3Qmw9AikU1Kgx6MRIbw8EzB/DQ2z/AR/mP0Agef/ND/PSV9zAWH0EhlmXSe9QosUQluiSyZJNE28Wtq4oIG/JuiXBnBRv7EDIWM0hkSeQwYExYiBuiAoe0Q2U6yOgEzbwF/xg6gm1v3I39p/tmfO3+XBabjhzGvceOkbAMVZdloQk7YyhYY0aBBZqymHNPRwFrlkYSgU3sKE8qyQvRhxAy3mK0/eeJe5Dlz2RIXpb5wAyVl0EL/WOSZt7GkuK+dpKZxKLYp7Bx8Y34QsdnsbJ1GTqcpLnOcGkM/fkBvDjUjz+f6scLp4fglVKcEk4xedvG0gqbxXFbWOJMfse5LXUMf/1aO5amIsl9b5a3482Vg4mldxDB3PCLJ36Clxh1R6m6rJAnyiOBGa/NEJglgVnWObQi7yXosWzGTtv82bNTn9yn2cfzWjk6TEK7KdY+geB1VMkn0PZaWCcCEmPYdnUXvr0qiQhQWR9jTFhsOQozFlx/6f1YlLyJJDlGgTmm9HMkcQwJFIJS5H6Xx9mLk5xN1dCsnACUpR4cS6sSIx+jqSpwuyCTz+SXxS4wWZuHy22pS9y+chGiIk9Q+S5DZSjHkPwEIsKty/+A7pZV7BvGWOLIk8C8qePsZMfY8XYYLe2APIXy2izTUpW8s2RruZfjaZKorEKlaCGStcfiktClKQ+/ubEHUUG+x1BpVx8YHh6W+eHIFpY/+f4D2HtqN825laXN1Dn6PiG0QCJd5ZhlHuNdvk+op2WeJUbzbWGyp5XmnIRbajM1ZJsmbLG+pq0b+264Ch0xG1FAxr+dnZ0ryttWzcFHESFuW/ZjbL18Jy6KLw+UF2ex4QamO27wWn1fODsXIipUSsw4UKExZ1FlEd+84hIcWn9NZOQJqL6d47arN6IMJtX4sHASj3/wNHafeolj5JYqH6gmTFv5S94sP5gwwmp2f3SgOo/l+q5e/GjFaqy9aDGiRK36/HurwdDQ0H1keQeagPfzH+OXH+zHC8Nv4b38kElAjL85Zcax/m1a/kIkMWNG63bViZXJy/H95WuxtmspmoGJ3lo6h0BRIQPKy2F9VWimODzyBg4Pv2nqYTfHft8Z9v/8VJb0BzvtJK5KXobL4otxbepKXE8hdDgtaBYmUp9gsvdEZOnWHnyCatw50edSJu2ik0QZmazDJxDsJXmbJzowaUqfWZo74SdSFjRouvJuyKRf9ZiUQI5O0vJOBBY4GAu2CxeTHZ9yUkkiTtR9wwsZ8uzTfSZq2jRFEJUPyRvcWECQfB8FtGa686ad1pREg+d5myWMY4FAnlWeeSbnzjhRRiX2BkrsxTxGQN76qfxeNerKNM53EuslT1B3qna+ktgIeYK6l3bID8gPiZPFPIE8SyPkCRpaGyM/JBFqPnRx5BkaJU8w69mW4CXtOfeFDxlhsNo52++qhjJdJX6ReUQhca586aNPhqqNqq4aoc73iRr5N7vjQg0wwZfQt4f5se5IJkyZlJXPgm67UIgMzFV83SO1ny2ZLSIhsIzzrcgoiSsjUgLLkJUPfIg72NwoL/YgQghp/I25/yn4ySDZbj6kBJtVYSUognH6Pnbw98qKqajUNhGaTmA1JNMjr9sGb4yW/zkMUWhvrVLLyYxgPaP55zCkLcvzmklYLf4PvHCv85rDvMgAAAAASUVORK5CYII=';

interface AccountRecord {
  id: string;
  issuer: string;
  name: string;
  secretAlias: string;
  algorithm: string;
  digits: number;
  period: number;
  group?: string;
  color?: string;
  createdAt?: number;
}

interface AccountStorePayload {
  version: number;
  accounts: AccountRecord[];
}

interface MfaAccount {
  id: string;
  issuer: string;
  name: string;
  secretAlias: string;
  algorithm: string;
  digits: number;
  period: number;
  group: string;
  color: string;
  createdAt: number;
  code: string;
  revealedSecret: string;
}

interface OtpAuthAccount {
  issuer: string;
  name: string;
  secret: string;
  algorithm: string;
  digits: number;
  period: number;
  group?: string;
  color?: string;
  createdAt?: number;
}

@Entry
@Component
struct Index {
  @Provide('pathStack') pathStack: NavPathStack = new NavPathStack();
  @State status: string = '准备就绪';
  @State permissionResult: string = '';
  @State accounts: MfaAccount[] = [];
  @State issuerInput: string = '';
  @State nameInput: string = '';
  @State secretInput: string = '';
  @State otpauthInput: string = '';
  @State isRunning: boolean = false;
  @State nowMs: number = Date.now();
  @State editingAccountId: string = '';
  @State editIssuerInput: string = '';
  @State editNameInput: string = '';
  @State editGroupInput: string = '';
  @State editColorInput: string = 'blue';
  @State groupInput: string = '';
  @State colorInput: string = 'blue';
  @State searchQuery: string = '';
  @State recoveryKey: string = '';
  @State preparedBackup: string = '';
  @State recoveryKeyCopied: boolean = false;
  @State importRecoveryKey: string = '';
  @State selectedBackupText: string = '';
  @State selectedBackupReady: boolean = false;
  @State qrTransferMode: string = 'choice';
  @State qrPairingValue: string = '';
  @State qrTransferParts: string[] = [];
  @State qrTransferPartIndex: number = 0;
  @State qrTransferAutoAdvance: boolean = true;
  @State qrReceivedParts: number = 0;
  @State qrExpectedParts: number = 0;
  @State themePreference: string = 'system';
  @State systemDark: boolean = false;
  @State consentLoaded: boolean = false;
  @State privacyAccepted: boolean = false;
  @State appLocked: boolean = false;
  @State appLockEnabled: boolean = false;
  @State pendingDeleteId: string = '';
  @State editingGroupName: string = '';
  @State renameGroupInput: string = '';
  @State expandedGroups: string[] = [];
  @State pendingAccountActionId: string = '';
  @State accountActionMode: string = '';
  @State groupActionMode: string = '';
  @State copiedAccountId: string = '';
  @State addCreatingGroup: boolean = false;
  @State editCreatingGroup: boolean = false;
  @State moveGroupInput: string = '';
  @StorageLink('mfa_verifier_should_lock') @Watch('onLifecycleLockChanged') lifecycleShouldLock: boolean = false;
  @StorageLink('mfa_verifier_app_lock_enabled') lifecycleAppLockEnabled: boolean = false;

  private readonly preferencesName: string = 'mfa_verifier_accounts_v1';
  private readonly accountsKey: string = 'accounts';
  private readonly pendingAliasCleanupKey: string = 'pending_alias_cleanup_v1';
  private readonly themeKey: string = 'theme_preference';
  private readonly privacyKey: string = 'mfa_verifier_privacy_consent_v1';
  private readonly appLockKey: string = 'mfa_verifier_app_lock_enabled_v1';
  private readonly privacyPolicyUrl: string = 'https://example.com/mfa-verifier/privacy';
  private readonly assetPrefix: string = 'mfa-verifier-account-secret-';
  private readonly codeClipboardTtlMs: number = 30_000;
  private readonly sensitiveClipboardTtlMs: number = 60_000;
  private sessionSecrets: Map<string, string> = new Map<string, string>();
  private qrReceiveSession: QrReceiveSession | undefined = undefined;
  private qrIncomingChunks: Map<number, string> = new Map<number, string>();
  private timerId: number = -1;
  private qrTransferTimerId: number = -1;
  private clipboardGeneration: number = 0;
  private accountsWritable: boolean = false;

  aboutToAppear(): void {
    void this.initializeProductState();
    this.timerId = setInterval(() => {
      this.nowMs = Date.now();
      void this.refreshUnlockedCodes();
    }, 1000);
  }

  private async initializeProductState(): Promise<void> {
    const colorMode = this.prefsContext().config.colorMode;
    this.systemDark = colorMode === ConfigurationConstant.ColorMode.COLOR_MODE_DARK;
    try {
      const prefs = await this.getPreferences();
      const theme = await prefs.get(this.themeKey, 'system');
      if (theme === 'light' || theme === 'dark' || theme === 'system') {
        this.themePreference = theme as string;
        this.applyThemePreference(this.themePreference);
      }
      const consent = await prefs.get(this.privacyKey, false);
      this.privacyAccepted = consent === true;
      const appLock = await prefs.get(this.appLockKey, false);
      this.appLockEnabled = appLock === true;
      this.lifecycleAppLockEnabled = this.appLockEnabled;
    } catch (error) {
      this.status = `读取应用设置失败：${this.errorText(error)}`;
    }
    this.consentLoaded = true;
    await this.loadAccounts();
    await this.cleanupPendingAliases();
    if (this.privacyAccepted && this.appLockEnabled) {
      await this.unlockApplication();
    } else {
      this.appLocked = false;
      try {
        await this.loadCodesWithoutApplicationLock();
      } catch (error) {
        try {
          await this.migrateAccountSecretAccessToDirectDisplay();
          await this.loadCodesWithoutApplicationLock();
          this.status = '已迁移账户安全策略，验证码现可直接显示';
        } catch (migrationError) {
          this.status = `无法直接显示验证码：${this.errorText(migrationError)}`;
        }
      }
    }
  }

  aboutToDisappear(): void {
    if (this.timerId >= 0) {
      clearInterval(this.timerId);
    }
    this.stopQrTransferAutoAdvance();
  }

  private onLifecycleLockChanged(): void {
    if (!this.lifecycleShouldLock || !this.appLockEnabled) {
      return;
    }
    this.lifecycleShouldLock = false;
    if (!this.appLocked) {
      this.pathStack.clear();
      this.lockApplication();
      this.status = '应用在后台停留超过 60 秒，已自动锁定';
    }
  }

  private bytesFromString(value: string): Uint8Array {
    const bytes = new Uint8Array(value.length);
    for (let index = 0; index < value.length; index++) {
      bytes[index] = value.charCodeAt(index);
    }
    return bytes;
  }

  private stringFromBytes(bytes: Uint8Array): string {
    let value = '';
    for (let index = 0; index < bytes.length; index++) {
      value += String.fromCharCode(bytes[index]);
    }
    return value;
  }

  private errorText(error: Object): string {
    const maybeError = error as Error;
    const maybeBusinessError = error as Record<string, Object>;
    const code = maybeBusinessError['code'];
    const message = maybeBusinessError['message'] ?? maybeError.message;
    const name = maybeError.name;
    return `code=${code ?? 'unknown'}, name=${name ?? 'Error'}, message=${message ?? JSON.stringify(error)}`;
  }

  private normalizedSecret(value: string): string {
    let normalized = '';
    const upper = value.toUpperCase();
    for (let index = 0; index < upper.length; index++) {
      const char = upper[index];
      if ((char >= 'A' && char <= 'Z') || (char >= '2' && char <= '7')) {
        normalized += char;
      }
    }
    return normalized;
  }

  private normalizeSecretInput(value: string): string {
    let normalized = '';
    const upper = value.toUpperCase();
    for (let index = 0; index < upper.length; index++) {
      const char = upper[index];
      if ((char >= 'A' && char <= 'Z') || (char >= '2' && char <= '7')) {
        normalized += char;
      } else if (char === ' ' || char === '-' || char === '=' || char === '\n' || char === '\t' || char === '\r') {
        continue;
      } else {
        throw new Error('Secret contains non-Base32 characters.');
      }
    }
    if (normalized.length === 0) {
      throw new Error('Secret is required.');
    }
    if (this.base32Decode(normalized).length === 0) {
      throw new Error('Secret is too short.');
    }
    return normalized;
  }

  private base32Decode(secret: string): Uint8Array {
    const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
    const output: number[] = [];
    let buffer = 0;
    let bitsLeft = 0;

    for (let index = 0; index < secret.length; index++) {
      const value = alphabet.indexOf(secret[index]);
      if (value < 0) {
        throw new Error('Secret must be Base32.');
      }
      buffer = (buffer << 5) | value;
      bitsLeft += 5;
      if (bitsLeft >= 8) {
        output.push((buffer >> (bitsLeft - 8)) & 0xff);
        bitsLeft -= 8;
      }
    }
    return new Uint8Array(output);
  }

  private counterBytes(counter: number): Uint8Array {
    const bytes = new Uint8Array(8);
    let value = counter;
    for (let index = 7; index >= 0; index--) {
      bytes[index] = value & 0xff;
      value = Math.floor(value / 256);
    }
    return bytes;
  }

  private padCode(value: number, digits: number): string {
    let code = `${value}`;
    while (code.length < digits) {
      code = `0${code}`;
    }
    return code;
  }

  private async hmac(key: Uint8Array, message: Uint8Array, algorithm: string): Promise<Uint8Array> {
    const keyGenerator = cryptoFramework.createSymKeyGenerator('HMAC');
    const symKey = await keyGenerator.convertKey({ data: key });
    const macSpec: cryptoFramework.HmacSpec = { algName: 'HMAC', mdName: algorithm };
    const mac = cryptoFramework.createMac(macSpec);
    await mac.init(symKey);
    await mac.update({ data: message });
    const result = await mac.doFinal();
    return result.data;
  }

  private async generateTotp(
    secret: string,
    timeMs: number,
    period: number,
    digits: number,
    algorithm: string
  ): Promise<string> {
    const key = this.base32Decode(this.normalizedSecret(secret));
    const counter = Math.floor(Math.floor(timeMs / 1000) / period);
    const digest = await this.hmac(key, this.counterBytes(counter), algorithm);
    const offset = digest[digest.length - 1] & 0x0f;
    const binary =
      ((digest[offset] & 0x7f) << 24) |
      ((digest[offset + 1] & 0xff) << 16) |
      ((digest[offset + 2] & 0xff) << 8) |
      (digest[offset + 3] & 0xff);
    const divisor = Math.pow(10, digits);
    return this.padCode(binary % divisor, digits);
  }

  private decodeOtpComponent(value: string): string {
    try {
      return decodeURIComponent(value.replace(/\+/g, ' '));
    } catch (error) {
      throw new Error(`Invalid URL encoding: ${value}`);
    }
  }

  private parseOtpQuery(query: string): Map<string, string> {
    const values = new Map<string, string>();
    if (query.length === 0) {
      return values;
    }
    const parts = query.split('&');
    for (const part of parts) {
      if (part.length === 0) {
        continue;
      }
      const equalsIndex = part.indexOf('=');
      const rawKey = equalsIndex >= 0 ? part.substring(0, equalsIndex) : part;
      const rawValue = equalsIndex >= 0 ? part.substring(equalsIndex + 1) : '';
      values.set(this.decodeOtpComponent(rawKey).toLowerCase(), this.decodeOtpComponent(rawValue));
    }
    return values;
  }

  private parsePositiveInteger(value: string | undefined, fallback: number, fieldName: string): number {
    if (value === undefined || value.trim().length === 0) {
      return fallback;
    }
    const parsed = Number(value);
    if (!Number.isInteger(parsed) || parsed <= 0) {
      throw new Error(`${fieldName} must be a positive integer.`);
    }
    return parsed;
  }

  private parseOtpAuthUrl(value: string): OtpAuthAccount {
    const trimmed = value.trim();
    const prefix = 'otpauth://';
    if (!trimmed.toLowerCase().startsWith(prefix)) {
      throw new Error('URL must start with otpauth://.');
    }

    const body = trimmed.substring(prefix.length);
    const queryIndex = body.indexOf('?');
    const path = queryIndex >= 0 ? body.substring(0, queryIndex) : body;
    const query = queryIndex >= 0 ? body.substring(queryIndex + 1) : '';
    const slashIndex = path.indexOf('/');
    if (slashIndex < 0) {
      throw new Error('otpauth URL is missing account label.');
    }

    const otpType = path.substring(0, slashIndex).toLowerCase();
    if (otpType !== 'totp') {
      throw new Error('Only TOTP otpauth URLs are supported.');
    }

    const label = this.decodeOtpComponent(path.substring(slashIndex + 1));
    const queryValues = this.parseOtpQuery(query);
    const rawSecret = queryValues.get('secret');
    if (rawSecret === undefined || rawSecret.trim().length === 0) {
      throw new Error('otpauth URL is missing secret.');
    }

    const algorithm = (queryValues.get('algorithm') ?? 'SHA1').toUpperCase();
    if (algorithm !== 'SHA1' && algorithm !== 'SHA256' && algorithm !== 'SHA512') {
      throw new Error(`Unsupported TOTP algorithm: ${algorithm}.`);
    }

    const labelSeparator = label.indexOf(':');
    const labelIssuer = labelSeparator >= 0 ? label.substring(0, labelSeparator).trim() : '';
    const labelName = labelSeparator >= 0 ? label.substring(labelSeparator + 1).trim() : label.trim();
    const issuer = (queryValues.get('issuer') ?? labelIssuer).trim();
    const name = labelName.length > 0 ? labelName : 'Imported account';
    const digits = this.parsePositiveInteger(queryValues.get('digits'), 6, 'digits');
    const period = this.parsePositiveInteger(queryValues.get('period'), 30, 'period');

    if (digits < 6 || digits > 8) {
      throw new Error('digits must be between 6 and 8.');
    }

    return {
      issuer: issuer.length > 0 ? issuer : 'Imported',
      name,
      secret: this.normalizeSecretInput(rawSecret),
      algorithm,
      digits,
      period
    };
  }

  private secondsRemaining(): number {
    return 30 - (Math.floor(this.nowMs / 1000) % 30);
  }

  private accountAssetQuery(alias: string): asset.AssetMap {
    const query: asset.AssetMap = new Map<asset.Tag, asset.Value>();
    query.set(asset.Tag.ALIAS, this.bytesFromString(alias));
    return query;
  }

  private async saveSecret(alias: string, secret: string): Promise<void> {
    try {
      await asset.remove(this.accountAssetQuery(alias));
    } catch (error) {
      // Missing assets are expected when adding a new account.
    }

    await this.addSecret(alias, secret, this.appLockEnabled ? asset.AuthType.ANY : asset.AuthType.NONE);
  }

  private async addSecret(alias: string, secret: string, authType: asset.AuthType): Promise<void> {
    const attributes = this.accountAssetQuery(alias);
    attributes.set(asset.Tag.SECRET, this.bytesFromString(secret));
    attributes.set(asset.Tag.ACCESSIBILITY, asset.Accessibility.DEVICE_UNLOCKED);
    attributes.set(asset.Tag.REQUIRE_PASSWORD_SET, true);
    attributes.set(asset.Tag.AUTH_TYPE, authType);
    await asset.add(attributes);
  }

  private async readDirectSecret(alias: string): Promise<string> {
    const query = this.accountAssetQuery(alias);
    query.set(asset.Tag.RETURN_TYPE, asset.ReturnType.ALL);
    const results = await asset.query(query);
    const secret = results.length > 0 ? results[0].get(asset.Tag.SECRET) : undefined;
    if (secret instanceof Uint8Array) {
      return this.stringFromBytes(secret);
    }
    throw new Error('Protected secret is missing.');
  }

  private async readProtectedSecret(alias: string, title: string): Promise<string> {
    if (!this.appLockEnabled) {
      await this.requestBiometricPermission();
      await this.authenticate(cryptoFramework.createRandom().generateRandomSync(32).data, title);
      return this.readDirectSecret(alias);
    }
    const query = this.accountAssetQuery(alias);
    const challenge = await asset.preQuery(query);
    const token = await this.authenticate(challenge, title);
    query.set(asset.Tag.AUTH_CHALLENGE, challenge);
    query.set(asset.Tag.AUTH_TOKEN, token);
    query.set(asset.Tag.RETURN_TYPE, asset.ReturnType.ALL);
    const results = await asset.query(query);
    const handle: asset.AssetMap = new Map<asset.Tag, asset.Value>();
    handle.set(asset.Tag.AUTH_CHALLENGE, challenge);
    await asset.postQuery(handle);

    const secret = results.length > 0 ? results[0].get(asset.Tag.SECRET) : undefined;
    if (secret instanceof Uint8Array) {
      return this.stringFromBytes(secret);
    }
    throw new Error('Protected secret is missing.');
  }

  private async loadCodesWithoutApplicationLock(): Promise<void> {
    const unlockedAccounts: MfaAccount[] = [];
    for (const account of this.accounts) {
      const secret = await this.readDirectSecret(account.secretAlias);
      this.sessionSecrets.set(account.id, secret);
      unlockedAccounts.push({
        id: account.id,
        issuer: account.issuer,
        name: account.name,
        secretAlias: account.secretAlias,
        algorithm: account.algorithm,
        digits: account.digits,
        period: account.period,
        group: account.group,
        color: account.color,
        createdAt: account.createdAt,
        code: await this.generateTotp(secret, this.nowMs, account.period, account.digits, account.algorithm),
        revealedSecret: ''
      });
    }
    this.accounts = unlockedAccounts;
  }

  private migratedSecretAlias(account: MfaAccount, policy: string): string {
    return `mfa-${account.id}-${policy}-${Date.now()}`;
  }

  private async readSecretsWithAssetAuthentication(title: string): Promise<Map<string, string>> {
    const secrets = new Map<string, string>();
    if (this.accounts.length === 0) {
      return secrets;
    }

    await this.requestBiometricPermission();
    const challenge = await asset.preQuery(this.accountAssetQuery(this.accounts[0].secretAlias));
    try {
      const token = await this.authenticate(challenge, title);
      for (const account of this.accounts) {
        const query = this.accountAssetQuery(account.secretAlias);
        query.set(asset.Tag.AUTH_CHALLENGE, challenge);
        query.set(asset.Tag.AUTH_TOKEN, token);
        query.set(asset.Tag.RETURN_TYPE, asset.ReturnType.ALL);
        const results = await asset.query(query);
        const value = results.length > 0 ? results[0].get(asset.Tag.SECRET) : undefined;
        if (!(value instanceof Uint8Array)) {
          throw new Error('Protected secret is missing.');
        }
        secrets.set(account.secretAlias, this.stringFromBytes(value));
      }
    } finally {
      const handle: asset.AssetMap = new Map<asset.Tag, asset.Value>();
      handle.set(asset.Tag.AUTH_CHALLENGE, challenge);
      await asset.postQuery(handle);
    }
    return secrets;
  }

  private async migrateAccountSecretAccessToDirectDisplay(): Promise<void> {
    const secrets = await this.readSecretsWithAssetAuthentication('关闭应用锁并直接显示验证码');
    const migratedAccounts: MfaAccount[] = [];
    const createdAliases: string[] = [];
    const oldAliases: string[] = [];
    let committed = false;
    try {
      for (const account of this.accounts) {
        const secret = secrets.get(account.secretAlias);
        if (secret === undefined) {
          throw new Error('Protected secret is missing.');
        }
        const secretAlias = this.migratedSecretAlias(account, 'direct');
        await this.addSecret(secretAlias, secret, asset.AuthType.NONE);
        createdAliases.push(secretAlias);
        oldAliases.push(account.secretAlias);
        migratedAccounts.push({
          id: account.id,
          issuer: account.issuer,
          name: account.name,
          secretAlias,
          algorithm: account.algorithm,
          digits: account.digits,
          period: account.period,
          group: account.group,
          color: account.color,
          createdAt: account.createdAt,
          code: '',
          revealedSecret: ''
        });
      }
      await this.saveAccountRecords(migratedAccounts, false);
      committed = true;
      this.accounts = migratedAccounts;
      await this.cleanupAliases(oldAliases);
    } catch (error) {
      if (!committed) {
        await this.cleanupAliases(createdAliases);
      }
      throw new Error(this.errorText(error as Object));
    }
  }

  private async migrateAccountSecretAccessToApplicationLock(): Promise<void> {
    const secrets = await this.readAllProtectedSecrets('开启应用锁并保护验证码');
    const migratedAccounts: MfaAccount[] = [];
    const createdAliases: string[] = [];
    const oldAliases: string[] = [];
    let committed = false;
    try {
      for (const account of this.accounts) {
        const secret = secrets.get(account.secretAlias);
        if (secret === undefined) {
          throw new Error('Protected secret is missing.');
        }
        const secretAlias = this.migratedSecretAlias(account, 'locked');
        await this.addSecret(secretAlias, secret, asset.AuthType.ANY);
        createdAliases.push(secretAlias);
        oldAliases.push(account.secretAlias);
        migratedAccounts.push({
          id: account.id,
          issuer: account.issuer,
          name: account.name,
          secretAlias,
          algorithm: account.algorithm,
          digits: account.digits,
          period: account.period,
          group: account.group,
          color: account.color,
          createdAt: account.createdAt,
          code: '',
          revealedSecret: ''
        });
      }
      await this.saveAccountRecords(migratedAccounts, true);
      committed = true;
      this.sessionSecrets.clear();
      this.accounts = migratedAccounts;
      await this.cleanupAliases(oldAliases);
    } catch (error) {
      if (!committed) {
        await this.cleanupAliases(createdAliases);
      }
      throw new Error(this.errorText(error as Object));
    }
  }

  private async copyText(value: string, clearAfterMs: number, clearedStatus: string): Promise<void> {
    const systemPasteboard = pasteboard.getSystemPasteboard();
    const data = pasteboard.createData(pasteboard.MIMETYPE_TEXT_PLAIN, value);
    const property = data.getProperty();
    property.shareOption = pasteboard.ShareOption.LOCALDEVICE;
    data.setProperty(property);
    let ownUpdateObserved = false;
    let changedAfterOwnUpdate = false;
    const updateCallback = (): void => {
      if (!ownUpdateObserved) {
        ownUpdateObserved = true;
        return;
      }
      changedAfterOwnUpdate = true;
    };
    systemPasteboard.on('update', updateCallback);
    try {
      await systemPasteboard.setData(data);
    } catch (error) {
      systemPasteboard.off('update', updateCallback);
      throw new Error(this.errorText(error));
    }
    this.clipboardGeneration += 1;
    const generation = this.clipboardGeneration;
    setTimeout(() => {
      systemPasteboard.off('update', updateCallback);
      if (!ownUpdateObserved || changedAfterOwnUpdate || generation !== this.clipboardGeneration) {
        return;
      }
      void systemPasteboard.clearData().then(() => {
        if (generation === this.clipboardGeneration) {
          this.clipboardGeneration += 1;
          this.status = clearedStatus;
        }
      }).catch(() => {
        // Clipboard can be unavailable after the app enters the background.
      });
    }, clearAfterMs);
  }

  private async requestBiometricPermission(): Promise<void> {
    const atManager = abilityAccessCtrl.createAtManager();
    const context = getContext(this) as common.UIAbilityContext;
    const permissions: Permissions[] = ['ohos.permission.ACCESS_BIOMETRIC'];
    const result = await atManager.requestPermissionsFromUser(context, permissions);
    this.permissionResult = `permissions=${result.permissions.join(', ')}, authResults=${result.authResults.join(', ')}`;
  }

  private async requestCameraPermission(): Promise<boolean> {
    const atManager = abilityAccessCtrl.createAtManager();
    const context = getContext(this) as common.UIAbilityContext;
    const permissions: Permissions[] = ['ohos.permission.CAMERA'];
    const result = await atManager.requestPermissionsFromUser(context, permissions);
    return result.authResults.length > 0 &&
      result.authResults[0] === abilityAccessCtrl.GrantStatus.PERMISSION_GRANTED;
  }

  private authTypeName(authType: userAuth.UserAuthType): string {
    switch (authType) {
      case userAuth.UserAuthType.PIN:
        return 'PIN';
      case userAuth.UserAuthType.FINGERPRINT:
        return 'FINGERPRINT';
      case userAuth.UserAuthType.FACE:
        return 'FACE';
      default:
        return `${authType}`;
    }
  }

  private trustLevelName(authTrustLevel: userAuth.AuthTrustLevel): string {
    switch (authTrustLevel) {
      case userAuth.AuthTrustLevel.ATL3:
        return 'ATL3';
      case userAuth.AuthTrustLevel.ATL2:
        return 'ATL2';
      case userAuth.AuthTrustLevel.ATL1:
        return 'ATL1';
      default:
        return `${authTrustLevel}`;
    }
  }

  private availableAuthSelection(): AuthSelection {
    const candidates: userAuth.UserAuthType[] = [
      userAuth.UserAuthType.FINGERPRINT,
      userAuth.UserAuthType.FACE,
      userAuth.UserAuthType.PIN
    ];
    const trustLevels: userAuth.AuthTrustLevel[] = [
      userAuth.AuthTrustLevel.ATL3,
      userAuth.AuthTrustLevel.ATL2
    ];
    const failures: string[] = [];
    let strongestFallback: AuthSelection | undefined = undefined;

    for (const trustLevel of trustLevels) {
      const availableTypes: userAuth.UserAuthType[] = [];
      for (const candidate of candidates) {
        try {
          userAuth.getAvailableStatus(candidate, trustLevel);
          availableTypes.push(candidate);
        } catch (error) {
          failures.push(`${this.authTypeName(candidate)} ${this.trustLevelName(trustLevel)}: ${this.errorText(error)}`);
        }
      }
      if (availableTypes.length > 0) {
        const selection: AuthSelection = {
          authTypes: availableTypes,
          authTrustLevel: trustLevel,
          details: `${this.trustLevelName(trustLevel)} with ${availableTypes
            .map((type: userAuth.UserAuthType): string => this.authTypeName(type)).join(', ')}`
        };
        if (strongestFallback === undefined) {
          strongestFallback = selection;
        }
        if (availableTypes.includes(userAuth.UserAuthType.FACE)) {
          return selection;
        }
      }
    }

    if (strongestFallback !== undefined) {
      return strongestFallback;
    }

    throw new Error(`No available system authentication. ${failures.join(' | ')}`);
  }

  private authenticate(challenge: Uint8Array, title: string): Promise<Uint8Array> {
    return new Promise<Uint8Array>((resolve, reject) => {
      let authSelection: AuthSelection;
      try {
        authSelection = this.availableAuthSelection();
      } catch (error) {
        reject(error as Error);
        return;
      }
      const authParam: userAuth.AuthParam = {
        challenge,
        authType: authSelection.authTypes,
        authTrustLevel: authSelection.authTrustLevel
      };
      const widgetParam: userAuth.WidgetParam = {
        title
      };
      const hostContext = this.getUIContext().getHostContext();
      if (hostContext !== undefined) {
        widgetParam.uiContext = hostContext;
      }
      const authInstance = userAuth.getUserAuthInstance(authParam, widgetParam);
      const callback: userAuth.IAuthCallback = {
        onResult: (result: userAuth.UserAuthResult): void => {
          authInstance.off('result', callback);
          if (result.result === userAuth.UserAuthResultCode.SUCCESS && result.token !== undefined) {
            resolve(result.token);
            return;
          }
          reject(new Error(`Authentication failed: ${result.result}`));
        }
      };
      authInstance.on('result', callback);
      try {
        authInstance.start();
      } catch (error) {
        authInstance.off('result', callback);
        reject(error as Error);
      }
    });
  }

  private prefsContext(): common.UIAbilityContext {
    return getContext(this) as common.UIAbilityContext;
  }

  private async getPreferences(): Promise<preferences.Preferences> {
    return preferences.getPreferences(this.prefsContext(), this.preferencesName);
  }

  private recordToAccount(record: AccountRecord): MfaAccount {
    if (record === undefined || typeof record.id !== 'string' || record.id.length === 0 ||
      typeof record.issuer !== 'string' || typeof record.name !== 'string' || record.name.trim().length === 0 ||
      typeof record.secretAlias !== 'string' || record.secretAlias.length === 0) {
      throw new Error('Account record is invalid.');
    }
    const algorithm = typeof record.algorithm === 'string' ? record.algorithm.toUpperCase() : 'SHA1';
    const digits = record.digits ?? 6;
    const period = record.period ?? 30;
    if ((algorithm !== 'SHA1' && algorithm !== 'SHA256' && algorithm !== 'SHA512') ||
      !Number.isInteger(digits) || digits < 6 || digits > 8 ||
      !Number.isInteger(period) || period < 1 || period > 300) {
      throw new Error('Account record contains unsupported TOTP parameters.');
    }
    const numericId = Number(record.id.split('-')[0]);
    return {
      id: record.id,
      issuer: record.issuer,
      name: record.name,
      secretAlias: record.secretAlias,
      algorithm,
      digits,
      period,
      group: record.group ?? '',
      color: record.color ?? 'blue',
      createdAt: record.createdAt ?? (Number.isFinite(numericId) ? numericId : Date.now()),
      code: '',
      revealedSecret: ''
    };
  }

  private accountToRecord(account: MfaAccount): AccountRecord {
    return {
      id: account.id,
      issuer: account.issuer,
      name: account.name,
      secretAlias: account.secretAlias,
      algorithm: account.algorithm,
      digits: account.digits,
      period: account.period,
      group: account.group,
      color: account.color,
      createdAt: account.createdAt
    };
  }

  private async loadAccounts(): Promise<void> {
    this.accountsWritable = false;
    try {
      const prefs = await this.getPreferences();
      const raw = await prefs.get(this.accountsKey, '{"version":1,"accounts":[]}');
      if (typeof raw !== 'string') {
        throw new Error('Account storage is not text.');
      }
      const parsed = JSON.parse(raw) as AccountRecord[] | AccountStorePayload;
      const legacy = Array.isArray(parsed);
      const records = legacy ? parsed as AccountRecord[] : (parsed as AccountStorePayload).accounts;
      if ((!legacy && (parsed as AccountStorePayload).version !== 1) || !Array.isArray(records)) {
        throw new Error('Unsupported account storage version.');
      }
      const loaded: MfaAccount[] = [];
      for (const record of records) {
        loaded.push(this.recordToAccount(record));
      }
      this.accounts = loaded;
      this.accountsWritable = true;
      if (legacy) {
        await this.saveAccountRecords(loaded);
      }
      this.status = this.accounts.length > 0 ? `已安全加载 ${this.accounts.length} 个账户` : '还没有账户，点击右上角添加';
    } catch (error) {
      this.accountsWritable = false;
      this.status = `账户加载失败，已暂停所有修改以保护旧数据：${this.errorText(error)}`;
    }
  }

  private async saveAccountRecords(accounts?: MfaAccount[], appLockValue?: boolean): Promise<void> {
    if (!this.accountsWritable) {
      throw new Error('Account storage is unavailable; writes are blocked to protect existing data.');
    }
    const records: AccountRecord[] = [];
    const source = accounts ?? this.accounts;
    for (const account of source) {
      records.push(this.accountToRecord(account));
    }
    const prefs = await this.getPreferences();
    const payload: AccountStorePayload = { version: 1, accounts: records };
    await prefs.put(this.accountsKey, JSON.stringify(payload));
    if (appLockValue !== undefined) {
      await prefs.put(this.appLockKey, appLockValue);
    }
    await prefs.flush();
  }

  private async cleanupAliases(aliases: string[]): Promise<void> {
    const prefs = await this.getPreferences();
    let pending: string[] = [];
    try {
      const raw = await prefs.get(this.pendingAliasCleanupKey, '[]');
      if (typeof raw === 'string') {
        const parsed = JSON.parse(raw) as string[];
        if (Array.isArray(parsed)) {
          pending = parsed.filter((value: string): boolean => typeof value === 'string');
        }
      }
    } catch (error) {
      pending = [];
    }
    const remaining: string[] = [];
    const candidates = Array.from(new Set<string>(pending.concat(aliases)));
    for (const alias of candidates) {
      try {
        await asset.remove(this.accountAssetQuery(alias));
      } catch (error) {
        remaining.push(alias);
      }
    }
    try {
      await prefs.put(this.pendingAliasCleanupKey, JSON.stringify(remaining));
      await prefs.flush();
    } catch (error) {
      // Cleanup metadata is best effort and must never roll back committed account records.
    }
  }

  private async cleanupPendingAliases(): Promise<void> {
    try {
      await this.cleanupAliases([]);
    } catch (error) {
      // Account availability takes priority over orphan cleanup.
    }
  }

  private updateAccount(id: string, code: string, revealedSecret: string): void {
    const next: MfaAccount[] = [];
    for (const account of this.accounts) {
      if (account.id === id) {
        next.push({
          id: account.id,
          issuer: account.issuer,
          name: account.name,
          secretAlias: account.secretAlias,
          algorithm: account.algorithm,
          digits: account.digits,
          period: account.period,
          group: account.group,
          color: account.color,
          createdAt: account.createdAt,
          code,
          revealedSecret
        });
      } else {
        next.push(account);
      }
    }
    this.accounts = next;
  }

  private replaceAccount(updated: MfaAccount): void {
    const next: MfaAccount[] = [];
    for (const account of this.accounts) {
      next.push(account.id === updated.id ? updated : account);
    }
    this.accounts = next;
  }

  private async saveProtectedAccount(input: OtpAuthAccount, successStatus: string): Promise<void> {
    const id = `${Date.now()}`;
    const secretAlias = `${this.assetPrefix}${id}`;
    let secretCreated = false;
    try {
      await this.saveSecret(secretAlias, input.secret);
      secretCreated = true;
      const code = await this.generateTotp(input.secret, this.nowMs, input.period, input.digits, input.algorithm);
      const account: MfaAccount = {
        id,
        issuer: input.issuer,
        name: input.name,
        secretAlias,
        algorithm: input.algorithm,
        digits: input.digits,
        period: input.period,
        group: input.group ?? '',
        color: input.color ?? 'blue',
        createdAt: input.createdAt ?? Date.now(),
        code,
        revealedSecret: ''
      };
      const updatedAccounts = [account, ...this.accounts];
      await this.saveAccountRecords(updatedAccounts);
      this.sessionSecrets.set(id, input.secret);
      this.accounts = updatedAccounts;
      this.status = successStatus;
    } catch (error) {
      if (secretCreated) {
        await this.cleanupAliases([secretAlias]);
      }
      throw new Error(this.errorText(error as Object));
    }
  }

  private async addAccount(): Promise<void> {
    this.isRunning = true;
    this.status = '正在安全保存账户…';
    try {
      const issuer = this.issuerInput.trim();
      const name = this.nameInput.trim();
      if (issuer.length === 0 || name.length === 0) {
        throw new Error('Issuer and account are required.');
      }
      await this.saveProtectedAccount({
        issuer,
        name,
        secret: this.normalizeSecretInput(this.secretInput),
        algorithm: 'SHA1',
        digits: 6,
        period: 30,
        group: this.normalizedGroupValue(this.groupInput, this.addCreatingGroup),
        color: this.colorInput,
        createdAt: Date.now()
      }, '账户已保存到系统安全存储');
      this.issuerInput = '';
      this.nameInput = '';
      this.secretInput = '';
      this.groupInput = '';
      this.addCreatingGroup = false;
      this.colorInput = 'blue';
      this.pathStack.pop();
    } catch (error) {
      this.status = `添加失败：${this.errorText(error)}`;
    }
    this.isRunning = false;
  }

  private async importOtpAuthUrl(): Promise<void> {
    this.isRunning = true;
    this.status = '正在导入 otpauth 链接…';
    try {
      const imported = this.parseOtpAuthUrl(this.otpauthInput);
      imported.group = this.normalizedGroupValue(this.groupInput, this.addCreatingGroup);
      imported.color = this.colorInput;
      await this.saveProtectedAccount(imported, 'otpauth 账户已导入系统安全存储');
      this.issuerInput = imported.issuer;
      this.nameInput = imported.name;
      this.secretInput = '';
      this.otpauthInput = '';
      this.groupInput = '';
      this.addCreatingGroup = false;
      this.colorInput = 'blue';
      this.pathStack.pop();
    } catch (error) {
      this.status = `链接导入失败：${this.errorText(error)}`;
    }
    this.isRunning = false;
  }

  private async scanOtpAuthQr(): Promise<void> {
    this.isRunning = true;
    this.status = '正在打开系统扫码服务…';
    this.otpauthInput = '';
    try {
      const hostContext = this.getUIContext().getHostContext();
      if (hostContext === undefined) {
        throw new Error('Cannot open scanner without an ArkUI host context.');
      }
      const options: scanBarcode.ScanOptions = {
        scanTypes: [scanCore.ScanType.QR_CODE],
        enableMultiMode: false,
        enableAlbum: false
      };
      const result = await scanBarcode.startScanForResult(hostContext, options);
      const scannedValue = result.originalValue.trim();
      if (scannedValue.length === 0) {
        throw new Error('Scanned QR code is empty.');
      }
      const imported = this.parseOtpAuthUrl(scannedValue);
      imported.group = this.normalizedGroupValue(this.groupInput, this.addCreatingGroup);
      imported.color = this.colorInput;
      await this.saveProtectedAccount(imported, '二维码账户已导入系统安全存储');
      this.issuerInput = imported.issuer;
      this.nameInput = imported.name;
      this.secretInput = '';
      this.groupInput = '';
      this.addCreatingGroup = false;
      this.colorInput = 'blue';
      this.pathStack.pop();
    } catch (error) {
      const maybeBusinessError = error as Record<string, Object>;
      if (maybeBusinessError['code'] === scanCore.ScanErrorCode.SCAN_SERVICE_CANCELED) {
        this.status = '已取消扫码';
      } else {
        this.status = `扫码失败：${this.errorText(error)}`;
      }
    }
    this.otpauthInput = '';
    this.isRunning = false;
  }

  private async unlockCode(account: MfaAccount): Promise<void> {
    this.isRunning = true;
    this.status = '正在通过系统身份验证解锁验证码…';
    try {
      await this.requestBiometricPermission();
      const secret = await this.readProtectedSecret(account.secretAlias, `生成 ${account.issuer} 验证码`);
      this.sessionSecrets.set(account.id, secret);
      const code = await this.generateTotp(secret, this.nowMs, account.period, account.digits, account.algorithm);
      this.updateAccount(account.id, code, account.revealedSecret);
      this.status = '验证码已安全解锁';
    } catch (error) {
      this.status = `解锁失败：${this.errorText(error)}`;
    }
    this.isRunning = false;
  }

  private async revealSecret(account: MfaAccount): Promise<void> {
    this.isRunning = true;
    this.status = '查看密钥前需要系统身份验证…';
    try {
      await this.requestBiometricPermission();
      const secret = await this.readProtectedSecret(account.secretAlias, `查看 ${account.issuer} 密钥`);
      this.updateAccount(account.id, account.code, secret);
      this.status = '密钥已显示';
    } catch (error) {
      this.status = `查看失败：${this.errorText(error)}`;
    }
    this.isRunning = false;
  }

  private hideSecret(account: MfaAccount): void {
    this.updateAccount(account.id, account.code, '');
    this.status = '密钥已隐藏';
  }

  private startEdit(account: MfaAccount): void {
    this.editingAccountId = account.id;
    this.editIssuerInput = account.issuer;
    this.editNameInput = account.name;
    this.editGroupInput = account.group;
    this.editCreatingGroup = false;
    this.editColorInput = account.color;
    this.status = '正在编辑账户';
  }

  private cancelEdit(): void {
    this.editingAccountId = '';
    this.editIssuerInput = '';
    this.editNameInput = '';
    this.editGroupInput = '';
    this.editCreatingGroup = false;
    this.editColorInput = 'blue';
    this.status = '已取消编辑';
  }

  private async saveEditedAccount(account: MfaAccount): Promise<void> {
    this.isRunning = true;
    try {
      const issuer = this.editIssuerInput.trim();
      const name = this.editNameInput.trim();
      if (issuer.length === 0 || name.length === 0) {
        throw new Error('Issuer and account name are required.');
      }
      const updatedAccount: MfaAccount = {
        id: account.id,
        issuer,
        name,
        secretAlias: account.secretAlias,
        algorithm: account.algorithm,
        digits: account.digits,
        period: account.period,
        group: this.normalizedGroupValue(this.editGroupInput, this.editCreatingGroup),
        color: this.editColorInput,
        createdAt: account.createdAt,
        code: account.code,
        revealedSecret: account.revealedSecret
      };
      const updatedAccounts: MfaAccount[] = [];
      for (const item of this.accounts) {
        updatedAccounts.push(item.id === account.id ? updatedAccount : item);
      }
      await this.saveAccountRecords(updatedAccounts);
      this.accounts = [...updatedAccounts];
      this.editingAccountId = '';
      this.editIssuerInput = '';
      this.editNameInput = '';
      this.editGroupInput = '';
      this.editCreatingGroup = false;
      this.editColorInput = 'blue';
      this.status = '账户信息已保存';
      this.pathStack.pop();
    } catch (error) {
      this.status = `保存失败：${this.errorText(error)}`;
    }
    this.isRunning = false;
  }

  private async deleteAccount(account: MfaAccount): Promise<void> {
    this.isRunning = true;
    try {
      const next: MfaAccount[] = [];
      for (const item of this.accounts) {
        if (item.id !== account.id) {
          next.push(item);
        }
      }
      await this.saveAccountRecords(next);
      this.accounts = next;
      this.sessionSecrets.delete(account.id);
      if (this.editingAccountId === account.id) {
        this.cancelEdit();
      }
      await this.cleanupAliases([account.secretAlias]);
      this.status = '账户及其安全密钥已删除';
    } catch (error) {
      this.status = `删除失败：${this.errorText(error)}`;
    }
    this.isRunning = false;
  }

  private async copyCode(account: MfaAccount): Promise<void> {
    this.isRunning = true;
    try {
      let code = account.code;
      const secret = this.sessionSecrets.get(account.id);
      if (code.length === 0 && secret !== undefined) {
        code = await this.generateTotp(secret, this.nowMs, account.period, account.digits, account.algorithm);
        this.updateAccount(account.id, code, account.revealedSecret);
      }
      if (code.length === 0) {
        throw new Error('请先解锁验证码。');
      }
      await this.copyText(code, this.codeClipboardTtlMs, '验证码已从剪贴板自动清除');
      this.status = '验证码已复制，30 秒后自动清除';
      this.copiedAccountId = account.id;
      setTimeout(() => {
        if (this.copiedAccountId === account.id) {
          this.copiedAccountId = '';
        }
      }, 1100);
    } catch (error) {
      this.status = `复制验证码失败：${this.errorText(error)}`;
    }
    this.isRunning = false;
  }

  private async copySecret(account: MfaAccount): Promise<void> {
    this.isRunning = true;
    try {
      let secret = account.revealedSecret;
      if (secret.length === 0) {
        this.status = '复制密钥前需要系统身份验证…';
        await this.requestBiometricPermission();
        secret = await this.readProtectedSecret(account.secretAlias, `复制 ${account.issuer} 密钥`);
      }
      await this.copyText(secret, this.sensitiveClipboardTtlMs, '敏感内容已从剪贴板自动清除');
      this.status = '密钥已复制，60 秒后自动清除';
    } catch (error) {
      this.status = `复制密钥失败：${this.errorText(error)}`;
    }
    this.isRunning = false;
  }

  private async readAllProtectedSecrets(title: string): Promise<Map<string, string>> {
    const secrets = new Map<string, string>();
    if (this.accounts.length === 0) {
      return secrets;
    }
    if (!this.appLockEnabled) {
      await this.requestBiometricPermission();
      await this.authenticate(cryptoFramework.createRandom().generateRandomSync(32).data, title);
      for (const account of this.accounts) {
        const secret = await this.readDirectSecret(account.secretAlias);
        secrets.set(account.secretAlias, secret);
        this.sessionSecrets.set(account.id, secret);
      }
      return secrets;
    }
    const challenge = await asset.preQuery(this.accountAssetQuery(this.accounts[0].secretAlias));
    try {
      const token = await this.authenticate(challenge, title);
      for (const account of this.accounts) {
        const query = this.accountAssetQuery(account.secretAlias);
        query.set(asset.Tag.AUTH_CHALLENGE, challenge);
        query.set(asset.Tag.AUTH_TOKEN, token);
        query.set(asset.Tag.RETURN_TYPE, asset.ReturnType.ALL);
        const results = await asset.query(query);
        const value = results.length > 0 ? results[0].get(asset.Tag.SECRET) : undefined;
        if (!(value instanceof Uint8Array)) {
          throw new Error(`Protected secret is missing for ${account.issuer}.`);
        }
        const secret = this.stringFromBytes(value);
        secrets.set(account.secretAlias, secret);
        this.sessionSecrets.set(account.id, secret);
      }
    } finally {
      const handle: asset.AssetMap = new Map<asset.Tag, asset.Value>();
      handle.set(asset.Tag.AUTH_CHALLENGE, challenge);
      await asset.postQuery(handle);
    }
    return secrets;
  }

  private async prepareEncryptedBackup(): Promise<void> {
    this.isRunning = true;
    this.status = '导出加密备份前需要系统身份验证…';
    try {
      if (this.accounts.length === 0) {
        throw new Error('请至少添加一个账户后再导出。');
      }
      await verifyMobileV2Compatibility();
      await this.requestBiometricPermission();
      const secrets = await this.readAllProtectedSecrets('导出留白 MFA 加密备份');
      const portableAccounts: PortableAccount[] = [];
      for (const account of this.accounts) {
        const secret = secrets.get(account.secretAlias);
        if (secret === undefined) {
          throw new Error(`${account.issuer} 的安全密钥不存在。`);
        }
        const numericId = Number(account.id);
        portableAccounts.push({
          id: account.id,
          issuer: account.issuer,
          account: account.name,
          secret,
          createdAt: account.createdAt > 0 ? account.createdAt : (Number.isFinite(numericId) ? numericId : Date.now()),
          algorithm: account.algorithm,
          digits: account.digits,
          period: account.period,
          group: account.group.length > 0 ? account.group : undefined,
          color: account.color.length > 0 ? account.color : undefined
        });
      }
      const key = generateRecoveryKey();
      const payload: SecureBackupPayload = {
        version: 2,
        created: Date.now(),
        encrypted: true,
        keyFormat: 'base64url-256',
        data: await encryptString(JSON.stringify(portableAccounts), key)
      };
      this.recoveryKey = key;
      this.preparedBackup = JSON.stringify(payload);
      this.recoveryKeyCopied = false;
      this.status = '加密备份已准备好，请先复制恢复密钥再保存或分享';
    } catch (error) {
      this.recoveryKey = '';
      this.preparedBackup = '';
      this.recoveryKeyCopied = false;
      this.status = `准备备份失败：${this.errorText(error)}`;
    }
    this.isRunning = false;
  }

  private async copyRecoveryKey(): Promise<void> {
    if (this.recoveryKey.length === 0) {
      return;
    }
    await this.copyText(
      formatRecoveryKey(this.recoveryKey),
      this.sensitiveClipboardTtlMs,
      '敏感内容已从剪贴板自动清除'
    );
    this.recoveryKeyCopied = true;
    this.status = '恢复密钥已复制，60 秒后自动清除；请与备份文件分开保存';
  }

  private async savePreparedBackup(): Promise<void> {
    this.isRunning = true;
    try {
      if (this.preparedBackup.length === 0 || !this.recoveryKeyCopied) {
        throw new Error('请先复制恢复密钥。');
      }
      const documentPicker = new picker.DocumentViewPicker(this.prefsContext());
      const options = new picker.DocumentSaveOptions();
      options.newFileNames = [`mfa-verifier-backup-${Date.now()}.json`];
      options.fileSuffixChoices = ['JSON backup|.json'];
      const uris = await documentPicker.save(options);
      if (uris.length === 0) {
        throw new Error('未选择备份保存位置。');
      }
      const file = await fileIo.open(
        uris[0],
        fileIo.OpenMode.READ_WRITE | fileIo.OpenMode.CREATE | fileIo.OpenMode.TRUNC
      );
      try {
        await fileIo.write(file.fd, this.preparedBackup);
      } finally {
        await fileIo.close(file);
      }
      this.status = '加密备份已保存，文件中不包含恢复密钥';
    } catch (error) {
      this.status = `保存备份失败：${this.errorText(error)}`;
    }
    this.isRunning = false;
  }

  private async sharePreparedBackup(): Promise<void> {
    this.isRunning = true;
    try {
      if (this.preparedBackup.length === 0 || !this.recoveryKeyCopied) {
        throw new Error('请先复制恢复密钥。');
      }
      const record: systemShare.SharedRecord = {
        utd: uniformTypeDescriptor.UniformDataType.PLAIN_TEXT,
        content: this.preparedBackup,
        title: '留白 MFA 加密备份',
        description: '不包含恢复密钥的加密备份'
      };
      const data = new systemShare.SharedData(record);
      const controller = new systemShare.ShareController(data);
      const options: systemShare.ShareControllerOptions = {};
      await controller.show(this.prefsContext(), options);
      this.status = '已打开系统分享面板';
    } catch (error) {
      this.status = `分享备份失败：${this.errorText(error)}`;
    }
    this.isRunning = false;
  }

  private clearPreparedBackup(): void {
    this.recoveryKey = '';
    this.preparedBackup = '';
    this.recoveryKeyCopied = false;
    this.status = '本次会话中的备份和恢复密钥已清除';
  }

  private async chooseBackupFile(): Promise<void> {
    this.isRunning = true;
    try {
      const documentPicker = new picker.DocumentViewPicker(this.prefsContext());
      const options = new picker.DocumentSelectOptions();
      options.maxSelectNumber = 1;
      options.fileSuffixFilters = ['JSON backup|.json'];
      const uris = await documentPicker.select(options);
      if (uris.length === 0) {
        throw new Error('未选择备份文件。');
      }
      const text = await this.readSelectedFile(uris[0]);
      if (text.length === 0 || text.length > 1024 * 1024) {
        throw new Error('Backup file is empty or exceeds the 1 MB limit.');
      }
      this.selectedBackupText = text;
      this.selectedBackupReady = true;
      this.status = '已选择加密备份，请输入单独保存的恢复密钥';
    } catch (error) {
      this.selectedBackupText = '';
      this.selectedBackupReady = false;
      this.status = `选择备份失败：${this.errorText(error)}`;
    }
    this.isRunning = false;
  }

  private async readSelectedFile(uri: string): Promise<string> {
    const file = await fileIo.open(uri, fileIo.OpenMode.READ_ONLY);
    try {
      const stat = await fileIo.stat(file.fd);
      if (stat.size <= 0 || stat.size > 1024 * 1024) {
        throw new Error('Backup file is empty or exceeds the 1 MB limit.');
      }
      const buffer = new ArrayBuffer(stat.size);
      const bytesRead = await fileIo.read(file.fd, buffer);
      return new util.TextDecoder().decodeToString(new Uint8Array(buffer, 0, bytesRead));
    } finally {
      await fileIo.close(file);
    }
  }

  private backupPayloadFromText(text: string): SecureBackupPayload {
    const payload = JSON.parse(text) as SecureBackupPayload;
    const envelope = payload.data as SecureEnvelope;
    if (payload.version !== 2 || payload.encrypted !== true || payload.keyFormat !== 'base64url-256' ||
      envelope === undefined || envelope.version !== 1 || envelope.algorithm !== 'A256CBC-HS256') {
      throw new Error('Unsupported backup format.');
    }
    return payload;
  }

  private validatedPortableAccount(account: PortableAccount): OtpAuthAccount {
    if (typeof account.issuer !== 'string' || typeof account.account !== 'string' ||
      typeof account.secret !== 'string') {
      throw new Error('Backup contains an invalid account.');
    }
    const issuer = account.issuer.trim();
    const name = account.account.trim();
    if (name.length === 0) {
      throw new Error('Backup account name cannot be empty.');
    }
    const algorithm = typeof account.algorithm === 'string' ? account.algorithm.toUpperCase() : 'SHA1';
    const digits = typeof account.digits === 'number' ? account.digits : 6;
    const period = typeof account.period === 'number' ? account.period : 30;
    if ((algorithm !== 'SHA1' && algorithm !== 'SHA256' && algorithm !== 'SHA512') ||
      !Number.isInteger(digits) || digits < 6 || digits > 8 ||
      !Number.isInteger(period) || period <= 0 || period > 300) {
      throw new Error('Backup contains unsupported TOTP parameters.');
    }
    return {
      issuer,
      name,
      secret: this.normalizeSecretInput(account.secret),
      algorithm,
      digits,
      period,
      group: typeof account.group === 'string' ? account.group.trim() : '',
      color: typeof account.color === 'string' ? account.color : 'blue',
      createdAt: typeof account.createdAt === 'number' ? account.createdAt : Date.now()
    };
  }

  private async importSelectedBackup(): Promise<void> {
    this.isRunning = true;
    const createdAliases: string[] = [];
    try {
      if (!this.selectedBackupReady || this.selectedBackupText.length === 0) {
        throw new Error('Select an encrypted backup file first.');
      }
      if (!isValidRecoveryKey(this.importRecoveryKey)) {
        throw new Error('Enter a valid 43-character recovery key.');
      }
      const payload = this.backupPayloadFromText(this.selectedBackupText);
      const plaintext = await decryptString(payload.data, normalizeRecoveryKey(this.importRecoveryKey));
      const portableAccounts = JSON.parse(plaintext) as PortableAccount[];
      if (!Array.isArray(portableAccounts)) {
        throw new Error('Backup account payload is invalid.');
      }

      await this.requestBiometricPermission();
      const existingSecretMap = await this.readAllProtectedSecrets('导入前检查重复账户');
      const knownSecrets = new Set<string>();
      existingSecretMap.forEach((secret: string): void => {
        knownSecrets.add(this.normalizedSecret(secret));
      });

      const importedAccounts: MfaAccount[] = [];
      let skipped = 0;
      for (let index = 0; index < portableAccounts.length; index++) {
        const input = this.validatedPortableAccount(portableAccounts[index]);
        if (knownSecrets.has(input.secret)) {
          skipped += 1;
          continue;
        }
        knownSecrets.add(input.secret);
        const id = `${Date.now()}-${index}`;
        const secretAlias = `${this.assetPrefix}${id}`;
        await this.saveSecret(secretAlias, input.secret);
        createdAliases.push(secretAlias);
        importedAccounts.push({
          id,
          issuer: input.issuer,
          name: input.name,
          secretAlias,
          algorithm: input.algorithm,
          digits: input.digits,
          period: input.period,
          group: input.group ?? '',
          color: input.color ?? 'blue',
          createdAt: input.createdAt ?? Date.now(),
          code: '',
          revealedSecret: ''
        });
      }

      const nextAccounts = [...importedAccounts, ...this.accounts];
      await this.saveAccountRecords(nextAccounts);
      this.accounts = nextAccounts;
      this.selectedBackupText = '';
      this.selectedBackupReady = false;
      this.importRecoveryKey = '';
      this.status = `备份导入完成：新增 ${importedAccounts.length} 个，跳过 ${skipped} 个重复账户`;
    } catch (error) {
      for (const alias of createdAliases) {
        try {
          await asset.remove(this.accountAssetQuery(alias));
        } catch (cleanupError) {
          // Orphan cleanup is best effort; no account record points at these aliases.
        }
      }
      this.status = `导入备份失败：${this.errorText(error)}`;
    }
    this.isRunning = false;
  }

  private async authenticateQrTransfer(title: string): Promise<void> {
    await this.requestBiometricPermission();
    const challenge = cryptoFramework.createRandom().generateRandomSync(32).data;
    await this.authenticate(challenge, title);
  }

  private async scanTransferQrValue(): Promise<string> {
    const hostContext = this.getUIContext().getHostContext();
    if (hostContext === undefined) {
      throw new Error('Cannot open scanner without an ArkUI host context.');
    }
    const options: scanBarcode.ScanOptions = {
      scanTypes: [scanCore.ScanType.QR_CODE],
      enableMultiMode: false,
      enableAlbum: false
    };
    const result = await scanBarcode.startScanForResult(hostContext, options);
    const value = result.originalValue.trim();
    if (value.length === 0) {
      throw new Error('Scanned QR code is empty.');
    }
    return value;
  }

  private async startReceivingQrTransfer(): Promise<void> {
    this.isRunning = true;
    try {
      await this.authenticateQrTransfer('创建一次性二维码接收密钥');
      clearQrReceiveSession(this.qrReceiveSession);
      this.qrReceiveSession = await createQrReceiveSession();
      this.qrPairingValue = buildPairingQr({
        sessionId: this.qrReceiveSession.sessionId,
        publicKey: this.qrReceiveSession.publicKey,
        expiresAt: this.qrReceiveSession.expiresAt
      });
      this.qrIncomingChunks.clear();
      this.qrReceivedParts = 0;
      this.qrExpectedParts = 0;
      this.qrTransferMode = 'receive';
      this.status = '一次性配对二维码已生成，5 分钟后失效';
      const expiringSession: QrReceiveSession = this.qrReceiveSession;
      setTimeout(() => {
        if (this.qrReceiveSession?.sessionId === expiringSession.sessionId && Date.now() >= expiringSession.expiresAt) {
          clearQrReceiveSession(expiringSession);
          this.qrReceiveSession = undefined;
          this.qrPairingValue = '';
          this.qrIncomingChunks.clear();
          this.qrTransferMode = 'choice';
          this.status = '二维码接收会话已过期，请重新开始';
        }
      }, QR_TRANSFER_SESSION_MS + 1000);
    } catch (error) {
      this.status = `无法开始接收：${this.errorText(error)}`;
    }
    this.isRunning = false;
  }

  private async openQrContinuousScanner(): Promise<void> {
    this.isRunning = true;
    try {
      const session = this.qrReceiveSession;
      if (session === undefined || session.expiresAt < Date.now()) {
        throw new Error('QR receive session has expired.');
      }
      if (!await this.requestCameraPermission()) {
        throw new Error('Camera permission was not granted.');
      }
      this.qrTransferMode = 'receive-scan';
      this.status = '连续扫码已启动，请保持镜头对准旧设备';
    } catch (error) {
      this.status = `无法启动连续扫码：${this.errorText(error)}`;
    }
    this.isRunning = false;
  }

  private stopQrTransferAutoAdvance(): void {
    if (this.qrTransferTimerId >= 0) {
      clearInterval(this.qrTransferTimerId);
      this.qrTransferTimerId = -1;
    }
  }

  private startQrTransferAutoAdvance(): void {
    this.stopQrTransferAutoAdvance();
    this.qrTransferAutoAdvance = true;
    if (this.qrTransferParts.length <= 1) {
      return;
    }
    this.qrTransferTimerId = setInterval(() => {
      if (this.qrTransferMode === 'send' && this.qrTransferAutoAdvance && this.qrTransferParts.length > 1) {
        this.qrTransferPartIndex = (this.qrTransferPartIndex + 1) % this.qrTransferParts.length;
      }
    }, 1500);
  }

  private toggleQrTransferAutoAdvance(): void {
    if (this.qrTransferAutoAdvance) {
      this.qrTransferAutoAdvance = false;
      this.stopQrTransferAutoAdvance();
    } else {
      this.startQrTransferAutoAdvance();
    }
  }

  private async startSendingQrTransfer(): Promise<void> {
    this.isRunning = true;
    try {
      if (this.accounts.length === 0) {
        throw new Error('No accounts to transfer.');
      }
      const secrets = await this.readAllProtectedSecrets('发送 MFA 账户到新设备');
      const pairing: PairingQr = parsePairingQr(await this.scanTransferQrValue());
      const portable: PortableAccount[] = [];
      for (const account of this.accounts) {
        const secret = secrets.get(account.secretAlias);
        if (secret === undefined) {
          throw new Error(`Protected secret is missing for ${account.issuer}.`);
        }
        portable.push({
          id: account.id,
          issuer: account.issuer,
          account: account.name,
          secret,
          createdAt: account.createdAt,
          algorithm: account.algorithm,
          digits: account.digits,
          period: account.period,
          group: account.group,
          color: account.color
        });
      }
      const portableJson = JSON.stringify(portable);
      const plaintextBytes = new util.TextEncoder().encodeInto(portableJson).length;
      const transferKey = generateRecoveryKey();
      const payload: EncryptedTransferPackage = {
        version: 1,
        sessionId: pairing.sessionId,
        wrappedKey: await wrapQrTransferKey(pairing.publicKey, transferKey),
        data: await encryptString(portableJson, transferKey)
      };
      this.qrTransferParts = encodeTransferPackage(payload);
      hilog.info(QR_TRANSFER_LOG_DOMAIN, 'MfaVerifier',
        'QR send metadata accounts=%{public}d plaintextBytes=%{public}d ciphertextChars=%{public}d parts=%{public}d renderer=%{public}s',
        portable.length, plaintextBytes, payload.data.ciphertext.length, this.qrTransferParts.length, 'native');
      this.qrTransferPartIndex = 0;
      this.qrTransferMode = 'send';
      this.startQrTransferAutoAdvance();
      this.status = `紧凑二维码已准备，共 ${this.qrTransferParts.length} 张，将自动轮播`;
    } catch (error) {
      this.stopQrTransferAutoAdvance();
      const maybeBusinessError = error as Record<string, Object>;
      if (maybeBusinessError['code'] === scanCore.ScanErrorCode.SCAN_SERVICE_CANCELED) {
        this.status = '已取消二维码迁移';
      } else {
        this.status = `无法准备账户二维码：${this.errorText(error)}`;
      }
    }
    this.isRunning = false;
  }

  private async importQrPortableAccounts(portableAccounts: PortableAccount[]): Promise<QrImportResult> {
    if (!Array.isArray(portableAccounts) || portableAccounts.length > 500) {
      throw new Error('Invalid account transfer payload.');
    }
    const createdAliases: string[] = [];
    try {
      const existingSecretMap = await this.readAllProtectedSecrets('导入二维码账户前检查重复密钥');
      const knownSecrets = new Set<string>();
      existingSecretMap.forEach((secret: string): void => {
        knownSecrets.add(this.normalizedSecret(secret));
      });
      const importedAccounts: MfaAccount[] = [];
      let skipped = 0;
      for (let index = 0; index < portableAccounts.length; index++) {
        const input = this.validatedPortableAccount(portableAccounts[index]);
        if (knownSecrets.has(input.secret)) {
          skipped += 1;
          continue;
        }
        knownSecrets.add(input.secret);
        const id = `${Date.now()}-qr-${index}`;
        const secretAlias = `${this.assetPrefix}${id}`;
        await this.saveSecret(secretAlias, input.secret);
        createdAliases.push(secretAlias);
        importedAccounts.push({
          id,
          issuer: input.issuer,
          name: input.name,
          secretAlias,
          algorithm: input.algorithm,
          digits: input.digits,
          period: input.period,
          group: input.group ?? '',
          color: input.color ?? 'blue',
          createdAt: input.createdAt ?? Date.now(),
          code: '',
          revealedSecret: ''
        });
      }
      const nextAccounts = [...importedAccounts, ...this.accounts];
      await this.saveAccountRecords(nextAccounts);
      this.accounts = nextAccounts;
      return { imported: importedAccounts.length, skipped };
    } catch (error) {
      for (const alias of createdAliases) {
        try {
          await asset.remove(this.accountAssetQuery(alias));
        } catch (cleanupError) {
          // Orphan cleanup is best effort; no saved record points at these aliases.
        }
      }
      throw new Error(this.errorText(error));
    }
  }

  private async consumeQrTransferPart(value: string): Promise<boolean> {
    try {
      const session = this.qrReceiveSession;
      if (session === undefined) {
        throw new Error('QR receive session has expired.');
      }
      if (session.expiresAt < Date.now()) {
        clearQrReceiveSession(session);
        this.qrReceiveSession = undefined;
        this.qrIncomingChunks.clear();
        this.qrPairingValue = '';
        this.qrReceivedParts = 0;
        this.qrExpectedParts = 0;
        this.qrTransferMode = 'choice';
        this.status = '二维码接收会话已过期，请重新开始';
        return false;
      }
      const chunk: TransferChunk = parseTransferChunk(value);
      if (chunk.sessionId !== session.sessionId) {
        this.status = '已忽略其他接收设备的二维码，继续等待本次迁移';
        return true;
      }
      if (this.qrExpectedParts !== 0 && this.qrExpectedParts !== chunk.total) {
        this.status = '已忽略其他迁移序列的二维码，继续等待本次迁移';
        return true;
      }
      this.qrIncomingChunks.set(chunk.index, chunk.data);
      this.qrExpectedParts = chunk.total;
      this.qrReceivedParts = this.qrIncomingChunks.size;
      if (this.qrIncomingChunks.size === chunk.total) {
        const payload = assembleTransferPackage(session.sessionId, this.qrIncomingChunks, chunk.total);
        this.qrReceiveSession = undefined;
        const transferKey = await unwrapQrTransferKey(session, payload.wrappedKey);
        if (!isValidRecoveryKey(transferKey)) {
          throw new Error('Temporary transfer key is invalid.');
        }
        const plaintext = await decryptString(payload.data, transferKey);
        const portable = JSON.parse(plaintext) as PortableAccount[];
        const result = await this.importQrPortableAccounts(portable);
        this.qrIncomingChunks.clear();
        this.qrPairingValue = '';
        this.qrTransferMode = 'choice';
        this.status = `二维码迁移完成：新增 ${result.imported} 个，跳过 ${result.skipped} 个重复账户`;
        return false;
      } else {
        this.status = `已接收 ${this.qrReceivedParts}/${this.qrExpectedParts} 张，正在自动等待其余二维码`;
        return true;
      }
    } catch (error) {
      if (this.qrReceiveSession === undefined) {
        this.qrIncomingChunks.clear();
        this.qrPairingValue = '';
        this.qrReceivedParts = 0;
        this.qrExpectedParts = 0;
        this.qrTransferMode = 'choice';
        this.status = `二维码迁移失败：${this.errorText(error)}`;
        return false;
      }
      this.status = `未识别为本次账户二维码，继续扫描：${this.errorText(error)}`;
      return true;
    }
  }

  private resetQrTransfer(): void {
    this.stopQrTransferAutoAdvance();
    clearQrReceiveSession(this.qrReceiveSession);
    this.qrReceiveSession = undefined;
    this.qrIncomingChunks.clear();
    this.qrPairingValue = '';
    this.qrTransferParts = [];
    this.qrTransferPartIndex = 0;
    this.qrTransferAutoAdvance = true;
    this.qrReceivedParts = 0;
    this.qrExpectedParts = 0;
    this.qrTransferMode = 'choice';
    this.status = '本次二维码迁移数据已清除';
  }

  private isDarkMode(): boolean {
    if (this.themePreference === 'dark') {
      return true;
    }
    if (this.themePreference === 'light') {
      return false;
    }
    return this.systemDark;
  }

  private colors(): AppColors {
    return colorsFor(this.isDarkMode());
  }

  private applyThemePreference(preference: string): void {
    if (preference === 'dark') {
      this.systemDark = true;
      this.prefsContext().setColorMode(ConfigurationConstant.ColorMode.COLOR_MODE_DARK);
    } else if (preference === 'light') {
      this.systemDark = false;
      this.prefsContext().setColorMode(ConfigurationConstant.ColorMode.COLOR_MODE_LIGHT);
    } else {
      this.prefsContext().setColorMode(ConfigurationConstant.ColorMode.COLOR_MODE_NOT_SET);
      const colorMode = this.prefsContext().config.colorMode;
      this.systemDark = colorMode === ConfigurationConstant.ColorMode.COLOR_MODE_DARK;
    }
  }

  private async setThemePreference(preference: string): Promise<void> {
    this.themePreference = preference;
    this.applyThemePreference(preference);
    const prefs = await this.getPreferences();
    await prefs.put(this.themeKey, preference);
    await prefs.flush();
    this.status = preference === 'system' ? '主题已设为跟随系统' : `已切换为${preference === 'dark' ? '深色' : '浅色'}主题`;
  }

  private async setAppLockEnabled(enabled: boolean): Promise<void> {
    this.isRunning = true;
    try {
      if (enabled) {
        this.availableAuthSelection();
        await this.migrateAccountSecretAccessToApplicationLock();
      } else {
        await this.migrateAccountSecretAccessToDirectDisplay();
        await this.loadCodesWithoutApplicationLock();
      }
      this.appLockEnabled = enabled;
      this.lifecycleAppLockEnabled = enabled;
      if (!enabled) {
        this.appLocked = false;
      }
      this.status = enabled ? '已开启进入应用时的身份验证' : '已关闭进入应用时的身份验证';
    } catch (error) {
      this.status = `更新应用锁失败：${this.errorText(error)}`;
    } finally {
      this.isRunning = false;
    }
  }

  private async acceptPrivacy(): Promise<void> {
    const prefs = await this.getPreferences();
    await prefs.put(this.privacyKey, true);
    await prefs.flush();
    this.privacyAccepted = true;
    this.status = '隐私设置已保存在本机';
    if (this.appLockEnabled) {
      await this.unlockApplication();
    } else {
      this.appLocked = false;
    }
  }

  private async openPrivacyPolicy(): Promise<void> {
    try {
      const context = this.getUIContext().getHostContext() as common.UIAbilityContext | undefined;
      if (context === undefined) {
        throw new Error('无法获取应用窗口。');
      }
      await context.startAbility({
        action: 'ohos.want.action.viewData',
        uri: this.privacyPolicyUrl
      });
      this.status = '已使用系统浏览器打开隐私政策';
    } catch (error) {
      this.status = `打开隐私政策失败：${this.errorText(error)}`;
    }
  }

  private async unlockApplication(): Promise<void> {
    this.isRunning = true;
    try {
      await this.requestBiometricPermission();
      if (this.accounts.length > 0) {
        const secrets = await this.readAllProtectedSecrets('验证身份以解锁留白 MFA');
        const unlockedAccounts: MfaAccount[] = [];
        for (const account of this.accounts) {
          const secret = secrets.get(account.secretAlias);
          if (secret === undefined) {
            throw new Error(`${account.issuer} 的安全密钥不存在。`);
          }
          unlockedAccounts.push({
            id: account.id,
            issuer: account.issuer,
            name: account.name,
            secretAlias: account.secretAlias,
            algorithm: account.algorithm,
            digits: account.digits,
            period: account.period,
            group: account.group,
            color: account.color,
            createdAt: account.createdAt,
            code: await this.generateTotp(secret, this.nowMs, account.period, account.digits, account.algorithm),
            revealedSecret: ''
          });
        }
        this.accounts = unlockedAccounts;
      } else {
        const challenge = this.bytesFromString('mfa-verifier-application-unlock');
        await this.authenticate(challenge, '验证身份以解锁 MFA Verifier');
      }
      this.appLocked = false;
      this.status = '应用已解锁';
    } catch (error) {
      this.status = `解锁失败：${this.errorText(error)}`;
    }
    this.isRunning = false;
  }

  private lockApplication(): void {
    const lockedAccounts: MfaAccount[] = [];
    for (const account of this.accounts) {
      lockedAccounts.push({
        id: account.id,
        issuer: account.issuer,
        name: account.name,
        secretAlias: account.secretAlias,
        algorithm: account.algorithm,
        digits: account.digits,
        period: account.period,
        group: account.group,
        color: account.color,
        createdAt: account.createdAt,
        code: '',
        revealedSecret: ''
      });
    }
    this.accounts = lockedAccounts;
    this.sessionSecrets.clear();
    this.appLocked = true;
    this.status = '应用已锁定';
  }

  private openAddPage(): void {
    this.issuerInput = '';
    this.nameInput = '';
    this.secretInput = '';
    this.otpauthInput = '';
    this.groupInput = '';
    this.addCreatingGroup = false;
    this.colorInput = 'blue';
    this.pathStack.pushPathByName('add', null);
  }

  private openEditPage(account: MfaAccount): void {
    this.startEdit(account);
    this.pathStack.pushPathByName('edit', null);
  }

  private accountById(id: string): MfaAccount | undefined {
    for (const account of this.accounts) {
      if (account.id === id) {
        return account;
      }
    }
    return undefined;
  }

  private editingAccount(): MfaAccount | undefined {
    return this.accountById(this.editingAccountId);
  }

  private filteredAccounts(): MfaAccount[] {
    const query = this.searchQuery.trim().toLowerCase();
    if (query.length === 0) {
      return this.accounts;
    }
    const matches: MfaAccount[] = [];
    for (const account of this.accounts) {
      if (account.issuer.toLowerCase().includes(query) || account.name.toLowerCase().includes(query) ||
        account.group.toLowerCase().includes(query)) {
        matches.push(account);
      }
    }
    return matches;
  }

  private accountGroups(accounts: MfaAccount[]): string[] {
    const groups = new Set<string>();
    for (const account of accounts) {
      if (account.group.length > 0) {
        groups.add(account.group);
      }
    }
    return Array.from(groups).sort((left: string, right: string): number => left.localeCompare(right));
  }

  private groupSelectOptions(): SelectOption[] {
    const options: SelectOption[] = [{ value: '未分组' }];
    for (const group of this.accountGroups(this.accounts)) {
      options.push({ value: group });
    }
    options.push({ value: '新建分组' });
    return options;
  }

  private groupSelectionIndex(group: string, creating: boolean): number {
    const groups = this.accountGroups(this.accounts);
    if (creating) {
      return groups.length + 1;
    }
    if (group.length === 0) {
      return 0;
    }
    const index = groups.indexOf(group);
    return index >= 0 ? index + 1 : groups.length + 1;
  }

  private groupSelectionValue(group: string, creating: boolean): string {
    if (creating) {
      return '新建分组';
    }
    return group.length > 0 ? group : '未分组';
  }

  private selectAddGroup(index: number): void {
    const groups = this.accountGroups(this.accounts);
    if (index === 0) {
      this.groupInput = '';
      this.addCreatingGroup = false;
    } else if (index <= groups.length) {
      this.groupInput = groups[index - 1];
      this.addCreatingGroup = false;
    } else {
      this.groupInput = '';
      this.addCreatingGroup = true;
    }
  }

  private selectEditGroup(index: number): void {
    const groups = this.accountGroups(this.accounts);
    if (index === 0) {
      this.editGroupInput = '';
      this.editCreatingGroup = false;
    } else if (index <= groups.length) {
      this.editGroupInput = groups[index - 1];
      this.editCreatingGroup = false;
    } else {
      this.editGroupInput = '';
      this.editCreatingGroup = true;
    }
  }

  private normalizedGroupValue(value: string, creating: boolean): string {
    const trimmed = value.trim();
    if (creating && trimmed.length === 0) {
      throw new Error('请输入新分组名称。');
    }
    for (const group of this.accountGroups(this.accounts)) {
      if (group.toLocaleLowerCase() === trimmed.toLocaleLowerCase()) {
        return group;
      }
    }
    return trimmed;
  }

  private accountsInGroup(accounts: MfaAccount[], group: string): MfaAccount[] {
    const matches: MfaAccount[] = [];
    for (const account of accounts) {
      if (account.group === group) {
        matches.push(account);
      }
    }
    return matches;
  }

  private accountInitials(account: MfaAccount): string {
    const source = account.issuer.trim().length > 0 ? account.issuer.trim() : account.name.trim();
    return source.length > 0 ? source.substring(0, Math.min(2, source.length)).toUpperCase() : 'M';
  }

  private serviceBadge(account: MfaAccount): ServiceBadge | undefined {
    const source = `${account.issuer} ${account.name}`.toLowerCase();
    if (source.includes('google') || source.includes('gmail') || source.includes('谷歌')) {
      return { label: 'G', foreground: '#4285F4', background: '#E8F0FE' };
    }
    if (source.includes('microsoft') || source.includes('outlook') || source.includes('office 365') ||
      source.includes('azure') || source.includes('微软')) {
      return { label: '⊞', foreground: '#737373', background: '#F3F4F6' };
    }
    if (source.includes('apple') || source.includes('icloud') || source.includes('苹果')) {
      return { label: 'A', foreground: '#111111', background: '#F4F4F5' };
    }
    if (source.includes('github')) {
      return { label: 'GH', foreground: '#FFFFFF', background: '#24292F' };
    }
    if (source.includes('amazon') || source.includes('aws')) {
      return { label: 'aws', foreground: '#FF9900', background: '#232F3E' };
    }
    if (source.includes('cloudflare')) {
      return { label: 'CF', foreground: '#F48120', background: '#FFF2E6' };
    }
    if (source.includes('facebook')) {
      return { label: 'f', foreground: '#FFFFFF', background: '#1877F2' };
    }
    if (source.includes('twitter') || source.includes('x.com')) {
      return { label: 'X', foreground: '#FFFFFF', background: '#111111' };
    }
    return undefined;
  }

  private isGoogleAccount(account: MfaAccount): boolean {
    const source = `${account.issuer} ${account.name}`.toLowerCase();
    return source.includes('google') || source.includes('gmail') || source.includes('谷歌');
  }

  private accountIconText(account: MfaAccount): string {
    const badge = this.serviceBadge(account);
    return badge === undefined ? this.accountInitials(account) : badge.label;
  }

  private accountIconForeground(account: MfaAccount): string {
    const badge = this.serviceBadge(account);
    return badge === undefined ? '#FFFFFF' : badge.foreground;
  }

  private accountIconBackground(account: MfaAccount): string {
    const badge = this.serviceBadge(account);
    return badge === undefined ? labelColor(account.color, this.colors().primarySoft) : badge.background;
  }

  private accountIconFontSize(account: MfaAccount): number {
    const length = this.accountIconText(account).length;
    return length > 2 ? 10 : length > 1 ? 12 : 18;
  }

  private accountPresentationKey(account: MfaAccount): string {
    return `${account.id}|${account.issuer}|${account.name}|${account.group}|${account.color}|${account.code}`;
  }

  private formattedCode(account: MfaAccount): string {
    if (account.code.length === 0) {
      return '••• •••';
    }
    const middle = Math.floor(account.code.length / 2);
    return `${account.code.substring(0, middle)} ${account.code.substring(middle)}`;
  }

  private accountSecondsRemaining(account: MfaAccount): number {
    return account.period - (Math.floor(this.nowMs / 1000) % account.period);
  }

  private isGroupExpanded(group: string): boolean {
    return this.expandedGroups.includes(group);
  }

  private toggleGroup(group: string): void {
    if (this.isGroupExpanded(group)) {
      this.expandedGroups = this.expandedGroups.filter((value: string): boolean => value !== group);
    } else {
      this.expandedGroups = [...this.expandedGroups, group];
    }
  }

  private openAccountActions(account: MfaAccount): void {
    this.pendingAccountActionId = account.id;
    this.moveGroupInput = '';
    this.accountActionMode = 'menu';
  }

  private async movePendingAccountToNewGroup(): Promise<void> {
    try {
      const group = this.normalizedGroupValue(this.moveGroupInput, true);
      await this.updateAccountPresentation(this.pendingAccountActionId, undefined, group);
      this.moveGroupInput = '';
    } catch (error) {
      this.status = this.errorText(error);
    }
  }

  private async updateAccountPresentation(accountId: string, color?: string, group?: string): Promise<void> {
    const updated: MfaAccount[] = [];
    for (const account of this.accounts) {
      updated.push(account.id === accountId ? {
        id: account.id,
        issuer: account.issuer,
        name: account.name,
        secretAlias: account.secretAlias,
        algorithm: account.algorithm,
        digits: account.digits,
        period: account.period,
        group: group === undefined ? account.group : group,
        color: color === undefined ? account.color : color,
        createdAt: account.createdAt,
        code: account.code,
        revealedSecret: account.revealedSecret
      } : account);
    }
    this.accounts = updated;
    await this.saveAccountRecords(updated);
    this.pendingAccountActionId = '';
    this.accountActionMode = '';
    this.status = color === undefined ? '账户已移动到分组' : '标签颜色已更新';
  }

  private async confirmPendingDelete(): Promise<void> {
    const account = this.accountById(this.pendingDeleteId);
    this.pendingDeleteId = '';
    if (account !== undefined) {
      await this.deleteAccount(account);
    }
  }

  private async renameGroup(): Promise<void> {
    const nextName = this.renameGroupInput.trim();
    if (this.editingGroupName.length === 0 || nextName.length === 0) {
      this.status = '分组名称不能为空';
      return;
    }
    const previousName = this.editingGroupName;
    const updated: MfaAccount[] = [];
    for (const account of this.accounts) {
      updated.push(account.group === this.editingGroupName ? {
        id: account.id,
        issuer: account.issuer,
        name: account.name,
        secretAlias: account.secretAlias,
        algorithm: account.algorithm,
        digits: account.digits,
        period: account.period,
        group: nextName,
        color: account.color,
        createdAt: account.createdAt,
        code: account.code,
        revealedSecret: account.revealedSecret
      } : account);
    }
    this.accounts = updated;
    await this.saveAccountRecords(updated);
    if (this.isGroupExpanded(previousName)) {
      this.expandedGroups = this.expandedGroups
        .filter((value: string): boolean => value !== previousName)
        .concat(nextName);
    }
    this.editingGroupName = '';
    this.renameGroupInput = '';
    this.groupActionMode = '';
    this.status = '分组已重命名';
  }

  private async removeGroup(group: string): Promise<void> {
    const updated: MfaAccount[] = [];
    for (const account of this.accounts) {
      updated.push(account.group === group ? {
        id: account.id,
        issuer: account.issuer,
        name: account.name,
        secretAlias: account.secretAlias,
        algorithm: account.algorithm,
        digits: account.digits,
        period: account.period,
        group: '',
        color: account.color,
        createdAt: account.createdAt,
        code: account.code,
        revealedSecret: account.revealedSecret
      } : account);
    }
    this.accounts = updated;
    await this.saveAccountRecords(updated);
    this.expandedGroups = this.expandedGroups.filter((value: string): boolean => value !== group);
    this.editingGroupName = '';
    this.groupActionMode = '';
    this.status = '分组已删除，组内账户已移至未分组';
  }

  private async refreshUnlockedCodes(): Promise<void> {
    if (this.accounts.length === 0 || this.sessionSecrets.size === 0 || this.isRunning) {
      return;
    }
    const next: MfaAccount[] = [];
    for (const account of this.accounts) {
      const secret = this.sessionSecrets.get(account.id);
      if (secret !== undefined) {
        const code = await this.generateTotp(secret, this.nowMs, account.period, account.digits, account.algorithm);
        next.push({
          id: account.id,
          issuer: account.issuer,
          name: account.name,
          secretAlias: account.secretAlias,
          algorithm: account.algorithm,
          digits: account.digits,
          period: account.period,
          group: account.group,
          color: account.color,
          createdAt: account.createdAt,
          code,
          revealedSecret: account.revealedSecret
        });
      } else {
        next.push(account);
      }
    }
    this.accounts = next;
  }

  @Builder
  private statusStrip() {
    Row({ space: 8 }) {
      Circle({ width: 7, height: 7 })
        .fill(this.isRunning ? this.colors().warning : this.colors().success);
      Text(this.status)
        .fontSize(12)
        .fontColor(this.colors().mutedForeground)
        .maxLines(2)
        .textOverflow({ overflow: TextOverflow.Ellipsis })
        .layoutWeight(1);
    }
    .width('100%')
    .padding({ left: 16, right: 16, top: 8, bottom: 8 })
    .backgroundColor(this.colors().surface);
  }

  @Builder
  private accountSwipeActions(account: MfaAccount) {
    Row() {
      Column({ space: 4 }) {
        SymbolGlyph($r('sys.symbol.square_and_pencil'))
          .fontSize(21)
          .fontColor(['#FFFFFF']);
        Text('编辑')
          .fontSize(12)
          .fontColor('#FFFFFF');
      }
      .justifyContent(FlexAlign.Center)
      .width(76)
      .height('100%')
      .backgroundColor(this.colors().primary)
      .onClick(() => {
        this.openEditPage(account);
      });

      Column({ space: 4 }) {
        SymbolGlyph($r('sys.symbol.trash'))
          .fontSize(21)
          .fontColor(['#FFFFFF']);
        Text('删除')
          .fontSize(12)
          .fontColor('#FFFFFF');
      }
      .justifyContent(FlexAlign.Center)
      .width(76)
      .height('100%')
      .backgroundColor(this.colors().destructive)
      .onClick(() => {
        this.pendingDeleteId = account.id;
      });
    }
    .width(152)
    .height('100%');
  }

  @Builder
  private accountRow(account: MfaAccount) {
    Stack() {
      Row({ space: 12 }) {
        Stack() {
          Circle({ width: 44, height: 44 })
            .fill(this.accountIconBackground(account));
          Text(this.accountIconText(account))
            .fontSize(this.accountIconFontSize(account))
            .fontWeight(FontWeight.Bold)
            .fontColor(this.accountIconForeground(account));
          if (this.isGoogleAccount(account)) {
            Image(GOOGLE_ICON_DATA_URI)
              .width(44)
              .height(44)
              .objectFit(ImageFit.Contain)
              .accessibilityText('Google');
          }
        }
        .width(44)
        .height(44);

        Column({ space: 2 }) {
          Text(account.issuer)
            .fontSize(15)
            .fontWeight(FontWeight.Bold)
            .fontColor(this.colors().foreground)
            .maxLines(1)
            .textOverflow({ overflow: TextOverflow.Ellipsis })
            .width('100%');
          Text(account.name)
            .fontSize(13)
            .fontWeight(FontWeight.Medium)
            .fontColor(this.colors().mutedForeground)
            .maxLines(1)
            .textOverflow({ overflow: TextOverflow.Ellipsis })
            .width('100%');
          Text(this.formattedCode(account))
            .fontSize(22)
            .fontWeight(FontWeight.Bold)
            .fontColor(account.code.length > 0 ? labelColor(account.color, this.colors().primary) :
              this.colors().mutedForeground)
            .fontFamily('HarmonyHeiTi')
            .letterSpacing(0)
            .maxLines(1)
            .width('100%');
        }
        .width(0)
        .layoutWeight(1)
        .alignItems(HorizontalAlign.Start);

        Stack() {
          Progress({
            value: this.accountSecondsRemaining(account),
            total: account.period,
            type: ProgressType.Ring
          })
            .width(40)
            .height(40)
            .color(this.accountSecondsRemaining(account) <= 5 ? this.colors().destructive :
              labelColor(account.color, this.colors().primary))
            .backgroundColor(this.colors().input)
            .style({ strokeWidth: 3 });
          Text(account.code.length > 0 ? `${this.accountSecondsRemaining(account)}` : '—')
            .fontSize(11)
            .fontWeight(FontWeight.Medium)
            .fontColor(this.colors().foreground);
        }
        .width(40)
        .height(40);
      }
      .padding({ left: 12, right: 12, top: 12, bottom: 12 })
      .width('100%');

      if (this.copiedAccountId === account.id) {
        Text('已复制')
          .fontSize(12)
          .fontWeight(FontWeight.Medium)
          .fontColor('#FFFFFF')
          .padding({ left: 10, right: 10, top: 4, bottom: 4 })
          .backgroundColor(labelColor(account.color, this.colors().primary))
          .borderRadius(8)
          .align(Alignment.End)
          .margin({ right: 58 });
      }
    }
    .border({ width: { left: 4 }, color: labelColor(account.color, this.colors().primary) })
    .backgroundColor(this.colors().surface)
    .borderRadius(8)
    .width('100%')
    .onClick(() => {
      if (account.code.length > 0) {
        void this.copyCode(account);
      }
    })
    .gesture(
      LongPressGesture({ repeat: false })
        .onAction(() => {
          this.openAccountActions(account);
        })
    );
  }

  @Builder
  private accountSections() {
    if (this.filteredAccounts().length === 0) {
      ListItem() {
        Column({ space: 12 }) {
          SymbolGlyph($r('sys.symbol.search_things'))
            .fontSize(36)
            .fontColor([this.colors().mutedForeground]);
          Text(this.accounts.length === 0 ? '暂无账户' : '没有匹配结果')
            .fontSize(20)
            .fontWeight(FontWeight.Bold)
            .fontColor(this.colors().foreground);
          Text(this.accounts.length === 0 ? '添加第一个身份验证账户' : '请尝试其他关键词')
            .fontSize(13)
            .fontColor(this.colors().mutedForeground);
          if (this.accounts.length === 0) {
            Button('添加账户')
              .height(42)
              .fontSize(14)
              .backgroundColor(this.colors().primary)
              .borderRadius(8)
              .onClick(() => {
                this.openAddPage();
              });
          }
        }
        .width('100%')
        .padding({ top: 80, bottom: 80 });
      }
      .width('100%')
      .padding({ left: 16, right: 16 });
    } else if (this.searchQuery.trim().length > 0) {
      ForEach(this.filteredAccounts(), (account: MfaAccount) => {
        ListItem() {
          this.accountRow(account);
        }
        .width('100%')
        .margin({ bottom: 10 })
        .swipeAction({
          end: {
            builder: () => {
              this.accountSwipeActions(account);
            },
            actionAreaDistance: 152
          },
          edgeEffect: SwipeEdgeEffect.Spring
        });
      }, (account: MfaAccount) => this.accountPresentationKey(account));
    } else {
      ForEach(this.accountGroups(this.accounts), (group: string) => {
        ListItem() {
          Row({ space: 8 }) {
            SymbolGlyph($r(this.isGroupExpanded(group) ? 'sys.symbol.chevron_down' : 'sys.symbol.chevron_right'))
              .fontSize(14)
              .fontColor([this.colors().mutedForeground]);
            Text(group)
              .fontSize(14)
              .fontWeight(FontWeight.Medium)
              .fontColor(this.colors().foreground)
              .layoutWeight(1);
            Text(`${this.accountsInGroup(this.accounts, group).length}`)
              .fontSize(11)
              .fontColor(this.colors().mutedForeground)
              .padding({ left: 7, right: 7, top: 2, bottom: 2 })
              .backgroundColor(this.colors().input)
              .borderRadius(8);
          }
          .width('100%')
          .height(42)
          .onClick(() => {
            this.toggleGroup(group);
          })
          .gesture(
            LongPressGesture({ repeat: false })
              .onAction(() => {
                this.editingGroupName = group;
                this.renameGroupInput = group;
                this.groupActionMode = 'menu';
              })
          );
        }
        .width('100%');

        if (this.isGroupExpanded(group)) {
          ForEach(this.accountsInGroup(this.accounts, group), (account: MfaAccount) => {
            ListItem() {
              this.accountRow(account);
            }
            .width('100%')
            .margin({ bottom: 10 })
            .swipeAction({
              end: {
                builder: () => {
                  this.accountSwipeActions(account);
                },
                actionAreaDistance: 152
              },
              edgeEffect: SwipeEdgeEffect.Spring
            });
          }, (account: MfaAccount) => this.accountPresentationKey(account));
        }
      }, (group: string) => group);

      ForEach(this.accountsInGroup(this.accounts, ''), (account: MfaAccount) => {
        ListItem() {
          this.accountRow(account);
        }
        .width('100%')
        .margin({ bottom: 10 })
        .swipeAction({
          end: {
            builder: () => {
              this.accountSwipeActions(account);
            },
            actionAreaDistance: 152
          },
          edgeEffect: SwipeEdgeEffect.Spring
        });
      }, (account: MfaAccount) => this.accountPresentationKey(account));
    }

    ListItem() {
      Row().height(96);
    }
    .width('100%');
  }

  @Builder
  private homePage() {
    Stack({ alignContent: Alignment.Bottom }) {
      Column() {
        Row({ space: 10 }) {
          Text('身份验证器')
            .fontSize(25)
            .fontWeight(FontWeight.Bold)
            .fontColor(this.colors().foreground)
            .letterSpacing(0)
            .layoutWeight(1);

          Button({ type: ButtonType.Circle }) {
            SymbolGlyph($r('sys.symbol.gearshape'))
              .fontSize(21)
              .fontColor([this.colors().foreground]);
          }
          .width(40)
          .height(40)
          .backgroundColor(this.colors().input)
          .accessibilityText('设置')
          .onClick(() => {
            this.pathStack.pushPathByName('backup', null);
          });

          Button({ type: ButtonType.Circle }) {
            SymbolGlyph($r('sys.symbol.plus'))
              .fontSize(22)
              .fontColor(['#FFFFFF']);
          }
          .width(40)
          .height(40)
          .backgroundColor(this.colors().primary)
          .accessibilityText('添加账户')
          .onClick(() => {
            this.openAddPage();
          });
        }
        .width('100%')
        .padding({ left: 18, right: 18, top: 16, bottom: 12 });

        if (this.accounts.length > 0) {
          Search({ value: this.searchQuery, placeholder: '搜索账户' })
            .height(42)
            .margin({ left: 16, right: 16, bottom: 10 })
            .backgroundColor(this.colors().surface)
            .placeholderColor(this.colors().mutedForeground)
            .fontColor(this.colors().foreground)
            .searchIcon({ src: $r('sys.symbol.search_things') })
            .onChange((value: string) => {
              this.searchQuery = value;
            });
        }

        List({ space: 0 }) {
          this.accountSections();
        }
        .layoutWeight(1)
        .width('100%')
        .padding({ left: 16, right: 16, top: 6 })
        .backgroundColor(this.colors().background);
      }
      .width('100%')
      .height('100%')
      .backgroundColor(this.colors().background);

    }
    .width('100%')
    .height('100%')
    .backgroundColor(this.colors().background);
  }

  @Builder
  private formLabel(value: string) {
    Text(value)
      .fontSize(12)
      .fontWeight(FontWeight.Medium)
      .fontColor(this.colors().mutedForeground)
      .width('100%');
  }

  @Builder
  private addDestination() {
    NavDestination() {
      Scroll() {
        Column({ space: 18 }) {
          Button('扫描二维码添加')
            .height(48)
            .fontSize(15)
            .fontColor(this.colors().primary)
            .backgroundColor(this.colors().primarySoft)
            .borderRadius(8)
            .width('100%')
            .enabled(!this.isRunning)
            .onClick(() => {
              void this.scanOtpAuthQr();
            });

          Column({ space: 10 }) {
            Text('手动添加')
              .fontSize(18)
              .fontWeight(FontWeight.Bold)
              .fontColor(this.colors().foreground)
              .width('100%');
            this.formLabel('服务名称');
            TextInput({ placeholder: '例如 Google', text: this.issuerInput })
              .height(46)
              .backgroundColor(this.colors().input)
              .fontColor(this.colors().foreground)
              .placeholderColor(this.colors().mutedForeground)
              .borderRadius(8)
              .onChange((value: string) => {
                this.issuerInput = value;
              });
            this.formLabel('账户名称');
            TextInput({ placeholder: 'user@example.com', text: this.nameInput })
              .height(46)
              .backgroundColor(this.colors().input)
              .fontColor(this.colors().foreground)
              .placeholderColor(this.colors().mutedForeground)
              .borderRadius(8)
              .onChange((value: string) => {
                this.nameInput = value;
              });
            this.formLabel('密钥 *');
            TextInput({ placeholder: '输入由服务提供的密钥', text: this.secretInput })
              .type(InputType.Password)
              .showPasswordIcon(true)
              .height(46)
              .backgroundColor(this.colors().input)
              .fontColor(this.colors().foreground)
              .placeholderColor(this.colors().mutedForeground)
              .borderRadius(8)
              .onChange((value: string) => {
                this.secretInput = value;
              });
          }
          .padding(16)
          .backgroundColor(this.colors().surface)
          .borderRadius(8)
          .border({ width: 1, color: this.colors().border })
          .width('100%');

          Column({ space: 10 }) {
            this.formLabel('分组（可选）');
            Select(this.groupSelectOptions())
              .selected(this.groupSelectionIndex(this.groupInput, this.addCreatingGroup))
              .value(this.groupSelectionValue(this.groupInput, this.addCreatingGroup))
              .height(44)
              .width('100%')
              .backgroundColor(this.colors().input)
              .fontColor(this.colors().foreground)
              .borderRadius(8)
              .onSelect((index: number) => {
                this.selectAddGroup(index);
              });
            if (this.addCreatingGroup) {
              TextInput({ placeholder: '输入新分组名称', text: this.groupInput })
                .height(44)
                .backgroundColor(this.colors().input)
                .fontColor(this.colors().foreground)
                .placeholderColor(this.colors().mutedForeground)
                .borderRadius(8)
                .onChange((value: string) => {
                  this.groupInput = value;
                });
            }
            this.formLabel('标签颜色');
            Flex({ wrap: FlexWrap.Wrap, justifyContent: FlexAlign.SpaceBetween }) {
              ForEach(LABEL_COLORS, (option: LabelColorOption) => {
                Button({ type: ButtonType.Circle }) {
                  if (this.colorInput === option.id) {
                    Text('✓').fontSize(14).fontColor('#FFFFFF');
                  }
                }
                .width(34)
                .height(34)
                .backgroundColor(option.value)
                .border({ width: this.colorInput === option.id ? 3 : 0, color: this.colors().foreground })
                .accessibilityText(option.label)
                .onClick(() => {
                  this.colorInput = option.id;
                });
              }, (option: LabelColorOption) => option.id);
            }
            .width('100%');
          }
          .padding(16)
          .backgroundColor(this.colors().surface)
          .borderRadius(8)
          .border({ width: 1, color: this.colors().border })
          .width('100%');

          Column({ space: 10 }) {
            Text('从 otpauth 链接导入')
              .fontSize(16)
              .fontWeight(FontWeight.Medium)
              .fontColor(this.colors().foreground)
              .width('100%');
            Text('高级导入方式，不影响上方手动填写内容。')
              .fontSize(12)
              .fontColor(this.colors().mutedForeground)
              .width('100%');
            TextInput({ placeholder: 'otpauth://totp/...', text: this.otpauthInput })
              .height(46)
              .backgroundColor(this.colors().input)
              .fontColor(this.colors().foreground)
              .placeholderColor(this.colors().mutedForeground)
              .borderRadius(8)
              .onChange((value: string) => {
                this.otpauthInput = value;
              });
            Button('导入链接')
              .height(42)
              .fontSize(14)
              .fontColor(this.colors().primary)
              .backgroundColor(this.colors().primarySoft)
              .borderRadius(8)
              .width('100%')
              .enabled(!this.isRunning && this.otpauthInput.trim().length > 0)
              .onClick(() => {
                void this.importOtpAuthUrl();
              });
          }
          .padding(16)
          .backgroundColor(this.colors().surface)
          .borderRadius(8)
          .border({ width: 1, color: this.colors().border })
          .width('100%');

          Button(this.isRunning ? '正在保存…' : '保存账户')
            .height(50)
            .fontSize(16)
            .fontWeight(FontWeight.Medium)
            .fontColor('#FFFFFF')
            .backgroundColor(this.colors().primary)
            .borderRadius(8)
            .width('100%')
            .enabled(!this.isRunning)
            .onClick(() => {
              void this.addAccount();
            });
        }
        .padding(16)
        .width('100%');
      }
      .width('100%')
      .height('100%');
    }
    .title('添加账户')
    .backgroundColor(this.colors().background);
  }

  @Builder
  private editDestination() {
    NavDestination() {
      if (this.editingAccount() !== undefined) {
        Scroll() {
          Column({ space: 18 }) {
            Column({ space: 12 }) {
              Text(this.formattedCode(this.editingAccount() as MfaAccount))
                .fontSize(34)
                .fontWeight(FontWeight.Bold)
                .fontColor(labelColor((this.editingAccount() as MfaAccount).color, this.colors().primary))
                .letterSpacing(0)
                .width('100%');
              Row({ space: 8 }) {
                Button((this.editingAccount() as MfaAccount).revealedSecret.length > 0 ? '隐藏密钥' : '查看密钥')
                  .height(38)
                  .fontSize(12)
                  .backgroundColor(this.colors().input)
                  .fontColor(this.colors().foreground)
                  .borderRadius(6)
                  .layoutWeight(1)
                  .onClick(() => {
                    const account = this.editingAccount() as MfaAccount;
                    if (account.revealedSecret.length > 0) {
                      this.hideSecret(account);
                    } else {
                      void this.revealSecret(account);
                    }
                  });
                Button('复制密钥')
                  .height(38)
                  .fontSize(12)
                  .backgroundColor(this.colors().input)
                  .fontColor(this.colors().foreground)
                  .borderRadius(6)
                  .layoutWeight(1)
                  .onClick(() => {
                    void this.copySecret(this.editingAccount() as MfaAccount);
                  });
              }
              .width('100%');
              if ((this.editingAccount() as MfaAccount).revealedSecret.length > 0) {
                Text((this.editingAccount() as MfaAccount).revealedSecret)
                  .fontSize(13)
                  .fontColor(this.colors().destructive)
                  .copyOption(CopyOptions.None)
                  .width('100%');
              }
            }
            .padding(16)
            .backgroundColor(this.colors().surface)
            .borderRadius(8)
            .border({ width: 1, color: this.colors().border })
            .width('100%');

            Column({ space: 10 }) {
              this.formLabel('服务名称');
              TextInput({ text: this.editIssuerInput })
                .height(46)
                .backgroundColor(this.colors().input)
                .fontColor(this.colors().foreground)
                .borderRadius(8)
                .onChange((value: string) => {
                  this.editIssuerInput = value;
                });
              this.formLabel('账户名称');
              TextInput({ text: this.editNameInput })
                .height(46)
                .backgroundColor(this.colors().input)
                .fontColor(this.colors().foreground)
                .borderRadius(8)
                .onChange((value: string) => {
                  this.editNameInput = value;
                });
              this.formLabel('分组（可选）');
              Select(this.groupSelectOptions())
                .selected(this.groupSelectionIndex(this.editGroupInput, this.editCreatingGroup))
                .value(this.groupSelectionValue(this.editGroupInput, this.editCreatingGroup))
                .height(46)
                .width('100%')
                .backgroundColor(this.colors().input)
                .fontColor(this.colors().foreground)
                .borderRadius(8)
                .onSelect((index: number) => {
                  this.selectEditGroup(index);
                });
              if (this.editCreatingGroup) {
                TextInput({ placeholder: '输入新分组名称', text: this.editGroupInput })
                  .height(46)
                  .backgroundColor(this.colors().input)
                  .fontColor(this.colors().foreground)
                  .placeholderColor(this.colors().mutedForeground)
                  .borderRadius(8)
                  .onChange((value: string) => {
                    this.editGroupInput = value;
                  });
              }
              this.formLabel('标签颜色');
              Flex({ wrap: FlexWrap.Wrap, justifyContent: FlexAlign.SpaceBetween }) {
                ForEach(LABEL_COLORS, (option: LabelColorOption) => {
                  Button({ type: ButtonType.Circle }) {
                    if (this.editColorInput === option.id) {
                      Text('✓').fontSize(14).fontColor('#FFFFFF');
                    }
                  }
                  .width(34)
                  .height(34)
                  .backgroundColor(option.value)
                  .border({ width: this.editColorInput === option.id ? 3 : 0, color: this.colors().foreground })
                  .onClick(() => {
                    this.editColorInput = option.id;
                  });
                }, (option: LabelColorOption) => option.id);
              }
              .width('100%');
            }
            .padding(16)
            .backgroundColor(this.colors().surface)
            .borderRadius(8)
            .border({ width: 1, color: this.colors().border })
            .width('100%');

            Button('保存更改')
              .height(48)
              .fontSize(15)
              .fontColor('#FFFFFF')
              .backgroundColor(this.colors().primary)
              .borderRadius(8)
              .width('100%')
              .enabled(!this.isRunning)
              .onClick(() => {
                void this.saveEditedAccount(this.editingAccount() as MfaAccount);
              });
          }
          .padding(16)
          .width('100%');
        }
        .width('100%')
        .height('100%');
      } else {
        Text('账户不存在')
          .fontColor(this.colors().mutedForeground);
      }
    }
    .title('编辑账户')
    .backgroundColor(this.colors().background);
  }

  @Builder
  private backupDestination() {
    NavDestination() {
      Scroll() {
        Column({ space: 16 }) {
          Column({ space: 12 }) {
            Text('外观')
              .fontSize(18)
              .fontWeight(FontWeight.Bold)
              .fontColor(this.colors().foreground)
              .width('100%');
            Text('选择界面主题')
              .fontSize(12)
              .fontColor(this.colors().mutedForeground)
              .width('100%');
            Row({ space: 8 }) {
              ForEach(['system', 'light', 'dark'], (theme: string) => {
                Button(theme === 'system' ? '跟随系统' : (theme === 'light' ? '浅色' : '深色'))
                  .height(38)
                  .fontSize(12)
                  .fontColor(this.themePreference === theme ? '#FFFFFF' : this.colors().foreground)
                  .backgroundColor(this.themePreference === theme ? this.colors().primary : this.colors().input)
                  .borderRadius(6)
                  .layoutWeight(1)
                  .onClick(() => {
                    void this.setThemePreference(theme);
                  });
              }, (theme: string) => theme);
            }
            .width('100%');
          }
          .padding(16)
          .backgroundColor(this.colors().surface)
          .borderRadius(8)
          .border({ width: 1, color: this.colors().border })
          .width('100%');

          Column({ space: 12 }) {
            Row({ space: 8 }) {
              SymbolGlyph($r('sys.symbol.lock'))
                .fontSize(20)
                .fontColor([this.colors().primary]);
              Text('系统身份认证')
                .fontSize(18)
                .fontWeight(FontWeight.Bold)
                .fontColor(this.colors().foreground)
                .layoutWeight(1);
            }
            .width('100%');
            Text('指纹、面容或手机锁屏凭据均由 HarmonyOS 系统完成验证，应用不会读取或保存系统密码。')
              .fontSize(13)
              .lineHeight(20)
              .fontColor(this.colors().mutedForeground)
              .width('100%');
            Row({ space: 12 }) {
              Column({ space: 3 }) {
                Text('进入 App 时验证身份')
                  .fontSize(14)
                  .fontWeight(FontWeight.Medium)
                  .fontColor(this.colors().foreground)
                  .width('100%');
                Text('首次安装默认关闭；开启后，从后台返回超过一分钟需要验证。')
                  .fontSize(12)
                  .lineHeight(18)
                  .fontColor(this.colors().mutedForeground)
                  .width('100%');
              }
              .layoutWeight(1);
              Toggle({ type: ToggleType.Switch, isOn: this.appLockEnabled })
                .selectedColor(this.colors().primary)
                .onChange((enabled: boolean) => {
                  void this.setAppLockEnabled(enabled);
                });
            }
            .width('100%');
            Text('关闭应用锁不影响查看或复制密钥、导出备份时的系统身份验证。')
              .fontSize(12)
              .lineHeight(18)
              .fontColor(this.colors().mutedForeground)
              .backgroundColor(this.colors().primarySoft)
              .padding(10)
              .borderRadius(8)
              .width('100%');
            Button('立即锁定应用')
              .height(42)
              .fontSize(14)
              .fontColor(this.colors().foreground)
              .backgroundColor(this.colors().input)
              .borderRadius(8)
              .width('100%')
              .onClick(() => {
                this.pathStack.clear();
                this.lockApplication();
              });
          }
          .padding(16)
          .backgroundColor(this.colors().surface)
          .borderRadius(8)
          .border({ width: 1, color: this.colors().border })
          .width('100%');

          Column({ space: 12 }) {
            Row({ space: 8 }) {
              SymbolGlyph($r('sys.symbol.qrcode'))
                .fontSize(20)
                .fontColor([this.colors().primary]);
              Text('二维码安全迁移')
                .fontSize(18)
                .fontWeight(FontWeight.Bold)
                .fontColor(this.colors().foreground)
                .layoutWeight(1);
            }
            .width('100%');
            Text('两台设备面对面加密传输，不经过云端；相同密钥自动跳过。')
              .fontSize(12)
              .lineHeight(19)
              .fontColor(this.colors().mutedForeground)
              .width('100%');
            Button('开始二维码迁移')
              .height(46)
              .fontSize(14)
              .fontColor('#FFFFFF')
              .backgroundColor(this.colors().primary)
              .borderRadius(8)
              .width('100%')
              .onClick(() => {
                this.resetQrTransfer();
                this.pathStack.pushPathByName('qrTransfer', null);
              });
          }
          .padding(16)
          .backgroundColor(this.colors().surface)
          .borderRadius(8)
          .border({ width: 1, color: this.colors().border })
          .width('100%');

          Column({ space: 12 }) {
            Text('导出备份')
              .fontSize(18)
              .fontWeight(FontWeight.Bold)
              .fontColor(this.colors().foreground)
              .width('100%');
            Text(`共 ${this.accounts.length} 个账户`)
              .fontSize(12)
              .fontColor(this.colors().mutedForeground)
              .width('100%');
            Text('导出前需要验证身份。备份始终加密，并生成仅用于本次备份的恢复密钥。')
              .fontSize(12)
              .lineHeight(19)
              .fontColor(this.colors().mutedForeground)
              .backgroundColor(this.colors().primarySoft)
              .padding(10)
              .borderRadius(8)
              .width('100%');
            Button('导出备份')
              .height(46)
              .fontSize(14)
              .fontColor('#FFFFFF')
              .backgroundColor(this.colors().primary)
              .borderRadius(8)
              .width('100%')
              .enabled(!this.isRunning && this.accounts.length > 0)
              .onClick(() => {
                void this.prepareEncryptedBackup();
              });
            if (this.recoveryKey.length > 0) {
              Text('恢复密钥')
                .fontSize(12)
                .fontColor(this.colors().mutedForeground)
                .width('100%');
              Text(formatRecoveryKey(this.recoveryKey))
                .fontSize(14)
                .lineHeight(21)
                .fontWeight(FontWeight.Medium)
                .fontColor(this.colors().foreground)
                .backgroundColor(this.colors().input)
                .padding(12)
                .borderRadius(6)
                .copyOption(CopyOptions.None)
                .width('100%');
              Button(this.recoveryKeyCopied ? '恢复密钥已复制' : '复制恢复密钥')
                .height(42)
                .fontSize(13)
                .fontColor(this.colors().primary)
                .backgroundColor(this.colors().primarySoft)
                .borderRadius(8)
                .width('100%')
                .onClick(() => {
                  void this.copyRecoveryKey();
                });
              Row({ space: 8 }) {
                Button('保存文件')
                  .height(42)
                  .fontSize(13)
                  .fontColor(this.colors().foreground)
                  .backgroundColor(this.colors().input)
                  .borderRadius(8)
                  .layoutWeight(1)
                  .enabled(this.recoveryKeyCopied && !this.isRunning)
                  .onClick(() => {
                    void this.savePreparedBackup();
                  });
                Button('系统分享')
                  .height(42)
                  .fontSize(13)
                  .fontColor(this.colors().foreground)
                  .backgroundColor(this.colors().input)
                  .borderRadius(8)
                  .layoutWeight(1)
                  .enabled(this.recoveryKeyCopied && !this.isRunning)
                  .onClick(() => {
                    void this.sharePreparedBackup();
                  });
              }
              .width('100%');
              Button('清除本次备份')
                .height(38)
                .fontSize(12)
                .fontColor(this.colors().destructive)
                .backgroundColor('#00000000')
                .width('100%')
                .onClick(() => {
                  this.clearPreparedBackup();
                });
            }
          }
          .padding(16)
          .backgroundColor(this.colors().surface)
          .borderRadius(8)
          .border({ width: 1, color: this.colors().border })
          .width('100%');

          Column({ space: 12 }) {
            Text('导入备份')
              .fontSize(18)
              .fontWeight(FontWeight.Bold)
              .fontColor(this.colors().foreground)
              .width('100%');
            Text('选择加密备份文件并输入单独保存的恢复密钥。')
              .fontSize(12)
              .fontColor(this.colors().mutedForeground)
              .width('100%');
            Button(this.selectedBackupReady ? '已选择备份文件' : '选择备份文件')
              .height(44)
              .fontSize(14)
              .fontColor(this.colors().foreground)
              .backgroundColor(this.colors().input)
              .borderRadius(8)
              .width('100%')
              .onClick(() => {
                void this.chooseBackupFile();
              });
            TextInput({ placeholder: '输入单独保存的恢复密钥', text: this.importRecoveryKey })
              .type(InputType.Password)
              .showPasswordIcon(true)
              .height(46)
              .fontColor(this.colors().foreground)
              .placeholderColor(this.colors().mutedForeground)
              .backgroundColor(this.colors().input)
              .borderRadius(8)
              .onChange((value: string) => {
                this.importRecoveryKey = value;
              });
            Button('导入加密备份')
              .height(46)
              .fontSize(14)
              .fontColor('#FFFFFF')
              .backgroundColor(this.colors().primary)
              .borderRadius(8)
              .width('100%')
              .enabled(this.selectedBackupReady && !this.isRunning)
              .onClick(() => {
                void this.importSelectedBackup();
              });
          }
          .padding(16)
          .backgroundColor(this.colors().surface)
          .borderRadius(8)
          .border({ width: 1, color: this.colors().border })
          .width('100%');


          this.statusStrip();
        }
        .padding(16)
        .width('100%');
      }
      .width('100%')
      .height('100%');
    }
    .title('备份与恢复')
    .backgroundColor(this.colors().background);
  }

  @Builder
  private qrTransferDestination() {
    NavDestination() {
      Scroll() {
        Column({ space: 16 }) {
          Column({ space: 10 }) {
            Row({ space: 8 }) {
              SymbolGlyph($r('sys.symbol.lock'))
                .fontSize(20)
                .fontColor([this.colors().primary]);
              Text('一次性公钥加密')
                .fontSize(17)
                .fontWeight(FontWeight.Bold)
                .fontColor(this.colors().foreground);
            }
            .width('100%');
            Text('配对二维码仅包含临时公钥，账户二维码仅包含密文。私钥只在接收设备内存中保存，完成或取消后立即清除。')
              .fontSize(13)
              .lineHeight(20)
              .fontColor(this.colors().mutedForeground)
              .width('100%');
          }
          .padding(16)
          .backgroundColor(this.colors().primarySoft)
          .borderRadius(8)
          .width('100%');

          if (this.qrTransferMode === 'choice') {
            Column({ space: 12 }) {
              Button('接收账户')
                .height(50)
                .fontSize(15)
                .fontColor('#FFFFFF')
                .backgroundColor(this.colors().primary)
                .borderRadius(8)
                .width('100%')
                .enabled(!this.isRunning)
                .onClick(() => {
                  void this.startReceivingQrTransfer();
                });
              Button(`发送 ${this.accounts.length} 个账户`)
                .height(50)
                .fontSize(15)
                .fontColor(this.colors().foreground)
                .backgroundColor(this.colors().input)
                .borderRadius(8)
                .width('100%')
                .enabled(!this.isRunning && this.accounts.length > 0)
                .onClick(() => {
                  void this.startSendingQrTransfer();
                });
            }
            .padding(16)
            .backgroundColor(this.colors().surface)
            .borderRadius(8)
            .border({ width: 1, color: this.colors().border })
            .width('100%');
          } else if (this.qrTransferMode === 'receive') {
            Column({ space: 12 }) {
              Text('让旧设备扫描此配对码')
                .fontSize(18)
                .fontWeight(FontWeight.Bold)
                .fontColor(this.colors().foreground);
              Text('配对码只包含一次性公钥，5 分钟后失效。')
                .fontSize(12)
                .fontColor(this.colors().mutedForeground);
              QRCode(this.qrPairingValue)
                .width(260)
                .height(260)
                .color('#000000')
                .backgroundColor('#FFFFFF');
              Button('开始连续扫描')
                .height(48)
                .fontSize(14)
                .fontColor('#FFFFFF')
                .backgroundColor(this.colors().primary)
                .borderRadius(8)
                .width('100%')
                .enabled(!this.isRunning)
                .onClick(() => {
                  void this.openQrContinuousScanner();
                });
              Button('取消本次接收')
                .height(40)
                .fontSize(13)
                .fontColor(this.colors().destructive)
                .backgroundColor('#00000000')
                .onClick(() => {
                  this.resetQrTransfer();
                });
            }
            .alignItems(HorizontalAlign.Center)
            .padding(16)
            .backgroundColor(this.colors().surface)
            .borderRadius(8)
            .border({ width: 1, color: this.colors().border })
            .width('100%');
          } else if (this.qrTransferMode === 'receive-scan') {
            Column({ space: 12 }) {
              Text('连续扫描账户二维码')
                .fontSize(18)
                .fontWeight(FontWeight.Bold)
                .fontColor(this.colors().foreground);
              Text('保持镜头对准旧设备，无需点击下一张；三张二维码会自动收集。')
                .fontSize(12)
                .fontColor(this.colors().mutedForeground)
                .textAlign(TextAlign.Center);
              QrContinuousScanner({
                onDetected: async (value: string): Promise<boolean> => await this.consumeQrTransferPart(value),
                onError: (message: string): void => {
                  this.status = `连续扫码异常，正在重试：${message}`;
                }
              });
              if (this.qrExpectedParts > 0) {
                Text(`已接收 ${this.qrReceivedParts}/${this.qrExpectedParts} 张`)
                  .fontSize(14)
                  .fontWeight(FontWeight.Medium)
                  .fontColor(this.colors().primary);
              }
              Button('取消本次接收')
                .height(40)
                .fontSize(13)
                .fontColor(this.colors().destructive)
                .backgroundColor('#00000000')
                .onClick(() => {
                  this.resetQrTransfer();
                });
            }
            .alignItems(HorizontalAlign.Center)
            .padding(16)
            .backgroundColor(this.colors().surface)
            .borderRadius(8)
            .border({ width: 1, color: this.colors().border })
            .width('100%');
          } else if (this.qrTransferMode === 'send' && this.qrTransferParts.length > 0) {
            Column({ space: 12 }) {
              Text('让新设备扫描账户码')
                .fontSize(18)
                .fontWeight(FontWeight.Bold)
                .fontColor(this.colors().foreground);
              Text('仅刚才配对的接收设备可以解密。请保持新设备对准，二维码会自动轮播。')
                .fontSize(12)
                .fontColor(this.colors().mutedForeground);
              QRCode(this.qrTransferParts[this.qrTransferPartIndex])
                .width(260)
                .height(260)
                .color('#000000')
                .backgroundColor('#FFFFFF');
              Text(`第 ${this.qrTransferPartIndex + 1}/${this.qrTransferParts.length} 张`)
                .fontSize(14)
                .fontWeight(FontWeight.Medium)
                .fontColor(this.colors().primary);
              if (this.qrTransferParts.length > 1) {
                Row({ space: 8 }) {
                  Button('上一张')
                    .height(42)
                    .fontSize(13)
                    .fontColor(this.colors().foreground)
                    .backgroundColor(this.colors().input)
                    .layoutWeight(1)
                    .onClick(() => {
                      this.qrTransferPartIndex = (this.qrTransferPartIndex - 1 +
                        this.qrTransferParts.length) % this.qrTransferParts.length;
                    });
                  Button(this.qrTransferAutoAdvance ? '暂停轮播' : '自动轮播')
                    .height(42)
                    .fontSize(13)
                    .fontColor(this.colors().foreground)
                    .backgroundColor(this.colors().input)
                    .layoutWeight(1)
                    .onClick(() => {
                      this.toggleQrTransferAutoAdvance();
                    });
                  Button('下一张')
                    .height(42)
                    .fontSize(13)
                    .fontColor(this.colors().foreground)
                    .backgroundColor(this.colors().input)
                    .layoutWeight(1)
                    .onClick(() => {
                      this.qrTransferPartIndex = (this.qrTransferPartIndex + 1) % this.qrTransferParts.length;
                    });
                }
                .width('100%');
              }
              Button('完成并清除二维码')
                .height(42)
                .fontSize(13)
                .fontColor(this.colors().destructive)
                .backgroundColor('#00000000')
                .onClick(() => {
                  this.resetQrTransfer();
                });
            }
            .alignItems(HorizontalAlign.Center)
            .padding(16)
            .backgroundColor(this.colors().surface)
            .borderRadius(8)
            .border({ width: 1, color: this.colors().border })
            .width('100%');
          }

          this.statusStrip();
        }
        .padding(16)
        .width('100%');
      }
      .width('100%')
      .height('100%');
    }
    .title('二维码安全迁移')
    .backgroundColor(this.colors().background);
  }

  @Builder
  private groupsDestination() {
    NavDestination() {
      Scroll() {
        Column({ space: 12 }) {
          if (this.accountGroups(this.accounts).length === 0) {
            Text('暂无分组')
              .fontSize(15)
              .fontColor(this.colors().mutedForeground)
              .padding({ top: 60 });
          }
          ForEach(this.accountGroups(this.accounts), (group: string) => {
            Column({ space: 10 }) {
              Row({ space: 8 }) {
                Text(group)
                  .fontSize(16)
                  .fontWeight(FontWeight.Medium)
                  .fontColor(this.colors().foreground)
                  .layoutWeight(1);
                Text(`${this.accountsInGroup(this.accounts, group).length} 个账户`)
                  .fontSize(11)
                  .fontColor(this.colors().mutedForeground);
              }
              .width('100%');
              if (this.editingGroupName === group) {
                TextInput({ text: this.renameGroupInput })
                  .height(42)
                  .backgroundColor(this.colors().input)
                  .fontColor(this.colors().foreground)
                  .borderRadius(8)
                  .onChange((value: string) => {
                    this.renameGroupInput = value;
                  });
                Row({ space: 8 }) {
                  Button('取消')
                    .height(36)
                    .fontSize(12)
                    .backgroundColor(this.colors().input)
                    .fontColor(this.colors().foreground)
                    .borderRadius(6)
                    .layoutWeight(1)
                    .onClick(() => {
                      this.editingGroupName = '';
                      this.renameGroupInput = '';
                    });
                  Button('保存')
                    .height(36)
                    .fontSize(12)
                    .backgroundColor(this.colors().primary)
                    .fontColor('#FFFFFF')
                    .borderRadius(6)
                    .layoutWeight(1)
                    .onClick(() => {
                      void this.renameGroup();
                    });
                }
                .width('100%');
              } else {
                Row({ space: 8 }) {
                  Button('重命名')
                    .height(36)
                    .fontSize(12)
                    .backgroundColor(this.colors().input)
                    .fontColor(this.colors().foreground)
                    .borderRadius(6)
                    .layoutWeight(1)
                    .onClick(() => {
                      this.editingGroupName = group;
                      this.renameGroupInput = group;
                    });
                  Button('删除分组')
                    .height(36)
                    .fontSize(12)
                    .backgroundColor('#00000000')
                    .fontColor(this.colors().destructive)
                    .border({ width: 1, color: this.colors().destructive })
                    .borderRadius(6)
                    .layoutWeight(1)
                    .onClick(() => {
                      void this.removeGroup(group);
                    });
                }
                .width('100%');
              }
            }
            .padding(14)
            .backgroundColor(this.colors().surface)
            .borderRadius(8)
            .border({ width: 1, color: this.colors().border })
            .width('100%');
          }, (group: string) => group);
        }
        .padding(16)
        .width('100%');
      }
      .width('100%')
      .height('100%');
    }
    .title('管理分组')
    .backgroundColor(this.colors().background);
  }

  @Builder
  private pathMap(routeName: string) {
    if (routeName === 'add') {
      this.addDestination();
    } else if (routeName === 'edit') {
      this.editDestination();
    } else if (routeName === 'backup') {
      this.backupDestination();
    } else if (routeName === 'groups') {
      this.groupsDestination();
    } else if (routeName === 'qrTransfer') {
      this.qrTransferDestination();
    }
  }

  @Builder
  private privacyGate() {
    Column() {
      Blank().layoutWeight(1);
      Column({ space: 14 }) {
        Text('留白 MFA 隐私政策')
          .fontSize(22)
          .fontWeight(FontWeight.Bold)
          .fontColor(this.colors().foreground)
          .width('100%');
        Text('欢迎使用留白 MFA。我们重视你的隐私和 MFA 密钥安全。首次使用前，请阅读并同意《留白 MFA 隐私政策》。账户与验证码主要在设备本地处理，密钥保存在系统安全存储中。')
          .fontSize(14)
          .lineHeight(23)
          .fontColor(this.colors().mutedForeground)
          .width('100%');
        Text('如不同意隐私政策，将无法继续使用留白 MFA。')
          .fontSize(13)
          .lineHeight(21)
          .fontColor(this.colors().mutedForeground)
          .width('100%');
        Button('阅读隐私政策')
          .height(44)
          .fontSize(14)
          .fontColor(this.colors().primary)
          .backgroundColor('#00000000')
          .border({ width: 1, color: this.colors().border })
          .borderRadius(8)
          .width('100%')
          .onClick(() => {
            void this.openPrivacyPolicy();
          });
        Row({ space: 10 }) {
          Button('不同意')
            .height(46)
            .fontSize(14)
            .fontColor(this.colors().foreground)
            .backgroundColor('#00000000')
            .border({ width: 1, color: this.colors().border })
            .borderRadius(8)
            .layoutWeight(1)
            .onClick(() => {
              this.status = '需要同意隐私政策后才能使用留白 MFA';
            });
          Button('同意并继续')
            .height(46)
            .fontSize(14)
            .fontColor('#FFFFFF')
            .backgroundColor(this.colors().primary)
            .borderRadius(8)
            .layoutWeight(1)
            .onClick(() => {
              void this.acceptPrivacy();
            });
        }
        .width('100%');
        if (this.status.includes('失败') || this.status.includes('需要同意')) {
          Text(this.status)
            .fontSize(12)
            .fontColor(this.colors().destructive)
            .textAlign(TextAlign.Center)
            .width('100%');
        }
      }
      .padding(22)
      .backgroundColor(this.colors().surface)
      .border({ width: 1, color: this.colors().border })
      .borderRadius(8)
      .width('100%');
      Blank().layoutWeight(1);
    }
    .padding(20)
    .width('100%')
    .height('100%')
    .backgroundColor(this.colors().background);
  }

  @Builder
  private lockScreen() {
    Column() {
      Blank().layoutWeight(1);
      Stack() {
        Circle({ width: 84, height: 84 })
          .fill(this.colors().primarySoft);
        SymbolGlyph($r('sys.symbol.lock'))
          .fontSize(38)
          .fontColor([this.colors().primary]);
      }
      .width(84)
      .height(84);
      Text('留白 MFA 已锁定')
        .fontSize(24)
        .fontWeight(FontWeight.Bold)
        .fontColor(this.colors().foreground)
        .margin({ top: 22 });
      Text('使用指纹、面容或手机锁屏凭据解锁')
        .fontSize(13)
        .fontColor(this.colors().mutedForeground)
        .margin({ top: 8, bottom: 24 });
      Button('验证身份')
        .height(48)
        .fontSize(15)
        .fontColor('#FFFFFF')
        .backgroundColor(this.colors().primary)
        .borderRadius(8)
        .width(220)
        .enabled(!this.isRunning)
        .onClick(() => {
          void this.unlockApplication();
        });
      if (this.status.includes('失败')) {
        Text(this.status)
          .fontSize(12)
          .fontColor(this.colors().destructive)
          .textAlign(TextAlign.Center)
          .margin({ top: 14 })
          .maxLines(2)
          .width('80%');
      }
      Blank().layoutWeight(1);
    }
    .width('100%')
    .height('100%')
    .backgroundColor(this.colors().background);
  }

  @Builder
  private accountActionDialog() {
    Column() {
      Blank().layoutWeight(1);
      Column({ space: 12 }) {
        if (this.accountActionMode === 'menu') {
          Text('账户操作')
            .fontSize(20)
            .fontWeight(FontWeight.Bold)
            .fontColor(this.colors().foreground)
            .width('100%');
          Text(this.accountById(this.pendingAccountActionId)?.name ?? '')
            .fontSize(13)
            .fontColor(this.colors().mutedForeground)
            .width('100%');
          Button('更改标签颜色')
            .height(44)
            .fontSize(14)
            .fontColor(this.colors().foreground)
            .backgroundColor(this.colors().input)
            .borderRadius(8)
            .width('100%')
            .onClick(() => {
              this.accountActionMode = 'color';
            });
          Button('移动到分组')
            .height(44)
            .fontSize(14)
            .fontColor(this.colors().foreground)
            .backgroundColor(this.colors().input)
            .borderRadius(8)
            .width('100%')
            .onClick(() => {
              this.accountActionMode = 'group';
            });
        } else if (this.accountActionMode === 'color') {
          Text('更改标签颜色')
            .fontSize(20)
            .fontWeight(FontWeight.Bold)
            .fontColor(this.colors().foreground)
            .width('100%');
          Flex({ wrap: FlexWrap.Wrap, justifyContent: FlexAlign.SpaceBetween }) {
            ForEach(LABEL_COLORS, (option: LabelColorOption) => {
              Button({ type: ButtonType.Circle })
                .width(42)
                .height(42)
                .backgroundColor(option.value)
                .accessibilityText(option.label)
                .onClick(() => {
                  void this.updateAccountPresentation(this.pendingAccountActionId, option.id, undefined);
                });
            }, (option: LabelColorOption) => option.id);
          }
          .width('100%');
        } else if (this.accountActionMode === 'group') {
          Text('移动到分组')
            .fontSize(20)
            .fontWeight(FontWeight.Bold)
            .fontColor(this.colors().foreground)
            .width('100%');
          Button('未分组')
            .height(42)
            .fontSize(14)
            .fontColor(this.colors().foreground)
            .backgroundColor(this.colors().input)
            .borderRadius(8)
            .width('100%')
            .onClick(() => {
              void this.updateAccountPresentation(this.pendingAccountActionId, undefined, '');
            });
          ForEach(this.accountGroups(this.accounts), (group: string) => {
            Button(group)
              .height(42)
              .fontSize(14)
              .fontColor(this.colors().foreground)
              .backgroundColor(this.colors().input)
              .borderRadius(8)
              .width('100%')
              .onClick(() => {
                void this.updateAccountPresentation(this.pendingAccountActionId, undefined, group);
              });
          }, (group: string) => group);
          Button('新建分组')
            .height(42)
            .fontSize(14)
            .fontColor(this.colors().primary)
            .backgroundColor(this.colors().primarySoft)
            .borderRadius(8)
            .width('100%')
            .onClick(() => {
              this.moveGroupInput = '';
              this.accountActionMode = 'group-new';
            });
        } else {
          Text('新建分组')
            .fontSize(20)
            .fontWeight(FontWeight.Bold)
            .fontColor(this.colors().foreground)
            .width('100%');
          TextInput({ placeholder: '输入新分组名称', text: this.moveGroupInput })
            .height(46)
            .backgroundColor(this.colors().input)
            .fontColor(this.colors().foreground)
            .placeholderColor(this.colors().mutedForeground)
            .borderRadius(8)
            .onChange((value: string) => {
              this.moveGroupInput = value;
            });
          Button('创建并移动')
            .height(44)
            .fontSize(14)
            .fontColor('#FFFFFF')
            .backgroundColor(this.colors().primary)
            .borderRadius(8)
            .width('100%')
            .enabled(this.moveGroupInput.trim().length > 0)
            .onClick(() => {
              void this.movePendingAccountToNewGroup();
            });
        }
        Button('取消')
          .height(42)
          .fontSize(14)
          .fontColor(this.colors().mutedForeground)
          .backgroundColor('#00000000')
          .borderRadius(8)
          .width('100%')
          .onClick(() => {
            this.pendingAccountActionId = '';
            this.accountActionMode = '';
            this.moveGroupInput = '';
          });
      }
      .padding(20)
      .backgroundColor(this.colors().surfaceRaised)
      .borderRadius(8)
      .width('86%');
      Blank().layoutWeight(1);
    }
    .width('100%')
    .height('100%')
    .backgroundColor(this.colors().scrim);
  }

  @Builder
  private groupActionDialog() {
    Column() {
      Blank().layoutWeight(1);
      Column({ space: 12 }) {
        if (this.groupActionMode === 'menu') {
          Text(this.editingGroupName)
            .fontSize(20)
            .fontWeight(FontWeight.Bold)
            .fontColor(this.colors().foreground)
            .width('100%');
          Button('重命名分组')
            .height(44)
            .fontSize(14)
            .fontColor(this.colors().foreground)
            .backgroundColor(this.colors().input)
            .borderRadius(8)
            .width('100%')
            .onClick(() => {
              this.groupActionMode = 'rename';
            });
          Button('删除分组')
            .height(44)
            .fontSize(14)
            .fontColor(this.colors().destructive)
            .backgroundColor(this.colors().input)
            .borderRadius(8)
            .width('100%')
            .onClick(() => {
              this.groupActionMode = 'delete';
            });
        } else if (this.groupActionMode === 'rename') {
          Text('重命名分组')
            .fontSize(20)
            .fontWeight(FontWeight.Bold)
            .fontColor(this.colors().foreground)
            .width('100%');
          TextInput({ text: this.renameGroupInput })
            .height(46)
            .backgroundColor(this.colors().input)
            .fontColor(this.colors().foreground)
            .borderRadius(8)
            .onChange((value: string) => {
              this.renameGroupInput = value;
            });
          Button('保存')
            .height(44)
            .fontSize(14)
            .fontColor('#FFFFFF')
            .backgroundColor(this.colors().primary)
            .borderRadius(8)
            .width('100%')
            .onClick(() => {
              void this.renameGroup();
            });
        } else {
          Text('删除分组？')
            .fontSize(20)
            .fontWeight(FontWeight.Bold)
            .fontColor(this.colors().foreground)
            .width('100%');
          Text('组内账户会移至未分组，账户和密钥不会删除。')
            .fontSize(13)
            .lineHeight(20)
            .fontColor(this.colors().mutedForeground)
            .width('100%');
          Button('确认删除分组')
            .height(44)
            .fontSize(14)
            .fontColor('#FFFFFF')
            .backgroundColor(this.colors().destructive)
            .borderRadius(8)
            .width('100%')
            .onClick(() => {
              void this.removeGroup(this.editingGroupName);
            });
        }
        Button('取消')
          .height(42)
          .fontSize(14)
          .fontColor(this.colors().mutedForeground)
          .backgroundColor('#00000000')
          .borderRadius(8)
          .width('100%')
          .onClick(() => {
            this.editingGroupName = '';
            this.renameGroupInput = '';
            this.groupActionMode = '';
          });
      }
      .padding(20)
      .backgroundColor(this.colors().surfaceRaised)
      .borderRadius(8)
      .width('86%');
      Blank().layoutWeight(1);
    }
    .width('100%')
    .height('100%')
    .backgroundColor(this.colors().scrim);
  }

  @Builder
  private deleteDialog() {
    Column() {
      Blank().layoutWeight(1);
      Column({ space: 14 }) {
        Text('删除账户？')
          .fontSize(20)
          .fontWeight(FontWeight.Bold)
          .fontColor(this.colors().foreground)
          .width('100%');
        Text('账户及其系统安全存储中的密钥将永久删除。')
          .fontSize(13)
          .lineHeight(20)
          .fontColor(this.colors().mutedForeground)
          .width('100%');
        Row({ space: 8 }) {
          Button('取消')
            .height(42)
            .fontSize(14)
            .fontColor(this.colors().foreground)
            .backgroundColor(this.colors().input)
            .borderRadius(8)
            .layoutWeight(1)
            .onClick(() => {
              this.pendingDeleteId = '';
            });
          Button('删除')
            .height(42)
            .fontSize(14)
            .fontColor('#FFFFFF')
            .backgroundColor(this.colors().destructive)
            .borderRadius(8)
            .layoutWeight(1)
            .onClick(() => {
              void this.confirmPendingDelete();
            });
        }
        .width('100%');
      }
      .padding(20)
      .backgroundColor(this.colors().surfaceRaised)
      .borderRadius(8)
      .width('86%');
      Blank().layoutWeight(1);
    }
    .width('100%')
    .height('100%')
    .backgroundColor(this.colors().scrim);
  }

  build() {
    Stack() {
      if (!this.consentLoaded) {
        Column() {
          LoadingProgress()
            .width(38)
            .height(38)
            .color(this.colors().primary);
        }
        .justifyContent(FlexAlign.Center)
        .width('100%')
        .height('100%')
        .backgroundColor(this.colors().background);
      } else if (!this.privacyAccepted) {
        this.privacyGate();
      } else if (this.appLocked) {
        this.lockScreen();
      } else {
        Navigation(this.pathStack) {
          this.homePage();
        }
        .hideTitleBar(true)
        .mode(NavigationMode.Stack)
        .navDestination(this.pathMap)
        .backgroundColor(this.colors().background)
        .width('100%')
        .height('100%');
      }

      if (this.pendingDeleteId.length > 0) {
        this.deleteDialog();
      }
      if (this.pendingAccountActionId.length > 0) {
        this.accountActionDialog();
      }
      if (this.groupActionMode.length > 0) {
        this.groupActionDialog();
      }
    }
    .width('100%')
    .height('100%')
    .backgroundColor(this.colors().background);
  }
}
