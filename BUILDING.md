# Building ShelfOps from source

ShelfOps is a .NET Framework 4.8 WPF application. It has **no NuGet dependencies** and does **not
include any Citrix binaries** — the Citrix Broker SDK is loaded at run time from the machine the
tool runs on. So the build itself needs nothing beyond Windows and Microsoft's free build tools.

## Requirements

| What | Why |
|---|---|
| Windows 10 / 11 or Server 2016+ | WPF and the .NET Framework are Windows-only |
| [Visual Studio Build Tools](https://visualstudio.microsoft.com/downloads/) (free) with the **.NET desktop build tools** workload | Provides `MSBuild.exe`. Full Visual Studio works too |
| Windows PowerShell 5.1 (built into Windows) | `System.Management.Automation.dll` is referenced from the GAC; nothing to install |
| .NET Framework 4.8 Developer Pack — *optional* | If it is not installed, `Directory.Build.props` falls back to the runtime's assembly folder automatically (see the comment in that file). Either path produces a binary that runs on .NET Framework 4.8 |

You do **not** need Citrix Studio or the Citrix SDK to *build* ShelfOps. You need them to *run* it
against a real site.

## Build

From a Developer Command Prompt (or any shell where `MSBuild.exe` is on the PATH):

```bat
MSBuild.exe CitrixAdminTool.sln /t:Rebuild /p:Configuration=Release
```

If MSBuild is not on your PATH, the usual location is:

```
C:\Program Files (x86)\Microsoft Visual Studio\<version>\BuildTools\MSBuild\Current\Bin\MSBuild.exe
```

Output goes to `dist\Release\`:

```
ShelfOps.exe            GUI
ShelfOps.Worker.exe     connection worker (one process per set of credentials)
ShelfOps.Core.dll       shared connection layer
ShelfOps.ConsoleTest.exe   internal test console — not part of the release package
```

The three files marked as the release are meant to be **deployed together**; the GUI and the worker
talk over a private protocol that changes between versions.

## Making a release package

```powershell
.\pack-release.ps1
```

This copies the three runtime files, their `.config` files, `LICENSE`, `NOTICE` and the quick start
guides into `release\ShelfOps-<version>.zip`. The version string is set at the top of the script;
keep it in step with `AssemblyInformationalVersion` in `src\CitrixAdminTool.Wpf\Properties\AssemblyInfo.cs`.

`pack-release.ps1 -CertThumbprint <thumbprint>` signs the binaries before zipping, if you have a code
signing certificate in your certificate store. Official releases are currently unsigned.

## Verifying a build

`docs/検証手順.md` (Japanese) describes the manual test procedure against a real CVAD site.

For changes to the UI or to the string table, run the smoke test after a Release build:

```powershell
.	ools\XamlSmokeun-smoke.ps1
```

It opens every window in both languages without showing them, and fails if a string key is
unresolved (they render as `!!Key_Name!!`), if WPF reports a binding error, or if the DataGrid column
headers do not follow a language switch. It needs no DDC. Exit code 0 means all checks passed.

It is a single C# file compiled on the fly with the `csc.exe` that ships with the .NET Framework, so
it needs nothing beyond what the main build needs. Contributions that grow it into a proper test
project are welcome.

## Project layout

```
src/CitrixAdminTool.Core/      connection layer, worker protocol, Broker SDK calls
src/CitrixAdminTool.Worker/    worker process (integrated auth or alternate credentials)
src/CitrixAdminTool.Wpf/       WPF GUI (MVVM), localization, export
src/CitrixAdminTool.ConsoleTest/  internal console for connection testing
docs/                          design documents (mostly Japanese) and release notes
```

The internal namespace and project names still say `CitrixAdminTool` — that is the tool's former
name. It is invisible to users and renaming everything is a large, risky change, so it has been left
as is.

## A note on the design documents

`docs/SPEC_phase*.md` record the design decisions phase by phase, including the ones that were tried
and abandoned (for example why connections run in a separate process per credential, and why
filtering is done client-side). They are in Japanese. If you are changing something and wondering
"why is it like this?", the answer is usually there.
