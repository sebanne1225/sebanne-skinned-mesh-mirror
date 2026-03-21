# RELEASE TEMPLATE

## この版でできること

- VPM 導入済みの `Skinned Mesh Mirror` を使って、片側の source side から反対側メッシュを生成できます。
- Dry Run / 診断 / 本生成の導線が package 版で利用できます。

## 想定用途

- VRChat 向け衣装・小物の Leg / Foot 周辺を中心に、片側 Skinned Mesh を反対側へミラーしたい場合を想定しています。

## 注意点 / 制限

- 現在は Leg / Foot 中心の MVP です。
- BlendShape は補正未対応で、そのまま保持されます。
- 問題が起きた場合は Dry Run の診断結果と Console ログを確認してください。

## 導入先

- VPM source: `https://sebanne1225.github.io/sebanne-listing/source.json`
- displayName: `Skinned Mesh Mirror`
- package name: `com.sebanne.skinned-mesh-mirror`
