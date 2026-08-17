# 実装チェックリスト: Marble AssetBundle Runtime Package

## 対応Plan / PR

- Plan: `./plan-1.md`
- Topic: `20260817_1_asset-bundle-runtime-fireworks`
- PR単位: Plan 1全体で1 PR

## PR / stack記録

| 項目 | 計画 | 実績 |
|---|---|---|
| headブランチ | `feat/asset-bundle-runtime` | `feat/asset-bundle-runtime` |
| baseブランチ | `main` | `main` |
| 直下の依存Plan | なし | なし |
| stack位置 | bottom | bottom |
| PR | 未作成 |  |
| マージ順 | 1番目 |  |

## ステータス定義

- `planned`: 計画済み（未着手）
- `done`: 計画どおり完了
- `changed`: 計画から変更して実施（理由を記載）
- `skipped`: 未実施 / 不要化（理由を記載）

## Task別チェック

### Task 1: 汎用Runtime PackageとFireworksテスト基盤を追加する

| ID | 種別 | 内容 | 対応コミット（予定） | 実績コミット | 状態 | メモ |
|---|---|---|---|---|---|---|
| 1-1 | config | 汎用Package／Assembly／Runtime-only依存境界 | `chore: scaffold asset bundle runtime package` | `fd3932e` | done | `com.marblexr.asset-bundle-runtime` / `Marble.AssetBundleRuntime` |
| 1-2 | test | Shot／Explosionと音声設定の契約テスト | `test: define firework particle audio behavior` | `8a914a2` | done | Shot配列長の誤用も回帰テスト化 |
| 1-3 | test | プール再利用・状態分離・空設定 | `test: cover firework audio pool edge cases` | `8a914a2`, `fd76644`, `6510850`, `45a9c36`, `219d8d1` | changed | 同一Particle重複、seed衝突／再利用、frame gap、明示Clear、component破棄まで追加 |
| 1-4 | test | Local／World／Custom座標変換 | `test: cover firework particle simulation spaces` | `8a914a2` | done | 移動・回転・非等方scaleを含む |
| 1-5 | verify | 実装前の失敗理由を確認 | - | - | done | stub時点で8件認識、未実装を理由に7 failed / 1 passed |

### Task 2: Runtimeコンポーネントを実装する

| ID | 種別 | 内容 | 対応コミット（予定） | 実績コミット | 状態 | メモ |
|---|---|---|---|---|---|---|
| 2-1 | impl | Particle配列と型付きAudioSourceプール | `feat: add firework particle audio runtime` | `0f3755b`, `fd76644`, `6510850`, `45a9c36`, `219d8d1` | done | Particle state配列もmaxParticles増加時のみ拡張 |
| 2-2 | impl | 検出・座標変換・音声再生・期限返却 | `feat: implement firework particle audio playback` | `0f3755b`, `fd76644`, `6510850`, `45a9c36`, `219d8d1` | changed | simulationSpeed／unscaled／pause、frame-gap death補完、seed衝突分離を追加 |
| 2-3 | fix | 空設定・枯渇・disable／destroyのガード | `fix: guard firework audio runtime state` | `fd76644` | done | inactive Pool root、無効Prefab空Pool化、所有Pool破棄 |
| 2-4 | verify | PlayModeテスト | - | - | done | Runtime behavior 18/18 passed |

### Task 3: 共有UPM Packageの配布・互換性契約を固定する

| ID | 種別 | 内容 | 対応コミット（予定） | 実績コミット | 状態 | メモ |
|---|---|---|---|---|---|---|
| 3-1 | fix | IL2CPP stripping保持設定 | `fix: preserve fireworks runtime types` | `5305055` | done | Package内`Runtime/link.xml`で型を保持 |
| 3-2 | docs | 汎用Packageの追加・依存・バージョン方針 | `docs: define asset bundle runtime package policy` | `5305055` | changed | 既存変更中のroot／Builder READMEは触らずPackage READMEを正本化 |
| 3-3 | docs | ホスト導入と互換性契約 | `docs: document asset bundle runtime integration contract` | `5305055`, `fd76644` | done | 実remoteとimmutable revision placeholderを記載 |
| 3-4 | verify | clean checkoutコンパイル／テスト | - | - | done | `git archive HEAD`からUnity import・最終19/19を実行 |

### Task 4: Editor性能ゲートを通す

| ID | 種別 | 内容 | 対応コミット（予定） | 実績コミット | 状態 | メモ |
|---|---|---|---|---|---|---|
| 4-1 | test | 4インスタンス／200 Particle性能シナリオ | `test: add fireworks runtime performance gate` | `5b5c49f`, `9a07813`, `fd76644` | done | 64 warm-up + 実300 PlayMode frames、live数を前後assert |
| 4-2 | verify | 定常フレームGC Alloc 0 B | - | - | done | clean HEAD archive: 0 B / 300 frames |
| 4-3 | verify | Editor CPU p95 1.0 ms以下 | - | - | done | clean HEAD archive: 0.6106 ms |
| 4-4 | verify | 全テストとコードレビュー | - | - | done | 19/19 passed、独立2レビューのHigh／Mediumを全修正 |

## 性能検証記録

| 検証 | 目標 | 実績 |
|---|---|---|
| managed GC Alloc | ウォームアップ後 0 B/frame | 0 B total / 300 measured frames |
| CPU p95 | Editorで1.0 ms以下 | 0.6106 ms / 300 measured frames（clean HEAD archive） |
| AudioSource数 | 設定したPool Size上限以下 | 32 / expected 32 |
| テストシナリオ | 4 instances / 200 live particles / 300 frames | Unity 6000.3.11f1、64 warm-up、200を計測前後assert |

## 計画差分ログ

| 日時 | 変更内容 | 理由 | 承認者 |
|---|---|---|---|
| 2026-08-17 | root／Builder READMEと`packages-lock.json`を変更対象外とし、Package READMEと`Packages/manifest.json`の`testables`で自己完結 | 対象ファイルにユーザー作業中の差分があり、混在コミットを避けるため | Codex |
| 2026-08-17 | randomSeed単位のedge-trigger状態とinactive Pool rootを追加 | 独立レビューのHigh／Medium指摘（slow/fast/unscaled/pause、component単体破棄、active prefab副作用） | Codex |
| 2026-08-17 | frame-gap death補完をdelta境界へ限定し、seed／lifetime衝突時のstate照合を追加 | 独立再レビューのHigh／Medium指摘（death欠落、Clear過剰発火、state誤対応） | Codex |

## 最終確認

- [ ] 全Taskの状態を更新した。
- [ ] `changed / skipped` の理由を記載した。
- [ ] 変更を伴うサブタスクの実績コミットを記録した。
- [ ] EditMode／PlayMode／Performanceテストを完了した。
- [ ] Plan内の全Taskが同じheadブランチに含まれている。
- [ ] head/baseブランチが計画どおりである。
- [ ] このPlanに対応するPRが1つだけ作成または更新されている。
- [ ] 実施内容、テスト、性能結果、stack依存をPR要約に反映した。
