# ShelfOps

**An operations console for Citrix Virtual Apps and Desktops (unofficial tool)**

ShelfOps is a free Windows GUI tool for checking machines, toggling maintenance mode, managing
sessions and performing VDI power operations in a Citrix Virtual Apps and Desktops (CVAD)
environment — without opening Studio. Available in **English and Japanese**. Its defining design
goal is that it **can be distributed and used without granting local administrator rights**.

Project site: [Infra Tech Shelf](https://infratechshelf.com/)

> **This is an alpha release.** It may change without notice and may contain defects.
> Maintenance mode, session disconnect/log off and **VDI power operations** affect your live
> environment. **Please verify it in a test environment before using it in production.**

---

## Features

| Feature | Description |
|---|---|
| Connection test | Verifies connectivity to Delivery Controllers, with failover |
| Machine list | Lists machines and their state, including assigned users |
| Maintenance mode | Toggle ON/OFF for several machines at once (with a confirmation showing the count) |
| Session management | List, disconnect and log off sessions; multi-select for bulk operations |
| **VDI power operations** | Shut down or restart the machine hosting a session, straight from the session list. Force power off and force reset are also available |
| Filtering and search | Filter by delivery group, catalog, session type, maintenance state and session state; substring search by name |
| List export | Save as CSV, or copy to the clipboard ready to paste into Excel |
| Duplicate a site | Copy a registered site including its DDC configuration, so you don't retype FQDNs |
| Integrated / alternate credentials | Connect as the current user, or with a different account |
| English / Japanese | Detected from the OS display language; switchable on the fly, and log files follow the same language |
| Fast | With a resident connection, operations after the first respond in about 0.1–0.3 seconds (measured; the first takes about 3 seconds) |

### Design highlights

- **Runs as a standard user**: every feature works for a domain user without local administrator
  rights. You do not have to hand out privileges to your operators' machines
- **Credential isolation**: connections run in a separate worker process per set of credentials.
  Passwords for alternate credentials are never written to files or logs
- **Write operations always confirm**: anything that changes your environment (maintenance mode,
  session disconnect/log off, power operations) goes through a confirmation dialog that states how
  many targets are affected
- **Bulk results are traceable**: if some targets fail, processing continues to the end and you can
  see **which target failed and why**, both on screen and in the log
- **Audit trail**: operations and their results are written to a log file
  (`%APPDATA%\ShelfOps\logs\`), in the language you have selected

### Not in this version

- Session shadowing — planned for a future release
- Delivery group and catalog management — under consideration
- **Citrix Cloud (DaaS) is not supported. This tool is for on-premises environments only**

## Requirements

- **Windows** 10 / 11, or Server 2016 or later.
- **.NET Framework 4.8**. Built into Windows 10/11; on Windows Server 2016/2019 you may need to
  install it separately.
- Run it on **an administration machine with Citrix Studio installed**. ShelfOps uses the Citrix
  Broker SDK, so it will not work on a PC without the SDK (you will see "Citrix SDK not found").
- The machine must be **domain-joined**, and you must be signed in with an **AD account that holds a
  Citrix administrator role**.
- Local administrator rights are **not** required, and no installation is needed.
- Verified on: Citrix Virtual Apps and Desktops 7 2507 (LTSR) CU1 with Windows 11. Other versions
  are untested.

## Installation

1. Download the latest zip from [Releases](https://github.com/InfraTechShelf/shelfops/releases/).
2. Before extracting, confirm that the SHA-256 hash of the zip matches the value below and the one
   shown on the Release page.

   ```
   SHA-256: 6c3524e5491ba6ba768746b2153cf2df6b96babbbe8dea11aac9c9f22dfbf47c
   ```

   To check (PowerShell):

   ```powershell
   Get-FileHash -Algorithm SHA256 .\ShelfOps-0.7.0-alpha.zip
   ```

3. Extract it anywhere and run `ShelfOps.exe`. There is no installer, and nothing is written to the
   registry.

> **About the SmartScreen warning**: this tool is not code-signed, so Windows SmartScreen may warn
> you the first time you run it. Choose "More info" → "Run anyway". Verify the hash first so you know
> the file has not been tampered with.

## Before you start

**Try it in a test environment first.** This tool performs write operations (maintenance mode,
session disconnect and log off, power operations). Before using it in production, verify its
behaviour in a test environment and follow your organisation's operational rules.

**Take particular care with bulk operations.** They affect many machines or sessions at once. Always
check the target count in the confirmation dialog, and start with a small number of targets.

**Power operations target the machine, not the session.** The machine hosting the selected session is
stopped, so other users on that machine are affected too. The confirmation dialog shows the total
number of sessions that will be affected — please read it.

For step-by-step instructions, see `QuickStart.md` (English) or `クイックスタート.md` (Japanese)
inside the ZIP.

## Feedback

Please report defects and feature requests through [Issues](../../issues). English or Japanese is
fine.

- Attaching the relevant part of the log file (`%APPDATA%\ShelfOps\logs\`) speeds up investigation
- Credentials and passwords are never recorded in logs

## Roadmap

- [ ] Session shadowing
- [ ] Delivery group and catalog management (under consideration)
- [ ] Code signing

## Terms of use

- **This version is free to use for individuals and organisations alike**, including commercial use
- Redistribution, modification and reverse engineering are prohibited
- **About future versions**: the tool is expected to remain free for some time after the 1.0.0
  release, but **use by organisations may become paid** in the future. If that happens, there will be
  **no difference in features** between free and paid use
- Even if a paid model is introduced, **the terms of already-distributed versions will not change
  retroactively**. The copy you have may continue to be used under the terms in effect when you
  obtained it

## Disclaimer

This software is provided AS IS. The author accepts no liability for any damage arising from the use
or inability to use this software. Use it at your own risk. In particular, verify write operations
thoroughly in a test environment before performing them in production.

## Trademarks

Citrix, Citrix Virtual Apps and Desktops, XenApp, XenDesktop and NetScaler are trademarks of Cloud
Software Group, Inc. or its affiliates. **This is an unofficial third-party tool developed by an
individual and is not affiliated with Cloud Software Group, Inc. in any way.**

---

Background and design notes: [Infra Tech Shelf](https://infratechshelf.com/) |
About the author: https://infratechshelf.com/profile/
