// SEC-CHK-008 R6 bundle 9a — Orchestrator for synthetic canary monitor stack.
// Composes managed-identity.bicep + checkout-terminal-canary.bicep + alerts.bicep.
// Two instances per slot (stripe + adyen) so failure attribution is unambiguous.

@description('Environment name (canary or prod slot).')
@allowed([
  'canary'
  'prod'
])
param environment string

@description('Azure region.')
param location string = resourceGroup().location

@description('Resource name prefix.')
param namePrefix string

@description('Container Apps Environment ID — same as checkout API for matched network posture.')
param containerAppsEnvironmentId string

@description('Canary-scoped Key Vault ID (sandbox provider creds + test PANs).')
param canaryKeyVaultId string

@description('Canary-scoped Key Vault URI.')
param canaryKeyVaultUri string

@description('Application Insights component ID — canary metric namespace.')
param applicationInsightsId string

@description('Application Insights connection string.')
@secure()
param applicationInsightsConnectionString string

@description('Log Analytics workspace ID for canary logs.')
param logAnalyticsWorkspaceId string

@description('Checkout API base URL on the slot under test.')
param checkoutApiBaseUrl string

@description('Container image for canary runner.')
param canaryRunnerImage string

@description('PagerDuty P1 action group ID.')
param pagerDutyP1ActionGroupId string

@description('PagerDuty P2 action group ID.')
param pagerDutyP2ActionGroupId string

@description('PagerDuty P3 action group ID.')
param pagerDutyP3ActionGroupId string

module mi 'managed-identity.bicep' = {
  name: 'canary-mi-${environment}'
  params: {
    environment: environment
    location: location
    namePrefix: namePrefix
    canaryKeyVaultId: canaryKeyVaultId
    logAnalyticsWorkspaceId: logAnalyticsWorkspaceId
    applicationInsightsId: applicationInsightsId
  }
}

module stripeRunner 'checkout-terminal-canary.bicep' = {
  name: 'canary-stripe-${environment}'
  params: {
    environment: environment
    location: location
    namePrefix: namePrefix
    containerAppsEnvironmentId: containerAppsEnvironmentId
    canaryRunnerMiId: mi.outputs.canaryRunnerMiId
    canaryRunnerMiClientId: mi.outputs.canaryRunnerMiClientId
    applicationInsightsConnectionString: applicationInsightsConnectionString
    checkoutApiBaseUrl: checkoutApiBaseUrl
    canaryKeyVaultUri: canaryKeyVaultUri
    canaryRunnerImage: canaryRunnerImage
    paymentProvider: 'stripe'
  }
}

module adyenRunner 'checkout-terminal-canary.bicep' = {
  name: 'canary-adyen-${environment}'
  params: {
    environment: environment
    location: location
    namePrefix: namePrefix
    containerAppsEnvironmentId: containerAppsEnvironmentId
    canaryRunnerMiId: mi.outputs.canaryRunnerMiId
    canaryRunnerMiClientId: mi.outputs.canaryRunnerMiClientId
    applicationInsightsConnectionString: applicationInsightsConnectionString
    checkoutApiBaseUrl: checkoutApiBaseUrl
    canaryKeyVaultUri: canaryKeyVaultUri
    canaryRunnerImage: canaryRunnerImage
    paymentProvider: 'adyen'
  }
}

module alerts 'alerts.bicep' = {
  name: 'canary-alerts-${environment}'
  params: {
    environment: environment
    location: location
    namePrefix: namePrefix
    applicationInsightsId: applicationInsightsId
    pagerDutyP1ActionGroupId: pagerDutyP1ActionGroupId
    pagerDutyP2ActionGroupId: pagerDutyP2ActionGroupId
    pagerDutyP3ActionGroupId: pagerDutyP3ActionGroupId
  }
}

output stripeRunnerId string = stripeRunner.outputs.canaryRunnerId
output adyenRunnerId string = adyenRunner.outputs.canaryRunnerId
output canaryRunnerMiPrincipalId string = mi.outputs.canaryRunnerMiPrincipalId
output alertIds object = alerts.outputs.alertIds
