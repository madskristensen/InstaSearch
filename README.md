[marketplace]: <https://marketplace.visualstudio.com/items?itemName=MadsKristensen.InstaSearch>
[vsixgallery]: <http://vsixgallery.com/extension/InstaSearch.5164fa67-5caa-4d84-9087-bbaedc2a5539/>
[repo]: <https://github.com/madskristensen/Insta Search>

# Insta Search - Quick File Search for Visual Studio

[![Build](https://github.com/madskristensen/InstaSearch/actions/workflows/build.yaml/badge.svg)](https://github.com/madskristensen/InstaSearch/actions/workflows/build.yaml)
![GitHub Sponsors](https://img.shields.io/github/sponsors/madskristensen)

Download this extension from the [Visual Studio Marketplace][marketplace]
or get the [CI build][vsixgallery].

--------------------------------------

A fast, lightweight file search dialog for Visual Studio. Instantly find and open files in your solution or folder with wildcard support and smart history.

![Search](art/search.gif)

## Features

- **Instant search** - Results appear as you type with wildcard support (`test*.cs`)
- **Query highlighting** - Matching text is highlighted in results for easy identification
- **Smart history** - Recently opened files appear first, prioritized by frequency
- **Live index** - File changes are detected automatically via FileSystemWatcher
- **File icons** - Visual Studio file icons for easy identification
- **Keyboard-driven** - Navigate entirely with keyboard
- **VS themed** - Follows your Visual Studio light/dark theme

## Usage

Press `Alt+Space` to open the search dialog (or find it under **Edit > Go To > File Search**).

### Search Patterns

| Pattern      | Matches                                    |
| ------------ | ------------------------------------------ |
| `dialog`     | Any file containing "dialog"               |
| `test*.cs`   | Files starting with "test" ending in ".cs" |
| `*service*`  | Files containing "service" anywhere        |
| `*.xaml`     | All XAML files                             |
| `file.cs:42` | Opens file.cs and navigates to line 42     |

### Go-to-Line

Append `:lineNumber` to your search query to jump directly to a specific line after opening the file. For example:

- `program.cs:100` - Opens program.cs at line 100
- `test*:25` - Opens the first matching file at line 25

### Keyboard Shortcuts

| Key                       | Action                              |
| ------------------------- | ----------------------------------- |
| `Alt+Space`               | Open Insta Search                   |
| `Up` / `Down`             | Navigate results                    |
| `Shift+Up` / `Shift+Down` | Extend selection                    |
| `Page Up` / `Page Down`   | Jump 10 items                       |
| `Enter`                   | Open selected file(s)               |
| `Ctrl+Enter`              | Open file and keep dialog open      |
| `Ctrl+Click`              | Add/remove file from selection      |
| `Shift+Click`             | Select range of files               |
| `Esc`                     | Close dialog                        |

### Multi-Select

Hold `Ctrl` while clicking to select multiple files, or hold `Shift` to select a range. You can also use `Shift+Arrow` keys to extend your selection from the keyboard. Press `Enter` to open all selected files at once.

## How It Works

Insta Search maintains an in-memory index of all files in your workspace. The index is built once when you first open the search dialog, then kept up to date automatically.

### Indexing

When you invoke Insta Search for the first time in a workspace, it performs a parallel scan of the file system. Multiple threads pull directories from a shared work queue, which keeps all CPU cores busy without waiting for each directory level to complete. The following directories are excluded by default:

- `.git`, `.vs`, `.svn`, `.hg`, `.idea`
- `bin`, `obj`, `Debug`, `Release`
- `node_modules`, `packages`, `.nuget`, `TestResults`

You can customize this list in **Tools > Options > InstaSearch > General**.

The resulting file list is cached in memory. Subsequent searches reuse this cache, making them nearly instant.

### Options

Configure InstaSearch via **Tools > Options > InstaSearch > General**:

| Setting         | Description                                                    |
| --------------- | -------------------------------------------------------------- |
| Ignored Folders | Comma-separated list of folder names to exclude from indexing. |

Changes to ignored folders take effect on the next search (the index is automatically refreshed).

### Live Updates

After the initial index is built, a FileSystemWatcher monitors the workspace for file creates, deletes, and renames. When a change is detected (outside of ignored directories), the cache is marked as "dirty" rather than updated immediately. The next time you open the search dialog, the index is rebuilt.

This lazy approach means that build operations (which may touch hundreds of files in `bin` and `obj`) cause no processing overhead. Only one re-index happens, and only when you actually search.

If the FileSystemWatcher fails to start (for example, on a network drive), Insta Search falls back to the cached index. You can manually refresh by clicking the "Refresh" link in the status bar.

### Searching

When you type a query, Insta Search filters the cached file list using case-insensitive substring matching. If your query contains wildcards (`*`), it splits the pattern into segments and checks that each segment appears in order within the filename. For example, `test*.cs` becomes `["test", ".cs"]` and matches any filename that starts with "test" and ends with ".cs".

Results are ranked by:

1. History score (files you have opened before are ranked higher)
2. Whether the filename starts with your query
3. Alphabetical order

Only the top 100 results are returned. File icons are fetched only for these final results, avoiding unnecessary work for matches that will not be displayed.

### History

InstaSearch tracks which files you open and how often. This history is stored per-workspace in the `.vs/InstaSearch` folder within your repository (the `.vs` folder is typically gitignored). When you open the search dialog with an empty query, your most frequently opened files are shown. When you type a query, files you have opened before are ranked higher in the results.

## Performance

Insta Search is designed for speed. Benchmarks are included in the repository and run on every build to catch regressions.

### Search Performance

Tested with synthetic file lists of varying sizes:

| Pattern               | 1,000 files | 10,000 files | 50,000 files | Allocations |
| --------------------- | ----------: | -----------: | -----------: | ----------: |
| Substring (`service`) |        9 μs |       117 μs |       978 μs |         0 B |
| Wildcard (`*.cs`)     |       14 μs |       290 μs |     2,225 μs |         0 B |
| Wildcard (`test*.cs`) |       13 μs |       148 μs |       928 μs |         0 B |

Wildcard patterns are pre-parsed once per search, so matching is allocation-free regardless of file count.

### Indexing Performance

Cold indexing (no cache) on a synthetic directory tree:

| Files |  Time |   Memory |
| ----: | ----: | -------: |
|   100 |  2 ms |   152 KB |
| 1,000 | 12 ms |   672 KB |
| 5,000 | 21 ms | 3,039 KB |

Indexing scales linearly with file count. Real-world performance depends on disk speed and directory depth.

## How can I help?

If you enjoy using the extension, please give it a rating on the [Visual Studio Marketplace][marketplace].

Should you encounter bugs or have feature requests, head over to the [GitHub repo][repo] to open an issue if one doesn't already exist.

Pull requests are also very welcome, as I can't always get around to fixing all bugs myself. This is a personal passion project, so my time is limited.

Another way to help out is to [sponsor me on GitHub](https://github.com/sponsors/madskristensen).
