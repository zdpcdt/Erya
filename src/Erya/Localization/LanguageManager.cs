using Irihi.Lingua;

namespace Erya.Localization;

/// <summary>
/// App-layer localized strings (base file = Simplified Chinese = invariant
/// fallback, so the default render is byte-identical with the pre-i18n UI).
/// Variants: <c>Strings.zh-Hant.json</c> / <c>Strings.en.json</c>.
/// </summary>
[LinguaManager("./Resources/Strings.json")]
public partial class LanguageManager;
