# Unity Git Better GUI

A JetBrains-quality Git tool window for the Unity Editor.

Three panes, one window: **commit graph | file changes | commit details** — with content-level diff,
hunk staging, 3-way merge conflict resolution, rebase/cherry-pick flows, blame, and remote management.

Built on top of [`com.spoiledcat.git.api`](https://github.com/spoiledcat/git-for-unity) (MIT) as the engine;
this package embeds the API's Editor sources so no extra dependency resolution is needed.

## Features

- Three-pane tool window: graph with branch swimlanes / file tree / commit detail
- Visual diff viewer with hunk-level **view / stage / unstage**
- 3-way merge conflict window (ours / theirs / merge base) with delete-handling
- Full **merge & rebase conflict workflows** — resolve, continue, abort; conflict badge turns green when resolved
- Blame sidebar, remote management window, tag/branch management (list + context menu)
- Graph shows branches, tags **and** remote-tracking refs without stash pollution
- I18n-ready UI: all 152 strings go through a translation table (zh-CN locale is a planned M4 deliverable)

## Requirements

- **Unity 2022.3 LTS or newer** (Editor tool window; not a runtime package)
- **Git on PATH** — no bundled git; no remote/network access required beyond what Git itself does
- Windows first-class (macOS untested but not intentionally blocked)

## Installation (UPM)

Package Manager ▸ **+** ▸ *Add package from git URL*:

```
https://github.com/VRChatCN-Kipfel/unity-git-bettergui.git
```

(For tagged releases append `#0.2.0-preview` or check the Releases page.)

## Quick start

1. Install the package as above.
2. Window ▸ Git (Better GUI) — or the toolbar button.
3. Open a project that is a Git worktree (or `git init` one). The graph appears immediately.

## Screenshots

_To be added — help wanted (M4)._

## Roadmap & contributing

See [ROADMAP.md](ROADMAP.md). Immediate community-help items (M4):

- **Project icons** — asset status indicators in the Project window
- **One-click ignore template**
- **Localization** — zh-CN primary; other languages welcome via the I18n table
- **Side-by-side diff** capstone

Issues and PRs welcome. Keep one PR to one change type; small focused commits are appreciated.

## License

MIT — see [LICENSE.md](LICENSE.md). The embedded engine
[`com.spoiledcat.git.api`](Editor/Api/LICENSE.md) and its dependency
`com.unity.editor.tasks` (in `Editor/Api/com.unity.editor.tasks/LICENSE.md`) are MIT as well.