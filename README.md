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
- **Live index** - File changes are detected automatically via FileSystemWatcher
- **File icons** - Visual Studio file icons for easy identification
- **Keyboard-driven** - Navigate entirely with keyboard
- **VS themed** - Follows your Visual Studio light/dark theme

## Usage

Press `Alt+Space` to open the search dialog (or find it under **Edit > Go To > InstaSearch - Quick File Search**).

### Search Patterns

| Pattern     | Matches                                    |
| ----------- | ------------------------------------------ |
| `dialog`    | Any file containing "dialog"               |
| `test*.cs`  | Files starting with "test" ending in ".cs" |
| `*service*` | Files containing "service" anywhere        |
| `*.xaml`    | All XAML files                             |

### Keyboard Shortcuts

| Key                     | Action             |
| ----------------------- | ------------------ |
| `Alt+Space`             | Open InstaSearch   |
| Up / Down               | Navigate results   |
| `Page Up` / `Page Down` | Jump 10 items      |
| `Enter`                 | Open selected file |
| `Esc`                   | Close dialog       |

## How It Works

InstaSearch maintains an in-memory index of all files in your workspace. The index is built once when you first open the search dialog, then kept up to date automatically.

### Indexing

When you invoke InstaSearch for the first time in a workspace, it performs a parallel scan of the file system. Multiple threads pull directories from a shared work queue, which keeps all CPU cores busy without waiting for each directory level to complete. The following directories are excluded from indexing:

- `.git`, `.vs`, `.svn`, `.hg`, `.idea`
- `bin`, `obj`, `Debug`, `Release`
- `node_modules`, `packages`, `.nuget`, `TestResults`

The resulting file list is cached in memory. Subsequent searches reuse this cache, making them nearly instant.

### Live Updates

After the initial index is built, a FileSystemWatcher monitors the workspace for file creates, deletes, and renames. When a change is detected (outside of ignored directories), the cache is marked as "dirty" rather than updated immediately. The next time you open the search dialog, the index is rebuilt.

This lazy approach means that build operations (which may touch hundreds of files in `bin` and `obj`) cause no processing overhead. Only one re-index happens, and only when you actually search.

If the FileSystemWatcher fails to start (for example, on a network drive), InstaSearch falls back to the cached index. You can manually refresh by clicking the "Refresh" link in the status bar.

### Searching

When you type a query, InstaSearch filters the cached file list using case-insensitive substring matching. If your query contains wildcards (`*`), it splits the pattern into segments and checks that each segment appears in order within the filename. For example, `test*.cs` becomes `["test", ".cs"]` and matches any filename that starts with "test" and ends with ".cs".

Results are ranked by:

1. History score (files you have opened before are ranked higher)
2. Whether the filename starts with your query
3. Alphabetical order

Only the top 100 results are returned. File icons are fetched only for these final results, avoiding unnecessary work for matches that will not be displayed.

### History

InstaSearch tracks which files you open and how often. This history is stored per-workspace in a JSON file under `%LocalAppData%\InstaSearch`. When you open the search dialog with an empty query, your most frequently opened files are shown. When you type a query, files you have opened before are ranked higher in the results.

## How can I help?

If you enjoy using the extension, please give it a rating on the [Visual Studio Marketplace][marketplace].

Should you encounter bugs or have feature requests, head over to the [GitHub repo][repo] to open an issue if one doesn't already exist.

Pull requests are also very welcome, as I can't always get around to fixing all bugs myself. This is a personal passion project, so my time is limited.

Another way to help out is to [sponsor me on GitHub](https://github.com/sponsors/madskristensen).
