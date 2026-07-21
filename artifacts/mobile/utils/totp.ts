import CryptoJS from "crypto-js";

function base32Decode(base32: string): number[] {
  const chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  const cleaned = base32.toUpperCase().replace(/\s|=/g, "");
  const bytes: number[] = [];
  let bits = 0;
  let value = 0;
  for (const char of cleaned) {
    const idx = chars.indexOf(char);
    if (idx === -1) continue;
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

export function generateTOTP(secret: string, period = 30, digits = 6): string {
  try {
    const counter = Math.floor(Date.now() / 1000 / period);

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

    // HMAC-SHA1
    const hmac = CryptoJS.HmacSHA1(counterWordArray, secretWordArray);

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
} | null {
  try {
    const url = new URL(uri);
    if (url.protocol !== "otpauth:") return null;
    const label = decodeURIComponent(url.pathname.slice(1));
    const params = new URLSearchParams(url.search);
    const secret = params.get("secret") ?? "";
    const issuerParam = params.get("issuer") ?? "";
    let issuer = issuerParam;
    let account = label;
    if (label.includes(":")) {
      const parts = label.split(":");
      issuer = issuer || parts[0].trim();
      account = parts[1].trim();
    }
    if (!secret) return null;
    return { issuer, account, secret };
  } catch {
    return null;
  }
}
