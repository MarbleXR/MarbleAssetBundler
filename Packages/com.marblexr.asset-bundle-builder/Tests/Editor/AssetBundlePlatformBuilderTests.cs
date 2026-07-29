using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Marble.AssetBundleBuilder.Editor;
using NUnit.Framework;
using UnityEditor;

namespace Marble.Tests.Editor
{
    [TestFixture]
    public sealed class AssetBundlePlatformBuilderTests
    {
        private string _tempRoot;
        private FakeAssetBundlePlatformBuildApi _api;
        private AssetBundlePlatformBuilder _builder;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "MarblePlatformBuilderTests", Guid.NewGuid().ToString("N"));
            _api = new FakeAssetBundlePlatformBuildApi();
            _builder = new AssetBundlePlatformBuilder(_api);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }

        [Test]
        public void CreateBuildMap_UsesPrefabPathsAndNamesWithoutLabels()
        {
            var definition = CreateDefinition();
            definition.Prefabs.Add(new AssetBundlePackagePrefabDefinition
            {
                Name = "MuseumTable",
                AssetPath = "Assets/Content/MuseumTable.prefab"
            });

            var map = AssetBundlePlatformBuilder.CreateBuildMap(definition).Single();

            Assert.That(map.assetBundleName, Is.EqualTo("museum_chair"));
            Assert.That(map.assetNames, Is.EqualTo(new[]
            {
                "Assets/Content/MuseumChair.prefab",
                "Assets/Content/MuseumTable.prefab"
            }));
            Assert.That(map.addressableNames, Is.EqualTo(new[] { "MuseumChair", "MuseumTable" }));
        }

        [Test]
        public void Build_CreatesBothPlatformArtifactsAndRestoresOriginalTarget()
        {
            var artifacts = _builder.Build(CreateDefinition(), _tempRoot);

            Assert.That(artifacts.Select(artifact => artifact.PlatformKey), Is.EqualTo(new[] { "ios", "android" }));
            Assert.That(artifacts, Has.All.Property("BundleName").EqualTo("museum_chair"));
            Assert.That(_api.BuildRequests.Select(request => request.Target), Is.EqualTo(new[]
            {
                BuildTarget.iOS,
                BuildTarget.Android
            }));
            Assert.That(_api.BuildRequests.All(request =>
                request.Options.HasFlag(BuildAssetBundleOptions.ChunkBasedCompression)
                && request.Options.HasFlag(BuildAssetBundleOptions.ForceRebuildAssetBundle)
            ), Is.True);
            Assert.That(_api.ActiveBuildTarget, Is.EqualTo(BuildTarget.StandaloneOSX));
            Assert.That(_api.SwitchTargets.Last(), Is.EqualTo(BuildTarget.StandaloneOSX));
            Assert.That(artifacts.All(artifact => File.Exists(artifact.BundlePath)), Is.True);
            Assert.That(artifacts.All(artifact => File.Exists(artifact.ManifestPath)), Is.True);
        }

        [Test]
        public void Build_RejectsExternalBundleDependenciesAndCleansTemporaryOutput()
        {
            _api.DependenciesByTarget[BuildTarget.iOS] = new[] { "shared_materials" };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                _builder.Build(CreateDefinition(), _tempRoot)
            );

            Assert.That(exception.Message, Does.Contain("shared_materials"));
            Assert.That(_api.ActiveBuildTarget, Is.EqualTo(BuildTarget.StandaloneOSX));
            Assert.That(Directory.Exists(_tempRoot), Is.False);
        }

        [Test]
        public void Build_FailureDoesNotDeleteUnrelatedFilesFromProvidedTemporaryRoot()
        {
            Directory.CreateDirectory(_tempRoot);
            var unrelatedPath = Path.Combine(_tempRoot, "keep.txt");
            File.WriteAllText(unrelatedPath, "keep");
            _api.DependenciesByTarget[BuildTarget.iOS] = new[] { "shared_materials" };

            Assert.Throws<InvalidOperationException>(() =>
                _builder.Build(CreateDefinition(), _tempRoot)
            );

            Assert.That(File.Exists(unrelatedPath), Is.True);
            Assert.That(_api.ActiveBuildTarget, Is.EqualTo(BuildTarget.StandaloneOSX));
        }

        [Test]
        public void Build_RejectsBundleThatCannotLoadEveryPrefab()
        {
            _api.MissingPrefabNamesByTarget[BuildTarget.Android] = new[] { "MuseumChair" };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                _builder.Build(CreateDefinition(), _tempRoot)
            );

            Assert.That(exception.Message, Does.Contain("MuseumChair"));
            Assert.That(_api.ActiveBuildTarget, Is.EqualTo(BuildTarget.StandaloneOSX));
            Assert.That(Directory.Exists(_tempRoot), Is.False);
        }

        [Test]
        public void Build_RejectsMissingUnityManifestAndCleansTemporaryOutput()
        {
            _api.WriteUnityManifest = false;

            var exception = Assert.Throws<InvalidOperationException>(() =>
                _builder.Build(CreateDefinition(), _tempRoot)
            );

            Assert.That(exception.Message, Does.Contain("manifest output was not found"));
            Assert.That(_api.ActiveBuildTarget, Is.EqualTo(BuildTarget.StandaloneOSX));
            Assert.That(Directory.Exists(_tempRoot), Is.False);
        }

        [Test]
        public void Build_CancellationRestoresTargetAndCleansTemporaryOutput()
        {
            var checks = 0;

            Assert.Throws<OperationCanceledException>(() =>
                _builder.Build(CreateDefinition(), _tempRoot, () => ++checks > 1)
            );

            Assert.That(_api.BuildRequests.Count, Is.Zero);
            Assert.That(_api.ActiveBuildTarget, Is.EqualTo(BuildTarget.StandaloneOSX));
            Assert.That(Directory.Exists(_tempRoot), Is.False);
        }

        private static AssetBundlePackageDefinition CreateDefinition()
        {
            return AssetBundlePackageContractTests.CreateValidDefinition();
        }

        private sealed class FakeBuildRequest
        {
            internal BuildTarget Target { get; set; }
            internal BuildAssetBundleOptions Options { get; set; }
        }

        private sealed class FakeAssetBundlePlatformBuildApi : IAssetBundlePlatformBuildApi
        {
            internal List<FakeBuildRequest> BuildRequests { get; } = new();
            internal List<BuildTarget> SwitchTargets { get; } = new();
            internal Dictionary<BuildTarget, string[]> DependenciesByTarget { get; } = new();
            internal Dictionary<BuildTarget, string[]> MissingPrefabNamesByTarget { get; } = new();
            internal bool WriteUnityManifest { get; set; } = true;

            public BuildTarget ActiveBuildTarget { get; private set; } = BuildTarget.StandaloneOSX;

            public BuildTargetGroup GetBuildTargetGroup(BuildTarget target)
            {
                return BuildPipeline.GetBuildTargetGroup(target);
            }

            public bool SwitchActiveBuildTarget(BuildTargetGroup group, BuildTarget target)
            {
                SwitchTargets.Add(target);
                ActiveBuildTarget = target;
                return true;
            }

            public AssetBundlePlatformBuildOutput BuildAssetBundles(
                string outputDirectory,
                AssetBundleBuild[] buildMap,
                BuildAssetBundleOptions options,
                BuildTarget target
            )
            {
                BuildRequests.Add(new FakeBuildRequest { Target = target, Options = options });
                Directory.CreateDirectory(outputDirectory);
                var bundlePath = Path.Combine(outputDirectory, buildMap.Single().assetBundleName);
                File.WriteAllText(bundlePath, target.ToString());
                if (WriteUnityManifest)
                {
                    File.WriteAllText(bundlePath + ".manifest", $"Manifest for {target}");
                }
                return new AssetBundlePlatformBuildOutput
                {
                    Dependencies = DependenciesByTarget.TryGetValue(target, out var dependencies)
                        ? dependencies
                        : Array.Empty<string>()
                };
            }

            public IReadOnlyList<string> GetMissingPrefabNames(
                string bundlePath,
                IReadOnlyList<string> prefabNames
            )
            {
                return MissingPrefabNamesByTarget.TryGetValue(ActiveBuildTarget, out var names)
                    ? names
                    : Array.Empty<string>();
            }
        }
    }
}
