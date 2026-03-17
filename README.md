# Sebanne Skinned Mesh Mirror

## 概要

`Sebanne Skinned Mesh Mirror` は、VRChat 向けの衣装・小物用 Skinned Mesh ミラー生成ツールです。片側の source side を選び、反対側の片側メッシュを生成する用途を想定しています。

現在は package 版だけで、VPM 導入から Dry Run、本生成まで確認済みです。Package Manager 上の displayName は `Skinned Mesh Mirror` です。

## 何ができるか

- source side にした片側メッシュから、反対側の片側メッシュを生成できます
- Dry Run で生成前に診断だけを先に確認できます
- `Tools/Sebanne/Skinned Mesh Mirror/Window` から main window を開いて、そのまま本生成まで進められます
- 問題が起きた場合は、診断結果と Console ログを見ながら原因を追えます

## 現在の対応範囲

- 現在の MVP は Leg / Foot 中心です
- package 版で VPM 導入から生成まで確認済みです
- BlendShape は補正未対応で、そのまま保持する前提です
- Runtime は現時点では実質未使用で、Editor 主体の構成です

## 導入方法

1. VCC の `Settings > Packages > Add Repository` から Sebanne の VPM listing を追加します。
2. listing URL に `https://sebanne1225.github.io/sebanne-listing/index.json` を指定します。
3. package 一覧から `Skinned Mesh Mirror` を導入します。

GitHub Release には version 付きの package zip も添付される前提です。手動導入したい場合は、release assets から zip を利用できます。

## 使い方

1. Unity 上部メニューの `Tools/Sebanne/Skinned Mesh Mirror/Window` を開きます。
2. 対象メッシュ、ミラールート、対象部位、source side を設定します。
3. まず `確認だけ` を有効にしたまま Dry Run を実行し、診断結果を確認します。
4. 問題がなければ本生成を実行します。
5. 必要に応じて `Tools/Sebanne/Skinned Mesh Mirror/Check Window` を開き、package 名や導線を確認します。

## Dry Run / 診断

- Dry Run では、生成処理を確定する前に診断だけを先に確認できます
- 失敗時は、まず診断結果と Unity Console のログを見る運用を前提にしています
- 対象メッシュ、推定される問題箇所、処理可否の判断が追いやすい形で診断情報を表示します

## 制限事項

- 対象は Leg / Foot 周辺を中心にした MVP です
- 全身の汎用ミラーにはまだ対応していません
- BlendShape は補正未対応で、そのまま保持されます
- 自動復旧や詳細な補助 UI は未整備です

## ライセンス

MIT License です。詳細は `LICENSE` を参照してください。
