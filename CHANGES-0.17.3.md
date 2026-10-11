# Spectra 0.17.3

- Installed Bedrock releases from the online catalog launch directly without an installation confirmation. Both the UI and C# check the installed package before requiring installation.
- UWP display/package version aliases and case-insensitive package identities are recognized. Preview remains a separate package.
- Installed imported packages also launch directly. Installation and replacement confirmation remain for missing packages.
- The footer reconciles Java and Bedrock process status every two seconds and clears stale launch/progress text when idle. Ongoing transfers and Microsoft sign-in keep their status.
- Launch-target operations now send transfer completion events. A Java process that already exited is no longer unconditionally announced as started.

Validation: npm test, including installed Bedrock direct launch, UWP version aliases, separate Preview identity, Java/Bedrock status snapshots and preservation of active download progress. Windows/.NET build and live process monitoring could not be run here.

Server changes: none; deployment is not required.
