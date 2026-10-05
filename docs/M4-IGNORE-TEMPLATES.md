# M4 · 一键 ignore 模板（设计稿）

> 状态：随 `Editor/Templates/` 一同落地（2026-10）
> 里程碑：M4 支柱四「Unity 原生融合」的一项
> 关联：[ROADMAP.md](../ROADMAP.md) 第 40/53 行、[README.md](../README.md) help wanted 列表

## 1. 目标

让 Unity 开发者**一键**把一份可靠的 `.gitignore` 写进工程根，并让**外部贡献者能轻易提交自己的模板**。

在本次改动之前，仓库只有两条相关事实：upstream api 内嵌了**一个**写死的 Unity `.gitignore`
（`Editor/Api/Api/PlatformResources/gitignore`，在"新建仓库"流程里按硬编码名字 `"gitignore"` 写出），
以及 README 里一条"help wanted"条目。既没有模板承载格式，也没有发现逻辑，更没有 PR 模板——
被邀请来做的贡献者反而无处落笔。本设计把这三件事一次补齐。

## 2. 贡献者契约（唯一需要记住的规则）

一个模板 = 往 `Editor/Templates/BuiltIn/` 丢 **1 个必需文件 + 1 个可选文件**：

| 文件 | 必需 | 说明 |
|---|---|---|
| `<id>.gitignore` | ✅ | 模板正文，纯 gitignore 文本 |
| `<id>.meta.json` | ⬜ | 显示名、描述、标签、排序权重；省略时显示名由 `<id>` 派生 |

`<id>` 为小写 kebab-case（`a–z`、`0–9`、单连字符；`unity-standard`、`studio-2d`），校验逻辑在
`GitIgnoreTemplate.IsValidId`，UI 的"导出模板"与冒烟共用它。

### 2.1 正文格式

- UTF-8、**LF 换行**、文件末尾恰好一个换行（`GitIgnoreWriter.Normalize` 会归一化，但请直接满足）。
- **第一个非空行必须是以 `#` 开头的用途注释**（冒烟断言此项）。
- 用空行 + 分组注释组织；合并写入时以"注释行/空行"为块边界，**整块追加**并自动剔除已存在的规则行，
  所以分组注释会跟着新增内容一起进入用户的 `.gitignore`。
- 注释**保持简短英文**：它会原样出现在用户的仓库文件里。长篇解释放 `CONTRIBUTING.md` 与本文档，
  不要写进模板正文。

### 2.2 元数据格式

```json
{
  "name": { "zh": "Unity 标准", "en": "Unity Standard" },
  "description": { "zh": "一句话中文说明", "en": "One-line English description" },
  "tags": ["unity", "standard"],
  "order": 100
}
```

字段全部可选。`order` 越小越靠前（内置模板 100/110/120/130，自定义默认 1000）。
`name`/`description` 为双语对象；`GitIgnoreLocalizedText.Pick()` 当前固定优先英文
（因为界面文案目前只有英文键表），M4 的 zh-CN bundle 落地后改为按当前 UI 语言优先。

## 3. 发现与优先级

```
UserSettings/GitBetterGui/IgnoreTemplates/   ← 个人本地（不入版本控制），优先级最高
<工程根>/.gitui-ignore-templates/            ← 项目内共享（可提交，团队复用）
<包>/Editor/Templates/BuiltIn/               ← 内置（随包升级），优先级最低
```

- 同名 `<id>` **整份覆盖**（高优先级赢），不是字段级合并——避免出现"名字来自本地、规则来自内置"的怪态。
- 目录不存在是正常状态，扫不到就是空列表；单个元数据文件损坏只降级该模板（回退 id 派生名），不影响整库。
- 包内路径通过 `PackageInfo.FindForAssembly` 解析真实 `resolvedPath`（file:/git/registry 安装形式都适用），
  失败时退回 Unity 的 `Packages/<name>` 虚拟路径。

## 4. 写入语义

目标固定为**工程根 `.gitignore`**（`GitIgnoreTemplateLibrary.GitIgnorePath()`）。

| 场景 | 行为 |
|---|---|
| 目标不存在 | 新建，内容 = 模板正文 |
| 目标已存在 + 默认**合并** | 逐块追加**缺失**规则；已存在的规则行不重复写入；既有内容原样保留在最前 |
| 目标已存在 + 显式**覆盖** | 先备份为 `.gitignore.bak`，再整份替换 |
| 幂等 | 同一模板连写两次，第二次为 no-op（`NothingToAdd`） |

写入一律 UTF-8 无 BOM + LF。UI 右侧显示的**不是模板原文，而是真实计算结果**（对当前 `.gitignore` 实算），
所以"会写成什么样"在点按钮之前就可见。

## 5. 界面入口

- 主窗口工具栏：`Ignore Template…` 按钮（[GitWindow.cs](../Editor/GitWindow.cs)）。
- 独立菜单：`Window/Git/Ignore Templates…` —— 刻意不依赖 `GitSession`，git 缺失或项目未初始化时同样可用。
- 窗口：左列模板（显示名 + 来源标签），右侧结果预览，底部写入模式下拉 + 写入 / 导出 / 打开项目模板目录 / 重新扫描。

### 5.1 导出（贡献者的捷径）

窗口里的 *Export current .gitignore as template…* 会把当前工程的 `.gitignore` 导出到
`.gitui-ignore-templates/<id>.gitignore`，并**自动补上用途注释**（首行不是注释时），
使导出物直接符合模板格式——把它复制进 `Editor/Templates/BuiltIn/` 就能提 PR。

## 6. 模板质量红线

模板会被一键写进别人的仓库，因此：

- **禁止**忽略 `Assets/` 下的真实资源、真实 `*.meta`、`ProjectSettings/`、`Packages/manifest.json`、`.gitattributes`。
- 只收**确信**存在的路径。无法确证的第三方 SDK/IDE 目录一律不收（例如 `[Aa]ssets/Plugins/Editor/JetBrains*`
  这类上游模板里出现过的条目，本仓库不确证就不写）。
- 明确无害的自有取舍，已记录在案：

| 取舍 | 理由 |
|---|---|
| 不收全局 `*.pdb` / `*.mdb`（仅留 `*.pidb` / `*.pidb.meta`） | 上游模板有；但 `Assets/Plugins` 下第三方 DLL 随附的调试符号可能是要提交的内容，全局忽略会静默漏提交 |
| 不收 `*.tmp` | 非 Unity 生成物，通用性差，可能命中用户自己的临时但有意义文件 |
| OS 垃圾只收 `.DS_Store` / `Thumbs.db` | `desktop.ini` 有人故意提交以自定义文件夹图标 |
| 生成目录用 `[Ll]ibrary/` 双写、**不做锚定** | 与 Unity 官方一致：双写兼容大小写敏感文件系统；不锚定意味着 `Assets/MyTool/Build/` 这类同名真实目录也会被命中，模板首行注释已提示改用 `/[Bb]uild/` |
| `unity-minimal` 不含 `*.unitypackage` | 用户可能故意提交分发包 |

## 7. 验证

```bash
# 包内冒烟（无 UI，覆盖发现/元数据/合并/幂等/覆盖+备份/优先级/导出/i18n 键完备性）
Unity -batchmode -nographics -projectPath <宿主项目> \
      -executeMethod KF.GitUI.IgnoreTemplateSmokeTest.Run

# 既有 api 链路冒烟
Unity -batchmode -nographics -projectPath <宿主项目> \
      -executeMethod KF.GitUI.ApiSmokeTest.Run -quit
```

`IgnoreTemplateSmokeTest` 还会双向校验 `ui.templates.*` 键：**声明了但表里没有**（会返回键名告警）
与**表里有但没人用**（死键）都算失败。

## 8. 未做（后续）

- 多模板组合勾选（一次写入 minimal + ide + assetstore）——当前靠"再选一个模板再写一次"，合并语义天然支持叠加。
- 模板正文的行级 diff 展示（当前是结果全文预览）。
- zh-CN 界面 bundle（M4 本地化里程碑，独立 PR）。
- 从远端拉取社区模板索引（先让本地贡献路径好用，再谈分发）。
