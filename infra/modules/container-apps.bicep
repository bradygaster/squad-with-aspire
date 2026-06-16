@description('Base name for the app')
param appName string

@description('Azure region')
param location string

@description('Container image for the API')
param apiImage string

@description('Container image for the Web frontend')
param webImage string

@description('Cosmos DB account endpoint')
param cosmosDbEndpoint string

@description('Cosmos DB account resource ID')
param cosmosDbAccountId string

@description('Application Insights connection string')
param appInsightsConnectionString string

@description('Key Vault URI')
param keyVaultUri string

@description('Log Analytics workspace ID')
param logAnalyticsWorkspaceId string

var envName = '${appName}-env'

resource containerAppEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: envName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: reference(logAnalyticsWorkspaceId, '2023-09-01').customerId
        sharedKey: listKeys(logAnalyticsWorkspaceId, '2023-09-01').primarySharedKey
      }
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
        allowInsecure: false
      }
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
            { name: 'AZURE_COSMOS_DB_ENDPOINT', value: cosmosDbEndpoint }
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
        allowInsecure: false
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

// --- RBAC: Grant API app Cosmos DB data access via managed identity ---
// Cosmos DB Built-in Data Contributor role
var cosmosDataContributorRoleId = '00000000-0000-0000-0000-000000000002'

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' existing = {
  name: split(cosmosDbAccountId, '/')[8]
}

resource apiCosmosRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = {
  parent: cosmosAccount
  name: guid(cosmosAccount.id, apiApp.id, cosmosDataContributorRoleId)
  properties: {
    roleDefinitionId: '${cosmosAccount.id}/sqlRoleDefinitions/${cosmosDataContributorRoleId}'
    principalId: apiApp.identity.principalId
    scope: cosmosAccount.id
  }
}

// --- RBAC: Grant API app Key Vault Secrets User access ---
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource apiKeyVaultRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVaultUri, apiApp.id, keyVaultSecretsUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: apiApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}
