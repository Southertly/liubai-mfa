const { withAppBuildGradle } = require("expo/config-plugins");

module.exports = function withNoDebugReleaseSigning(config) {
  return withAppBuildGradle(config, (modConfig) => {
    if (modConfig.modResults.language !== "groovy") {
      throw new Error("Tick MFA release signing guard only supports Groovy build.gradle files.");
    }

    const contents = modConfig.modResults.contents;
    const releaseSigningPattern =
      /(buildTypes\s*\{[\s\S]*?release\s*\{[\s\S]*?)\n\s*signingConfig signingConfigs\.debug/;

    if (!releaseSigningPattern.test(contents)) {
      throw new Error("Could not find the generated debug release signing configuration.");
    }

    modConfig.modResults.contents = contents.replace(
      releaseSigningPattern,
      "$1\n            // Intentionally unsigned until an AppGallery upload key is configured."
    );

    return modConfig;
  });
};
