# unity-git-bettergui —— 产品路线图（ROADMAP）

> 项目定名：**unity-git-bettergui**（GitHub 仓库，org: VRChatCN-Kipfel）
> 状态：M1、M2、M3 已收口（2026-10），M4 待开工
> 日期：2026-08
> 原则：一个 PR 只交一种类型的变更

---

## 1. 项目定位

在 Unity 编辑器内提供一个 **JetBrains 级用户体验的 Git 工具窗**——不是复刻 JetBrains
的 Feature 清单，而是借其优秀实现作为载体，回到 git 本身的体验上做到极致，并叠加
Unity 原生深度集成（支柱四）这一独有增量。

### 一句话目标
> **从任何现有 Unity Git 插件换过来，没有人回头。**

### 非目标（明确不做，防止范围蔓延）
- ❌ Shelf / Changelists：那是 JetBrains 的工作流霸权，不是 git 体验本身（git stash
  + commit 流程够用，我们把它做顺手即可）
- ❌ 本地持久化搜索索引：git log 全量语义已把提交拿进内存（5 万提交 ≈ 5MB 轻载行），
  筛选 = 内存遍历几毫秒。索引 + 持久化 + 失效 + 重建是纯负担
- ❌ 自研 3-way 合并引擎（M3 只做可编辑的比较视图 + 复制，不引入真实合并器）
- ❌ 不捆绑 git 二进制：初始化 `git version` 探活，缺失引导用户安装官方 git
- ❌ 不做完整商业级 Git 客户端（不进 Asset Store 商业化，M0 阶段定位开源 MIT 回馈社区）

---

## 2. 最终功能目标（M0 收敛版，全部对应 JetBrains 蓝图出处）

| # | 功能 | 说明 | 蓝图参考 |
|---|---|---|---|
| 1 | **三栏窗口 + 提交图谱** | 上/左图谱表格（泳道主线靠左）+ 右列（上改动文件树 / 下提交详情 full message）；Bek 排序、refs 行内标签（HEAD→本地→tracked→tags）、长边箭头、折叠线性段 | vcs-log graph 全模块 |
| 2 | **右键菜单体系** | 文件 / 目录 / 提交三语境；静态骨架 + update() 动态过滤（按选中/仓库状态换项） | git4idea XML + GitFileActionGroup |
| 3 | **Commit 流程** | 勾选树（多文件部分提交，GitStatusEntry 双列 X/Y）+ 消息编辑器 + 一键提交；**已落地**：GitCommitTaskEx（--amend/--signoff/--no-verify）与 gpg stderr 探测（"gpg failed to sign the data" 专属提示）；**M3**：提交模板/最近消息、--cleanup=strip（可选） | AbstractCommitWorkflow + GitRepositoryCommitter |
| 4 | **Diff 层** | **现状**：name-status 预览（GitDiffNameStatusTask + CompareWindow）；**M3**：内容级（行内词级高亮、side-by-side/unified）、hunk 级 stage/unstage/revert、双击文件联动（ChangesTree.ItemChosen 已就绪无订阅者） | vcs-log GitLogDiffHandler 语义（platform/diff 缺失，自绘等价） |
| 5 | **3-way 冲突** | **现状**：冲突走 stderr 提示 + GitStatus 的 Unmerged(U) 条目；**M3**：ours/theirs/result 三栏 + 冲突导航 + 整侧接受（checkout --ours/--theirs，需扩 GitCheckoutPathsTask 或新任务） | GitMergeProvider |
| 6 | **分支管理** | **已落地（M2）**：左侧常驻 BranchesPanel（分组折叠/持久化/右键动作集：Checkout/从…新建/更新/推送/合并/重命名/删除，上游子菜单无重命名）+ GitTagTask/GitTagDeleteTask + 图谱分支筛选下拉（All/Current/refs 单选）；**M3**：Compare with Branch 内容级对比、remote 管理 UI、标签推送/远程标签、分支级 ahead/behind、reflog 最近分支 | GitBranchesTreeModel + GitBranchesSearcher |
| 7 | **支柱四 · Unity 原生融合** ⭐ | Project 窗口资产状态图标 + 目录聚合色块；`.meta` 与 prefab 冲突专属提示；"一键忽略"Unity 生成目录模板；**中文本地化一等公民** | Unity 特有（JetBrains 无此层） |
| 8 | **git 探活** | `git version` 探活 + 缺失引导安装官方 git（git-scm.com） | GitVersionTask + GitInstaller 探活半段 |

---

## 3. 里程碑

| 里程碑 | 内容 | 验收标准 |
|---|---|---|
| **M0** | 本 ROADMAP + 仓库初始化 + 许可合规框架（本阶段） | 仓库可提交、文档齐、技能已归档 |
| **M1** | 新包脚手架（自带 api 切包）＋ 三栏窗口骨架 + 图谱（Bek/refs/折叠）+ 行内选择联动 + git 探活 | 打开任意项目可见三栏、图谱正确、点提交右栏联动（已收口：管线引擎/表格渲染/详情/refs 标签/指纹自动刷新） |
| **M2** | 窗口内右键菜单三语境 + Commit 流程 + 分支管理（左侧常驻面板） | 必达：窗口内右键（图谱行=提交语境、改动树=文件&目录语境、分支面板）、完整走一次提交流程（勾选→commit→自动刷新）、分支面板（过滤+checkout/新建/删除/重命名/合并/更新/推送）；**后置→M3**：Compare 内容级、rebase 非交互式（Reword/Squash/Drop；落地索引见 `docs/M2-SOLUTION.md §6`；交互式 rebase 序列编辑器→M4）、提交模板/最近消息、Uncommit、Cherry-Pick、分支级 ahead/behind、reflog 最近分支、remote 管理、标签推送。**GPG pinentry 向导→M4**（M2 的 stderr 探测 + CommitGpgHint 已是兜底）。**已收口（2026-09/2026-10）**：三语境右键、Log\|Commit Tab 提交流程（amend/signoff/no-verify + gpg 探测）、左侧常驻分支面板（分组折叠/持久化/右键动作集/上游子菜单无重命名/tag 增删）、图谱分支筛选下拉（单选，按 ref 祖先过滤）、merge 详情分节、泳道行压缩、路径归一化、箭头点击跳转；冒烟含端到端提交与分支过滤、菜单结构断言 |
| **M3** | 承接 M2 后置全部 + 内容级 Diff/3-way：Compare 内容级 + Diff 词级高亮与 hunk 操作 + 3-way 冲突视图（ours/theirs/result，整侧接受）+ 文件历史/blame + rebase 非交互式 + Uncommit + Cherry-Pick + 提交模板/最近消息 + remote 管理 + 标签推送/远程标签 + 双击文件联动（P1 优先：remote 管理、标签推送、提交模板/最近消息、Uncommit）｜**调研定版：`docs/M3-SOLUTION.md`（2026-10：本地 git 实测 + JetBrains 源码 + doko-search 联网；词级高亮=自研行对 LCS、hunk=git apply 三态、3-way=`ls-files -u`+`show :N:`、rebase 冲突=`## HEAD (no branch)` 判定）**｜**已收口（2026-10，主线 0d5501f）**：内容级 DiffViewer（unified 单栏 + 词级 `<mark>/<s>` 高亮 + 大文件折叠）、hunk 级 stage/unstage/revert（git apply 三态，LF patch）、CompareWindow 内容级、rebase 非交互式（任务 + 冲突判定 + 分支菜单）、3-way 冲突视图（三 stage + 整侧接受 + rebase 标签对调 + 工具栏冲突徽标）、P1 四件套（remote 管理 / 标签推送・远程标签 / 提交模板+最近消息 / Uncommit）、P2（分支级 ahead-behind 徽标 / reflog 最近分支 / Cherry-Pick / blame）、双击文件联动；冒烟扩至 #39 全绿 | 可完成一次带冲突解决的真实 merge；远程操作链路（push/fetch/pull）完整可用；rebase 非交互式可完成一次变基 |
| **M4** | 支柱四：Project 融合 + 资产语义 + 一键 ignore 模板 + 中文本地化 | 字资产层状态可见、中文 UI 完整 |

> 工程纪律：每个里程碑内部按"一个 PR 一种功能"拆分；图谱算法正确性管线先行，
> 美观/性能层（Bek 二期精化、缓存）按需后置但不砍目标。

---

## 4. 技术选型

| 层 | 选择 | 理由 |
|---|---|---|
| 引擎 | `com.spoiledcat.git.api`（MIT，git 命令行封装器）**git subtree 内嵌**（源 = fork 发布分支 `split/api`） | 与 UI 解耦、活跃、无 libgit2sharp 的 HTTP-only push 限制；fork main 已含 parents 修复 + 中文本地化 |
| 依赖策略 | **单包零依赖**：api 整体物理内嵌，`package.json` 无 dependencies 字段 | UPM 禁止包间 git 依赖（仅 manifest 层）；registry 方案需自建源；submodule 在 UPM 安装时不会 init → 只有 subtree 满足"导入尾包即齐全"（用户拍板） |
| UI | **UI Toolkit**：`TwoPaneSplitView` 嵌套三栏 + `generateVisualContent` 自绘图谱；改动树用 UITK `TreeView`（2022.2+，用户拍板 2026-09） | GraphView 是实验性 API 且 node/port 体系不适配泳道图；TreeView 2021.3 无（降级策略见 §8） |
| 图谱算法 | 移植 JetBrains `vcs-log/graph`：PermanentLinearGraph → (Bek) → EdgesInRow → 打印元素 | Apache-2.0 可移植；C# 值类型无装箱更简单 |
| 数据双通道 | 图谱轻载行（%H%P + %D ref→commit）+ 文件列表惰性 diff-tree + 增量刷新 | 5 万提交 ≈ 5MB，全量可接受 |
| git 依赖 | 系统 git，`git version` 探活 + 引导安装 | 零捆绑、零分发合规负担（用户拍板） |
| 本地化 | 资源包（仿 GitBundle）内置中文 bundle | 主要用户群体 |

### 引擎层改动点（parents 修复已并入 fork main；状态更新 2026-10 M3 收口）
1. ⭐ `LogEntryOutputProcessor` parents 全量保留（当前单亲为空、octopus 丢线）→ **已在 fork main（2026-08-24 merge f81596d6 / PR #48）**
2. 内容级 `GitDiffTask`（git diff --no-ext-diff + unified/词级通道）→ **已落地（GitExt/GitDiffTask + Diff/UnifiedDiffParser + Diff/WordDiff 自研行对 LCS + Diff/DiffRichText + Diff/DiffRows + Diff/DiffViewer，M3）**
3. `GitForEachRefTask`（for-each-ref，兼容 packed refs，含 `%(upstream:track)` 第 5 列）→ **已落地（GitExt/，非子树内容）**，后续以 PR 形式回馈 upstream
4. M3 新增 GitExt 任务（均非子树内容，后续可回馈 upstream）：`GitApplyTask`/`GitPatchBuilder`（hunk 三态）、`GitRebaseTask`/`GitPullRebaseTask`、`GitCherryPickTask`、`GitBlameTask`/`GitBlame`（porcelain 解析）、`GitReflogTask`、`GitTagPushTask`/`GitRemoteTagDeleteTask`（refspec）

### 依赖链路（决策记录：2026-08-24，subtree 方案已实测验证）
- ❌ **包间 git 依赖**：UPM 官方明文禁止 package.json 内写 git URL（仅项目 manifest 层支持）→ 用户设想"尾包自动拉 fork + fork 再拉上游"在第一环就断
- ❌ **submodule**：UPM 安装 git 包不执行 `submodule update --init`，子模块目录为空、交付即坏
- ❌ **npm registry 版本链**：需自建 registry（spoiledcat 自己的 registry.spoiledcat.com 即此模式），起步重
- ✅ **git subtree 物理内嵌**：单包零依赖（api 本身即零依赖，editor.tasks 也已内嵌于 api 包内）；subtree 源 = fork 的 `split/api` 发布分支；`build.bat` 一条命令增量同步

**Git subtree 方案落地细节（2026-08-24 实测）**
- **为什么不是整树导入**：`subtree add` 搬的是"源分支的整棵树"。fork main 树根是完整仓库（37MB，含 UI 源码/tests/build 脚本）→ 直接指 main 会把上游 UI 一起编译进来。正解：用 `git subtree split --prefix=src/com.spoiledcat.git.api -b split/api main` 在 fork 上切出"树根=api包"的发布分支（14 个提交、含 parents 修复、editor.tasks 内嵌），subtree 指向它
- **`--squash` 的含义**：压缩的是"上游历史在我们仓库里的记录"而非内容。每个 subtree 提交形如 `Merge commit '...' as 'Editor/Api'` + body 带 `git-subtree-dir` / `git-subtree-split <hash>` 锚点；上游 14 个提交折成 1 个内容快照，历史干净
- **不会不同步**：`subtree pull` 读 `git-subtree-split` 锚点，fetch 上游新提交后**只增量合并锚点之后的部分**。实测：上游 +1 提交 → 我们仓库只多 1 个 squash 提交，锚点随之更新
- **已知坑 1（LFS 指针）**：fork 的 `*.dll/so/bundle` 用 Git LFS 存储，但 `git subtree split` 只搬
  blob（=指针文本）不触发 smudge → 子树落盘是 **LFS 指针而非真实二进制**，Unity 编译报
  `namespace 'sfw' could not be found`。修复：`tools/materialize-lfs.ps1` 在 split/api 分支顶追加
  "实体化提交"（用独立 index + read-tree/update-index/write-tree/commit-tree 把 9 个指针 blob 换成
  本地 LFS 缓存中的真实文件），已并入 `build.bat split` 流程。**注意**：此坑一旦遇 library 缓存
  旧状态，还需删除测试工程 `Library/` 再重新导入
- **已知坑 2（检查点）**：验证包能否编译的测试工程在 `verify/unity-verify`（git 已忽略；
  注意：路径中**不能**含点前缀目录名——`.temp/unity-verify` 会被 Unity 以
  ".temp is not a valid directory name" 拒绝，必须 <根>/<单层>/unity-verify 形态），
  manifest 用 `file:../../../Packages/com.kf.gitui` 相对引用；批处理验证：
  `Unity.exe -batchmode -nographics -projectPath verify/unity-verify -executeMethod KF.GitUI.GitWindow.SmokeTest`
  （cmd /c 重定向捕获输出 + -logFile，勿用 pwsh 直接捕获 GUI 程序输出）
- **已知坑 3（meta 行尾空格，2026-08-25 实测）**：fork 源头的 `.meta` 被剥过行尾空格——
  `userData:` / `assetBundleName:` / `assetBundleVariant:` 冒号后无空格且文件无结尾换行。
  Unity YAML 解析器在 EOF 处报 `[Parser Failure ... Expect ':' between key and value]`（约 1/3
  meta 受影响，经 subtree 流入包内）。修复：`tools/normalize-metas.ps1` 幂等补空格/换行
  （只改缺失行、保留原 CRLF/LF），已挂入 `build.bat init/pull`，同步即自愈。
- **已知坑 4（自绘元素高度）**：`generateVisualContent` 的自绘不参与布局，元素在 ScrollView
  里会塌成 0 高、什么都不画（真实窗口"纯文本"根因；冒烟只测布局数学测不到像素）。
  法则：自绘元素必须显式 `style.height = 内容行数 × 行高` + `MarkDirtyRepaint()`，
  冒烟测试同步断言高度非零。

**build.bat 用法**（仓库根，Windows）
| 命令 | 作用 |
|---|---|
| `build.bat split` | 在 fork 上重新 split 发布分支并 push（增量，秒级） |
| `build.bat init` | 首次：把 `split/api` subtree add 进包内 `Editor/Api` |
| `build.bat pull` | 增量同步 `split/api` → 本仓库（自动 fetch） |
| `build.bat`（无参） | 全流程：split + pull |

---

## 5. 数据流架构（对齐 JetBrains 分层）

```
git 命令层（GitProcessTask + OutputProcessor，api 自带）→ 已解析模型
  → 数据缓存/事件（Repository + CacheContainer）
  → 图谱（PermanentLinearGraph → GraphLayout → 可选 Bek/Collapsed → EdgesInRow → 打印元素）
  → UI 渲染（图谱自定义元素 + 文件树 + 详情面板 + 右键菜单）
交互链路：表格选择 → 去抖 → 异步加载 metadata+refs → 双路更新文件树与详情
           （多选只显示前 50 个 + "Showing N commits" 溢出提示）
操作侧链路（M2，2026-08-31 审查补充）：status(勾选树) → add / reset HEAD / commit
           → 指纹失效 → 重载 history+refs（InvalidateCaches + 指纹自动刷新，窗口 1.5s 轮询）
```

---

## 6. 目录结构规划（目标）

```
<repo>/                      # unity-git-bettergui（容器仓库，一个 PR 一种变更）
├── docs/ROADMAP.md          # 本文档
├── Packages/
│   └── com.<vendor>.gitui/  # UPM 包（Editor/ 代码 + Runtime 空壳/asmdef）
│       ├── Editor/          # 三栏窗口、图谱自绘、右键菜单、Commit 面板…
│       ├── Editor/Api/      # git subtree 内嵌 com.spoiledcat.git.api（源=fork split/api，MIT）
│       ├── package.json     # SPDX license 字段；无 dependencies（零依赖单包）
│       ├── LICENSE.md
│       ├── Third Party Notices.md
│       └── CHANGELOG.md
├── .dsh/                    # 本地技能 + 对照源码（git 已忽略）
└── .gitignore
```

---

## 7. 许可与合规（红线）

- 新插件整体 **MIT**，UPM 发布含 LICENSE.md + Third Party Notices.md（第三方许可声明）
- `com.spoiledcat.git.api`：MIT ✅ 可直接自带；保留其版权声明（GitHub/Unity/Andreia Gaita）
- JetBrains intellij-community：**Apache-2.0** ✅ 可参考/移植，保留版权声明；对照源码在
  `.dsh/.temp/ij-intellij-community`（移植细节见技能 `unity-git-ui-blueprint`）
- UniGit / UnityGitUI：**GPL-3.0** ⛔ 仅参考布局/数据流，代码禁抄
- Gitostory / gitgraph.js / git-graph-drawing：MIT/Unlicense ✅ 可放心参考

---

## 8. 风险与开放问题

| 风险 | 缓解 |
|---|---|
| .NET 工程文件被 .gitignore 忽略（见注释） | 未来手写 csproj/sln 时改用白名单 |
| api 层与 upstream 分叉 | 数据层小修复逐个提 PR 回馈 upstream（parents/DiffTask/ForEachRefTask），分叉可控 |
| Unity 版本兼容面 | 目标 2021.3+（UI Toolkit 稳定版）；改动树 TreeView 需 2022.2+（用户拍板 2026-09），2021.3 降级为扁平 ListView（M4 前不做），CI 矩阵验证 |
| 首次提交 gpg 签名 | 按环境 gpgsign=true，如沙箱签名失败改本地提交说明 |
| 提交失败 stderr 探测 | ProcessTask 把 stderr 写入 `task.Errors`（源码核实）→ gpg 失败匹配 "gpg failed to sign the data" 给专属提示；batchmode 冒烟仓必须 commit.gpgsign=false（无人值守签名会挂） |
| **api 任务三坑（2026-09 实测）** | ① `TaskBase<TResult>` 用 `new` 重声明 RunSynchronously（内联跑 Wrapper.Run）；经 `ITask`/`IProcessTask` **接口**调用会绑定旧 void 调度版——进程不启动但 Successful=True（静默空转）→ 一律用 `IProcessTask<T>`/具体类型调用（GitSession.RunOp 已强制泛型）；② api `ProcessEnvironment.Configure` 未设置 WorkingDirectory（源码注释）→ 任务进程 cwd 不是仓库目录，GitSession.Prepare 在 Configure 后钉 `Wrapper.StartInfo.WorkingDirectory`=仓库根（仓库≠编辑器工程时必需，e2e 冒烟即此场景）；③ Windows 上 git 松散对象文件只读 → `Directory.Delete(recursive)` 拒绝，清理前先 `File.SetAttributes(Normal)` |
| **M3 新增风险（2026-10 调研实证，详见 M3-SOLUTION）** | ① `git apply` 的 patch 文件**必须 LF 行尾**（PowerShell Out-File 写 CRLF 时 exit=0 静默不生效）+ 末尾必换行 + `\ No newline` 标记必留 → GitPatchBuilder 用 IO.WriteAllText 写临时文件；② `--word-diff=porcelain` 协议有空行增删无标记/行首空白进 context/`~` 无法归属行/CJK 整段一词四坑 → 词级高亮**自研行对 LCS**（porcelain 仅测试 oracle）；③ git progress 走 stderr → PowerShell 误报 NativeCommandError，RunOp 失败判定=非零退出码而非 stderr 非空；④ rebase 冲突时 `checkout --ours` 取"被变基提交侧"→ 3-way UI 须按状态交换 Yours/Theirs 标签 |

---

*文档随里程碑迭代更新；技术细节以 `.dsh/skills/unity-git-ui-blueprint` 为权威参考。*