# Spectra 0.17.5

- Bedrock pack import is sequential: open one pending .mcpack/.mcaddon, then wait for its manifest UUIDs and versions to appear in the selected edition’s installed storage before opening the next file.
- Two positive checks two seconds apart confirm detection. This checks filesystem manifests rather than Minecraft’s completion notification.
- Stop after about three minutes without confirmation for an individual file; never open subsequent files after a timeout. Retry by clicking Play after checking the Minecraft import result.
- Already installed packs are skipped. Queued archives are parsed once per launch attempt; installed storage is refreshed while waiting.
- The footer shows the current import file and queue position. Repeated launch clicks during import are ignored.
- The normal game activation remains skipped when an import occurred. After the queue completes, click Play again.

Validation: frontend npm test passed. Added C# queue checks for sequential opening, installed-file skipping and stopping on timeout. C# tests and real Windows imports cannot be run here because .NET SDK is unavailable.

Server changes: none.
