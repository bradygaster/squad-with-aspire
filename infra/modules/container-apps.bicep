@description('Base name for the app')
param appName string

@description('Azure region')
param location string

@description('Container image for the API')
param apiImage string

@description('Container image for the Web frontend')
param webImage string

@secure()
@description('Cosmos DB connection string')
param cosmosDbConnectionString string

@description('Application Insights connection string')
param appInsightsConnectionString string

@description('Key Vault URI')
param keyVaultUri string

var envName = '${appName}-env'

resource containerAppEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: envName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
    }
  }
}

resource apiApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${appName}-api'
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: containerAppEnv.id
    configuration: {
      ingress: {
        external: false
        targetPort: 8080
        transport: 'http'
      }
      secrets: [
        {
          name: 'cosmos-connection-string'
          value: cosmosDbConnectionString
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: !empty(apiImage) ? apiImage : 'mcr.microsoft.com/dotnet/samples:aspnetapp'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
            { name: 'AZURE_KEY_VAULT_ENDPOINT', value: keyVaultUri }
            { name: 'ConnectionStrings__cosmos', secretRef: 'cosmos-connection-string' }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 5
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

resource webApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${appName}-web'
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: containerAppEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
      }
    }
    template: {
      containers: [
        {
          name: 'web'
          image: !empty(webImage) ? webImage : 'mcr.microsoft.com/dotnet/samples:aspnetapp'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
            { name: 'services__api__https__0', value: 'https://${apiApp.properties.configuration.ingress.fqdn}' }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 3
      }
    }
  }
}

output apiUrl string = 'https://${apiApp.properties.configuration.ingress.fqdn}'
output webUrl string = 'https://${webApp.properties.configuration.ingress.fqdn}'
