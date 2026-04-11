# ScreenSearchOverlay

A lightweight Windows overlay that captures your screen, runs OCR, and lets you search for any visible text in real-time. Think Ctrl+F, but for everything on your screen.

![Windows 10/11](https://img.shields.io/badge/Windows-10%2F11-blue)
![.NET 10](https://img.shields.io/badge/.NET-10.0-purple)

[![Preview](preview.jpg)](preview.jpg)

## How it works

1. Press **Ctrl+Alt+F** to capture the screen under your mouse cursor
2. Type in the search bar to highlight matching text
3. Click any highlighted word to copy it to clipboard
4. Press **Escape** to close the overlay

The app runs in the system tray and waits for the hotkey.

## Features

### Core
- **OCR-powered screen search** using built-in Windows OCR (`Windows.Media.Ocr`)
- **Single monitor capture** - captures only the screen where your mouse cursor is
- **Real-time highlighting** with fade-in animation as you type
- **Click to copy** - click any highlighted word to copy it to clipboard (green flash confirms)
- **Regex search** - toggle the `.*` button to switch to regex matching

### Search bar
- **Drag & drop** - move the search bar anywhere on screen, position is remembered
- **Resizable** - choose between large / medium / small / tiny scale via the hamburger menu
- **Search history** - press Arrow Up/Down to cycle through previous searches (persisted to disk)

### Menu (hamburger button)
- **Copy all screen text** - copies all OCR-detected text to clipboard
- **Copy highlighted text** - copies only the matched words to clipboard
- **Clear history** - wipes saved search history
- **Zen mode** - subtle white/gray highlights instead of yellow, for a less distracting look
- **Highlight size** - large / medium / small padding around matched words
- **Search bar size** - scales the entire search bar UI

### Settings
- **Multi-language OCR** - right-click the tray icon to select which OCR languages to use (all installed languages are enabled by default)
- **Persistent settings** - zen mode, highlight size, search bar size, and search bar position are saved to `settings.json`
- **Persistent history** - search history is saved to `search-history.json` (max 50 entries)

## Installation

### Portable (recommended)
Download `ScreenSearchOverlay.exe` from the [latest release](../../releases/latest). Self-contained, no dependencies needed.

### Build from source
```bash
git clone https://github.com/jonlepage/LS-OCRLAYOUT.git
cd LS-OCRLAYOUT
dotnet run --project ScreenSearchOverlay.csproj
```

### Publish portable exe
```bash
dotnet publish ScreenSearchOverlay.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## Usage

| Action | Shortcut |
|--------|----------|
| Open overlay | `Ctrl+Alt+F` |
| Close overlay | `Escape` |
| Search | Type in the search bar |
| Toggle regex | Click `.*` button |
| Copy a word | Click on a highlighted word |
| Previous search | `Arrow Up` |
| Next search | `Arrow Down` |
| Confirm search to history | `Enter` |
| Move search bar | Drag the search bar |
| Quit app | Right-click tray icon > Quit |

## Requirements

- Windows 10 (build 19041+) or Windows 11
- At least one OCR language pack installed (English and your system language are typically pre-installed)

## Tech stack

- C# / WPF / .NET 10
- `Windows.Media.Ocr` (built-in, no external dependencies)
- `System.Windows.Forms.NotifyIcon` for system tray
