import CryptoJS from "crypto-js";

import { SecureEnvelope } from "@/utils/secureCrypto";

export const QR_TRANSFER_VERSION = 1 as const;
export const QR_TRANSFER_MAX_RENDERED_CHARS = 512;
// Both ArkUI QRCode and HarmonyOS Scan Kit reject text above 512 characters.
// MFAQ3D uses an ASCII-only Z85 envelope to avoid cross-platform Unicode QR
// decoding differences while keeping the current four-account payload at
// three frames.
export const QR_TRANSFER_CHUNK_SIZE = 480;
export const QR_TRANSFER_MAX_PARTS = 64;
export const QR_TRANSFER_SESSION_MS = 5 * 60 * 1000;

const PAIR_PREFIX = "MFAQ1P";
const LEGACY_DATA_PREFIX = "MFAQ1D";
const LEGACY_DENSE_DATA_PREFIX = "MFAQ2D";
const ASCII_DATA_PREFIX = "MFA3";
const SAFE_TOKEN = /^[A-Za-z0-9_-]+$/;
const DENSE_MARKER = "D:";
const ASCII_MARKER = "Z";
const DENSE_CODE_POINT_BASE = 0x4e00;
const DENSE_CODE_POINT_LIMIT = DENSE_CODE_POINT_BASE + (1 << 14);
const Z85_ALPHABET = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ.-:+=^!/*?&<>()[]{}@%$#";
const QR_WRAPPED_KEY_BYTES = 256;
const QR_IV_BYTES = 16;
const QR_MAC_BYTES = 32;

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
  version: 1;
  sessionId: string;
  wrappedKey: string;
  data: SecureEnvelope;
}

export interface PortableQrAccount {
  issuer: string;
  account: string;
  secret: string;
  createdAt: number;
  group?: string;
  color?: string;
}

interface CompactEncryptedTransferPackage {
  v: 1;
  s: string;
  w: string;
  i: string;
  c: string;
  m: string;
}

export function utf8ToBase64Url(value: string): string {
  return CryptoJS.enc.Base64url.stringify(CryptoJS.enc.Utf8.parse(value));
}

export function base64UrlToUtf8(value: string): string {
  if (!SAFE_TOKEN.test(value)) throw new Error("Invalid base64url data");
  return CryptoJS.enc.Base64url.parse(value).toString(CryptoJS.enc.Utf8);
}

export function buildPairingQr(pairing: PairingQr): string {
  validateToken(pairing.sessionId, "session id");
  validateToken(pairing.publicKey, "public key");
  if (!Number.isSafeInteger(pairing.expiresAt) || pairing.expiresAt <= Date.now()) {
    throw new Error("Pairing session has expired");
  }
  const value = `${PAIR_PREFIX}|${pairing.sessionId}|${pairing.expiresAt}|${pairing.publicKey}`;
  if (value.length > QR_TRANSFER_MAX_RENDERED_CHARS) {
    throw new Error("Pairing QR code is too large to display safely");
  }
  return value;
}

export function parsePairingQr(value: string, now = Date.now()): PairingQr {
  const parts = value.trim().split("|");
  if (parts.length !== 4 || parts[0] !== PAIR_PREFIX) {
    throw new Error("Not a supported pairing QR code");
  }
  const expiresAt = Number(parts[2]);
  validateToken(parts[1], "session id");
  validateToken(parts[3], "public key");
  if (!Number.isSafeInteger(expiresAt) || expiresAt < now || expiresAt > now + QR_TRANSFER_SESSION_MS + 30_000) {
    throw new Error("Pairing QR code has expired");
  }
  return { sessionId: parts[1], expiresAt, publicKey: parts[3] };
}

export function encodeTransferPackage(payload: EncryptedTransferPackage): string[] {
  validateTransferPackage(payload);
  const encoded = encodeAsciiPackage(payload);
  let chunkSize = QR_TRANSFER_CHUNK_SIZE;
  let total = Math.ceil(encoded.length / chunkSize);
  while (total > 0 && total <= QR_TRANSFER_MAX_PARTS) {
    const digits = String(total).length;
    const headerLength = ASCII_DATA_PREFIX.length + payload.sessionId.length + (digits * 2) + 4;
    const safeChunkSize = Math.min(QR_TRANSFER_CHUNK_SIZE, QR_TRANSFER_MAX_RENDERED_CHARS - headerLength);
    const adjustedTotal = Math.ceil(encoded.length / safeChunkSize);
    chunkSize = safeChunkSize;
    if (adjustedTotal === total) break;
    total = adjustedTotal;
  }
  if (total < 1 || total > QR_TRANSFER_MAX_PARTS) {
    throw new Error("Too many accounts for QR transfer");
  }
  const chunks: string[] = [];
  for (let index = 0; index < total; index += 1) {
    const data = encoded.slice(index * chunkSize, (index + 1) * chunkSize);
    const value = `${ASCII_DATA_PREFIX}|${payload.sessionId}|${index + 1}|${total}|${data}`;
    if (value.length > QR_TRANSFER_MAX_RENDERED_CHARS) {
      throw new Error("Account QR code is too large to display safely");
    }
    chunks.push(value);
  }
  return chunks;
}

export function parseTransferChunk(value: string): TransferChunk {
  const parts = value.trim().split("|");
  if (parts.length !== 5 || (parts[0] !== LEGACY_DATA_PREFIX &&
      parts[0] !== LEGACY_DENSE_DATA_PREFIX && parts[0] !== ASCII_DATA_PREFIX)) {
    throw new Error("Not a supported account transfer QR code");
  }
  validateToken(parts[1], "session id");
  if (parts[0] === LEGACY_DATA_PREFIX) {
    validateToken(parts[4], "chunk data");
  } else if (!parts[4] || parts[4].includes("|")) {
    throw new Error("Invalid compact transfer chunk");
  }
  const index = Number(parts[2]);
  const total = Number(parts[3]);
  if (!Number.isInteger(index) || !Number.isInteger(total) || total < 1 || total > QR_TRANSFER_MAX_PARTS || index < 1 || index > total) {
    throw new Error("Invalid transfer QR sequence");
  }
  const data = parts[0] === ASCII_DATA_PREFIX && index === 1 ? `${ASCII_MARKER}${parts[4]}` : parts[4];
  return { sessionId: parts[1], index, total, data };
}

export function assembleTransferPackage(sessionId: string, chunks: Map<number, string>, total: number): EncryptedTransferPackage {
  validateToken(sessionId, "session id");
  if (!Number.isInteger(total) || total < 1 || total > QR_TRANSFER_MAX_PARTS || chunks.size !== total) {
    throw new Error("Transfer QR sequence is incomplete");
  }
  let encoded = "";
  for (let index = 1; index <= total; index += 1) {
    const chunk = chunks.get(index);
    if (!chunk) throw new Error("Transfer QR sequence is incomplete");
    encoded += chunk;
  }
  let payload: EncryptedTransferPackage;
  try {
    payload = encoded.startsWith(ASCII_MARKER)
      ? decodeAsciiPackage(encoded)
      : encoded.startsWith(DENSE_MARKER)
        ? decodeDensePackage(encoded)
        : JSON.parse(base64UrlToUtf8(encoded)) as EncryptedTransferPackage;
  } catch {
    throw new Error("账户二维码数据不完整，请在旧设备重新生成后扫描");
  }
  validateTransferPackage(payload);
  if (payload.sessionId !== sessionId) throw new Error("Transfer session does not match");
  return payload;
}

function encodeAsciiPackage(payload: EncryptedTransferPackage): string {
  const session = base64UrlToBytes(payload.sessionId);
  const wrappedKey = base64UrlToBytes(payload.wrappedKey);
  const iv = base64ToBytes(payload.data.iv);
  const ciphertext = base64ToBytes(payload.data.ciphertext);
  const mac = hexToBytes(payload.data.mac);
  if (session.length !== 16 || wrappedKey.length !== QR_WRAPPED_KEY_BYTES || iv.length !== QR_IV_BYTES ||
      ciphertext.length === 0 || mac.length !== QR_MAC_BYTES) {
    throw new Error("Unsupported compact account transfer payload");
  }
  const raw = concatBytes(session, wrappedKey, iv, ciphertext, mac);
  if (raw.length % 4 !== 0) throw new Error("Unsupported compact account transfer alignment");
  return encodeZ85(raw);
}

function decodeAsciiPackage(encoded: string): EncryptedTransferPackage {
  if (!encoded.startsWith(ASCII_MARKER) || encoded.length < 2) {
    throw new Error("Invalid ASCII transfer payload");
  }
  const raw = decodeZ85(encoded.slice(1));
  const fixedLength = 16 + QR_WRAPPED_KEY_BYTES + QR_IV_BYTES + QR_MAC_BYTES;
  if (raw.length <= fixedLength) throw new Error("Invalid ASCII transfer payload");
  const embeddedSessionId = bytesToBase64Url(raw.slice(0, 16));
  const wrappedEnd = 16 + QR_WRAPPED_KEY_BYTES;
  const ivEnd = wrappedEnd + QR_IV_BYTES;
  const macStart = raw.length - QR_MAC_BYTES;
  return {
    version: 1,
    sessionId: embeddedSessionId,
    wrappedKey: bytesToBase64Url(raw.slice(16, wrappedEnd)),
    data: {
      version: 1,
      algorithm: "A256CBC-HS256",
      iv: bytesToBase64(raw.slice(wrappedEnd, ivEnd)),
      ciphertext: bytesToBase64(raw.slice(ivEnd, macStart)),
      mac: bytesToHex(raw.slice(macStart)),
    },
  };
}

function encodeZ85(value: Uint8Array): string {
  if (value.length % 4 !== 0) throw new Error("Invalid Z85 byte length");
  let result = "";
  for (let offset = 0; offset < value.length; offset += 4) {
    let block = (((value[offset] * 256 + value[offset + 1]) * 256 + value[offset + 2]) * 256 + value[offset + 3]);
    const encoded = new Array<string>(5);
    for (let index = 4; index >= 0; index -= 1) {
      encoded[index] = Z85_ALPHABET[block % 85];
      block = Math.floor(block / 85);
    }
    result += encoded.join("");
  }
  return result;
}

function decodeZ85(value: string): Uint8Array {
  if (!value || value.length % 5 !== 0) throw new Error("Invalid Z85 text length");
  const result = new Uint8Array((value.length / 5) * 4);
  let output = 0;
  for (let offset = 0; offset < value.length; offset += 5) {
    let block = 0;
    for (let index = 0; index < 5; index += 1) {
      const digit = Z85_ALPHABET.indexOf(value[offset + index]);
      if (digit < 0) throw new Error("Invalid Z85 character");
      block = block * 85 + digit;
    }
    if (block > 0xffffffff) throw new Error("Invalid Z85 block");
    result[output++] = Math.floor(block / 0x1000000) & 0xff;
    result[output++] = Math.floor(block / 0x10000) & 0xff;
    result[output++] = Math.floor(block / 0x100) & 0xff;
    result[output++] = block & 0xff;
  }
  return result;
}

function concatBytes(...values: Uint8Array[]): Uint8Array {
  const result = new Uint8Array(values.reduce((total, value) => total + value.length, 0));
  let offset = 0;
  values.forEach((value) => {
    result.set(value, offset);
    offset += value.length;
  });
  return result;
}

function bytesToBase64Url(value: Uint8Array): string {
  const words: number[] = [];
  for (let index = 0; index < value.length; index += 1) {
    words[index >>> 2] = (words[index >>> 2] || 0) | (value[index] << (24 - (index % 4) * 8));
  }
  return CryptoJS.enc.Base64url.stringify(CryptoJS.lib.WordArray.create(words, value.length));
}

function base64UrlToBytes(value: string): Uint8Array {
  if (!SAFE_TOKEN.test(value)) throw new Error("Invalid base64url data");
  const wordArray = CryptoJS.enc.Base64url.parse(value);
  const result = new Uint8Array(wordArray.sigBytes);
  for (let index = 0; index < wordArray.sigBytes; index += 1) {
    result[index] = (wordArray.words[index >>> 2] >>> (24 - (index % 4) * 8)) & 0xff;
  }
  return result;
}

function bytesToBase64(value: Uint8Array): string {
  const words: number[] = [];
  for (let index = 0; index < value.length; index += 1) {
    words[index >>> 2] = (words[index >>> 2] || 0) | (value[index] << (24 - (index % 4) * 8));
  }
  return CryptoJS.enc.Base64.stringify(CryptoJS.lib.WordArray.create(words, value.length));
}

function base64ToBytes(value: string): Uint8Array {
  if (!/^[A-Za-z0-9+/]+={0,2}$/.test(value)) throw new Error("Invalid base64 data");
  const wordArray = CryptoJS.enc.Base64.parse(value);
  const result = new Uint8Array(wordArray.sigBytes);
  for (let index = 0; index < wordArray.sigBytes; index += 1) {
    result[index] = (wordArray.words[index >>> 2] >>> (24 - (index % 4) * 8)) & 0xff;
  }
  return result;
}

function bytesToHex(value: Uint8Array): string {
  let result = "";
  for (const byte of value) result += byte.toString(16).padStart(2, "0");
  return result;
}

function hexToBytes(value: string): Uint8Array {
  if (!/^[0-9a-fA-F]+$/.test(value) || value.length % 2 !== 0) throw new Error("Invalid hexadecimal data");
  const result = new Uint8Array(value.length / 2);
  for (let index = 0; index < result.length; index += 1) {
    result[index] = Number.parseInt(value.slice(index * 2, index * 2 + 2), 16);
  }
  return result;
}

function encodeDensePackage(payload: EncryptedTransferPackage): string {
  const compact: CompactEncryptedTransferPackage = {
    v: 1,
    s: payload.sessionId,
    w: payload.wrappedKey,
    i: payload.data.iv,
    c: payload.data.ciphertext,
    m: payload.data.mac,
  };
  return encodeDenseAscii(JSON.stringify(compact));
}

function decodeDensePackage(encoded: string): EncryptedTransferPackage {
  const json = decodeDenseAscii(encoded);
  const compact = JSON.parse(json) as Partial<CompactEncryptedTransferPackage>;
  if (compact.v !== 1 || typeof compact.s !== "string" || typeof compact.w !== "string" ||
      typeof compact.i !== "string" || typeof compact.c !== "string" || typeof compact.m !== "string") {
    throw new Error("Unsupported compact account transfer payload");
  }
  return {
    version: 1,
    sessionId: compact.s,
    wrappedKey: compact.w,
    data: {
      version: 1,
      algorithm: "A256CBC-HS256",
      iv: compact.i,
      ciphertext: compact.c,
      mac: compact.m,
    },
  };
}

function encodeDenseAscii(value: string): string {
  let buffer = 0;
  let bits = 0;
  let encoded = "";
  for (let index = 0; index < value.length; index += 1) {
    const byte = value.charCodeAt(index);
    if (byte > 0x7f) throw new Error("Compact transfer envelope must be ASCII");
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
  if (!value.startsWith(DENSE_MARKER)) throw new Error("Invalid compact transfer payload");
  const lengthEnd = value.indexOf(":", DENSE_MARKER.length);
  if (lengthEnd < 0) throw new Error("Invalid compact transfer payload");
  const lengthToken = value.slice(DENSE_MARKER.length, lengthEnd);
  if (!/^[0-9a-z]+$/.test(lengthToken)) throw new Error("Invalid compact transfer length");
  const expectedLength = Number.parseInt(lengthToken, 36);
  const dense = value.slice(lengthEnd + 1);
  if (!Number.isSafeInteger(expectedLength) || expectedLength < 1 ||
      dense.length !== Math.ceil((expectedLength * 8) / 14)) {
    throw new Error("Invalid compact transfer length");
  }

  let buffer = 0;
  let bits = 0;
  let decoded = "";
  for (let index = 0; index < dense.length; index += 1) {
    const codePoint = dense.charCodeAt(index);
    if (codePoint < DENSE_CODE_POINT_BASE || codePoint >= DENSE_CODE_POINT_LIMIT) {
      throw new Error("Invalid compact transfer data");
    }
    buffer = (buffer << 14) | (codePoint - DENSE_CODE_POINT_BASE);
    bits += 14;
    while (bits >= 8 && decoded.length < expectedLength) {
      bits -= 8;
      const byte = (buffer >>> bits) & 0xff;
      if (byte > 0x7f) throw new Error("Invalid compact transfer text");
      decoded += String.fromCharCode(byte);
      buffer = bits === 0 ? 0 : buffer & ((1 << bits) - 1);
    }
  }
  if (decoded.length !== expectedLength || buffer !== 0) {
    throw new Error("Invalid compact transfer padding");
  }
  return decoded;
}

export function validatePortableAccounts(value: unknown): PortableQrAccount[] {
  if (!Array.isArray(value) || value.length > 500) throw new Error("Invalid account transfer payload");
  return value.map((candidate) => {
    if (!candidate || typeof candidate !== "object") throw new Error("Invalid account transfer payload");
    const account = candidate as Record<string, unknown>;
    if (typeof account.secret !== "string" || typeof account.issuer !== "string" || typeof account.account !== "string") {
      throw new Error("Invalid account transfer payload");
    }
    const secret = normalizeSecret(account.secret);
    if (!secret || account.issuer.trim().length === 0 || account.account.trim().length === 0) {
      throw new Error("Invalid account transfer payload");
    }
    return {
      issuer: account.issuer.trim(),
      account: account.account.trim(),
      secret,
      createdAt: typeof account.createdAt === "number" ? account.createdAt : Date.now(),
      group: typeof account.group === "string" ? account.group.trim() || undefined : undefined,
      color: typeof account.color === "string" ? account.color : undefined,
    };
  });
}

export function normalizeSecret(value: string): string {
  return value.replace(/[\s=-]/g, "").toUpperCase();
}

export function selectUniqueAccountsBySecret<T extends { secret: string }>(
  existing: readonly { secret: string }[],
  incoming: readonly T[],
): { accepted: (T & { secret: string })[]; skipped: number } {
  const knownSecrets = new Set(existing.map((account) => normalizeSecret(account.secret)));
  const accepted: (T & { secret: string })[] = [];
  let skipped = 0;
  incoming.forEach((candidate) => {
    const secret = normalizeSecret(candidate.secret);
    if (!secret || knownSecrets.has(secret)) {
      skipped += 1;
      return;
    }
    knownSecrets.add(secret);
    accepted.push({ ...candidate, secret });
  });
  return { accepted, skipped };
}

function validateToken(value: string, label: string): void {
  if (!value || !SAFE_TOKEN.test(value)) throw new Error(`Invalid ${label}`);
}

function validateTransferPackage(payload: EncryptedTransferPackage): void {
  if (!payload || payload.version !== QR_TRANSFER_VERSION || typeof payload.sessionId !== "string" ||
      typeof payload.wrappedKey !== "string" || !payload.data || payload.data.version !== 1 ||
      payload.data.algorithm !== "A256CBC-HS256" || typeof payload.data.iv !== "string" ||
      typeof payload.data.ciphertext !== "string" || typeof payload.data.mac !== "string") {
    throw new Error("Unsupported account transfer payload");
  }
  validateToken(payload.sessionId, "session id");
  validateToken(payload.wrappedKey, "wrapped key");
}
