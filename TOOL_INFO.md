# TOOL_INFO

このファイルは内部向けの整理メモです。公開向けの説明は `README.md` を優先します。

## ツール名

- Skinned Mesh Mirror

## package名

- `com.sebanne.skinned-mesh-mirror`

## 表示名

- `Skinned Mesh Mirror`

## 想定用途

- VRChat 向けに、source side として選んだ片側の衣装・小物用 Skinned Mesh から反対側メッシュを生成するためのツールを提供する。

## 現状の構成方針

- package は Editor 主体で整理し、受け皿は `Editor/Core`、`Editor/UI`、`Editor/Diagnostics`、`Editor/Utility` を基本にする。
- Runtime は現時点では実質不要で、共通モデルや再利用ロジックが必要になるまで予約領域として扱う。

## 現在の状態

- package 版だけで、VPM 導入、main window、Dry Run、本生成の確認が通っている。
- Check Window は package 確認用と、本体 Window への導線として残している。

## 非対応

- 全身汎用の高精度ミラー生成
- BlendShape の補正処理
- 自動復旧や高度な補助 UI
- 自動ビルドや CI 設定

## 今後やりたいこと

- Diagnostics / Utility の分離整理
- Runtime を本当に空のままでよいかの見直し
- BlendShape 補正方針の検討
