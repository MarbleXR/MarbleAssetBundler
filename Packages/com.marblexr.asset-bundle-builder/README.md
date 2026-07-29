# Marble AssetBundle Builder

Marbleアプリで表示するPrefabをiOS／Android向けAssetBundleへビルドし、Backend管理画面へ手動アップロードするZIPを生成するUnity Editor Packageです。

## 動作条件

- Unity `6000.3.11f1`
- iOS Build Support
- Android Build Support
- Built-in Render Pipelineを使うMarble互換のコンテンツ設定

Packageは`com.unity.nuget.newtonsoft-json`と`com.unity.modules.assetbundle`を依存関係として導入します。

## 別Unityプロジェクトへの導入

Package Managerの`Add package from git URL...`へ、利用するcommitまたはtagを固定したURLを入力します。

```text
https://github.com/MarbleXR/MarbleAssetBundler.git?path=/Packages/com.marblexr.asset-bundle-builder#<commit-or-tag>
```

開発中にローカルで確認する場合は、このディレクトリを対象プロジェクトの`Packages/com.marblexr.asset-bundle-builder`へコピーしてEmbedded Packageとして利用できます。

## 使い方

1. `Tools > Marble > AssetBundle Package Builder`を開く。
2. Bundle Name、Display Name、Content Version、1件以上のPrefabを入力する。
3. `Validate`でエラーと警告を確認する。
4. `Build Package`でiOS／Android Bundleを含むZIPを生成する。
5. ZIPをBackend管理画面へ手動アップロードする。

既定の出力先はUnityプロジェクト直下の`AssetBundlePackages/`です。ZIPには`package.json`、iOS／Android Bundle、各BundleのUnity manifestが含まれます。

## コンテンツ互換性

- AssetBundleはMarbleアプリと同じUnityバージョンでビルドする。
- PrefabのShader、Material、Unity PackageはMarbleアプリで利用可能なものに限定する。
- Custom MonoBehaviourのC#コードはAssetBundleに含められない。使用する型はMarbleアプリへ事前に組み込み、IL2CPP strippingの対象外にする。
- Builderは外部AssetBundle依存を許可しない。
- 最終確認はiOS／Android実機で行う。

## テスト

PackageのEditModeテストAssemblyは`Marble.AssetBundleBuilder.Editor.Tests`です。Unity Test Runner、または次のBatch Mode相当の指定で実行します。

```text
-runTests -testPlatform EditMode -assemblyNames Marble.AssetBundleBuilder.Editor.Tests
```
