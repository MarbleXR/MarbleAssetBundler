using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Marble.AssetBundleBuilder.Editor
{
    internal sealed class AssetBundlePackageBuildRequest
    {
        public string BundleName { get; set; }
        public string DisplayName { get; set; }
        public string ContentVersion { get; set; }
        public string OutputDirectory { get; set; }
        public List<GameObject> Prefabs { get; } = new();
    }

    internal sealed class AssetBundlePrefabInspection
    {
        public GameObject Prefab { get; set; }
        public string PrefabName { get; set; }
        public string AssetPath { get; set; }
        public bool IsPersistentPrefab { get; set; }
        public int MissingScriptCount { get; set; }
        public string[] ConflictingBundleAssets { get; set; } = Array.Empty<string>();
        public bool HasRenderer { get; set; }
        public bool HasOnlyDeepRenderers { get; set; }
        public string[] UnsupportedShaders { get; set; } = Array.Empty<string>();
        public string[] CustomMonoBehaviours { get; set; } = Array.Empty<string>();
        public long EstimatedDependencyBytes { get; set; }
    }

    internal sealed class AssetBundlePackagePreflightResult
    {
        public AssetBundlePackagePreflightResult(
            AssetBundlePackageDefinition definition,
            IReadOnlyList<AssetBundlePrefabInspection> inspections,
            IReadOnlyList<AssetBundlePackageIssue> issues
        )
        {
            Definition = definition;
            Inspections = inspections;
            Issues = issues;
        }

        public AssetBundlePackageDefinition Definition { get; }
        public IReadOnlyList<AssetBundlePrefabInspection> Inspections { get; }
        public IReadOnlyList<AssetBundlePackageIssue> Issues { get; }
        public bool HasErrors => Issues.Any(issue => issue.Severity == AssetBundlePackageIssueSeverity.Error);
    }

    internal interface IAssetBundlePackagePreflightRunner
    {
        AssetBundlePackagePreflightResult Validate(AssetBundlePackageBuildRequest request);
    }

    internal interface IAssetBundlePackagePreflightEnvironment
    {
        string ProjectRoot { get; }
        bool IsBuildTargetSupported(BuildTarget target);
        AssetBundlePrefabInspection Inspect(GameObject prefab);
    }

    internal sealed class AssetBundlePackagePreflightRunner : IAssetBundlePackagePreflightRunner
    {
        internal const long LargeAssetWarningBytes = 100L * 1024L * 1024L;

        private readonly IAssetBundlePackagePreflightEnvironment _environment;
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly Func<string> _unityVersion;

        internal AssetBundlePackagePreflightRunner()
            : this(
                new UnityAssetBundlePackagePreflightEnvironment(),
                () => DateTimeOffset.UtcNow,
                () => Application.unityVersion
            )
        {
        }

        internal AssetBundlePackagePreflightRunner(
            IAssetBundlePackagePreflightEnvironment environment,
            Func<DateTimeOffset> utcNow = null,
            Func<string> unityVersion = null
        )
        {
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            _unityVersion = unityVersion ?? (() => Application.unityVersion);
        }

        public AssetBundlePackagePreflightResult Validate(AssetBundlePackageBuildRequest request)
        {
            var issues = new List<AssetBundlePackageIssue>();
            var inspections = new List<AssetBundlePrefabInspection>();
            var definition = CreateDefinition(request);

            if (request == null)
            {
                issues.Add(new AssetBundlePackageIssue("request_required", "Package build request is required."));
                return new AssetBundlePackagePreflightResult(definition, inspections, issues);
            }

            foreach (var prefab in request.Prefabs)
            {
                if (prefab == null)
                {
                    issues.Add(new AssetBundlePackageIssue("prefab_asset_invalid", "A selected Prefab is missing."));
                    continue;
                }

                AssetBundlePrefabInspection inspection;
                try
                {
                    inspection = _environment.Inspect(prefab);
                }
                catch (Exception exception)
                {
                    issues.Add(new AssetBundlePackageIssue(
                        "prefab_inspection_failed",
                        $"Failed to inspect '{prefab.name}': {exception.Message}"
                    ));
                    continue;
                }

                inspections.Add(inspection);
                definition.Prefabs.Add(new AssetBundlePackagePrefabDefinition
                {
                    Name = inspection.PrefabName,
                    AssetPath = inspection.AssetPath
                });
                AddInspectionIssues(inspection, issues);
            }

            issues.AddRange(AssetBundlePackageContract.ValidateDefinition(definition));
            AddOutputIssues(request, issues);
            AddBuildSupportIssues(issues);
            return new AssetBundlePackagePreflightResult(definition, inspections, issues);
        }

        private AssetBundlePackageDefinition CreateDefinition(AssetBundlePackageBuildRequest request)
        {
            return new AssetBundlePackageDefinition
            {
                BundleName = request?.BundleName,
                DisplayName = request?.DisplayName,
                ContentVersion = request?.ContentVersion,
                UnityVersion = _unityVersion(),
                GeneratedAtUtc = _utcNow().ToUniversalTime()
            };
        }

        private static void AddInspectionIssues(
            AssetBundlePrefabInspection inspection,
            ICollection<AssetBundlePackageIssue> issues
        )
        {
            if (!inspection.IsPersistentPrefab)
            {
                issues.Add(new AssetBundlePackageIssue(
                    "prefab_asset_invalid",
                    $"'{inspection.PrefabName}' must be a persistent Prefab Asset."
                ));
            }

            if (inspection.MissingScriptCount > 0)
            {
                issues.Add(new AssetBundlePackageIssue(
                    "prefab_missing_script",
                    $"'{inspection.PrefabName}' contains {inspection.MissingScriptCount} Missing Script reference(s)."
                ));
            }

            if (inspection.ConflictingBundleAssets?.Length > 0)
            {
                issues.Add(new AssetBundlePackageIssue(
                    "asset_bundle_dependency_conflict",
                    $"'{inspection.PrefabName}' depends on Assets that already have an AssetBundle label: "
                    + string.Join(", ", inspection.ConflictingBundleAssets)
                ));
            }

            if (!inspection.HasRenderer)
            {
                issues.Add(Warning(
                    "prefab_renderer_missing",
                    $"'{inspection.PrefabName}' has no Renderer. Confirm that it is intended to be invisible."
                ));
            }

            if (inspection.HasOnlyDeepRenderers)
            {
                issues.Add(Warning(
                    "prefab_renderer_deep",
                    $"'{inspection.PrefabName}' only has Renderers at grandchild depth or deeper. Confirm selection and Collider behavior."
                ));
            }

            if (inspection.UnsupportedShaders?.Length > 0)
            {
                issues.Add(Warning(
                    "shader_unsupported",
                    $"'{inspection.PrefabName}' references unsupported Shader(s): "
                    + string.Join(", ", inspection.UnsupportedShaders)
                ));
            }

            if (inspection.CustomMonoBehaviours?.Length > 0)
            {
                issues.Add(Warning(
                    "mono_behaviour_stripping_risk",
                    $"'{inspection.PrefabName}' uses custom MonoBehaviour(s). Confirm app inclusion and IL2CPP stripping: "
                    + string.Join(", ", inspection.CustomMonoBehaviours)
                ));
            }

            if (inspection.EstimatedDependencyBytes > LargeAssetWarningBytes)
            {
                issues.Add(Warning(
                    "prefab_dependency_size_large",
                    $"'{inspection.PrefabName}' dependencies exceed {LargeAssetWarningBytes / (1024 * 1024)} MB before Bundle compression."
                ));
            }
        }

        private void AddOutputIssues(
            AssetBundlePackageBuildRequest request,
            ICollection<AssetBundlePackageIssue> issues
        )
        {
            if (string.IsNullOrWhiteSpace(request.OutputDirectory))
            {
                issues.Add(new AssetBundlePackageIssue("output_directory_required", "Output Directory is required."));
                return;
            }

            try
            {
                var outputPath = Path.GetFullPath(request.OutputDirectory);
                if (File.Exists(outputPath))
                {
                    issues.Add(new AssetBundlePackageIssue(
                        "output_directory_is_file",
                        $"Output Directory points to an existing file: {outputPath}"
                    ));
                    return;
                }

                var projectRoot = Path.GetFullPath(_environment.ProjectRoot);
                var assetsRoot = Path.Combine(projectRoot, "Assets");
                var filesystemRoot = Path.GetPathRoot(outputPath);
                if ((!string.IsNullOrEmpty(filesystemRoot) && PathsEqual(outputPath, filesystemRoot))
                    || PathsEqual(outputPath, projectRoot)
                    || IsSameOrChildPath(outputPath, assetsRoot))
                {
                    issues.Add(new AssetBundlePackageIssue(
                        "output_directory_unsafe",
                        "Output Directory cannot be a filesystem root, the project root or inside Assets."
                    ));
                }

                if (AssetBundlePackageContract.IsBundleNameValid(request.BundleName)
                    && AssetBundlePackageContract.IsContentVersionValid(request.ContentVersion))
                {
                    var outputFile = Path.Combine(
                        outputPath,
                        AssetBundlePackageContract.GetPackageFileName(
                            request.BundleName,
                            request.ContentVersion
                        )
                    );
                    if (File.Exists(outputFile))
                    {
                        issues.Add(new AssetBundlePackageIssue(
                            "output_file_exists",
                            $"Output file already exists: {outputFile}"
                        ));
                    }
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is NotSupportedException
                || exception is PathTooLongException
            )
            {
                issues.Add(new AssetBundlePackageIssue(
                    "output_directory_invalid",
                    $"Output Directory is invalid: {exception.Message}"
                ));
            }
        }

        private void AddBuildSupportIssues(ICollection<AssetBundlePackageIssue> issues)
        {
            foreach (var target in new[] { BuildTarget.iOS, BuildTarget.Android })
            {
                if (!_environment.IsBuildTargetSupported(target))
                {
                    issues.Add(new AssetBundlePackageIssue(
                        "build_target_unsupported",
                        $"Build Support is not installed for {target}."
                    ));
                }
            }
        }

        private static AssetBundlePackageIssue Warning(string code, string message)
        {
            return new AssetBundlePackageIssue(code, message, AssetBundlePackageIssueSeverity.Warning);
        }

        private static bool PathsEqual(string first, string second)
        {
            return string.Equals(
                first.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                second.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static bool IsSameOrChildPath(string candidate, string parent)
        {
            if (PathsEqual(candidate, parent))
            {
                return true;
            }

            var normalizedParent = parent.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            ) + Path.DirectorySeparatorChar;
            return candidate.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class UnityAssetBundlePackagePreflightEnvironment
        : IAssetBundlePackagePreflightEnvironment
    {
        public string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        public bool IsBuildTargetSupported(BuildTarget target)
        {
            return BuildPipeline.IsBuildTargetSupported(
                BuildPipeline.GetBuildTargetGroup(target),
                target
            );
        }

        public AssetBundlePrefabInspection Inspect(GameObject prefab)
        {
            var assetPath = AssetDatabase.GetAssetPath(prefab);
            var isPersistentPrefab = !string.IsNullOrEmpty(assetPath)
                                     && EditorUtility.IsPersistent(prefab)
                                     && AssetDatabase.LoadMainAssetAtPath(assetPath) == prefab
                                     && PrefabUtility.GetPrefabAssetType(prefab) != PrefabAssetType.NotAPrefab;
            if (string.IsNullOrEmpty(assetPath))
            {
                assetPath = string.Empty;
            }

            var transforms = prefab.GetComponentsInChildren<Transform>(true);
            var missingScriptCount = transforms.Sum(transform =>
                GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject)
            );
            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            var unsupportedShaders = renderers
                .SelectMany(renderer => renderer.sharedMaterials ?? Array.Empty<Material>())
                .Where(material => material != null)
                .Select(material => material.shader)
                .Where(shader => shader == null || !shader.isSupported)
                .Select(shader => shader == null ? "(missing shader)" : shader.name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var customMonoBehaviours = prefab
                .GetComponentsInChildren<MonoBehaviour>(true)
                .Where(component => component != null)
                .Select(component => component.GetType().FullName)
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var dependencies = string.IsNullOrEmpty(assetPath)
                ? Array.Empty<string>()
                : AssetDatabase.GetDependencies(assetPath, true);
            var conflictingBundleAssets = dependencies
                .Where(dependencyPath =>
                {
                    var importer = AssetImporter.GetAtPath(dependencyPath);
                    return importer != null && !string.IsNullOrEmpty(importer.assetBundleName);
                })
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            return new AssetBundlePrefabInspection
            {
                Prefab = prefab,
                PrefabName = prefab.name,
                AssetPath = assetPath,
                IsPersistentPrefab = isPersistentPrefab,
                MissingScriptCount = missingScriptCount,
                ConflictingBundleAssets = conflictingBundleAssets,
                HasRenderer = renderers.Length > 0,
                HasOnlyDeepRenderers = renderers.Length > 0
                                       && renderers.All(renderer => GetDepth(prefab.transform, renderer.transform) >= 2),
                UnsupportedShaders = unsupportedShaders,
                CustomMonoBehaviours = customMonoBehaviours,
                EstimatedDependencyBytes = EstimateDependencyBytes(dependencies)
            };
        }

        private long EstimateDependencyBytes(IEnumerable<string> dependencies)
        {
            long total = 0;
            foreach (var assetPath in dependencies)
            {
                var absolutePath = Path.GetFullPath(Path.Combine(ProjectRoot, assetPath));
                if (File.Exists(absolutePath))
                {
                    total += new FileInfo(absolutePath).Length;
                }
            }
            return total;
        }

        private static int GetDepth(Transform root, Transform current)
        {
            var depth = 0;
            while (current != null && current != root)
            {
                depth++;
                current = current.parent;
            }
            return depth;
        }
    }
}
