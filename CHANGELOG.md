# CHANGELOG

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