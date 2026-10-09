using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace ScreenSearchOverlay;

// A UI language: its own name, and its code shown small beside it — the name
// alone is unreadable for someone who doesn't speak it, the code is the same
// in every language.
internal sealed record UiLanguage(string Code, string Name);

// UI strings of the Prompt Builder, the ChatGPT window, the overlay and the
// tray menu.
//
// One table per language: English and French here, the others in
// Languages/Loc.<code>.cs. Adding a language is adding a table and a line in
// Languages. Apply() publishes the current table as an application resource
// dictionary, so every {DynamicResource key} in XAML follows a language
// change live; code reads strings through T().
internal static partial class Loc
{
    internal static readonly UiLanguage[] Languages =
    [
        new("en", "English"),
        new("fr", "Français"),
        new("de", "Deutsch"),
        new("es", "Español"),
        new("it", "Italiano"),
        new("pt-BR", "Português (Brasil)"),
        new("pl", "Polski"),
        new("tr", "Türkçe"),
        new("id", "Bahasa Indonesia"),
        new("vi", "Tiếng Việt"),
        new("ru", "Русский"),
        new("uk", "Українська"),
        new("ja", "日本語"),
        new("ko", "한국어"),
        new("zh-Hans", "简体中文"),
        new("zh-Hant", "繁體中文"),
    ];

    private static readonly Dictionary<string, Dictionary<string, string>> Tables = new()
    {
        ["de"] = German(),
        ["es"] = Spanish(),
        ["it"] = Italian(),
        ["pt-BR"] = Portuguese(),
        ["pl"] = Polish(),
        ["tr"] = Turkish(),
        ["id"] = Indonesian(),
        ["vi"] = Vietnamese(),
        ["ru"] = Russian(),
        ["uk"] = Ukrainian(),
        ["ja"] = Japanese(),
        ["ko"] = Korean(),
        ["zh-Hans"] = ChineseSimplified(),
        ["zh-Hant"] = ChineseTraditional(),
        ["en"] = new()
        {
            ["pb.close"] = "Close (Esc)",
            ["pb.tea"] = "Buy me a tea",
            ["pb.tea.tip"] = "Support the app: a tip by card, through Stripe",
            ["pb.openChat"] = "Open ChatGPT, without sending anything",
            ["pb.update.banner"] = "Version {0} available · click to install",
            ["pb.update.banner.tip"] = "Downloads about {0} MB, then the app restarts on its own",
            ["pb.settings"] = "Settings",
            ["pb.settings.languageRow"] = "Language",
            ["pb.settings.themeRow"] = "Theme",
            ["theme.dark"] = "Dark",
            ["theme.light"] = "Light",
            ["theme.nightdev"] = "Night Dev",
            ["theme.vscode"] = "VS Code",
            ["pb.settings.chatgpt"] = "CHATGPT",
            ["pb.settings.temporary"] = "Temporary chat",
            ["pb.settings.temporary.tip"] = "Nothing is kept in the ChatGPT account's history",
            ["pb.settings.autoSendText"] = "Send text automatically",
            ["pb.settings.autoSendText.tip"] = "Text alone. Unchecked: it is written into ChatGPT, you press Enter yourself",
            ["pb.settings.autoSendImage"] = "Send with an image automatically",
            ["pb.settings.autoSendImage.tip"] = "When an image is attached. Unchecked: you press Enter yourself",
            ["pb.settings.clipboardImage"] = "Clipboard image first",
            ["pb.settings.clipboardImage.tip"] = "An image in the clipboard (Win+Shift+S…) is used instead of a new screenshot",
            ["pb.settings.textSize"] = "TEXT SIZE",
            ["pb.settings.textSizeHint"] = "Also Ctrl+wheel in a text box",
            ["pb.settings.account"] = "ACCOUNT",
            ["pb.settings.newAccount"] = "Generate an account",
            ["pb.settings.newAccount.tip"] = "Opens temp-mail.id in your browser: a temporary address to create a ChatGPT account",
            ["pb.settings.agents"] = "AGENTS (MCP)",
            ["pb.settings.mcp"] = "MCP server",
            ["pb.settings.mcp.tip"] = "Lets an AI agent (Claude Code, Cursor…) list, add and edit your prompts. Off at every start.",
            ["pb.settings.mcpHelp"] = "Set up in an agent…",
            ["mcp.title"] = "MCP server",
            ["mcp.intro"] = "An AI agent can list, add and edit your prompts — ask it \"add a prompt that…\". Add this server to your agent once:",
            ["mcp.claudeCode"] = "CLAUDE CODE (TERMINAL)",
            ["mcp.json"] = "CURSOR, VS CODE AND OTHERS (JSON CONFIGURATION)",
            ["mcp.note"] = "The server only answers this computer, and only while the box is ticked (it is off at every start). An agent can't delete prompts.",
            ["mcp.running"] = "Running on port {0}.",
            ["mcp.stopped"] = "Stopped. Tick “MCP server” in ⚙ to start it.",
            ["mcp.failed"] = "Could not start on port {0}: {1}",
            ["mcp.copy"] = "Copy",
            ["mcp.close"] = "Close (Esc)",
            ["pb.settings.updates"] = "UPDATES",
            ["pb.settings.autoUpdate"] = "Check automatically",
            ["pb.settings.changelog"] = "What's new",
            ["pb.settings.changelog.tip"] = "Opens the changelog on GitHub",
            ["pb.search"] = "Search (Ctrl+F)",
            ["pb.menu.send"] = "Send with this prompt",
            ["pb.key.enter"] = "Enter",
            ["pb.menu.up"] = "Move up",
            ["pb.menu.down"] = "Move down",
            ["pb.menu.duplicate"] = "Duplicate",
            ["pb.menu.delete"] = "Delete",
            ["pb.key.delete"] = "Del",
            ["pb.empty.none"] = "No prompts yet.",
            ["pb.empty.noMatch"] = "No matching prompt.",
            ["pb.empty.create"] = "Create a prompt",
            ["pb.empty.createNamed"] = "Create “{0}”",
            ["pb.new"] = "New prompt",
            ["pb.new.tip"] = "Create a prompt (Ctrl+N)",
            ["pb.section.prompt"] = "PROMPT",
            ["pb.section.text"] = "TEXT",
            ["pb.tip.up"] = "Move up (Alt+↑)",
            ["pb.tip.down"] = "Move down (Alt+↓)",
            ["pb.tip.duplicate"] = "Duplicate (Ctrl+D)",
            ["pb.tip.restore"] = "Restore the original",
            ["pb.tip.delete"] = "Delete (Del in the list)",
            ["pb.tip.splitter"] = "Drag to resize",
            ["pb.name.placeholder"] = "Prompt name (optional)",
            ["pb.prompt.placeholder"] = "Write your instruction. E.g.: Fix and translate this text:",
            ["pb.text.placeholder"] = "No text selected. Type or paste your text here.",
            ["pb.color.default"] = "Default color",
            ["pb.capture"] = "Attach screenshot",
            ["pb.capture.clipboard"] = "Attach clipboard image",
            ["pb.capture.snipTip"] = "Click: capture a region of the screen",
            ["pb.hint.sendKey"] = "Ctrl+Enter",
            ["pb.hint.send"] = " send  ·  ",
            ["pb.hint.pick"] = " pick  ·  ",
            ["pb.hint.escKey"] = "Esc",
            ["pb.hint.close"] = " close",
            ["pb.undo"] = "Undo",
            ["pb.deleted"] = "“{0}” deleted.",
            ["pb.send"] = "Send",
            ["pb.send.tip"] = "Ctrl+Enter",
            ["pb.chars.one"] = "{0} character",
            ["pb.chars.other"] = "{0} characters",
            ["pb.total"] = "Total: {0}",
            ["pb.total.capture"] = " + screenshot",
            ["prompt.untitled"] = "Untitled",
            ["prompt.copySuffix"] = " (copy)",

            ["chat.back"] = "Prompt Builder",
            ["chat.back.tip"] = "Back to the Prompt Builder",
            ["chat.opening"] = "Opening ChatGPT…",
            ["chat.sendingText"] = "Sending the text…",
            ["chat.sendingBoth"] = "Sending the screenshot and the text…",
            ["chat.sent"] = "Sent to ChatGPT in {0}.",
            ["chat.sentNoImage"] = "Sent in {0}, but the screenshot could not be attached. It is in the clipboard: Ctrl+V in the field.",
            ["chat.inserted"] = "Ready in {0}: check the message, then press Enter to send it.",
            ["chat.insertedNoImage"] = "Text ready in {0}, but the screenshot could not be attached. It is in the clipboard: Ctrl+V in the field.",
            ["chat.noComposer"] = "Input field not found. ChatGPT's page may have changed (F12 to inspect it).",
            ["chat.sendNeverReady"] = "The send button never became active. If the text is in the field, press Enter.",
            ["chat.navigated"] = "The page changed during the send. Try again.",
            ["chat.crashed"] = "ChatGPT's page stopped (its process was closed). It reloads by itself: send again.",
            ["chat.pageError"] = "Error in the page: {0}",
            ["chat.timeout"] = "ChatGPT did not respond in time. If the text is in the field, press Enter to send it.",
            ["chat.noRuntime"] = "WebView2 runtime not found. Install “Microsoft Edge WebView2 Runtime”, then try again.",
            ["chat.failed"] = "Send failed: {0}",

            ["tray.tooltip"] = "Screen Search Overlay\nCtrl+Alt+F  search\nCtrl+Alt+G  Prompt Builder",
            ["tray.find"] = "Search the screen",
            ["tray.ocr"] = "OCR languages",
            ["tray.quit"] = "Quit",
            ["app.hotkeyFailed"] = "Could not register {0}.\nAnother program may be using this shortcut.",
            ["app.promptError"] = "Prompt Builder: {0}",
            ["app.alreadyRunning"] = "Already running. Ctrl+Alt+F to search, Ctrl+Alt+G for the Prompt Builder.",

            ["update.check"] = "Check for updates",
            ["update.checking"] = "Checking for updates…",
            ["update.current"] = "Version {0}",
            ["update.upToDate"] = "Version {0} · up to date",
            ["update.upToDateBalloon"] = "You have the latest version (v{0}).",
            ["update.available"] = "Version {0} available (you have {1})",
            ["update.install"] = "Install v{0} and restart",
            ["update.downloading"] = "Downloading the update… {0}",
            ["update.checkFailed"] = "Could not check: {0}",
            ["update.offer.title"] = "Update available",
            ["update.offer"] = "ScreenSearchOverlay v{0} is available. Click here to install it.",
            ["update.confirm"] = "Install ScreenSearchOverlay v{0}?\n\nAbout {1} MB to download, then the app restarts on its own.",
            ["update.installed"] = "Updated to v{0}.",
            ["update.failed"] = "The update failed: {0}",
            ["update.corrupt"] = "the downloaded file is incomplete or damaged",
            ["update.noWrite"] = "cannot write to {0}",
            ["update.openPage"] = "Open the download page?",

            ["ov.regexTip"] = "Regex search",
            ["ov.translateTip"] = "Translate screen",
            ["ov.invalidRegex"] = "invalid regex",
            ["ov.match.one"] = "{0} match",
            ["ov.match.other"] = "{0} matches",
            ["ov.noMatch"] = "no match",
            ["ov.copyAll"] = "Copy all screen text",
            ["ov.allCopied"] = "all text copied",
            ["ov.copyHighlighted"] = "Copy highlighted text",
            ["ov.highlightedCopied"] = "highlighted text copied",
            ["ov.clearHistory"] = "Clear history",
            ["ov.historyCleared"] = "history cleared",
            ["ov.zen"] = "Zen mode",
            ["ov.highlightSize"] = "Highlight size",
            ["ov.searchBarSize"] = "Search bar size",
            ["ov.size.large"] = "large",
            ["ov.size.medium"] = "medium",
            ["ov.size.small"] = "small",
            ["ov.size.tiny"] = "tiny",
            ["ov.translating"] = "translating…",
            ["ov.noText"] = "no text found",
            ["ov.nothingToTranslate"] = "nothing to translate",
            ["ov.translated.one"] = "{0} line translated — click anywhere to close",
            ["ov.translated.other"] = "{0} lines translated — click anywhere to close",
            ["ov.translateFailed"] = "translation failed: {0}",
        },
        ["fr"] = new()
        {
            ["pb.close"] = "Fermer (Échap)",
            ["pb.tea"] = "Paye-moi un thé",
            ["pb.tea.tip"] = "Soutenir l'application : un pourboire par carte, via Stripe",
            ["pb.openChat"] = "Ouvrir ChatGPT, sans rien envoyer",
            ["pb.update.banner"] = "Version {0} disponible · clique pour installer",
            ["pb.update.banner.tip"] = "Télécharge environ {0} Mo, puis l'application redémarre d'elle-même",
            ["pb.settings"] = "Paramètres",
            ["pb.settings.languageRow"] = "Langue",
            ["pb.settings.themeRow"] = "Thème",
            ["theme.dark"] = "Sombre",
            ["theme.light"] = "Clair",
            ["theme.nightdev"] = "Night Dev",
            ["theme.vscode"] = "VS Code",
            ["pb.settings.chatgpt"] = "CHATGPT",
            ["pb.settings.temporary"] = "Discussion temporaire",
            ["pb.settings.temporary.tip"] = "Rien n'est gardé dans l'historique du compte ChatGPT",
            ["pb.settings.autoSendText"] = "Envoyer le texte automatiquement",
            ["pb.settings.autoSendText.tip"] = "Texte seul. Décoché : il est écrit dans ChatGPT, tu appuies toi-même sur Entrée",
            ["pb.settings.autoSendImage"] = "Envoyer avec image automatiquement",
            ["pb.settings.autoSendImage.tip"] = "Quand une image est jointe. Décoché : tu appuies toi-même sur Entrée",
            ["pb.settings.clipboardImage"] = "Priorité à l'image du presse-papier",
            ["pb.settings.clipboardImage.tip"] = "Une image dans le presse-papier (Win+Maj+S…) est utilisée au lieu d'une nouvelle capture d'écran",
            ["pb.settings.textSize"] = "TAILLE DU TEXTE",
            ["pb.settings.textSizeHint"] = "Aussi Ctrl+molette dans un champ",
            ["pb.settings.account"] = "COMPTE",
            ["pb.settings.newAccount"] = "Générer un compte",
            ["pb.settings.newAccount.tip"] = "Ouvre temp-mail.id dans ton navigateur : une adresse temporaire pour créer un compte ChatGPT",
            ["pb.settings.agents"] = "AGENTS (MCP)",
            ["pb.settings.mcp"] = "Serveur MCP",
            ["pb.settings.mcp.tip"] = "Permet à un agent IA (Claude Code, Cursor…) de lister, ajouter et modifier tes prompts. Désactivé à chaque démarrage.",
            ["pb.settings.mcpHelp"] = "Installer dans un agent…",
            ["mcp.title"] = "Serveur MCP",
            ["mcp.intro"] = "Un agent IA peut lister, ajouter et modifier tes prompts — demande-lui « ajoute un prompt qui… ». Ajoute ce serveur à ton agent une seule fois :",
            ["mcp.claudeCode"] = "CLAUDE CODE (TERMINAL)",
            ["mcp.json"] = "CURSOR, VS CODE ET AUTRES (CONFIGURATION JSON)",
            ["mcp.note"] = "Le serveur ne répond qu'à cet ordinateur, et seulement tant que la case est cochée (désactivé à chaque démarrage). Un agent ne peut pas supprimer de prompts.",
            ["mcp.running"] = "Actif sur le port {0}.",
            ["mcp.stopped"] = "Arrêté. Coche « Serveur MCP » dans ⚙ pour le démarrer.",
            ["mcp.failed"] = "Impossible de démarrer sur le port {0} : {1}",
            ["mcp.copy"] = "Copier",
            ["mcp.close"] = "Fermer (Échap)",
            ["pb.settings.updates"] = "MISES À JOUR",
            ["pb.settings.autoUpdate"] = "Vérifier automatiquement",
            ["pb.settings.changelog"] = "Nouveautés",
            ["pb.settings.changelog.tip"] = "Ouvre le journal des versions sur GitHub",
            ["pb.search"] = "Rechercher (Ctrl+F)",
            ["pb.menu.send"] = "Envoyer avec ce prompt",
            ["pb.key.enter"] = "Entrée",
            ["pb.menu.up"] = "Monter",
            ["pb.menu.down"] = "Descendre",
            ["pb.menu.duplicate"] = "Dupliquer",
            ["pb.menu.delete"] = "Supprimer",
            ["pb.key.delete"] = "Suppr",
            ["pb.empty.none"] = "Aucun prompt pour l'instant.",
            ["pb.empty.noMatch"] = "Aucun prompt ne correspond.",
            ["pb.empty.create"] = "Créer un prompt",
            ["pb.empty.createNamed"] = "Créer « {0} »",
            ["pb.new"] = "Nouveau prompt",
            ["pb.new.tip"] = "Créer un prompt (Ctrl+N)",
            ["pb.section.prompt"] = "PROMPT",
            ["pb.section.text"] = "TEXTE",
            ["pb.tip.up"] = "Monter (Alt+↑)",
            ["pb.tip.down"] = "Descendre (Alt+↓)",
            ["pb.tip.duplicate"] = "Dupliquer (Ctrl+D)",
            ["pb.tip.restore"] = "Restaurer l'original",
            ["pb.tip.delete"] = "Supprimer (Suppr dans la liste)",
            ["pb.tip.splitter"] = "Glisser pour redimensionner",
            ["pb.name.placeholder"] = "Nom du prompt (facultatif)",
            ["pb.prompt.placeholder"] = "Écris ta consigne. Ex. : Corrige et traduis ce texte :",
            ["pb.text.placeholder"] = "Aucun texte sélectionné. Écris ou colle ton texte ici.",
            ["pb.color.default"] = "Couleur par défaut",
            ["pb.capture"] = "Joindre la capture",
            ["pb.capture.clipboard"] = "Joindre l'image du presse-papier",
            ["pb.capture.snipTip"] = "Clic : capturer une zone de l'écran",
            ["pb.hint.sendKey"] = "Ctrl+Entrée",
            ["pb.hint.send"] = " envoyer  ·  ",
            ["pb.hint.pick"] = " choisir  ·  ",
            ["pb.hint.escKey"] = "Échap",
            ["pb.hint.close"] = " fermer",
            ["pb.undo"] = "Annuler",
            ["pb.deleted"] = "« {0} » supprimé.",
            ["pb.send"] = "Envoyer",
            ["pb.send.tip"] = "Ctrl+Entrée",
            ["pb.chars.one"] = "{0} caractère",
            ["pb.chars.other"] = "{0} caractères",
            ["pb.total"] = "Total : {0}",
            ["pb.total.capture"] = " + capture",
            ["prompt.untitled"] = "Sans titre",
            ["prompt.copySuffix"] = " (copie)",

            ["chat.back"] = "Prompt Builder",
            ["chat.back.tip"] = "Revenir au Prompt Builder",
            ["chat.opening"] = "Ouverture de ChatGPT…",
            ["chat.sendingText"] = "Envoi du texte…",
            ["chat.sendingBoth"] = "Envoi de la capture et du texte…",
            ["chat.sent"] = "Envoyé à ChatGPT en {0}.",
            ["chat.sentNoImage"] = "Envoyé en {0}, mais la capture n'a pas pu être jointe. Elle est dans le presse-papier : Ctrl+V dans le champ.",
            ["chat.inserted"] = "Prêt en {0} : vérifie le message, puis appuie sur Entrée pour l'envoyer.",
            ["chat.insertedNoImage"] = "Texte prêt en {0}, mais la capture n'a pas pu être jointe. Elle est dans le presse-papier : Ctrl+V dans le champ.",
            ["chat.noComposer"] = "Champ de saisie introuvable. La page de ChatGPT a peut-être changé (F12 pour l'inspecter).",
            ["chat.sendNeverReady"] = "Le bouton d'envoi n'est jamais devenu actif. Si le texte est dans le champ, appuie sur Entrée.",
            ["chat.navigated"] = "La page a changé pendant l'envoi. Réessaie.",
            ["chat.crashed"] = "La page ChatGPT s'est arrêtée (son processus a été fermé). Elle se recharge d'elle-même : renvoie.",
            ["chat.pageError"] = "Erreur dans la page : {0}",
            ["chat.timeout"] = "ChatGPT n'a pas répondu à temps. Si le texte est dans le champ, appuie sur Entrée pour l'envoyer.",
            ["chat.noRuntime"] = "Le runtime WebView2 est introuvable. Installe « Microsoft Edge WebView2 Runtime », puis réessaie.",
            ["chat.failed"] = "Envoi impossible : {0}",

            ["tray.tooltip"] = "Screen Search Overlay\nCtrl+Alt+F  chercher\nCtrl+Alt+G  Prompt Builder",
            ["tray.find"] = "Chercher à l'écran",
            ["tray.ocr"] = "Langues OCR",
            ["tray.quit"] = "Quitter",
            ["app.hotkeyFailed"] = "Impossible d'enregistrer {0}.\nUn autre programme utilise peut-être ce raccourci.",
            ["app.promptError"] = "Prompt Builder : {0}",
            ["app.alreadyRunning"] = "Déjà ouvert. Ctrl+Alt+F pour chercher, Ctrl+Alt+G pour le Prompt Builder.",

            ["update.check"] = "Rechercher les mises à jour",
            ["update.checking"] = "Recherche de mises à jour…",
            ["update.current"] = "Version {0}",
            ["update.upToDate"] = "Version {0} · à jour",
            ["update.upToDateBalloon"] = "Tu as la dernière version (v{0}).",
            ["update.available"] = "Version {0} disponible (tu as la {1})",
            ["update.install"] = "Installer la v{0} et redémarrer",
            ["update.downloading"] = "Téléchargement de la mise à jour… {0}",
            ["update.checkFailed"] = "Vérification impossible : {0}",
            ["update.offer.title"] = "Mise à jour disponible",
            ["update.offer"] = "ScreenSearchOverlay v{0} est disponible. Clique ici pour l'installer.",
            ["update.confirm"] = "Installer ScreenSearchOverlay v{0} ?\n\nEnviron {1} Mo à télécharger, puis l'application redémarre d'elle-même.",
            ["update.installed"] = "Mis à jour vers la v{0}.",
            ["update.failed"] = "La mise à jour a échoué : {0}",
            ["update.corrupt"] = "le fichier téléchargé est incomplet ou endommagé",
            ["update.noWrite"] = "impossible d'écrire dans {0}",
            ["update.openPage"] = "Ouvrir la page de téléchargement ?",

            ["ov.regexTip"] = "Recherche par regex",
            ["ov.translateTip"] = "Traduire l'écran",
            ["ov.invalidRegex"] = "regex invalide",
            ["ov.match.one"] = "{0} résultat",
            ["ov.match.other"] = "{0} résultats",
            ["ov.noMatch"] = "aucun résultat",
            ["ov.copyAll"] = "Copier tout le texte de l'écran",
            ["ov.allCopied"] = "tout le texte copié",
            ["ov.copyHighlighted"] = "Copier le texte surligné",
            ["ov.highlightedCopied"] = "texte surligné copié",
            ["ov.clearHistory"] = "Effacer l'historique",
            ["ov.historyCleared"] = "historique effacé",
            ["ov.zen"] = "Mode zen",
            ["ov.highlightSize"] = "Taille du surlignage",
            ["ov.searchBarSize"] = "Taille de la barre de recherche",
            ["ov.size.large"] = "grande",
            ["ov.size.medium"] = "moyenne",
            ["ov.size.small"] = "petite",
            ["ov.size.tiny"] = "minuscule",
            ["ov.translating"] = "traduction…",
            ["ov.noText"] = "aucun texte trouvé",
            ["ov.nothingToTranslate"] = "rien à traduire",
            ["ov.translated.one"] = "{0} ligne traduite — clique n'importe où pour fermer",
            ["ov.translated.other"] = "{0} lignes traduites — clique n'importe où pour fermer",
            ["ov.translateFailed"] = "échec de la traduction : {0}",
        },
    };

    private static ResourceDictionary? _published;

    internal static string Current { get; private set; } = "en";

    // Numbers and durations follow the language: "1 320" and "0,8 s" in
    // French, "1,320" and "0.8 s" in English.
    internal static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-CA");

    internal static event Action? Changed;

    // The system's UI language when we have it, English otherwise. Chinese
    // goes by script (Taiwan, Hong Kong, Macao: traditional), Portuguese to
    // the Brazilian table, the only one.
    internal static string DefaultCode()
    {
        var ui = CultureInfo.CurrentUICulture;
        var code = ui.TwoLetterISOLanguageName switch
        {
            "zh" => IsTraditionalChinese(ui) ? "zh-Hant" : "zh-Hans",
            "pt" => "pt-BR",
            var two => two,
        };
        return Tables.ContainsKey(code) ? code : "en";
    }

    private static bool IsTraditionalChinese(CultureInfo culture)
    {
        for (var c = culture; !string.IsNullOrEmpty(c.Name); c = c.Parent)
            if (c.Name is "zh-Hant" or "zh-TW" or "zh-HK" or "zh-MO") return true;
        return false;
    }

    internal static void Apply(string code)
    {
        if (!Tables.ContainsKey(code)) code = "en";
        Current = code;
        Culture = CultureFor(code);
        TagWindows(code);

        var dictionary = new ResourceDictionary();
        foreach (var (key, value) in Tables[code])
            dictionary[key] = value;

        var merged = System.Windows.Application.Current.Resources.MergedDictionaries;
        if (_published is not null) merged.Remove(_published);
        merged.Add(dictionary);
        _published = dictionary;

        Changed?.Invoke();
    }

    // A missing key falls back to English, then to the key itself — visible,
    // never a crash.
    internal static string T(string key) =>
        Tables[Current].TryGetValue(key, out var text) ? text
        : Tables["en"].TryGetValue(key, out var english) ? english
        : key;

    internal static string T(string key, params object[] arguments) => string.Format(Culture, T(key), arguments);

    // Japanese, Chinese and Korean share thousands of characters drawn
    // differently in each (直, 骨, 角…). WPF picks the font for them from the
    // element's language: without it, Japanese can come out in a Chinese
    // font. That language is the UI's when it is one of the three, else the
    // first of them in the user's Windows languages — the text boxes hold
    // what the user writes, not what the UI says. Latin text is unaffected.
    // Every window carries it: the open ones now, later ones (the overlay,
    // each time) as they load.
    private static bool _tagsNewWindows;
    private static XmlLanguage _windowLanguage = XmlLanguage.GetLanguage("en");

    private static void TagWindows(string code)
    {
        _windowLanguage = XmlLanguage.GetLanguage(HanLanguage(code) ?? code);
        foreach (Window window in System.Windows.Application.Current.Windows)
            window.Language = _windowLanguage;
        if (_tagsNewWindows) return;
        _tagsNewWindows = true;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is Window window && window.Language != _windowLanguage) window.Language = _windowLanguage;
        }));
    }

    // ja, ko, zh-Hans or zh-Hant; null when the user reads none of them.
    private static string? HanLanguage(string uiCode)
    {
        if (uiCode is "ja" or "ko" or "zh-Hans" or "zh-Hant") return uiCode;
        try
        {
            foreach (var tag in Windows.System.UserProfile.GlobalizationPreferences.Languages)
            {
                var lower = tag.ToLowerInvariant();
                if (lower.StartsWith("ja")) return "ja";
                if (lower.StartsWith("ko")) return "ko";
                if (lower.StartsWith("zh"))
                    return lower.Contains("hant") || lower.EndsWith("-tw") || lower.EndsWith("-hk") || lower.EndsWith("-mo")
                        ? "zh-Hant" : "zh-Hans";
            }
        }
        catch { /* not available: the UI language decides */ }
        return null;
    }

    // Canadian English and French, as before; the others in their own
    // culture (pt-BR, zh-Hans… are all known to .NET).
    private static CultureInfo CultureFor(string code)
    {
        try { return CultureInfo.GetCultureInfo(code is "en" or "fr" ? code + "-CA" : code); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }

    internal static string Characters(int count) => Plural("pb.chars", count);

    // key.one / .few / .many / .other with the count formatted for the
    // language; a table without that form uses .other. French and English
    // disagree on zero ("0 caractère", "0 characters"); Russian, Ukrainian
    // and Polish have three forms (1 символ, 2 символа, 5 символов);
    // Japanese, Korean, Chinese, Indonesian, Vietnamese and Turkish one.
    internal static string Plural(string key, int count)
    {
        var form = key + "." + PluralForm(count);
        if (!Tables[Current].ContainsKey(form)) form = key + ".other";
        return T(form, count.ToString("N0", Culture));
    }

    private static string PluralForm(int n)
    {
        var tens = n % 100;
        var fewEnding = n % 10 is >= 2 and <= 4 && tens is < 12 or > 14;
        return Current switch
        {
            "fr" or "pt-BR" => n <= 1 ? "one" : "other",
            "ru" or "uk" => n % 10 == 1 && tens != 11 ? "one" : fewEnding ? "few" : "many",
            "pl" => n == 1 ? "one" : fewEnding ? "few" : "many",
            "ja" or "ko" or "zh-Hans" or "zh-Hant" or "id" or "vi" or "tr" => "other",
            _ => n == 1 ? "one" : "other",
        };
    }
}
