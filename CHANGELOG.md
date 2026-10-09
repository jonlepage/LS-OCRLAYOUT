# Changelog

## 1.6.0

### New

- **Capture a region**: click the screenshot thumbnail and pick any part of the screen with Windows' snipping tool. The region replaces the screenshot and is attached right away.
- **Clipboard image first** (⚙): an image you just copied (Win+Shift+S…) is used instead of a new screenshot, and attached. Each image is used once.
- **Themes** (⚙ > Theme): **Light** (soft greys, easy on the eyes), **Night Dev** (dark, orange and fuchsia) and **VS Code** (the editor's dark colors). Dark stays the default.
- **ChatGPT follows the theme**: its page takes the theme's colors, and gets a desktop look in every theme — square corners, smaller text, tighter spacing.
- **MCP server** (⚙ > Agents): an AI agent (Claude Code, Cursor…) can list, add and edit your prompts — "add a prompt that…". Ticking it shows the line to give your agent, with a copy button. Off at every start; agents can't delete prompts.
- **Automatic send** split in two: for text alone, and for a message with an image.
- **What's new** link in ⚙.

### Improved

- One window at a time: going back to the Prompt Builder hides ChatGPT instead of leaving both on screen. The answer is kept: the ChatGPT button shows it again.
- ChatGPT recovers by itself when its browser process is closed (a cleanup script, a crash): no need to restart the app.
- Language and theme are submenus in ⚙.
- Only one copy of the app runs: launching it again shows a reminder in the tray.
- The Prompt Builder comes back in front after a region capture, instead of only flashing in the taskbar.

### Fixed

- The last selected prompt was not restored at startup.
- Prompt and text arrived in ChatGPT on one line ("…translation:your text"): they are now on separate lines.
- A second copy of the app could not get the hotkeys and overwrote the settings of the first.
- The "Send automatically" choice made in 1.5.2 is kept.

## 1.5.2

- **Automatic updates**: checked at startup, at most once a day. A newer version shows in the Prompt Builder's title bar; one click downloads, installs and restarts.
- **Open ChatGPT** without sending anything: new button next to ⚙.
- **Temporary chat** can be turned off (⚙).
- **Send automatically** can be turned off: the message is written into ChatGPT, you press Enter (⚙).
- The send button now just says **Send**.
- The last selected prompt is remembered right away, even if the app is closed abruptly.

## 1.5.1

- **Generate an account** (⚙): opens a temporary email address to sign up a ChatGPT account.

## 1.5.0

- **Prompt Builder** (Ctrl+Alt+G): send the selected text to ChatGPT with a saved prompt, screenshot optional.
- English / Français interface.

## 1.4.0

- **Screen translation** (文A): translates every line of text on screen, in place.

## 1.3.0

- Scroll-through: the mouse wheel scrolls the app under the overlay.
- Faster OCR.

## 1.2.1

- The screen is captured before the overlay appears.

## 1.1.0

- Menu, regex search, search history, zen mode, movable search bar.

## 1.0.0

- OCR screen search with Ctrl+Alt+F.
