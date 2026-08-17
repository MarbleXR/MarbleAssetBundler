using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Marble.AssetBundleBuilder.Editor
{
    internal static class AssetBundleBuilderUnityPackageExporter
    {
        private const string ImportRootAssetPath = "Assets/MarbleAssetBundleBuilder";

        [MenuItem("Tools/Marble/Export AssetBundle Builder .unitypackage")]
        private static void Export()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(AssetBundleBuilderUnityPackageExporter).Assembly
            );
            if (package == null)
            {
                Debug.LogError("Could not locate the Marble AssetBundle Builder package.");
                return;
            }

            var outputPath = EditorUtility.SaveFilePanel(
                "Export Marble AssetBundle Builder",
                Path.GetFullPath("."),
                $"MarbleAssetBundleBuilder-{package.version}",
                "unitypackage"
            );
            if (string.IsNullOrEmpty(outputPath))
            {
                return;
            }

            outputPath = Path.ChangeExtension(outputPath, "unitypackage");
            var importRootPath = Path.GetFullPath(ImportRootAssetPath);
            if (Directory.Exists(importRootPath))
            {
                Debug.LogError(
                    $"Cannot export because '{ImportRootAssetPath}' already exists. "
                    + "Move or remove it, then run the export again."
                );
                return;
            }

            try
            {
                CopyPackageFiles(package.resolvedPath, importRootPath);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ExportPackage(
                    ImportRootAssetPath,
                    outputPath,
                    ExportPackageOptions.Recurse
                );
                Debug.Log($"Exported Marble AssetBundle Builder: {outputPath}");
                EditorUtility.RevealInFinder(outputPath);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Export failed",
                    $"Could not create the Unity package.\n\n{exception.Message}",
                    "OK"
                );
            }
            finally
            {
                if (Directory.Exists(importRootPath))
                {
                    AssetDatabase.DeleteAsset(ImportRootAssetPath);
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }
            }
        }

        private static void CopyPackageFiles(string packageRootPath, string importRootPath)
        {
            var sourceEditorPath = Path.Combine(packageRootPath, "Editor");
            var destinationEditorPath = Path.Combine(importRootPath, "Editor");
            Directory.CreateDirectory(destinationEditorPath);

            foreach (var sourcePath in Directory.GetFiles(
                         sourceEditorPath,
                         "*.cs",
                         SearchOption.AllDirectories
                     ))
            {
                if (string.Equals(
                        Path.GetFileName(sourcePath),
                        nameof(AssetBundleBuilderUnityPackageExporter) + ".cs",
                        StringComparison.Ordinal
                    ))
                {
                    continue;
                }

                var relativePath = Path.GetRelativePath(sourceEditorPath, sourcePath);
                var destinationPath = Path.Combine(destinationEditorPath, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath);
            }

            File.Copy(
                Path.Combine(packageRootPath, "README.md"),
                Path.Combine(importRootPath, "README.md")
            );
        }
    }
}
