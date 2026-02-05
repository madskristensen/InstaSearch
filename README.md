[marketplace]: <https://marketplace.visualstudio.com/items?itemName=MadsKristensen.InstaSearch>
[vsixgallery]: <http://vsixgallery.com/extension/InstaSearch.5164fa67-5caa-4d84-9087-bbaedc2a5539/>
[repo]: <https://github.com/madskristensen/InstaSearch>

# InstaSearch - Quick File Search for Visual Studio

[![Build](https://github.com/madskristensen/InstaSearch/actions/workflows/build.yaml/badge.svg)](https://github.com/madskristensen/InstaSearch/actions/workflows/build.yaml)
![GitHub Sponsors](https://img.shields.io/github/sponsors/madskristensen)

Download this extension from the [Visual Studio Marketplace][marketplace]
or get the [CI build][vsixgallery].

--------------------------------------

A fast, lightweight file search dialog for Visual Studio. Instantly find and open files in your solution or folder with wildcard support and smart history.

![Search](art/search.gif)

## Features

- **Instant search** - Results appear as you type with wildcard support (`test*.cs`)
- **Smart history** - Recently opened files appear first, prioritized by frequency
- **File icons** - Visual Studio file icons for easy identification
- **Keyboard-driven** - Navigate entirely with keyboard
- **VS themed** - Follows your Visual Studio light/dark theme

## Usage

Press `Alt+Space` to open the search dialog (or find it under **Edit > Go To > InstaSearch - Quick File Search**).

### Search Patterns

| Pattern | Matches |
| ------- | ------- |
| `dialog` | Any file containing "dialog" |
| `test*.cs` | Files starting with "test" ending in ".cs" |
| `*service*` | Files containing "service" anywhere |
| `*.xaml` | All XAML files |

### Keyboard Shortcuts

| Key                     | Action             |
| ----------------------- | ------------------ |
| `Alt+Space`             | Open InstaSearch   |
| `↑` / `↓`               | Navigate results   |
| `Page Up` / `Page Down` | Jump 10 items      |
| `Enter`                 | Open selected file |
| `Esc`                   | Close dialog       |

## How It's So Fast

InstaSearch uses several performance techniques to deliver sub-millisecond search results:

### Parallel File System Indexing
- **Work-stealing thread pool** — Multiple threads continuously pull directories from a shared queue, eliminating synchronization barriers between directory levels
- **BlockingCollection with atomic completion detection** — No busy-waiting or race conditions when determining traversal completion
- **Smart directory exclusion** — Skips `.git`, `node_modules`, `bin`, `obj`, and other non-essential directories at enumeration time

### Zero-Allocation Search Path
- **Pre-computed lowercase filenames** — Case-insensitive matching without per-search `ToLower()` allocations
- **Segment-based wildcard matching** — Patterns like `test*.cs` are split once into `["test", ".cs"]` and matched via `IndexOf` chains—no regex compilation or backtracking
- **Deferred icon resolution** — File icons are fetched only for the final top-100 results, not all matches

### WPF Virtualization
- **VirtualizingStackPanel with container recycling** — Only visible list items are rendered; scrolling reuses existing UI elements
- **OneTime bindings** — Search results are immutable, so WPF skips change-notification overhead

### Debounced Input
- **150ms keystroke debounce** — Rapid typing doesn't trigger redundant searches; only the final query executes

## How can I help?

If you enjoy using the extension, please give it a ★★★★★ rating on the [Visual Studio Marketplace][marketplace].

Should you encounter bugs or have feature requests, head over to the [GitHub repo][repo] to open an issue if one doesn't already exist.

Pull requests are also very welcome, as I can't always get around to fixing all bugs myself. This is a personal passion project, so my time is limited.

Another way to help out is to [sponsor me on GitHub](https://github.com/sponsors/madskristensen).
