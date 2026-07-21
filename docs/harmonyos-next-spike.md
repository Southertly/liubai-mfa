# HarmonyOS NEXT Native Spike

## Goal

Validate whether Tick MFA can be delivered as a native HarmonyOS NEXT app without weakening the existing security model.

This spike should answer one practical question: should the HarmonyOS NEXT version be built as an ArkTS/ArkUI native app with shared core logic, or as a React Native OpenHarmony app with native platform modules?

## Current Baseline

The existing mobile app is built with Expo SDK 54 and React Native 0.81.5. Its security-sensitive behavior is:

- MFA secrets are encrypted at rest.
- Device encryption key material is stored through platform secure storage.
- Secret reveal, secret copy, and backup export require system authentication.
- Backups are encrypted and protected by a separate recovery key.
- The recovery key is not embedded in the backup payload.
- Browser or web-only behavior is not accepted as proof for native security flows.

Primary implementation anchors:

- `artifacts/mobile/utils/totp.ts`
- `artifacts/mobile/utils/secureCrypto.ts`
- `artifacts/mobile/utils/secureStorage.ts`
- `artifacts/mobile/context/AccountsContext.tsx`
- `artifacts/mobile/context/LockContext.tsx`
- `artifacts/mobile/components/SensitiveAuthGate.tsx`
- `artifacts/mobile/app/scan.tsx`
- `artifacts/mobile/app/edit/[id].tsx`
- `artifacts/mobile/app/backup.tsx`
- `artifacts/mobile/scripts/security-regression.ts`

## Recommended Spike Path

Start with an ArkTS/ArkUI native shell and validate the core platform capabilities directly. In parallel, evaluate React Native OpenHarmony only as a possible reuse path, not as an assumed target.

This is the safer first move because HarmonyOS NEXT is not an Expo build target, and the current app depends on native security APIs that must be proven on-device.

## Candidate Implementation Paths

### Option A: ArkTS/ArkUI Native App

Use DevEco Studio to build a native HarmonyOS NEXT app.

Expected advantages:

- Best alignment with HarmonyOS NEXT platform APIs.
- Direct access to system authentication, secure storage, camera, file, and sharing APIs.
- Lower risk for app store review and platform-specific UX.

Expected cost:

- UI and navigation must be rebuilt.
- React Native components cannot be reused directly.
- Shared TypeScript logic may need adaptation or porting.

### Option B: React Native OpenHarmony + Native Modules

Use React Native OpenHarmony as the HarmonyOS runtime and add native modules for security-sensitive platform APIs.

Expected advantages:

- Potential reuse of some React Native UI and business logic.
- Lower rewrite cost if compatibility is acceptable.

Expected cost:

- Compatibility with React Native 0.81.5 is pending confirmation.
- Expo modules such as `expo-secure-store`, `expo-local-authentication`, `expo-camera`, and `expo-clipboard` must be replaced.
- Native security flows still require HarmonyOS-specific implementation.

## Reuse Matrix

| Area | Current source | HarmonyOS NEXT reuse decision |
| --- | --- | --- |
| TOTP generation | `utils/totp.ts` | Reuse or port. Algorithm is platform-independent. |
| otpauth URI parsing | `utils/totp.ts` | Reuse or port. Keep behavior compatible. |
| Account data model | `AccountsContext.tsx` | Reuse schema. Do not reuse React context directly in ArkTS. |
| Backup envelope format | `utils/secureCrypto.ts` | Reuse format if crypto primitives match. Otherwise preserve format semantics. |
| Recovery key behavior | `backup.tsx`, `secureCrypto.ts` | Reuse product behavior. Recovery key remains separate from backup. |
| Encrypted local storage flow | `secureStorage.ts`, `AccountsContext.tsx` | Replace implementation with HarmonyOS secure storage and encrypted persistence. |
| System authentication gate | `LockContext.tsx`, `SensitiveAuthGate.tsx` | Replace with HarmonyOS NEXT system authentication. Preserve gate semantics. |
| QR scanning | `scan.tsx` | Replace with HarmonyOS camera and QR parsing capability. |
| Clipboard | `AccountCard.tsx`, `edit/[id].tsx`, `backup.tsx` | Replace with HarmonyOS clipboard API. |
| Backup export/share | `backup.tsx` | Replace with HarmonyOS file/share APIs. |
| UI/navigation | Expo Router app files | Rebuild in ArkUI, or selectively reuse through RNOH if chosen. |

## Minimum Prototype Scope

The spike prototype should implement only these flows:

1. Create one account manually from issuer, account name, and secret.
2. Generate a valid six-digit TOTP code.
3. Save the account with the secret encrypted at rest.
4. Require system authentication before revealing or copying the secret.
5. Scan an `otpauth://` QR code and create an account from it.
6. Export an encrypted v2 backup with a separate recovery key.
7. Import that encrypted backup and regenerate the same TOTP code.

Do not build full theming, grouping, polishing, cloud sync, widgets, or app store packaging during this spike.

## Platform Capabilities To Verify

Mark each item as passed only after testing on a HarmonyOS NEXT simulator or device.

- [x] DevEco Studio can create, sign, build, and run a HarmonyOS NEXT app.
- [x] A Huawei developer account is available for signing.
- [x] Secure local storage can protect app encryption key material.
- [x] Protected account secrets can be written and read without plaintext secret leakage in preferences or logs.
- [x] System authentication can be triggered for secret reveal/copy and backup export.
- [x] Authentication supports the expected device unlock methods on the test device.
- [x] Camera permission and ScanKit QR recognition work for real `otpauth://` QR codes on the physical device.
- [x] Pasted `otpauth://` URLs can be parsed and imported before camera scanning is added.
- [x] Clipboard writes work for TOTP code, protected secret, and the separate recovery key.
- [x] Encrypted v2 backup preparation uses one system-authentication prompt and never embeds the recovery key.
- [x] The system document picker opens for JSON backup import, and save/share integrations compile against the installed SDK.
- [x] The HarmonyOS crypto implementation decrypts an Expo-generated v2 fixture and rejects wrong recovery keys and tampered ciphertext.
- [x] A full physical-device save/select/import round trip succeeds; importing the same backup skips duplicate secrets instead of creating duplicate accounts.
- System share panel completion remains pending manual validation without choosing an external recipient during testing.

## Device Validation Result

Validated through July 15, 2026 with a signed native ArkTS prototype installed on a physical HarmonyOS NEXT device.

- Bundle `com.tickmfa.harmonyosnext.spike` installs and launches through `hdc`.
- `ohos.permission.ACCESS_BIOMETRIC` is declared and granted with `authResults=0`.
- `userAuth.getUserAuthInstance()` can open the system fingerprint authentication UI.
- Successful authentication returns a user auth token.
- Asset Store can save a protected test secret and read it back only through the `preQuery -> userAuth -> query -> postQuery` flow.
- The protected Asset Store round trip matched the original test secret.
- A minimal native MVP can add a manual MFA account, store the Base32 secret in Asset Store, generate a six-digit TOTP code, and reveal the secret only after system fingerprint authentication.

Implementation notes from the device run:

- Prefer a single available auth type in `AuthParam.authType`; passing multiple auth types caused a `401` parameter failure on the test device.
- Do not set `navigationButtonText` for the current prototype auth widget; it caused a `401` parameter failure when the selected auth path involved PIN/fallback behavior.
- Do not pass `asset.Tag.AUTH_VALIDITY_PERIOD` to `asset.add()` in this prototype; the device returned `Unsupported tag[asset.Tag.AUTH_VALIDITY_PERIOD] for the function`.
- Do not pass `asset.Tag.RETURN_TYPE` to `asset.preQuery()`; set it only on the final `asset.query()` call.
- The current verified Asset Store query sequence is:
  1. `asset.add()` with `SECRET`, `ALIAS`, `ACCESSIBILITY`, `REQUIRE_PASSWORD_SET`, and `AUTH_TYPE`.
  2. `asset.preQuery()` with the asset alias.
  3. `userAuth.getUserAuthInstance()` with the challenge returned by `preQuery`.
  4. `asset.query()` with `AUTH_CHALLENGE`, `AUTH_TOKEN`, and `RETURN_TYPE`.
  5. `asset.postQuery()` with `AUTH_CHALLENGE`.

## Simulator Validation Result

Validated on July 8, 2026 with the DevEco Studio Pura 90 simulator running HarmonyOS 6.1.1(24).

Observed evidence:

- DevEco simulator system image was installed locally.
- Pura 90 starts through the DevEco-bundled `Emulator` CLI and appears in `hdc` as `127.0.0.1:10123`.
- `hdc -t 127.0.0.1:10123 shell echo tick-mfa-emulator-ok` passed.
- The signed HAP installs with `install bundle successfully`.
- `aa start -b com.tickmfa.harmonyosnext.spike -a EntryAbility` starts the app successfully.
- The app renders the native MVP screen on the simulator.
- Before a simulator lock credential was configured, saving a protected account failed with `code=24000005` and `message=The screen lock status does not match`.
- After configuring a simulator lock credential, saving a protected account passed with `Account saved in protected Asset Store`.
- Revealing the protected secret triggers the secure system authentication surface. This surface appears black in screenshots, which is expected for a protected authentication view.
- Entering the simulator lock credential completed authentication and returned to the app with `Secret revealed after system authentication` and `authResults=0`.
- A pasted `otpauth://totp/...` URL can be parsed and imported into the protected Asset Store.
- The imported account generates a six-digit TOTP code and still requires system authentication before revealing the secret.
- The `Scan QR Code` entry opens the ScanKit system scanner on the simulator.
- Canceling the ScanKit scanner returns to the app without leaving the MVP stuck.
- A real `otpauth://` QR code was recognized on the physical device, imported into Asset Store, and persisted after an app restart.
- Successful QR import clears the scanned URI immediately instead of leaving the secret-bearing value in the input field.
- Backup export authenticates once, then reuses the same Asset Store challenge token to read each account alias on the physical device.
- The Expo-generated public v2 compatibility fixture decrypts successfully on HarmonyOS; wrong keys and modified ciphertext fail closed.
- The HarmonyOS system document picker opens with a JSON suffix filter and app-scoped file access.
- The encrypted JSON backup saves to the system `Download` directory without embedding its recovery key.
- Picker-returned document URIs must be opened with `fileIo.open(..., READ_ONLY)` and read through the file descriptor; direct path-style reads fail with `code=13900002` on this device.
- Selecting the saved backup and supplying its separate recovery key completed import after one system-authentication prompt.
- Re-importing the device's own backup reported `0 added, 6 duplicates skipped`, proving duplicate secrets are skipped.
- After a force-stop and restart, account records loaded normally, the selected backup was cleared, and the recovery-key field was empty.

Simulator limitation:

- Treat the simulator as valid for launch, basic UI smoke testing, protected Asset Store save, and system-auth-gated reveal after a lock credential is configured.
- Keep physical-device validation as the source of truth for biometric UX, because the simulator flow uses lock credential authentication rather than real fingerprint hardware.

## Security Requirements

- Never write MFA secrets, recovery keys, or decrypted backup payloads to logs.
- Never store secrets in plaintext persistence.
- Never remove encryption because system authentication is present.
- Never implement or store a custom phone PIN.
- Secret reveal, secret copy, and backup export must require system authentication.
- Recovery keys must remain separate from backup payloads.
- Destructive restore testing must use test data unless the user explicitly authorizes real data deletion.

## Open Questions

- Can React Native OpenHarmony support React Native 0.81.5 in this project?
- What AppGallery requirements apply to this app category?
- Does the installed system share panel accept the encrypted text payload on all target HarmonyOS NEXT versions?

## Acceptance Criteria

The spike is successful when there is a small HarmonyOS NEXT prototype and a written decision that includes:

- Recommended implementation path: ArkTS native or React Native OpenHarmony.
- Evidence for secure storage behavior.
- Evidence for system authentication gating.
- Evidence for backup encryption import/export compatibility.
- List of current Expo dependencies and their HarmonyOS replacements.
- Remaining risks and estimated implementation phases.

## Suggested Phases After Spike

1. Platform foundation: DevEco project, signing, navigation, theme skeleton. Completed for the native MVP.
2. Core logic: TOTP, account model, protected secret persistence. MVP path validated on device.
3. Security gates: system auth for secret reveal. MVP path validated on device.
4. Input and migration: pasted URL import, physical-device ScanKit QR recognition, account editing, encrypted v2 backup crypto, and the full file save/select/import round trip are in place.
5. Product parity: grouping, colors, backup UX polish, empty states, and system share completion.
6. Validation: automated native tests, dedicated restore-only test data, device matrix verification, and AppGallery readiness.
