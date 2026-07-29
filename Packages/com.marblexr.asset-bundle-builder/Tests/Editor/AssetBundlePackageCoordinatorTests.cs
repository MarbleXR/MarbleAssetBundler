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
    public sealed class AssetBundlePackageCoordinatorTests
    {
        private string _tempRoot;
        private FakePreflightRunner _preflight;
        private FakePlatformBuilder _platformBuilder;
        private FakeArchiveWriter _archive;
        private AssetBundlePackageCoordinator _coordinator;
        private AssetBundlePackageBuildRequest _request;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "MarbleCoordinatorTests", Guid.NewGuid().ToString("N"));
            _preflight = new FakePreflightRunner(CreateValidPreflightResult());
            _platformBuilder = new FakePlatformBuilder();
            _archive = new FakeArchiveWriter();
            _coordinator = new AssetBundlePackageCoordinator(
                _preflight,
                _platformBuilder,
                _archive,
                () => _tempRoot
            );
            _request = new AssetBundlePackageBuildRequest
            {
                BundleName = "museum_chair",
                DisplayName = "Museum Chair",
                ContentVersion = "2026.07.20.1",
                OutputDirectory = Path.Combine(Path.GetTempPath(), "MarblePackages")
            };
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
        public void Build_ExecutesPhasesInOrderAndCleansTemporaryDirectory()
        {
            var phases = new List<AssetBundlePackageProgressPhase>();

            var result = _coordinator.Build(
                _request,
                progress => phases.Add(progress.Phase)
            );

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.PackagePath, Is.EqualTo("/packages/museum_chair-2026.07.20.1.zip"));
            Assert.That(phases, Is.EqualTo(new[]
            {
                AssetBundlePackageProgressPhase.Preflight,
                AssetBundlePackageProgressPhase.BuildIos,
                AssetBundlePackageProgressPhase.BuildAndroid,
                AssetBundlePackageProgressPhase.Archive,
                AssetBundlePackageProgressPhase.Completed
            }));
            Assert.That(_platformBuilder.CallCount, Is.EqualTo(1));
            Assert.That(_archive.CallCount, Is.EqualTo(1));
            Assert.That(Directory.Exists(_tempRoot), Is.False);
        }

        [Test]
        public void Build_StopsBeforeBuildWhenPreflightHasErrors()
        {
            _preflight.Result = CreatePreflightResult(new AssetBundlePackageIssue(
                "prefab_missing_script",
                "Missing Script"
            ));

            var result = _coordinator.Build(_request);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Cancelled, Is.False);
            Assert.That(result.Issues.Select(issue => issue.Code), Does.Contain("prefab_missing_script"));
            Assert.That(_platformBuilder.CallCount, Is.Zero);
            Assert.That(_archive.CallCount, Is.Zero);
        }

        [Test]
        public void Build_AllowsWarningsAndReturnsThemToCaller()
        {
            _preflight.Result = CreatePreflightResult(new AssetBundlePackageIssue(
                "mono_behaviour_stripping_risk",
                "Confirm stripping",
                AssetBundlePackageIssueSeverity.Warning
            ));

            var result = _coordinator.Build(_request);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Issues.Single().Severity, Is.EqualTo(AssetBundlePackageIssueSeverity.Warning));
        }

        [Test]
        public void Build_CancellationStopsWorkflowAndCleansTemporaryDirectory()
        {
            var cancellationChecks = 0;
            _platformBuilder.CheckCancellation = true;

            var result = _coordinator.Build(
                _request,
                isCancellationRequested: () => ++cancellationChecks >= 2
            );

            Assert.That(result.Cancelled, Is.True);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(_archive.CallCount, Is.Zero);
            Assert.That(Directory.Exists(_tempRoot), Is.False);
        }

        [Test]
        public void Build_NormalizesExceptionsIntoFailureResult()
        {
            _platformBuilder.Exception = new InvalidOperationException("build exploded");

            var result = _coordinator.Build(_request);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Issues.Select(issue => issue.Code), Does.Contain("package_build_failed"));
            Assert.That(result.Issues.Single(issue => issue.Code == "package_build_failed").Message, Does.Contain("build exploded"));
            Assert.That(Directory.Exists(_tempRoot), Is.False);
        }

        private static AssetBundlePackagePreflightResult CreateValidPreflightResult()
        {
            return CreatePreflightResult();
        }

        private static AssetBundlePackagePreflightResult CreatePreflightResult(
            params AssetBundlePackageIssue[] issues
        )
        {
            return new AssetBundlePackagePreflightResult(
                AssetBundlePackageContractTests.CreateValidDefinition(),
                Array.Empty<AssetBundlePrefabInspection>(),
                issues
            );
        }

        private sealed class FakePreflightRunner : IAssetBundlePackagePreflightRunner
        {
            internal FakePreflightRunner(AssetBundlePackagePreflightResult result)
            {
                Result = result;
            }

            internal AssetBundlePackagePreflightResult Result { get; set; }

            public AssetBundlePackagePreflightResult Validate(AssetBundlePackageBuildRequest request)
            {
                return Result;
            }
        }

        private sealed class FakePlatformBuilder : IAssetBundlePlatformBuilder
        {
            internal int CallCount { get; private set; }
            internal bool CheckCancellation { get; set; }
            internal Exception Exception { get; set; }

            public IReadOnlyList<AssetBundlePlatformArtifact> Build(
                AssetBundlePackageDefinition definition,
                string temporaryRoot,
                Func<bool> isCancellationRequested = null,
                Action<BuildTarget> onTargetStarting = null
            )
            {
                CallCount++;
                Directory.CreateDirectory(temporaryRoot);
                if (Exception != null)
                {
                    throw Exception;
                }
                if (CheckCancellation && isCancellationRequested?.Invoke() == true)
                {
                    throw new OperationCanceledException();
                }

                onTargetStarting?.Invoke(BuildTarget.iOS);
                onTargetStarting?.Invoke(BuildTarget.Android);
                return new[]
                {
                    new AssetBundlePlatformArtifact
                    {
                        PlatformKey = "ios",
                        BuildTarget = BuildTarget.iOS,
                        BundleName = definition.BundleName,
                        BundlePath = Path.Combine(temporaryRoot, "ios", definition.BundleName),
                        ManifestPath = Path.Combine(
                            temporaryRoot,
                            "ios",
                            definition.BundleName + ".manifest"
                        )
                    },
                    new AssetBundlePlatformArtifact
                    {
                        PlatformKey = "android",
                        BuildTarget = BuildTarget.Android,
                        BundleName = definition.BundleName,
                        BundlePath = Path.Combine(temporaryRoot, "android", definition.BundleName),
                        ManifestPath = Path.Combine(
                            temporaryRoot,
                            "android",
                            definition.BundleName + ".manifest"
                        )
                    }
                };
            }
        }

        private sealed class FakeArchiveWriter : IAssetBundlePackageArchiveWriter
        {
            internal int CallCount { get; private set; }

            public AssetBundlePackageArchiveResult Create(
                AssetBundlePackageDefinition definition,
                IReadOnlyList<AssetBundlePlatformArtifact> artifacts,
                string outputDirectory
            )
            {
                CallCount++;
                return new AssetBundlePackageArchiveResult(
                    "/packages/museum_chair-2026.07.20.1.zip",
                    AssetBundlePackageContractTests.CreateValidManifest()
                );
            }
        }
    }
}
