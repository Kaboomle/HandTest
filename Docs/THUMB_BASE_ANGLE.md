# 親指の基部角度による判定

更新日：2026-10-12。ルール版6、設定版`6.0-thumb-base-angle`。

親指のCMC→MCPと、手首→中指MCPのなす角度を使います。

```text
u = ThumbProximal - ThumbMetacarpal
v = MiddleProximal - Wrist
angle = acos(clamp(dot(u, v) / (length(u) * length(v)), -1, 1)) [degree]
```

| 解剖学上の点 | アプリ／PICO SDKの関節 | SDK番号 |
|---|---|---|
| 親指CMC | ThumbMetacarpal／JointThumbMetacarpal | 2 |
| 親指MCP | ThumbProximal／JointThumbProximal | 3 |
| 手首 | Wrist／JointWrist | 1 |
| 中指MCP | MiddleProximal／JointMiddleProximal | 12 |

SDKの関節名は解剖学上の関節名とは異なります。対応関係は[UnityのHand data model](https://docs.unity.cn/Packages/com.unity.xr.hands@1.5/manual/hand-data/xr-hand-data-model.html)に基づき、インストール済みPICO SDKの名前と番号を確認しました。

| 基部角度 | 計測した親指状態 | ポーズ照合で使う状態 |
|---|---|---|
| 45°以上 | Extended | Extended |
| 20°以下 | Bent | Bent |
| 20°より大きく45°未満 | Intermediate | Bent |
| 必要点が欠損／非有限／線分が短すぎる | Invalid | Invalid |

45°／20°は利用者が選択した初期値です。`Assets/Resources/DefaultPoseConfiguration.asset > settings > thumbBase`のextendedDegrees／bentDegreesで調整できます。実行中はApply configurationで反映します。浮動小数点の境界誤差を吸収するため0.0001°の許容を使います。

親指のMCP／IP内角、ThumbDistal、ThumbTipの曲がりは、この状態分類に使いません。ThumbDistalの欠損だけでも親指をInvalidにしません。Pinchは従来の親指先と人差し指先の距離で判定するため、ThumbTipを必要とします。

ダッシュボードには`Thumb: Extended  Base angle 65.0 deg`のように基部角度を表示します。Intermediateは`Intermediate (as Bent)`と表示します。CSVの`thumb_state`には計測した状態、`thumb_base_angle_deg`には新しい判定角度を保存します。MCP／IPのCSV列は過去の診断との比較用に残しています。

新規記録はCSV schema 2／rulesVersion 6。旧版1～5の記録は保存されたルール版で再計算します。旧内角の150°／125°等のしきい値を、新しい方向の角度には転用しません。

手首に対する方向の角度なので、手の平行移動、回転、左右反転、等方的な拡大縮小では値が変わりません。世界座標の上方向は使いません。

Live Previewの視点・手認識は版5でユーザー確認済みです。本定義の閾値による認識精度は実機での調整・評価が必要です。
