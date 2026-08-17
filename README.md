# Marble AssetBundle

Marbleアプリで利用するPrefabを、iOS／Android向けのAssetBundleとしてビルドし、Backend管理画面へアップロードできるZIPパッケージを生成するUnityプロジェクトです。

## 動作条件

- Unity `6000.3.11f1`
- iOS Build Support
- Android Build Support

## はじめ方

1. Unity HubからこのプロジェクトをUnity `6000.3.11f1`で開きます。
2. `Assets/Prefabs/`にパッケージ化するPrefabを配置します。
3. Unity Editorで`Tools > Marble > AssetBundle Package Builder`を開きます。
4. Bundle Name、Display Name、Content Version、Prefabを入力します。
5. `Validate`で内容を確認し、`Build Package`を実行します。
6. 生成されたZIPをBackend管理画面へ手動アップロードします。

出力先はプロジェクト直下の`AssetBundlePackages/`です。ZIPにはメタデータ、iOS／Android向けAssetBundle、および各BundleのUnity manifestが含まれます。

## プロジェクト構成

| パス | 内容 |
| --- | --- |
| `Assets/Scripts/` | Gitで共有するプロジェクト固有のC#スクリプト |
| `Assets/Prefabs/` | ローカルでimportするパッケージ化対象Prefab（Git追跡対象外） |
| `Packages/com.marblexr.asset-bundle-builder/` | AssetBundle ZIPを生成するUnity Editor Package |

## 注意事項

- AssetBundleはMarbleアプリと同じUnityバージョンでビルドしてください。
- `Assets/`配下は`Assets/Scripts/`だけをGitで共有します。Prefab、Scene、画像、音声などはローカルでimportし、Publicリポジトリへコミットしないでください。
- Shader、Material、Unity PackageはMarbleアプリで利用可能なものに限定してください。
- Custom MonoBehaviourのコードはAssetBundleには含まれません。使用する型はMarbleアプリ側にあらかじめ組み込み、IL2CPP strippingの対象外にしてください。
- 実機のiOS／Android両方で動作を確認してください。

## 開発者向け

Builder Packageの詳細、導入方法、およびEditModeテストの実行方法は[パッケージのREADME](Packages/com.marblexr.asset-bundle-builder/README.md)を参照してください。
