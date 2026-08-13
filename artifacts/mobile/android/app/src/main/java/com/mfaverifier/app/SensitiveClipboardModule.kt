package com.mfaverifier.app

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.os.PersistableBundle
import com.facebook.react.bridge.LifecycleEventListener
import com.facebook.react.bridge.Promise
import com.facebook.react.bridge.ReactApplicationContext
import com.facebook.react.bridge.ReactContextBaseJavaModule
import com.facebook.react.bridge.ReactMethod

class SensitiveClipboardModule(
  private val reactContext: ReactApplicationContext,
) : ReactContextBaseJavaModule(reactContext), LifecycleEventListener {
  private val clipboard = reactContext.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
  private val handler = Handler(Looper.getMainLooper())
  private var lastSensitiveValue: String? = null
  private var ownsSensitiveClip = false
  private val clipChangedListener = ClipboardManager.OnPrimaryClipChangedListener {
    refreshClipboardOwnership()
  }

  init {
    reactContext.addLifecycleEventListener(this)
    clipboard.addPrimaryClipChangedListener(clipChangedListener)
  }

  override fun getName(): String = "SensitiveClipboard"

  @ReactMethod
  fun setSensitiveText(value: String, ttlMs: Double, promise: Promise) {
    try {
      val clip = ClipData.newPlainText("sensitive", value)
      val extras = PersistableBundle()
      extras.putBoolean("android.content.extra.IS_SENSITIVE", true)
      clip.description.extras = extras
      clipboard.setPrimaryClip(clip)
      lastSensitiveValue = value
      ownsSensitiveClip = true
      handler.postDelayed({ clearIfOwned(value) }, ttlMs.toLong().coerceIn(1_000L, 300_000L))
      promise.resolve(true)
    } catch (error: Exception) {
      promise.reject("SENSITIVE_CLIPBOARD_WRITE_FAILED", error)
    }
  }

  @ReactMethod
  fun clearSensitiveText(promise: Promise) {
    clearIfOwned(lastSensitiveValue)
    promise.resolve(true)
  }

  private fun clearIfOwned(expected: String?) {
    if (expected == null) return
    val current = currentClipboardText()
    if (current != expected) {
      if (lastSensitiveValue == expected) {
        lastSensitiveValue = null
        ownsSensitiveClip = false
      }
      return
    }
    clearPrimaryClip()
    if (lastSensitiveValue == expected) {
      lastSensitiveValue = null
      ownsSensitiveClip = false
    }
  }

  private fun currentClipboardText(): String? = clipboard.primaryClip
      ?.takeIf { it.itemCount > 0 }
      ?.getItemAt(0)
      ?.coerceToText(reactContext)
      ?.toString()

  private fun refreshClipboardOwnership() {
    val expected = lastSensitiveValue
    if (expected == null) {
      ownsSensitiveClip = false
      return
    }
    ownsSensitiveClip = currentClipboardText() == expected
    if (!ownsSensitiveClip) lastSensitiveValue = null
  }

  private fun clearPrimaryClip() {
    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
      clipboard.clearPrimaryClip()
    } else {
      clipboard.setPrimaryClip(ClipData.newPlainText("", ""))
    }
  }

  private fun clearOwnedClipOnBackground() {
    if (!ownsSensitiveClip || lastSensitiveValue == null) return
    clearPrimaryClip()
    lastSensitiveValue = null
    ownsSensitiveClip = false
  }

  override fun onHostResume() = Unit

  override fun onHostPause() {
    clearOwnedClipOnBackground()
  }

  override fun onHostDestroy() {
    clearOwnedClipOnBackground()
    clipboard.removePrimaryClipChangedListener(clipChangedListener)
    handler.removeCallbacksAndMessages(null)
  }
}
