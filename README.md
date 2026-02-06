[marketplace]: <https://marketplace.visualstudio.com/items?itemName=MadsKristensen.InstaSearch>
[vsixgallery]: <http://vsixgallery.com/extension/InstaSearch.5164fa67-5caa-4d84-9087-bbaedc2a5539/>
[repo]: <https://github.com/madskristensen/InstaSearch>

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

| Pattern             | Matches                                           |
| ------------------- | ------------------------------------------------- |
| `dialog`            | Any file containing "dialog"                      |
| `test*.cs`          | Files starting with "test" ending in ".cs"        |
| `*.xaml`            | All XAML files                                    |
| `file.cs:42`        | Opens file.cs and navigates to line 42            |
| `dialog .cs .ts`    | Files containing "dialog" with .cs or .ts extension |
| `dialog -.xaml`     | Files containing "dialog", excluding .xaml files  |
| `dialog \src\`      | Files containing "dialog" under a `src` folder    |
| `.cs \tests\`       | All .cs files under a `tests` folder              |

### Search Filters

You can append space-separated modifiers to any search query:

- **Extension include** (`.ext`): Only show files with the given extension. Use multiple to match any of them.
  - `service .cs .ts` → files containing "service" that end in `.cs` or `.ts`
- **Extension exclude** (`-.ext`): Hide files with the given extension.
  - `dialog -.designer.cs -.g.cs` → files containing "dialog", excluding generated files
- **Path filter** (`\folder\`): Only show files whose relative path contains the folder segment.
  - `controller \api\` → files containing "controller" under an `api` folder
  - `\src\ .cs` → all `.cs` files under `src` (no text query needed)

Filters can be combined freely: `service \src\ .cs -.designer.cs`

### Go-to-Line

Append `:lineNumber` to your search query to jump directly to a specific line after opening the file. For example:

- `program.cs:100` - Opens program.cs at line 100
- `test*:25` - Opens the first matching file at line 25

### Keyboard Shortcuts

| Key                       | Action                         |
| ------------------------- | ------------------------------ |
| `Alt+Space`               | Open Insta Search              |
| `Up` / `Down`             | Navigate results               |
| `Shift+Up` / `Shift+Down` | Extend selection               |
| `Page Up` / `Page Down`   | Jump 10 items                  |
| `Enter`                   | Open selected file(s)          |
| `Ctrl+Enter`              | Open file and keep dialog open |
| `Ctrl+Click`              | Add/remove file from selection |
| `Shift+Click`             | Select range of files          |
| `Esc`                     | Close dialog                   |

### Multi-Select

Hold `Ctrl` while clicking to select multiple files, or hold `Shift` to select a range. You can also use `Shift+Arrow` keys to extend your selection from the keyboard. Press `Enter` to open all selected files at once.

## How It Works

Insta Search maintains an in-memory index of all files in your workspace. The index is built once when you first open the search dialog, then kept up to date automatically.

### Indexing

When you invoke Insta Search for the first time in a workspace, it performs a parallel scan of the file system. Multiple threads pull directories from a shared work queue, which keeps all CPU cores busy without waiting for each directory level to complete. The following directories are excluded by default:

- `.git`, `.vs`, `.svn`, `.hg`, `.idea`
- `bin`, `obj`, `Debug`, `Release`
- `node_modules`, `packages`, `.nuget`, `TestResults`

You can customize this list in **Tools > Options > InstaSearch > General**. Wildcard patterns like `*.Migrations` are also supported.

The resulting file list is cached in memory. Subsequent searches reuse this cache, making them nearly instant.

### Options

Configure InstaSearch via **Tools > Options > InstaSearch > General**:

| Setting               | Description                                                                                                 |
| --------------------- | ----------------------------------------------------------------------------------------------------------- |
| Ignored Folders       | Comma-separated list of folder names to exclude from indexing. Supports `*` wildcards (e.g., `*.Migrations`). |
| Ignored File Patterns | Comma-separated file name patterns to exclude from results (e.g., `*.designer.cs`). Supports `*` wildcards. |
| Take over Go To All   | When enabled, `Ctrl+T` and `Ctrl+,` will open Insta Search instead of Go To All.                            |

Changes to ignored folders take effect on the next search (the index is automatically refreshed).

### Go To All Integration

By default, Insta Search intercepts the built-in **Go To All** command (`Ctrl+T` / `Ctrl+,`) when invoked via keyboard shortcut. This lets you use Insta Search as your primary file navigation tool without changing keybindings.

The interception only occurs when:

- The **Take over Go To All** option is enabled (default: on)
- The command is triggered via keyboard (holding `Ctrl`)

Invoking Go To All from the menu (**Edit > Go To > Go To All**) will still open the standard Visual Studio dialog, allowing you to access symbol search and other Go To All features when needed.

To disable this behavior, uncheck **Take over Go To All** in **Tools > Options > InstaSearch > General**.

### Live Updates

After the initial index is built, a FileSystemWatcher monitors the workspace for file creates, deletes, and renames. When a change is detected (outside of ignored directories), the cache is marked as "dirty" rather than updated immediately. The next time you open the search dialog, the index is rebuilt.

This lazy approach means that build operations (which may touch hundreds of files in `bin` and `obj`) cause no processing overhead. Only one re-index happens, and only when you actually search.

If the FileSystemWatcher fails to start (for example, on a network drive), Insta Search falls back to the cached index. You can manually refresh by clicking the "Refresh" link in the status bar.

### Searching

When you type a query, Insta Search filters the cached file list using case-insensitive substring matching. If your query contains wildcards (`*`), it splits the pattern into segments and checks that each segment appears in order within the filename. For example, `test*.cs` becomes `["test", ".cs"]` and matches any filename that starts with "test" and ends with ".cs".

Space-separated modifiers (`.ext`, `-.ext`, `\folder\`) are parsed out of the query once per keystroke. The remaining text is the core search term. During matching, each file is checked against all active filters using simple `EndsWith` / `IndexOf` comparisons — zero allocations per file, same as the core substring/wildcard matching.

Results are ranked by:

1. History score (files you have opened before are ranked higher)
2. File type (code/text files are prioritized over binary files like images, executables, etc.)
3. Exact match (query matches filename with or without extension, e.g., `dialog` → `Dialog.cs`)
4. Whether the filename starts with your query
5. Shorter filenames first (closer matches where the query covers more of the name)
6. Alphabetical order

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
