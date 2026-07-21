import {
  decryptString,
  encryptString,
  formatRecoveryKey,
  generateRecoveryKey,
  isValidRecoveryKey,
  normalizeRecoveryKey,
} from "../utils/secureCrypto";

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message);
}

function expectThrow(action: () => void, expectedMessage: string, message: string) {
  try {
    action();
  } catch (error) {
    assert(error instanceof Error && error.message.includes(expectedMessage), message);
    return;
  }
  throw new Error(message);
}

const secretPayload = JSON.stringify([
  {
    id: "test",
    issuer: "Example",
    account: "user@example.com",
    secret: "JBSWY3DPEHPK3PXP",
    color: "yellow",
    createdAt: 1,
  },
]);

const recoveryKey = generateRecoveryKey();
assert(isValidRecoveryKey(recoveryKey), "generated recovery key must be valid");
assert(normalizeRecoveryKey(formatRecoveryKey(recoveryKey)) === recoveryKey, "formatted recovery key must round-trip");

const encrypted = encryptString(secretPayload, recoveryKey);
assert(!JSON.stringify(encrypted).includes("JBSWY3DPEHPK3PXP"), "encrypted payload must not contain the MFA secret");
assert(decryptString(encrypted, recoveryKey) === secretPayload, "valid key must decrypt backup");

expectThrow(
  () => decryptString(encrypted, generateRecoveryKey()),
  "authentication failed",
  "wrong recovery key must fail closed",
);

const tampered = {
  ...encrypted,
  ciphertext: `${encrypted.ciphertext.slice(0, -2)}AA`,
};
expectThrow(
  () => decryptString(tampered, recoveryKey),
  "authentication failed",
  "tampered backup must fail integrity validation",
);

console.log("security regression checks passed");
