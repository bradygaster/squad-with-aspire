@description('Cosmos DB account name')
param cosmosDbAccountName string

@description('Principal ID to grant data access')
param principalId string

@description('Database name for container-level scope')
param databaseName string = 'TodoDb'

@description('Container name for container-level scope')
param containerName string = 'Items'

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-02-15-preview' existing = {
  name: cosmosDbAccountName
}

// Cosmos DB Built-in Data Contributor role — scoped to container level (least privilege)
resource sqlRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-02-15-preview' = {
  parent: cosmosAccount
  name: guid(cosmosAccount.id, principalId, '00000000-0000-0000-0000-000000000002')
  properties: {
    roleDefinitionId: '${cosmosAccount.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002'
    principalId: principalId
    scope: '${cosmosAccount.id}/dbs/${databaseName}/colls/${containerName}'
  }
}
