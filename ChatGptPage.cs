using System.Text.Json;

namespace ScreenSearchOverlay;

// Everything this app knows about chatgpt.com, in one place — no UI
// dependency. OpenAI redesigns its composer every few months: when sending
// breaks, this is the only file to fix. F12 in the ChatGPT window opens the
// DevTools to re-read the page.
//
// Selectors come from LSDE2's webviewFallbackServices.const.ts and were
// re-read in the live page on 2026-10-04 (logged out). Each one lists several
// candidates, most precise first: querySelector takes the first match, which
// lets the recipe survive a redesign rolled out to only part of the users.
//
// The scripts are plain strings run through ExecuteScriptAsync. Unlike
// Electron's executeJavaScript, ExecuteScriptAsync does NOT await a returned
// Promise (it hands back "{}"), so the async send script reports through
// chrome.webview.postMessage instead of returning.
internal static class ChatGptPage
{
    // Every send starts from a blank conversation. Temporary (the default
    // setting): nothing lands in the account's history.
    internal static string NewConversationUrl(bool temporary) =>
        temporary ? "https://chatgpt.com/?temporary-chat=true" : "https://chatgpt.com/";

    private const string PromptSelector =
        "div#prompt-textarea[contenteditable=\"true\"], " +
        "textarea#prompt-textarea, " +
        "div.ProseMirror[contenteditable=\"true\"], " +
        "textarea.wm-composer-textarea, " +
        "form textarea[name=\"prompt\"], " +
        "form div[contenteditable=\"true\"]";

    // ':not([aria-disabled="true"])' matters: the 2026 composer keeps its
    // button in the DOM and switches it off with aria-disabled, not with the
    // native attribute. Clicking it then sends nothing.
    private const string SendSelector =
        "button[data-testid=\"send-button\"]:not([disabled]), " +
        "button#composer-submit-button:not([disabled]), " +
        "form button[data-composer-submit]:not([disabled]):not([aria-disabled=\"true\"])";

    // The cookie banner, dismissed by REFUSING non-essential cookies — never
    // by accepting them on the user's behalf. Designated by the form's hidden
    // action value, not by its hashed class names or its translated text.
    private const string ConsentRejectSelector =
        "form[data-privacy-consent-form]:has(input[name=\"action\"][value=\"reject\"]) button[type=\"submit\"]";

    // Fallback when the composer ignores a synthetic image paste.
    private const string ImageInputSelector =
        "input[type=\"file\"][accept*=\"image/png\"], input[type=\"file\"][accept*=\"image\"]";

    // How long a just-loaded page may take to start listening (see SendScript).
    internal const int PageBootTimeoutMs = 10_000;
    internal const int ImageUploadTimeoutMs = 30_000;
    internal const int SendTimeoutMs = 15_000;

    // Synchronous probe: "true" once the composer exists, i.e. the page is
    // hydrated and will not swap its document under the send script.
    internal static string ReadyProbeScript =>
        $"document.querySelector({Literal(PromptSelector)}) !== null";

    // Synchronous: "true" when the banner was there and got refused. Refusing
    // posts a form, so the page reloads right after.
    internal static string RejectCookiesScript => $$"""
        (() => {
          const button = document.querySelector({{Literal(ConsentRejectSelector)}});
          if (!button) return false;
          button.click();
          return true;
        })()
        """;

    // Attach the screenshot (optional), type the text, wait for the send
    // button to come alive, click it — or, submit false, leave the message
    // in the composer for the user to send. Posts { id, ok, at,
    // imageAttached, pastes } — pastes: how many tries the page took to
    // accept the image.
    //
    // The composer is server-rendered: it is in the page before the scripts
    // that listen to it. A send into a document loaded a moment ago — the
    // second send, after "back to the Prompt Builder", which starts a new
    // conversation right then — hits that window (measured: the image paste
    // ignored, the text sent alone 30 s later). The page's paste handler is
    // what takes the image, and it cancels the event: an uncancelled paste
    // means nobody listens yet, so paste again. Typed text has no such
    // signal: when the button stays off, type it again.
    //
    // Order verified on the live page: the image goes first, into an EMPTY
    // composer, because its upload switches the send button off and an image
    // alone switches it back on when the upload is done. That is the only
    // reliable "upload finished" signal; with text already typed the button
    // would stay on throughout.
    //
    // Every wait is a MutationObserver: it reacts to the very DOM change (the
    // button losing aria-disabled) instead of discovering it at the next poll.
    //
    // Typing tries execCommand("insertText") first — the path a real keystroke
    // takes, which the page's editor framework hears — then a synthetic paste,
    // then a direct write (enough for a plain textarea). Same ladder as
    // LSDE2's typeAndSend; text on several lines goes paste first (see type).
    internal static string SendScript(string requestId, string text, string? pngBase64, bool submit) => $$"""
        (async () => {
          const id = {{Literal(requestId)}};
          const sendSelector = {{Literal(SendSelector)}};
          const post = (result) => window.chrome.webview.postMessage(Object.assign({ id: id }, result));
          const sleep = (ms) => new Promise((done) => setTimeout(done, ms));
          const waitFor = (selector, ms) => new Promise((resolve) => {
            const now = document.querySelector(selector);
            if (now) return resolve(now);
            let timer = 0;
            const observer = new MutationObserver(() => {
              const found = document.querySelector(selector);
              if (!found) return;
              clearTimeout(timer);
              observer.disconnect();
              resolve(found);
            });
            observer.observe(document.documentElement, {
              subtree: true, childList: true, attributes: true, attributeFilter: ["disabled", "aria-disabled"],
            });
            timer = setTimeout(() => { observer.disconnect(); resolve(document.querySelector(selector)); }, ms);
          });
          try {
            const field = document.querySelector({{Literal(PromptSelector)}});
            if (!field) return post({ ok: false, at: "prompt" });
            const isTextarea = typeof field.value === "string";
            const selectAll = () => {
              field.focus();
              if (isTextarea) { field.select(); return; }
              const range = document.createRange();
              range.selectNodeContents(field);
              const selection = window.getSelection();
              selection.removeAllRanges();
              selection.addRange(range);
            };
            field.focus();

            let imageAttached = null;
            let pastes = 0;
            const image = {{(pngBase64 is null ? "null" : Literal(pngBase64))}};
            if (image !== null) {
              // The upload signal needs an empty composer (see above).
              if ((isTextarea ? field.value : field.textContent).length > 0) {
                selectAll();
                document.execCommand("delete");
              }
              // One charCodeAt per byte in a plain loop: Uint8Array.from with a
              // map callback costs a function call per character (1.2 M here).
              const binary = atob(image);
              const bytes = new Uint8Array(binary.length);
              for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
              const transfer = new DataTransfer();
              transfer.items.add(new File([bytes], "capture.png", { type: "image/png" }));
              // Cancelled = taken by the page (see above). A synthetic paste
              // has no default action: repeating it never pastes twice.
              const paste = () => {
                pastes++;
                return !field.dispatchEvent(
                  new ClipboardEvent("paste", { clipboardData: transfer, bubbles: true, cancelable: true }));
              };
              let handled = paste();
              for (const until = Date.now() + {{PageBootTimeoutMs}}; !handled && Date.now() < until;) {
                await sleep(100);
                handled = paste();
              }
              if (!handled) {
                const input = document.querySelector({{Literal(ImageInputSelector)}});
                if (input) {
                  input.files = transfer.files;
                  input.dispatchEvent(new Event("change", { bubbles: true }));
                }
              }
              // The button may flick on for an instant as the attachment
              // appears, before its upload starts: confirm it stays on.
              let ready = await waitFor(sendSelector, {{ImageUploadTimeoutMs}});
              if (ready) {
                await sleep(120);
                ready = await waitFor(sendSelector, {{ImageUploadTimeoutMs}});
              }
              imageAttached = ready !== null;
            }

            const text = {{Literal(text)}};
            // Replaces the whole content: typing it again changes nothing.
            const insert = () => {
              try { return document.execCommand("insertText", false, text); } catch (e) { return false; }
            };
            const paste = () => {
              try {
                const transfer = new DataTransfer();
                transfer.setData("text/plain", text);
                return !field.dispatchEvent(
                  new ClipboardEvent("paste", { clipboardData: transfer, bubbles: true, cancelable: true }));
              } catch (e) { return false; }
            };
            // The composer's editor drops the line breaks of inserted text
            // ("prompt:text" on one line) but keeps those of a paste: text on
            // several lines is pasted first. A plain textarea keeps them either way.
            const multiline = !isTextarea && text.includes("\n");
            const type = () => {
              selectAll();
              let typed = multiline ? paste() || insert() : insert() || paste();
              if (!typed) {
                if (isTextarea) field.value = text;
                else field.textContent = text;
                field.dispatchEvent(new Event("input", { bubbles: true }));
              }
            };
            if (text.length > 0) type();

            // Text typed before the page listens is in the field, but the page
            // doesn't know it and keeps its button off (see above). Waited
            // for even when not submitting: a live button is the proof the
            // page took the message, and Enter will send it.
            let button = await waitFor(sendSelector, 500);
            for (const until = Date.now() + {{SendTimeoutMs}}; !button && Date.now() < until;) {
              if (text.length > 0) type();
              button = await waitFor(sendSelector, 500);
            }
            if (!button) return post({ ok: false, at: "send", imageAttached: imageAttached, pastes: pastes });
            // Reported BEFORE the click: if sending navigates the page, this
            // script dies with the document and could never report after.
            post({ ok: true, at: "", imageAttached: imageAttached, pastes: pastes });
            if ({{(submit ? "true" : "false")}}) button.click();
            else field.focus();
          } catch (error) {
            post({ ok: false, at: "script", detail: String(error) });
          }
        })();
        """;

    // ── Theme ──

    // The app's theme over ChatGPT's colors. The page draws everything from
    // CSS custom properties on <html> (.dark / .light): overriding them
    // recolors it without touching its layout. Two generations of names are
    // listed — an unknown one is harmless — so a partial rollout still takes.
    internal static string ThemeCss(PageColors c) => $$"""
        html, html.dark, html.light, .dark, .light {
          --main-surface-primary: {{c.Page}} !important;
          --main-surface-secondary: {{c.Raised}} !important;
          --main-surface-tertiary: {{c.Hover}} !important;
          --sidebar-surface-primary: {{c.Sidebar}} !important;
          --sidebar-surface-secondary: {{c.Hover}} !important;
          --sidebar-surface-tertiary: {{c.Selected}} !important;
          --bg-primary: {{c.Page}} !important;
          --bg-secondary: {{c.Raised}} !important;
          --bg-tertiary: {{c.Hover}} !important;
          --bg-elevated-primary: {{c.Raised}} !important;
          --bg-elevated-secondary: {{c.Raised}} !important;
          --message-surface: {{c.Raised}} !important;
          --composer-surface-primary: {{c.Raised}} !important;
          --text-primary: {{c.Text}} !important;
          --text-secondary: {{c.TextSecondary}} !important;
          --text-tertiary: {{c.TextMuted}} !important;
          --border-light: {{c.Line}} !important;
          --border-medium: {{c.Border}} !important;
          --border-default: {{c.Border}} !important;
        }
        html, body, main { background-color: {{c.Page}} !important; }
        nav, aside, #stage-slideover-sidebar { background-color: {{c.Sidebar}} !important; }
        {{SendButtons}} { background-color: {{c.Accent}} !important; color: #fff !important; }
        {{SendButtons.Replace(",", " *,")}} * { background-color: transparent !important; }
        """;

    // Every send button state, enabled or not (SendSelector without its :not).
    private static string SendButtons => SendSelector.Replace(":not([disabled])", "");

    // In every theme: a desktop application, not a phone one. Square corners
    // (2 px at most) and a 14 px base instead of 16 — the page sizes its
    // paddings and gaps in rem, so everything tightens with the text.
    internal const string CompactCss = """
        html { font-size: 14px !important; }
        *, *::before, *::after { border-radius: 2px !important; }

        """;

    // Runs in every new document (AddScriptToExecuteOnDocumentCreated) and,
    // on a theme change, in the current one: CompactCss, plus ThemeCss
    // except in the dark theme (ChatGPT's own colors).
    //
    // A constructed stylesheet, not a <style> element: the page's
    // Content-Security-Policy may refuse inline styles but not the CSSOM, and
    // its framework cannot drop it when it rebuilds <head>. It is adopted
    // once the document can take it — at creation time there is no page yet —
    // and kept in window so a theme change only replaces its rules.
    internal static string ThemeScript(string css) => $$"""
        (() => {
          const css = {{Literal(css)}};
          let sheet = window.__lsThemeSheet;
          if (!sheet) {
            sheet = window.__lsThemeSheet = new CSSStyleSheet();
          }
          sheet.replaceSync(css);
          const adopt = () => {
            if (!document.adoptedStyleSheets.includes(sheet))
              document.adoptedStyleSheets = [...document.adoptedStyleSheets, sheet];
          };
          try { adopt(); } catch (e) { }
          document.addEventListener("DOMContentLoaded", adopt);
          window.addEventListener("load", adopt);
        })();
        """;

    // A JSON string is a valid JS string literal, quotes and newlines
    // escaped: user text can never break out of the injected script.
    private static string Literal(string value) => JsonSerializer.Serialize(value);
}

// The app's theme, as "#RRGGBB" strings for the page's CSS (see ThemeCss).
internal sealed record PageColors(
    string Page, string Raised, string Hover, string Selected, string Sidebar,
    string Text, string TextSecondary, string TextMuted, string Line, string Border, string Accent);
