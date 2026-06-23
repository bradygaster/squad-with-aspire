// INF-4: Worker Container App with KEDA Service Bus scaler
// Contract from APP-9 (app-dev branch feat/app-9-10-infra-contracts @ 1189141)
//
// KEDA scale rule:
//   - trigger: azure-servicebus, messageCount = 20 messages per replica
//   - replicas: min 0, max 10
//   - identity: workload identity (runtime UAMI) — no connection string

@description('Environment name: dev | staging | prod')
@allowed(['dev', 'staging', 'prod'])
param env string

@description('Azure region')
param location string = resourceGroup().location

@description('Container Apps Environment resource id')
param containerAppsEnvironmentId string

@description('ACR login server (e.g., acrtraveldev.azurecr.io)')
param acrLoginServer string

@description('Container image tag (sha or version)')
param imageTag string = 'latest'

@description('Runtime UAMI resource id (id-travel-app-{env}) — used for ACR pull + AAD data plane')
param runtimeUamiResourceId string

@description('Runtime UAMI client id (for KEDA workload identity)')
param runtimeUamiClientId string

@description('Service Bus namespace FQDN — from servicebus.bicep output')
param serviceBusNamespaceFqdn string

@description('Service Bus namespace name — from servicebus.bicep output')
param serviceBusNamespaceName string

@description('Worker queue name — from servicebus.bicep output')
param workerQueueName string

@description('Key Vault URI — non-secret routing (KeyVault__Uri env var)')
param keyVaultUri string

@description('Application Insights connection string')
param appInsightsConnectionString string

@description('Tags')
param tags object = {}

var appName = 'ca-worker-${env}'

resource workerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: appName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${runtimeUamiResourceId}': {}
    }
  }
  properties: {
    environmentId: containerAppsEnvironmentId
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [
        {
          server: acrLoginServer
          identity: runtimeUamiResourceId
        }
      ]
      // No secrets block — all auth via MI / AAD
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: '${acrLoginServer}/travel-assistant-worker:${imageTag}'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'AZURE_CLIENT_ID', value: runtimeUamiClientId }
            { name: 'KeyVault__Uri', value: keyVaultUri }
            { name: 'ServiceBus__FullyQualifiedNamespace', value: serviceBusNamespaceFqdn }
            { name: 'ServiceBus__WorkerQueue', value: workerQueueName }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
            { name: 'OTEL_SERVICE_NAME', value: 'travel-assistant-worker' }
            { name: 'DOTNET_ENVIRONMENT', value: env == 'prod' ? 'Production' : (env == 'staging' ? 'Staging' : 'Development') }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/health/live', port: 8080 }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health/ready', port: 8080 }
              initialDelaySeconds: 5
              periodSeconds: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 10
        rules: [
          {
            name: 'worker-jobs-queue'
            custom: {
              type: 'azure-servicebus'
              identity: runtimeUamiResourceId
              metadata: {
                namespace: serviceBusNamespaceName
                queueName: workerQueueName
                messageCount: '20'
              }
            }
          }
        ]
      }
    }
  }
}

@description('Worker Container App name')
output workerAppName string = workerApp.name

@description('Worker Container App resource id')
output workerAppId string = workerApp.id
