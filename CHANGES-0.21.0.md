# Spectra 0.21.0

- Added Romanian, Dutch, Czech, Swedish, Hungarian and Indonesian; 19 interface languages in total.
- Added complete local catalogs with 1,545 launcher strings per language, English fallback and offline language switching.
- Expanded localization to dynamic dialogs, settings, placeholders, tray actions, launcher notification labels and native file-dialog titles/filter labels.
- Preserved player names, chat, user input, paths and third-party content when changing languages.
- Added catalog validation, native localization tests and browser checks for every language at three window widths.

Extended descriptions use machine translation and may benefit from native-speaker wording review. Windows-owned dialog buttons follow the Windows display language. Microsoft/Google callback pages retain their requested English text. Installer and friends server are unchanged.

Validation: frontend and DOM checks; 19-language catalog/DOM/Chromium checks; C# syntax, helper semantic compilation and native helper tests. A full Windows WPF build was not available in this environment.
