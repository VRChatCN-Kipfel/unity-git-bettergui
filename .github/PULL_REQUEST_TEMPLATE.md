<!--
Thanks for contributing to unity-git-bettergui.
This repository has no CI — nothing runs automatically for your pull request.
Delete every section below that does not apply to your change.
One pull request carries one change type only.
-->

## Change type

Tick exactly one. One PR = one change type.

- [ ] `feat` — a new user-visible capability
- [ ] `fix` — a bug fix
- [ ] `docs` — documentation only
- [ ] `templates` — a one-click `.gitignore` template under `Editor/Templates/BuiltIn/`
- [ ] `i18n` — a localization pack under `Editor/I18n/BuiltIn/` (or a translation fix)
- [ ] `perf` — performance work with no intended behavior change

## Related issue

Closes #

## What changed

<!--
The user-visible effect and the files you touched. Name the window, menu path or
component so a reviewer knows where to look.
-->

## How this was verified

Verification is manual: this repository has no CI and no status check to wait for.
Describe exactly what you ran and what you saw.

- Unity version used (2022.3 LTS or newer):
- Host Unity project (path, and how the package is referenced — `file:` or git URL):
- Smoke command and its result (exit code + log tail):

  ```
  Unity -batchmode -nographics -projectPath <host project path> -executeMethod KF.GitUI.ApiSmokeTest.Run -quit
  ```

- For any change under `Editor/Templates/`, also run:

  ```
  Unity -batchmode -nographics -projectPath <host project path> -executeMethod KF.GitUI.IgnoreTemplateSmokeTest.Run -quit
  ```

- For any change under `Editor/I18n/`, also run:

  ```
  Unity -batchmode -nographics -projectPath <host project path> -executeMethod KF.GitUI.I18nSmokeTest.Run -quit
  ```

- Log lines (the `[gitui]` and `[api-smoke]` tags are the ones a reviewer looks for):
- Manual check in the editor (what you clicked, what you expected, what you got):

## Checklist

- [ ] One pull request contains one change type only
- [ ] Every new Unity asset file is committed together with the `.meta` file Unity generated
- [ ] No code was copied from the GPL-3.0 projects UniGit / UnityGitUI (layout and data-flow ideas only)
- [ ] No new external dependency was introduced (this is a single dependency-free UPM package; `package.json` has no `dependencies`)
- [ ] Any third-party code or text is MIT or Apache-2.0 compatible and its notice is preserved
- [ ] Commit messages use an English conventional prefix plus a Chinese description, e.g. `fix(poll): 自动刷新轮询不再阻塞编辑器主线程`

---

## 中文说明

<!-- 本节供中文读者快速对照，保留即可。 -->

- **变更类型**：一个 PR 只交一种类型的变更（`feat` / `fix` / `docs` / `templates` / `i18n` / `perf`）。
- **验证方式**：本仓库没有 CI，不会自动跑任何检查，也没有需要等待的状态。请在 PR 里写清 Unity 版本、宿主工程、执行的冒烟命令与结果；改动 `Editor/Templates/` 时请额外跑 `KF.GitUI.IgnoreTemplateSmokeTest.Run`，改动 `Editor/I18n/` 时请额外跑 `KF.GitUI.I18nSmokeTest.Run`。日志里 `[gitui]` 与 `[api-smoke]` 是关键行。
- **检查清单**：新增的 Unity 资源文件必须连同 Unity 生成的 `.meta` 一起提交；禁止抄 UniGit / UnityGitUI（GPL-3.0）的代码，只能参考其布局与数据流思路；不要引入外部依赖。
- **提交消息**：英文 conventional 前缀 + 中文描述，例如 `fix(poll): 自动刷新轮询不再阻塞编辑器主线程`、`perf(poll): 合并每轮的两次 git status`。
