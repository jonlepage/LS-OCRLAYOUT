# Changelog

## 1.7.0

- **Show me where**: lost in an app your AI agent (Claude Code, Cursor…) is walking you through? Ask "where do I click?" — it circles the spot on your real screen, in any app. Tick **⚙ > MCP server** to use it.
- **Explain this screen**: the agent frames each part of an app in its own color, numbered, with what it is for.
- **Step-by-step guide**: ask "how do I change…" and the agent walks you through the clicks. It circles each one on the live app, you click the real button, and the next step appears. A panel at the top right lists the steps; **Esc** or its ✕ leaves the guide.
- To show you where to click, the agent captures the app's window and sends it to its AI. Only while **MCP server** is ticked; it is off at every start.

## 1.6.4

- **Text history**: each prompt remembers its last 100 texts. Go back to them with **▲ ▼** above the text, or with `↑` / `↓` in it, as in a terminal.
- Fixed: Ctrl+Alt+G with nothing selected emptied the text — your last text now stays, even after a restart.

## 1.6.3

- The app can now start with Windows: tick **Start with Windows** in ⚙ or in the tray menu.
- The exe is now signed by its author: see **Verify the download** in the README to check that your copy is genuine.

## 1.6.2

- Fixed: signed in to a ChatGPT account, a screenshot sent with text never left — the text took half a minute to appear and nothing was sent.
- Fixed: signed in to a ChatGPT account, messages were no longer sent automatically and an error showed, even though the message was written.

## 1.6.1

- Fixed: clicking **Send** a second time sometimes did nothing — the window closed and ChatGPT never opened.
- Fixed: text in ChatGPT could not be selected for a few seconds after sending.
- The ChatGPT window no longer opens hidden behind your other windows.

## 1.6.0

- **Capture any part of the screen**: click the screenshot thumbnail.
- **Use the image you just copied** instead of a new screenshot.
- **16 languages**: Deutsch, Español, Italiano, Português, Polski, Türkçe, Bahasa Indonesia, Tiếng Việt, Русский, Українська, 日本語, 한국어, 简体中文 and 繁體中文 join English and Français.
- **Starter prompts follow the language** until you edit them; an edited one can be restored in one click.
- **Tray menu**: open the screen search or the Prompt Builder with a click, and change the language.
- **New themes**: Light, Night Dev and VS Code. ChatGPT follows.
- **ChatGPT, desktop style**: square corners, smaller text, less wasted space.
- **Add prompts from an AI agent** (Claude Code, Cursor…) with the built-in MCP server.
- **Auto-send** set separately for text and for images.
- One window at a time, and ChatGPT recovers on its own if it crashes.
- Fixes: last prompt remembered, prompt and text on separate lines, no more duplicate app.
- Like the app? **Buy me a tea**: the new link in the Prompt Builder's title bar.

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
