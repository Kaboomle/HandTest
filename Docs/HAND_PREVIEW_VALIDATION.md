# 手プレビュー追加の検証 — 2026-10-11

対象プロジェクトは、前回のフォルダから移動された `F:\LaboLatry\UItry` です。既存のXR Origin、カメラ、シーン構成と署名設定を保持し、必要な表示処理と無効カメラ参照だけを更新しました。

## 追加した表示

- 左右の26関節と28本のボーンを接続した骨格プレビュー。左は青緑、右はオレンジ。
- 手の最上部より上に表示し、閲覧中のカメラを向くポーズ名。安定判定を大きく、rawとpendingを下段に表示。
- 初回・復帰時は `Waiting...`。手の一部は見えていても分類不能なら `TrackingUnavailable`。手全体の追跡喪失・有効位置なしの場合は、その手のプレビューとラベルを非表示。
- 各手に入力元 `SYNTHETIC`／`PICO`／`REPLAY` を表示。
- 実機入力は有効なXRカメラの親を描画原点に使い、Deviceモードの高さオフセットとXR Originの移動を手・カメラで共有。SDK→Unity変換は追加しません。合成・再生入力は設定済みのtrackingOriginを使用。
- 詳細ダッシュボードの点だけの表示を置き換え、手の描画は独立した `HandPoseHandView` へ移動。ダッシュボードを無効にしても手とラベルは使用可能。

## 実施した確認

Unity 6000.0.38f1の検証用コピーでコンパイルし、最終EditModeテスト **41/41成功**。実際の保存済み `HandPoseDemo` をPlayModeで開き、次を確認しました。

- 有効なXRカメラ参照、プレビューコンポーネントが1個、左右の骨格とOpen／Victoryラベル。
- ラベルがカメラを向き、手の上に余白を確保。
- 表示無効化時に残像オブジェクトを残さない。
- 左の追跡喪失で左だけ消え、右のVictoryを維持。左の復帰で表示を再開。
- 合成入力による座標変換検査で、ネイティブ入力用の描画原点とカメラの高さオフセットが一致。
- 安定化待ち、部分欠損のTrackingUnavailable、無効な関節と接続ボーンの非表示。
- 骨格を非表示にしてもラベルを表示可能。有効位置がなくなると手を非表示。

`Docs/hand-preview.png` はこのPlayModeで描画した **合成入力の表示例** です。見やすく撮影するためカメラ位置を一時的に `(0,1.28,0)` にしてダッシュボードを隠しています。撮影時の変更はシーンに保存していません。

## Android

更新APK：`Builds/HandPose-PICO4Ultra-HandPreview.apk`。以前の `HandPose-PICO4Ultra.apk` は保持しています。Unityの通常ビルドメニュー／スクリプトで今後再生成される名前は `HandPose-PICO4Ultra.apk` です。

- ARM64／IL2CPP、エラー 0、警告 138。
- APK Signature Scheme v2：検証成功。
- SHA-256：`F77D8AD01D7A3252440C443A498746328AD5DF2ED8993AD31758D7259E91F23E`。
- デバッグ署名。元のプロジェクトの署名鍵・署名設定は変更せず、検証用コピーのみでデバッグ署名を使用しました。既に自分の鍵で署名したAPKを端末へ導入している場合は、元の署名設定で再ビルドして更新してください。

ビルド警告にはPICO SDKの非推奨API・型宣言、SDK同梱コントローラーprefabの欠損スクリプト、SDK同梱Outlineシェーダーの警告を含みます。今回の手プレビューはUnityの球／円柱とUnlitマテリアルで描画します。`HandPoseHandView`にC#コンパイラー警告はなく、SDKソースは直接変更していません。

## 未確認

PICO実機の手追跡・座標整合・ラベルの読みやすさ・描画負荷は **未確認**。合成入力の成功はセンサー精度の保証ではありません。Editorで標準のPlayを行う場合は合成骨格であり、実際の自分の手の入力はPICO単体でAPKを実行して確認します。

## 証跡

| ファイル | 内容 |
|---|---|
| Validation/Logs/preview-editmode-final.xml | 最終41テスト結果 |
| Validation/Logs/preview-editmode-final.log | コンパイル・テストログ |
| Validation/Logs/preview-playmode.log | 手プレビューのPlayMode検証 |
| Validation/Logs/hand-preview-smoke.txt | 表示・欠損・復帰の確認結果 |
| Validation/Logs/preview-android-build.log | 更新APKビルドログ |
| Builds/hand-preview-build-result.txt | 更新APKビルド概要 |
| Docs/hand-preview.png | 合成入力による描画結果 |
