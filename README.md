# PICO 4 Ultra 静的手ポーズ判定



要件定義書1.0（2026-10-10）に基づくUnity試作です。機械学習を使わず、Open／Fist／Victory／Point／Pinch／Gun／Rock／ThumbsUpの8種類を左右独立に判定します。候補集合、Other、TrackingUnavailable、時間による安定化、イベント、デバッグ画面、全フレームCSV記録とEditor再生を含みます。



## 実装環境



| 項目 | 採用内容 |

|---|---|

| Unity | **6000.0.38f1**。既存のProjectVersion.txtを維持 |

| PICO SDK | **Unity Integration SDK 3.4.0**、公式release_3.4.0、commit `e0740bf309b26a0b9adae33c506514569bfe5d38` |

| SDK導入 | 元のmanifest／lock／Assets／PackageCacheにPICOなし。公式リポジトリを`Packages/com.unity.xr.picoxr`に追加。SDKソースは改変していません |

| XR入力 | PICOネイティブ`PXR_HandTracking.GetJointLocations`と`PXR_Loader`。XR Hands／Unity OpenXRとの入力混在なし |

| UPM | XR Management 4.5.0、XR Interaction Toolkit 3.0.7、XR Core Utils 2.3.0。既存Input System 1.13.0／URP 17.0.3を維持 |

| Android | ARM64／IL2CPP、最低API 29、Target API Automatic、Activity、OpenGLES3／Multiview |

| アプリID | `com.labolatry.handpose` |

| PICO OS | PICO 4 Ultra、**5.15.7.U**（利用者申告）。ADBのbuild DISPLAYは5.15.7。SDKによる手ポーズ判定の実機評価は継続して確認します |



Unity 6対応はSDK 3.1.0以降の公式リリースで確認したため、既存Unityを変更せず3.4.0を採用しました。[公式リリース](https://github.com/Pico-Developer/PICO-Unity-Integration-SDK/releases)、[手追跡の公式手順](https://developer.picoxr.com/document/unity/hand-tracking/)、[採用SDK](https://github.com/Pico-Developer/PICO-Unity-Integration-SDK/tree/release_3.4.0)。検証状況は`Docs/VALIDATION.md`を参照してください。



## 最初の実行



1. Unity Hubでこのフォルダを**6000.0.38f1**として開き、UPM解決とコンパイルを待ちます。

2. `Assets/HandPose/Scenes/HandPoseDemo.unity`を開きます。生成済みシーンがない場合はメニュー **Hand Pose > Generate demo and configure Android** を実行します。

3. EditorでPlayします。入力の既定値は**Auto**です。PICO Live Preview起動中は実測関節を取得します。接続しない場合は入力元に**SYNTHETIC**と表示し、合成の左Open、右Victoryが出ます。

4. `Hand Pose System > SyntheticHandJointProvider`のleftPose／rightPoseを変えるか、cyclePosesを有効にして切替や追跡欠損を試します。これはSDKセンサー検証ではありません。

5. 原本の`Assets/Scenes/SampleScene.unity`は残しています。AndroidビルドメニューはHandPoseDemoだけをAPKに含めます。



シーン生成メニューは既存のデモシーン／設定アセットを再作成しません。AndroidのアプリID、ARM64／IL2CPP、API、描画API、XRローダー、Hands Onlyを設定します。他のXRローダーがある場合は変更を拒否します。



## 親指基部の方向による判定と8ポーズ（2026-10-12）



親指の **metacarpal／proximal／distal／tip（SDKの2～5番）** は元から関節CSVと骨格プレビューに含まれています。現在の親指判定はCMC→MCPと手首→中指MCPの2方向の角度を使います。45度以上でExtended、20度以下でBent、その間はIntermediateです。Intermediateはポーズ照合時にBentとして扱います。



追加ポーズはGun、Rock、ThumbsUpです。Fuckは現行の認識ルール、候補表示、合成入力の選択・巡回、評価指示から削除しました。手元のラベル、安定化・イベント、CSV記録・再生、160試行の評価指示・Python集計は8種類に対応します。既存PoseIdの番号を保持しています。最新の定義ではOpenの親指はExtended、Fist／Victory／Pointの親指はBentが必須です。



最新ソースはルール版6（6.0-thumb-base-angle）です。設定と検証は[親指の基部角度](Docs/THUMB_BASE_ANGLE.md)を参照してください。新しいAPKは[HandPose-PICO4Ultra-ThumbBaseAngle.apk](Builds/HandPose-PICO4Ultra-ThumbBaseAngle.apk)、検証記録は[親指基部角度の検証](Docs/THUMB_BASE_ANGLE_VALIDATION.md)です。検証用コピーのデバッグ署名であり、既存の鍵で署名したアプリを更新する場合は、プロジェクトの保持されている署名設定で再ビルドしてください。旧McpOnlyEightPoses APKは版5です。



## 手のプレビューと手元のポーズ表示（2026-10-11追加）



手のプレビュー追加時のAPKは `Builds/HandPose-PICO4Ultra-HandPreview.apk` です。現行ソースの定義を本体で使う場合は再ビルドしてください。[表示例](Docs/hand-preview.png)と[追加表示の検証結果](Docs/HAND_PREVIEW_VALIDATION.md)も保存しました。APKはデバッグ署名です。既に自分の鍵で署名した版を端末に導入している場合は、元の署名設定を使ってUnityから再ビルドして更新してください。プロジェクトの既存署名設定は保持しています。



`HandPoseDemo`をPlayすると、左右の26関節を球とボーンで接続した手のプレビューが表示されます。左は青緑、右はオレンジです。分類に使用した関節位置をそのまま描画し、手モデルのメッシュや補間値を分類へ戻しません。



各手の上にカメラへ向くラベルが追従します。大きな文字は安定後のポーズ名で、下段に`raw`と切替待ちの`pending`を表示します。起動・復帰時の安定化待ちは`Waiting...`、手は観測できていても分類に必要な関節が不足する場合は`TrackingUnavailable`です。手全体が追跡不能な場合はプレビューとラベルを非表示にし、最後の位置を残しません。



`Hand Pose System > HandPoseHandView`でプレビュー／ラベルの表示、関節・ボーンの太さ、手の上の余白（`labelGapMeters`）、ラベルの大きさ（`labelScale`）、左右の色を調整できます。詳細ダッシュボードとは独立しているため、`PoseDebugView`を無効にしても手とラベルは表示されます。



**EditorのAuto入力は、起動中のPICO Live Previewから実測の手関節を取得します。Live Previewが起動していない場合やSyntheticを選んだ場合は合成骨格です。入力元の表示で区別してください。PICO単体のAPKはネイティブSDKを使用します。** CSV再生時も記録された関節と判定からプレビューを表示し、入力元を`REPLAY`と明示します。



シーンの表示用カメラ参照を、有効なXR OriginのMain Cameraへ修正しました。実機入力の描画原点には、そのカメラの親（XR Camera Offset）を使い、Deviceモードの高さオフセットやXR Originの移動も手とカメラでそろえます。合成・再生入力は設定済みのtrackingOriginを使います。無効なカメラが設定された別シーンでは、起動時に有効な`Camera.main`へ切り替えます。



## UnityのPlayでヘッドセットに表示する

PC側のXRローダーは**PICO Live Preview**です。PICO Developer Center（PDC）で端末が`Connected`になってから、`HandPoseDemo`を開きPlayを押します。設定変更後もOpenXRの初期化エラーが続く場合は、シーンを保存してUnityを再起動します。接続手順と今回の修正は[Unity Live Previewの接続](Docs/UNITY_LIVE_PREVIEW.md)を参照してください。

`PoseApplication > Editor Input`は`Auto`を選択します。画面の入力元が`PICO Live Preview GetJointLocations / PDC`になったことを確認し、コントローラーを置いて手をHMDのカメラへ見せてください。左右26関節から8ポーズを判定します。視点はXR OriginのFloor設定を使用し、手もカメラと同じ追跡座標で表示します。

## 実機ビルド・導入



1. Unity HubからAndroid Build Support／SDK & NDK Tools／OpenJDKを導入します。UnityのExternal Toolsで同梱ツールを選択します。

2. Build ProfilesでAndroidへ切替。Project Settings > XR Plug-in ManagementのAndroidでPICO Native Loaderを確認します。OpenXRローダーを併用しません。

3. シーンのTracking Originに`PXR_Manager`があります。Inspectorの**Hand Tracking**と**Hands Only**は`Assets/Resources/PXR_ProjectSetting.asset`に保存されています。

4. 実機のハンドトラッキングを有効にします。OSによって表示が異なる場合があります。公式手順はSettings > LAB > Hand Trackingです。実機OS版を記録し、採用SDKの追加条件も確認します。

5. **現在のプロジェクトはASCIIパスの `F:\LaboLatry\UItry` へ移動済みです。** **Hand Pose > Build Android APK** で直接ビルドできます。日本語パスで使用する場合は `Tools/Build-Android.ps1` でASCIIのTEMPコピーからビルドできます。出力は`Builds/HandPose-PICO4Ultra.apk`。`Builds/build-result.txt`で結果を確認します。

6. PICOの開発者モード／USBデバッグを有効にし、USB接続を許可します。Unity同梱adbの例：



```powershell

$adbPath = 'C:\Program Files\Unity\Hub\Editor\6000.0.38f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'

& $adbPath devices

& $adbPath install -r '.\Builds\HandPose-PICO4Ultra.apk'

& $adbPath shell monkey -p com.labolatry.handpose 1

& $adbPath shell getprop ro.build.display.id

```



Windowsのコマンドラインビルド：



```powershell

powershell -ExecutionPolicy Bypass -File '.\Tools\Build-Android.ps1'

# ASCIIパスのプロジェクトの場合のみ、直接実行も可能：

& 'C:\Program Files\Unity\Hub\Editor\6000.0.38f1\Editor\Unity.exe' -batchmode -nographics -quit -buildTarget Android -projectPath (Get-Location).Path -executeMethod HandPose.Editor.HandPoseProjectSetup.BuildAndroid -logFile '.\android-build.log'

```



既にそのプロジェクトをUnityで開いている場合は、Editorメニューから実行するか検証用コピーを使ってください。開いているプロジェクトに別のUnityプロセスを重ねて起動できません。



コピーを使うスクリプトは検証用フォルダを削除せず、`Validation/Logs/android-build-project-path.txt`へ場所を残します。最終成果物とログの回収後に、この特定の一時フォルダを必要に応じて削除できます。



## 画面と実機確認



左右別にhand tracked／input valid、raw、stable、ready、pending、9候補、親指の状態とMCP／IP、他4指の状態とPIP／DIP、距離m、手サイズm、Pinch比、時刻、無効理由を表示します。候補のM／-／?は一致／不一致／判定不能です。UIは英語表示で、パー=Open、グー=Fist、チョキ=Victory、指差し=Point、ピンチ=Pinchです。



原点ローカルの観測関節を小球で描きます。モデルの装飾や補間を入力には使いません。最初の頭位置が有効になった後にRECENTERを使い、パネルを正面に置き直してください。



実機では頭の向きをボタンへ合わせ、**1.2秒滞留**させます。START / STOP REC、BEGIN 160 TRIALS、RECENTERは手ポーズに依存しません。ボタンから視線を外すまで連続発火しません。EditorではGameビューのボタン、左上のEditor操作、Inspectorを使用できます。



期待する確認：



- Open→100ms後にstable_ready=trueとstable Open。Fistへ変更直後はstable Openとpending Fist、100ms継続後にstable Fist。

- 視野外へ出した手だけraw／stable TrackingUnavailable、ready=false。もう片手の待機時間や安定状態は継続します。

- 視野へ戻すと100msの安定化をやり直します。SDKの片手取得失敗をもう片手へ波及させません。

- 記録中はRECと経過秒、保存先が表示されます。失敗時はRecording errorとエラー内容が出ます。



## ルールと設定



`Assets/Resources/DefaultPoseConfiguration.asset`で設定を保存します。`version`は変更理由に対応する版名へ更新してください。実行中にEditorのApply configurationで反映できます。記録中の変更は現在の記録を閉じ、状態をリセットしてから適用します。再生時はsession.jsonの設定を自動的に適用します。



| Pose | 初期条件 |

|---|---|

| Open | 親指を含む5指がすべてExtended |

| Fist | 親指を含む5指がすべてBent |

| Victory | 人差し指・中指がExtended、親指・薬指・小指がBent。2指間の開き条件なし |

| Point | 人差し指がExtended、親指・中指・薬指・小指がBent |

| Pinch | 親指先と人差し指先の距離／IndexProximal–LittleProximal間距離が成立域 |

| Gun | 親指・人差し指がExtended、中指・薬指・小指がBent |

| Rock | 人差し指・小指がExtended、親指・中指・薬指がBent |

| ThumbsUp | 親指がExtended、他4指がBent。空間的な上方向は条件にしない |

| Other | 必要入力が有効で、対象8条件に不一致 |

| TrackingUnavailable | 必要点・有限性・幾何計算・正規化尺度・時刻のいずれかが無効 |



**Openの親指はExtended、Fist／Victory／Pointの親指は判定上Bentが必須**です。距離で判定するPinch以外の7種類は、5指すべての状態を指定します。**親指だけはIntermediateをポーズ判定上Bentとして扱います**。そのためFist／Victory／Point／Rockの親指条件に一致します。Open／Gun／ThumbsUpにはExtendedが必要です。他4指のIntermediateはBent／Extendedとして扱いません。Invalidは欠損として扱います。GunとPoint、ThumbsUpとFistは同時には一致しません。Pinchと指状態の候補は重なる場合があるため、代表判定には優先順を使います。NoGestureや操作意図の推定はありません。



PIPの3点はproximal／intermediate／distal、DIPはintermediate／distal／tip。内角は伸展で180度です。設定のfingers配列は0=人差し指、1=中指、2=薬指、3=小指です。指別に両角度160度以上でExtended、少なくとも一方130度以下でBent、その間はIntermediate。浮動小数点の角度境界に0.0001度の許容を設けています。



親指の基部角度は、`ThumbProximal - ThumbMetacarpal`（CMC→MCP）と`MiddleProximal - Wrist`（手首→中指MCP）のなす0～180度の角度です。45度以上でExtended、20度以下でBent、その間はIntermediateです。`DefaultPoseConfiguration > settings > thumbBase`のextendedDegrees／bentDegreesで調整します。親指Distal／Tipの曲がりは分類と親指状態の有効性に使いません。MCP／IP内角は過去の記録との互換性のためCSVに残し、判定用の`thumb_base_angle_deg`を追加します。実測のIntermediateは保持し、ポーズ照合時だけBentへ変換します。Pinchは従来の指先距離判定を使用します。



Pinchは比≤0.25で成立、比≥0.35で解除、その間は前状態を維持します。起動時・リセット後の境界域は不成立です。代表判定に必要な入力が欠けたときも履歴を破棄します。仮のスケール範囲は0.01～0.20m、最小骨区間は0.000001mで、設定可能です。サイズ不変性はこの有効スケール範囲内に限ります。



候補は別々に保持し、代表は**Pinch > Gun > ThumbsUp > Rock > Point > Victory > Fist > Open**。指状態ルールは4指の16点、ThumbMetacarpal、ThumbProximal、Wristの計19点を使います。中指MCPは16点の中に含まれます。全候補の代表判定はPinch用のThumbTipも必要です。ThumbDistalの欠損だけでは版6の判定を無効にしません。Wristを含む基部角度の必要点が欠損した場合、親指の状態はInvalidになり代表はTrackingUnavailableです。Palmと関節の向きは分類に不要です。`PoseRuleEvaluator.RequiredJoints(pose, rulesVersion)`で必要点を取得できます。



stableはrawが設定秒数（初期0.1秒）継続してから採用します。初回・復帰時は未確定ready=false。切替待ち中は旧stableを表示しUpdateを発火します。喪失時に旧ポーズのEndを1回だけ発火し、即座にTrackingUnavailableへ変更します。通常変更はEnd→Start→Update。OtherにもStart／Updateがあり、TrackingUnavailableにはStartを出しません。



`PoseApplication.Left/Right`から入力・特徴・候補・raw・stable・時刻・手を取得できます。`PoseEventRaised`はStart／Update／End／Reset、手、時刻、継続秒、reasonを公開します。Endは安定採用時点からの継続秒です。Resetは一時停止・無効化などの再現用マーカーです。



## 座標・有効性・サンプリング



名前付きJointIdはOpenXR／PICOの26点です。Palm=0、Wrist=1、Thumb=2～5、Index=6～10、Middle=11～15、Ring=16～20、Little=21～25。SDKの列挙名と明示対応しています。



全入力は**Unity左手系のtracking-originローカル、x右・y上・z前、メートル**です。原点GameObjectはscale=1にしてください。SDKの`Vector3f.ToVector3()`は(x,y,-z)、`Quatf.ToQuat()`は(x,y,-z,-w)へ変換します。これを1回だけ適用し、CSVはQuaternion **qx,qy,qz,qw**順。頭と関節は同じXR追跡原点を使い、Floorモードを要求します。非対応ならランタイムの原点を使用します。実機で頭と関節の整合確認が必要です。



実際に確認したAPIはSDKの`Runtime/Scripts/Features/PXR_HandTracking.cs`、`Utils/PXR_Type.cs`、`Hand/PXR_Hand.cs`です。GetJointLocationsのbool、isActive、jointCount、HandLocationStatus.PositionValid／OrientationValidを使用し、元のTracked等フラグも記録します。有限値だけで観測精度は保証しません。SDK提供のフラグは校正された信頼度ではありません。



取得は描画Updateごとの問い合わせです。SDK骨格構造にセンサー時刻がないためsdk_timestamp_sは空欄、アプリ時刻はTime.realtimeSinceStartupAsDoubleです。描画レートをセンサー更新頻度と呼ばず、同じ推定値を複数回取得する可能性を残します。SDK失敗時は前フレームの値を有効入力として再利用しません。



## 記録と回収



保存先は`Application.persistentDataPath/HandPoseSessions/<UTC日時_GUID>/`です。autoRecord、匿名participantId、キュー上限は設定アセットで変更できます。記録開始は分類状態をリセットし、セッションが途中のヒステリシス／安定化履歴に依存しないようにします。



| ファイル | 内容 |

|---|---|

| session.json | schema、ID、UTC時刻、匿名ID、Unity／SDK版とcommit、OS/build DISPLAY、座標、全設定、設定版、完了状態、脱落数、書込エラー |

| joints.csv | 全26点×左右×全フレーム。hand_valid、position_valid、orientation_valid、m位置、Quaternion、SDK生フラグ、SDK時刻、provider理由 |

| poses.csv | raw／stable／ready／pending、候補8列、親指状態・基部角度・MCP/IP deg、4指状態・PIP/DIP deg、Pinch距離m・尺度m・比、有効性、診断、設定版、両手処理ms |

| events.csv | Start／Update／End／Reset、手・時刻・pose・継続秒・reason・after_frame_id |

| trials.csv | 独立した指示ターゲット、手、cue／hold開始終了時刻、Completed／Aborted |



新規記録はCSV schema_version=2、設定rulesVersion=6（6.0-thumb-base-angle）です。`thumb_base_angle_deg`に判定用の角度を保存します。旧記録は保存時の版1～5で再生します（版1～4は従来の2角度判定、版5はMCPのみ）。ライブ設定の旧版は版6へ更新し、4指のしきい値と8ポーズの優先順を保持します。旧親指の内角しきい値は新しい角度へ転用せず、thumbBaseを45／20度で初期化します。版6のthumbBaseの調整値は保持します。



UTF-8・ヘッダー・InvariantCultureです。無効座標や向きは空欄とfalseにします。SDKフラグは数値を保存し、SDK時刻未提供を空欄で区別します。キューは上限付きで、1パケットに左右両手とイベントを含めます。ファイル書込は専用スレッド、定期flushと停止時close。上限超過はdropped_batches／dropped_eventsへ残し、recording_complete=falseにします。trialの脱落は記録エラーです。突然の強制終了では完了フラグがfalseのまま残ります。停止時は書込完了を待ちます。



実機で記録を停止してから回収：



```powershell

& $adbPath shell ls /sdcard/Android/data/com.labolatry.handpose/files/HandPoseSessions

& $adbPath pull /sdcard/Android/data/com.labolatry.handpose/files/HandPoseSessions '.\Recordings'

```



OSが外部アプリ専用領域へのアクセスを制限する場合、生成APKはDevelopment Buildなので`adb shell run-as com.labolatry.handpose`の可否を確認してください。`adb logcat -s Unity`で画面の保存先と書込エラーを確認できます。SDK自体のカメラ入力は使いますが、本アプリは映像・氏名を保存せず、ネットワーク送信もしません。



## Editor再生



1. Playし、左上の欄に**session.jsonが入っているセッションフォルダの絶対パス**を入力してLoad session folder。

2. Realtimeは保存時刻差に基づいて再生します。描画が遅れても入力フレームを飛ばさず、1描画最大256バッチまで処理し、その後追いつきます。

3. Stepは左右1バッチずつ進めます。PauseとRewindも使用できます。Rewindは状態とPinch履歴をリセットします。再生を記録していた場合は時刻・frame_idが反復する前に記録を閉じ、新しいセッションで記録します。

4. 保存された設定を自動適用します。events.csvのResetも元のフレームの後で適用し、一時停止や記録停止を再現します。その他のイベント・判定は関節入力から計算し直します。

5. 最新定義の試用データは`Samples/SyntheticSessionV6`です。版5のデータも保持しています。`Samples/IntermediateThumbSessionV5`は親指Intermediateの左Fist・右Pointを記録した表示確認用データです。`Samples/McpThumbSessionV5`はMCP=180度・IP=95度の左Gun・右ThumbsUpを記録した表示確認用データです。旧版のV4／V3／V2／SyntheticSessionも保持しています。**合成データ**であり実機計測ではありません。



再生はEditorメモリーへ全フレームを読み込みます。長時間セッションは分割してください。欠損・脱落を含む記録は再現性を保証しません。スキーマ不正、重複点、欠けた手フレーム、非有限値、時刻逆転を拒否します。



## 検証・予備評価



Window > General > Test RunnerのEditModeでHandPose.Testsを実行します。コマンドラインは`-runTests -testPlatform EditMode -testResults <絶対パス.xml>`。最新定義の検証結果は`Validation/Logs/thumb-mcp-eight-editmode.xml`です。



実機でBEGIN 160 TRIALSを選ぶと、左右各8種類×10回を固定乱数で並べ替え、**準備1秒・保持2秒・休息1秒**の指示を出します（全160試行、約10分40秒）。指示が正解であり分類器の出力を正解にしません。被験者が指示に従ったかは観察者が別に確認してください。中断試行はAbortedとして残します。設定調整用セッションと評価セッションを分け、評価データに合わせてしきい値を変更しません。



標準Pythonのみで集計できます：



```powershell

python Tools/analyze_session.py 'Recordings/<session-folder>' --output 'Evaluation/<session-folder>'

python -m unittest discover -s Tools -p 'test_*.py'

```



evaluation.json／classes.csv／evaluation.mdへ左右・クラス別に欠損率、判定可能時間のraw正解率、ready時間のstable正解率、欠損込みstable正解割合、試行成功、遅延、保持中の切替、両手処理時間の中央値／P95を出します。フレーム数でなく保存時刻の区間長で重み付けします。250msより長い未観測区間は欠損扱い（--max-gapで変更）。時刻・単位・分母をJSONに残します。



試行成功の初期運用定義は、保持全時間に対して**入力有効90%以上、stable正解90%以上、正解stable連続1秒以上**をすべて満たすことです。遅延は保持開始から最初の正解readyまでで、既に正解なら0秒。検出なしの件数も報告します。暫定目標はクラス別raw正解率90%以上、各手各クラス9/10試行以上。欠損を除いた精度だけで達成としません。



切替は保持中のreadyラベル変化を数えます。「不要な切替」はそのうち最初の正解ラベルへの切替だけを除外した件数です。追跡喪失はreadyラベルの切替として数えず、欠損率と試行成功へ反映します。初期認識への遷移を雑音と混同しません。



画面の処理msと集計はSDK入力取得・CSV・UIを除き、両手の特徴量／ルール／安定化／イベント処理を計ります。PICO実機で中央値≤1ms、P95≤2msを確認してください。外部イベント購読者の同期処理は計測に含まれます。目標未達なら原因と実測値を報告します。



追加予備評価は左右、位置移動、向き変更、対象外形状、片手・両手の視野外／復帰、照明と遮蔽を分けて記録します。失敗条件と調整理由は`Docs/EVALUATION_NOTES.md`へ残してください。本実験の人数・データ分割・統計解析は別途設計します。



## 構成と制約



`Assets/HandPose/Core`はSDK／UIに依存しない幾何計算、状態遷移、CSV。`Runtime`はPICO／合成／再生Provider、ScriptableObject、実行ループ、画面、評価指示。`Editor`はシーン／設定生成とビルド。`Tests/Editor`は合成検証。`Tools`は記録集計です。



SDKアダプター、HMD表示の読みやすさ、視線操作、実際の保存先回収、端末OS互換性、センサー精度・欠損率・端末処理速度はPICO 4 Ultraで確認が必要です。合成テストやAPK生成はその保証にはなりません。Motion、Pose／Motion統合、CTC、両手組合せ、NoGesture意図、手話翻訳、ネットワーク送信は実装対象外です。



SDK 3.4.0のEditor／Development初期化はデバッガー設定がない場合に例外を出すため、標準型の`PXR_PicoDebuggerSO.asset`をResourcesへ用意し、isOpen=falseとしています。SDKコードは変更していません。公開される観測・候補オブジェクトは読み取り専用として扱ってください。

