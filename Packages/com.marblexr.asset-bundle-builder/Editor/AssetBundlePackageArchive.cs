using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;

namespace Marble.AssetBundleBuilder.Editor
{
    internal sealed class AssetBundlePackageArchiveResult
    {
        public AssetBundlePackageArchiveResult(
            string packagePath,
            AssetBundlePackageManifest manifest
        )
        {
            PackagePath = packagePath;
            Manifest = manifest;
        }

        public string PackagePath { get; }
        public AssetBundlePackageManifest Manifest { get; }
    }

    internal interface IAssetBundlePackageArchiveWriter
    {
        AssetBundlePackageArchiveResult Create(
            AssetBundlePackageDefinition definition,
            IReadOnlyList<AssetBundlePlatformArtifact> artifacts,
            string outputDirectory
        );
    }

    internal sealed class AssetBundlePackageArchive : IAssetBundlePackageArchiveWriter
    {
        private static readonly UTF8Encoding Utf8WithoutBom = new(false);

        public AssetBundlePackageArchiveResult Create(
            AssetBundlePackageDefinition definition,
            IReadOnlyList<AssetBundlePlatformArtifact> artifacts,
            string outputDirectory
        )
        {
            ValidateCreateInputs(definition, artifacts, outputDirectory);
            var manifest = CreateManifest(definition, artifacts);
            var manifestIssues = AssetBundlePackageContract.ValidateManifest(manifest);
            if (manifestIssues.Any(issue => issue.Severity == AssetBundlePackageIssueSeverity.Error))
            {
                throw new InvalidDataException(
                    "Generated package manifest is invalid: "
                    + string.Join(", ", manifestIssues.Select(issue => issue.Message))
                );
            }

            var absoluteOutputDirectory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(absoluteOutputDirectory);
            var packagePath = Path.Combine(
                absoluteOutputDirectory,
                AssetBundlePackageContract.GetPackageFileName(
                    definition.BundleName,
                    definition.ContentVersion
                )
            );
            if (File.Exists(packagePath))
            {
                throw new IOException($"AssetBundle package already exists: {packagePath}");
            }

            var partialPath = packagePath + ".partial-" + Guid.NewGuid().ToString("N");
            try
            {
                WriteArchive(partialPath, manifest, artifacts);
                var validatedManifest = Validate(partialPath);
                File.Move(partialPath, packagePath);
                return new AssetBundlePackageArchiveResult(packagePath, validatedManifest);
            }
            catch
            {
                TryDeleteFile(partialPath);
                throw;
            }
        }

        internal AssetBundlePackageManifest Validate(string packagePath)
        {
            if (string.IsNullOrWhiteSpace(packagePath))
            {
                throw new ArgumentException("Package path is required.", nameof(packagePath));
            }
            if (!File.Exists(packagePath))
            {
                throw new FileNotFoundException("AssetBundle package was not found.", packagePath);
            }

            using var packageStream = new FileStream(
                packagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read
            );
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, false);
            var entries = archive.Entries.ToArray();
            if (entries.Length != 5
                || entries.Select(entry => entry.FullName).Distinct(StringComparer.Ordinal).Count() != 5)
            {
                throw new InvalidDataException(
                    "AssetBundle package entries must contain exactly package.json, iOS/Android Bundles and their Unity manifests."
                );
            }

            var manifestEntry = entries.SingleOrDefault(entry =>
                string.Equals(
                    entry.FullName,
                    AssetBundlePackageContract.ManifestEntryName,
                    StringComparison.Ordinal
                )
            );
            if (manifestEntry == null)
            {
                throw new InvalidDataException("AssetBundle package is missing package.json.");
            }
            if (manifestEntry.Length > 1024 * 1024)
            {
                throw new InvalidDataException("package.json exceeds the 1 MB self-validation limit.");
            }

            AssetBundlePackageManifest manifest;
            using (var manifestStream = manifestEntry.Open())
            using (var reader = new StreamReader(
                       manifestStream,
                       Encoding.UTF8,
                       true,
                       1024,
                       false
                   ))
            {
                manifest = AssetBundlePackageManifestJson.Deserialize(reader.ReadToEnd());
            }

            var issues = AssetBundlePackageContract.ValidateManifest(manifest);
            if (issues.Any(issue => issue.Severity == AssetBundlePackageIssueSeverity.Error))
            {
                throw new InvalidDataException(
                    "AssetBundle package manifest validation failed: "
                    + string.Join(", ", issues.Select(issue => issue.Message))
                );
            }

            var allowedEntries = new HashSet<string>(StringComparer.Ordinal)
            {
                AssetBundlePackageContract.ManifestEntryName,
                manifest.Platforms.Ios.Path,
                manifest.Platforms.Ios.ManifestPath,
                manifest.Platforms.Android.Path,
                manifest.Platforms.Android.ManifestPath
            };
            if (!entries.Select(entry => entry.FullName).All(allowedEntries.Contains))
            {
                throw new InvalidDataException(
                    "AssetBundle package entries do not match the Package Schema v1 allowlist."
                );
            }

            ValidateBundleEntry(entries, manifest.Platforms.Ios, "ios");
            ValidateBundleEntry(entries, manifest.Platforms.Android, "android");
            ValidateUnityManifestEntry(entries, manifest.Platforms.Ios, "ios");
            ValidateUnityManifestEntry(entries, manifest.Platforms.Android, "android");
            return manifest;
        }

        internal static string ComputeSha256(string filePath)
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read
            );
            return ComputeSha256(stream);
        }

        private static string ComputeSha256(Stream stream)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void ValidateCreateInputs(
            AssetBundlePackageDefinition definition,
            IReadOnlyList<AssetBundlePlatformArtifact> artifacts,
            string outputDirectory
        )
        {
            var definitionIssues = AssetBundlePackageContract.ValidateDefinition(definition);
            if (definitionIssues.Any(issue => issue.Severity == AssetBundlePackageIssueSeverity.Error))
            {
                throw new ArgumentException(
                    "Package definition is invalid: "
                    + string.Join(", ", definitionIssues.Select(issue => issue.Message)),
                    nameof(definition)
                );
            }
            if (artifacts == null || artifacts.Count != 2)
            {
                throw new ArgumentException("Exactly two platform artifacts are required.", nameof(artifacts));
            }
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new ArgumentException("Output Directory is required.", nameof(outputDirectory));
            }

            foreach (var platformKey in new[] { "ios", "android" })
            {
                var matches = artifacts.Count(artifact =>
                    artifact != null
                    && string.Equals(artifact.PlatformKey, platformKey, StringComparison.Ordinal)
                );
                if (matches != 1)
                {
                    throw new ArgumentException(
                        $"Exactly one '{platformKey}' artifact is required.",
                        nameof(artifacts)
                    );
                }
            }

            foreach (var artifact in artifacts)
            {
                if (!string.Equals(artifact.BundleName, definition.BundleName, StringComparison.Ordinal)
                    || !string.Equals(
                        Path.GetFileName(artifact.BundlePath),
                        definition.BundleName,
                        StringComparison.Ordinal
                    ))
                {
                    throw new InvalidDataException(
                        $"Artifact Bundle name does not match '{definition.BundleName}'."
                    );
                }
                if (!File.Exists(artifact.BundlePath))
                {
                    throw new FileNotFoundException(
                        $"Platform Bundle was not found: {artifact.BundlePath}",
                        artifact.BundlePath
                    );
                }
                if (!string.Equals(
                        artifact.ManifestPath,
                        artifact.BundlePath + ".manifest",
                        StringComparison.Ordinal
                    ))
                {
                    throw new InvalidDataException(
                        $"Platform '{artifact.PlatformKey}' Unity manifest path must match its Bundle path."
                    );
                }
                if (!File.Exists(artifact.ManifestPath))
                {
                    throw new FileNotFoundException(
                        $"Platform Unity manifest was not found: {artifact.ManifestPath}",
                        artifact.ManifestPath
                    );
                }
                if (artifact.Dependencies?.Length > 0)
                {
                    throw new InvalidDataException(
                        $"Platform '{artifact.PlatformKey}' contains external Bundle dependencies."
                    );
                }
            }
        }

        private static AssetBundlePackageManifest CreateManifest(
            AssetBundlePackageDefinition definition,
            IReadOnlyList<AssetBundlePlatformArtifact> artifacts
        )
        {
            var ios = artifacts.Single(artifact => artifact.PlatformKey == "ios");
            var android = artifacts.Single(artifact => artifact.PlatformKey == "android");
            return new AssetBundlePackageManifest
            {
                SchemaVersion = AssetBundlePackageContract.SchemaVersion,
                BundleName = definition.BundleName,
                DisplayName = definition.DisplayName,
                ContentVersion = definition.ContentVersion,
                UnityVersion = definition.UnityVersion,
                GeneratedAtUtc = definition.GeneratedAtUtc
                    .ToUniversalTime()
                    .ToString("O", CultureInfo.InvariantCulture),
                Prefabs = definition.Prefabs
                    .Select(prefab => new AssetBundlePackagePrefabManifest
                    {
                        Name = prefab.Name,
                        AssetPath = prefab.AssetPath
                    })
                    .ToArray(),
                Platforms = new AssetBundlePackagePlatformsManifest
                {
                    Ios = CreatePlatformManifest(ios, "ios", "iOS"),
                    Android = CreatePlatformManifest(android, "android", "Android")
                }
            };
        }

        private static AssetBundlePackagePlatformManifest CreatePlatformManifest(
            AssetBundlePlatformArtifact artifact,
            string platformKey,
            string buildTarget
        )
        {
            return new AssetBundlePackagePlatformManifest
            {
                BuildTarget = buildTarget,
                Path = AssetBundlePackageContract.GetBundleEntryPath(
                    platformKey,
                    artifact.BundleName
                ),
                Size = new FileInfo(artifact.BundlePath).Length,
                Sha256 = ComputeSha256(artifact.BundlePath),
                ManifestPath = AssetBundlePackageContract.GetBundleManifestEntryPath(
                    platformKey,
                    artifact.BundleName
                ),
                ManifestSize = new FileInfo(artifact.ManifestPath).Length,
                ManifestSha256 = ComputeSha256(artifact.ManifestPath),
                Dependencies = Array.Empty<string>()
            };
        }

        private static void WriteArchive(
            string partialPath,
            AssetBundlePackageManifest manifest,
            IReadOnlyList<AssetBundlePlatformArtifact> artifacts
        )
        {
            using var output = new FileStream(
                partialPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None
            );
            using var archive = new ZipArchive(output, ZipArchiveMode.Create, false);

            var manifestEntry = archive.CreateEntry(
                AssetBundlePackageContract.ManifestEntryName,
                CompressionLevel.Optimal
            );
            using (var entryStream = manifestEntry.Open())
            using (var writer = new StreamWriter(entryStream, Utf8WithoutBom, 1024, false))
            {
                writer.Write(AssetBundlePackageManifestJson.Serialize(manifest));
            }

            foreach (var artifact in artifacts.OrderBy(artifact => artifact.PlatformKey, StringComparer.Ordinal))
            {
                WriteFileEntry(
                    archive,
                    AssetBundlePackageContract.GetBundleEntryPath(
                        artifact.PlatformKey,
                        artifact.BundleName
                    ),
                    artifact.BundlePath
                );
                WriteFileEntry(
                    archive,
                    AssetBundlePackageContract.GetBundleManifestEntryPath(
                        artifact.PlatformKey,
                        artifact.BundleName
                    ),
                    artifact.ManifestPath
                );
            }
        }

        private static void WriteFileEntry(
            ZipArchive archive,
            string entryPath,
            string sourcePath
        )
        {
            var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
            using (var source = new FileStream(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read
                ))
            using (var destination = entry.Open())
            {
                source.CopyTo(destination, 81920);
            }
        }

        private static void ValidateBundleEntry(
            IReadOnlyCollection<ZipArchiveEntry> entries,
            AssetBundlePackagePlatformManifest platform,
            string platformKey
        )
        {
            var entry = entries.SingleOrDefault(candidate =>
                string.Equals(candidate.FullName, platform.Path, StringComparison.Ordinal)
            );
            if (entry == null)
            {
                throw new InvalidDataException($"AssetBundle package is missing the {platformKey} Bundle.");
            }
            if (entry.Length != platform.Size)
            {
                throw new InvalidDataException(
                    $"AssetBundle package {platformKey} Bundle size does not match package.json."
                );
            }

            using var stream = entry.Open();
            var actualSha256 = ComputeSha256(stream);
            if (!string.Equals(actualSha256, platform.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"AssetBundle package {platformKey} Bundle SHA-256 does not match package.json."
                );
            }
        }

        private static void ValidateUnityManifestEntry(
            IReadOnlyCollection<ZipArchiveEntry> entries,
            AssetBundlePackagePlatformManifest platform,
            string platformKey
        )
        {
            var entry = entries.SingleOrDefault(candidate =>
                string.Equals(candidate.FullName, platform.ManifestPath, StringComparison.Ordinal)
            );
            if (entry == null)
            {
                throw new InvalidDataException(
                    $"AssetBundle package is missing the {platformKey} Unity manifest."
                );
            }
            if (entry.Length != platform.ManifestSize)
            {
                throw new InvalidDataException(
                    $"AssetBundle package {platformKey} Unity manifest size does not match package.json."
                );
            }

            using var stream = entry.Open();
            var actualSha256 = ComputeSha256(stream);
            if (!string.Equals(actualSha256, platform.ManifestSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"AssetBundle package {platformKey} Unity manifest SHA-256 does not match package.json."
                );
            }
        }

        private static void TryDeleteFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
                // Preserve the original archive generation failure.
            }
        }
    }
}
