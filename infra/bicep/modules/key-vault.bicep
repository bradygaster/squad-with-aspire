// Subagent: identity-secrets — Key Vault with RBAC; UAMI gets Secrets User
// Holds: auth JWT signing key, rate-limit Redis connstr, SMTP creds, etc.
param name string
param location string
param tags object
param appIdentityPrincipalId string
param additionalSecretReaders array = []

resource kv 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

var secretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource appIdRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: kv
  name: guid(kv.id, appIdentityPrincipalId, secretsUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', secretsUserRoleId)
    principalId: appIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource extraReaderAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (oid, i) in additionalSecretReaders: {
  scope: kv
  name: guid(kv.id, oid, secretsUserRoleId, string(i))
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', secretsUserRoleId)
    principalId: oid
    principalType: 'ServicePrincipal'
  }
}]

output id string = kv.id
output uri string = kv.properties.vaultUri
