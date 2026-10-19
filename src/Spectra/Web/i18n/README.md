# Launcher language catalogs

The launcher contains 19 interface languages. Russian strings are source keys; en.json is the complete fallback. Other JSON files have the same keys. en.js is generated from en.json for immediate offline startup. Other languages load locally on demand and are cached in memory.

Numbered parameters such as {0} must remain unchanged. They are data, never translation input. Preserve paths, filenames, product names and technical identifiers. Mark user content with data-no-i18n; use data-i18n-skip-attrs="title" for username tooltips whose adjacent UI labels still need translation.

Core labels and Minecraft terminology have editorial overrides. Extended text was drafted with offline machine translation; linguistic review by native speakers is still useful. No translation model, API key, external translation call or online translator is included in the launcher.

After editing catalogs, run npm run build:locales, npm run test:localization and npm run test:localization-ui from the repository root. Language codes are also declared in LauncherLocalization.Languages and i18n/runtime.js. Add a language to all three lists together with a complete catalog.

Game logs, player names, chat messages, world/instance names, file contents and third-party project descriptions remain original data. Microsoft/Google callback pages retain their previously requested English text. Standard Windows dialog buttons follow Windows itself; their launcher-owned titles and filter labels are localized.
