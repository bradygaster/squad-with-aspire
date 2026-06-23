# NetworkAgent — History

## Project Context

- **Project:** azure-infrastructure
- **Purpose:** Design, provision, automate, deploy, and operate the Azure platform and supporting delivery infrastructure.
- **Created:** 2026-06-23
- **Hired by:** bradygaster (via Copilot)
- **Role:** Azure Network Engineer

## Day-1 Brief

The squad already has architecture, platform, deployment, CI, CD, and observability roles. NetworkAgent was added because Azure networking (VNet topology, Private Endpoints, hub-spoke, NSGs, Azure Firewall, hybrid connectivity, DNS) is a deep, independent discipline that was unaddressed and previously implicit. Coordinate with `AzurePlatformAgent` for platform-wide standards, `DeploymentAgent` for rollouts, and the `security-hardening` squad's `IdentitySecurityAgent` for zero-trust posture.
