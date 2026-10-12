# 親指基部角度の検証

実施日：2026-10-12。Unity 6000.0.38f1／PICO Integration SDK 3.4.0／Live Preview 1.0.5。ルール版6。

- EditMode：**125/125 Passed、0 Failed、0 Skipped**。新規19ケースで45°／20°境界、CMC→MCPと手首→中指MCPの方向、MCP／IPの曲がりの独立性、欠損・非有限・退化した線分、左右反転・回転・移動・尺度変換、しきい値の更新・コピー、旧版5／新版6のCSV再生を確認。
- PlayMode：修正後のデモシーンで8ポーズが左右独立に安定し、手ラベルとダッシュボードが一致。Base angle表示、65°基部・95°IPのGun／ThumbsUp、Intermediate (as Bent)、CSV再生の表示が通過。
- Python集計：9/9 Passed。rulesVersion 6を受け入れ、現行記録の削除ポーズを拒否する。

結果：[Unity XML](../Validation/Logs/thumb-base-angle-editmode.xml)、[UI検証](../Validation/Logs/thumb-base-angle-ui-smoke.txt)。表示例：[ダッシュボード](thumb-base-angle-dashboard.png)、[手ラベル](thumb-base-angle-hands.png)。

合成例：[8ポーズ](../Samples/SyntheticSessionV6)、[Intermediate](../Samples/IntermediateThumbSessionV6)、[先端が曲がった親指](../Samples/BaseCurlSessionV6)。すべて合成データで、PICOでの計測ではありません。過去のV1～V5の記録は保持しています。

Live Previewの視点と実測手入力は、利用者より「高さも手の認識も正常になった」と版5で確認済みです。今回の45°／20°の認識精度は実機評価を行っていません。定義と調整方法は[親指の基部角度](THUMB_BASE_ANGLE.md)を参照してください。

## Androidビルド

Android ARM64／IL2CPPのDevelopment APKを検証用コピーからビルドしました。結果Succeeded、Errors 0、Warnings 116。SDKを含む再コンパイルの警告はビルドログに記録しています。端末へのインストールはしていません。デバッグ署名であり、元プロジェクトの署名設定は保持しています。

APK：[HandPose-PICO4Ultra-ThumbBaseAngle.apk](../Builds/HandPose-PICO4Ultra-ThumbBaseAngle.apk)。サイズ77,599,920 bytes。SHA256 `81AB6A60ED9757ADDDA3FF49949F02F3AD89E2D6FF9416822374CB839011847E`。
[ビルド結果](../Builds/thumb-base-angle-build-result.txt)、[合成16試行の集計](../Validation/ThumbBaseAngleEvaluation/evaluation.md)。合成試行の結果は実機精度を表すものではありません。
