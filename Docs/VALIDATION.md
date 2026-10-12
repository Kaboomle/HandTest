# 検証結果 — 2026-10-10

2026-10-11に追加した手プレビューと手元ラベルの検証は [HAND_PREVIEW_VALIDATION.md](HAND_PREVIEW_VALIDATION.md) を参照してください。以下は初回実装の証跡です。

## 完了区分

| 区分 | 状態 | 根拠 |
|---|---|---|
| コード作成済み | 完了 | SDK入力、特徴量、5ルール、候補、安定化、イベント、UI、CSV、再生、設定、評価指示・集計 |
| Editor検証済み | 完了 | Unity 6000.0.38f1、EditMode **41/41成功**、PlayMode画面・記録・読込み確認 |
| Androidビルド済み | 完了 | **ARM64／IL2CPP APK生成成功、エラー0、警告12**。署名v2検証成功 |
| PICO実機検証済み | **未実施** | adb devicesで接続端末なし。SDK取得・HMD・OS・精度・端末速度は未確認 |

既存Unity版を維持し、未導入だった公式PICO Integration SDK 3.4.0を追加しました。SDK HEADは`e0740bf309b26a0b9adae33c506514569bfe5d38`、SDKチェックアウトにソース変更はありません。関節対応、API構造体・フラグ、座標変換は取得したSDKソースで確認しました。

## 受入条件

| ID | 実施内容 | 結果 |
|---|---|---|
| A01 | Unityコンパイル、Android ARM64 IL2CPP APK、package／API／ABI／署名検査 | 成功。PICOで起動できることは未確認 |
| A02 | 5ポーズとIntermediate→Otherの合成骨格 | 成功 |
| A03 | 両手、平行移動、回転、一様尺度0.6／1／1.8、鏡映 | 成功。有効尺度範囲内の幾何条件を検証 |
| A04 | 角度境界、Pinch境界と履歴、NaN、Infinity、無効点、ゼロ区間、異常尺度 | 成功 |
| A05 | 100ms未満の変化、可変間隔、pending中の旧stable維持 | 成功 |
| A06 | Open＋Pinch競合、優先順変更、部分候補と代表TrackingUnavailable | 成功 |
| A07 | Endの1回発火、追跡復帰、一時停止・無効化・巻戻しのリセット | 成功（状態機械／ライフサイクル入力のソフトウェア検証） |
| A08 | UTF-8 CSV、引用符・改行・ロケール、再読込み、判定・イベント一致、逐次／時刻再生 | 成功 |
| A09 | 左の喪失が右の安定化へ波及しない、左右独立 | 成功 |

追加検証は、無効な任意点・Quaternionの空欄化、記録上限超過の件数と未完了フラグ、書込不能の表示用エラー、記録再開始失敗が前セッションを上書きしないこと、設定の独立コピーです。Python集計は**6/6成功**し、欠損を除く正解率が100%でも欠損込み試行成功が失敗になること、指示された正解への初回切替を不要切替から除くことを確認しました。

## PlayMode・処理時間

生成シーンを実際にPlayModeで動かし、左Open・右Victoryの安定判定、デバッグCanvas描画、記録開始／停止、**145バッチ（左右290手フレーム）の書込・再読込み、完了フラグtrue**を確認しました。画面は`Docs/dashboard.png`、実行結果は`Validation/Logs/playmode-smoke.txt`です。実機の視線滞留操作・文字サイズは未確認です。

PCの合成入力で6形状を保存時刻20ms間隔で各3秒保持し、1000回ウォームアップ後、両手5000回を測定：

- 中央値：**0.0030ms**
- 95パーセンタイル：**0.0036ms**

対象は特徴量・ルール・安定化・イベントとその割当てで、安定後のUpdateイベントとポーズ遷移を含み、SDK取得・UI・ファイルI/Oを除きます。**Windows Editorの値であり、PICO実機の目標達成を意味しません。** 実機の中央値1ms／P95 2ms目標は未計測です。

`Samples/SyntheticSession`は260バッチのソフトウェア例で、5ポーズ・Other・追跡喪失と独立した10試行の注釈を含みます。集計結果は`Validation/Logs/sample-analysis`。その正解率は合成骨格に対する動作確認であり、実測のハンドトラッキング精度として使用できません。

## APK

- 成果物：`Builds/HandPose-PICO4Ultra.apk`
- application ID：`com.labolatry.handpose`
- versionName：`0.1.0`、versionCode：`1`
- 最低API：29、ターゲット／compile API：35（Automaticで選択）
- ABI：arm64-v8aのみ。libil2cpp.soとPICOのネイティブライブラリを含む
- APK Signature Scheme v2：検証成功
- SHA-256：`421CE00F4BD71B4995AA124662E4D57622F4464799B3C8D9CF0CD7328BCE77D4`

Unity 6のGameActivity既定値はSDK検査で拒否されたため、公式SDKの指定するActivityへ設定しました。元の日本語パスはAndroidビルドで拒否されるため、ASCIIのTEMPコピーからビルドし、APKを作業フォルダへ回収しました。`Tools/Build-Android.ps1`で同じ方法を再実行できます。SDKが必要とする未生成のデバッガー設定アセットも標準型で用意しています。

最終ビルドの12警告はSDKの非推奨API・型宣言等を含みます。SDKソースを直接変更して抑制していません。終了時のXR Manager未初期化警告は、Standalone EditorでAndroid用ローダーを開始していない状態の終了処理であり、テスト・ビルドは成功しています。

## 証跡

| ファイル | 内容 |
|---|---|
| Validation/Logs/editmode-final.xml | 最終41テストのNUnit結果 |
| Validation/Logs/tests-final.log | 最終Editorテストログ |
| Validation/Logs/playmode-repaired.log | 成功したPlayMode描画・記録ログ |
| Validation/Logs/playmode-smoke.txt | 記録・再読込み結果 |
| Validation/Logs/benchmark.txt | PC分類処理時間 |
| Validation/Logs/android-build-final.log | 最終APKビルドログ |
| Builds/build-result.txt | 最終ビルド概要 |

初回の環境・設定確認用失敗ログも保存しています。最終成功の判定は上記のfinal／repairedログに基づきます。

## 実機で残る確認

READMEの実機手順に従い、SDKフラグ・座標と頭位置の整合、片手／両手欠損、追跡復帰、HMDパネルの読みやすさ、視線による記録、OS/build DISPLAY、CSV回収を確認してください。良好な保持条件で左右各5ポーズ×10試行を取り、欠損率・有効区間のクラス別精度・欠損込み成功・遅延・不要切替を分けて集計します。端末の処理時間と失敗条件も記録し、`Docs/EVALUATION_NOTES.md`へ追記してください。しきい値は予備調整値のままで、実機評価による調整はまだ行っていません。
