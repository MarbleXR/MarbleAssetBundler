using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Marble.AssetBundleBuilder.Editor
{
    internal static class AssetBundlePackageContract
    {
        internal const int SchemaVersion = 1;
        internal const string ManifestEntryName = "package.json";

        private static readonly Regex BundleNamePattern = new(
            "^[a-z0-9][a-z0-9_-]*$",
            RegexOptions.CultureInvariant
        );
        private static readonly Regex ContentVersionPattern = new(
            "^[A-Za-z0-9][A-Za-z0-9._-]*$",
            RegexOptions.CultureInvariant
        );
        private static readonly Regex Sha256Pattern = new(
            "^[0-9a-f]{64}$",
            RegexOptions.CultureInvariant
        );

        internal static IReadOnlyList<AssetBundlePackageIssue> ValidateDefinition(
            AssetBundlePackageDefinition definition
        )
        {
            var issues = new List<AssetBundlePackageIssue>();
            if (definition == null)
            {
                issues.Add(new AssetBundlePackageIssue("definition_required", "Package definition is required."));
                return issues;
            }

            if (!IsBundleNameValid(definition.BundleName))
            {
                issues.Add(new AssetBundlePackageIssue(
                    "bundle_name_invalid",
                    "Bundle Name must start with a lowercase letter or digit and contain only lowercase letters, digits, '_' or '-'."
                ));
            }

            if (string.IsNullOrWhiteSpace(definition.DisplayName))
            {
                issues.Add(new AssetBundlePackageIssue("display_name_required", "Display Name is required."));
            }

            if (!IsContentVersionValid(definition.ContentVersion))
            {
                issues.Add(new AssetBundlePackageIssue(
                    "content_version_invalid",
                    "Content Version must contain only letters, digits, '.', '_' or '-'."
                ));
            }

            if (string.IsNullOrWhiteSpace(definition.UnityVersion))
            {
                issues.Add(new AssetBundlePackageIssue("unity_version_required", "Unity Version is required."));
            }

            if (definition.GeneratedAtUtc == default || definition.GeneratedAtUtc.Offset != TimeSpan.Zero)
            {
                issues.Add(new AssetBundlePackageIssue(
                    "generated_at_utc_invalid",
                    "Generated timestamp must be a non-default UTC value."
                ));
            }

            ValidatePrefabs(definition.Prefabs, issues);
            return issues;
        }

        internal static IReadOnlyList<AssetBundlePackageIssue> ValidateManifest(
            AssetBundlePackageManifest manifest
        )
        {
            var issues = new List<AssetBundlePackageIssue>();
            if (manifest == null)
            {
                issues.Add(new AssetBundlePackageIssue("manifest_required", "Package manifest is required."));
                return issues;
            }

            if (manifest.SchemaVersion != SchemaVersion)
            {
                issues.Add(new AssetBundlePackageIssue("schema_version_unsupported", "Schema Version must be 1."));
            }

            var definition = new AssetBundlePackageDefinition
            {
                BundleName = manifest.BundleName,
                DisplayName = manifest.DisplayName,
                ContentVersion = manifest.ContentVersion,
                UnityVersion = manifest.UnityVersion,
                GeneratedAtUtc = ParseGeneratedAtUtc(manifest.GeneratedAtUtc)
            };
            if (manifest.Prefabs != null)
            {
                foreach (var prefab in manifest.Prefabs.Where(prefab => prefab != null))
                {
                    definition.Prefabs.Add(new AssetBundlePackagePrefabDefinition
                    {
                        Name = prefab.Name,
                        AssetPath = prefab.AssetPath
                    });
                }
            }

            issues.AddRange(ValidateDefinition(definition));

            if (manifest.Platforms == null)
            {
                issues.Add(new AssetBundlePackageIssue("platforms_required", "Both platform entries are required."));
                return issues;
            }

            ValidatePlatform(
                manifest.Platforms.Ios,
                "ios",
                "iOS",
                manifest.BundleName,
                "platform_ios_required",
                issues
            );
            ValidatePlatform(
                manifest.Platforms.Android,
                "android",
                "Android",
                manifest.BundleName,
                "platform_android_required",
                issues
            );
            return issues;
        }

        internal static bool IsBundleNameValid(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                   && value.Length <= 100
                   && BundleNamePattern.IsMatch(value);
        }

        internal static bool IsContentVersionValid(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                   && value.Length <= 100
                   && ContentVersionPattern.IsMatch(value);
        }

        internal static string GetBundleEntryPath(string platformKey, string bundleName)
        {
            if (platformKey != "ios" && platformKey != "android")
            {
                throw new ArgumentException("Platform key must be 'ios' or 'android'.", nameof(platformKey));
            }

            if (!IsBundleNameValid(bundleName))
            {
                throw new ArgumentException("Bundle name is invalid.", nameof(bundleName));
            }

            return $"bundles/{platformKey}/{bundleName}";
        }

        internal static string GetBundleManifestEntryPath(string platformKey, string bundleName)
        {
            return GetBundleEntryPath(platformKey, bundleName) + ".manifest";
        }

        internal static string GetPackageFileName(string bundleName, string contentVersion)
        {
            if (!IsBundleNameValid(bundleName))
            {
                throw new ArgumentException("Bundle name is invalid.", nameof(bundleName));
            }

            if (!IsContentVersionValid(contentVersion))
            {
                throw new ArgumentException("Content Version is invalid.", nameof(contentVersion));
            }

            return $"{bundleName}-{contentVersion}.zip";
        }

        private static void ValidatePrefabs(
            IReadOnlyCollection<AssetBundlePackagePrefabDefinition> prefabs,
            ICollection<AssetBundlePackageIssue> issues
        )
        {
            if (prefabs == null || prefabs.Count == 0)
            {
                issues.Add(new AssetBundlePackageIssue("prefabs_required", "At least one Prefab is required."));
                return;
            }

            var prefabNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var prefab in prefabs)
            {
                if (prefab == null || string.IsNullOrWhiteSpace(prefab.Name))
                {
                    issues.Add(new AssetBundlePackageIssue("prefab_name_required", "Every Prefab requires a name."));
                    continue;
                }

                if (!prefabNames.Add(prefab.Name))
                {
                    issues.Add(new AssetBundlePackageIssue(
                        "prefab_name_duplicate",
                        $"Prefab name is duplicated: {prefab.Name}"
                    ));
                }

                if (string.IsNullOrWhiteSpace(prefab.AssetPath)
                    || !prefab.AssetPath.StartsWith("Assets/", StringComparison.Ordinal)
                    || !prefab.AssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                    || prefab.AssetPath.Contains("\\"))
                {
                    issues.Add(new AssetBundlePackageIssue(
                        "prefab_asset_path_invalid",
                        $"Prefab Asset path is invalid: {prefab.AssetPath ?? "(null)"}"
                    ));
                }
            }
        }

        private static DateTimeOffset ParseGeneratedAtUtc(string value)
        {
            if (DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var parsed
                ) && parsed.Offset == TimeSpan.Zero)
            {
                return parsed;
            }

            return default;
        }

        private static void ValidatePlatform(
            AssetBundlePackagePlatformManifest platform,
            string platformKey,
            string expectedBuildTarget,
            string bundleName,
            string missingCode,
            ICollection<AssetBundlePackageIssue> issues
        )
        {
            if (platform == null)
            {
                issues.Add(new AssetBundlePackageIssue(missingCode, $"Platform '{platformKey}' is required."));
                return;
            }

            if (!string.Equals(platform.BuildTarget, expectedBuildTarget, StringComparison.Ordinal))
            {
                issues.Add(new AssetBundlePackageIssue(
                    "platform_build_target_invalid",
                    $"Platform '{platformKey}' must use BuildTarget '{expectedBuildTarget}'."
                ));
            }

            if (!IsBundleNameValid(bundleName)
                || !string.Equals(
                    platform.Path,
                    GetBundleEntryPath(platformKey, bundleName),
                    StringComparison.Ordinal
                ))
            {
                issues.Add(new AssetBundlePackageIssue(
                    "platform_path_invalid",
                    $"Platform '{platformKey}' has an invalid Bundle path."
                ));
            }

            if (platform.Size <= 0)
            {
                issues.Add(new AssetBundlePackageIssue(
                    "platform_size_invalid",
                    $"Platform '{platformKey}' Bundle size must be greater than zero."
                ));
            }

            if (string.IsNullOrEmpty(platform.Sha256) || !Sha256Pattern.IsMatch(platform.Sha256))
            {
                issues.Add(new AssetBundlePackageIssue(
                    "platform_sha256_invalid",
                    $"Platform '{platformKey}' SHA-256 must be 64 lowercase hexadecimal characters."
                ));
            }

            if (!IsBundleNameValid(bundleName)
                || !string.Equals(
                    platform.ManifestPath,
                    GetBundleManifestEntryPath(platformKey, bundleName),
                    StringComparison.Ordinal
                ))
            {
                issues.Add(new AssetBundlePackageIssue(
                    "platform_manifest_path_invalid",
                    $"Platform '{platformKey}' has an invalid Unity manifest path."
                ));
            }

            if (platform.ManifestSize <= 0)
            {
                issues.Add(new AssetBundlePackageIssue(
                    "platform_manifest_size_invalid",
                    $"Platform '{platformKey}' Unity manifest size must be greater than zero."
                ));
            }

            if (string.IsNullOrEmpty(platform.ManifestSha256)
                || !Sha256Pattern.IsMatch(platform.ManifestSha256))
            {
                issues.Add(new AssetBundlePackageIssue(
                    "platform_manifest_sha256_invalid",
                    $"Platform '{platformKey}' Unity manifest SHA-256 must be 64 lowercase hexadecimal characters."
                ));
            }

            if (platform.Dependencies == null)
            {
                issues.Add(new AssetBundlePackageIssue(
                    "platform_dependencies_required",
                    $"Platform '{platformKey}' dependencies must be an empty array."
                ));
            }
            else if (platform.Dependencies.Length > 0)
            {
                issues.Add(new AssetBundlePackageIssue(
                    "platform_dependencies_not_empty",
                    $"Platform '{platformKey}' cannot depend on another AssetBundle."
                ));
            }
        }
    }

    internal static class AssetBundlePackageManifestJson
    {
        internal static string Serialize(AssetBundlePackageManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            return JsonUtility.ToJson(ManifestDto.FromManifest(manifest), true);
        }

        internal static AssetBundlePackageManifest Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Package manifest JSON is required.", nameof(json));
            }

            var dto = JsonUtility.FromJson<ManifestDto>(json);
            if (dto == null)
            {
                throw new ArgumentException("Package manifest JSON produced no object.", nameof(json));
            }

            return dto.ToManifest();
        }

        [Serializable]
        private sealed class ManifestDto
        {
            public int schemaVersion;
            public string bundleName;
            public string displayName;
            public string contentVersion;
            public string unityVersion;
            public string generatedAtUtc;
            public PrefabDto[] prefabs;
            public PlatformsDto platforms;

            internal static ManifestDto FromManifest(AssetBundlePackageManifest manifest)
            {
                return new ManifestDto
                {
                    schemaVersion = manifest.SchemaVersion,
                    bundleName = manifest.BundleName,
                    displayName = manifest.DisplayName,
                    contentVersion = manifest.ContentVersion,
                    unityVersion = manifest.UnityVersion,
                    generatedAtUtc = manifest.GeneratedAtUtc,
                    prefabs = manifest.Prefabs?
                        .Select(PrefabDto.FromManifest)
                        .ToArray(),
                    platforms = PlatformsDto.FromManifest(manifest.Platforms)
                };
            }

            internal AssetBundlePackageManifest ToManifest()
            {
                return new AssetBundlePackageManifest
                {
                    SchemaVersion = schemaVersion,
                    BundleName = bundleName,
                    DisplayName = displayName,
                    ContentVersion = contentVersion,
                    UnityVersion = unityVersion,
                    GeneratedAtUtc = generatedAtUtc,
                    Prefabs = prefabs?.Select(prefab => prefab.ToManifest()).ToArray()
                              ?? Array.Empty<AssetBundlePackagePrefabManifest>(),
                    Platforms = platforms?.ToManifest()
                };
            }
        }

        [Serializable]
        private sealed class PrefabDto
        {
            public string name;
            public string assetPath;

            internal static PrefabDto FromManifest(AssetBundlePackagePrefabManifest manifest)
            {
                return new PrefabDto { name = manifest.Name, assetPath = manifest.AssetPath };
            }

            internal AssetBundlePackagePrefabManifest ToManifest()
            {
                return new AssetBundlePackagePrefabManifest { Name = name, AssetPath = assetPath };
            }
        }

        [Serializable]
        private sealed class PlatformsDto
        {
            public PlatformDto ios;
            public PlatformDto android;

            internal static PlatformsDto FromManifest(AssetBundlePackagePlatformsManifest manifest)
            {
                if (manifest == null)
                {
                    return null;
                }

                return new PlatformsDto
                {
                    ios = PlatformDto.FromManifest(manifest.Ios),
                    android = PlatformDto.FromManifest(manifest.Android)
                };
            }

            internal AssetBundlePackagePlatformsManifest ToManifest()
            {
                return new AssetBundlePackagePlatformsManifest
                {
                    Ios = ios?.ToManifest(),
                    Android = android?.ToManifest()
                };
            }
        }

        [Serializable]
        private sealed class PlatformDto
        {
            public string buildTarget;
            public string path;
            public long size;
            public string sha256;
            public string manifestPath;
            public long manifestSize;
            public string manifestSha256;
            public string[] dependencies;

            internal static PlatformDto FromManifest(AssetBundlePackagePlatformManifest manifest)
            {
                if (manifest == null)
                {
                    return null;
                }

                return new PlatformDto
                {
                    buildTarget = manifest.BuildTarget,
                    path = manifest.Path,
                    size = manifest.Size,
                    sha256 = manifest.Sha256,
                    manifestPath = manifest.ManifestPath,
                    manifestSize = manifest.ManifestSize,
                    manifestSha256 = manifest.ManifestSha256,
                    dependencies = manifest.Dependencies
                };
            }

            internal AssetBundlePackagePlatformManifest ToManifest()
            {
                return new AssetBundlePackagePlatformManifest
                {
                    BuildTarget = buildTarget,
                    Path = path,
                    Size = size,
                    Sha256 = sha256,
                    ManifestPath = manifestPath,
                    ManifestSize = manifestSize,
                    ManifestSha256 = manifestSha256,
                    Dependencies = dependencies
                };
            }
        }
    }
}
