# Security Controls Inventory

Owner: **security-hardening-squad** · Last updated: 2026-06-24

Mapping of implemented & in-flight controls to OWASP ASVS L2 + Microsoft SFI baselines.

## Pipeline / Supply chain

| Control | Workflow | Status |
|---|---|---|
| Semgrep (custom + OSS rules) | `.github/workflows/security-static.yml` | ✅ in repo |
| CodeQL — JavaScript/TypeScript | `.github/workflows/security-static.yml` | ✅ in repo |
| **CodeQL — C# (security-extended + quality)** | `.github/workflows/security-supplychain.yml` | 🆕 this PR |
| **Dependency Review (PR gate, fail-on high)** | `.github/workflows/security-supplychain.yml` | 🆕 this PR |
| **Gitleaks secret scan (full history)** | `.github/workflows/security-supplychain.yml` | 🆕 this PR |
| **Trivy fs (vuln + misconfig + secret) + SBOM** | `.github/workflows/security-supplychain.yml` | 🆕 this PR |
| Weekly re-scan against `main` | cron in `security-supplychain.yml` | 🆕 this PR |

## Runtime / App

| Control | Artifact | Status |
|---|---|---|
| Security response headers middleware | `security-headers/SecurityHeadersExtensions.cs` | 🆕 this PR (drop-in) |
| Idempotency key validation (checkout) | `checkout-sec-reference/Idempotency*` | ✅ reference shipped |
| Unicode/JSON canonicalization (checkout) | `checkout-sec-reference/Unicode*`, `JsonCa*` | ✅ reference shipped |
| PII column-level encryption | `app-6-pii-encryption-{spec,patch}` | ✅ spec + patch |
| Pre-prod security gate | `preprod-security-gate/` | ✅ reference checklist |

## Identity / Secrets / Network (owned with azure-infrastructure-squad)

| Control | Owner | Status |
|---|---|---|
| Managed Identity for all Azure resource access | azure-infrastructure-squad (Nia) | 🟡 in flight |
| Key Vault for all secrets, no app-settings secrets | azure-infrastructure-squad (Nia) | 🟡 in flight (`keyVault.bicep`) |
| Private endpoints for data plane | azure-infrastructure-squad (Nia) | 🟡 in flight (`network.bicep`) |
| Entra ID auth for APIs (no API keys) | shared with application-development-squad | 🟡 in flight |

## Open follow-ups (to be filed as issues)

1. Integrate `SecurityHeadersExtensions` into a shared `ServiceDefaults` project so every Aspire service inherits it by default.
2. Add OWASP ZAP baseline scan to the pre-prod gate workflow.
3. Add `actions/dependency-submission` for transitive .NET deps so Dependabot covers the full graph.
4. Pin all third-party GitHub Actions by SHA (currently pinned by major version).
5. Add container image signing (cosign) and SBOM attestation once images are published.
