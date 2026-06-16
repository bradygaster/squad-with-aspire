@description('Base name for resources')
param appName string

@description('Azure region')
param location string

@description('Application Insights connection string')
param appInsightsConnectionString string

var envName = '${appName}-env-${uniqueString(resourceGroup().id)}'

resource containerAppEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: envName
  location: location
  properties: {
    daprAIConnectionString: appInsightsConnectionString
  }
}

output environmentId string = containerAppEnv.id
output name string = containerAppEnv.name
