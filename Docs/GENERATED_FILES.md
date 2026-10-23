# 生成物とローカル設定の管理

生成物・端末依存の設定は作業フォルダーに残し、Gitの追跡対象から外す。
`Assets/Plugins` のプラグインDLLや、モデル・画像・Scene・Prefabは対象にしない。
既存のGit履歴の削除・圧縮・書き換えは行わない。
生成コードの差分は `.gitattributes` でバイナリ扱いにし、数百万行の削除差分を展開しない。

## 対象

- リポジトリ直下のWindows Player依存ファイル: `D3D12`、`MonoBleedingEdge`、`UnityPlayer.dll`、`UnityCrashHandler64.exe`、`WakayamaAR_Photo(proto)_Data`。
- ビルド時の `*_BurstDebugInformation_DoNotShip`。
- `test_a_BackUpThisFolder_ButDontShipItWithYourGame` と、「新しいフォルダー」内の同種バックアップ。
- Unityの個人用 `UserSettings` と自動生成 `.slnx`。
- 今後のテスト・ビルド・ログの出力先 `Artifacts`。

正確な対象パスは [整理ツール](../Tools/untrack_generated.py) の `TARGETS` に限定する。
新しい生成物を発見しても、ソースや必要な成果物か確認せず対象を拡張しない。

## 確認と整理

```powershell
python -B Tools/untrack_generated.py
python -B Tools/untrack_generated.py --untrack
```

引数なしは読み取り専用。`--untrack` は次の順に実行する。

1. 対象がリポジトリ内の通常ファイルであることを確認する。
2. `Artifacts/GitCleanup/<日時>.json` へ、元のコミット、パス、Git blob、サイズ、SHA-256を記録する。
3. `git rm --cached` でindexから外す。ファイルの実体は削除しない。
4. 全対象ファイルを再ハッシュし、実体が同一であることを確認する。

```powershell
python -B Tools/untrack_generated.py --verify Artifacts/GitCleanup/<日時>.json
git diff --cached --stat
```

index上では多数の削除がステージ済みになる。これは実体の削除ではない。
その他のコード変更は自動ステージしない。コミット・Pushも行わない。
作業フォルダーと過去の履歴はそのまま残るため、現在のディスク使用量やcloneサイズが直ちに減るわけではない。

## 追跡状態を戻す

整理を取り消す場合は、今回生成されたmanifestを指定する。作業ファイルを上書きしない。

```powershell
$cleanup = Get-Content -Encoding UTF8 Artifacts/GitCleanup/<日時>.json -Raw | ConvertFrom-Json
$restoreTargets = @($cleanup.targets)
git restore --source=$($cleanup.sourceCommit) --staged -- $restoreTargets
git status --short
```

その後、必要に応じて `.gitignore` の対応する除外を見直す。
将来ファイルの実体を失った場合、元の追跡ファイルはmanifestに記録したコミットから復元できる。
整理直前のローカルファイルの同一性はmanifestのSHA-256で確認する。
