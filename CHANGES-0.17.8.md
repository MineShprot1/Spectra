# Spectra 0.17.8

- A manifest alone no longer permits restarting Bedrock. The imported pack must contain all archive assets with matching sizes and SHA-256 checksums, and its directory snapshot must stay unchanged for at least ten seconds.
- Expected archive assets are parsed once per attempt. While waiting, inspect file metadata first; perform full asset verification after the quiet interval.
- Once a manifest appears, suppress the 15-second reopen retry while resources are copying. The three-minute timeout still stops the queue safely.
- This is filesystem verification, not an official Minecraft import-completion callback.

Validation: frontend npm test passed. Added C# checks for manifest-only installation, missing files, same-size incorrect resources, verified asset sets and delaying restarts while resources copy. .NET tests and a real Windows import could not be run locally.

Server changes: none.
