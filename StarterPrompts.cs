namespace ScreenSearchOverlay;

// The six prompts of a first run, in every UI language. Each keeps its key
// (SavedPrompt.Starter). While its name and text are still an original — in
// any language — it follows the UI language; once edited it is the user's,
// and "Restore the original" puts the current language's version back.
//
// "Fix and translate" goes to English, or to French for English speakers;
// "Translate" goes to the UI language itself.
internal static class StarterPrompts
{
    private sealed record Starter(string Key, string Name, string Prompt);

    private static readonly Dictionary<string, Starter[]> ByLanguage = new()
    {
        // en and fr exactly as 1.5 wrote them: prompts saved then are recognized by their text.
        ["en"] =
        [
            new("fix", "Fix", "Fix the spelling, grammar and punctuation of this text without changing its style or meaning. Reply only with the corrected text:"),
            new("fixTranslate", "Fix and translate to French", "Fix this text, then translate it into natural French. Reply only with the translation:"),
            new("translate", "Translate to English", "Translate this text into natural, faithful English. Reply only with the translation:"),
            new("explain", "Explain", "Explain this text simply, in a few sentences:"),
            new("summarize", "Summarize", "Summarize this text as key points:"),
            new("rephrase", "Rephrase", "Rephrase this text to make it clearer and more professional, keeping the same meaning:"),
        ],
        ["fr"] =
        [
            new("fix", "Corriger", "Corrige l'orthographe, la grammaire et la ponctuation de ce texte sans changer le style ni le sens. Réponds uniquement avec le texte corrigé :"),
            new("fixTranslate", "Corriger et traduire en anglais", "Corrige ce texte, puis traduis-le en anglais naturel. Réponds uniquement avec la traduction :"),
            new("translate", "Traduire en français", "Traduis ce texte en français (Québec), de façon naturelle et fidèle. Réponds uniquement avec la traduction :"),
            new("explain", "Expliquer", "Explique-moi ce texte simplement, en quelques phrases :"),
            new("summarize", "Résumer", "Résume ce texte en points clés :"),
            new("rephrase", "Reformuler", "Reformule ce texte pour qu'il soit plus clair et plus professionnel, en gardant le même sens :"),
        ],
        ["de"] =
        [
            new("fix", "Korrigieren", "Korrigiere Rechtschreibung, Grammatik und Zeichensetzung dieses Textes, ohne Stil oder Sinn zu ändern. Antworte nur mit dem korrigierten Text:"),
            new("fixTranslate", "Korrigieren und ins Englische übersetzen", "Korrigiere diesen Text und übersetze ihn dann in natürliches Englisch. Antworte nur mit der Übersetzung:"),
            new("translate", "Ins Deutsche übersetzen", "Übersetze diesen Text natürlich und originalgetreu ins Deutsche. Antworte nur mit der Übersetzung:"),
            new("explain", "Erklären", "Erkläre mir diesen Text einfach, in wenigen Sätzen:"),
            new("summarize", "Zusammenfassen", "Fasse diesen Text in Stichpunkten zusammen:"),
            new("rephrase", "Umformulieren", "Formuliere diesen Text klarer und professioneller um, ohne den Sinn zu ändern:"),
        ],
        ["es"] =
        [
            new("fix", "Corregir", "Corrige la ortografía, la gramática y la puntuación de este texto sin cambiar su estilo ni su sentido. Responde solo con el texto corregido:"),
            new("fixTranslate", "Corregir y traducir al inglés", "Corrige este texto y luego tradúcelo a un inglés natural. Responde solo con la traducción:"),
            new("translate", "Traducir al español", "Traduce este texto al español de forma natural y fiel. Responde solo con la traducción:"),
            new("explain", "Explicar", "Explícame este texto de forma sencilla, en pocas frases:"),
            new("summarize", "Resumir", "Resume este texto en puntos clave:"),
            new("rephrase", "Reformular", "Reformula este texto para que sea más claro y profesional, manteniendo el mismo sentido:"),
        ],
        ["it"] =
        [
            new("fix", "Correggi", "Correggi ortografia, grammatica e punteggiatura di questo testo senza cambiarne lo stile né il significato. Rispondi solo con il testo corretto:"),
            new("fixTranslate", "Correggi e traduci in inglese", "Correggi questo testo, poi traducilo in un inglese naturale. Rispondi solo con la traduzione:"),
            new("translate", "Traduci in italiano", "Traduci questo testo in italiano in modo naturale e fedele. Rispondi solo con la traduzione:"),
            new("explain", "Spiega", "Spiegami questo testo in modo semplice, in poche frasi:"),
            new("summarize", "Riassumi", "Riassumi questo testo in punti chiave:"),
            new("rephrase", "Riformula", "Riformula questo testo per renderlo più chiaro e professionale, mantenendo lo stesso significato:"),
        ],
        ["pt-BR"] =
        [
            new("fix", "Corrigir", "Corrija a ortografia, a gramática e a pontuação deste texto sem mudar o estilo nem o sentido. Responda apenas com o texto corrigido:"),
            new("fixTranslate", "Corrigir e traduzir para o inglês", "Corrija este texto e depois traduza-o para um inglês natural. Responda apenas com a tradução:"),
            new("translate", "Traduzir para o português", "Traduza este texto para o português do Brasil, de forma natural e fiel. Responda apenas com a tradução:"),
            new("explain", "Explicar", "Explique este texto de forma simples, em poucas frases:"),
            new("summarize", "Resumir", "Resuma este texto em tópicos:"),
            new("rephrase", "Reformular", "Reformule este texto para deixá-lo mais claro e profissional, mantendo o mesmo sentido:"),
        ],
        ["pl"] =
        [
            new("fix", "Popraw", "Popraw pisownię, gramatykę i interpunkcję tego tekstu, nie zmieniając stylu ani sensu. Odpowiedz tylko poprawionym tekstem:"),
            new("fixTranslate", "Popraw i przetłumacz na angielski", "Popraw ten tekst, a następnie przetłumacz go na naturalny angielski. Odpowiedz tylko tłumaczeniem:"),
            new("translate", "Przetłumacz na polski", "Przetłumacz ten tekst na polski w sposób naturalny i wierny. Odpowiedz tylko tłumaczeniem:"),
            new("explain", "Wyjaśnij", "Wyjaśnij mi ten tekst prosto, w kilku zdaniach:"),
            new("summarize", "Streść", "Streść ten tekst w punktach:"),
            new("rephrase", "Przeredaguj", "Przeredaguj ten tekst, aby był jaśniejszy i bardziej profesjonalny, zachowując ten sam sens:"),
        ],
        ["tr"] =
        [
            new("fix", "Düzelt", "Bu metnin yazım, dil bilgisi ve noktalama hatalarını, üslubunu ve anlamını değiştirmeden düzelt. Yalnızca düzeltilmiş metinle yanıt ver:"),
            new("fixTranslate", "Düzelt ve İngilizceye çevir", "Bu metni düzelt, ardından doğal bir İngilizceye çevir. Yalnızca çeviriyle yanıt ver:"),
            new("translate", "Türkçeye çevir", "Bu metni doğal ve aslına sadık bir şekilde Türkçeye çevir. Yalnızca çeviriyle yanıt ver:"),
            new("explain", "Açıkla", "Bu metni bana birkaç cümleyle basitçe açıkla:"),
            new("summarize", "Özetle", "Bu metni ana maddeler halinde özetle:"),
            new("rephrase", "Yeniden yaz", "Bu metni aynı anlamı koruyarak daha açık ve profesyonel olacak şekilde yeniden yaz:"),
        ],
        ["id"] =
        [
            new("fix", "Perbaiki", "Perbaiki ejaan, tata bahasa, dan tanda baca teks ini tanpa mengubah gaya maupun maknanya. Jawab hanya dengan teks yang sudah diperbaiki:"),
            new("fixTranslate", "Perbaiki dan terjemahkan ke bahasa Inggris", "Perbaiki teks ini, lalu terjemahkan ke dalam bahasa Inggris yang alami. Jawab hanya dengan terjemahannya:"),
            new("translate", "Terjemahkan ke bahasa Indonesia", "Terjemahkan teks ini ke dalam bahasa Indonesia secara alami dan setia. Jawab hanya dengan terjemahannya:"),
            new("explain", "Jelaskan", "Jelaskan teks ini kepada saya secara sederhana, dalam beberapa kalimat:"),
            new("summarize", "Ringkas", "Ringkas teks ini menjadi poin-poin utama:"),
            new("rephrase", "Tulis ulang", "Tulis ulang teks ini agar lebih jelas dan profesional, dengan makna yang sama:"),
        ],
        ["vi"] =
        [
            new("fix", "Sửa lỗi", "Sửa lỗi chính tả, ngữ pháp và dấu câu của đoạn văn này mà không thay đổi văn phong hay ý nghĩa. Chỉ trả lời bằng đoạn văn đã sửa:"),
            new("fixTranslate", "Sửa lỗi và dịch sang tiếng Anh", "Sửa lỗi đoạn văn này, rồi dịch sang tiếng Anh tự nhiên. Chỉ trả lời bằng bản dịch:"),
            new("translate", "Dịch sang tiếng Việt", "Dịch đoạn văn này sang tiếng Việt một cách tự nhiên và sát nghĩa. Chỉ trả lời bằng bản dịch:"),
            new("explain", "Giải thích", "Giải thích đoạn văn này cho tôi một cách đơn giản, trong vài câu:"),
            new("summarize", "Tóm tắt", "Tóm tắt đoạn văn này thành các ý chính:"),
            new("rephrase", "Viết lại", "Viết lại đoạn văn này cho rõ ràng và chuyên nghiệp hơn, giữ nguyên ý nghĩa:"),
        ],
        ["ru"] =
        [
            new("fix", "Исправить", "Исправь орфографию, грамматику и пунктуацию в этом тексте, не меняя стиль и смысл. Ответь только исправленным текстом:"),
            new("fixTranslate", "Исправить и перевести на английский", "Исправь этот текст, затем переведи его на естественный английский. Ответь только переводом:"),
            new("translate", "Перевести на русский", "Переведи этот текст на русский естественно и точно. Ответь только переводом:"),
            new("explain", "Объяснить", "Объясни мне этот текст простыми словами, в нескольких предложениях:"),
            new("summarize", "Кратко изложить", "Изложи этот текст в виде ключевых пунктов:"),
            new("rephrase", "Перефразировать", "Перефразируй этот текст, чтобы он был яснее и профессиональнее, сохранив смысл:"),
        ],
        ["uk"] =
        [
            new("fix", "Виправити", "Виправ орфографію, граматику й пунктуацію в цьому тексті, не змінюючи стилю та змісту. Відповідай лише виправленим текстом:"),
            new("fixTranslate", "Виправити й перекласти англійською", "Виправ цей текст, потім переклади його природною англійською. Відповідай лише перекладом:"),
            new("translate", "Перекласти українською", "Переклади цей текст українською природно й точно. Відповідай лише перекладом:"),
            new("explain", "Пояснити", "Поясни мені цей текст простими словами, кількома реченнями:"),
            new("summarize", "Підсумувати", "Підсумуй цей текст у вигляді ключових пунктів:"),
            new("rephrase", "Перефразувати", "Перефразуй цей текст, щоб він був зрозумілішим і професійнішим, зберігши зміст:"),
        ],
        ["ja"] =
        [
            new("fix", "校正", "この文章の文体や意味を変えずに、誤字・文法・句読点を直してください。修正後の文章だけを返してください:"),
            new("fixTranslate", "校正して英訳", "この文章を校正してから、自然な英語に翻訳してください。翻訳だけを返してください:"),
            new("translate", "日本語に翻訳", "この文章を自然で正確な日本語に翻訳してください。翻訳だけを返してください:"),
            new("explain", "説明", "この文章を数文でわかりやすく説明してください:"),
            new("summarize", "要約", "この文章を要点にまとめてください:"),
            new("rephrase", "言い換え", "意味を変えずに、この文章をより明確でプロフェッショナルな表現に言い換えてください:"),
        ],
        ["ko"] =
        [
            new("fix", "교정", "문체와 의미는 바꾸지 말고 이 글의 맞춤법, 문법, 문장 부호를 고쳐 주세요. 고친 글만 답해 주세요:"),
            new("fixTranslate", "교정 후 영어로 번역", "이 글을 교정한 다음 자연스러운 영어로 번역해 주세요. 번역문만 답해 주세요:"),
            new("translate", "한국어로 번역", "이 글을 자연스럽고 정확한 한국어로 번역해 주세요. 번역문만 답해 주세요:"),
            new("explain", "설명", "이 글을 몇 문장으로 쉽게 설명해 주세요:"),
            new("summarize", "요약", "이 글을 핵심 요점으로 요약해 주세요:"),
            new("rephrase", "다시 쓰기", "같은 의미를 유지하면서 이 글을 더 명확하고 전문적으로 다시 써 주세요:"),
        ],
        ["zh-Hans"] =
        [
            new("fix", "校对", "校对这段文字的拼写、语法和标点，不要改变风格和含义。只回复修改后的文字："),
            new("fixTranslate", "校对并译成英文", "校对这段文字，然后将其翻译成自然的英文。只回复译文："),
            new("translate", "译成中文", "将这段文字自然、忠实地翻译成简体中文。只回复译文："),
            new("explain", "解释", "用几句话简单地给我解释这段文字："),
            new("summarize", "总结", "将这段文字总结为要点："),
            new("rephrase", "改写", "在保持原意的前提下，把这段文字改写得更清晰、更专业："),
        ],
        ["zh-Hant"] =
        [
            new("fix", "校對", "校對這段文字的拼寫、文法和標點，不要改變風格和含義。只回覆修改後的文字："),
            new("fixTranslate", "校對並譯成英文", "校對這段文字，然後將其翻譯成自然的英文。只回覆譯文："),
            new("translate", "譯成中文", "將這段文字自然、忠實地翻譯成繁體中文。只回覆譯文："),
            new("explain", "解釋", "用幾句話簡單地為我解釋這段文字："),
            new("summarize", "摘要", "將這段文字整理成重點摘要："),
            new("rephrase", "改寫", "在保持原意的前提下，把這段文字改寫得更清楚、更專業："),
        ],
    };

    // A first run: the six, in the UI language.
    internal static IEnumerable<SavedPrompt> Create(string language) =>
        For(language).Select(s => new SavedPrompt { Name = s.Name, Prompt = s.Prompt, Starter = s.Key });

    // Prompts saved before the keys existed (1.5): recognized by their text.
    internal static void Recognize(IEnumerable<SavedPrompt> prompts)
    {
        foreach (var prompt in prompts)
            prompt.Starter ??= ByLanguage.Values.SelectMany(set => set)
                .FirstOrDefault(s => s.Name == prompt.Name && s.Prompt == prompt.Prompt)?.Key;
    }

    // After a language change: the untouched ones follow it. True when one did.
    internal static bool Localize(IEnumerable<SavedPrompt> prompts, string language)
    {
        var changed = false;
        foreach (var prompt in prompts)
        {
            if (!IsUntouched(prompt) || Original(prompt, language) is not { } original) continue;
            if (prompt.Name == original.Name && prompt.Prompt == original.Prompt) continue;
            prompt.Name = original.Name;
            prompt.Prompt = original.Prompt;
            changed = true;
        }
        return changed;
    }

    // A starter edited since: "Restore the original" applies to it.
    internal static bool CanRestore(SavedPrompt prompt, string language) =>
        Original(prompt, language) is { } original && (prompt.Name != original.Name || prompt.Prompt != original.Prompt);

    // Name and text back; the title color is the user's choice and stays.
    internal static void Restore(SavedPrompt prompt, string language)
    {
        if (Original(prompt, language) is not { } original) return;
        prompt.Name = original.Name;
        prompt.Prompt = original.Prompt;
    }

    private static Starter[] For(string language) =>
        ByLanguage.TryGetValue(language, out var set) ? set : ByLanguage["en"];

    private static Starter? Original(SavedPrompt prompt, string language) =>
        prompt.Starter is { } key ? For(language).FirstOrDefault(s => s.Key == key) : null;

    // Still exactly an original, in whichever language.
    private static bool IsUntouched(SavedPrompt prompt) =>
        prompt.Starter is { } key && ByLanguage.Values.Any(set =>
            set.Any(s => s.Key == key && s.Name == prompt.Name && s.Prompt == prompt.Prompt));
}
