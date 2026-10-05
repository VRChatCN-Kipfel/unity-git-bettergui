# Contributing to unity-git-bettergui

[简体中文](CONTRIBUTING.zh.md) | English

Thanks for helping. This repository is the UPM package itself: a Git tool window for the
Unity Editor (C#, UI Toolkit) with three panes — **commit graph | file changes | commit
details** — licensed MIT, currently version 0.1.0, requiring Unity 2022.3 LTS or newer.

## Ground rules

1. **One pull request carries one change type.** A feature PR must not also rewrite
   documentation or smuggle in an unrelated fix — split it into separate pull requests.
   This is the project's stated principle ("一个 PR 只交一种类型的变更") and it applies at
   milestone scale too.
2. **Commit the `.meta` file with every Unity asset you add.** Unity generates `.meta`
   files; without them the package is broken for everybody else. Never commit an asset
   without its `.meta`, and never hand-edit a `.meta` to "fix" something.
3. **No new external dependencies.** `package.json` has no `dependencies` field and the
   package embeds the engine sources it needs. Keep it a single dependency-free package.
4. **Respect the license red lines** — see [License](#license) below.
5. Small, focused commits with a readable message are far easier to review than one large
   commit that touches everything.

## Development environment

- **Unity 2022.3 LTS or newer.** This is an Editor tool window, not a runtime package.
- **git on your PATH.** The package does not bundle git; it probes `git version` and
  guides the user to install git when it is missing.
- **Windows is first-class.** macOS is untested but not intentionally blocked.
- No network access beyond what git itself does.

## Wiring the package into a host Unity project

This repository *is* the UPM package — its `package.json` sits at the repository root and
the package name is `com.kf.gitui` — so you never copy files into `Packages/`.

**Option A — git URL (quick look).**

`Window ▸ Package Manager ▸ + ▸ Add package from git URL…` and paste:

```
https://github.com/VRChatCN-Kipfel/unity-git-bettergui.git
```

Append `#v0.1.0` for a tagged release. This way you can use the tool window but not edit it.

**Option B — local `file:` reference (the edit loop we recommend).**

1. Clone this repository to a path outside your Unity project.
2. In the host Unity project, open `Packages/manifest.json` and point the dependency at
   your clone:

   ```json
   {
     "dependencies": {
       "com.kf.gitui": "file:E:/path/to/unity-git-bettergui"
     }
   }
   ```

3. Switch back to Unity. The package is imported and `Window ▸ Git (Better GUI)` becomes
   available; edits you make inside the clone are picked up on the next refresh/reimport.
4. Open a project that is a Git worktree (or `git init` one) — the graph appears
   immediately.

## Smoke tests — you run them yourself

**This repository has no CI.** No workflow runs on your pull request, no status check will
turn green, and there is nothing to wait for. Verification is manual, and you are the one
who runs it.

With the package wired into a host Unity project, run the editor in batch mode from a shell
(`<host project path>` is that Unity project):

```
Unity -batchmode -nographics -projectPath <host project path> -executeMethod KF.GitUI.ApiSmokeTest.Run -quit
```

When your change touches `Editor/Templates/` — the one-click ignore templates — also run:

```
Unity -batchmode -nographics -projectPath <host project path> -executeMethod KF.GitUI.IgnoreTemplateSmokeTest.Run -quit
```

- Paste the tail of the log and the exit code into your pull request.
- Editor output is tagged: `[gitui]` marks tool-window diagnostics, `[api-smoke]` marks
  smoke output. Those are the lines a reviewer looks for.
- If your shell swallows the editor's output, add `-logFile <path>` and read that file.
- Smoke runs assert layout math and pipeline behavior, not rendered pixels — so also open
  the window once and exercise the path you changed by hand.

## Contributing a one-click ignore template

A template contribution is **two files (the second optional) dropped into
`Editor/Templates/BuiltIn/`**. That is the entire format — no C# required.

**Step 0 — the laziest path.** The tool window can export the current project's
`.gitignore` as a project-shared template, and the export is already in template format
(if the first line is not a purpose comment, the export adds one). Take that file, adjust
the comment, and continue with step 1 — you skip writing ignore patterns by hand.

**Step 1 — add the files.**

Required, `Editor/Templates/BuiltIn/<id>.gitignore`:

- plain gitignore text, UTF-8, LF line endings, one trailing newline;
- `<id>` is lowercase kebab-case, for example `unity-standard`;
- the **first non-empty line must be a purpose comment**, for example
  `# Unity standard ignores (Library/, Temp/, build outputs, IDE artifacts)`.

Optional, `Editor/Templates/BuiltIn/<id>.meta.json` — if you omit it, the display name is
derived from `<id>` and the description stays empty:

```json
{
  "name": { "zh": "Unity 标准", "en": "Unity Standard" },
  "description": { "zh": "…", "en": "…" },
  "tags": ["unity", "standard"],
  "order": 100
}
```

- every field is optional;
- a smaller `order` sorts earlier — built-in templates use 100, 110, 120, …;
- `tags` are short lowercase labels.

Do **not** hand-write the `.meta` file: Unity generates it, and you commit it together
with your new files. If your local Unity has not generated one yet, say so in the pull
request and a reviewer will add it.

**Step 2 — verify locally.** Run the template smoke command above, then open the window
and apply your template to a throwaway Git worktree. Applying it writes `.gitignore` in
the project root:

- a missing `.gitignore` is created;
- an existing one is **merged** by default — only the missing ignore patterns are appended
  and the original content is kept;
- the window also offers an explicit **overwrite** choice, which backs the previous file
  up to `.gitignore.bak` first.

Check both paths as far as they apply to your template.

**Step 3 — open the pull request.** Change type `templates`, the two new files plus the
Unity-generated `.meta` files, and the smoke result pasted into the PR description. If you
would rather hand the content over, open the **Contribute a one-click ignore template**
issue instead; a maintainer can land the file for you.

For reference, template loading priority on the user side is user-local
`UserSettings/GitBetterGui/IgnoreTemplates/` ▸ project-shared `.gitui-ignore-templates/` ▸
the built-in `Editor/Templates/BuiltIn/` shipped in the package. On an id clash the
higher-priority copy wins, so a local file always beats your built-in template while
testing.

### Copy-paste skeleton

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

## Contributing a language

A language contribution is **two files (the second optional) dropped into
`Editor/I18n/BuiltIn/`**. No C# is involved, and you do not have to translate every key
at once.

**Step 0 — the laziest path.** The tool window can **export a skeleton**: it writes a
`<lang>.json` containing every key next to its English text into the project-level
`.gitui-i18n/` folder. Fill in the values you know and copy the file into
`Editor/I18n/BuiltIn/` — you never transcribe a key by hand. Because `.gitui-i18n/` is read
before the built-in folder, you can also apply the pack from there while you work.

**Step 1 — add the files.**

Required, `Editor/I18n/BuiltIn/<lang>.json` — a **flat** key/value map, UTF-8, LF line
endings, one trailing newline. The keys are exactly the `I18n.Keys` strings:

```json
{
  "ui.window.title": "Git 增强界面",
  "ui.graph.loading": "加载中…"
}
```

- `<lang>` is a BCP-47-style code: a 2–3 letter language code, optionally followed by `-`
  and a 2–8 letter region code — `zh-CN`, `zh-TW`, `ja-JP`, `ko-KR`, `de-DE`, `ru-RU`. The
  code **is** the file name, so `zh-CN` means `zh-CN.json`.
- The map is flat: no nested objects, no comments, one key per line.

Optional, `Editor/I18n/BuiltIn/<lang>.meta.json`:

```json
{
  "nativeName": "简体中文",
  "maintainers": ["your-github-login"],
  "order": 20
}
```

- `nativeName` is the language's own name for itself (`简体中文`, `日本語`, `Deutsch`), not
  the English name;
- `maintainers` is optional and lists GitHub user names — **one language has one primary
  maintainer**, so two people do not edit the same pack at the same time and drift apart on
  terminology;
- `order` is optional, and a smaller value sorts earlier in the language picker.

The Unity-generated `.meta` files for the new files are committed with them, exactly as for
every other file in this package.

**Step 2 — the rules that are not optional.**

*Placeholders.* Many keys carry format placeholders such as `{0}` and `{1}` — the
authoritative list is whatever `Editor/I18n/I18n.cs` holds, and the exported skeleton spells
every key out, so you never count them yourself. For a key that has them you must keep

- the **same set of indices**, and
- the **same format specifier for each index**, such as the `:N0` in `{0:N0}`;

while the order may follow your language's grammar freely. A key that breaks either rule is
**discarded and falls back to the English original** — the window never breaks — and it is
counted as a problem key.

*Unknown keys.* A key the English table does not contain is ignored and logged as a warning,
so a pack written against an older release cannot pollute a newer one.

*Partial translations are welcome.* Anything you leave out falls back to English, and the
language picker shows the coverage of each pack (`ja-JP 42/203`). A pack that is honest
about its coverage is more useful than no pack at all.

**Step 3 — verify locally.** Run the i18n smoke command:

```
Unity -batchmode -nographics -projectPath <host project path> -executeMethod KF.GitUI.I18nSmokeTest.Run -quit
```

It covers pack discovery, placeholder validation, unknown keys, coverage reporting, priority
merging and skeleton export. Expect it to finish with exit code 0 and no assertion failure.
Then open the window, switch to your language and read the strings you changed — a smoke run
asserts mechanics, not how a translation reads.

**Step 4 — open the pull request.** Change type `i18n`, the new `.json` file (plus
`.meta.json` if you wrote one) and the smoke result pasted into the PR description. If you
would rather hand the content over, open the **Contribute a language pack** issue instead; a
maintainer can land the files for you.

Where a pack is read from — the three sources are merged **per key**, not per file:

```
UserSettings/GitBetterGui/I18n/   ← personal (not version controlled), highest priority
<project root>/.gitui-i18n/       ← project-shared (commit-friendly), team reuse
<package>/Editor/I18n/BuiltIn/    ← built-in (ships with the package), lowest priority
```

This is a deliberate difference from the one-click ignore templates, where the
higher-priority file replaces the whole template: here only the keys you actually provide
win, and every other key keeps its English text. A local pack can therefore carry just the
handful of keys you disagree with.

The boundary of a "translated" window: menu paths declared with `[MenuItem("Window/Git/…")]`
are static and Unity cannot localize them, so they stay English. Completing a language means
translating every string **inside the window**; the `Window ▸ Git ▸ …` entries are out of
scope. Terminology comes from the term table at the top of `Editor/I18n/I18n.cs`, which is
the single authority — fetch → 提取, stage → 暂存, unstage → 取消暂存, checkout → 检出,
branch → 分支, tag → 标签, merge → 合并, reset → 重置, stash → 贮藏, remote → 远程,
2FA → 备选, index → 索引, worktree → 工作树, revert → 撤销变动, commit → 提交. Keep one
term per concept and keep verbs consistent; if you believe a term is wrong, raise it in the
issue rather than translating it your own way in a single pack.

### Copy-paste skeleton

```
Editor/I18n/BuiltIn/zh-CN.json
```

```json
{
  "ui.window.title": "Git 增强界面",
  "ui.graph.loading": "加载中…"
}
```

```
Editor/I18n/BuiltIn/zh-CN.meta.json
```

```json
{
  "nativeName": "简体中文",
  "maintainers": ["your-github-login"],
  "order": 20
}
```

## Commit messages and branch names

- **Commit messages:** an English conventional prefix plus a Chinese description. Types in
  use are `feat`, `fix`, `docs`, `templates` and `perf`.
  - `fix(poll): 自动刷新轮询不再阻塞编辑器主线程`
  - `perf(poll): 合并每轮的两次 git status`
- **Branch names:** short kebab-case with the type as prefix, for example
  `fix/non-blocking-poll`.
- Keep the subject line about one change; split unrelated work into separate commits and
  separate pull requests.

## Pull requests

- Fill in the pull request template (`.github/PULL_REQUEST_TEMPLATE.md`) and delete the
  sections that do not apply: change type, related issue, what changed, how you verified
  it, and the checklist.
- One change type per pull request.
- There is no CI to wait for; a maintainer reviews by reading the diff and reproducing
  your smoke command. Expect questions about scope, `.meta` files and license
  compatibility — those are the review's main axes.
- Keep the description honest about what you did not test.

## Issues

- **Bug reports** use the bug report form: Unity version, package version or commit, OS,
  git version, reproduction steps, expected vs actual behavior, and the `[gitui]` /
  `[api-smoke]` log lines. A window screenshot helps and is optional.
- **Feature requests** use the feature request form, which asks for the closest roadmap
  milestone (M1–M4) and whether you want to implement it.
- **Ignore template proposals** use the dedicated template form, even if you have no code
  to write yet.
- Blank issues are enabled if a form does not fit your case.
- Please skim [ROADMAP.md](ROADMAP.md) first: M1–M3 are delivered and M4 (Project asset
  status, one-click ignore templates, zh-CN localization, side-by-side diff) is where
  community help is most welcome.

## License

- The package as a whole is **MIT** — see [LICENSE.md](LICENSE.md).
- The embedded engine `com.spoiledcat.git.api` is **MIT**; keep its copyright notice
  ([Editor/Api/LICENSE.md](Editor/Api/LICENSE.md)). Its dependency
  `com.unity.editor.tasks` is MIT as well
  ([Editor/Api/com.unity.editor.tasks/LICENSE.md](Editor/Api/com.unity.editor.tasks/LICENSE.md)).
- Reference source from JetBrains `intellij-community` is **Apache-2.0**: porting from it
  is fine as long as the copyright notice is preserved.
- **UniGit / UnityGitUI are GPL-3.0: do not copy their code.** Reading them for layout and
  data-flow ideas is acceptable; translating their implementation into this repository is
  not.
- If you add third-party code or text, state its license and keep the notice in
  [Third Party Notices.md](Third Party Notices.md) current.

## Behaviour expectations

- English and Chinese are both fine in issues, pull requests and review comments.
- Be specific about the user-visible effect of your change, not only the implementation.
- Be patient: reviewers reproduce your change by hand because there is no CI.
- Settle disagreement about scope in the issue before doing large work.
