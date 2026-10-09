# Spectra 0.12.0

- Minecraft: 16 parallel downloads, up to four integrity checkers, bounded queue; common asset cache under LocalAppData/Spectra/cache/assets. Libraries and version profiles remain per instance to preserve Forge compatibility.
- Vanilla no longer runs the full installation twice before starting. Libraries still check files before launch; checksum validation remains enabled.
- Progress emissions throttled to 150 ms. Installations share a gate to avoid racing the Java installation and shared assets across simultaneous launches.
- Eclipse Temurin JRE instead of JDK for fresh Java installations; already installed runtimes remain usable. CmlLib's second automatic Java download is disabled because Spectra installs and supplies Java explicitly.
- Pixel minimize/maximize/restore/close controls follow native WPF state changes and theme colors.
- CurseForge and Crafty keys supplied by the project owner are included as launcher defaults. API input fields removed from settings. No claim that real API requests were tested.
- Dark/light/gradient themes allow distinct fonts for headings and reading text; curated installed fonts plus shipped pixel/reading fonts; each row has import and apply-to-both actions. Import TTF/OTF/WOFF/WOFF2 up to 10 MiB, store by SHA-256 in LocalAppData/Spectra/fonts, private local WebView font host. Imported fonts survive restarts; JSON theme exports refer to fonts, so custom font files must be imported separately on another PC. Specific Steam/Minecraft/Aero/Windows themes retain their own typography.

Validation: JS syntax and frontend/appearance/friends/skin tests. C# Windows build and actual download speed not tested here: .NET SDK/Windows runtime unavailable. The speed improvement depends on bandwidth, Mojang servers and disk; no fixed multiplier claimed.
