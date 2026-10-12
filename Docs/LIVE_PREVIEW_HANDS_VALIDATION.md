# Live Previewの手入力と視点修正の検証

実施日：2026-10-12。Unity 6000.0.38f1／PICO Integration SDK 3.4.0／PICO Live Preview 1.0.5。

## 修正

- Editor InputにAutoを追加。起動中のPICO Live Previewから左右26関節を読み取り、既存のポーズ判定・CSV・手プレビューへ渡す。
- Androidのネイティブ入力を保持。Editor専用SDK参照と実装をEditorアセンブリへ配置。
- SDKの座標変換を1回だけ適用し、位置／向きの有効フラグを独立に確認する。古い関節データを毎回消去してから取得し、手の欠損を合成入力で補わない。
- HandPoseDemoのXR OriginをFloorにし、Camera Offsetの初期Yを0に設定。実測手とカメラの親を共通にし、頭部追跡後にダッシュボードを再配置。

## ソフトウェア検証

- EditMode：**106/106 Passed、0 Failed、0 Skipped**。新規5件は全26関節の変換、無効／非有限データ、追跡欠損、左右独立の8ポーズ、Autoと明示的Syntheticの入力選択を確認。
- PlayMode：保存された修正後のシーンで、合成入力の8ポーズ安定判定、手の上のラベル、ダッシュボード、MCPだけの親指判定、IntermediateのBent扱い、CSV再生の表示が通過。
- 検証用コピーではPC用XRローダーを起動していない。PlayModeの入力元はSYNTHETIC。接続中のHMDへの配信をテストプロセスが奪わないための設定。
- 結果：[EditMode XML](../Validation/Logs/live-preview-hands-editmode.xml)、[UI検証](../Validation/Logs/live-preview-hands-ui-smoke.txt)。

SDK形式のテストデータは合成データであり、実測の手データではありません。ソフトウェアテストは、実機の受信成功や装着者が感じる視点の高さを証明しません。

## 実機確認

PDCのADB検出と接続準備、および修正前のUnity再起動後にHMDへゲーム映像が表示されることは確認済みです。2026-10-12、ユーザーから「高さも手の認識も正常になった」と回答があり、修正後の実機表示・認識を確認しました。この確認はルール版5の時点のもので、親指基部角度の版6の評価は含みません。確認手順は[Unity Live Previewの接続](UNITY_LIVE_PREVIEW.md)に記載しています。

既存APKファイルは今回再ビルドしていません。この修正をAPKへ含める場合は、UnityプロジェクトからAndroidビルドを作成します。
