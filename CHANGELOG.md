# CHANGELOG

## [Unreleased]

### Fixed
- 自动刷新轮询不再阻塞编辑器主线程。原先 `OnEditorUpdate` 每次都在主线程同步执行
  `GetFingerprint()`/`LoadConflictPaths()`，其中 `LoadStatus()` 走 `RunSynchronously()`
  会 fork 一个 `git status -b -u --porcelain` 子进程并等待其结束；窗口不可见时也照跑，
  导致大仓库（含 `Library/`、未跟踪目录）下编辑器周期性掉帧。现在：
  1. **只在标签页真正渲染时轮询** —— 窗口被切到后台（`rootVisualElement.panel == null`）
     时完全不做 git 工作；切回时立即补一次刷新。
  2. **git I/O 全部移入后台线程**，主线程仅比较指纹并更新 UI；单飞 + 30s 超时兜底，
     避免请求叠加或异常后轮询永久停摆。

## [0.1.0] - 2026-09

### Added
- M2 图谱：泳道绘制、分支/标签/远端追踪覆盖、筛选、分支与标签管理（列表+右键菜单）。
- M3 内容级 DiffViewer：hunk 三态（查看/暂存/取消暂存）、3-way 冲突窗口（ours/theirs/merge base）、
  merge/rebase 冲突解决流程（解决→继续/中止，徽标随冲突清零转绿）、blame 侧栏、remote 管理窗口。
- I18n 框架：152 个界面文本键统一走翻译表（zh-CN 为 M4 交付项）。

### Fixed
- 图谱使用 `--branches --tags --remotes`，避免 `--all` 引入的 stash 节点污染。
- 合并冲突全部解决后（`git status --porcelain` 无条目）仍允许提交收尾合并。
- rebase 续作任务注入 `GIT_EDITOR=true`，避免编辑器拉起导致主线程挂起。

## [0.1.0-preview] - 2026-08

### Added
- M1 骨架：三栏窗口（图谱 | 文件树 | 提交详情）占位 + api 内嵌编译链验证。

_本包处于早期预览，API 不稳定。_