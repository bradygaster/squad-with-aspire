@description('Name of the container app')
param name string

@description('Azure region')
param location string

@description('Resource tags')
param tags object = {}

@description('Container Apps Environment resource ID')
param containerAppsEnvironmentId string

@description('ACR login server')
param acrLoginServer string

@description('Cosmos DB endpoint (empty string if not needed)')
param cosmosDbEndpoint string

@description('Application Insights connection string')
param appInsightsConnectionString string

@description('Key Vault URI')
param keyVaultUri string

@description('Whether the app is externally accessible')
param external bool = true

@description('Container target port')
param targetPort int = 8080

resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: name
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironmentId
    configuration: {
      ingress: {
        external: external
        targetPort: targetPort
        transport: 'http'
        allowInsecure: false
      }
      registries: [
        {
          server: acrLoginServer
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: name
          image: '${acrLoginServer}/${name}:latest'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: concat(
            [
              {
                name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
                value: appInsightsConnectionString
              }
              {
                name: 'AZURE_KEYVAULT_URI'
                value: keyVaultUri
              }
            ],
            !empty(cosmosDbEndpoint) ? [
              {
                name: 'COSMOS_DB_ENDPOINT'
                value: cosmosDbEndpoint
              }
            ] : []
          )
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 3
        rules: [
          {
            name: 'http-scaling'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
}

output fqdn string = containerApp.properties.configuration.ingress.fqdn
output identityPrincipalId string = containerApp.identity.principalId
output name string = containerApp.name
