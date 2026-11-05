# Security Policy

## Supported versions

Ohrbob is in beta. Only the latest tagged release and the current `main` branch receive security fixes.

| Version | Supported |
| ------- | --------- |
| 0.2.x (latest) | ✅ |
| older | ❌ |

## Reporting a vulnerability

Please **do not open a public issue** for a security problem.

Report it privately to the maintainers — either through the private security advisory feature of the repository host (GitHub: *Security → Advisories*; GitLab/Forgejo: *New issue → Confidential*), or by email to `kwnetzwelt+ohrbobsecurity@gmail.com`.

Include:

- what is affected (service, endpoint, config),
- steps to reproduce or a proof of concept,
- the impact you believe it has,
- any suggested fix, if you have one.

You can expect an acknowledgement within a few days. We will keep you informed about the fix and credit you in the release notes unless you prefer not to be named.

## Scope

In scope: authentication and authorization bypass, cross-account access to profiles, stations or listen state, stream-token leakage, injection, SSRF in feed polling, and exposed secrets.

Out of scope: findings that require access to a self-hosted instance's own configuration or database, missing hardening headers with no demonstrated impact, and reports from automated scanners without a reproducible issue.
