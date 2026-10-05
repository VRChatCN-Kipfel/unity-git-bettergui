# CHANGELOG

## [Unreleased]

### Added
- M4 界面多语言**贡献框架**（与 ignore 模板同一套"丢文件即贡献"思路；简体中文语言包随后按此流程提
  交，用于压力测试渠道本身）：
  - 一门语言 = `Editor/I18n/BuiltIn/<lang>.json`（扁平键值映射）+ 可选 `<lang>.meta.json`
    （`nativeName` / `maintainers` / `order`）。贡献者不需要改代码。
  - 三来源**键级**合并：个人 `UserSettings/GitBetterGui/I18n/` ＞ 项目 `.gitui-i18n/` ＞ 包内内置；
    **允许部分翻译**，未提供的键回退英文，语言窗口显示覆盖率（`ja-JP 42/203`）。
  - 占位符硬校验：带 `{0}` 类格式占位符的键（当前 64/203，分母随界面增长），索引集合与格式说明符必须与英文一致
    （语序自由）；不一致的键被**丢弃并回退英文**——否则 `I18n.L(key, args)` 会抛 FormatException。
    未知键忽略并计数，`_` 前缀键视为注释静默忽略。
  - 界面语言窗口（`Window ▸ Git ▸ Language…`）：列出语言包与覆盖率、报告被忽略/被丢弃的键、
    一键切换并记住（EditorPrefs `kf.gitui.language`）；启动时按已保存选择或系统语言应用。
  - 语言切换后主窗口界面重建（`I18n.LanguageChanged` → `RebuildUI()` + 重新灌图谱数据）。
  - **骨架导出**：一键生成含全部键与英文原文的语言包到项目目录，贡献者填空即可；已存在则拒绝覆盖。
  - `FlatJson`：自研严格扁平 JSON 解析器（`JsonUtility` 不支持任意键字典，本包又要求零依赖）。
  - 冒烟入口 `KF.GitUI.I18nSmokeTest.Run`：键表三方一致、英文表占位符连续性、JSON 六类非法输入、
    占位符比对、语言代码校验、单文件解析诊断、键级归并与"高层无效键剔除"、ApplyLanguage 回退与
    无残留、骨架导出、`ui.language.*` 键双向完备性。
- 设计稿 [docs/M4-LOCALIZATION.md](docs/M4-LOCALIZATION.md)：贡献者契约、合并语义、校验与防御、
  运行时 API、已知边界（菜单路径受 Unity 限制不可本地化、RTL 未验证、翻译平台未接）。
- M4 一键 ignore 模板（支柱四）：菜单 `Window ▸ Git ▸ Ignore Templates…`（主窗口工具栏同入口）。
  刻意独立于 `GitSession`——它只碰工程根 `.gitignore`，因此 git 缺失或项目未初始化时同样可用。
  - 模板库三来源：包内 `Editor/Templates/BuiltIn/`、项目内 `.gitui-ignore-templates/`、
    个人 `UserSettings/GitBetterGui/IgnoreTemplates/`；同名 id 由高优先级**整份**覆盖。
  - 贡献一个模板 = 一个 `<id>.gitignore`（首行必须为用途注释）+ 可选 `<id>.meta.json`
    （双语名/描述、tags、order）。没有清单文件，也不需要改代码。
  - 写入默认**合并**：只追加缺失规则、既有内容原样保留、已存在的规则行绝不重复写入；
    覆盖模式先备份 `.gitignore.bak`；右侧预览显示对当前 `.gitignore` 的**真实计算结果**；
    同一模板重复写入是 no-op。
  - 窗口内可把当前 `.gitignore` 导出为项目共享模板（缺首行注释时自动补），作为提 PR 的捷径。
  - 内置 4 套：`unity-standard`（推荐默认）、`unity-minimal`、`unity-ide`、`unity-assetstore`。
  - 冒烟入口 `KF.GitUI.IgnoreTemplateSmokeTest.Run`：发现/元数据/块级合并/幂等/覆盖+备份/来源优先级/
    导出/`ui.templates.*` 键的双向完备性（缺失与死键都判失败）。
- 贡献渠道（此前完全缺失）：`.github/PULL_REQUEST_TEMPLATE.md`、`.github/ISSUE_TEMPLATE/`
  （bug 报告 / 功能请求 / **贡献 ignore 模板**表单）、`CONTRIBUTING.md` 与 `CONTRIBUTING.zh.md`。
- 设计稿 [docs/M4-IGNORE-TEMPLATES.md](docs/M4-IGNORE-TEMPLATES.md)：模板格式契约、加载优先级、
  写入语义、模板质量红线与已知取舍（含"为什么不收全局 `*.pdb`"）。

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