# ShelfOps Quick Start

**ShelfOps 0.7.1-alpha** — an operations console for Citrix Virtual Apps and Desktops (on-premises)

This guide is for CVAD operators using ShelfOps for the first time. It walks through registering a
site, connecting to it, and working with machines and sessions.

> **This is an alpha release.** It may change without notice and may contain defects.
> Maintenance mode, session disconnect/log off and power operations affect your live environment.
> **Try it in a test environment first.**

---

## 1. Requirements

- **Windows** 10 / 11, or Server 2016 or later.
- **.NET Framework 4.8**. Built into Windows 10/11; on Windows Server 2016/2019 you may need to
  install it separately.
- **Run it on an administration machine that has Citrix Studio installed.** ShelfOps uses the
  Citrix Broker SDK, so it will not work on a PC without the SDK (you will see "Citrix SDK not found").
- The machine must be **domain-joined**, and you must be signed in with an **AD account that holds a
  Citrix administrator role**.
- **Local administrator rights are not required.** No installation is required either.
- Verified on: Citrix Virtual Apps and Desktops 7 2507 (LTSR) CU1 with Windows 11.
  Other versions are untested.

## 2. Install and start

1. Extract the ZIP to any folder.
2. Run `ShelfOps.exe`. There is no installer and nothing is written to the registry.

> ShelfOps is not code-signed, so Windows SmartScreen may warn you the first time you run it.
> Choose **More info** → **Run anyway**.

Settings and logs are written to `%APPDATA%\ShelfOps\`.

## 3. Register a site

1. Click **Add** at the lower left.
2. Fill in the form on the right.

   | Field | Description |
   |---|---|
   | ID | Any unique identifier (for example `tokyo-prod`) |
   | Display name | The name shown on screen and in logs |
   | Primary DDC | FQDN of the Delivery Controller to try first |
   | Alternate DDCs | DDCs to try if the primary does not answer. **One per line**, tried in order |
   | Authentication | "Integrated Windows authentication" or "Different credentials (prompt on connect)" |

3. Click **Save**. Settings go to `%APPDATA%\ShelfOps\sites.json`.
   **No credentials are ever saved** — only connection details.

You can register several sites and switch between them in the list on the left.

**Use Duplicate to add a similar site.** Select a site and click **Duplicate** to get a copy that
keeps the DDC configuration and authentication mode, inserted directly below the original.
A new ID and display name are assigned automatically. This saves retyping FQDNs — and mistyping them.
The copy is not yet connection-tested; review it and click **Save**.

## 4. Test the connection

1. Select a site and click **Test connection** (or **Test all sites**).
2. The result appears on the right, and the symbol next to the site changes.

   | Symbol | Meaning |
   |---|---|
   | ○ | Connected |
   | × | Failed |
   | − | Not tested |

Every test is written to a log file (see section 8).

## 5. Working with machines

1. Select a site and click **Machines** in the toolbar.
2. Click **Refresh** to load the list (maintenance state, machine name, **assigned users**, catalog,
   delivery group, **type**, registration, power, state, session count).

The **Maint** column shows maintenance mode. ON is shown in **bold red** and OFF in light grey, so
you can pick it out at a glance.

The **Assigned users** column shows who a machine is assigned to. It is only populated for statically
assigned (dedicated) desktops. **It is normally empty for multi-session machines** — that is not a
fault. Use the session list to see who is currently logged on.

**Type** is the session support model: `Single` means single-session OS (VDI, one user at a time),
`Multi` means multi-session OS (shared desktops and published apps).

### Filtering and search

The toolbar has two rows: operations on top, filters below.

- **Group** / **Catalog**: choose from the delivery groups and catalogs found in the retrieved
  machines. `(All)` shows everything; `(Unassigned)` shows machines with no group or catalog.
- **Type**: filter by single-session or multi-session.
- **Maint**: filter by maintenance mode ON or OFF.
- **Search**: substring match over machine name, DNS name and **assigned users** (case-insensitive).
  Enter a user name to find the desktop assigned to that person.
- Filters combine. The bottom right shows "Showing N of M machine(s)".
- Filtering works on data already retrieved, so changing a filter does not re-query the DDC.

### Changing maintenance mode

**You can change several machines at once.**

1. Select the machines. **Ctrl+click** to add individually, **Shift+click** for a range.
2. Click **Maintenance ON** or **Maintenance OFF** (also available from the right-click menu).
3. The confirmation dialog shows **how many machines** are affected. Review it and confirm.
4. The list refreshes automatically and the Maint column updates.

**If only some machines fail** (which happens because of permissions or machine state), processing
does not stop — every machine is attempted and you get "N succeeded / M failed". Click **Details**
at the bottom right to see **which machines failed and why**. The Details window has a Copy list
button so you can paste the result into Excel.

### Exporting the list (CSV / Excel)

- **Copy for Excel**: copies the table to the clipboard as tab-separated text. Just paste into Excel.
  **This is usually the quickest option.**
- **Save as CSV...**: saves a CSV file. It is UTF-8 with BOM, so it opens correctly in Excel.

The export covers **the selected rows, or all displayed (filtered) rows if nothing is selected**.
It also includes the **DNS name**, which is not shown on screen.

> You can also select rows and press **Ctrl+C** to copy them with headers — handy for a few rows.

---

## 6. Working with sessions

1. Select a site and click **Sessions** in the toolbar.
2. Click **Refresh** to load the sessions (user, machine, delivery group, state, client, start time,
   state change time).

"State changed" is when the session state last changed — useful for judging how long a session has
been sitting disconnected.

### Filtering and search

- **Group**: choose from the delivery groups found in the retrieved sessions.
- **State**: filter by `Active` or `Disconnected`. **Use this to find sessions left disconnected.**
- **Search**: substring match over user name and machine name (case-insensitive).

### Disconnect and log off (destructive)

> **These affect end users' sessions.** Log off in particular means **unsaved work is lost.**
> Check your targets carefully.

**You can act on several sessions at once.**

1. Select the sessions with **Ctrl+click** or **Shift+click**.
2. Click **Disconnect** or **Log off** (also available from the right-click menu).
3. The confirmation dialog shows **how many sessions** and which users are affected.
4. The list refreshes automatically.

As with machines, processing does not stop on partial failure, and **Details** shows which sessions
failed.

### Shutting down and restarting a VDI (power operations)

> **These target the machine, not the session.** The machine hosting the selected session is stopped,
> so **other users on that same machine are affected too.**

Citrix Studio makes you switch to the machine list for this; ShelfOps lets you do it while looking at
the sessions.

1. Select the target sessions.
2. Choose an action from **Power ▾** in the toolbar (or the right-click menu).

   | Action | What it does |
   |---|---|
   | Shut down | Asks the guest OS to shut down gracefully. **No effect on an unresponsive machine** |
   | Restart | Asks the guest OS to shut down gracefully, then restarts. Same caveat |
   | **Force power off** | Cuts power without letting the OS shut down. Unsaved work is lost |
   | **Force reset** | Power-cycles without letting the OS shut down. Unsaved work is lost |

   The forced actions are shown in red below a separator. **To recover a VDI that has stopped
   responding, use Force reset** — a graceful request will not reach a hung OS.

3. The confirmation dialog shows the **full impact**. Even if you selected one session, you will see
   a warning such as "you selected 1 session, but these machines host 5 sessions in total".
4. Confirm to proceed.

**A power operation is reported as done once it has been accepted.** The actual shutdown or restart
happens asynchronously on the hypervisor, so refreshing immediately will still show the old state.
Wait a little and check again.

**Machines that are not power-managed** (for example physical endpoints with no hosting connection)
will fail. Use **Details** to see the reason per machine.

### Exporting the list (CSV / Excel)

**Copy for Excel** and **Save as CSV...** work the same way as for machines. The export covers the
selected rows, or all displayed rows if nothing is selected, and includes the session **Uid**, which
is not shown on screen.

---

## 7. Connecting with different credentials

If a site's authentication mode is "Different credentials", you are prompted the first time you
perform an operation in that window.

- The credentials are held **only while that window is open** and are discarded when it closes.
- **Nothing is written to disk**, and passwords never appear in log files.
- Each set of credentials runs in its own worker process, so connections are never reused across
  accounts.

If you enter the user name in `user@domain` form, you can leave the domain box empty.

## 8. Logs

Logs are written to `%APPDATA%\ShelfOps\logs\`. Click **Log folder** in the toolbar to open it.

- Connection tests, maintenance changes, session operations, power operations and list exports are
  all recorded.
- For bulk operations the **per-target result** is recorded, so you can tell exactly what failed.
- **Credentials and passwords are never recorded.**
- Logs do contain DDC FQDNs, machine names and logged-on user names. Keep that in mind before
  sharing a log file.

## 9. Troubleshooting

**"Citrix SDK not found"**
→ Run ShelfOps on a machine where Citrix Studio is installed. This is about the machine running the
tool, not about reaching the DDC.

**A connection test fails**
→ Check the DDC FQDN, that the machine is domain-joined, and that your account holds a Citrix
administrator role. If the primary DDC fails, ShelfOps automatically tries the alternates.

**SmartScreen warning on startup**
→ ShelfOps is not code-signed yet. Choose "More info" → "Run anyway".

**The first operation is slow**
→ Loading the Citrix SDK takes about three seconds the first time. Later operations respond in
about 0.1–0.3 seconds because the worker process stays resident.

## 10. Known limitations (alpha)

- **Session shadowing is not supported.**
- Targets on-premises Citrix Virtual Apps and Desktops. **Citrix Cloud (DaaS) is not supported.**
- Current scope: connection testing / machine list and maintenance mode / session list, disconnect,
  log off and power operations / list export (CSV and clipboard).
- Power operations only apply to **power-managed machines** (those tied to a hosting connection).
- Export formats are CSV and tab-separated clipboard text. There is no direct xlsx output
  (open the CSV in Excel and save it as xlsx).
- The application is **not code-signed**, so SmartScreen may warn on startup until a certificate is
  in place.

## 11. Switching the display language

ShelfOps is available in Japanese and English.

- **On first run the language follows your Windows display language** (Japanese on a Japanese
  system, English otherwise).
- You can switch it manually from **Language** at the top right. The change applies **immediately** —
  there is no need to reopen windows.
- Your choice is remembered for next time (`%APPDATA%\ShelfOps\ui-state.json`).
- Log files are written in the selected language, so a log produced at an overseas site can be read
  in English as-is.

## 12. Feedback

Please report defects and feature requests through [Issues](../../issues).

- Attaching the relevant part of the log file (`%APPDATA%\ShelfOps\logs\`) speeds up investigation.
- Credentials and passwords are never recorded in logs.
