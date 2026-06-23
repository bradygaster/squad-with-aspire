// =============================================================================
// Subagent: policy-guardrails
// Resource-group-scoped policy assignments enforcing org-wide baseline.
// =============================================================================
targetScope = 'resourceGroup'

@description('Display name suffix for policy assignments')
param envName string

var policyDisablePublicStorage = '/providers/Microsoft.Authorization/policyDefinitions/b2982f36-99f2-4db5-8eff-283140c09693'
var policyKvSoftDelete = '/providers/Microsoft.Authorization/policyDefinitions/1e66c121-a66a-4b1f-9b83-0fd99bf0fc2d'
var policyKvPurgeProtection = '/providers/Microsoft.Authorization/policyDefinitions/0b60c0b2-2dc2-4e1c-b5c9-abbed971de53'
var policyKvDiagnostics = '/providers/Microsoft.Authorization/policyDefinitions/cf820ca0-f99e-4f3e-84fb-66e913812d21'

resource asgDisableStoragePublic 'Microsoft.Authorization/policyAssignments@2023-04-01' = {
  name: 'squad-${envName}-disable-storage-public'
  properties: {
    displayName: 'squad/${envName}: deny public storage'
    policyDefinitionId: policyDisablePublicStorage
    enforcementMode: 'Default'
  }
}

resource asgKvSoftDelete 'Microsoft.Authorization/policyAssignments@2023-04-01' = {
  name: 'squad-${envName}-kv-soft-delete'
  properties: {
    displayName: 'squad/${envName}: KV soft-delete required'
    policyDefinitionId: policyKvSoftDelete
    enforcementMode: 'Default'
  }
}

resource asgKvPurge 'Microsoft.Authorization/policyAssignments@2023-04-01' = {
  name: 'squad-${envName}-kv-purge'
  properties: {
    displayName: 'squad/${envName}: KV purge protection required'
    policyDefinitionId: policyKvPurgeProtection
    enforcementMode: 'Default'
  }
}

resource asgKvDiag 'Microsoft.Authorization/policyAssignments@2023-04-01' = {
  name: 'squad-${envName}-kv-diag'
  properties: {
    displayName: 'squad/${envName}: KV diagnostics required'
    policyDefinitionId: policyKvDiagnostics
    enforcementMode: 'Default'
  }
}
