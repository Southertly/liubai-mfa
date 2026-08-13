import { util } from '@kit.ArkTS';
import { cryptoFramework } from '@kit.CryptoArchitectureKit';
import { base64UrlToBytes, bytesToBase64Url, QR_TRANSFER_SESSION_MS } from './QrTransfer';

export interface QrReceiveSession {
  sessionId: string;
  publicKey: string;
  expiresAt: number;
  keyPair: cryptoFramework.KeyPair;
}

const encoder: util.TextEncoder = new util.TextEncoder();
const decoder: util.TextDecoder = new util.TextDecoder();
const RSA_ALGORITHM: string = 'RSA2048|PKCS1_OAEP|SHA256|MGF1_SHA1';

export async function createQrReceiveSession(): Promise<QrReceiveSession> {
  const generator = cryptoFramework.createAsyKeyGenerator('RSA2048');
  const keyPair = await generator.generateKeyPair();
  return {
    sessionId: bytesToBase64Url(cryptoFramework.createRandom().generateRandomSync(16).data),
    publicKey: bytesToBase64Url(keyPair.pubKey.getEncoded().data),
    expiresAt: Date.now() + QR_TRANSFER_SESSION_MS,
    keyPair
  };
}

export async function wrapQrTransferKey(publicKey: string, keyMaterial: string): Promise<string> {
  const generator = cryptoFramework.createAsyKeyGenerator('RSA2048');
  const keyPair = await generator.convertKey({ data: base64UrlToBytes(publicKey) }, null);
  const cipher = cryptoFramework.createCipher(RSA_ALGORITHM);
  await cipher.init(cryptoFramework.CryptoMode.ENCRYPT_MODE, keyPair.pubKey, null);
  const encrypted = await cipher.doFinal({ data: encoder.encodeInto(keyMaterial) });
  return bytesToBase64Url(encrypted.data);
}

export async function unwrapQrTransferKey(session: QrReceiveSession, wrappedKey: string): Promise<string> {
  if (session.expiresAt < Date.now()) {
    clearQrReceiveSession(session);
    throw new Error('QR receive session has expired.');
  }
  const cipher = cryptoFramework.createCipher(RSA_ALGORITHM);
  await cipher.init(cryptoFramework.CryptoMode.DECRYPT_MODE, session.keyPair.priKey, null);
  try {
    const decrypted = await cipher.doFinal({ data: base64UrlToBytes(wrappedKey) });
    return decoder.decodeToString(decrypted.data);
  } finally {
    clearQrReceiveSession(session);
  }
}

export function clearQrReceiveSession(session: QrReceiveSession | undefined): void {
  if (session !== undefined) {
    session.keyPair.priKey.clearMem();
  }
}
