targetScope = 'resourceGroup'

@description('The base name for all resources')
param appName string = 'todolist'

@description('The Azure region for resource deployment')
param location string = resourceGroup().location

@description('The container image for the API')
param apiImage string = ''

@description('The container image for the Web frontend')
param webImage string = ''

// --- Modules ---

module cosmosDb 'modules/cosmos-db.bicep' = {
  name: 'cosmosDb'
  params: {
    appName: appName
    location: location
  }
}

module keyVault 'modules/key-vault.bicep' = {
  name: 'keyVault'
  params: {
    appName: appName
    location: location
  }
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    appName: appName
    location: location
  }
}

module containerApps 'modules/container-apps.bicep' = {
  name: 'containerApps'
  params: {
    appName: appName
    location: location
    apiImage: apiImage
    webImage: webImage
    cosmosDbEndpoint: cosmosDb.outputs.endpoint
    cosmosDbAccountId: cosmosDb.outputs.accountId
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    keyVaultUri: keyVault.outputs.vaultUri
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsWorkspaceId
  }
}

// --- Outputs ---

output cosmosDbEndpoint string = cosmosDb.outputs.endpoint
output keyVaultUri string = keyVault.outputs.vaultUri
output appInsightsConnectionString string = monitoring.outputs.appInsightsConnectionString
output apiUrl string = containerApps.outputs.apiUrl
output webUrl string = containerApps.outputs.webUrl
