# 为 unity-git-bettergui 贡献代码

[English](CONTRIBUTING.md) | 简体中文

感谢参与。本仓库**本身就是** UPM 包：一个运行在 Unity 编辑器里的 Git 工具窗（C# + UI Toolkit），
三栏一体 —— **提交图谱 | 改动文件 | 提交详情** —— 整体 MIT 许可，当前版本 0.1.0，
要求 Unity 2022.3 LTS 及以上。

## 基本纪律

1. **一个 PR 只交一种类型的变更。** 功能 PR 里不要顺手重写文档或夹带无关修复，请拆成多个 PR。
   这是项目明文原则（“一个 PR 只交一种类型的变更”），在里程碑尺度上同样适用。
2. **每个新增的 Unity 资源文件都必须连同 `.meta` 一起提交。** `.meta` 由 Unity 生成；
   缺少它，包在别人机器上就是坏的。不要提交没有 `.meta` 的资源文件，也不要手改 `.meta` 去“修问题”。
3. **不要引入外部依赖。** `package.json` 没有 `dependencies` 字段，包把需要的引擎源码内嵌在内。
   请保持“单包零依赖”。
4. **遵守许可红线** —— 见下文〈许可〉。
5. 小而聚焦的提交，配一条读得懂的提交消息，远比一次改一大片更好审。

## 开发环境

- **Unity 2022.3 LTS 及以上。** 这是编辑器工具窗，不是运行时包。
- **PATH 中有 git。** 本包不捆绑 git，而是通过 `git version` 探活；缺失时引导用户安装 git。
- **Windows 为一等公民。** macOS 未测试，但未刻意屏蔽。
- 除 git 自身外不发起任何网络请求。

## 把包接进宿主 Unity 工程

本仓库就是 UPM 包（`package.json` 在仓库根目录，包名为 `com.kf.gitui`），
因此**不要**把文件复制进 `Packages/`。

**方案 A —— git URL（先看一眼）。**

`Window ▸ Package Manager ▸ + ▸ Add package from git URL…`，粘贴：

```
https://github.com/VRChatCN-Kipfel/unity-git-bettergui.git
```

需要标签版本时在末尾追加 `#v0.1.0`。这种方式只能用窗口，不能改代码。

**方案 B —— 本地 `file:` 引用（推荐的改动循环）。**

1. 把本仓库克隆到宿主 Unity 工程之外的路径。
2. 在宿主工程打开 `Packages/manifest.json`，把依赖指向你的克隆：

   ```json
   {
     "dependencies": {
       "com.kf.gitui": "file:E:/path/to/unity-git-bettergui"
     }
   }
   ```

3. 切回 Unity，包会被导入，`Window ▸ Git (Better GUI)` 即可用；你在克隆里做的改动会在
   下次刷新 / 重新导入时生效。
4. 打开一个 git 工作树工程（或先 `git init`）—— 图谱立即呈现。

## 冒烟测试 —— 由你自己执行

**本仓库没有 CI。** 你的 PR 不会触发任何工作流，没有状态检查会变绿，也没有任何东西需要等待。
验证是手动的，执行者就是你自己。

把包接进宿主 Unity 工程后，在 shell 里以批处理模式运行编辑器
（`<宿主工程路径>` 指那个 Unity 工程）：

```
Unity -batchmode -nographics -projectPath <宿主工程路径> -executeMethod KF.GitUI.ApiSmokeTest.Run -quit
```

当改动涉及 `Editor/Templates/`（一键 ignore 模板）时，另外再跑：

```
Unity -batchmode -nographics -projectPath <宿主工程路径> -executeMethod KF.GitUI.IgnoreTemplateSmokeTest.Run -quit
```

- 把日志末尾与退出码贴进 PR。
- 编辑器输出带标签：`[gitui]` 是工具窗诊断，`[api-smoke]` 是冒烟输出 —— 审阅者主要看这些行。
- 如果你的 shell 吞掉了编辑器输出，加上 `-logFile <路径>`，再去读那个文件。
- 冒烟只断言布局数学与管线行为，**不覆盖像素渲染** —— 请另外打开窗口，手工走一遍你改动的路径。

## 贡献一键 ignore 模板

模板贡献就是**往 `Editor/Templates/BuiltIn/` 丢两个文件（第二个可选）**。
这就是全部格式，不需要写 C#。

**第 0 步 —— 最省事的路径。** 工具窗支持“把当前工程的 `.gitignore` 导出为项目共享模板”，
导出物已经是模板格式（若首行不是用途注释，导出会自动补上）。拿这个文件改一改注释，
直接进入第 1 步 —— 你不用手写忽略模式。

**第 1 步 —— 放文件。**

必需：`Editor/Templates/BuiltIn/<id>.gitignore`

- 纯 gitignore 文本，UTF-8，LF 换行，末尾留一个换行；
- `<id>` 为小写 kebab-case，例如 `unity-standard`；
- **第一个非空行必须是用途注释**，例如
  `# Unity standard ignores (Library/, Temp/, build outputs, IDE artifacts)`。

可选：`Editor/Templates/BuiltIn/<id>.meta.json` —— 若省略，显示名由 `<id>` 派生、描述为空：

```json
{
  "name": { "zh": "Unity 标准", "en": "Unity Standard" },
  "description": { "zh": "…", "en": "…" },
  "tags": ["unity", "standard"],
  "order": 100
}
```

- 字段全部可选；
- `order` 越小越靠前 —— 内置模板用 100、110、120…；
- `tags` 为小写短标签。

**不要手写 `.meta` 文件**：它由 Unity 生成，与新增文件一并提交。若你本地 Unity 尚未生成，
在 PR 里说明即可，审阅者会补齐。

**第 2 步 —— 本地验证。** 先跑上面的模板冒烟命令，再打开窗口，把模板应用到一个临时 git 工作树工程。
应用会在工程根写入 `.gitignore`：

- 目标 `.gitignore` 不存在则新建；
- 已存在时默认**合并** —— 只追加缺失的忽略模式，保留原有内容；
- 窗口里也可显式选择**覆盖**，覆盖前会自动备份为 `.gitignore.bak`。

凡适用于你的模板的路径，都请实际验一遍。

**第 3 步 —— 提 PR。** 变更类型选 `templates`，提交两个新文件加上 Unity 生成的 `.meta`，
并在 PR 描述里贴上冒烟结果。若你只想把内容交出来，改用
**Contribute a one-click ignore template** 表单开 issue，维护者可以代为落地。

作为参考，模板加载优先级（用户侧）是：用户本地 `UserSettings/GitBetterGui/IgnoreTemplates/`
▸ 项目内 `.gitui-ignore-templates/` ▸ 包内内置 `Editor/Templates/BuiltIn/`。
同名 id 时高优先级覆盖低优先级 —— 所以本地文件总会盖过你的内置模板，测试时尤其要注意。

### 可直接复制的骨架

```
Editor/Templates/BuiltIn/unity-standard.gitignore
```

```gitignore
# Unity standard ignores (Library/, Temp/, build outputs, IDE artifacts)
[Ll]ibrary/
[Tt]emp/
[Oo]bj/
[Bb]uild/
[Bb]uilds/
[Ll]ogs/
[Uu]ser[Ss]ettings/
*.csproj
*.sln
```

```
Editor/Templates/BuiltIn/unity-standard.meta.json
```

```json
{
  "name": { "zh": "Unity 标准", "en": "Unity Standard" },
  "description": {
    "zh": "Unity 生成目录与常见构建、IDE 产物",
    "en": "Unity-generated folders plus common build and IDE artifacts"
  },
  "tags": ["unity", "standard"],
  "order": 100
}
```

## 提交消息与分支命名

- **提交消息：** 英文 conventional 前缀 + 中文描述。在用类型为 `feat`、`fix`、`docs`、
  `templates`、`perf`。
  - `fix(poll): 自动刷新轮询不再阻塞编辑器主线程`
  - `perf(poll): 合并每轮的两次 git status`
- **分支命名：** 短 kebab-case，以类型作前缀，例如 `fix/non-blocking-poll`。
- 一条提交只说一件事；无关改动拆成独立提交、独立 PR。

## 拉取请求（PR）

- 按 PR 模板（`.github/PULL_REQUEST_TEMPLATE.md`）填写，不适用的段落直接删掉：
  变更类型、关联 issue、变更说明、验证方式、检查清单。
- 一个 PR 只含一种类型的变更。
- 没有 CI 需要等待；维护者靠读 diff 和复跑你的冒烟命令来审阅。预期会有关于范围、
  `.meta` 文件与许可兼容性的提问 —— 这三条是审阅主线。
- 描述里请如实说明哪些地方你没测。

## Issue

- **缺陷报告**用 bug report 表单：Unity 版本、包版本或提交号、操作系统、git 版本、
  复现步骤、期望与实际、以及 `[gitui]` / `[api-smoke]` 日志行。窗口截图有帮助，可选。
- **功能请求**用 feature request 表单：需要选择最接近的路线图里程碑（M1–M4），
  以及你是否愿意自己实现。
- **ignore 模板提案**用专门的模板表单，即使你暂时不打算写代码。
- 表单不适配的场景可以直接开空白 issue（已启用）。
- 建议先扫一眼 [ROADMAP.md](ROADMAP.md)：M1–M3 已收口，M4（Project 资产状态、一键 ignore 模板、
  zh-CN 本地化、side-by-side 对比）是社区最受欢迎的方向。

## 许可

- 包整体为 **MIT** —— 见 [LICENSE.md](LICENSE.md)。
- 内嵌引擎 `com.spoiledcat.git.api` 为 **MIT**，需保留其版权声明
  （[Editor/Api/LICENSE.md](Editor/Api/LICENSE.md)）；其依赖 `com.unity.editor.tasks` 同为 MIT
  （[Editor/Api/com.unity.editor.tasks/LICENSE.md](Editor/Api/com.unity.editor.tasks/LICENSE.md)）。
- JetBrains `intellij-community` 参考源码为 **Apache-2.0**：可以移植，但必须保留版权声明。
- **UniGit / UnityGitUI 是 GPL-3.0：禁止抄其代码。** 只可参考布局与数据流思路，
  把它们的实现翻译进本仓库是不允许的。
- 如果你引入第三方代码或文本，请标明许可证，并同步更新
  [Third Party Notices.md](Third Party Notices.md)。

## 行为期望

- issue、PR 与审阅评论中，中英文都可以。
- 请说清改动的用户可见效果，而不只是实现细节。
- 请多一点耐心：因为没有 CI，审阅者要手工复现你的改动。
- 关于范围的争议，请在大改动动手之前先在 issue 里谈定。
