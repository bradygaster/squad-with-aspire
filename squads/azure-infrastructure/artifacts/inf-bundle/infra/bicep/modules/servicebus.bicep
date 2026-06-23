// INF-4: Service Bus namespace + worker jobs queue
// Contract from APP-9 (app-dev branch feat/app-9-10-infra-contracts @ 1189141)
//
// Decisions:
// - Service Bus (not Storage Queue): need DLQ, duplicate detection, >64KB payloads
// - Queue (not Topic): single consumer; second consumer would trigger ADR + migration
// - AAD-only data plane (no SAS connection strings); runtime MI gets Azure Service Bus Data Owner
//   on the queue scope. Workflow OIDC UAMI keeps Contributor at RG for provisioning.

@description('Environment name: dev | staging | prod')
@allowed(['dev', 'staging', 'prod'])
param env string

@description('Azure region')
param location string = resourceGroup().location

@description('Runtime UAMI principalId (id-travel-app-{env}) for data-plane RBAC')
param runtimePrincipalId string

@description('Tags applied to all resources')
param tags object = {}

var nsName = 'sb-travel-${env}-${uniqueString(resourceGroup().id)}'
var queueName = 'travel-assistant-worker-jobs'

// Standard tier: queues, topics, DLQ, duplicate detection. Premium not required at v0.1
// volumes. Upgrade path is non-breaking (tier change preserves data).
var skuName = env == 'prod' ? 'Standard' : 'Standard'

resource namespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: nsName
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: skuName
  }
  properties: {
    disableLocalAuth: true   // AAD-only — no SAS keys
    zoneRedundant: env == 'prod'
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'  // PE in future iteration; locked by RBAC for now
  }
}

resource queue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: namespace
  name: queueName
  properties: {
    // Per APP-9 contract in docs/architecture/queues.md
    lockDuration: 'PT60S'                  // 60s message lock
    maxDeliveryCount: 5                    // DLQ after 5 attempts
    deadLetteringOnMessageExpiration: true
    requiresDuplicateDetection: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'  // 10-minute dedup window
    requiresSession: false
    enablePartitioning: false
    enableBatchedOperations: true
    defaultMessageTimeToLive: 'P14D'       // 14-day TTL safety net
    maxSizeInMegabytes: 1024
  }
}

// Built-in role: Azure Service Bus Data Owner
// 090c5cfd-751d-490a-894a-3ce6f1109419
var sbDataOwnerRoleId = '090c5cfd-751d-490a-894a-3ce6f1109419'

resource runtimeQueueRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: queue
  name: guid(queue.id, runtimePrincipalId, sbDataOwnerRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', sbDataOwnerRoleId)
    principalId: runtimePrincipalId
    principalType: 'ServicePrincipal'
  }
}

@description('Service Bus namespace FQDN (for AAD-authenticated clients)')
output namespaceFqdn string = '${namespace.name}.servicebus.windows.net'

@description('Service Bus namespace name (for KEDA trigger metadata)')
output namespaceName string = namespace.name

@description('Worker jobs queue name')
output queueName string = queueName

@description('Service Bus namespace resource id (for diagnostic settings)')
output namespaceId string = namespace.id
