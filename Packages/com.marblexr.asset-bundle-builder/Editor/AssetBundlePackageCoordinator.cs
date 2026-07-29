using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Marble.AssetBundleBuilder.Editor
{
    internal enum AssetBundlePackageProgressPhase
    {
        Preflight,
        BuildIos,
        BuildAndroid,
        Archive,
        Completed,
        Cancelled,
        Failed
    }

    internal sealed class AssetBundlePackageProgress
    {
        public AssetBundlePackageProgress(
            AssetBundlePackageProgressPhase phase,
            string message,
            float normalizedProgress
        )
        {
            Phase = phase;
            Message = message;
            NormalizedProgress = normalizedProgress;
        }

        public AssetBundlePackageProgressPhase Phase { get; }
        public string Message { get; }
        public float NormalizedProgress { get; }
    }

    internal sealed class AssetBundlePackageBuildResult
    {
        private AssetBundlePackageBuildResult(
            bool succeeded,
            bool cancelled,
            string packagePath,
            AssetBundlePackageManifest manifest,
            IReadOnlyList<AssetBundlePackageIssue> issues
        )
        {
            Succeeded = succeeded;
            Cancelled = cancelled;
            PackagePath = packagePath;
            Manifest = manifest;
            Issues = issues ?? Array.Empty<AssetBundlePackageIssue>();
        }

        public bool Succeeded { get; }
        public bool Cancelled { get; }
        public string PackagePath { get; }
        public AssetBundlePackageManifest Manifest { get; }
        public IReadOnlyList<AssetBundlePackageIssue> Issues { get; }

        internal static AssetBundlePackageBuildResult Success(
            AssetBundlePackageArchiveResult archive,
            IReadOnlyList<AssetBundlePackageIssue> issues
        )
        {
            return new AssetBundlePackageBuildResult(
                true,
                false,
                archive.PackagePath,
                archive.Manifest,
                issues
            );
        }

        internal static AssetBundlePackageBuildResult Failure(
            IReadOnlyList<AssetBundlePackageIssue> issues
        )
        {
            return new AssetBundlePackageBuildResult(false, false, null, null, issues);
        }

        internal static AssetBundlePackageBuildResult Cancellation(
            IReadOnlyList<AssetBundlePackageIssue> issues
        )
        {
            return new AssetBundlePackageBuildResult(false, true, null, null, issues);
        }
    }

    internal sealed class AssetBundlePackageCoordinator
    {
        private readonly IAssetBundlePackagePreflightRunner _preflight;
        private readonly IAssetBundlePlatformBuilder _platformBuilder;
        private readonly IAssetBundlePackageArchiveWriter _archive;
        private readonly Func<string> _temporaryRootFactory;

        internal AssetBundlePackageCoordinator()
            : this(
                new AssetBundlePackagePreflightRunner(),
                new AssetBundlePlatformBuilder(),
                new AssetBundlePackageArchive(),
                CreateTemporaryRoot
            )
        {
        }

        internal AssetBundlePackageCoordinator(
            IAssetBundlePackagePreflightRunner preflight,
            IAssetBundlePlatformBuilder platformBuilder,
            IAssetBundlePackageArchiveWriter archive,
            Func<string> temporaryRootFactory
        )
        {
            _preflight = preflight ?? throw new ArgumentNullException(nameof(preflight));
            _platformBuilder = platformBuilder ?? throw new ArgumentNullException(nameof(platformBuilder));
            _archive = archive ?? throw new ArgumentNullException(nameof(archive));
            _temporaryRootFactory = temporaryRootFactory
                                    ?? throw new ArgumentNullException(nameof(temporaryRootFactory));
        }

        internal AssetBundlePackagePreflightResult Validate(AssetBundlePackageBuildRequest request)
        {
            return _preflight.Validate(request);
        }

        internal AssetBundlePackageBuildResult Build(
            AssetBundlePackageBuildRequest request,
            Action<AssetBundlePackageProgress> onProgress = null,
            Func<bool> isCancellationRequested = null
        )
        {
            Report(
                onProgress,
                AssetBundlePackageProgressPhase.Preflight,
                "Validating package inputs and Prefabs...",
                0.05f
            );
            var preflightResult = _preflight.Validate(request);
            if (preflightResult.HasErrors)
            {
                Report(
                    onProgress,
                    AssetBundlePackageProgressPhase.Failed,
                    "Preflight validation failed.",
                    1f
                );
                return AssetBundlePackageBuildResult.Failure(preflightResult.Issues);
            }

            var issues = preflightResult.Issues.ToList();
            string temporaryRoot = null;
            try
            {
                ThrowIfCancelled(isCancellationRequested);
                temporaryRoot = _temporaryRootFactory();
                if (Directory.Exists(temporaryRoot))
                {
                    throw new InvalidOperationException(
                        $"AssetBundle package temporary directory already exists: {temporaryRoot}"
                    );
                }
                Directory.CreateDirectory(temporaryRoot);

                var artifacts = _platformBuilder.Build(
                    preflightResult.Definition,
                    temporaryRoot,
                    isCancellationRequested,
                    target => ReportTarget(onProgress, target)
                );
                ThrowIfCancelled(isCancellationRequested);

                Report(
                    onProgress,
                    AssetBundlePackageProgressPhase.Archive,
                    "Generating and validating upload ZIP...",
                    0.85f
                );
                var archiveResult = _archive.Create(
                    preflightResult.Definition,
                    artifacts,
                    request.OutputDirectory
                );
                Report(
                    onProgress,
                    AssetBundlePackageProgressPhase.Completed,
                    "AssetBundle package completed.",
                    1f
                );
                return AssetBundlePackageBuildResult.Success(archiveResult, issues);
            }
            catch (OperationCanceledException)
            {
                Report(
                    onProgress,
                    AssetBundlePackageProgressPhase.Cancelled,
                    "AssetBundle package build was cancelled.",
                    1f
                );
                return AssetBundlePackageBuildResult.Cancellation(issues);
            }
            catch (Exception exception)
            {
                issues.Add(new AssetBundlePackageIssue(
                    "package_build_failed",
                    $"AssetBundle package build failed: {exception.Message}"
                ));
                Report(
                    onProgress,
                    AssetBundlePackageProgressPhase.Failed,
                    "AssetBundle package build failed.",
                    1f
                );
                return AssetBundlePackageBuildResult.Failure(issues);
            }
            finally
            {
                TryDeleteDirectory(temporaryRoot);
            }
        }

        private static void ReportTarget(
            Action<AssetBundlePackageProgress> onProgress,
            BuildTarget target
        )
        {
            if (target == BuildTarget.iOS)
            {
                Report(
                    onProgress,
                    AssetBundlePackageProgressPhase.BuildIos,
                    "Building and validating iOS AssetBundle...",
                    0.25f
                );
                return;
            }

            Report(
                onProgress,
                AssetBundlePackageProgressPhase.BuildAndroid,
                "Building and validating Android AssetBundle...",
                0.55f
            );
        }

        private static void Report(
            Action<AssetBundlePackageProgress> onProgress,
            AssetBundlePackageProgressPhase phase,
            string message,
            float progress
        )
        {
            onProgress?.Invoke(new AssetBundlePackageProgress(phase, message, progress));
        }

        private static void ThrowIfCancelled(Func<bool> isCancellationRequested)
        {
            if (isCancellationRequested?.Invoke() == true)
            {
                throw new OperationCanceledException();
            }
        }

        private static string CreateTemporaryRoot()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "MarbleAssetBundlePackages",
                Guid.NewGuid().ToString("N")
            );
        }

        private static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                return;
            }

            try
            {
                Directory.Delete(path, true);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Failed to clean AssetBundle package temporary directory '{path}': {exception.Message}"
                );
            }
        }
    }
}
