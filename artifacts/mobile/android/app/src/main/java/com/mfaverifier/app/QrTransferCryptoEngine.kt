package com.mfaverifier.app

import android.util.Base64
import android.util.Log
import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import java.security.KeyFactory
import java.security.KeyPairGenerator
import java.security.PrivateKey
import java.security.SecureRandom
import java.security.spec.MGF1ParameterSpec
import java.security.spec.X509EncodedKeySpec
import java.util.concurrent.ConcurrentHashMap
import javax.crypto.Cipher
import javax.crypto.Mac
import javax.crypto.spec.OAEPParameterSpec
import javax.crypto.spec.PSource
import javax.crypto.spec.IvParameterSpec
import javax.crypto.spec.SecretKeySpec
import org.json.JSONObject

internal class QrTransferCryptoEngine(
  private val now: () -> Long = System::currentTimeMillis,
  private val sessionTtlMs: Long = DEFAULT_SESSION_TTL_MS,
) {
  private val sessions = ConcurrentHashMap<String, ReceiveSession>()
  private val random = SecureRandom()

  fun createReceiveSession(): PublicReceiveSession {
    removeExpiredSessions()
    val generator = KeyPairGenerator.getInstance("RSA")
    generator.initialize(2048, random)
    val keyPair = generator.generateKeyPair()
    val sessionId = base64Url(ByteArray(16).also(random::nextBytes))
    val expiresAt = now() + sessionTtlMs
    sessions[sessionId] = ReceiveSession(keyPair.private, expiresAt)
    return PublicReceiveSession(sessionId, base64Url(keyPair.public.encoded), expiresAt)
  }

  fun wrapKey(publicKey: String, keyMaterial: String): String {
    val plaintext = keyMaterial.toByteArray(StandardCharsets.UTF_8)
    require(plaintext.size <= MAX_OAEP_SHA256_PAYLOAD_BYTES) { "Invalid key material" }
    val key = KeyFactory.getInstance("RSA").generatePublic(X509EncodedKeySpec(base64UrlDecode(publicKey)))
    val cipher = rsaCipher(Cipher.ENCRYPT_MODE, key)
    return base64Url(cipher.doFinal(plaintext))
  }

  fun unwrapKey(sessionId: String, wrappedKey: String): String {
    val session = sessions.remove(sessionId)
    if (session == null || session.expiresAt < now()) {
      destroySession(session)
      throw IllegalStateException("QR transfer receive session has expired")
    }
    try {
      val cipher = rsaCipher(Cipher.DECRYPT_MODE, session.privateKey)
      return String(cipher.doFinal(base64UrlDecode(wrappedKey)), StandardCharsets.UTF_8)
    } finally {
      destroySession(session)
    }
  }

  fun clearSession(sessionId: String) {
    destroySession(sessions.remove(sessionId))
  }

  fun decryptTransferEnvelope(envelopeJson: String, keyMaterial: String): String {
    val envelope = JSONObject(envelopeJson)
    require(envelope.optInt("version") == 1) { "Unsupported encrypted payload" }
    require(envelope.optString("algorithm") == "A256CBC-HS256") { "Unsupported encrypted payload" }
    val iv = envelope.optString("iv")
    val ciphertext = envelope.optString("ciphertext")
    val mac = envelope.optString("mac")
    require(iv.isNotEmpty() && ciphertext.isNotEmpty() && mac.isNotEmpty()) { "Unsupported encrypted payload" }

    val normalizedKey = keyMaterial.filterNot(Char::isWhitespace)
    require(normalizedKey.isNotEmpty()) { "Invalid key material" }
    val encryptionKey = hmacSha256(normalizedKey, "mfa-verifier:encryption:v1")
    val authenticationKey = hmacSha256(normalizedKey, "mfa-verifier:authentication:v1")
    val macInput = "1|A256CBC-HS256|$iv|$ciphertext"
    val expectedMac = hmacSha256(authenticationKey, macInput.toByteArray(StandardCharsets.UTF_8))
    require(MessageDigest.isEqual(expectedMac, hexDecode(mac))) { "Encrypted payload authentication failed" }

    val cipher = Cipher.getInstance("AES/CBC/PKCS7Padding")
    cipher.init(
      Cipher.DECRYPT_MODE,
      SecretKeySpec(encryptionKey, "AES"),
      IvParameterSpec(Base64.decode(iv, Base64.DEFAULT)),
    )
    val plaintextBytes = cipher.doFinal(Base64.decode(ciphertext, Base64.DEFAULT))
    val plaintext = String(plaintextBytes, StandardCharsets.UTF_8)
    require(plaintext.isNotEmpty()) { "Unable to decrypt payload" }
    Log.i("MfaQrTransfer", "QR receive metadata plaintextBytes=${plaintextBytes.size} plaintextChars=${plaintext.length} endsWithArray=${plaintext.endsWith("]")}")
    return plaintext
  }

  private fun rsaCipher(mode: Int, key: java.security.Key): Cipher {
    val cipher = Cipher.getInstance("RSA/ECB/OAEPWithSHA-256AndMGF1Padding")
    val params = OAEPParameterSpec("SHA-256", "MGF1", MGF1ParameterSpec.SHA1, PSource.PSpecified.DEFAULT)
    cipher.init(mode, key, params, random)
    return cipher
  }

  private fun removeExpiredSessions() {
    val currentTime = now()
    sessions.entries.forEach { entry ->
      if (entry.value.expiresAt < currentTime && sessions.remove(entry.key, entry.value)) {
        destroySession(entry.value)
      }
    }
  }

  private fun destroySession(session: ReceiveSession?) {
    try {
      session?.privateKey?.destroy()
    } catch (_: Exception) {
      // Removing the last retained reference still lets the provider release key material.
    }
  }

  private fun base64Url(value: ByteArray): String =
    Base64.encodeToString(value, Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING)

  private fun base64UrlDecode(value: String): ByteArray =
    Base64.decode(value, Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING)

  private fun hmacSha256(key: String, message: String): ByteArray =
    hmacSha256(key.toByteArray(StandardCharsets.UTF_8), message.toByteArray(StandardCharsets.UTF_8))

  private fun hmacSha256(key: ByteArray, message: ByteArray): ByteArray {
    val mac = Mac.getInstance("HmacSHA256")
    mac.init(SecretKeySpec(key, "HmacSHA256"))
    return mac.doFinal(message)
  }

  private fun hexDecode(value: String): ByteArray {
    require(value.length % 2 == 0 && value.all { it.isDigit() || it.lowercaseChar() in 'a'..'f' }) {
      "Encrypted payload authentication failed"
    }
    return ByteArray(value.length / 2) { index ->
      value.substring(index * 2, index * 2 + 2).toInt(16).toByte()
    }
  }

  internal data class PublicReceiveSession(
    val sessionId: String,
    val publicKey: String,
    val expiresAt: Long,
  )

  private data class ReceiveSession(val privateKey: PrivateKey, val expiresAt: Long)

  companion object {
    const val DEFAULT_SESSION_TTL_MS = 5 * 60 * 1000L
    private const val MAX_OAEP_SHA256_PAYLOAD_BYTES = 190
  }
}
