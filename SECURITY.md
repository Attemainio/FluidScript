# Security policy

## Supported releases

FluidScript is pre-release. Until the first tagged release, only `main` is supported.

## Reporting a vulnerability

Report privately through **GitHub Security Advisories** on this repository
(Security → Report a vulnerability). Do not open a public issue: ordinary issues are not the
disclosure channel, and the issue templates route security reports here.

Expect an acknowledgement within **7 days** and an assessment within **30 days**.

## Please do not publish payloads before coordination

FluidScript executes user-authored scripts and accepts them over an API. A script or API payload that
exercises a parser, solver or resource-limit flaw is directly exploitable against anyone running the
tool. Share it in the advisory rather than publicly, and give us the disclosure window above.

## What is in scope

Anything that lets a script or an API request read or write outside its own model, exhaust host
resources past the documented limits, or execute code. The documented limits themselves are in
`plan/00-foundation/07-quality-attributes.md`; a script that is merely slow inside them is a
performance issue, not a vulnerability.
