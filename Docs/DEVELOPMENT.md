# 開発と検証

## 開発の入口

- Unity: `6000.2.10f1`。更新は別作業として扱う。
- プロジェクト: `WakayamaAR_Photo(proto)`。
- 製品Scene: `Assets/Scenes/SampleScene.unity`。
- 撮影UI: `Assets/Script/ScreenshotManager.cs`、`CameraMaskController.cs`。
- AR: Scene内の `ARTrackedImageManager` → `MyImageLibrary` → `Wakayama_Root`。
- `ARContentChanger` / `Prefab/AR_Spawner` は上記のScene参照経路にない。名前だけで修正対象と判断しない。
- テンプレートとXRI SamplesにはSceneからの依存がある。一括削除しない。
- Androidを今回の自動ビルド確認対象とする。iOSの配布・実機確認は別途必要。

## AIと人間の担当

AIは参照照合、座標計算、保存結果の模擬テスト、コンパイル、ビルドとログ整理を担当する。
人間は認識精度、ノッチ周辺、表示と保存画像の一致、写真アプリへの保存、発熱・操作感を実機で確認する。
自動テスト成功を、実機・見た目の承認として扱わない。

## 変更前の確認

リポジトリのルートで実行する。

```powershell
git status --short
python Tools/validate_project.py
python -B -m unittest discover -s Tools/tests -v
```

製品Scene・設定は読み取り専用。必須参照、ボタン接続、撮影設定、AR参照、metaの欠落・GUID重複、JSONの構造と依存バージョンを検査する。
異常系テストは `Artifacts/ToolTests` 配下の隔離コピーにだけ不正データを書き込み、終了後に自分のコピーだけを削除する。
Unityによる完全な依存解決や動作確認を代替するものではない。

## テストとAndroidビルド

```powershell
powershell -ExecutionPolicy Bypass -File Tools/develop.ps1 -Task Check
powershell -ExecutionPolicy Bypass -File Tools/develop.ps1 -Task Test
powershell -ExecutionPolicy Bypass -File Tools/develop.ps1 -Task BuildAndroid
```

`Test` は静的検査→検査ツールの回帰テスト→EditMode→PlayModeを順に実行する。
`BuildAndroid` は同じ検査が通った後にDevelopment APKを生成する。署名・配布・インストールは行わない。
Unity 6000.2.10f1とAndroid Build Support、利用可能なUnityライセンス、`unity` CLI、Python 3.11以上が必要。
同じプロジェクトのEditorを閉じてから実行する。Editor・テスト・ビルドを同時実行しない。

- APK: `Artifacts/Builds/Android/WakayamaAR.apk`
- 各回のログ、NUnit XML、所要時間、ビルドの出典情報: `Artifacts/Runs/<日時>/`
- `Check` も含め、開始時に専用の出力フォルダーを作成する。失敗した実行も記録を残す。
- 要約: 同フォルダーの `summary.md` と `summary.json`。失敗段階、未実行段階、検査・テスト件数、時間、既知のログ診断を集約する。
- `StaticValidation.json` / `ToolTests.json` はその実行時の結果。`run-state.json` は各コマンドの終了状態。後から現在のソースを再検査した結果と混ぜない。
- XMLは合計と個々のテスト結果・対象クラスも照合する。欠落・破損・0件・件数不整合では後続処理を止める。
- `Check` / `Test` の要約に古いAPKを掲載しない。`BuildAndroid` ではその実行で記録したハッシュとの一致を確認する。
- 0件テストや古いXMLを成功扱いしない。失敗した段階で後続を止める。
- 独自スクリプトは `WakayamaAR.Runtime`。既存GUIDと公開ボタンメソッドは保持している。
- EditModeは座標計算と保存Scene参照を検査する。
- PlayModeは保存先だけを模擬し、画面遷移、失敗後の再試行、二重操作、Texture解放を検査する。
- 中断テストでは実際のシャッター処理を開始し、最初の描画待ちが終わる前に無効化する。GPU読取りの成功確認とは区別する。
- 撮影途中のオブジェクト／コンポーネント無効化では、その撮影を中断する。再有効化時は撮影画面へ戻り、再撮影できる。
- 外部保存が進行中なら無効化しても勝手に成功・失敗へ変更せず、保存結果の通知を待つ。
- `WaitForEndOfFrame` / GPU読取り、カメラ映像、認識、端末ギャラリー保存はこのテストの検証対象外。
- Editorでは実機保存を成功扱いせず、端末が必要な旨を画面へ表示する。
- 撮影枠の余白は `PhotoPanel` の `CameraMaskController.topMargin` のみで設定する。
- 撮影範囲が足りない横長画面では撮影しない。縦長のGameビューで画像確認する。
- 失敗・保存中の通知は実行時にCanvas上へ表示する。既存のScene階層は作り直さない。

Editorの `Wakayama AR > Validate Project` からも参照検査できる。
APK生成前には同じ検査を自動実行する。Sceneが未保存ならビルドせず、保存か取り消しを求める。

## 実機での確認とログ

人間が端末を選び、アプリを起動してから必要時に実行する。既存ログは消去しない。

```powershell
powershell -ExecutionPolicy Bypass -File Tools/collect-device-log.ps1 -DeviceSerial <adbの端末ID>
```

現在のアプリプロセスのログのみを `Artifacts/DeviceLogs` へ保存する。
クラッシュしてプロセスが終了した後のログ収集は別途Android Logcatで行う。
端末がない環境で、このコマンドや保存権限・写真アプリ連携を確認済みと報告しない。

## 失敗時の切り分け

- ライセンスIPCへの接続拒否・タイムアウトは、コードのコンパイル結果ではない。同じ条件で起動を反復せず、実行権限とライセンスクライアントを確認する。
- XMLが出ない場合はテスト未完了として扱い、該当回の `.log` を確認する。
- 失敗時はまず `summary.md` の失敗段階を見る。Python自体を起動できないなど、要約生成にも失敗した場合は `run-state.json` とコンソールを参照する。
- 要約のログ診断は既知のパターンだけを抽出する。分類されない警告や詳細な経緯は元ログに残す。診断情報の有無だけで成功判定しない。
- 実行中Editorがある場合、別のbatch Editorを同じプロジェクトへ起動しない。
- 初回AndroidビルドのImport・IL2CPP処理と、次回以降の所要時間を混同しない。
- ビルド中はXRシミュレーション設定の一時移動などが発生する。終了後の復元まで待ってからGit差分を確認する。

## 変更範囲

最初に `Assets/Script`、製品Scene、`Packages`、`ProjectSettings` を調べる。
`Library`、出力済みPlayer、IL2CPP出力、バックアップを探索対象に含めない。
既存のScene、Prefab、モデルのGUIDを保持し、Unityのバージョンとパッケージを無断で更新しない。
生成物の保管・Git追跡解除・復元は [生成物の管理](GENERATED_FILES.md) に従う。

## 改善効果の比較

同じ端末・解像度・操作で改善前後を各3回測り、中央値を比較する。

- 調査開始から変更箇所を特定するまでの時間、AIの再試行回数。
- 設定確認・ビルド準備の操作数と作業時間。
- 不整合発見までの時間、再ビルド／再インストール回数。
- 表示枠と保存画像の境界差、保存失敗後の再試行可否。
- 撮影反復時の時間とメモリ。初回Importと2回目以降は分ける。

監査時点では作業時間の基準値は未計測。推測した削減率を実績として記載しない。

## 今回の確認記録（2026-10-23）

- 静的検査: 16項目通過。検査ツールの回帰テスト: 5件通過。
- Unity 6000.2.10f1: EditMode 18件、PlayMode 7件通過。
- Android ARM64 Development APK: 生成成功。ZIP内CRCとmanifest・ARM64ライブラリの存在も確認。
- APK: [WakayamaAR.apk](../Artifacts/Builds/Android/WakayamaAR.apk)（51,026,615 bytes）。
- SHA-256: `AA07C03EE775F7D939B435FC7FF560B9CDC25924C42BE0CB19189CE4CF65AE0C`。
- 自動実行の記録: [Artifacts/Runs/20261023-120806-238](../Artifacts/Runs/20261023-120806-238)。
- 所要時間（今回各1回）: 静的検査0.23秒、検査ツールの回帰テスト0.24秒、EditMode 20.86秒、PlayMode 27.38秒、Androidビルド1163.49秒。テストの時間は初回Import後の値。ビルドには初回ネイティブコンパイルを含む。人間の作業時間の短縮率は未計測。
- 生成物・ローカル設定1,282件、1,572,416,786 bytesをGit追跡対象から除外。実体は保持し、前後のSHA-256一致を確認した。
- 既存追跡ファイル数は2,196→914（今回の新規追加ファイルを除く）。既存Assets 873件は追跡を維持。生成C/C++の追跡は942→0。
- 保持・復元情報: [Artifacts/GitCleanup/20261023T033002Z.json](../Artifacts/GitCleanup/20261023T033002Z.json)。Git上の削除はステージ済み。コード変更は自動ステージ・コミットしていない。
- 既存スクリプト3本のmetaは変更なし。Scene階層・モデル・画像・AR表示先は保持。Scene/AR_Spawnerのアセンブリ名とボタン接続表現を更新し、独立した撮影側余白を削除した。
- Unityのビルド処理が一時変更したAsset・ProjectSettingsは終了後に復元された。Unityバージョンとパッケージの変更なし。

未検証: 実機の画像認識、実カメラを含むGPU撮影、画角・色・通知の目視、写真アプリへの保存、長時間利用、iOSビルド・動作、端末ログ収集スクリプトの実機動作。

既存画像ライブラリの警告: `メディア` はARCore評価30/100、`PXL_20260205_053845665` は65/100。ビルドログの推奨値は75以上。今回、画像や認識設定は変更していないため、実機で重点確認する。
ビルド開始時にはライセンスクライアントの検証メッセージも記録されたが、テストとビルドは成功した。ログを「警告・エラー表示なし」とは扱わない。

## 追加改善の確認（2026-10-24）

- JSON破損・構造不正・文字コード不正、meta欠落・GUID重複、誤ったAR参照／スクリプトGUIDを、製品ファイルを書き換えず検証できるようにした。
- 静的検査は18項目。ツール回帰テストは39件（XML検証、要約の誤判定防止、隔離した一括手順の異常終了確認を含む）。最終の[Check要約](../Artifacts/Runs/20261024-030405-302/summary.md)で全件通過を確認した。
- 撮影途中に無効化して戻す2件のテストで、状態が `Capturing` に残る問題を修正前に再現した。[修正前のXML](../Artifacts/Tests/20261024-lifecycle/before-fix.xml)
- 中断復帰を修正し、PlayMode 13件が通過。100回の撮り直し／模擬保存で、各Textureの解放と通知オブジェクト数の維持を検証した。
- 新しい一括手順でEditMode 18件・PlayMode 13件が通過し、[検証要約](../Artifacts/Runs/20261024-025906-673/summary.md)を自動生成した。この実行のツールテストは38件。後から追加した一括手順の異常系1件は、別途通過を確認した。
- XMLを生成しない模擬Unityを隔離コピーで実行し、終了コード0でも `EditMode report` で失敗し、PlayModeを実行せず、失敗した要約を残すことを確認した。
- Scene、Prefab、画像、パッケージ設定は今回変更していない。前回からのGit整理状態は保持する。
- 今回APKは再生成していない。上記2026-10-23のAPKには、今回の中断復帰修正は含まれない。必要時に `BuildAndroid` で新しく生成する。
- 実カメラのGPU読取り、端末保存・認識、目視、iOS、実機でのメモリ・性能は引き続き未検証。反復テストは実端末の長時間動作確認の代替ではない。
