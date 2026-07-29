using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Marble.AssetBundleBuilder.Editor
{
    internal sealed class AssetBundlePlatformArtifact
    {
        public string PlatformKey { get; set; }
        public BuildTarget BuildTarget { get; set; }
        public string BundleName { get; set; }
        public string BundlePath { get; set; }
        public string ManifestPath { get; set; }
        public string[] Dependencies { get; set; } = Array.Empty<string>();
    }

    internal sealed class AssetBundlePlatformBuildOutput
    {
        public string[] Dependencies { get; set; } = Array.Empty<string>();
    }

    internal interface IAssetBundlePlatformBuildApi
    {
        BuildTarget ActiveBuildTarget { get; }
        BuildTargetGroup GetBuildTargetGroup(BuildTarget target);
        bool SwitchActiveBuildTarget(BuildTargetGroup group, BuildTarget target);
        AssetBundlePlatformBuildOutput BuildAssetBundles(
            string outputDirectory,
            AssetBundleBuild[] buildMap,
            BuildAssetBundleOptions options,
            BuildTarget target
        );
        IReadOnlyList<string> GetMissingPrefabNames(
            string bundlePath,
            IReadOnlyList<string> prefabNames
        );
    }

    internal interface IAssetBundlePlatformBuilder
    {
        IReadOnlyList<AssetBundlePlatformArtifact> Build(
            AssetBundlePackageDefinition definition,
            string temporaryRoot,
            Func<bool> isCancellationRequested = null,
            Action<BuildTarget> onTargetStarting = null
        );
    }

    internal sealed class AssetBundlePlatformBuilder : IAssetBundlePlatformBuilder
    {
        private static readonly BuildAssetBundleOptions BuildOptions =
            BuildAssetBundleOptions.ChunkBasedCompression
            | BuildAssetBundleOptions.ForceRebuildAssetBundle;

        private readonly IAssetBundlePlatformBuildApi _api;

        internal AssetBundlePlatformBuilder()
            : this(new UnityAssetBundlePlatformBuildApi())
        {
        }

        internal AssetBundlePlatformBuilder(IAssetBundlePlatformBuildApi api)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
        }

        public IReadOnlyList<AssetBundlePlatformArtifact> Build(
            AssetBundlePackageDefinition definition,
            string temporaryRoot,
            Func<bool> isCancellationRequested = null,
            Action<BuildTarget> onTargetStarting = null
        )
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }
            if (string.IsNullOrWhiteSpace(temporaryRoot))
            {
                throw new ArgumentException("Temporary root is required.", nameof(temporaryRoot));
            }

            var definitionIssues = AssetBundlePackageContract.ValidateDefinition(definition);
            if (definitionIssues.Any(issue => issue.Severity == AssetBundlePackageIssueSeverity.Error))
            {
                throw new InvalidOperationException(
                    "Package definition is invalid: "
                    + string.Join(", ", definitionIssues.Select(issue => issue.Message))
                );
            }

            var originalTarget = _api.ActiveBuildTarget;
            var artifacts = new List<AssetBundlePlatformArtifact>();
            Directory.CreateDirectory(temporaryRoot);

            try
            {
                artifacts.Add(BuildTargetArtifact(
                    definition,
                    temporaryRoot,
                    "ios",
                    BuildTarget.iOS,
                    isCancellationRequested,
                    onTargetStarting
                ));
                artifacts.Add(BuildTargetArtifact(
                    definition,
                    temporaryRoot,
                    "android",
                    BuildTarget.Android,
                    isCancellationRequested,
                    onTargetStarting
                ));
                return artifacts;
            }
            catch
            {
                TryCleanupBuildDirectories(temporaryRoot);
                throw;
            }
            finally
            {
                RestoreBuildTarget(originalTarget, temporaryRoot);
            }
        }

        internal static AssetBundleBuild[] CreateBuildMap(AssetBundlePackageDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            return new[]
            {
                new AssetBundleBuild
                {
                    assetBundleName = definition.BundleName,
                    assetNames = definition.Prefabs.Select(prefab => prefab.AssetPath).ToArray(),
                    addressableNames = definition.Prefabs.Select(prefab => prefab.Name).ToArray()
                }
            };
        }

        private AssetBundlePlatformArtifact BuildTargetArtifact(
            AssetBundlePackageDefinition definition,
            string temporaryRoot,
            string platformKey,
            BuildTarget target,
            Func<bool> isCancellationRequested,
            Action<BuildTarget> onTargetStarting
        )
        {
            ThrowIfCancelled(isCancellationRequested);
            onTargetStarting?.Invoke(target);
            ThrowIfCancelled(isCancellationRequested);
            SwitchBuildTarget(target);

            var targetDirectory = Path.Combine(temporaryRoot, platformKey);
            if (Directory.Exists(targetDirectory))
            {
                if (Directory.EnumerateFileSystemEntries(targetDirectory).Any())
                {
                    throw new InvalidOperationException(
                        $"AssetBundle target temporary directory must be empty: {targetDirectory}"
                    );
                }
                Directory.Delete(targetDirectory, false);
            }
            Directory.CreateDirectory(targetDirectory);

            var output = _api.BuildAssetBundles(
                targetDirectory,
                CreateBuildMap(definition),
                BuildOptions,
                target
            ) ?? throw new InvalidOperationException($"AssetBundle build returned no manifest for {target}.");
            var dependencies = output.Dependencies ?? Array.Empty<string>();
            if (dependencies.Length > 0)
            {
                throw new InvalidOperationException(
                    $"AssetBundle '{definition.BundleName}' has external dependencies on {target}: "
                    + string.Join(", ", dependencies)
                );
            }

            var bundlePath = Path.Combine(targetDirectory, definition.BundleName);
            if (!File.Exists(bundlePath))
            {
                throw new InvalidOperationException($"AssetBundle output was not found: {bundlePath}");
            }
            var manifestPath = bundlePath + ".manifest";
            if (!File.Exists(manifestPath))
            {
                throw new InvalidOperationException(
                    $"AssetBundle manifest output was not found: {manifestPath}"
                );
            }

            var prefabNames = definition.Prefabs.Select(prefab => prefab.Name).ToArray();
            var missingPrefabNames = _api.GetMissingPrefabNames(bundlePath, prefabNames);
            if (missingPrefabNames.Count > 0)
            {
                throw new InvalidOperationException(
                    $"AssetBundle '{definition.BundleName}' cannot load Prefab(s) by name on {target}: "
                    + string.Join(", ", missingPrefabNames)
                );
            }

            return new AssetBundlePlatformArtifact
            {
                PlatformKey = platformKey,
                BuildTarget = target,
                BundleName = definition.BundleName,
                BundlePath = bundlePath,
                ManifestPath = manifestPath,
                Dependencies = dependencies
            };
        }

        private void SwitchBuildTarget(BuildTarget target)
        {
            if (_api.ActiveBuildTarget == target)
            {
                return;
            }

            var group = _api.GetBuildTargetGroup(target);
            if (!_api.SwitchActiveBuildTarget(group, target))
            {
                throw new InvalidOperationException($"Failed to switch active BuildTarget to {target}.");
            }
        }

        private void RestoreBuildTarget(BuildTarget originalTarget, string temporaryRoot)
        {
            if (_api.ActiveBuildTarget == originalTarget)
            {
                return;
            }

            var group = _api.GetBuildTargetGroup(originalTarget);
            if (!_api.SwitchActiveBuildTarget(group, originalTarget))
            {
                TryCleanupBuildDirectories(temporaryRoot);
                throw new InvalidOperationException(
                    $"Failed to restore the original active BuildTarget: {originalTarget}."
                );
            }
        }

        private static void ThrowIfCancelled(Func<bool> isCancellationRequested)
        {
            if (isCancellationRequested?.Invoke() == true)
            {
                throw new OperationCanceledException("AssetBundle package build was cancelled.");
            }
        }

        private static void TryCleanupBuildDirectories(string temporaryRoot)
        {
            if (string.IsNullOrEmpty(temporaryRoot) || !Directory.Exists(temporaryRoot))
            {
                return;
            }

            try
            {
                var absoluteRoot = Path.GetFullPath(temporaryRoot).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                );
                foreach (var platformKey in new[] { "ios", "android" })
                {
                    var childPath = Path.GetFullPath(Path.Combine(absoluteRoot, platformKey));
                    var parentPath = Directory.GetParent(childPath)?.FullName?.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar
                    );
                    if (!string.Equals(parentPath, absoluteRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (Directory.Exists(childPath))
                    {
                        Directory.Delete(childPath, true);
                    }
                }

                if (!Directory.EnumerateFileSystemEntries(absoluteRoot).Any())
                {
                    Directory.Delete(absoluteRoot, false);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Failed to clean AssetBundle temporary directory '{temporaryRoot}': {exception.Message}"
                );
            }
        }
    }

    internal sealed class UnityAssetBundlePlatformBuildApi : IAssetBundlePlatformBuildApi
    {
        public BuildTarget ActiveBuildTarget => EditorUserBuildSettings.activeBuildTarget;

        public BuildTargetGroup GetBuildTargetGroup(BuildTarget target)
        {
            return BuildPipeline.GetBuildTargetGroup(target);
        }

        public bool SwitchActiveBuildTarget(BuildTargetGroup group, BuildTarget target)
        {
            return EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);
        }

        public AssetBundlePlatformBuildOutput BuildAssetBundles(
            string outputDirectory,
            AssetBundleBuild[] buildMap,
            BuildAssetBundleOptions options,
            BuildTarget target
        )
        {
            var manifest = BuildPipeline.BuildAssetBundles(
                outputDirectory,
                buildMap,
                options,
                target
            );
            if (manifest == null)
            {
                return null;
            }

            return new AssetBundlePlatformBuildOutput
            {
                Dependencies = manifest.GetAllDependencies(buildMap.Single().assetBundleName)
            };
        }

        public IReadOnlyList<string> GetMissingPrefabNames(
            string bundlePath,
            IReadOnlyList<string> prefabNames
        )
        {
            var bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null)
            {
                return prefabNames.ToArray();
            }

            try
            {
                return prefabNames
                    .Where(prefabName => bundle.LoadAsset<GameObject>(prefabName) == null)
                    .ToArray();
            }
            finally
            {
                bundle.Unload(true);
            }
        }
    }
}
