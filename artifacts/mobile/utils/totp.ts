import CryptoJS from "crypto-js";

import {
  normalizeBase32Secret,
  normalizeTotpAlgorithm,
  TotpAlgorithm,
} from "./accountSchema";

function base32Decode(base32: string): number[] {
  const chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  const cleaned = normalizeBase32Secret(base32);
  const bytes: number[] = [];
  let bits = 0;
  let value = 0;
  for (const char of cleaned) {
    const idx = chars.indexOf(char);
    if (idx === -1) throw new Error("Invalid Base32 secret");
    value = (value << 5) | idx;
    bits += 5;
    if (bits >= 8) {
      bytes.push((value >>> (bits - 8)) & 0xff);
      bits -= 8;
    }
  }
  return bytes;
}

// CryptoJS WordArray stores data as 32-bit big-endian words.
// This helper correctly packs a byte array into that format.
function bytesToWordArray(bytes: number[]): CryptoJS.lib.WordArray {
  const words: number[] = [];
  for (let i = 0; i < bytes.length; i += 4) {
    words.push(
      (((bytes[i] ?? 0) << 24) |
        ((bytes[i + 1] ?? 0) << 16) |
        ((bytes[i + 2] ?? 0) << 8) |
        (bytes[i + 3] ?? 0)) >>>
        0
    );
  }
  return CryptoJS.lib.WordArray.create(words as unknown as number[], bytes.length);
}

export function generateTOTP(
  secret: string,
  period = 30,
  digits = 6,
  algorithm: TotpAlgorithm = "SHA1",
  timeMs = Date.now(),
): string {
  try {
    if (!Number.isInteger(period) || period < 1 || period > 300) throw new Error("Invalid period");
    if (!Number.isInteger(digits) || digits < 6 || digits > 8) throw new Error("Invalid digits");
    const normalizedAlgorithm = normalizeTotpAlgorithm(algorithm);
    const counter = Math.floor(timeMs / 1000 / period);

    // Encode counter as 8-byte big-endian (RFC 6238)
    const hi = Math.floor(counter / 0x100000000);
    const lo = counter >>> 0;
    const counterBytes = [
      (hi >>> 24) & 0xff,
      (hi >>> 16) & 0xff,
      (hi >>> 8) & 0xff,
      hi & 0xff,
      (lo >>> 24) & 0xff,
      (lo >>> 16) & 0xff,
      (lo >>> 8) & 0xff,
      lo & 0xff,
    ];

    const secretBytes = base32Decode(secret);
    const secretWordArray = bytesToWordArray(secretBytes);
    const counterWordArray = bytesToWordArray(counterBytes);

    const hmac = normalizedAlgorithm === "SHA512"
      ? CryptoJS.HmacSHA512(counterWordArray, secretWordArray)
      : normalizedAlgorithm === "SHA256"
        ? CryptoJS.HmacSHA256(counterWordArray, secretWordArray)
        : CryptoJS.HmacSHA1(counterWordArray, secretWordArray);

    // Extract HMAC bytes from WordArray
    const hmacBytes: number[] = [];
    for (let i = 0; i < hmac.sigBytes; i++) {
      hmacBytes.push((hmac.words[i >>> 2] >>> (24 - (i % 4) * 8)) & 0xff);
    }

    // Dynamic truncation (RFC 4226)
    const offset = hmacBytes[hmacBytes.length - 1] & 0x0f;
    const code =
      (((hmacBytes[offset] & 0x7f) << 24) |
        ((hmacBytes[offset + 1] & 0xff) << 16) |
        ((hmacBytes[offset + 2] & 0xff) << 8) |
        (hmacBytes[offset + 3] & 0xff)) %
      Math.pow(10, digits);

    return code.toString().padStart(digits, "0");
  } catch {
    return "------";
  }
}

export function getTimeRemaining(period = 30): number {
  return period - (Math.floor(Date.now() / 1000) % period);
}

export function parseOtpAuthUri(uri: string): {
  issuer: string;
  account: string;
  secret: string;
  algorithm: TotpAlgorithm;
  digits: number;
  period: number;
} {
    const url = new URL(uri.trim());
    if (url.protocol !== "otpauth:") throw new Error("不是 otpauth 二维码");
    if (url.hostname.toLowerCase() !== "totp") {
      throw new Error(url.hostname.toLowerCase() === "hotp" ? "暂不支持 HOTP" : "仅支持 TOTP 二维码");
    }
    const label = decodeURIComponent(url.pathname.slice(1));
    const params = new URLSearchParams(url.search);
    const secret = normalizeBase32Secret(params.get("secret") ?? "");
    const algorithm = normalizeTotpAlgorithm(params.get("algorithm") ?? "SHA1");
    const digits = Number(params.get("digits") ?? "6");
    const period = Number(params.get("period") ?? "30");
    if (!Number.isInteger(digits) || digits < 6 || digits > 8) throw new Error("不支持的 TOTP 位数");
    if (!Number.isInteger(period) || period < 1 || period > 300) throw new Error("不支持的 TOTP 周期");
    const issuerParam = params.get("issuer") ?? "";
    let issuer = issuerParam;
    let account = label;
    if (label.includes(":")) {
      const parts = label.split(":");
      issuer = issuer || parts[0].trim();
      account = parts[1].trim();
    }
    if (!issuer.trim() || !account.trim()) throw new Error("二维码缺少服务或账户名称");
    return { issuer: issuer.trim(), account: account.trim(), secret, algorithm, digits, period };
}
