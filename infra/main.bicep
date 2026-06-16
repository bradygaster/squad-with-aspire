targetScope = 'resourceGroup'

@description('The base name for all resources')
param appName string = 'todo'

@description('The Azure region for all resources')
param location string = resourceGroup().location

@description('Cosmos DB autoscale max throughput (RU/s)')
param cosmosMaxThroughput int = 1000

var uniqueSuffix = uniqueString(resourceGroup().id, appName)
var resourcePrefix = '${appName}-${uniqueSuffix}'
var tags = {
  application: 'todo-app'
  managedBy: 'bicep'
}

// Networking (VNet for Container Apps + private endpoints)
module networking 'modules/networking.bicep' = {
  name: 'networking'
  params: {
    location: location
    resourceToken: uniqueSuffix
  }
}

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

// Azure Cosmos DB (RBAC-only, no connection strings)
module cosmosDb 'modules/cosmos-db.bicep' = {
  name: 'cosmosDb'
  params: {
    name: 'cosmos-${uniqueSuffix}'
    location: location
    maxThroughput: cosmosMaxThroughput
  }
}

// Container Apps Environment with VNet integration
module containerAppsEnv 'modules/container-apps-environment.bicep' = {
  name: 'containerAppsEnv'
  params: {
    name: 'cae-${resourcePrefix}'
    location: location
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsWorkspaceId
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    infrastructureSubnetId: networking.outputs.infrastructureSubnetId
    tags: tags
  }
}

// Todo API (internal, accessed by the web frontend)
module todoApi 'modules/container-app.bicep' = {
  name: 'todoApi'
  params: {
    name: 'todoapi'
    location: location
    tags: tags
    containerAppsEnvironmentId: containerAppsEnv.outputs.id
    acrLoginServer: acr.outputs.loginServer
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    cosmosDbEndpoint: cosmosDb.outputs.endpoint
    keyVaultUri: keyVault.outputs.uri
    external: false
    targetPort: 8080
  }
}

// Todo Web Frontend (external, public-facing)
module todoWeb 'modules/container-app.bicep' = {
  name: 'todoWeb'
  params: {
    name: 'todoweb'
    location: location
    tags: tags
    containerAppsEnvironmentId: containerAppsEnv.outputs.id
    acrLoginServer: acr.outputs.loginServer
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    cosmosDbEndpoint: ''
    keyVaultUri: keyVault.outputs.uri
    external: true
    targetPort: 8080
  }
}

// Grant API managed identity access to Cosmos DB (Data Contributor)
module cosmosRoleAssignment 'modules/cosmos-role-assignment.bicep' = {
  name: 'cosmosRoleAssignment'
  params: {
    cosmosDbAccountName: cosmosDb.outputs.accountName
    principalId: todoApi.outputs.identityPrincipalId
  }
}

// Grant API managed identity access to Key Vault secrets
module keyVaultAccessApi 'modules/key-vault-access.bicep' = {
  name: 'keyVaultAccessApi'
  params: {
    keyVaultName: keyVault.outputs.name
    principalId: todoApi.outputs.identityPrincipalId
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