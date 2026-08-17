# Changelog

## [1.1.0] - 2026-07-31

- 非エンジニア向けの`.unitypackage`をUnity Editorメニューから作成できるようにした。
- `.unitypackage`を導入するプロジェクトで追加のUPM依存関係が不要になるよう、JSON処理をUnity標準APIへ変更した。

## [1.0.0] - 2026-07-20

- AssetBundle Package Builderを再利用可能なUPM Packageとして分離。
- iOS／Android AssetBundle、Unity manifest、`package.json`を含むZIP生成機能を収録。
- BuilderのEditModeテストをPackageへ同梱。
