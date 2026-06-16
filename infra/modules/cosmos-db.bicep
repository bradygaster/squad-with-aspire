@description('Cosmos DB account name')
param name string

@description('Azure region')
param location string

@description('Autoscale max throughput in RU/s')
param maxThroughput int = 1000

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-02-15-preview' = {
  name: name
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    minimalTlsVersion: 'Tls12'
    disableLocalAuth: true
    publicNetworkAccess: 'Disabled'
  }
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-02-15-preview' = {
  parent: cosmosAccount
  name: 'TodoDb'
  properties: {
    resource: {
      id: 'TodoDb'
    }
  }
}

resource container 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-02-15-preview' = {
  parent: database
  name: 'Items'
  properties: {
    resource: {
      id: 'Items'
      partitionKey: {
        paths: ['/userId']
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        includedPaths: [
          { path: '/*' }
        ]
        excludedPaths: [
          { path: '/"_etag"/?' }
        ]
      }
    }
    options: {
      autoscaleSettings: {
        maxThroughput: maxThroughput
      }
    }
  }
}

output accountName string = cosmosAccount.name
output endpoint string = cosmosAccount.properties.documentEndpoint
// No connection string output — RBAC-only access via managed identity
output databaseName string = database.properties.resource.id
output containerName string = container.properties.resource.id
