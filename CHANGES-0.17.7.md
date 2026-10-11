# Spectra 0.17.7

- After one pack is confirmed imported, stop Bedrock before opening the next pending pack. No restart occurs after the last pending pack, or when remaining packs are already installed.
- Intentional restarts reset the observed-running latch. Manual Stop cancels the import attempt, preventing the queue from reopening a file.
- Quick launch, Java version lists, Bedrock lists and instance cards switch to Stop for a running game. Stop terminates the game and its child processes.
- Java runtime version metadata identifies the running vanilla version independently of the currently selected version.

Validation: npm test including Bedrock Stop during import and Java/Bedrock button transitions. Added C# queue checks for restarting between packs and leaving the final game open. .NET SDK and a live Windows game are unavailable locally, so C# and real-process behavior were not tested here.

Server changes: none.
