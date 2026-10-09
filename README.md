# ScreenSearchOverlay

Press **Ctrl+Alt+F**, search any text visible on screen with OCR, click to copy. Like Ctrl+F for the whole screen — and one click on **文A** translates everything on screen, in place, in any app.

Press **Ctrl+Alt+G** on any selected text to send it to ChatGPT with one of your saved prompts — see [Prompt Builder](#prompt-builder).

![Windows 10/11](https://img.shields.io/badge/Windows-10%2F11-blue)
![.NET 10](https://img.shields.io/badge/.NET-10.0-purple)

[![Preview](preview.jpg)](preview.jpg)

## Shortcuts

| | |
|---|---|
| `Ctrl+Alt+F` | Open overlay (global hotkey) |
| `Ctrl+Alt+G` | Prompt Builder: send the selected text to ChatGPT with a saved prompt (global hotkey, see [Prompt Builder](#prompt-builder)) |
| Type | Highlight matching text |
| Click match | Copy word to clipboard |
| `文A` button | Translate the whole screen (click again to go back to search) |
| `Left-click` (translate mode) | Close overlay, back to the app |
| Hover a translation | Show the text the OCR read |
| `Mouse wheel` | Scroll the app underneath; overlay re-captures (and re-translates) when you stop |
| `Escape` / `Right-click` | Close overlay |
| `↑` / `↓` | Search history |
| `Enter` | Save current search to history |
| Drag search bar | Move it (position persisted) |

Right-click inside the search box itself still opens the native paste menu.

## Features

- **OCR search** — `Windows.Media.Ocr`, multi-language (pick from tray icon, choice persisted)
- **Screen translation** — `文A` covers every line of text with its translation, painted on the line's own background color. Works on any app: games, chat apps, software that was never localized. See [Screen translation](#screen-translation).
- **Scroll-through** — wheel anywhere on the overlay scrolls the app below natively (works with Chromium/Electron). Overlay reappears with a fresh capture when you stop.
- **Regex** — toggle `.*` button
- **Copy all / copy matched** — hamburger menu
- **Zen mode**, **highlight size**, **search bar size** — hamburger menu, persisted
- **Prompt Builder** — select text anywhere, `Ctrl+Alt+G`, pick a saved prompt, send to ChatGPT (screenshot optional). See [Prompt Builder](#prompt-builder).
- **English / Français** — ⚙ in the Prompt Builder, applied live to the whole app

## Screen translation

Built first for Japanese software; works for any language Windows OCR can read.

- **Target language**: French by default. Change `translateTarget` in `settings.json` to any Google Translate code (`en`, `es`, `de`…).
- **Source language** is detected per line, so a screen mixing English menus and Japanese content is fully translated.
- **Only what changes is covered**: lines already in the target language, names returned unchanged, dates, counters and icons keep the real screen pixels.
- **Cached**: translations are kept while the app runs, so reopening the same screen is instant.

Translation uses the free, keyless Google Translate endpoint (`translate.googleapis.com`, the one behind the Chrome extension). It is unofficial and may be throttled. **The text visible on screen is sent to Google when you click `文A`** — nothing leaves your machine while you only search.

### Japanese (and other Asian languages)

Windows only reads the languages whose OCR pack is installed, usually just your system language. The tray menu **OCR Languages** lists them. Without the Japanese pack, Japanese text comes out as garbage (a Chinese pack turns kana into look-alike symbols) and so does the translation.

To add Japanese:

1. Settings → Time & Language → Language → **Add a language** → **日本語 Japanese** → Next
2. **Uncheck** "Set as my Windows display language", then Install. Optical character recognition is part of the required features.
3. Restart ScreenSearchOverlay (tray icon → Quit, then launch it again).

Or from an admin PowerShell:

```powershell
Add-WindowsCapability -Online -Name "Language.Basic~~~ja-JP~0.0.1.0"
Add-WindowsCapability -Online -Name "Language.OCR~~~ja-JP~0.0.1.0"
```

On Windows 10, the "Optional features" page does not list OCR packs — use one of the two ways above.

## Prompt Builder

Select text anywhere — a web page, a PDF, a chat, any app — press **Ctrl+Alt+G**, pick a prompt, send it to ChatGPT. Your prompts are saved, searchable and color-coded, so "fix this", "translate that", "explain this error" are one keystroke away.

[![Prompt Builder](prompt-builder.png)](prompt-builder.png)

### How it works

1. **Ctrl+Alt+G** copies the selection (a real `Ctrl+C` sent to the active app) and captures the screen under the cursor. Both stay in the clipboard: `Win+V` lists the screenshot right above the text.
2. **The Prompt Builder opens** on the monitor you are working on: saved prompts on the left, the selected prompt and the text on the right — both editable before sending. No selection? The text box is empty and ready to type or paste into.
3. **Send** opens ChatGPT in its own window, starts a new chat (temporary by default), types the message and sends it — or leaves it in ChatGPT's field for you to check and send with `Enter`, if **Send automatically** is unchecked in ⚙. The message is **the prompt, a blank line, then the text**, so a prompt ending with a colon (`Fix this text:`) reads naturally. Tick **Attach screenshot** to send the screen capture with it (hover the thumbnail to preview it, click it to toggle). The footer shows the total ChatGPT will receive, e.g. `Total: 226 characters + screenshot`.
4. **← Prompt Builder**, top left of the ChatGPT window, brings the Prompt Builder back exactly as it was left — same prompt, text and screenshot — to adjust and send again while the answer stays visible.

The chat button next to ⚙ opens the ChatGPT window directly, without sending anything.

### Keyboard

| | |
|---|---|
| Type in the search box | Filter prompts by name or content (accents and case ignored) |
| `↑` / `↓` | Pick a prompt (from the search box) |
| `Enter` | Send (from the search box or the list) |
| `Ctrl+Enter` | Send (from anywhere) |
| `Ctrl+1` … `Ctrl+9` | Pick the n-th visible prompt (numbers shown in the list) |
| `Ctrl+N` / `Ctrl+D` | New prompt / duplicate the selected one |
| `Alt+↑` / `Alt+↓` | Move the selected prompt up / down |
| `Delete` (in the list) | Delete, with **Undo** in the footer for a few seconds |
| `Ctrl+F` | Search |
| `Ctrl+wheel` in a text box | Text size |
| Double-click a prompt | Send with it |
| `Escape` | Clear the search, then close |

Right-click a prompt for the same actions.

### Managing prompts

- **Edit in place**: the selected prompt's name and text are edited right in the window and saved as you type to `prompts.json`.
- **The name is optional**: an unnamed prompt is listed by its first line. Write a prompt and send it right away.
- **Title colors**: while a name is being edited, a palette appears next to it. The color is shown on the title, in the list and on the selection bar, so prompts are recognized at a glance.
- **First run**: six starter prompts (fix, translate, explain, summarize, rephrase) in the UI language.
- **Layout**: drag the vertical line to widen the prompt list, and the handle under the prompt box to make it taller. Sizes, text size and window size are remembered.
- **Last prompt**: the selected prompt is remembered as soon as it is picked, and selected again at the next start.

### Settings and languages

**⚙** in the title bar:

- **Language**: English (en) or Français (fr), applied live to the whole app — overlay, Prompt Builder, ChatGPT window and tray menu. Languages are always shown with their ISO code, readable whatever the current language. The first run follows the Windows display language. Adding a language is one table in `Loc.cs`.
- **ChatGPT**: **Temporary chat** (on by default: nothing is kept in the account's history) and **Send automatically** (on by default; off, the message is written into ChatGPT and you press `Enter` yourself).
- **Text size** of the prompt and text boxes (also `Ctrl+wheel`).
- **Generate an account**: opens [temp-mail.id](https://temp-mail.id) in your browser, a throwaway address to sign up a ChatGPT account with.
- **Updates**: the current version, a button to check now or install what was found, and **Check automatically** (see [Updates](#updates)).

Hover the tray icon to see both hotkeys.

### Fast by design

The window shows ~60 ms after the hotkey once something is selected (~300 ms more with nothing selected: the time given to the app to answer `Ctrl+C`).

- The screenshot is taken on a worker thread while `Ctrl+C` runs; its thumbnail, its PNG and its clipboard copy are all produced off the UI thread, after the window is up.
- The window is built while the app is idle after startup, then reused: closing it only hides it.
- It is a regular window, not a layered one (`AllowsTransparency`), so resizing stays on the GPU. Everything is square, Windows 11 corners included.
- Every `Ctrl+Alt+G` preheats ChatGPT: a blank chat loads in the background while you pick a prompt, so **Send** usually only has to type. The ChatGPT window's status strip shows how long the send took. If that window was open on a previous answer, the answer is replaced by the new blank chat.

### ChatGPT window

A WebView2 created on the first `Ctrl+Alt+G`, then hidden — never closed. It opens a temporary chat by default (nothing is kept in any history; uncheck **Temporary chat** in ⚙ to keep your conversations), in a profile of its own (`ScreenSearchOverlay.WebView2/`); the cookie banner is refused automatically. Links in answers open in your browser.

It works logged out, or logged in to a ChatGPT account: log in once in that window and the session survives restarts (it lives in the profile). At login, accept **Save password**: should the session ever end, the login form fills itself back. Passwords are kept by WebView2's own password store, encrypted for your Windows account — the app never handles them. Keep the profile folder to yourself: it holds the session.

Everything the app knows about chatgpt.com — selectors and injected scripts — lives in `ChatGptPage.cs`: when OpenAI changes its page, that is the file to fix. `F12` in the ChatGPT window opens the DevTools. If sending fails, the status strip says at which step.

**The text (and the screenshot, when ticked) is sent to OpenAI when you click Send.** Nothing leaves your machine before.

`Ctrl+C` goes to whatever app is active. In a terminal with nothing selected, that interrupts the running command.

## Install

Download `ScreenSearchOverlay.exe` from the [latest release](../../releases/latest). Self-contained, portable.

## Updates

The app checks this repository's [latest release](../../releases/latest) at startup, at most once a day (it lives in the tray for days, and may be restarted many times in one). A check that found a newer version doesn't count: the next start asks again, so the offer shows right away. When a newer version is out, a notification offers it (click it to install), and the Prompt Builder's title bar shows **Version x.y.z available · click to install** — one click downloads, installs and restarts, with the download's progress shown in place. The tray menu and the Prompt Builder's ⚙ also check on demand and install.

Installing downloads the new `.exe` next to the running one, checks it against the size and SHA-256 digest GitHub publishes for it, then swaps the two files where the app lives and restarts it. Windows won't overwrite a running program but lets it be renamed: the running exe becomes `ScreenSearchOverlay.exe.old`, the new one takes its name, starts, waits for the old one to exit, and deletes the `.old`. If anything fails along the way, the files are put back as they were.

In a folder the app cannot write to (`Program Files`…), the update offers to open the download page instead. Uncheck **Check automatically** in ⚙ to stop the automatic checks; debug builds never check on their own.

## Build

```powershell
.\build.ps1 -Run       # build + launch
.\build.ps1            # build to dist/
.\build.ps1 -Release   # build + GitHub release
```

Version lives in `package.json` only — the `.csproj` and `build.ps1` both read it from there.

To enable diagnostic logging at `%TEMP%\ls-ocrlayout-scroll.log`, add `<DefineConstants>LS_DEBUG_LOG</DefineConstants>` to the csproj. Off by default — `[Conditional]` strips the calls completely in release.

## Code layout

`MainWindow` is split into partial files by concern:

| File | Responsibility |
|---|---|
| `MainWindow.xaml.cs` | ctor, lifecycle, fields, log |
| `MainWindow.Scroll.cs` | scroll state machine + global hook handler |
| `MainWindow.Capture.cs` | screen capture, `WriteableBitmap` swap, OCR pipeline + cache |
| `MainWindow.Search.cs` | search box, regex, hamburger menu, highlights |
| `MainWindow.SearchBar.cs` | drag, position, size, key nav, close handlers |
| `MainWindow.Translate.cs` | translate mode: batch request, background sampling, font fitting, click-to-close |
| `MainWindow.NativeInterop.cs` | Win32 P/Invoke + helpers (`SetClickThrough`, `InjectMouseWheel`, …) |
| `OcrLineBuilder.cs` | OCR results → translation lines (engine arbitration, CJK joining, split-line repair) — no UI dependency |
| `Translator.cs` | `ITranslator`, Google implementation (batching, cache), `TranslationFilter` |
| `LowLevelMouseHook.cs` | `WH_MOUSE_LL` on a dedicated thread (per MSDN guidance — UI-thread hooks risk silent detachment past `LowLevelHooksTimeout`) |
| `App.xaml.cs` | global hotkey, tray icon, settings/history persistence |
| `App.Prompt.cs` | Prompt Builder: `Ctrl+Alt+G`, selection + screenshot capture, prompt/chat window lifetime, its settings |
| `App.Update.cs` | update checks (startup, at most once a day, on demand), tray balloon and menu item, download and restart |
| `Updater.cs` | GitHub latest release, download + SHA-256 check, exe swap and post-update cleanup — no UI dependency |
| `PromptWindow.xaml(.cs)` | prompt list and editor, keyboard navigation, delete/undo; built at idle, hidden on close and reused |
| `ChatWindow.xaml(.cs)` | ChatGPT in a WebView2: preheated on every `Ctrl+Alt+G`, hidden on close, send status strip with timing |
| `ChatGptPage.cs` | chatgpt.com selectors + injected scripts — no UI dependency |
| `PromptLibrary.cs` | `SavedPrompt`, `prompts.json`, message composition — no UI dependency |
| `Loc.cs` | UI languages and strings (Prompt Builder, ChatGPT window, tray), published as live resources |
| `SelectionGrabber.cs` | simulated `Ctrl+C` (the hotkey's modifiers released on the app's side), clipboard access |
| `ScreenCapture.cs` | screenshot pipeline off the UI thread: capture, thumbnails, PNG, full-resolution clipboard copy |

### Scroll-through architecture

State machine `Idle ↔ Scrolling`. On the first wheel, the window goes `WS_EX_TRANSPARENT` (click-through Win32) and injects the wheel via `SendInput` so it lands on the app below. Subsequent wheels go natively to that app — Chromium-friendly because we never use synthetic `WM_MOUSEWHEEL`. A `WH_MOUSE_LL` hook on a dedicated thread keeps the debounce alive while we don't receive any events. After 400 ms of wheel silence: re-capture, refresh OCR, restore the overlay.

### Translation pipeline

1. **Lines, not words.** OCR runs once per enabled language; `OcrLineBuilder` keeps one reading per line position. Latin engines go first, and a CJK engine only takes over where its reading is mostly CJK — kana beat kanji-only readings, so the Japanese engine wins over the Chinese one. CJK characters are glued back together (the engines space every character), lines the OCR cut in two are re-joined, and two frequent Japanese-engine misreads are repaired (`G。d。t` → `Godot`, `ヒ。ッ` → `ピッ`).
2. **One indexed batch.** Lines with fewer than two letters (OCR noise) are dropped; the rest go out through `translate_a/t` with repeated `q` parameters, which returns `[translation, detectedLanguage]` per item, in order. Index *i* in = index *i* out: each translation maps straight back to its screen rectangle, no markers needed. (`translate_a/single` detects one language for the whole payload and leaves the minority language untranslated on mixed screens.)
3. **Draw only what changed.** Lines detected in the target language, or returned with the same letters, are not drawn.
4. **Blend in.** Each box takes the dominant color of a thin ring just outside the source text, with black or white text by luminance. The font size comes from the ink height of the OCR text itself, so the translation matches the original glyph size.

## Files written

- `settings.json` — zen mode, highlight size, search bar size & position, translation target language, unchecked OCR languages, UI language, Prompt Builder window sizes, splitters and text size, last prompt, screenshot checkbox, ChatGPT options (temporary chat, send automatically), automatic update checks
- `search-history.json` — last 50 searches
- `prompts.json` — saved prompts with their title color (indented, hand-editable; an unreadable file is set aside as `prompts.json.bak`, never overwritten)
- `ScreenSearchOverlay.WebView2/` — the ChatGPT window's browser profile (created on the first send)

All of them live next to the .exe.

## Requirements

Windows 10 build 19041+ or Windows 11. At least one OCR language pack (your system language is usually pre-installed). Translation needs an internet connection; translating from Japanese needs the Japanese OCR pack ([see above](#japanese-and-other-asian-languages)). The Prompt Builder needs the Microsoft Edge WebView2 Runtime, preinstalled on Windows 11 and on up-to-date Windows 10.
