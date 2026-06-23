// =============================================================================
// squad — Aspire deployment entry point
// Subagent: bicep-foundation
// Targets: Container Apps environment + supporting platform services
// =============================================================================
targetScope = 'resourceGroup'

@minLength(3)
@maxLength(12)
@description('Short environment name (e.g. dev, stg, prod). Used in resource naming.')
param envName string

@description('Azure region for all resources. Defaults to RG location.')
param location string = resourceGroup().location

@description('Tags applied to every resource. Mandated by policy/cost-guardrails subagent.')
param tags object = {
  'squad:env': envName
  'squad:owner': 'azure-infrastructure-squad'
  'squad:costCenter': 'engineering'
  'squad:dataClass': 'internal'
}

@description('Object IDs (Entra) granted Key Vault Secrets User role. CI service principal goes here.')
param keyVaultSecretReaders array = []

var resourceToken = uniqueString(subscription().id, resourceGroup().id, envName)
var namePrefix = 'squad-${envName}'

module logs 'modules/log-analytics.bicep' = {
  name: 'logs'
  params: {
    name: '${namePrefix}-logs-${resourceToken}'
    location: location
    tags: tags
    retentionDays: 30
  }
}

module appInsights 'modules/app-insights.bicep' = {
  name: 'appInsights'
  params: {
    name: '${namePrefix}-appi-${resourceToken}'
    location: location
    tags: tags
    workspaceId: logs.outputs.workspaceId
  }
}

module identity 'modules/managed-identity.bicep' = {
  name: 'identity'
  params: {
    name: '${namePrefix}-id-${resourceToken}'
    location: location
    tags: tags
  }
}

module kv 'modules/key-vault.bicep' = {
  name: 'kv'
  params: {
    name: take('kv${envName}${resourceToken}', 24)
    location: location
    tags: tags
    appIdentityPrincipalId: identity.outputs.principalId
    additionalSecretReaders: keyVaultSecretReaders
  }
}

module containerEnv 'modules/container-apps-env.bicep' = {
  name: 'containerEnv'
  params: {
    name: '${namePrefix}-cae-${resourceToken}'
    location: location
    tags: tags
    logAnalyticsCustomerId: logs.outputs.customerId
    logAnalyticsSharedKey: logs.outputs.primarySharedKey
  }
}

output containerAppsEnvironmentId string = containerEnv.outputs.id
output managedIdentityClientId string = identity.outputs.clientId
output managedIdentityPrincipalId string = identity.outputs.principalId
output keyVaultUri string = kv.outputs.uri
output appInsightsConnectionString string = appInsights.outputs.connectionString
output logAnalyticsWorkspaceId string = logs.outputs.workspaceId
