# Contributing to ShelfOps

Thanks for your interest. ShelfOps is a small project maintained by one person, so a few ground
rules keep things manageable for everyone.

## Reporting a problem

Open an [issue](../../issues). **English or Japanese is fine.**

The most useful report includes:

- what you did, what you expected, and what happened instead
- the ShelfOps version (from the About dialog)
- your CVAD version and whether you used integrated authentication or alternate credentials
- the relevant part of the log file from `%APPDATA%\ShelfOps\logs\`

Logs never contain passwords, but they do contain DDC names, machine names and user names. Redact
anything you would rather not post publicly.

**Security issues should not be reported as public issues.** See [SECURITY.md](SECURITY.md).

## Proposing a change

For anything beyond a small fix, please open an issue first to talk it over. This tool runs with
Citrix administrator rights against production sites, so changes to what it *does* (as opposed to how
it looks) are weighed carefully. The design documents in `docs/` explain the reasoning behind the
current structure — several of the less obvious choices exist because the simpler approach was tried
and failed in a real environment.

Things that are always welcome:

- bug fixes with a clear description of the failure
- improvements to English wording in the UI or documentation (the author is not a native speaker)
- tests
- documentation

## Pull requests

- Build must pass: `MSBuild.exe CitrixAdminTool.sln /t:Rebuild /p:Configuration=Release`
- Match the surrounding code. Comments in the codebase are in Japanese; comments in English are
  equally welcome — write in whichever you can express the *reason* for the change most clearly
- **Every user-facing string must be added to `StringTable.cs` in both Japanese and English.** The
  table is deliberately one file with both languages side by side so that a missing translation is
  a visible gap in the diff, not a silent omission. If you can only write one language, add the other
  as a placeholder and say so in the PR; it will be filled in
- Keep the "no external dependencies" rule. ShelfOps is deployed by copying a folder; that is a
  feature, and NuGet packages that must be shipped alongside break it
- Do not add anything that writes credentials to disk or to logs, under any circumstances

You do not need to sign a CLA. By submitting a pull request you agree that your contribution is
licensed under the Apache License 2.0, the same as the rest of the project (this is Section 5 of the
license).

## Testing

There is no automated test suite yet. `docs/検証手順.md` describes the manual procedure against a
real CVAD site. If you can test your change against a real site, say so in the PR and note the CVAD
version; if you cannot, say that too — it is not a reason to reject the change, but it decides how
carefully it gets reviewed.
