using System;
using System.Collections.Generic;

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
        public int SchemaVersion { get; set; }

        public string BundleName { get; set; }

        public string DisplayName { get; set; }

        public string ContentVersion { get; set; }

        public string UnityVersion { get; set; }

        public string GeneratedAtUtc { get; set; }

        public AssetBundlePackagePrefabManifest[] Prefabs { get; set; } =
            Array.Empty<AssetBundlePackagePrefabManifest>();

        public AssetBundlePackagePlatformsManifest Platforms { get; set; }
    }

    internal sealed class AssetBundlePackagePrefabManifest
    {
        public string Name { get; set; }

        public string AssetPath { get; set; }
    }

    internal sealed class AssetBundlePackagePlatformsManifest
    {
        public AssetBundlePackagePlatformManifest Ios { get; set; }

        public AssetBundlePackagePlatformManifest Android { get; set; }
    }

    internal sealed class AssetBundlePackagePlatformManifest
    {
        public string BuildTarget { get; set; }

        public string Path { get; set; }

        public long Size { get; set; }

        public string Sha256 { get; set; }

        public string ManifestPath { get; set; }

        public long ManifestSize { get; set; }

        public string ManifestSha256 { get; set; }

        public string[] Dependencies { get; set; } = Array.Empty<string>();
    }
}
