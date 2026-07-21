import CryptoJS from "crypto-js";

import { getSecureRandomBytes } from "./secureRandom";

export const SECURE_ENVELOPE_ALGORITHM = "A256CBC-HS256" as const;

export interface SecureEnvelope {
  version: 1;
  algorithm: typeof SECURE_ENVELOPE_ALGORITHM;
  iv: string;
  ciphertext: string;
  mac: string;
}

function deriveKeys(keyMaterial: string) {
  const normalized = normalizeRecoveryKey(keyMaterial);
  if (!normalized) throw new Error("Invalid key material");
  return {
    encryptionKey: CryptoJS.HmacSHA256("mfa-verifier:encryption:v1", normalized),
    authenticationKey: CryptoJS.HmacSHA256("mfa-verifier:authentication:v1", normalized),
  };
}

function constantTimeHexEqual(a: string, b: string): boolean {
  if (a.length !== b.length) return false;
  let difference = 0;
  for (let index = 0; index < a.length; index += 1) {
    difference |= a.charCodeAt(index) ^ b.charCodeAt(index);
  }
  return difference === 0;
}

function macInput(envelope: Pick<SecureEnvelope, "version" | "algorithm" | "iv" | "ciphertext">) {
  return `${envelope.version}|${envelope.algorithm}|${envelope.iv}|${envelope.ciphertext}`;
}

function secureRandomWordArray(byteCount: number): CryptoJS.lib.WordArray {
  return CryptoJS.lib.WordArray.create(getSecureRandomBytes(byteCount));
}

export function generateRecoveryKey(): string {
  return secureRandomWordArray(32).toString(CryptoJS.enc.Base64url);
}

export function normalizeRecoveryKey(value: string): string {
  return value.trim().replace(/\s/g, "");
}

export function formatRecoveryKey(value: string): string {
  return normalizeRecoveryKey(value).match(/.{1,4}/g)?.join(" ") ?? "";
}

export function isValidRecoveryKey(value: string): boolean {
  const normalized = normalizeRecoveryKey(value);
  if (!/^[A-Za-z0-9_-]{43}$/.test(normalized)) return false;
  try {
    return CryptoJS.enc.Base64url.parse(normalized).sigBytes === 32;
  } catch {
    return false;
  }
}

export function encryptString(plaintext: string, keyMaterial: string): SecureEnvelope {
  const { encryptionKey, authenticationKey } = deriveKeys(keyMaterial);
  const iv = secureRandomWordArray(16);
  const encrypted = CryptoJS.AES.encrypt(plaintext, encryptionKey, {
    iv,
    mode: CryptoJS.mode.CBC,
    padding: CryptoJS.pad.Pkcs7,
  });
  const envelopeWithoutMac = {
    version: 1 as const,
    algorithm: SECURE_ENVELOPE_ALGORITHM,
    iv: iv.toString(CryptoJS.enc.Base64),
    ciphertext: encrypted.ciphertext.toString(CryptoJS.enc.Base64),
  };
  const mac = CryptoJS.HmacSHA256(
    macInput(envelopeWithoutMac),
    authenticationKey,
  ).toString(CryptoJS.enc.Hex);
  return { ...envelopeWithoutMac, mac };
}

export function decryptString(envelope: SecureEnvelope, keyMaterial: string): string {
  if (
    envelope.version !== 1 ||
    envelope.algorithm !== SECURE_ENVELOPE_ALGORITHM ||
    !envelope.iv ||
    !envelope.ciphertext ||
    !envelope.mac
  ) {
    throw new Error("Unsupported encrypted payload");
  }

  const { encryptionKey, authenticationKey } = deriveKeys(keyMaterial);
  const expectedMac = CryptoJS.HmacSHA256(
    macInput(envelope),
    authenticationKey,
  ).toString(CryptoJS.enc.Hex);
  if (!constantTimeHexEqual(expectedMac, envelope.mac)) {
    throw new Error("Encrypted payload authentication failed");
  }

  const cipherParams = CryptoJS.lib.CipherParams.create({
    ciphertext: CryptoJS.enc.Base64.parse(envelope.ciphertext),
  });
  const decrypted = CryptoJS.AES.decrypt(cipherParams, encryptionKey, {
    iv: CryptoJS.enc.Base64.parse(envelope.iv),
    mode: CryptoJS.mode.CBC,
    padding: CryptoJS.pad.Pkcs7,
  }).toString(CryptoJS.enc.Utf8);
  if (!decrypted) throw new Error("Unable to decrypt payload");
  return decrypted;
}
