# Third Party Notices

This package (`com.kf.gitui`, "Unity Git Better GUI") contains or is derived
from the third-party components listed below. Where a component's own license
text ships inside this package, the in-package file is authoritative;
otherwise the license text is reproduced here.

The git engine is embedded via **git subtree** from the fork branch
`VRChatCN-Kipfel/git-for-unity` `split/api` (upstream
[`spoiledcat/git-for-unity`](https://github.com/spoiledcat/git-for-unity)),
which carries the upstream parents fix and the zh-CN localization bundle.
All engine sources remain under `Editor/Api/`. The upstream notices
`Editor/Api/Third Party Notices.md` and `Editor/Api/Third Party Notices - net35.md`
are authoritative for the API's own auxiliary components.

---

## com.spoiledcat.git.api — embedded engine (MIT)

- Source: <https://github.com/spoiledcat/git-for-unity>
- License: MIT — full text in `Editor/Api/LICENSE.md`
- Copyright (c) 2019 Unity Technologies
- Copyright (c) 2016-2018 GitHub
  (additional contributors per upstream repository history)
- Embedded location: `Editor/Api/` — sources plus the auxiliary binaries below

## com.unity.editor.tasks (MIT)

- Source: <https://github.com/Unity-Technologies/com.unity.editor.tasks>
- License: MIT — full text in `Editor/Api/com.unity.editor.tasks/LICENSE.md`
- Copyright (c) 2019 Unity Technologies
- Copyright (c) 2016-2019 Andreia Gaita
- Copyright (c) 2016-2018 GitHub
- Embedded location: `Editor/Api/com.unity.editor.tasks/`

## sfw — native helper libraries (MIT)

- Source: <https://github.com/github-for-unity/sfw> — port of Axosoft's NSFW
- License: MIT — full text in `Editor/Api/Third Party Notices.md`
- Copyright (c) 2017-2018 GitHub; Copyright (c) 2015 Axosoft
- Shipped binaries: `Editor/Api/sfw/**`

## Mono.Posix (MIT)

- Assembly from the Mono project (<https://www.mono-project.com/>), shipped by
  com.spoiledcat.git.api at `Editor/Api/Mono.Posix/Mono.Posix.dll`.
  Copyright holders include Novell, Inc. and the Mono / .NET Foundation
  contributors.
- License text:

> The MIT License
>
> Copyright (c) Novell, Inc. and the Mono project contributors
>
> Permission is hereby granted, free of charge, to any person obtaining a copy
> of this software and associated documentation files (the "Software"), to deal
> in the Software without restriction, including without limitation the rights
> to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in
> all copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
> THE SOFTWARE.

## ICSharpCode.SharpZipLib (MIT)

- Source: <https://github.com/icsharpcode/SharpZipLib>
- License: MIT — copyright (c) the SharpZipLib contributors (ICSharpCode).
  Condition text identical to the MIT license reproduced above for Mono.Posix.
- Embedded source location: `Editor/Api/Api/SharpZipLib/`
  (namespace `Unity.VersionControl.Git.ICSharpCode.SharpZipLib`)

## JetBrains intellij-community — design reference (Apache-2.0)

- Source: <https://github.com/JetBrains/intellij-community>
- License: Apache License 2.0 — <https://www.apache.org/licenses/LICENSE-2.0>

The three-pane tool-window layout and the commit-graph swimlane/edge layout
of this product are ported from JetBrains' VCS Log UI as a design reference.
This product is not affiliated with, endorsed by, or sponsored by
JetBrains s.r.o.

---

## unity-git-bettergui — this package (MIT)

- License: MIT — see `LICENSE.md`
- Copyright (c) 2026 VRChatCN-Kipfel