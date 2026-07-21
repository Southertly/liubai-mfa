import { getRandomValues } from "expo-crypto";

export function getSecureRandomBytes(byteCount: number): Uint8Array {
  return getRandomValues(new Uint8Array(byteCount));
}
