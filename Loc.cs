using System.Globalization;
using System.Windows;

namespace ScreenSearchOverlay;

// A UI language. Always shown as "name (code)": the name alone is unreadable
// for someone who doesn't speak it, the ISO 639-1 code is the same in every
// language.
internal sealed record UiLanguage(string Code, string Name)
{
    public string Label => $"{Name} ({Code})";
}

// UI strings of the Prompt Builder, the ChatGPT window and the tray menu.
//
// One table per language; adding a language is adding a table and a line in
// Languages. Apply() publishes the current table as an application resource
// dictionary, so every {DynamicResource key} in XAML follows a language
// change live; code reads strings through T().
internal static class Loc
{
    internal static readonly UiLanguage[] Languages = [new("en", "English"), new("fr", "Français")];

    private static readonly Dictionary<string, Dictionary<string, string>> Tables = new()
    {
        ["en"] = new()
        {
            ["pb.chip"] = "ChatGPT · temporary chat",
            ["pb.close"] = "Close (Esc)",
            ["pb.settings"] = "Settings",
            ["pb.settings.language"] = "LANGUAGE",
            ["pb.settings.textSize"] = "TEXT SIZE",
            ["pb.settings.textSizeHint"] = "Also Ctrl+wheel in a text box",
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
            ["pb.tip.delete"] = "Delete (Del in the list)",
            ["pb.tip.splitter"] = "Drag to resize",
            ["pb.name.placeholder"] = "Prompt name (optional)",
            ["pb.prompt.placeholder"] = "Write your instruction. E.g.: Fix and translate this text:",
            ["pb.text.placeholder"] = "No text selected. Type or paste your text here.",
            ["pb.color.default"] = "Default color",
            ["pb.capture"] = "Attach screenshot",
            ["pb.hint.sendKey"] = "Ctrl+Enter",
            ["pb.hint.send"] = " send  ·  ",
            ["pb.hint.pick"] = " pick  ·  ",
            ["pb.hint.escKey"] = "Esc",
            ["pb.hint.close"] = " close",
            ["pb.undo"] = "Undo",
            ["pb.deleted"] = "“{0}” deleted.",
            ["pb.send"] = "Send to ChatGPT",
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
            ["chat.noComposer"] = "Input field not found. ChatGPT's page may have changed (F12 to inspect it).",
            ["chat.sendNeverReady"] = "The send button never became active. If the text is in the field, press Enter.",
            ["chat.navigated"] = "The page changed during the send. Try again.",
            ["chat.pageError"] = "Error in the page: {0}",
            ["chat.timeout"] = "ChatGPT did not respond in time. If the text is in the field, press Enter to send it.",
            ["chat.noRuntime"] = "WebView2 runtime not found. Install “Microsoft Edge WebView2 Runtime”, then try again.",
            ["chat.failed"] = "Send failed: {0}",

            ["tray.findHint"] = "Ctrl+Alt+F to search",
            ["tray.promptHint"] = "Ctrl+Alt+G for the Prompt Builder",
            ["tray.ocr"] = "OCR languages",
            ["tray.quit"] = "Quit",
            ["app.hotkeyFailed"] = "Could not register {0}.\nAnother program may be using this shortcut.",
            ["app.promptError"] = "Prompt Builder: {0}",
        },
        ["fr"] = new()
        {
            ["pb.chip"] = "ChatGPT · discussion temporaire",
            ["pb.close"] = "Fermer (Échap)",
            ["pb.settings"] = "Paramètres",
            ["pb.settings.language"] = "LANGUE",
            ["pb.settings.textSize"] = "TAILLE DU TEXTE",
            ["pb.settings.textSizeHint"] = "Aussi Ctrl+molette dans un champ",
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
            ["pb.tip.delete"] = "Supprimer (Suppr dans la liste)",
            ["pb.tip.splitter"] = "Glisser pour redimensionner",
            ["pb.name.placeholder"] = "Nom du prompt (facultatif)",
            ["pb.prompt.placeholder"] = "Écris ta consigne. Ex. : Corrige et traduis ce texte :",
            ["pb.text.placeholder"] = "Aucun texte sélectionné. Écris ou colle ton texte ici.",
            ["pb.color.default"] = "Couleur par défaut",
            ["pb.capture"] = "Joindre la capture",
            ["pb.hint.sendKey"] = "Ctrl+Entrée",
            ["pb.hint.send"] = " envoyer  ·  ",
            ["pb.hint.pick"] = " choisir  ·  ",
            ["pb.hint.escKey"] = "Échap",
            ["pb.hint.close"] = " fermer",
            ["pb.undo"] = "Annuler",
            ["pb.deleted"] = "« {0} » supprimé.",
            ["pb.send"] = "Envoyer à ChatGPT",
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
            ["chat.noComposer"] = "Champ de saisie introuvable. La page de ChatGPT a peut-être changé (F12 pour l'inspecter).",
            ["chat.sendNeverReady"] = "Le bouton d'envoi n'est jamais devenu actif. Si le texte est dans le champ, appuie sur Entrée.",
            ["chat.navigated"] = "La page a changé pendant l'envoi. Réessaie.",
            ["chat.pageError"] = "Erreur dans la page : {0}",
            ["chat.timeout"] = "ChatGPT n'a pas répondu à temps. Si le texte est dans le champ, appuie sur Entrée pour l'envoyer.",
            ["chat.noRuntime"] = "Le runtime WebView2 est introuvable. Installe « Microsoft Edge WebView2 Runtime », puis réessaie.",
            ["chat.failed"] = "Envoi impossible : {0}",

            ["tray.findHint"] = "Ctrl+Alt+F pour chercher",
            ["tray.promptHint"] = "Ctrl+Alt+G pour le Prompt Builder",
            ["tray.ocr"] = "Langues OCR",
            ["tray.quit"] = "Quitter",
            ["app.hotkeyFailed"] = "Impossible d'enregistrer {0}.\nUn autre programme utilise peut-être ce raccourci.",
            ["app.promptError"] = "Prompt Builder : {0}",
        },
    };

    private static ResourceDictionary? _published;

    internal static string Current { get; private set; } = "en";

    // Numbers and durations follow the language: "1 320" and "0,8 s" in
    // French, "1,320" and "0.8 s" in English.
    internal static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-CA");

    internal static event Action? Changed;

    // The system's UI language when we have it, English otherwise.
    internal static string DefaultCode()
    {
        var system = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Tables.ContainsKey(system) ? system : "en";
    }

    internal static void Apply(string code)
    {
        if (!Tables.ContainsKey(code)) code = "en";
        Current = code;
        Culture = CultureInfo.GetCultureInfo(code + "-CA");

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

    internal static string T(string key, object argument) => string.Format(Culture, T(key), argument);

    // "1 caractère" / "0 caractère" in French, "1 character" / "0 characters"
    // in English: the two languages disagree on zero.
    internal static string Characters(int count)
    {
        var singular = Current == "fr" ? count <= 1 : count == 1;
        return T(singular ? "pb.chars.one" : "pb.chars.other", count.ToString("N0", Culture));
    }
}
