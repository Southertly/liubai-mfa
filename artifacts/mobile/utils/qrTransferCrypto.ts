import { NativeModules, Platform } from "react-native";

interface ReceiveSession {
  sessionId: string;
  publicKey: string;
  expiresAt: number;
}

interface NativeQrTransferCrypto {
  createReceiveSession(): Promise<ReceiveSession>;
  wrapKey(publicKey: string, keyMaterial: string): Promise<string>;
  unwrapKey(sessionId: string, wrappedKey: string): Promise<string>;
  clearSession(sessionId: string): Promise<void>;
  decryptTransferEnvelope(envelopeJson: string, keyMaterial: string): Promise<string>;
}

function requireModule(): NativeQrTransferCrypto {
  if (Platform.OS === "web") throw new Error("网页端不支持二维码安全迁移");
  const module = NativeModules.QrTransferCrypto as NativeQrTransferCrypto | undefined;
  if (!module) throw new Error("当前安装包不支持二维码安全迁移，请更新应用");
  return module;
}

export const qrTransferCrypto = {
  createReceiveSession: () => requireModule().createReceiveSession(),
  wrapKey: (publicKey: string, keyMaterial: string) => requireModule().wrapKey(publicKey, keyMaterial),
  unwrapKey: (sessionId: string, wrappedKey: string) => requireModule().unwrapKey(sessionId, wrappedKey),
  decryptTransferEnvelope: (envelopeJson: string, keyMaterial: string) =>
    requireModule().decryptTransferEnvelope(envelopeJson, keyMaterial),
  clearSession: async (sessionId: string) => {
    if (Platform.OS !== "web") await requireModule().clearSession(sessionId);
  },
};
