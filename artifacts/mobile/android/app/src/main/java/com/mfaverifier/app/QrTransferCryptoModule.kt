package com.mfaverifier.app

import android.os.Handler
import android.os.Looper
import com.facebook.react.bridge.Promise
import com.facebook.react.bridge.ReactApplicationContext
import com.facebook.react.bridge.ReactContextBaseJavaModule
import com.facebook.react.bridge.ReactMethod
import com.facebook.react.bridge.WritableNativeMap

class QrTransferCryptoModule(context: ReactApplicationContext) : ReactContextBaseJavaModule(context) {
  private val engine = QrTransferCryptoEngine()
  private val expiryHandler = Handler(Looper.getMainLooper())

  override fun getName(): String = "QrTransferCrypto"

  @ReactMethod
  fun createReceiveSession(promise: Promise) {
    try {
      val session = engine.createReceiveSession()
      expiryHandler.postDelayed(
        { engine.clearSession(session.sessionId) },
        QrTransferCryptoEngine.DEFAULT_SESSION_TTL_MS,
      )
      val result = WritableNativeMap().apply {
        putString("sessionId", session.sessionId)
        putString("publicKey", session.publicKey)
        putDouble("expiresAt", session.expiresAt.toDouble())
      }
      promise.resolve(result)
    } catch (error: Exception) {
      promise.reject("QR_TRANSFER_KEYGEN", "无法创建一次性接收密钥", error)
    }
  }

  @ReactMethod
  fun wrapKey(publicKey: String, keyMaterial: String, promise: Promise) {
    try {
      promise.resolve(engine.wrapKey(publicKey, keyMaterial))
    } catch (error: Exception) {
      promise.reject("QR_TRANSFER_WRAP", "接收端公钥无效或加密失败", error)
    }
  }

  @ReactMethod
  fun unwrapKey(sessionId: String, wrappedKey: String, promise: Promise) {
    try {
      promise.resolve(engine.unwrapKey(sessionId, wrappedKey))
    } catch (error: IllegalStateException) {
      promise.reject("QR_TRANSFER_EXPIRED", "二维码接收会话已过期，请重新开始", error)
    } catch (error: Exception) {
      promise.reject("QR_TRANSFER_UNWRAP", "账户二维码与当前接收会话不匹配", error)
    }
  }

  @ReactMethod
  fun clearSession(sessionId: String, promise: Promise) {
    engine.clearSession(sessionId)
    promise.resolve(null)
  }

  @ReactMethod
  fun decryptTransferEnvelope(envelopeJson: String, keyMaterial: String, promise: Promise) {
    try {
      promise.resolve(engine.decryptTransferEnvelope(envelopeJson, keyMaterial))
    } catch (error: Exception) {
      promise.reject("QR_TRANSFER_DECRYPT", "账户二维码解密或完整性校验失败", error)
    }
  }
}
