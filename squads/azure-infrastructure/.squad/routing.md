# Work Routing

How to decide who handles what in the **azure-infrastructure** squad.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| Landing zones, environment topology, governance boundaries, architectural trade-offs | InfrastructureArchitectAgent | "Design the prod landing zone", "Should we use management groups per BU?", "Pick between Container Apps and AKS" |
| Shared Azure platform capabilities, Policy, tagging baselines, platform standards | AzurePlatformAgent | "Roll out a Policy initiative for tagging", "Publish a reusable storage module", "Standardize subscription scaffolding" |
| VNet / subnet / hub-spoke / Private Endpoints / NSG / Azure Firewall / DNS / hybrid connectivity | NetworkAgent | "Plan address space for a new region", "Add Private Endpoint to a storage account", "Set up hub-spoke with Azure Firewall" |
| Cost modeling, budgets, tagging governance, rightsizing, reservations, FinOps reviews | CostGovernanceAgent | "Estimate monthly cost for design X", "Why did spend spike in dev?", "Reservation strategy for AKS" |
| Provisioning execution, rollout safety, drift management, deployment runbooks | DeploymentAgent | "Provision the staging environment", "Add drift detection", "Write a rollback runbook" |
| CI pipelines for IaC, validation gates, fast feedback on infra PRs | ContinuousIntegrationAgent | "Add Bicep lint + what-if to PRs", "Cache provider downloads", "Fail builds on Policy violations" |
| Release promotion flow, environment progression, approvals, audit | ContinuousDeliveryAgent | "Define dev → staging → prod promotion gates", "Add manual approval for prod", "Audit a release" |
| Monitoring, logging, alerting, dashboards, incident readiness | ObservabilityAgent | "Add platform health dashboard", "Wire diagnostic settings to Log Analytics", "Define SLOs for the platform" |
| Claim verification, devil's advocate, pre-mortem | Fact Checker | "Verify these Azure SKU claims", "Steelman against this architecture", "Pre-mortem the cutover" |
| RAI / content safety / credential scan / public-exposure misconfig | Rai | Auto on Pre-Ship; on-demand: "Rai, review this Bicep" |
| Session logging, decision merging, history hygiene | Scribe | Automatic — never needs routing |
| Work queue pacing across multi-issue runs | Ralph | "Ralph, go" / "keep working" |

## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage: analyze issue, assign `squad:{member}` label | Lead |
| `squad:{name}` | Pick up issue and complete the work | Named member |

### How Issue Assignment Works

1. When a GitHub issue gets the `squad` label, the **Lead** triages it — analyzing content, assigning the right `squad:{member}` label, and commenting with triage notes.
2. When a `squad:{member}` label is applied, that member picks up the issue in their next session.
3. Members can reassign by removing their label and adding another member's label.
4. The `squad` label is the "inbox" — untriaged issues waiting for Lead review.

## Rules

1. **Eager by default** — spawn all agents who could usefully start work, including anticipatory downstream work.
2. **Scribe always runs** after substantial work, always as `mode: "background"`. Never blocks.
3. **Quick facts → coordinator answers directly.** Don't spawn an agent for "what port does the server run on?"
4. **When two agents could handle it**, pick the one whose domain is the primary concern.
5. **"Team, ..." → fan-out.** Spawn all relevant agents in parallel as `mode: "background"`.
6. **Anticipate downstream work.** If a feature is being built, spawn the tester to write test cases from requirements simultaneously.
7. **Issue-labeled work** — when a `squad:{member}` label is applied to an issue, route to that member. The Lead handles all `squad` (base label) triage.
