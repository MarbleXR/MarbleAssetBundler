using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Marble.AssetBundleBuilder.Editor;
using NUnit.Framework;

namespace Marble.Tests.Editor
{
    [TestFixture]
    public sealed class AssetBundlePackageArchiveTests
    {
        private string _tempRoot;
        private string _outputDirectory;
        private AssetBundlePackageDefinition _definition;
        private AssetBundlePlatformArtifact[] _artifacts;
        private AssetBundlePackageArchive _archive;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "MarblePackageArchiveTests", Guid.NewGuid().ToString("N"));
            _outputDirectory = Path.Combine(_tempRoot, "output");
            Directory.CreateDirectory(_tempRoot);
            _definition = AssetBundlePackageContractTests.CreateValidDefinition();
            _artifacts = CreateArtifacts(_tempRoot);
            _archive = new AssetBundlePackageArchive();
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
        public void Create_WritesOnlyPackageJsonBundlesAndUnityManifests()
        {
            var result = _archive.Create(_definition, _artifacts, _outputDirectory);

            Assert.That(File.Exists(result.PackagePath), Is.True);
            using var stream = File.OpenRead(result.PackagePath);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            Assert.That(
                zip.Entries.Select(entry => entry.FullName).OrderBy(name => name),
                Is.EqualTo(new[]
                {
                    "bundles/android/museum_chair",
                    "bundles/android/museum_chair.manifest",
                    "bundles/ios/museum_chair",
                    "bundles/ios/museum_chair.manifest",
                    "package.json"
                })
            );
            Assert.That(result.Manifest.Platforms.Ios.Size, Is.EqualTo(new FileInfo(_artifacts[0].BundlePath).Length));
            Assert.That(
                result.Manifest.Platforms.Android.Sha256,
                Is.EqualTo(AssetBundlePackageArchive.ComputeSha256(_artifacts[1].BundlePath))
            );
            Assert.That(
                result.Manifest.Platforms.Ios.ManifestSha256,
                Is.EqualTo(AssetBundlePackageArchive.ComputeSha256(_artifacts[0].ManifestPath))
            );
        }

        [Test]
        public void Validate_RoundTripsManifestAndChecksums()
        {
            var packagePath = _archive.Create(_definition, _artifacts, _outputDirectory).PackagePath;

            var manifest = _archive.Validate(packagePath);

            Assert.That(manifest.SchemaVersion, Is.EqualTo(1));
            Assert.That(manifest.Prefabs.Single().Name, Is.EqualTo("MuseumChair"));
            Assert.That(manifest.Platforms.Ios.Dependencies, Is.Empty);
            Assert.That(manifest.Platforms.Android.Dependencies, Is.Empty);
        }

        [Test]
        public void Create_DoesNotOverwriteExistingPackage()
        {
            _archive.Create(_definition, _artifacts, _outputDirectory);

            var exception = Assert.Throws<IOException>(() =>
                _archive.Create(_definition, _artifacts, _outputDirectory)
            );

            Assert.That(exception.Message, Does.Contain("already exists"));
        }

        [Test]
        public void Validate_RejectsEntriesOutsideAllowlist()
        {
            var packagePath = _archive.Create(_definition, _artifacts, _outputDirectory).PackagePath;
            using (var stream = new FileStream(packagePath, FileMode.Open, FileAccess.ReadWrite))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Update))
            {
                zip.CreateEntry("unexpected.txt");
            }

            var exception = Assert.Throws<InvalidDataException>(() => _archive.Validate(packagePath));

            Assert.That(exception.Message, Does.Contain("entries"));
        }

        [Test]
        public void Validate_RejectsBundleWhoseChecksumChanged()
        {
            var packagePath = _archive.Create(_definition, _artifacts, _outputDirectory).PackagePath;
            using (var stream = new FileStream(packagePath, FileMode.Open, FileAccess.ReadWrite))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Update))
            {
                var entry = zip.GetEntry("bundles/ios/museum_chair");
                entry.Delete();
                var replacement = zip.CreateEntry("bundles/ios/museum_chair");
                using var replacementStream = replacement.Open();
                replacementStream.Write(new byte[] { 5, 4, 3, 2, 1 }, 0, 5);
            }

            var exception = Assert.Throws<InvalidDataException>(() => _archive.Validate(packagePath));

            Assert.That(exception.Message, Does.Contain("SHA-256"));
        }

        [Test]
        public void Validate_RejectsUnityManifestWhoseChecksumChanged()
        {
            var packagePath = _archive.Create(_definition, _artifacts, _outputDirectory).PackagePath;
            using (var stream = new FileStream(packagePath, FileMode.Open, FileAccess.ReadWrite))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Update))
            {
                var entry = zip.GetEntry("bundles/android/museum_chair.manifest");
                entry.Delete();
                var replacement = zip.CreateEntry("bundles/android/museum_chair.manifest");
                using var replacementStream = replacement.Open();
                var replacementBytes = new byte[(int)new FileInfo(_artifacts[1].ManifestPath).Length];
                replacementStream.Write(replacementBytes, 0, replacementBytes.Length);
            }

            var exception = Assert.Throws<InvalidDataException>(() => _archive.Validate(packagePath));

            Assert.That(exception.Message, Does.Contain("Unity manifest SHA-256"));
        }

        [Test]
        public void Create_CleansPartialFileWhenArtifactIsMissing()
        {
            File.Delete(_artifacts[1].BundlePath);

            Assert.Throws<FileNotFoundException>(() =>
                _archive.Create(_definition, _artifacts, _outputDirectory)
            );

            Assert.That(
                Directory.Exists(_outputDirectory)
                    ? Directory.GetFiles(_outputDirectory, "*.partial-*", SearchOption.TopDirectoryOnly)
                    : Array.Empty<string>(),
                Is.Empty
            );
        }

        private static AssetBundlePlatformArtifact[] CreateArtifacts(string root)
        {
            var iosPath = Path.Combine(root, "ios", "museum_chair");
            var androidPath = Path.Combine(root, "android", "museum_chair");
            Directory.CreateDirectory(Path.GetDirectoryName(iosPath));
            Directory.CreateDirectory(Path.GetDirectoryName(androidPath));
            File.WriteAllBytes(iosPath, new byte[] { 1, 2, 3, 4, 5 });
            File.WriteAllBytes(androidPath, new byte[] { 6, 7, 8, 9 });
            File.WriteAllText(iosPath + ".manifest", "ios manifest");
            File.WriteAllText(androidPath + ".manifest", "android manifest");
            return new[]
            {
                new AssetBundlePlatformArtifact
                {
                    PlatformKey = "ios",
                    BuildTarget = UnityEditor.BuildTarget.iOS,
                    BundleName = "museum_chair",
                    BundlePath = iosPath,
                    ManifestPath = iosPath + ".manifest"
                },
                new AssetBundlePlatformArtifact
                {
                    PlatformKey = "android",
                    BuildTarget = UnityEditor.BuildTarget.Android,
                    BundleName = "museum_chair",
                    BundlePath = androidPath,
                    ManifestPath = androidPath + ".manifest"
                }
            };
        }
    }
}
