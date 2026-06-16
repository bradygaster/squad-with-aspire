@description('The name of the Container Apps Environment')
param name string

@description('The location for the resource')
param location string

@description('The Log Analytics workspace ID')
param logAnalyticsWorkspaceId string

@description('Application Insights connection string for Dapr/telemetry')
param appInsightsConnectionString string

param tags object = {}

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: reference(logAnalyticsWorkspaceId, '2023-09-01').customerId
        sharedKey: listKeys(logAnalyticsWorkspaceId, '2023-09-01').primarySharedKey
      }
    }
    daprAIConnectionString: appInsightsConnectionString
  }
}

output id string = containerAppsEnvironment.id
output name string = containerAppsEnvironment.name
output defaultDomain string = containerAppsEnvironment.properties.defaultDomain
