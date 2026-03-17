# Sebanne Skinned Mesh Mirror

## 概要

`Sebanne Skinned Mesh Mirror` は、VRChat 向けの衣装・小物用 Skinned Mesh ミラー生成ツールです。source side として片側のメッシュを選ぶと、反対側の片側メッシュを生成することを目的にしています。

現在の package 版では、MVP 範囲の main window、Dry Run、本生成が通る状態まで整理されています。対象は Leg / Foot 周辺を中心にした実装です。
Package Manager 上の displayName は `Skinned Mesh Mirror` です。

## 何ができるか

- source side にした片側メッシュから、反対側の片側メッシュ生成を行うための土台を提供します
- Dry Run で実処理の前に診断だけ先に確認できます
- package 版の main window から本生成まで実行できます
- Runtime / Editor を分離した UPM package 構成で管理できます
- 診断結果とログを見ながら、失敗時の原因確認をしやすい形に整備していきます

## 現在対応していること

- 現在の package には、`Skinned Mesh Mirror` 用の命名、asmdef、main window、確認ウィンドウが含まれます
- package 内の受け皿は Editor 主体で、`Editor/Core`、`Editor/UI`、`Editor/Diagnostics`、`Editor/Utility` へ段階的に移植する前提です
- 現在 package 化できた範囲は `Editor/Core/SkinnedMeshMirrorTypes.cs`、`Editor/Core/SkinnedMeshMirrorLocalMap.cs`、`Editor/Core/SkinnedMeshMirrorBuilder.cs`、`Editor/UI/SkinnedMeshMirrorWindow.cs` です
- MVP の対象は Leg / Foot 中心です
- BlendShape は補正未対応で、現時点ではそのまま保持する前提です
- Runtime は現時点では実質不要と判断しており、共通モデルが必要になるまで予約領域として扱います

## 使い方

1. Unity Project の Package Manager から、この repo をローカル package として読み込みます。
2. Unity 上部メニューの `Tools/Sebanne/Skinned Mesh Mirror/Window` を開きます。
3. `確認だけ` を有効にしたまま Dry Run を先に試し、診断結果と Console ログを確認します。
4. 必要に応じて `Tools/Sebanne/Skinned Mesh Mirror/Check Window` を開き、package 名と導線を確認します。

## Dry Run / 診断

- Dry Run では、生成処理を確定する前に診断だけを先に確認できる想定です
- 問題が起きた場合は、まず診断結果と Unity Console のログを見る運用を前提にしています
- 対象メッシュ、推定される問題箇所、処理可否の判断が追いやすいログ設計を今後の本体移植で整備します

## 制限事項

- package 版は MVP 範囲の実行まで通っていますが、高度な整理や追加機能はまだこれからです
- 現在の移植計画は Editor コード中心で、Runtime にはまだ実質的な移植対象を置いていません
- MVP は Leg / Foot 周辺を主対象としており、全身の汎用ミラーにはまだ対応していません
- BlendShape は補正未対応で、そのまま保持される前提です
- 失敗時は診断結果とログの確認が前提で、自動復旧や詳細な補助 UI は未整備です

## ライセンス

MIT License です。詳細は `LICENSE` を参照してください。

## Release

GitHub Release を publish すると、version 付きの package zip が release assets に自動添付される前提です。
GitHub Actions の `workflow_dispatch` から手動実行した場合は、まず zip 作成結果を workflow artifact として確認できます。
