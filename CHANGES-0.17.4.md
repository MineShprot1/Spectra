# Spectra 0.17.4

- Before launching an installed Bedrock version, Spectra checks queued .mcpack/.mcaddon files against pack UUIDs and versions in that edition’s behavior_packs/resource_packs directories.
- If any packs are missing, only those files are opened through Windows file associations. The normal AppsFolder activation and world-file opening are skipped. After import, click Play again.
- Opening a file is not treated as proof of installation. All components of a bundled .mcaddon must exist in the game. Installed newer versions also satisfy a queued older version.
- Preview and standard game storage are checked separately.
- Minecraft may start automatically through its import file association. Spectra does not separately activate it during the import phase.
- Existing latest-version .mcworld behavior is retained after a normal launch.

Validation: npm test passed, including the importingContent response stopping the launch flow. Added standalone C# pack tests and a Windows CI step for identity/version matching, bundled packs, malformed files and edition separation. .NET SDK is unavailable locally; those C# tests and a live Windows import were not run here.

Server changes: none.
