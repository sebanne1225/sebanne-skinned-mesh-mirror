# CLAUDE

> この文書は開発用のハンドオフ / 現況メモです。エンドユーザー向けの導入や使い方は `README.md` を参照してください。

## Goal

- `Sebanne Skinned Mesh Mirror` を package repo として維持し、片側 `SkinnedMeshRenderer` から反対側メッシュを非破壊生成できる状態を保つ。
- handoff には古い移植計画ではなく、package 化後の実装状態と、次に触る時の注意点だけを残す。

## Current State

- 既存 Assets 実装の package への受け替えは済んでいます。repo root が package source of truth です。
- `package.json` は `0.1.3`、ローカル HEAD は `3038593` (`master`) です。tag は `0.1.3` まであります。
- main UI は `Editor/UI/SkinnedMeshMirrorWindow.cs` にあり、menu は `Tools/Sebanne/Skinned Mesh Mirror/Window` です。`Editor/SkinnedMeshMirrorCheckWindow.cs` は package 読み込み確認用の補助ウィンドウとして残っています。
- Window から Dry Run と本生成の両方が行えます。出力先、suffix、verbose log、diagnostics panel 高さまで保持しながら使う構成です。
- `Editor/Core/SkinnedMeshMirrorBuilder.cs` で compatibility analysis、source side 判定、bone mapping、vertex 抽出、mesh 生成、asset 保存、sibling component コピーまで担当しています。
- `Editor/Core/SkinnedMeshMirrorLocalMap.cs` は `PrefabLocalMirror` 前提で左右判定と opposite bone 解決を行います。UI 上は `Hand / Arm / Foot / Leg` を選べますが、README 上の公開表現はまだ MVP 寄りで保守的です。
- `Editor/Core/SkinnedMeshMirrorTypes.cs` に enum、config/result、diagnostic entry、logging、`SideTokenUtility` がまだ同居しています。移植は済んでいるが、分割は未着手です。
- BlendShape は「補正未実装でそのまま保持」の方針です。builder 内でもその前提の warning / TODO が残っています。
- Runtime は asmdef だけの最小構成で、実装の中心は Editor 側です。
- README / CHANGELOG / `BOOTH_PACKAGE` / release workflow は現行の VCC listing 導線に合わせて整理済みです。

## Current Direction

- repo 内に hard blocker はありません。
- 次に触るなら、`SkinnedMeshMirrorTypes.cs` の責務分離を先にやる回か、MVP の対応範囲確認と公開表現を調整する回かを最初に決めると進めやすいです。
- いまの public claim は保守的です。UI が見せている `Hand / Arm` を README で広げるなら、Unity 側で実際の成功率を先に確認した方が安全です。
- Check Window を残すかどうかも次回整理候補です。ただし main window 導線を壊さないことを優先します。

## Current Blocker

- 明確な blocker はありません。
- 未実装の大きい論点は BlendShape 補正です。生成後の見た目確認は引き続き必要です。
- `MappingMode` / `OutputMode` の enum はありますが、MVP 実装は実質 `PrefabLocalMirror` + `MirroredRendererOnly` 固定です。

## Rules

- 非破壊
- 冪等
- まず `Dry Run`
- ログ重視
- 元の mesh / GameObject を直接壊さない
- まずは Editor 主体、Runtime は必要になるまで増やさない
- public claim を広げる前に Unity で実機確認する
- まず短い plan を出してから作業

## Key Files

- `Editor/UI/SkinnedMeshMirrorWindow.cs`
- `Editor/Core/SkinnedMeshMirrorBuilder.cs`
- `Editor/Core/SkinnedMeshMirrorLocalMap.cs`
- `Editor/Core/SkinnedMeshMirrorTypes.cs`
- `Editor/SkinnedMeshMirrorCheckWindow.cs`
- `README.md`
- `.github/workflows/release.yml`

## Resume Notes

- package: `com.sebanne.skinned-mesh-mirror`
- version: `0.1.3`
- latest tag: `0.1.3`
- HEAD: `3038593` (`master`)
- release asset 名: `com.sebanne.skinned-mesh-mirror-0.1.3.zip`
- menu: `Tools/Sebanne/Skinned Mesh Mirror/Window`
- 既定出力先: `Assets/Sebanne/SkinnedMeshMirror/Generated`
