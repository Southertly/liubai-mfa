const { withAppBuildGradle } = require("expo/config-plugins");

const MARKER = "// MFA_RELEASE_SIGNING_V1";

module.exports = function withReleaseSigningGuard(config) {
  return withAppBuildGradle(config, (modConfig) => {
    if (modConfig.modResults.language !== "groovy") {
      throw new Error("Liubai MFA release signing guard only supports Groovy build.gradle files.");
    }

    let contents = modConfig.modResults.contents;
    contents = contents.replace(
      /(buildTypes\s*\{[\s\S]*?release\s*\{[\s\S]*?)\n\s*signingConfig signingConfigs\.debug/,
      "$1\n            // Release signing is configured from environment secrets below.",
    );

    if (!contents.includes(MARKER)) {
      contents += `

${MARKER}
def mfaReleaseKeystorePath = System.getenv("MFA_ANDROID_KEYSTORE_PATH")
def mfaReleaseKeystorePassword = System.getenv("MFA_ANDROID_KEYSTORE_PASSWORD")
def mfaReleaseKeyAlias = System.getenv("MFA_ANDROID_KEY_ALIAS")
def mfaReleaseKeyPassword = System.getenv("MFA_ANDROID_KEY_PASSWORD")
def mfaReleaseSigningReady = [mfaReleaseKeystorePath, mfaReleaseKeystorePassword, mfaReleaseKeyAlias, mfaReleaseKeyPassword]
    .every { value -> value != null && !value.isBlank() }
def mfaLocalHomePath = System.getProperty("user.home")

android {
    defaultConfig {
        externalNativeBuild {
            cmake {
                cFlags "-ffile-prefix-map=\${mfaLocalHomePath}=/build"
                cppFlags "-ffile-prefix-map=\${mfaLocalHomePath}=/build"
            }
        }
    }
    signingConfigs {
        if (mfaReleaseSigningReady) {
            create("mfaRelease") {
                storeFile file(mfaReleaseKeystorePath)
                storePassword mfaReleaseKeystorePassword
                keyAlias mfaReleaseKeyAlias
                keyPassword mfaReleaseKeyPassword
            }
        }
    }
    buildTypes {
        release {
            if (mfaReleaseSigningReady) signingConfig signingConfigs.mfaRelease
        }
    }
}

tasks.configureEach { task ->
    if (!mfaReleaseSigningReady && task.name.toLowerCase().contains("release") &&
        (task.name.toLowerCase().contains("package") || task.name.toLowerCase().contains("assemble") ||
            task.name.toLowerCase().contains("bundle"))) {
        task.doFirst {
            throw new GradleException("Release signing environment is required; unsigned release output is forbidden.")
        }
    }
}
`;
    }

    modConfig.modResults.contents = contents;
    return modConfig;
  });
};
