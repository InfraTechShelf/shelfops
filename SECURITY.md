# Security policy

ShelfOps runs with Citrix administrator rights and, in alternate-credential mode, briefly holds an
administrator password in memory. Security reports are taken seriously and handled privately.

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

Use GitHub's private vulnerability reporting: go to the **Security** tab of this repository and choose
**Report a vulnerability**. Only the maintainer can see the report.

If that option is not available to you, contact the maintainer through
[Infra Tech Shelf](https://infratechshelf.com/profile/).

You can write in English or Japanese.

## What to include

- The version of ShelfOps and how you were running it (integrated authentication or alternate
  credentials)
- Steps to reproduce, or a description of the weakness if you have not reproduced it
- What an attacker could achieve

## What happens next

- You will get an acknowledgement, normally within a few days
- The report stays private while a fix is prepared
- Once a fixed version is released, the issue is described in the release notes. You will be
  credited if you want to be

## Scope notes

Things that are **by design** and not considered vulnerabilities:

- ShelfOps can change the state of a production site (maintenance mode, log off, power operations).
  That is its purpose. Every such operation goes through a confirmation dialog
- Log files contain DDC names, machine names and user names. They never contain credentials
- The released binaries are currently **not code-signed**. Verify the SHA-256 hash published with each
  release, or build from source

Things that **would** be vulnerabilities and should be reported:

- Any way for credentials to reach disk, a log file, or another process
- Any way to perform an operation without the confirmation dialog
- Any way for the GUI–worker protocol to be reached by another user on the same machine
