export function getSecureRandomBytes(byteCount: number): Uint8Array {
  if (!globalThis.crypto?.getRandomValues) {
    throw new Error("Secure random number generator is unavailable");
  }
  return globalThis.crypto.getRandomValues(new Uint8Array(byteCount));
}
