using System.Collections.Generic;
using System.IO;
using System.Linq;
using Marble.AssetBundleBuilder.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Marble.Tests.Editor
{
    [TestFixture]
    public sealed class AssetBundlePackagePreflightTests
    {
        private readonly List<GameObject> _objects = new();
        private FakePreflightEnvironment _environment;
        private AssetBundlePackagePreflightRunner _runner;
        private AssetBundlePackageBuildRequest _request;

        [SetUp]
        public void SetUp()
        {
            _environment = new FakePreflightEnvironment();
            _runner = new AssetBundlePackagePreflightRunner(_environment);
            _request = new AssetBundlePackageBuildRequest
            {
                BundleName = "museum_chair",
                DisplayName = "Museum Chair",
                ContentVersion = "2026.07.20.1",
                OutputDirectory = Path.Combine(Path.GetTempPath(), "MarbleAssetBundlePackagesTests")
            };
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var gameObject in _objects)
            {
                Object.DestroyImmediate(gameObject);
            }
            _objects.Clear();
        }

        [Test]
        public void Validate_RejectsNonPersistentObjectAndMissingScript()
        {
            var prefab = CreateCandidate("MuseumChair");
            _request.Prefabs.Add(prefab);
            _environment.SetInspection(prefab, new AssetBundlePrefabInspection
            {
                Prefab = prefab,
                PrefabName = "MuseumChair",
                IsPersistentPrefab = false,
                MissingScriptCount = 1,
                HasRenderer = true
            });

            var result = _runner.Validate(_request);

            Assert.That(result.HasErrors, Is.True);
            Assert.That(result.Issues.Select(issue => issue.Code), Does.Contain("prefab_asset_invalid"));
            Assert.That(result.Issues.Select(issue => issue.Code), Does.Contain("prefab_missing_script"));
        }

        [Test]
        public void Validate_RejectsDuplicatePrefabNamesAndBundleDependencies()
        {
            var first = CreateCandidate("First");
            var second = CreateCandidate("Second");
            _request.Prefabs.Add(first);
            _request.Prefabs.Add(second);
            _environment.SetInspection(first, ValidInspection(first, "MuseumChair"));
            var secondInspection = ValidInspection(second, "MuseumChair");
            secondInspection.ConflictingBundleAssets = new[] { "Assets/Shared/Material.mat" };
            _environment.SetInspection(second, secondInspection);

            var result = _runner.Validate(_request);

            Assert.That(result.Issues.Select(issue => issue.Code), Does.Contain("prefab_name_duplicate"));
            Assert.That(result.Issues.Select(issue => issue.Code), Does.Contain("asset_bundle_dependency_conflict"));
        }

        [Test]
        public void Validate_ReportsVisualAndStrippingRisksAsWarnings()
        {
            var prefab = CreateCandidate("MuseumChair");
            _request.Prefabs.Add(prefab);
            var inspection = ValidInspection(prefab, "MuseumChair");
            inspection.HasRenderer = false;
            inspection.HasOnlyDeepRenderers = true;
            inspection.UnsupportedShaders = new[] { "Hidden/Unsupported" };
            inspection.CustomMonoBehaviours = new[] { "Marble.ContentBehaviour" };
            inspection.EstimatedDependencyBytes = AssetBundlePackagePreflightRunner.LargeAssetWarningBytes + 1;
            _environment.SetInspection(prefab, inspection);

            var result = _runner.Validate(_request);

            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Issues, Has.All.Property("Severity").EqualTo(AssetBundlePackageIssueSeverity.Warning));
            Assert.That(result.Issues.Select(issue => issue.Code), Does.Contain("prefab_renderer_missing"));
            Assert.That(result.Issues.Select(issue => issue.Code), Does.Contain("prefab_renderer_deep"));
            Assert.That(result.Issues.Select(issue => issue.Code), Does.Contain("shader_unsupported"));
            Assert.That(result.Issues.Select(issue => issue.Code), Does.Contain("mono_behaviour_stripping_risk"));
            Assert.That(result.Issues.Select(issue => issue.Code), Does.Contain("prefab_dependency_size_large"));
        }

        [Test]
        public void Validate_RejectsMissingPlatformBuildSupport()
        {
            var prefab = CreateCandidate("MuseumChair");
            _request.Prefabs.Add(prefab);
            _environment.SetInspection(prefab, ValidInspection(prefab, "MuseumChair"));
            _environment.UnsupportedTargets.Add(BuildTarget.iOS);
            _environment.UnsupportedTargets.Add(BuildTarget.Android);

            var result = _runner.Validate(_request);

            Assert.That(
                result.Issues.Count(issue => issue.Code == "build_target_unsupported"),
                Is.EqualTo(2)
            );
        }

        [Test]
        public void Validate_RejectsFilesystemRootOutputDirectory()
        {
            var prefab = CreateCandidate("MuseumChair");
            _request.Prefabs.Add(prefab);
            _environment.SetInspection(prefab, ValidInspection(prefab, "MuseumChair"));
            _request.OutputDirectory = Path.GetPathRoot(_environment.ProjectRoot);

            var result = _runner.Validate(_request);

            Assert.That(result.Issues.Select(issue => issue.Code), Does.Contain("output_directory_unsafe"));
        }

        [Test]
        public void Validate_RejectsExistingFileAsOutputDirectory()
        {
            var prefab = CreateCandidate("MuseumChair");
            _request.Prefabs.Add(prefab);
            _environment.SetInspection(prefab, ValidInspection(prefab, "MuseumChair"));
            var existingFile = Path.GetTempFileName();

            try
            {
                _request.OutputDirectory = existingFile;

                var result = _runner.Validate(_request);

                Assert.That(result.HasErrors, Is.True);
                Assert.That(
                    result.Issues.Select(issue => issue.Code),
                    Does.Contain("output_directory_is_file")
                );
            }
            finally
            {
                File.Delete(existingFile);
            }
        }

        [Test]
        public void Validate_AcceptsValidPrefabsAndProducesDefinitions()
        {
            var prefab = CreateCandidate("MuseumChair");
            _request.Prefabs.Add(prefab);
            _environment.SetInspection(prefab, ValidInspection(prefab, "MuseumChair"));

            var result = _runner.Validate(_request);

            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Inspections.Single().AssetPath, Is.EqualTo("Assets/Content/MuseumChair.prefab"));
            Assert.That(result.Definition.Prefabs.Single().Name, Is.EqualTo("MuseumChair"));
        }

        private GameObject CreateCandidate(string name)
        {
            var gameObject = new GameObject(name);
            _objects.Add(gameObject);
            return gameObject;
        }

        private static AssetBundlePrefabInspection ValidInspection(GameObject prefab, string prefabName)
        {
            return new AssetBundlePrefabInspection
            {
                Prefab = prefab,
                PrefabName = prefabName,
                AssetPath = $"Assets/Content/{prefabName}.prefab",
                IsPersistentPrefab = true,
                HasRenderer = true
            };
        }

        private sealed class FakePreflightEnvironment : IAssetBundlePackagePreflightEnvironment
        {
            private readonly Dictionary<GameObject, AssetBundlePrefabInspection> _inspections = new();

            internal HashSet<BuildTarget> UnsupportedTargets { get; } = new();

            public string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            public bool IsBuildTargetSupported(BuildTarget target)
            {
                return !UnsupportedTargets.Contains(target);
            }

            public AssetBundlePrefabInspection Inspect(GameObject prefab)
            {
                return _inspections[prefab];
            }

            internal void SetInspection(GameObject prefab, AssetBundlePrefabInspection inspection)
            {
                _inspections[prefab] = inspection;
            }
        }
    }
}
