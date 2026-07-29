using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Marble.AssetBundleBuilder.Editor
{
    internal enum AssetBundlePackageIssueSeverity
    {
        Warning,
        Error
    }

    internal sealed class AssetBundlePackageIssue
    {
        public AssetBundlePackageIssue(
            string code,
            string message,
            AssetBundlePackageIssueSeverity severity = AssetBundlePackageIssueSeverity.Error
        )
        {
            Code = code;
            Message = message;
            Severity = severity;
        }

        public string Code { get; }
        public string Message { get; }
        public AssetBundlePackageIssueSeverity Severity { get; }
    }

    internal sealed class AssetBundlePackageDefinition
    {
        public string BundleName { get; set; }
        public string DisplayName { get; set; }
        public string ContentVersion { get; set; }
        public string UnityVersion { get; set; }
        public DateTimeOffset GeneratedAtUtc { get; set; }
        public List<AssetBundlePackagePrefabDefinition> Prefabs { get; } = new();
    }

    internal sealed class AssetBundlePackagePrefabDefinition
    {
        public string Name { get; set; }
        public string AssetPath { get; set; }
    }

    internal sealed class AssetBundlePackageManifest
    {
        [JsonProperty("schemaVersion", Order = 1)]
        public int SchemaVersion { get; set; }

        [JsonProperty("bundleName", Order = 2)]
        public string BundleName { get; set; }

        [JsonProperty("displayName", Order = 3)]
        public string DisplayName { get; set; }

        [JsonProperty("contentVersion", Order = 4)]
        public string ContentVersion { get; set; }

        [JsonProperty("unityVersion", Order = 5)]
        public string UnityVersion { get; set; }

        [JsonProperty("generatedAtUtc", Order = 6)]
        public string GeneratedAtUtc { get; set; }

        [JsonProperty("prefabs", Order = 7)]
        public AssetBundlePackagePrefabManifest[] Prefabs { get; set; } =
            Array.Empty<AssetBundlePackagePrefabManifest>();

        [JsonProperty("platforms", Order = 8)]
        public AssetBundlePackagePlatformsManifest Platforms { get; set; }
    }

    internal sealed class AssetBundlePackagePrefabManifest
    {
        [JsonProperty("name", Order = 1)]
        public string Name { get; set; }

        [JsonProperty("assetPath", Order = 2)]
        public string AssetPath { get; set; }
    }

    internal sealed class AssetBundlePackagePlatformsManifest
    {
        [JsonProperty("ios", Order = 1)]
        public AssetBundlePackagePlatformManifest Ios { get; set; }

        [JsonProperty("android", Order = 2)]
        public AssetBundlePackagePlatformManifest Android { get; set; }
    }

    internal sealed class AssetBundlePackagePlatformManifest
    {
        [JsonProperty("buildTarget", Order = 1)]
        public string BuildTarget { get; set; }

        [JsonProperty("path", Order = 2)]
        public string Path { get; set; }

        [JsonProperty("size", Order = 3)]
        public long Size { get; set; }

        [JsonProperty("sha256", Order = 4)]
        public string Sha256 { get; set; }

        [JsonProperty("manifestPath", Order = 5)]
        public string ManifestPath { get; set; }

        [JsonProperty("manifestSize", Order = 6)]
        public long ManifestSize { get; set; }

        [JsonProperty("manifestSha256", Order = 7)]
        public string ManifestSha256 { get; set; }

        [JsonProperty("dependencies", Order = 8)]
        public string[] Dependencies { get; set; } = Array.Empty<string>();
    }
}
