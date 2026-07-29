using UnityEditor;
using UnityEngine;
using System.IO;

public class AssetBundleBuilder
{
    [MenuItem("Assets/Build AssetBundle")]
    static void BuildAllAssetBundles()
    {
        string path = "Assets/StreamingAssets/AssetBundles";
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);

        var iOSPath = path + "/iOS";
        if (!Directory.Exists(iOSPath))
            Directory.CreateDirectory(iOSPath);

        BuildPipeline.BuildAssetBundles(iOSPath, BuildAssetBundleOptions.None, BuildTarget.iOS);

        var androidPath = path + "/Android";
        if (!Directory.Exists(androidPath))
            Directory.CreateDirectory(androidPath);

        BuildPipeline.BuildAssetBundles(
            androidPath,
            BuildAssetBundleOptions.None,
            BuildTarget.Android
        );
    }
}
