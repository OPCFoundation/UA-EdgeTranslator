# Security Policy

## Reporting a Vulnerability

We take the security of UA Edge Translator seriously. If you discover a
vulnerability, **please do not open a public GitHub issue**. Instead, report it
privately so we can investigate and ship a fix before the details are
disclosed.

### How to report

* Preferred: use GitHub's **Private vulnerability reporting** form on this
  repository (`Security` tab → `Report a vulnerability`). This creates a
  private advisory only the maintainers can see.
* Alternatively, email the OPC Foundation security contact listed at
  <https://opcfoundation.org/about/contact-us/> and reference
  "UA-EdgeTranslator" in the subject line.

When you report, please include:

* a description of the issue and its impact,
* the affected version (commit SHA or container image tag is best),
* reproduction steps or a proof-of-concept,
* any suggested mitigations you have already identified.

### What to expect

* We will acknowledge receipt within **3 business days**.
* We will provide an initial assessment and an indicative remediation timeline
  within **5 business days**.
* We coordinate disclosure with the reporter. Public details are only
  published after a fix is available, or after **30 days** if no fix can be
  produced, whichever comes first.

## Supported Versions

Security fixes are produced for the `main` branch and are published as new
container images at `ghcr.io/opcfoundation/ua-edgetranslator`. Older tagged
releases are not back-ported; operators are expected to track `main` or the
most recent `vX.Y.Z` tag.

## Out of scope

* Vulnerabilities in third-party protocol-driver dependencies should be
  reported to those projects first; we will pick up the fixes through our
  Dependabot integration.
* Findings that require an attacker who already has write access to the
  container's filesystem, the OPC UA `pki/` directory, or the host's network
  namespace are considered post-compromise hardening rather than
  vulnerabilities, but are still welcome as feature requests.
