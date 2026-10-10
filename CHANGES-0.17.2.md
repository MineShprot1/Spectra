# Spectra 0.17.2

- Bedrock Discover: Show more aligns with the right edge, matching the other lists.
- Microsoft and Google completion/error pages use English and have no Close tab button. Google completion pages require no JavaScript.
- Expanded dictionaries for all 12 non-Russian languages: Discover categories, Bedrock additions, friends, verification, fonts, fullscreen and unsaved settings.
- Expanded English coverage of static UI descriptions and content/authentication errors. Missing translations fall back to English; this is not a complete translation of every technical message into every language.
- Translation supports symbols, keyboard hints, counts and selected dynamic labels, and restores Russian when switching back. User content and log text are preserved.
- Download counts and file sizes follow the selected language.

Validation: launcher npm test and Worker npm test. Windows/.NET build and live browser layout could not be checked in this environment.

Server update from 0.17.1: npm ci, then npm.cmd run deploy on Windows. No new database migration.
