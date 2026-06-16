targetScope = 'resourceGroup'

@description('The base name for all resources')
param appName string = 'todo'

@description('The Azure region for all resources')
param location string = resourceGroup().location

@description('The container image to deploy (e.g., myacr.azurecr.io/todoapi:latest)')
param containerImage string = ''

@description('Cosmos DB autoscale max throughput (RU/s)')
param cosmosMaxThroughput int = 1000

var uniqueSuffix = uniqueString(resourceGroup().id, appName)
var resourcePrefix = '${appName}-${uniqueSuffix}'

// Log Analytics Workspace + Application Insights
module monitoring 'modules/app-insights.bicep' = {
  name: 'monitoring'
  params: {
    name: resourcePrefix
    location: location
  }
}

// Azure Container Registry
module acr 'modules/container-registry.bicep' = {
  name: 'acr'
  params: {
    name: replace('${appName}${uniqueSuffix}', '-', '')
    location: location
  }
}

// Azure Key Vault
module keyVault 'modules/key-vault.bicep' = {
  name: 'keyVault'
  params: {
    name: 'kv-${uniqueSuffix}'
    location: location
  }
}

// Azure Cosmos DB
module cosmosDb 'modules/cosmos-db.bicep' = {
  name: 'cosmosDb'
  params: {
    name: 'cosmos-${uniqueSuffix}'
    location: location
    maxThroughput: cosmosMaxThroughput
  }
}

// Cosmos DB uses RBAC-only access via managed identity — no connection string secrets needed

// Container Apps Environment
module containerAppsEnv 'modules/container-apps-env.bicep' = {
  name: 'containerAppsEnv'
  params: {
    name: 'cae-${resourcePrefix}'
    location: location
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsWorkspaceId
  }
}

// Todo API Container App (ASP.NET Core 8 Web API)
module todoApi 'modules/container-app.bicep' = {
  name: 'todoApi'
  params: {
    name: 'todoapi'
    location: location
    containerAppsEnvironmentId: containerAppsEnv.outputs.id
    acrLoginServer: acr.outputs.loginServer
    cosmosDbEndpoint: cosmosDb.outputs.endpoint
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    keyVaultUri: keyVault.outputs.uri
    external: true
    targetPort: 8080
  }
}

// Todo Web Container App (Blazor WebAssembly frontend)
module todoWeb 'modules/container-app.bicep' = {
  name: 'todoWeb'
  params: {
    name: 'todoweb'
    location: location
    containerAppsEnvironmentId: containerAppsEnv.outputs.id
    acrLoginServer: acr.outputs.loginServer
    cosmosDbEndpoint: ''
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    keyVaultUri: keyVault.outputs.uri
    external: true
    targetPort: 8080
  }
}

// Grant todoapi managed identity access to Cosmos DB (data contributor - no connection strings)
module cosmosRoleAssignment 'modules/cosmos-role-assignment.bicep' = {
  name: 'cosmosRoleAssignment'
  params: {
    cosmosDbAccountName: cosmosDb.outputs.accountName
    principalId: todoApi.outputs.identityPrincipalId
  }
}

// Grant both apps access to Key Vault secrets
module keyVaultAccessApi 'modules/key-vault-access.bicep' = {
  name: 'keyVaultAccessApi'
  params: {
    keyVaultName: keyVault.outputs.name
    principalId: todoApi.outputs.identityPrincipalId
  }
}

module keyVaultAccessWeb 'modules/key-vault-access.bicep' = {
  name: 'keyVaultAccessWeb'
  params: {
    keyVaultName: keyVault.outputs.name
    principalId: todoWeb.outputs.identityPrincipalId
  }
}

// Grant both apps pull access to ACR
module acrPullApi 'modules/acr-pull-role.bicep' = {
  name: 'acrPullApi'
  params: {
    acrName: acr.outputs.name
    principalId: todoApi.outputs.identityPrincipalId
  }
}

module acrPullWeb 'modules/acr-pull-role.bicep' = {
  name: 'acrPullWeb'
  params: {
    acrName: acr.outputs.name
    principalId: todoWeb.outputs.identityPrincipalId
  }
}

output todoApiUrl string = 'https://${todoApi.outputs.fqdn}'
output todoWebUrl string = 'https://${todoWeb.outputs.fqdn}'
output acrLoginServer string = acr.outputs.loginServer
output cosmosDbEndpoint string = cosmosDb.outputs.endpoint
output keyVaultUri string = keyVault.outputs.uri
