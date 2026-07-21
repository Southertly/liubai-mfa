import { util } from '@kit.ArkTS';
import { cryptoFramework } from '@kit.CryptoArchitectureKit';

export const SECURE_ENVELOPE_ALGORITHM: string = 'A256CBC-HS256';

export interface SecureEnvelope {
  version: number;
  algorithm: string;
  iv: string;
  ciphertext: string;
  mac: string;
}

export interface SecureBackupPayload {
  version: number;
  created: number;
  encrypted: boolean;
  keyFormat: string;
  data: SecureEnvelope;
}

export interface PortableAccount {
  id: string;
  issuer: string;
  account: string;
  secret: string;
  createdAt: number;
  digits?: number;
  period?: number;
  group?: string;
  color?: string;
}

interface DerivedKeys {
  encryptionKey: Uint8Array;
  authenticationKey: Uint8Array;
}

const base64: util.Base64Helper = new util.Base64Helper();
const textEncoder: util.TextEncoder = new util.TextEncoder();
const textDecoder: util.TextDecoder = new util.TextDecoder();

function utf8(value: string): Uint8Array {
  return textEncoder.encodeInto(value);
}

function utf8String(value: Uint8Array): string {
  return textDecoder.decodeToString(value);
}

function base64Encode(value: Uint8Array): string {
  return base64.encodeToStringSync(value, util.Type.BASIC);
}

function base64Decode(value: string): Uint8Array {
  return base64.decodeSync(value, util.Type.BASIC);
}

function base64UrlEncode(value: Uint8Array): string {
  const encoded = base64.encodeToStringSync(value, util.Type.BASIC_URL_SAFE);
  let unpadded = encoded;
  while (unpadded.endsWith('=')) {
    unpadded = unpadded.substring(0, unpadded.length - 1);
  }
  return unpadded;
}

function paddedBase64Url(value: string): string {
  let padded = value;
  while (padded.length % 4 !== 0) {
    padded += '=';
  }
  return padded;
}

function base64UrlDecode(value: string): Uint8Array {
  return base64.decodeSync(paddedBase64Url(value), util.Type.BASIC_URL_SAFE);
}

function hexEncode(value: Uint8Array): string {
  const digits = '0123456789abcdef';
  let output = '';
  for (let index = 0; index < value.length; index++) {
    output += digits.charAt((value[index] >> 4) & 0x0f);
    output += digits.charAt(value[index] & 0x0f);
  }
  return output;
}

function constantTimeEqual(left: string, right: string): boolean {
  if (left.length !== right.length) {
    return false;
  }
  let difference = 0;
  for (let index = 0; index < left.length; index++) {
    difference |= left.charCodeAt(index) ^ right.charCodeAt(index);
  }
  return difference === 0;
}

async function hmacSha256(keyBytes: Uint8Array, message: string): Promise<Uint8Array> {
  const keyGenerator = cryptoFramework.createSymKeyGenerator('HMAC');
  const key = await keyGenerator.convertKey({ data: keyBytes });
  const macSpec: cryptoFramework.HmacSpec = { algName: 'HMAC', mdName: 'SHA256' };
  const mac = cryptoFramework.createMac(macSpec);
  await mac.init(key);
  await mac.update({ data: utf8(message) });
  return (await mac.doFinal()).data;
}

async function deriveKeys(keyMaterial: string): Promise<DerivedKeys> {
  const normalized = normalizeRecoveryKey(keyMaterial);
  if (!isValidRecoveryKey(normalized)) {
    throw new Error('Invalid recovery key.');
  }
  const keyBytes = utf8(normalized);
  return {
    encryptionKey: await hmacSha256(keyBytes, 'mfa-verifier:encryption:v1'),
    authenticationKey: await hmacSha256(keyBytes, 'mfa-verifier:authentication:v1')
  };
}

function macInput(envelope: SecureEnvelope): string {
  return `${envelope.version}|${envelope.algorithm}|${envelope.iv}|${envelope.ciphertext}`;
}

async function aesCbc(
  mode: cryptoFramework.CryptoMode,
  keyBytes: Uint8Array,
  iv: Uint8Array,
  input: Uint8Array
): Promise<Uint8Array> {
  const keyGenerator = cryptoFramework.createSymKeyGenerator('AES256');
  const key = await keyGenerator.convertKey({ data: keyBytes });
  const cipher = cryptoFramework.createCipher('AES256|CBC|PKCS7');
  const params: cryptoFramework.IvParamsSpec = {
    algName: 'IvParamsSpec',
    iv: { data: iv }
  };
  await cipher.init(mode, key, params);
  return (await cipher.doFinal({ data: input })).data;
}

export function generateRecoveryKey(): string {
  return base64UrlEncode(cryptoFramework.createRandom().generateRandomSync(32).data);
}

export function normalizeRecoveryKey(value: string): string {
  let normalized = '';
  for (let index = 0; index < value.length; index++) {
    const character = value[index];
    if (character !== ' ' && character !== '\n' && character !== '\r' && character !== '\t') {
      normalized += character;
    }
  }
  return normalized;
}

export function formatRecoveryKey(value: string): string {
  const normalized = normalizeRecoveryKey(value);
  let formatted = '';
  for (let index = 0; index < normalized.length; index++) {
    if (index > 0 && index % 4 === 0) {
      formatted += ' ';
    }
    formatted += normalized[index];
  }
  return formatted;
}

export function isValidRecoveryKey(value: string): boolean {
  const normalized = normalizeRecoveryKey(value);
  if (normalized.length !== 43) {
    return false;
  }
  for (let index = 0; index < normalized.length; index++) {
    const character = normalized[index];
    const valid = (character >= 'A' && character <= 'Z') ||
      (character >= 'a' && character <= 'z') ||
      (character >= '0' && character <= '9') || character === '_' || character === '-';
    if (!valid) {
      return false;
    }
  }
  try {
    return base64UrlDecode(normalized).length === 32;
  } catch (error) {
    return false;
  }
}

export async function encryptString(plaintext: string, keyMaterial: string): Promise<SecureEnvelope> {
  const keys = await deriveKeys(keyMaterial);
  const iv = cryptoFramework.createRandom().generateRandomSync(16).data;
  const ciphertextBytes = await aesCbc(
    cryptoFramework.CryptoMode.ENCRYPT_MODE,
    keys.encryptionKey,
    iv,
    utf8(plaintext)
  );
  const envelope: SecureEnvelope = {
    version: 1,
    algorithm: SECURE_ENVELOPE_ALGORITHM,
    iv: base64Encode(iv),
    ciphertext: base64Encode(ciphertextBytes),
    mac: ''
  };
  envelope.mac = hexEncode(await hmacSha256(keys.authenticationKey, macInput(envelope)));
  return envelope;
}

export async function decryptString(envelope: SecureEnvelope, keyMaterial: string): Promise<string> {
  if (envelope.version !== 1 || envelope.algorithm !== SECURE_ENVELOPE_ALGORITHM ||
    envelope.iv.length === 0 || envelope.ciphertext.length === 0 || envelope.mac.length === 0) {
    throw new Error('Unsupported encrypted payload.');
  }
  const keys = await deriveKeys(keyMaterial);
  const expectedMac = hexEncode(await hmacSha256(keys.authenticationKey, macInput(envelope)));
  if (!constantTimeEqual(expectedMac, envelope.mac.toLowerCase())) {
    throw new Error('Encrypted payload authentication failed.');
  }
  const plaintext = await aesCbc(
    cryptoFramework.CryptoMode.DECRYPT_MODE,
    keys.encryptionKey,
    base64Decode(envelope.iv),
    base64Decode(envelope.ciphertext)
  );
  if (plaintext.length === 0) {
    throw new Error('Unable to decrypt payload.');
  }
  return utf8String(plaintext);
}

export async function verifyMobileV2Compatibility(): Promise<void> {
  const publicRecoveryKey = 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA';
  const expected = '[{"id":"fixture-1","issuer":"CrossPlatform","account":"public-test",' +
    '"secret":"JBSWY3DPEHPK3PXP","createdAt":0}]';
  const fixture: SecureEnvelope = {
    version: 1,
    algorithm: SECURE_ENVELOPE_ALGORITHM,
    iv: 'laAy7tBYm9bAEkr642MimQ==',
    ciphertext: 'j+37vPQJ8wxuahd6/tayGKtxWFuHDNjRFmILx02KV3E+EH1I4tSgL+A/s3fIeFUd' +
      'Ti1AGbaQJV8aJhlBn8b1XDzssGkenpvvyETjy2RjmgTOTQJK4+mxWVgbc6icPwQgDOZvJvCgvCRAj4dYh5wyKA==',
    mac: '1f9622e55affed297059e7198cd26e4d0a99d1dd7b12064305880b87519b59be'
  };
  const decrypted = await decryptString(fixture, publicRecoveryKey);
  if (decrypted !== expected) {
    throw new Error('Mobile v2 compatibility fixture did not match.');
  }

  let wrongKeyRejected = false;
  try {
    await decryptString(fixture, 'BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB');
  } catch (error) {
    wrongKeyRejected = true;
  }
  if (!wrongKeyRejected) {
    throw new Error('Wrong recovery key was not rejected.');
  }

  const tampered: SecureEnvelope = {
    version: fixture.version,
    algorithm: fixture.algorithm,
    iv: fixture.iv,
    ciphertext: `${fixture.ciphertext.substring(0, fixture.ciphertext.length - 2)}AA`,
    mac: fixture.mac
  };
  let tamperRejected = false;
  try {
    await decryptString(tampered, publicRecoveryKey);
  } catch (error) {
    tamperRejected = true;
  }
  if (!tamperRejected) {
    throw new Error('Tampered ciphertext was not rejected.');
  }
}
