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

  init {
    reactContext.addLifecycleEventListener(this)
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
    val current = clipboard.primaryClip
      ?.takeIf { it.itemCount > 0 }
      ?.getItemAt(0)
      ?.coerceToText(reactContext)
      ?.toString()
    if (current != expected) return
    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
      clipboard.clearPrimaryClip()
    } else {
      clipboard.setPrimaryClip(ClipData.newPlainText("", ""))
    }
    if (lastSensitiveValue == expected) lastSensitiveValue = null
  }

  override fun onHostResume() = Unit

  override fun onHostPause() {
    clearIfOwned(lastSensitiveValue)
  }

  override fun onHostDestroy() {
    clearIfOwned(lastSensitiveValue)
    handler.removeCallbacksAndMessages(null)
  }
}
