const { withAndroidManifest } = require("expo/config-plugins");

module.exports = function withAndroidDataProtection(config) {
  return withAndroidManifest(config, (modConfig) => {
    const application = modConfig.modResults.manifest.application?.[0];
    if (!application) throw new Error("Android application manifest entry is missing.");
    application.$["android:allowBackup"] = "false";
    delete application.$["android:fullBackupContent"];
    delete application.$["android:dataExtractionRules"];
    return modConfig;
  });
};
