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

// Store Cosmos DB connection string in Key Vault
module cosmosSecret 'modules/key-vault-secret.bicep' = {
  name: 'cosmosSecret'
  params: {
    keyVaultName: keyVault.outputs.name
    secretName: 'CosmosDbConnectionString'
    secretValue: cosmosDb.outputs.connectionString
  }
}

// Azure Container Apps Environment + App
module containerApp 'modules/container-apps.bicep' = {
  name: 'containerApp'
  params: {
    name: resourcePrefix
    location: location
    containerImage: containerImage
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsWorkspaceId
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    cosmosDbEndpoint: cosmosDb.outputs.endpoint
    keyVaultUri: keyVault.outputs.uri
    acrLoginServer: acr.outputs.loginServer
  }
}

// Grant Container App's managed identity access to Cosmos DB
module cosmosRoleAssignment 'modules/cosmos-role-assignment.bicep' = {
  name: 'cosmosRoleAssignment'
  params: {
    cosmosDbAccountName: cosmosDb.outputs.accountName
    principalId: containerApp.outputs.identityPrincipalId
  }
}

// Grant Container App's managed identity access to Key Vault
module keyVaultAccess 'modules/key-vault-access.bicep' = {
  name: 'keyVaultAccess'
  params: {
    keyVaultName: keyVault.outputs.name
    principalId: containerApp.outputs.identityPrincipalId
  }
}

// Grant Container App's managed identity pull access to ACR
module acrPull 'modules/acr-pull-role.bicep' = {
  name: 'acrPull'
  params: {
    acrName: acr.outputs.name
    principalId: containerApp.outputs.identityPrincipalId
  }
}

output containerAppFqdn string = containerApp.outputs.fqdn
output containerAppUrl string = 'https://${containerApp.outputs.fqdn}'
output acrLoginServer string = acr.outputs.loginServer
output cosmosDbEndpoint string = cosmosDb.outputs.endpoint
output keyVaultUri string = keyVault.outputs.uri
