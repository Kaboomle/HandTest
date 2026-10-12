# Unity Live Previewの接続

更新日：2026-10-12

PICO 4 Ultra／OS 5.15.7.U、Unity 6000.0.38f1、PICO Integration SDK 3.4.0、PICO Live Preview 1.0.5、PDC 1.5.0での接続手順です。

## 手順

1. USBでPICOとPCを接続します。本体の開発者設定でUSBデバッグを有効にし、PCへの接続を許可します。
2. PDCの端末情報でPICO 4 Ultraが`Connected`になることを確認します。
3. UnityのProject Settings > XR Plug-in Management > PC Standaloneで**PICO Live Preview**を選択します。この経路ではOpenXR／PICO Nativeを併用しません。Android側はPICO Nativeのままです。
4. `Assets/HandPose/Scenes/HandPoseDemo.unity`を開いてPlayを押します。PDCが`Streaming`になると、HMDにMain Cameraのゲーム映像が表示されます。Sceneビュー自体は配信されません。
5. 外部からXR設定を変更した後に、以前のローダーでPlayが始まる場合は、シーンを保存してUnityを再起動します。

[PICO公式のLive Preview手順](https://developer.picoxr.com/document/unity/preview-app-scenes/)

## 端末を検出できない場合

今回、Unity付属ADBは端末を認識していましたが、PDCは`ADB_NOT_INSTALLED`で端末一覧の取得を止めていました。PDCが備えるカスタムADBパスへ、次の既存実行ファイルを登録しました。

```text
C:\Program Files\Unity\Hub\Editor\6000.0.38f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe
```

PDC側の設定キーは`custom_adb_path`です。PDCの設定画面から同じ実行ファイルを選択できます。登録後のPDCログで`source=custom`、`reason=ok`、`light=GREEN state=ready reason=service_and_device_connected`を確認しました。

PICO 4 Ultraでストリーミング接続だけが失敗する場合、PICO公式手順は本体の**Settings > General > PICO Connect Auto Discovery**をOFF→ONにし、PDCを再起動するものです。USB接続の認識と映像配信の準備状態を区別して確認してください。

[PICO公式のPDC接続手順](https://developer.picoxr.com/document/unity/pdc-basic-info/)、[PDCのトラブルシューティング](https://developer.picoxr.com/document/unreal/pdc-faq/)

## 今回のプロジェクト修正

`EditorBuildSettings`が参照する`Assets/XR/Settings/XRGeneralSettingsPerBuildTarget.asset`のStandalone Providersを、OpenXRから導入済みPICO Live Previewローダーへ変更しました。AndroidのPICO Native設定は保持しています。Unityログでは変更前のOpenXR起動が`xrCreateInstance: XR_ERROR_RUNTIME_FAILURE`で失敗していました。Windowsの描画はDirect3D 11、PDCの配信サービスは起動済みでした。

PDCの緑表示と、Unity再起動後にHMDへゲーム映像が表示されることを確認しました。

## 手の入力について

`Hand Pose System > PoseApplication > Editor Input`の既定値は`Auto`です。PICO Live Previewローダーが起動している場合は、Editor専用の`LivePreviewHandJointProvider`がPDCから左右26関節を読み取り、手プレビューと既存の8ポーズ判定へ渡します。画面の入力元に`PICO Live Preview GetJointLocations / PDC`が表示されます。APKは従来のAndroidネイティブSDKを使用します。

HMDのハンドトラッキングを有効にし、コントローラーを置いて、明るい場所で両手をHMDのカメラに見せます。手を見失うと`TrackingUnavailable`になり、合成骨格へ切り替わりません。Consoleの`[HandPose Live Preview]`に左右それぞれのactive／joint countと失敗理由を、状態が変わった時に出力します。

PICO Live Previewが起動していない場合の`Auto`と、明示的な`Synthetic`は合成骨格です。センサー取得に成功した証拠として扱わないでください。CSVのsourceにも使用した入力元を記録します。

## 視点と手の位置

`XR Origin`は`Floor`を要求し、`Camera Offset`の初期Yを0にしています。床基準のHMD位置に追加の1.1176mを加算しません。手プレビューもカメラと同じ`Camera Offset`を親にして、HMDと関節データの座標を合わせます。`Camera Y Offset`の値はDeviceモードへ戻した場合の補正として保持します。ダッシュボードは最初の頭部追跡が取得できた時点で正面へ配置し直します。

2026-10-12、利用者より「高さも手の認識も正常になった」と実機確認の回答を得ました。この確認はルール版5の時点です。親指角度の新しい判定定義は[親指の基部角度](THUMB_BASE_ANGLE.md)を参照してください。
