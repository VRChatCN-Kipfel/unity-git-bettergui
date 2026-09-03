# Unity Git Better GUI

在 Unity 编辑器中提供 **JetBrains 级用户体验**的 Git 工具窗口。

三栏一体化窗口：**提交图谱 | 改动文件 | 提交详情** —— 支持内容级 diff、
hunk 三态（暂存 / 取消暂存）、3-way 合并冲突解决、rebase/cherry-pick 流程、blame 与远程仓库管理。

引擎基于 [`com.spoiledcat.git.api`](https://github.com/spoiledcat/git-for-unity)（MIT）；
本包内嵌其 Editor 源码，无需额外依赖解析。

[English](README.md) | 简体中文

## 功能特性

- 三栏工具窗：泳道式提交图谱 / 文件树 / 提交详情
- 可视化 diff 查看器：hunk 级 **查看 / 暂存 / 取消暂存**
- 3-way 冲突窗口（ours / theirs / 合并基线），含删除处理
- 完整 **merge & rebase 冲突流程** —— 解决、继续、中止；冲突清零后徽标转绿
- blame 侧栏、远程仓库管理窗口、标签 / 分支管理（列表 + 右键菜单）
- 图谱覆盖分支、标签与远端追踪引用，且不混入 stash 节点
- I18n 就绪：全部 152 条界面文案走翻译表（zh-CN 为 M4 交付项）

## 环境要求

- **Unity 2022.3 LTS 及以上**（编辑器工具窗，非运行时包）
- **系统 PATH 中可用 git** —— 不捆绑 git；除 git 自身外不发起任何网络请求
- Windows 为一等公民（macOS 未测试，但未刻意屏蔽）

## 安装（UPM）

Package Manager ▸ **+** ▸ *Add package from git URL*：

```
https://github.com/VRChatCN-Kipfel/unity-git-bettergui.git
```

（使用标签版本请在 URL 后追加 `#0.2.0-preview`，或查看 Releases 页面。）

## 快速开始

1. 按上述方式安装本包。
2. 菜单 Window ▸ Git (Better GUI)（或工具栏按钮）打开窗口。
3. 打开一个 git 工作树项目（或先 `git init`）——图谱立即呈现。

## 截图

_待补充 —— 欢迎贡献（M4）。_

## 路线图与贡献

详见 [ROADMAP.md](ROADMAP.md)。近期社区可参与项（M4）：

- **Project 资产图标** —— 在 Project 窗口展示文件 / 目录的 git 状态标记
- **一键 ignore 模板**
- **本地化** —— 中文优先；其余语言可通过 I18n 表贡献
- **side-by-side 对比** 压轴

欢迎 Issue 与 PR：一个 PR 只交一类变更，鼓励小而聚焦的提交。

## 许可证

MIT —— 见 [LICENSE.md](LICENSE.md)。内嵌引擎
[`com.spoiledcat.git.api`](Editor/Api/LICENSE.md) 与其依赖
`com.unity.editor.tasks`（`Editor/Api/com.unity.editor.tasks/LICENSE.md`）同为 MIT。