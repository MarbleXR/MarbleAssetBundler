# Marble AssetBundle Runtime PackageとFireworks音声の実装

## 概要

AssetBundle Prefabが参照するRuntime C#型を、AssetBundle作成プロジェクトとMarbleアプリで共有する汎用UPM Package `com.marblexr.asset-bundle-runtime`を作成する。Packageは`Marble.AssetBundleRuntime`という安定したAssembly契約を持ち、AssetBundleにコード自体を同梱せず、両プロジェクトが同じcommitまたはtagを参照する。

最初の機能として、`HS_ParticleEndSound`の意図した機能を`Marble.AssetBundleRuntime.Fireworks.FireworkParticleAudio`として再実装する。今後のAssetBundle用Runtimeコンポーネントも機能別namespace／フォルダでこのPackageへ追加できる構成とする。

Visual Scriptingで発生した毎フレームのGraph実行コストとParticle structのboxingを避け、ウォームアップ後のmanaged GC Alloc 0を性能契約とする。

## PR / stack構成

| 項目 | 計画値 |
|---|---|
| Plan | Plan 1（このPlan全体で1 PR） |
| headブランチ | `feat/asset-bundle-runtime` |
| baseブランチ | `main` |
| 直下の依存Plan | なし |
| stack位置 | bottom |
| マージ順 | 1番目 |

## 前提条件

- Unity `6000.3.11f1` でパッケージとテストをコンパイルする。
- Runtime PackageはAssetBundle作成プロジェクト固有Asset、Hovl Studio資産、`UnityEditor`、Marbleアプリ固有実装を参照しない。
- Assembly名 `Marble.AssetBundleRuntime`、namespace `Marble.AssetBundleRuntime.Fireworks`、型名 `FireworkParticleAudio` をAssetBundleとMarbleアプリ間の互換性契約とする。
- Packageのdisplay nameは`Marble AssetBundle Runtime`とし、Fireworks専用Packageとして扱わない。
- 既存のAssetBundle Builder Packageとユーザー作業中の差分に不要な変更を加えない。

**特記事項**

- トランクブランチには直接コミットしないこと。
- このPlan内の全Taskを同じheadブランチと1つのPRで実装すること。
- サブタスクを基準に適切な粒度でコミットすること。
- 実装完了後にコードレビューを実行すること。
- 全Taskと検証が完了した後、このPlanに対応するPRを1つ作成すること。

## 実装契約

- `ParticleSystem`と`ParticleSystem.Particle[]`はキャッシュし、`main.maxParticles`が増えた場合だけ配列を拡張する。
- ExplosionとShotは型付きの`AudioSource[]`、返却時刻配列、round-robin cursorをインスタンスごとに持つ。`static`、Dictionary、Queue、Coroutine、LINQは使用しない。
- Clip、volume、pitchはプール作成時ではなく再生ごとに選択する。ShotのClip選択はShot配列の要素数だけを使う。
- Local／World／Custom Simulation Spaceを判定し、Particle座標を常に正しいワールド座標へ変換する。
- 再利用時は旧返却時刻を上書きし、過去の再生期限で新しい音を停止しない。
- `OnDisable` / `OnDestroy`で音源を停止し、生存プールや静的状態を残さない。

## タスク一覧

### Task 1: 汎用Runtime PackageとFireworksテスト基盤を追加する

**目的:** 汎用Packageの識別子／Assembly／依存境界を先に固定し、元スクリプトの意図した動作と再現しない不具合を実装前に契約化する。

**変更ファイル:**

- `Packages/com.marblexr.asset-bundle-runtime/package.json`
- `Packages/com.marblexr.asset-bundle-runtime/Runtime/Marble.AssetBundleRuntime.asmdef`
- `Packages/com.marblexr.asset-bundle-runtime/Runtime/Fireworks/FireworkParticleAudio.cs`
- `Packages/com.marblexr.asset-bundle-runtime/Tests/Runtime/Marble.AssetBundleRuntime.Tests.asmdef`
- `Packages/com.marblexr.asset-bundle-runtime/Tests/Runtime/Fireworks/FireworkParticleAudioTests.cs`

**サブタスク:**

1. [ ] Package ID、display name、Assembly名、Runtime-only依存境界と、テストをコンパイルするための最小`FireworkParticleAudio` stubを追加する。
   - 予定コミット: `chore: scaffold asset bundle runtime package`
2. [ ] Shot／Explosionの発火、Clip種別、volume／pitch範囲、返却を検証するPlayModeテストを先に追加する。
   - 予定コミット: `test: define firework particle audio behavior`
3. [ ] 2インスタンスの状態分離、Pool Size 1の期限前再利用、空Clip／Pool Size 0を検証する。
   - 予定コミット: `test: cover firework audio pool edge cases`
4. [ ] Local／World／Custom Simulation Spaceと移動／回転／scale付きTransformの音源座標を検証する。
   - 予定コミット: `test: cover firework particle simulation spaces`
5. [ ] 実装前に対象テストが未実装を理由に失敗し、テスト基盤はコンパイルできることを確認する。

**受け入れ条件:**

- [ ] Package名とAssembly名がFireworks専用でなく、Runtime-onlyの依存境界を満たす。
- [ ] 発火、返却、再利用、例外設定、座標変換を自動検証できる。
- [ ] 対象テストの失敗理由がRuntime未実装に限定される。

---

### Task 2: アロケーションを抑えたRuntimeコンポーネントを実装する

**目的:** Visual Scriptingと元スクリプトの負荷要因を除き、インスタンスごとに安全な音声プールを提供する。

**変更ファイル:**

- `Packages/com.marblexr.asset-bundle-runtime/Runtime/Marble.AssetBundleRuntime.asmdef`
- `Packages/com.marblexr.asset-bundle-runtime/Runtime/Fireworks/FireworkParticleAudio.cs`

**サブタスク:**

1. [ ] `ParticleSystem`、Particle配列、Explosion／Shotプール、返却時刻、cursorを事前確保する初期化を実装する。
   - 予定コミット: `feat: add firework particle audio runtime`
2. [ ] Particle生成／消滅検出、座標変換、再生ごとのClip／volume／pitch選択、期限返却を実装する。
   - 予定コミット: `feat: implement firework particle audio playback`
3. [ ] 空設定、nullプールPrefab、無効な範囲、プール枯渇、disable／destroyを例外や状態漏れなく処理する。
   - 予定コミット: `fix: guard firework audio runtime state`
4. [ ] Task 1のPlayModeテストを実行する。

**受け入れ条件:**

- [ ] `static`状態、Coroutine、定常的なParticle配列生成がない。
- [ ] ShotとExplosionが正しい設定とワールド座標で再生される。
- [ ] プールはインスタンス間で共有されず、設定数を超えて増えない。

---

### Task 3: 共有UPM Packageの配布・互換性契約を固定する

**目的:** AssetBundle作成側とMarbleアプリ側が同じAssembly・型・serialized field契約を使える状態にする。

**変更ファイル:**

- `Packages/com.marblexr.asset-bundle-runtime/Runtime/link.xml`
- `Packages/com.marblexr.asset-bundle-runtime/README.md`
- `Packages/packages-lock.json`
- `Packages/com.marblexr.asset-bundle-builder/README.md`
- `README.md`

**サブタスク:**

1. [ ] AssetBundleからdeserializeされる`Marble.AssetBundleRuntime`のRuntime型（初回は`FireworkParticleAudio`）がIL2CPP strippingで失われない`link.xml`を追加する。
   - 予定コミット: `fix: preserve fireworks runtime types`
2. [ ] Packageに追加できるコードを「AssetBundle Prefabが直接参照するRuntime型」に限定し、機能別namespace／フォルダ、依存方向、Semantic VersioningのルールをPackage／ルートREADMEへ記載する。
   - 予定コミット: `docs: define asset bundle runtime package policy`
3. [ ] Marbleアプリ側にGit URLのcommit/tag固定で導入する手順、Builder利用者が共有Runtime型をPrefabへ付与する手順、Assembly／型名／serialized fieldを破壊的に変更しないルールをREADMEへ記載する。
   - 予定コミット: `docs: document asset bundle runtime integration contract`
4. [ ] clean checkoutでPackageのコンパイルとテスト実行が再現できることを確認する。

**受け入れ条件:**

- [ ] Runtime PackageがAssetBundle作成側／Marbleアプリ側の固有コードなしで単独コンパイル・テストできる。
- [ ] Fireworks以外のAssetBundle用Runtime型を追加できる配置・依存・バージョン契約が文書化される。
- [ ] Marbleアプリ側に導入すべきPackage revisionと互換性契約が明文化される。
- [ ] IL2CPP stripping対策がPackage自身に含まれる。

---

### Task 4: Editor性能ゲートを通す

**目的:** Visual Scripting PoCで発生した「Editorで重い」問題を、Prefab量産前に定量検証する。

**変更ファイル:**

- `Packages/com.marblexr.asset-bundle-runtime/Tests/Performance/Marble.AssetBundleRuntime.PerformanceTests.asmdef`
- `Packages/com.marblexr.asset-bundle-runtime/Tests/Performance/Fireworks/FireworkParticleAudioPerformanceTests.cs`

**サブタスク:**

1. [ ] 4インスタンス／合計200生存Particle／300フレームの再現可能な負荷シナリオを追加する。
   - 予定コミット: `test: add fireworks runtime performance gate`
2. [ ] プールウォームアップ後の`FireworkParticleAudio.LateUpdate`起因managed GC Allocが0 B/frameであることをProfilerで確認する。
3. [ ] 同一シナリオで、`FireworkParticleAudio.LateUpdate`のCPU p95がEditorで1.0 ms以下、かつAudioSource数が設定上限から増えないことを記録する。
4. [ ] EditMode／PlayMode／Performanceテストとコードレビューを完了する。

**受け入れ条件:**

- [ ] ウォームアップ後の定常フレームでmanaged GC Alloc 0を満たす。
- [ ] 4インスタンス／200生存Particleの負荷でCPU p95 1.0 ms以下を満たす。
- [ ] 計測シナリオ、Unityバージョン、結果がchecklistとPRに記録される。

## Plan内の依存関係

- Task 1 → Task 2 → Task 3 → Task 4の順に実施する。
- Task 4の性能ゲートを通過しなければPlan 2のPrefab作成へ進まない。

## Planの完了条件

- [ ] 全Taskの受け入れ条件を満たした。
- [ ] 必要なEditMode／PlayMode／Performanceテストを完了した。
- [ ] コードレビューの指摘を反映した。
- [ ] head/baseブランチがPR / stack構成どおりである。
- [ ] このPlanに対応するPRを1つ作成または更新した。

## 備考

- AssetBundleにC# assemblyは含まれない。AssetBundleがこのPackageの型を参照する場合、Marbleアプリが同一Package revisionをPlayerビルドへ含めることが必須である。
- Public serialized fieldの改名は既存AssetBundleの互換性を壊す。必要な場合は`FormerlySerializedAs`とPackageバージョン方針を使う。
