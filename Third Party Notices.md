# Third Party Notices

本包（unity-git-bettergui / com.kf.gitui）为 MIT 许可，但**内嵌并分发以下第三方组件**，
使用前请阅读其原始许可：

## com.spoiledcat.git.api（Git for Unity API）
- 来源：https://github.com/spoiledcat/git-for-unity
- 许可证：**MIT**（Copyright 2020-2024 Andreia Gaita；Copyright 2019 Unity；Copyright 2015-2018 GitHub, Inc.）
- 内嵌位置：`Editor/Api/`（经 org fork `VRChatCN-Kipfel/git-for-unity` 的 `split/api` 分支以
  git subtree 引入；含 parents 修复与 zh-CN 本地化）
- 完整许可文本见 `Editor/Api/LICENSE.md`

## com.unity.editor.tasks（Unity Editor Tasks）
- 来源：https://github.com/Unity-Technologies/com.unity.editor.tasks
- 许可证：**MIT**
- 内嵌位置：`Editor/Api/com.unity.editor.tasks/`（随 api 包一并分发）

## 其它内嵌二进制（随 api 包分发）
- `Mono.Posix`（MIT/X11）
- `sfw`（libgit2 原生封装等，见 `Editor/Api/Third Party Notices.md`）
- `ICSharpCode.SharpZipLib`（MIT）

> 完整第三方许可清单以 `Editor/Api/Third Party Notices.md` 与 `Editor/Api/Third Party Notices - net35.md` 为准。