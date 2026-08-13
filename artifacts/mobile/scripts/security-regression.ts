import {
  decryptString,
  encryptString,
  formatRecoveryKey,
  generateRecoveryKey,
  isValidRecoveryKey,
  normalizeRecoveryKey,
} from "../utils/secureCrypto";
import {
  assembleTransferPackage,
  buildPairingQr,
  encodeTransferPackage,
  parsePairingQr,
  parseTransferChunk,
  QR_TRANSFER_MAX_RENDERED_CHARS,
  selectUniqueAccountsBySecret,
  utf8ToBase64Url,
  validatePortableAccounts,
} from "../utils/qrTransfer";

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
    group: "工作",
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

const transferSessionId = utf8ToBase64Url("s".repeat(16));
const pairing = buildPairingQr({
  sessionId: transferSessionId,
  publicKey: "public-test-key",
  expiresAt: Date.now() + 60_000,
});
assert(parsePairingQr(pairing).sessionId === transferSessionId, "valid pairing QR must round-trip");
expectThrow(
  () => parsePairingQr(pairing, Date.now() + 61_000),
  "expired",
  "expired pairing QR must fail closed",
);

const transferPayload = {
  version: 1,
  sessionId: transferSessionId,
  wrappedKey: utf8ToBase64Url("w".repeat(256)),
  data: encrypted,
} as const;
const transferParts = encodeTransferPackage(transferPayload);
assert(
  transferParts.every((part) => part.length <= QR_TRANSFER_MAX_RENDERED_CHARS),
  "every compact account QR must stay within the cross-platform 512-character limit",
);
assert(transferParts.every((part) => part.startsWith("MFA3|")), "new transfers must use ASCII compact MFA3 frames");
const chunks = new Map<number, string>();
for (const part of [...transferParts].reverse()) {
  const chunk = parseTransferChunk(part);
  chunks.set(chunk.index, chunk.data);
}
const duplicateChunk = parseTransferChunk(transferParts[0]);
const duplicateSafeChunks = new Map<number, string>();
duplicateSafeChunks.set(duplicateChunk.index, duplicateChunk.data);
duplicateSafeChunks.set(duplicateChunk.index, duplicateChunk.data);
assert(duplicateSafeChunks.size === 1, "repeated QR frames must not increase received progress");
const assembled = assembleTransferPackage(transferSessionId, chunks, transferParts.length);
assert(decryptString(assembled.data, recoveryKey) === secretPayload, "out-of-order QR chunks must reassemble safely");

const legacyEncoded = utf8ToBase64Url(JSON.stringify(transferPayload));
const legacyPart = `MFAQ1D|${transferSessionId}|1|1|${legacyEncoded}`;
const legacyChunk = parseTransferChunk(legacyPart);
const legacyAssembled = assembleTransferPackage(transferSessionId, new Map([[1, legacyChunk.data]]), 1);
assert(decryptString(legacyAssembled.data, recoveryKey) === secretPayload, "legacy MFAQ1D transfers must remain readable");
expectThrow(
  () => assembleTransferPackage("different-session", chunks, transferParts.length),
  "does not match",
  "QR chunks must stay bound to the receiving session",
);

const validated = validatePortableAccounts(JSON.parse(secretPayload));
assert(validated.length === 1 && validated[0].secret === "JBSWY3DPEHPK3PXP", "portable account validation must normalize secrets");
assert(validated[0].group === "工作", "portable account validation must retain the source group name");

const existingAccount = {
  issuer: "Existing",
  account: "keep@example.com",
  secret: "JBSW Y3DP-EHPK3PXP",
  group: "本机分组",
  createdAt: 1,
};
const selected = selectUniqueAccountsBySecret(
  [existingAccount],
  [
    { issuer: "Duplicate", account: "must-not-overwrite@example.com", secret: "jbswy3dpehpk3pxp", group: "旧设备分组", createdAt: 2 },
    { issuer: "New", account: "new@example.com", secret: "KRUGS4ZANFZSAYJA", group: "迁移分组", createdAt: 3 },
    { issuer: "Repeated New", account: "repeat@example.com", secret: "KRUG S4ZA-NFZSAYJA", group: "另一个分组", createdAt: 4 },
  ],
);
assert(selected.accepted.length === 1, "only distinct incoming secrets may be imported");
assert(selected.skipped === 2, "existing and repeated incoming secrets must be skipped");
assert(selected.accepted[0].issuer === "New", "duplicate secrets must never overwrite the existing account");
assert(selected.accepted[0].group === "迁移分组", "new accounts must retain their transferred group name");
assert(existingAccount.issuer === "Existing" && existingAccount.group === "本机分组", "deduplication must leave existing account metadata and group unchanged");

console.log("security regression checks passed");
