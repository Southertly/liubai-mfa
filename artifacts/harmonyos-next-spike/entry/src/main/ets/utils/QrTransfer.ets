import { util } from '@kit.ArkTS';
import { SecureEnvelope } from './BackupCrypto';

export const QR_TRANSFER_SESSION_MS: number = 5 * 60 * 1000;
export const QR_TRANSFER_MAX_PARTS: number = 64;
export const QR_TRANSFER_MAX_RENDERED_CHARS: number = 512;
export const QR_TRANSFER_SCAN_KIT_CHUNK_SIZE: number = 480;
export const QR_TRANSFER_SCAN_KIT_MAX_RENDERED_CHARS: number = 512;
// ArkUI QRCode enforces a 512-character limit. MFAQ3D uses an ASCII-only Z85
// envelope to avoid cross-platform Unicode QR decoding differences while
// keeping the current four-account payload at three frames.
const QR_TRANSFER_FALLBACK_CHUNK_SIZE: number = 480;
const PAIR_PREFIX: string = 'MFAQ1P';
const LEGACY_DATA_PREFIX: string = 'MFAQ1D';
const LEGACY_DENSE_DATA_PREFIX: string = 'MFAQ2D';
const ASCII_DATA_PREFIX: string = 'MFA3';
const DENSE_MARKER: string = 'D:';
const ASCII_MARKER: string = 'Z';
const DENSE_CODE_POINT_BASE: number = 0x4e00;
const DENSE_CODE_POINT_LIMIT: number = DENSE_CODE_POINT_BASE + (1 << 14);
const Z85_ALPHABET: string = '0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ.-:+=^!/*?&<>()[]{}@%$#';
const QR_WRAPPED_KEY_BYTES: number = 256;
const QR_IV_BYTES: number = 16;
const QR_MAC_BYTES: number = 32;

export interface PairingQr {
  sessionId: string;
  expiresAt: number;
  publicKey: string;
}

export interface TransferChunk {
  sessionId: string;
  index: number;
  total: number;
  data: string;
}

export interface EncryptedTransferPackage {
  version: number;
  sessionId: string;
  wrappedKey: string;
  data: SecureEnvelope;
}

interface CompactEncryptedTransferPackage {
  v: number;
  s: string;
  w: string;
  i: string;
  c: string;
  m: string;
}

const base64: util.Base64Helper = new util.Base64Helper();
const encoder: util.TextEncoder = new util.TextEncoder();
const decoder: util.TextDecoder = new util.TextDecoder();

export function bytesToBase64Url(value: Uint8Array): string {
  let encoded = base64.encodeToStringSync(value, util.Type.BASIC_URL_SAFE);
  while (encoded.endsWith('=')) {
    encoded = encoded.substring(0, encoded.length - 1);
  }
  return encoded;
}

export function base64UrlToBytes(value: string): Uint8Array {
  validateToken(value, 'base64url data');
  let padded = value;
  while (padded.length % 4 !== 0) {
    padded += '=';
  }
  return base64.decodeSync(padded, util.Type.BASIC_URL_SAFE);
}

export function utf8ToBase64Url(value: string): string {
  return bytesToBase64Url(encoder.encodeInto(value));
}

export function base64UrlToUtf8(value: string): string {
  return decoder.decodeToString(base64UrlToBytes(value));
}

export function buildPairingQr(pairing: PairingQr): string {
  validateToken(pairing.sessionId, 'session id');
  validateToken(pairing.publicKey, 'public key');
  if (!Number.isSafeInteger(pairing.expiresAt) || pairing.expiresAt <= Date.now()) {
    throw new Error('Pairing session has expired.');
  }
  const value = `${PAIR_PREFIX}|${pairing.sessionId}|${pairing.expiresAt}|${pairing.publicKey}`;
  if (value.length > QR_TRANSFER_MAX_RENDERED_CHARS) {
    throw new Error('Pairing QR code is too large to display safely.');
  }
  return value;
}

export function parsePairingQr(value: string, now: number = Date.now()): PairingQr {
  const parts = value.trim().split('|');
  if (parts.length !== 4 || parts[0] !== PAIR_PREFIX) {
    throw new Error('Not a supported pairing QR code.');
  }
  const expiresAt = Number(parts[2]);
  validateToken(parts[1], 'session id');
  validateToken(parts[3], 'public key');
  if (!Number.isSafeInteger(expiresAt) || expiresAt < now || expiresAt > now + QR_TRANSFER_SESSION_MS + 30000) {
    throw new Error('Pairing QR code has expired.');
  }
  return { sessionId: parts[1], expiresAt, publicKey: parts[3] };
}

export function encodeTransferPackage(
  payload: EncryptedTransferPackage,
  chunkSize: number = QR_TRANSFER_FALLBACK_CHUNK_SIZE,
  maxRenderedChars: number = QR_TRANSFER_MAX_RENDERED_CHARS
): string[] {
  validateTransferPackage(payload);
  if (!Number.isInteger(chunkSize) || chunkSize < 1 || chunkSize > QR_TRANSFER_SCAN_KIT_CHUNK_SIZE ||
    !Number.isInteger(maxRenderedChars) || maxRenderedChars !== QR_TRANSFER_MAX_RENDERED_CHARS) {
    throw new Error('Invalid QR transfer rendering limits.');
  }
  const encoded = encodeAsciiPackage(payload);
  let selectedChunkSize: number = chunkSize;
  let total: number = Math.ceil(encoded.length / selectedChunkSize);
  while (total > 0 && total <= QR_TRANSFER_MAX_PARTS) {
    const digits: number = total.toString().length;
    const headerLength: number = ASCII_DATA_PREFIX.length + payload.sessionId.length + (digits * 2) + 4;
    const safeChunkSize: number = Math.min(chunkSize, maxRenderedChars - headerLength);
    const adjustedTotal: number = Math.ceil(encoded.length / safeChunkSize);
    selectedChunkSize = safeChunkSize;
    if (adjustedTotal === total) {
      break;
    }
    total = adjustedTotal;
  }
  if (total < 1 || total > QR_TRANSFER_MAX_PARTS) {
    throw new Error('Too many accounts for QR transfer.');
  }
  const chunks: string[] = [];
  for (let index = 0; index < total; index++) {
    const chunk = encoded.substring(index * selectedChunkSize,
      Math.min((index + 1) * selectedChunkSize, encoded.length));
    const value = `${ASCII_DATA_PREFIX}|${payload.sessionId}|${index + 1}|${total}|${chunk}`;
    if (value.length > maxRenderedChars) {
      throw new Error('Account QR code is too large to display safely.');
    }
    chunks.push(value);
  }
  return chunks;
}

export function parseTransferChunk(value: string): TransferChunk {
  const parts = value.trim().split('|');
  if (parts.length !== 5 || (parts[0] !== LEGACY_DATA_PREFIX &&
    parts[0] !== LEGACY_DENSE_DATA_PREFIX && parts[0] !== ASCII_DATA_PREFIX)) {
    throw new Error('Not a supported account transfer QR code.');
  }
  validateToken(parts[1], 'session id');
  if (parts[0] === LEGACY_DATA_PREFIX) {
    validateToken(parts[4], 'chunk data');
  } else if (parts[4].length === 0 || parts[4].includes('|')) {
    throw new Error('Invalid compact transfer chunk.');
  }
  const index = Number(parts[2]);
  const total = Number(parts[3]);
  if (!Number.isInteger(index) || !Number.isInteger(total) || total < 1 || total > QR_TRANSFER_MAX_PARTS ||
    index < 1 || index > total) {
    throw new Error('Invalid transfer QR sequence.');
  }
  const data: string = parts[0] === ASCII_DATA_PREFIX && index === 1 ? `${ASCII_MARKER}${parts[4]}` : parts[4];
  return { sessionId: parts[1], index, total, data };
}

export function assembleTransferPackage(
  sessionId: string,
  chunks: Map<number, string>,
  total: number
): EncryptedTransferPackage {
  validateToken(sessionId, 'session id');
  if (total < 1 || total > QR_TRANSFER_MAX_PARTS || chunks.size !== total) {
    throw new Error('Transfer QR sequence is incomplete.');
  }
  let encoded = '';
  for (let index = 1; index <= total; index++) {
    const chunk = chunks.get(index);
    if (chunk === undefined) {
      throw new Error('Transfer QR sequence is incomplete.');
    }
    encoded += chunk;
  }
  let payload: EncryptedTransferPackage;
  try {
    payload = encoded.startsWith(ASCII_MARKER) ? decodeAsciiPackage(encoded) :
      encoded.startsWith(DENSE_MARKER) ? decodeDensePackage(encoded) :
        JSON.parse(base64UrlToUtf8(encoded)) as EncryptedTransferPackage;
  } catch (error) {
    throw new Error('账户二维码数据不完整，请在旧设备重新生成后扫描');
  }
  validateTransferPackage(payload);
  if (payload.sessionId !== sessionId) {
    throw new Error('Transfer session does not match.');
  }
  return payload;
}

function encodeAsciiPackage(payload: EncryptedTransferPackage): string {
  const session: Uint8Array = base64UrlToBytes(payload.sessionId);
  const wrappedKey: Uint8Array = base64UrlToBytes(payload.wrappedKey);
  const iv: Uint8Array = base64ToBytes(payload.data.iv);
  const ciphertext: Uint8Array = base64ToBytes(payload.data.ciphertext);
  const mac: Uint8Array = hexToBytes(payload.data.mac);
  if (session.length !== 16 || wrappedKey.length !== QR_WRAPPED_KEY_BYTES || iv.length !== QR_IV_BYTES ||
    ciphertext.length === 0 || mac.length !== QR_MAC_BYTES) {
    throw new Error('Unsupported compact account transfer payload.');
  }
  const raw: Uint8Array = concatBytes([session, wrappedKey, iv, ciphertext, mac]);
  if (raw.length % 4 !== 0) {
    throw new Error('Unsupported compact account transfer alignment.');
  }
  return encodeZ85(raw);
}

function decodeAsciiPackage(encoded: string): EncryptedTransferPackage {
  if (!encoded.startsWith(ASCII_MARKER) || encoded.length < 2) {
    throw new Error('Invalid ASCII transfer payload.');
  }
  const raw: Uint8Array = decodeZ85(encoded.substring(1));
  const fixedLength: number = 16 + QR_WRAPPED_KEY_BYTES + QR_IV_BYTES + QR_MAC_BYTES;
  if (raw.length <= fixedLength) {
    throw new Error('Invalid ASCII transfer payload.');
  }
  const embeddedSessionId: string = bytesToBase64Url(raw.slice(0, 16));
  const wrappedEnd: number = 16 + QR_WRAPPED_KEY_BYTES;
  const ivEnd: number = wrappedEnd + QR_IV_BYTES;
  const macStart: number = raw.length - QR_MAC_BYTES;
  return {
    version: 1,
    sessionId: embeddedSessionId,
    wrappedKey: bytesToBase64Url(raw.slice(16, wrappedEnd)),
    data: {
      version: 1,
      algorithm: 'A256CBC-HS256',
      iv: bytesToBase64(raw.slice(wrappedEnd, ivEnd)),
      ciphertext: bytesToBase64(raw.slice(ivEnd, macStart)),
      mac: bytesToHex(raw.slice(macStart))
    }
  };
}

function encodeZ85(value: Uint8Array): string {
  if (value.length % 4 !== 0) {
    throw new Error('Invalid Z85 byte length.');
  }
  let result: string = '';
  for (let offset: number = 0; offset < value.length; offset += 4) {
    let block: number = (((value[offset] * 256 + value[offset + 1]) * 256 + value[offset + 2]) * 256 +
      value[offset + 3]);
    const characters: string[] = ['', '', '', '', ''];
    for (let index: number = 4; index >= 0; index--) {
      characters[index] = Z85_ALPHABET[Math.floor(block % 85)];
      block = Math.floor(block / 85);
    }
    result += characters.join('');
  }
  return result;
}

function decodeZ85(value: string): Uint8Array {
  if (value.length === 0 || value.length % 5 !== 0) {
    throw new Error('Invalid Z85 text length.');
  }
  const result: Uint8Array = new Uint8Array((value.length / 5) * 4);
  let output: number = 0;
  for (let offset: number = 0; offset < value.length; offset += 5) {
    let block: number = 0;
    for (let index: number = 0; index < 5; index++) {
      const digit: number = Z85_ALPHABET.indexOf(value[offset + index]);
      if (digit < 0) {
        throw new Error('Invalid Z85 character.');
      }
      block = block * 85 + digit;
    }
    if (block > 0xffffffff) {
      throw new Error('Invalid Z85 block.');
    }
    result[output++] = Math.floor(block / 0x1000000) & 0xff;
    result[output++] = Math.floor(block / 0x10000) & 0xff;
    result[output++] = Math.floor(block / 0x100) & 0xff;
    result[output++] = block & 0xff;
  }
  return result;
}

function concatBytes(values: Uint8Array[]): Uint8Array {
  let length: number = 0;
  for (const value of values) {
    length += value.length;
  }
  const result: Uint8Array = new Uint8Array(length);
  let offset: number = 0;
  for (const value of values) {
    result.set(value, offset);
    offset += value.length;
  }
  return result;
}

function bytesToBase64(value: Uint8Array): string {
  return base64.encodeToStringSync(value, util.Type.BASIC);
}

function base64ToBytes(value: string): Uint8Array {
  if (!isBase64(value)) {
    throw new Error('Invalid base64 data.');
  }
  return base64.decodeSync(value, util.Type.BASIC);
}

function bytesToHex(value: Uint8Array): string {
  const digits: string = '0123456789abcdef';
  let result: string = '';
  for (let index: number = 0; index < value.length; index++) {
    result += digits[(value[index] >> 4) & 0x0f];
    result += digits[value[index] & 0x0f];
  }
  return result;
}

function hexToBytes(value: string): Uint8Array {
  if (value.length === 0 || value.length % 2 !== 0) {
    throw new Error('Invalid hexadecimal data.');
  }
  const result: Uint8Array = new Uint8Array(value.length / 2);
  for (let index: number = 0; index < result.length; index++) {
    const high: number = hexDigit(value[index * 2]);
    const low: number = hexDigit(value[index * 2 + 1]);
    if (high < 0 || low < 0) {
      throw new Error('Invalid hexadecimal data.');
    }
    result[index] = (high << 4) | low;
  }
  return result;
}

function hexDigit(value: string): number {
  if (value >= '0' && value <= '9') {
    return value.charCodeAt(0) - '0'.charCodeAt(0);
  }
  if (value >= 'a' && value <= 'f') {
    return value.charCodeAt(0) - 'a'.charCodeAt(0) + 10;
  }
  if (value >= 'A' && value <= 'F') {
    return value.charCodeAt(0) - 'A'.charCodeAt(0) + 10;
  }
  return -1;
}

function isBase64(value: string): boolean {
  if (value.length === 0 || value.length % 4 !== 0) {
    return false;
  }
  let paddingStarted: boolean = false;
  let paddingCount: number = 0;
  for (let index: number = 0; index < value.length; index++) {
    const character: string = value[index];
    if (character === '=') {
      paddingStarted = true;
      paddingCount++;
      if (paddingCount > 2) {
        return false;
      }
      continue;
    }
    if (paddingStarted) {
      return false;
    }
    const valid: boolean = (character >= 'A' && character <= 'Z') ||
      (character >= 'a' && character <= 'z') || (character >= '0' && character <= '9') ||
      character === '+' || character === '/';
    if (!valid) {
      return false;
    }
  }
  return true;
}

function encodeDensePackage(payload: EncryptedTransferPackage): string {
  const compact: CompactEncryptedTransferPackage = {
    v: 1,
    s: payload.sessionId,
    w: payload.wrappedKey,
    i: payload.data.iv,
    c: payload.data.ciphertext,
    m: payload.data.mac
  };
  return encodeDenseAscii(JSON.stringify(compact));
}

function decodeDensePackage(encoded: string): EncryptedTransferPackage {
  const compact = JSON.parse(decodeDenseAscii(encoded)) as CompactEncryptedTransferPackage;
  if (compact.v !== 1 || typeof compact.s !== 'string' || typeof compact.w !== 'string' ||
    typeof compact.i !== 'string' || typeof compact.c !== 'string' || typeof compact.m !== 'string') {
    throw new Error('Unsupported compact account transfer payload.');
  }
  return {
    version: 1,
    sessionId: compact.s,
    wrappedKey: compact.w,
    data: {
      version: 1,
      algorithm: 'A256CBC-HS256',
      iv: compact.i,
      ciphertext: compact.c,
      mac: compact.m
    }
  };
}

function encodeDenseAscii(value: string): string {
  let buffer: number = 0;
  let bits: number = 0;
  let encoded: string = '';
  for (let index: number = 0; index < value.length; index++) {
    const byte: number = value.charCodeAt(index);
    if (byte > 0x7f) {
      throw new Error('Compact transfer envelope must be ASCII.');
    }
    buffer = (buffer << 8) | byte;
    bits += 8;
    while (bits >= 14) {
      bits -= 14;
      encoded += String.fromCharCode(DENSE_CODE_POINT_BASE + ((buffer >>> bits) & 0x3fff));
      buffer = bits === 0 ? 0 : buffer & ((1 << bits) - 1);
    }
  }
  if (bits > 0) {
    encoded += String.fromCharCode(DENSE_CODE_POINT_BASE + (buffer << (14 - bits)));
  }
  return `${DENSE_MARKER}${value.length.toString(36)}:${encoded}`;
}

function decodeDenseAscii(value: string): string {
  if (!value.startsWith(DENSE_MARKER)) {
    throw new Error('Invalid compact transfer payload.');
  }
  const lengthEnd: number = value.indexOf(':', DENSE_MARKER.length);
  if (lengthEnd < 0) {
    throw new Error('Invalid compact transfer payload.');
  }
  const lengthToken: string = value.substring(DENSE_MARKER.length, lengthEnd);
  if (!isLowerBase36(lengthToken)) {
    throw new Error('Invalid compact transfer length.');
  }
  const expectedLength: number = Number.parseInt(lengthToken, 36);
  const dense: string = value.substring(lengthEnd + 1);
  if (!Number.isSafeInteger(expectedLength) || expectedLength < 1 ||
    dense.length !== Math.ceil((expectedLength * 8) / 14)) {
    throw new Error('Invalid compact transfer length.');
  }

  let buffer: number = 0;
  let bits: number = 0;
  let decoded: string = '';
  for (let index: number = 0; index < dense.length; index++) {
    const codePoint: number = dense.charCodeAt(index);
    if (codePoint < DENSE_CODE_POINT_BASE || codePoint >= DENSE_CODE_POINT_LIMIT) {
      throw new Error('Invalid compact transfer data.');
    }
    buffer = (buffer << 14) | (codePoint - DENSE_CODE_POINT_BASE);
    bits += 14;
    while (bits >= 8 && decoded.length < expectedLength) {
      bits -= 8;
      const byte: number = (buffer >>> bits) & 0xff;
      if (byte > 0x7f) {
        throw new Error('Invalid compact transfer text.');
      }
      decoded += String.fromCharCode(byte);
      buffer = bits === 0 ? 0 : buffer & ((1 << bits) - 1);
    }
  }
  if (decoded.length !== expectedLength || buffer !== 0) {
    throw new Error('Invalid compact transfer padding.');
  }
  return decoded;
}

function isLowerBase36(value: string): boolean {
  if (value.length === 0) {
    return false;
  }
  for (let index: number = 0; index < value.length; index++) {
    const character: string = value[index];
    const valid: boolean = (character >= '0' && character <= '9') ||
      (character >= 'a' && character <= 'z');
    if (!valid) {
      return false;
    }
  }
  return true;
}

function validateToken(value: string, label: string): void {
  if (value.length === 0) {
    throw new Error(`Invalid ${label}.`);
  }
  for (let index = 0; index < value.length; index++) {
    const character = value[index];
    const valid = (character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z') ||
      (character >= '0' && character <= '9') || character === '_' || character === '-';
    if (!valid) {
      throw new Error(`Invalid ${label}.`);
    }
  }
}

function validateTransferPackage(payload: EncryptedTransferPackage): void {
  if (payload.version !== 1 || payload.data.version !== 1 || payload.data.algorithm !== 'A256CBC-HS256' ||
    payload.sessionId.length === 0 || payload.wrappedKey.length === 0 || payload.data.iv.length === 0 ||
    payload.data.ciphertext.length === 0 || payload.data.mac.length === 0) {
    throw new Error('Unsupported account transfer payload.');
  }
  validateToken(payload.sessionId, 'session id');
  validateToken(payload.wrappedKey, 'wrapped key');
}
