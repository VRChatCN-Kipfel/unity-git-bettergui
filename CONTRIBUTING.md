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
