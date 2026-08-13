package com.mfaverifier.app

import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class QrTransferCryptoEngineTest {
  private val mobileFixture = """
    {"version":1,"algorithm":"A256CBC-HS256","iv":"laAy7tBYm9bAEkr642MimQ==","ciphertext":"j+37vPQJ8wxuahd6/tayGKtxWFuHDNjRFmILx02KV3E+EH1I4tSgL+A/s3fIeFUdTi1AGbaQJV8aJhlBn8b1XDzssGkenpvvyETjy2RjmgTOTQJK4+mxWVgbc6icPwQgDOZvJvCgvCRAj4dYh5wyKA==","mac":"1f9622e55affed297059e7198cd26e4d0a99d1dd7b12064305880b87519b59be"}
  """.trimIndent()

  @Test
  fun roundTripUsesAndroidProviderAndConsumesSession() {
    val engine = QrTransferCryptoEngine()
    val session = engine.createReceiveSession()
    val recoveryKey = "public-test-recovery-key"

    val wrapped = engine.wrapKey(session.publicKey, recoveryKey)

    assertEquals(recoveryKey, engine.unwrapKey(session.sessionId, wrapped))
    assertThrows(IllegalStateException::class.java) {
      engine.unwrapKey(session.sessionId, wrapped)
    }
  }

  @Test
  fun wrongCiphertextFailsAndStillConsumesSession() {
    val engine = QrTransferCryptoEngine()
    val expectedSession = engine.createReceiveSession()
    val differentSession = engine.createReceiveSession()
    val wrappedForDifferentSession = engine.wrapKey(differentSession.publicKey, "public-test-key")

    assertThrows(Exception::class.java) {
      engine.unwrapKey(expectedSession.sessionId, wrappedForDifferentSession)
    }
    assertThrows(IllegalStateException::class.java) {
      engine.unwrapKey(expectedSession.sessionId, wrappedForDifferentSession)
    }
  }

  @Test
  fun clearAndExpiryInvalidatePrivateSession() {
    var currentTime = 1_000L
    val engine = QrTransferCryptoEngine(now = { currentTime }, sessionTtlMs = 1_000L)
    val cleared = engine.createReceiveSession()
    val clearedWrapped = engine.wrapKey(cleared.publicKey, "public-test-key")
    engine.clearSession(cleared.sessionId)
    assertThrows(IllegalStateException::class.java) {
      engine.unwrapKey(cleared.sessionId, clearedWrapped)
    }

    val expired = engine.createReceiveSession()
    val expiredWrapped = engine.wrapKey(expired.publicKey, "public-test-key")
    currentTime += 1_001L
    assertThrows(IllegalStateException::class.java) {
      engine.unwrapKey(expired.sessionId, expiredWrapped)
    }
  }

  @Test
  fun decryptsTheMobileEncryptedEnvelopeAndRejectsTampering() {
    val engine = QrTransferCryptoEngine()
    val recoveryKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
    val expected = "[{\"id\":\"fixture-1\",\"issuer\":\"CrossPlatform\",\"account\":\"public-test\",\"secret\":\"JBSWY3DPEHPK3PXP\",\"createdAt\":0}]"

    assertEquals(expected, engine.decryptTransferEnvelope(mobileFixture, recoveryKey))
    assertThrows(IllegalArgumentException::class.java) {
      engine.decryptTransferEnvelope(mobileFixture, "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB")
    }
    assertThrows(IllegalArgumentException::class.java) {
      engine.decryptTransferEnvelope(mobileFixture.replace("1f", "2f"), recoveryKey)
    }
  }
}
