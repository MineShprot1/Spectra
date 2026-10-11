# Spectra 0.17.6

- Reopen the current pending .mcpack/.mcaddon every 15 seconds without import confirmation. The existing three-minute per-file limit remains.
- Check Bedrock process presence once per second while waiting. After a running game has been observed, its disappearance cancels the queue before any retry or subsequent file.
- Initial game startup is allowed: absence of a process before the first observed launch does not immediately cancel importing.
- Two consecutive positive installation checks still gate the next file; they now run one second apart.

Validation: frontend npm test. Added C# tests for 15-second retry, stopping after game closure and startup grace. .NET tests and Windows import behavior could not be run locally.

Server changes: none.
