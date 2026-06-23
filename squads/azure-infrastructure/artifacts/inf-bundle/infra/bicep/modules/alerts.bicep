// INF-5: Alert rules — cost burn-rate + 5xx spike.
@description('Action group resource id (email/SMS/webhook fan-out).')
param actionGroupId string

@description('App Insights resource id.')
param appInsightsId string

@description('Log Analytics workspace resource id.')
param logAnalyticsId string

@description('Environment tag (dev/staging/prod).')
param environmentName string

param location string = resourceGroup().location
param tags object = {}

// Alert 1: 5xx spike — > 5% of HTTP requests over 5 min.
resource fivexxAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-${environmentName}-5xx-spike'
  location: location
  tags: tags
  properties: {
    displayName: '5xx spike (${environmentName})'
    severity: 1
    enabled: true
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    scopes: [ appInsightsId ]
    criteria: {
      allOf: [
        {
          query: 'requests | where timestamp > ago(5m) | summarize total=count(), errs=countif(resultCode startswith "5") | extend rate=todouble(errs)/iff(total==0,1,total) | project rate'
          timeAggregation: 'Maximum'
          metricMeasureColumn: 'rate'
          operator: 'GreaterThan'
          threshold: 0.05
          failingPeriods: { numberOfEvaluationPeriods: 1, minFailingPeriodsToAlert: 1 }
        }
      ]
    }
    actions: { actionGroups: [ actionGroupId ] }
  }
}

// Alert 2: LLM cost burn-rate — hourly spend projects > 100% of daily budget.
// Daily budget passed as a workspace constant; tweak per env.
resource costBurnAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-${environmentName}-llm-cost-burn'
  location: location
  tags: tags
  properties: {
    displayName: 'LLM cost burn-rate > 100% of daily budget (${environmentName})'
    severity: 2
    enabled: true
    evaluationFrequency: 'PT15M'
    windowSize: 'PT1H'
    scopes: [ logAnalyticsId ]
    criteria: {
      allOf: [
        {
          query: 'customMetrics | where name == "llm.cost.usd" and timestamp > ago(1h) | summarize hourly_usd=sum(value) | extend projected_daily=hourly_usd*24 | project projected_daily'
          timeAggregation: 'Maximum'
          metricMeasureColumn: 'projected_daily'
          operator: 'GreaterThan'
          threshold: environmentName == 'prod' ? 60 : (environmentName == 'staging' ? 15 : 5)
          failingPeriods: { numberOfEvaluationPeriods: 1, minFailingPeriodsToAlert: 1 }
        }
      ]
    }
    actions: { actionGroups: [ actionGroupId ] }
  }
}

// Alert 3: Container App revision unhealthy.
resource revisionAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-${environmentName}-revision-unhealthy'
  location: location
  tags: tags
  properties: {
    displayName: 'Container App revision unhealthy (${environmentName})'
    severity: 1
    enabled: true
    evaluationFrequency: 'PT5M'
    windowSize: 'PT10M'
    scopes: [ logAnalyticsId ]
    criteria: {
      allOf: [
        {
          query: 'ContainerAppSystemLogs_CL | where Log_s contains "Liveness probe failed" or Log_s contains "CrashLoopBackOff" | summarize c=count() by ContainerAppName_s | where c > 3'
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: { numberOfEvaluationPeriods: 1, minFailingPeriodsToAlert: 1 }
        }
      ]
    }
    actions: { actionGroups: [ actionGroupId ] }
  }
}

// Alert 4: Token-rate surge — > 500k input+output tokens per 5 min (APP-10 metric contract)
resource tokenRateAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-${environmentName}-llm-token-surge'
  location: location
  tags: tags
  properties: {
    displayName: 'LLM token rate surge (${environmentName})'
    severity: 2
    enabled: true
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    scopes: [ appInsightsId ]
    criteria: {
      allOf: [
        {
          query: 'customMetrics | where name in ("llm.tokens.in","llm.tokens.out") and timestamp > ago(5m) | summarize tokens=sum(value) | project tokens'
          timeAggregation: 'Maximum'
          metricMeasureColumn: 'tokens'
          operator: 'GreaterThan'
          threshold: 500000
          failingPeriods: { numberOfEvaluationPeriods: 1, minFailingPeriodsToAlert: 1 }
        }
      ]
    }
    actions: { actionGroups: [ actionGroupId ] }
  }
}

// Alert 5: Chip cache hit-rate degraded — < 30% over 15 min (APP-10 metric contract).
// Low hit-rate => prompt cost regression; investigate before budget alert fires.
resource chipCacheAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-${environmentName}-chip-cache-degraded'
  location: location
  tags: tags
  properties: {
    displayName: 'Chip cache hit-rate degraded (${environmentName})'
    severity: 3
    enabled: true
    evaluationFrequency: 'PT15M'
    windowSize: 'PT15M'
    scopes: [ appInsightsId ]
    criteria: {
      allOf: [
        {
          query: 'customMetrics | where name == "chip.cache.hit" and timestamp > ago(15m) | extend result = tostring(customDimensions.result) | summarize hits=countif(result=="hit"), total=count() | extend rate = todouble(hits)/iff(total==0,1,total) | project rate'
          timeAggregation: 'Minimum'
          metricMeasureColumn: 'rate'
          operator: 'LessThan'
          threshold: 0.30
          failingPeriods: { numberOfEvaluationPeriods: 1, minFailingPeriodsToAlert: 1 }
        }
      ]
    }
    actions: { actionGroups: [ actionGroupId ] }
  }
}

// Alert 6: Worker queue backlog — > 500 active messages for 10 min (KEDA scale ceiling reached).
resource workerQueueAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-${environmentName}-worker-queue-backlog'
  location: location
  tags: tags
  properties: {
    displayName: 'Worker queue backlog (${environmentName})'
    severity: 2
    enabled: true
    evaluationFrequency: 'PT5M'
    windowSize: 'PT10M'
    scopes: [ appInsightsId ]
    criteria: {
      allOf: [
        {
          query: 'AzureMetrics | where ResourceProvider == "MICROSOFT.SERVICEBUS" and MetricName == "ActiveMessages" and TimeGenerated > ago(10m) | summarize depth=max(Maximum) | project depth'
          timeAggregation: 'Maximum'
          metricMeasureColumn: 'depth'
          operator: 'GreaterThan'
          threshold: 500
          failingPeriods: { numberOfEvaluationPeriods: 2, minFailingPeriodsToAlert: 2 }
        }
      ]
    }
    actions: { actionGroups: [ actionGroupId ] }
  }
}

output alertIds array = [
  fivexxAlert.id
  costBurnAlert.id
  revisionAlert.id
  tokenRateAlert.id
  chipCacheAlert.id
  workerQueueAlert.id
]
