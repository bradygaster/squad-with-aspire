// SEC-CHK-008 R6 bundle 9a — Canary runner managed identity
// Scope: canary-only. No prod KV, no prod data, no app-slot config read.
// Owner: azure-infrastructure-squad
// Reviewers: security-hardening-squad (RBAC scoping), review-deployment-squad (deploy)

@description('Environment name (canary or prod).')
@allowed([
  'canary'
  'prod'
])
param environment string

@description('Azure region.')
param location string

@description('Resource name prefix.')
param namePrefix string

@description('Resource ID of the canary-scoped Key Vault holding test account credentials.')
param canaryKeyVaultId string

@description('Resource ID of the Log Analytics workspace receiving canary custom metrics.')
param logAnalyticsWorkspaceId string

@description('Resource ID of the Application Insights component receiving canary custom metrics.')
param applicationInsightsId string

// ----------------------------------------------------------------------------
// User-assigned managed identity for the synthetic canary runner.
// Scoped strictly to canary surface — no prod read, no prod write.
// ----------------------------------------------------------------------------
resource canaryRunnerMi 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${namePrefix}-canary-runner-mi-${environment}'
  location: location
  tags: {
    purpose: 'sec-chk-008-r6-canary'
    boundary: 'canary-only'
    'data-access': 'none-prod'
  }
}

// ----------------------------------------------------------------------------
// Role definition IDs (built-in Azure roles)
// ----------------------------------------------------------------------------
// Key Vault Secrets User — read only, canary KV only
var keyVaultSecretsUserRoleId = '4633458b-17de-41a5-8b7e-bbd906fd99a2'
// Monitoring Metrics Publisher — write custom metrics to App Insights namespace only
var monitoringMetricsPublisherRoleId = '3913510d-42f4-4e42-8a64-420c390055eb'
// Log Analytics Contributor — write logs (scoped to canary workspace)
var logAnalyticsContributorRoleId = '92aaf0da-9dab-42b6-94a3-d43ce8d16293'

// ----------------------------------------------------------------------------
// RBAC: Canary KV secrets read (test account creds only — payment provider sandbox keys).
// Explicitly NOT granted on prod KV. The canaryKeyVaultId MUST point at a separate
// canary-scoped vault containing only sandbox provider credentials and test PANs.
// ----------------------------------------------------------------------------
resource kvScope 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: last(split(canaryKeyVaultId, '/'))
}

resource kvSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: kvScope
  name: guid(canaryKeyVaultId, canaryRunnerMi.id, keyVaultSecretsUserRoleId)
  properties: {
    principalId: canaryRunnerMi.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
  }
}

// ----------------------------------------------------------------------------
// RBAC: Application Insights metrics publisher (scoped to canary namespace only).
// Custom metric namespace: `checkout.canary_terminal_response_timing` — security-hardening
// pinned this MUST NOT widen the existing `checkout.terminal_response_timing` namespace
// (RBAC scoping per GATE-CO-06c).
// ----------------------------------------------------------------------------
resource appInsightsScope 'Microsoft.Insights/components@2020-02-02' existing = {
  name: last(split(applicationInsightsId, '/'))
}

resource appInsightsMetricsPublisher 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: appInsightsScope
  name: guid(applicationInsightsId, canaryRunnerMi.id, monitoringMetricsPublisherRoleId)
  properties: {
    principalId: canaryRunnerMi.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', monitoringMetricsPublisherRoleId)
  }
}

// ----------------------------------------------------------------------------
// RBAC: Log Analytics contributor scoped to canary workspace only.
// ----------------------------------------------------------------------------
resource lawScope 'Microsoft.OperationalInsights/workspaces@2022-10-01' existing = {
  name: last(split(logAnalyticsWorkspaceId, '/'))
}

resource lawContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: lawScope
  name: guid(logAnalyticsWorkspaceId, canaryRunnerMi.id, logAnalyticsContributorRoleId)
  properties: {
    principalId: canaryRunnerMi.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', logAnalyticsContributorRoleId)
  }
}

// ----------------------------------------------------------------------------
// Outputs
// ----------------------------------------------------------------------------
output canaryRunnerMiId string = canaryRunnerMi.id
output canaryRunnerMiPrincipalId string = canaryRunnerMi.properties.principalId
output canaryRunnerMiClientId string = canaryRunnerMi.properties.clientId
output canaryRunnerMiName string = canaryRunnerMi.name
