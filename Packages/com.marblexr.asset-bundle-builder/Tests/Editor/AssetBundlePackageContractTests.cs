using System;
using System.Linq;
using Marble.AssetBundleBuilder.Editor;
using NUnit.Framework;

namespace Marble.Tests.Editor
{
    [TestFixture]
    public sealed class AssetBundlePackageContractTests
    {
        [Test]
        public void Json_RoundTripsSchemaV1Manifest()
        {
            var manifest = CreateValidManifest();

            var json = AssetBundlePackageManifestJson.Serialize(manifest);
            var restored = AssetBundlePackageManifestJson.Deserialize(json);

            Assert.That(json, Does.Contain("\"schemaVersion\": 1"));
            Assert.That(restored.SchemaVersion, Is.EqualTo(1));
            Assert.That(restored.BundleName, Is.EqualTo("museum_chair"));
            Assert.That(restored.Prefabs.Single().Name, Is.EqualTo("MuseumChair"));
            Assert.That(restored.Platforms.Ios.Path, Is.EqualTo("bundles/ios/museum_chair"));
            Assert.That(
                restored.Platforms.Ios.ManifestPath,
                Is.EqualTo("bundles/ios/museum_chair.manifest")
            );
            Assert.That(restored.Platforms.Android.BuildTarget, Is.EqualTo("Android"));
        }

        [TestCase("MuseumChair")]
        [TestCase("museum chair")]
        [TestCase("museum/chair")]
        [TestCase("")]
        public void ValidateDefinition_RejectsInvalidBundleName(string bundleName)
        {
            var definition = CreateValidDefinition();
            definition.BundleName = bundleName;

            var issues = AssetBundlePackageContract.ValidateDefinition(definition);

            Assert.That(issues.Select(issue => issue.Code), Does.Contain("bundle_name_invalid"));
        }

        [Test]
        public void ValidateDefinition_RejectsDuplicatePrefabNames()
        {
            var definition = CreateValidDefinition();
            definition.Prefabs.Add(new AssetBundlePackagePrefabDefinition
            {
                Name = "MuseumChair",
                AssetPath = "Assets/Content/OtherChair.prefab"
            });

            var issues = AssetBundlePackageContract.ValidateDefinition(definition);

            Assert.That(issues.Select(issue => issue.Code), Does.Contain("prefab_name_duplicate"));
        }

        [Test]
        public void ValidateDefinition_RejectsMissingMetadata()
        {
            var definition = CreateValidDefinition();
            definition.DisplayName = " ";
            definition.ContentVersion = null;
            definition.UnityVersion = string.Empty;

            var codes = AssetBundlePackageContract.ValidateDefinition(definition)
                .Select(issue => issue.Code)
                .ToArray();

            Assert.That(codes, Does.Contain("display_name_required"));
            Assert.That(codes, Does.Contain("content_version_invalid"));
            Assert.That(codes, Does.Contain("unity_version_required"));
        }

        [Test]
        public void ValidateManifest_RequiresBothPlatforms()
        {
            var manifest = CreateValidManifest();
            manifest.Platforms.Android = null;

            var issues = AssetBundlePackageContract.ValidateManifest(manifest);

            Assert.That(issues.Select(issue => issue.Code), Does.Contain("platform_android_required"));
        }

        [Test]
        public void ValidateManifest_RejectsUnexpectedDependenciesAndChecksum()
        {
            var manifest = CreateValidManifest();
            manifest.Platforms.Ios.Dependencies = new[] { "shared_materials" };
            manifest.Platforms.Android.Sha256 = "not-a-sha";

            var codes = AssetBundlePackageContract.ValidateManifest(manifest)
                .Select(issue => issue.Code)
                .ToArray();

            Assert.That(codes, Does.Contain("platform_dependencies_not_empty"));
            Assert.That(codes, Does.Contain("platform_sha256_invalid"));
        }

        [Test]
        public void ValidateManifest_RejectsInvalidUnityManifestMetadata()
        {
            var manifest = CreateValidManifest();
            manifest.Platforms.Ios.ManifestPath = "bundles/android/museum_chair.manifest";
            manifest.Platforms.Ios.ManifestSize = 0;
            manifest.Platforms.Android.ManifestSha256 = "not-a-sha";

            var codes = AssetBundlePackageContract.ValidateManifest(manifest)
                .Select(issue => issue.Code)
                .ToArray();

            Assert.That(codes, Does.Contain("platform_manifest_path_invalid"));
            Assert.That(codes, Does.Contain("platform_manifest_size_invalid"));
            Assert.That(codes, Does.Contain("platform_manifest_sha256_invalid"));
        }

        [Test]
        public void Paths_AreDerivedFromValidatedContractValues()
        {
            Assert.That(
                AssetBundlePackageContract.GetBundleEntryPath("ios", "museum_chair"),
                Is.EqualTo("bundles/ios/museum_chair")
            );
            Assert.That(
                AssetBundlePackageContract.GetBundleManifestEntryPath("ios", "museum_chair"),
                Is.EqualTo("bundles/ios/museum_chair.manifest")
            );
            Assert.That(
                AssetBundlePackageContract.GetPackageFileName("museum_chair", "2026.07.20.1"),
                Is.EqualTo("museum_chair-2026.07.20.1.zip")
            );
        }

        internal static AssetBundlePackageDefinition CreateValidDefinition()
        {
            var definition = new AssetBundlePackageDefinition
            {
                BundleName = "museum_chair",
                DisplayName = "Museum Chair",
                ContentVersion = "2026.07.20.1",
                UnityVersion = "6000.3.11f1",
                GeneratedAtUtc = new DateTimeOffset(2026, 7, 20, 5, 0, 0, TimeSpan.Zero)
            };
            definition.Prefabs.Add(new AssetBundlePackagePrefabDefinition
            {
                Name = "MuseumChair",
                AssetPath = "Assets/Content/MuseumChair.prefab"
            });
            return definition;
        }

        internal static AssetBundlePackageManifest CreateValidManifest()
        {
            return new AssetBundlePackageManifest
            {
                SchemaVersion = 1,
                BundleName = "museum_chair",
                DisplayName = "Museum Chair",
                ContentVersion = "2026.07.20.1",
                UnityVersion = "6000.3.11f1",
                GeneratedAtUtc = "2026-07-20T05:00:00.0000000+00:00",
                Prefabs = new[]
                {
                    new AssetBundlePackagePrefabManifest
                    {
                        Name = "MuseumChair",
                        AssetPath = "Assets/Content/MuseumChair.prefab"
                    }
                },
                Platforms = new AssetBundlePackagePlatformsManifest
                {
                    Ios = CreatePlatform("iOS", "bundles/ios/museum_chair", 'a'),
                    Android = CreatePlatform("Android", "bundles/android/museum_chair", 'b')
                }
            };
        }

        private static AssetBundlePackagePlatformManifest CreatePlatform(
            string buildTarget,
            string path,
            char hashCharacter
        )
        {
            return new AssetBundlePackagePlatformManifest
            {
                BuildTarget = buildTarget,
                Path = path,
                Size = 128,
                Sha256 = new string(hashCharacter, 64),
                ManifestPath = path + ".manifest",
                ManifestSize = 64,
                ManifestSha256 = new string(hashCharacter, 64),
                Dependencies = Array.Empty<string>()
            };
        }
    }
}
