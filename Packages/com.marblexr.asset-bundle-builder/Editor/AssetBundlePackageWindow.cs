using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Marble.AssetBundleBuilder.Editor
{
    internal sealed class AssetBundlePackageWindow : EditorWindow
    {
        [SerializeField] private string _bundleName = string.Empty;
        [SerializeField] private string _displayName = string.Empty;
        [SerializeField] private string _contentVersion = string.Empty;
        [SerializeField] private string _outputDirectory = string.Empty;
        [SerializeField] private List<GameObject> _prefabs = new();

        private AssetBundlePackageCoordinator _coordinator;
        private AssetBundlePackagePreflightResult _validationResult;
        private AssetBundlePackageBuildResult _buildResult;
        private AssetBundlePackageProgress _progress;
        private Vector2 _scrollPosition;
        private bool _isBuilding;

        [MenuItem("Tools/Marble/AssetBundle Package Builder")]
        private static void Open()
        {
            var window = GetWindow<AssetBundlePackageWindow>();
            window.titleContent = new GUIContent("AssetBundle Package");
            window.minSize = new Vector2(520f, 520f);
            window.Show();
        }

        private void OnEnable()
        {
            _coordinator = new AssetBundlePackageCoordinator();
            _prefabs ??= new List<GameObject>();
            if (string.IsNullOrWhiteSpace(_outputDirectory))
            {
                _outputDirectory = Path.GetFullPath("AssetBundlePackages");
            }
        }

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            EditorGUILayout.LabelField("AssetBundle Package Builder", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Builds one AssetBundle for iOS and Android, then creates a Package Schema v1 ZIP for manual Backend upload. It does not modify AssetBundle labels or upload automatically.",
                MessageType.Info
            );

            using (new EditorGUI.DisabledScope(_isBuilding))
            {
                DrawMetadataFields();
                EditorGUILayout.Space();
                DrawPrefabList();
                EditorGUILayout.Space();
                DrawOutputDirectory();
                EditorGUILayout.Space();
                DrawActions();
            }

            DrawProgress();
            DrawIssues();
            DrawBuildResult();
            EditorGUILayout.EndScrollView();
        }

        private void DrawMetadataFields()
        {
            EditorGUILayout.LabelField("Package", EditorStyles.boldLabel);
            _bundleName = EditorGUILayout.TextField("Bundle Name", _bundleName);
            _displayName = EditorGUILayout.TextField("Display Name", _displayName);
            _contentVersion = EditorGUILayout.TextField("Content Version", _contentVersion);
        }

        private void DrawPrefabList()
        {
            EditorGUILayout.LabelField("Prefabs", EditorStyles.boldLabel);
            for (var index = 0; index < _prefabs.Count; index++)
            {
                EditorGUILayout.BeginHorizontal();
                _prefabs[index] = (GameObject)EditorGUILayout.ObjectField(
                    $"Prefab {index + 1}",
                    _prefabs[index],
                    typeof(GameObject),
                    false
                );
                if (GUILayout.Button("Remove", GUILayout.Width(70f)))
                {
                    _prefabs.RemoveAt(index);
                    index--;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Add Prefab"))
            {
                _prefabs.Add(null);
            }
        }

        private void DrawOutputDirectory()
        {
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _outputDirectory = EditorGUILayout.TextField("Directory", _outputDirectory);
            if (GUILayout.Button("Browse", GUILayout.Width(70f)))
            {
                var selectedDirectory = EditorUtility.OpenFolderPanel(
                    "Select AssetBundle Package Output Directory",
                    ResolveExistingDirectory(_outputDirectory),
                    string.Empty
                );
                if (!string.IsNullOrEmpty(selectedDirectory))
                {
                    _outputDirectory = selectedDirectory;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawActions()
        {
            var editorUnavailable = EditorApplication.isCompiling
                                    || EditorApplication.isPlayingOrWillChangePlaymode;
            using (new EditorGUI.DisabledScope(editorUnavailable))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Validate"))
                {
                    ValidateRequest();
                }
                if (GUILayout.Button("Build Package"))
                {
                    BuildPackage();
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_outputDirectory)))
            {
                if (GUILayout.Button("Open Output Folder"))
                {
                    RevealOutput();
                }
            }
            using (new EditorGUI.DisabledScope(_prefabs.All(prefab => prefab == null)))
            {
                if (GUILayout.Button("Copy Prefab Names"))
                {
                    EditorGUIUtility.systemCopyBuffer = string.Join(
                        Environment.NewLine,
                        _prefabs.Where(prefab => prefab != null).Select(prefab => prefab.name)
                    );
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void ValidateRequest()
        {
            _buildResult = null;
            _progress = null;
            _validationResult = _coordinator.Validate(CreateRequest());
            Repaint();
        }

        private void BuildPackage()
        {
            _validationResult = null;
            _buildResult = null;
            _progress = null;
            _isBuilding = true;
            var cancellationRequested = false;
            try
            {
                _buildResult = _coordinator.Build(
                    CreateRequest(),
                    progress =>
                    {
                        _progress = progress;
                        cancellationRequested |= EditorUtility.DisplayCancelableProgressBar(
                            "AssetBundle Package Builder",
                            progress.Message,
                            progress.NormalizedProgress
                        );
                        Repaint();
                    },
                    () => cancellationRequested
                );
            }
            finally
            {
                _isBuilding = false;
                EditorUtility.ClearProgressBar();
                Repaint();
            }
        }

        private AssetBundlePackageBuildRequest CreateRequest()
        {
            var request = new AssetBundlePackageBuildRequest
            {
                BundleName = _bundleName?.Trim(),
                DisplayName = _displayName?.Trim(),
                ContentVersion = _contentVersion?.Trim(),
                OutputDirectory = _outputDirectory?.Trim()
            };
            request.Prefabs.AddRange(_prefabs);
            return request;
        }

        private void DrawProgress()
        {
            if (_progress == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Progress", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(_progress.Phase.ToString(), _progress.Message);
        }

        private void DrawIssues()
        {
            var issues = _validationResult?.Issues ?? _buildResult?.Issues;
            if (issues == null || issues.Count == 0)
            {
                if (_validationResult != null)
                {
                    EditorGUILayout.HelpBox("Validation passed.", MessageType.Info);
                }
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);
            foreach (var issue in issues)
            {
                EditorGUILayout.HelpBox(
                    $"[{issue.Code}] {issue.Message}",
                    issue.Severity == AssetBundlePackageIssueSeverity.Error
                        ? MessageType.Error
                        : MessageType.Warning
                );
            }
        }

        private void DrawBuildResult()
        {
            if (_buildResult == null)
            {
                return;
            }

            EditorGUILayout.Space();
            if (_buildResult.Succeeded)
            {
                EditorGUILayout.HelpBox("AssetBundle package was generated successfully.", MessageType.Info);
                EditorGUILayout.SelectableLabel(
                    _buildResult.PackagePath,
                    EditorStyles.textField,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight)
                );
                EditorGUILayout.LabelField("Prefab Names", EditorStyles.boldLabel);
                foreach (var prefab in _buildResult.Manifest.Prefabs)
                {
                    EditorGUILayout.SelectableLabel(
                        prefab.Name,
                        EditorStyles.textField,
                        GUILayout.Height(EditorGUIUtility.singleLineHeight)
                    );
                }
            }
            else if (_buildResult.Cancelled)
            {
                EditorGUILayout.HelpBox("AssetBundle package build was cancelled.", MessageType.Warning);
            }
        }

        private static string ResolveExistingDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    var absolutePath = Path.GetFullPath(path);
                    if (Directory.Exists(absolutePath))
                    {
                        return absolutePath;
                    }
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is NotSupportedException
                || exception is PathTooLongException
            )
            {
                // Fall back to the project root for the folder picker.
            }
            return Path.GetFullPath(".");
        }

        private void RevealOutput()
        {
            try
            {
                var path = _buildResult?.PackagePath ?? Path.GetFullPath(_outputDirectory);
                if (_buildResult == null && !Directory.Exists(path))
                {
                    ShowNotification(new GUIContent("Output Directory does not exist yet."));
                    return;
                }
                EditorUtility.RevealInFinder(path);
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is NotSupportedException
                || exception is PathTooLongException
            )
            {
                ShowNotification(new GUIContent($"Invalid output path: {exception.Message}"));
            }
        }
    }
}
