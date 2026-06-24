// SEC-CHK-008 R6 bundle 9a — Canary alert rules
// 5 rules per security-hardening spec. PagerDuty action groups wired separately
// (existing in `infra/bicep/observability/action-groups.bicep`).
//
// Metric namespace: `checkout.canary_terminal_response_timing` (NEW — does NOT widen
// existing `checkout.terminal_response_timing`). Dimensions on the metric:
//   - reason_internal  (hard_decline | fraud_block | insufficient_funds_terminal | provider_rejected_permanent)
//   - provider         (stripe | adyen)
//   - slot             (canary | prod)
//   - session_id       (per Q-9a-2 — included for debugging)
//
// Owner: azure-infrastructure-squad
// Reviewers: security-hardening-squad

@description('Environment name.')
@allowed([
  'canary'
  'prod'
])
param environment string

@description('Azure region.')
param location string

@description('Resource name prefix.')
param namePrefix string

@description('Application Insights resource ID (canary metric source).')
param applicationInsightsId string

@description('PagerDuty P1 action group resource ID.')
param pagerDutyP1ActionGroupId string

@description('PagerDuty P2 action group resource ID.')
param pagerDutyP2ActionGroupId string

@description('PagerDuty P3 action group resource ID.')
param pagerDutyP3ActionGroupId string

@description('Per-reason p99 ceiling (ms). Floor 800 + 200 slack = 1000.')
param p99CeilingMs int = 1000

@description('Pair-divergence ceiling (ms). >100 = fraud-oracle leak detector firing.')
param pairDivergenceCeilingMs int = 100

@description('Minimum acceptable sample rate before A4 fires.')
param minSamplesPerMinute int = 12

// ----------------------------------------------------------------------------
// A1 — Per-reason p99 ceiling (PagerDuty P2)
// Sanity check. NOT the actual fraud-oracle detector — that's A2.
// ----------------------------------------------------------------------------
resource alertA1P99 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${namePrefix}-canary-A1-p99-ceiling-${environment}'
  location: location
  tags: {
    purpose: 'sec-chk-008-r6-canary'
    rule: 'A1-p99-ceiling'
    severity: 'P2'
  }
  properties: {
    displayName: 'A1 — Canary per-reason p99 > ${p99CeilingMs}ms (${environment})'
    description: 'Per-reason p99 of canary terminal response timing exceeded floor + 200ms slack for 5 min.'
    severity: 2
    enabled: true
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    scopes: [
      applicationInsightsId
    ]
    criteria: {
      allOf: [
        {
          query: '''
            customMetrics
            | where name == "checkout.canary_terminal_response_timing"
            | where customDimensions.slot == "${environment}"
            | summarize p99=percentile(value, 99) by tostring(customDimensions.reason_internal), tostring(customDimensions.provider)
            | where p99 > ${p99CeilingMs}
          '''
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 5
            minFailingPeriodsToAlert: 5
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        pagerDutyP2ActionGroupId
      ]
    }
    autoMitigate: true
  }
}

// ----------------------------------------------------------------------------
// A2 — Pair-divergence (PagerDuty P2) — THE actual fraud-oracle leak detector.
// Drift BETWEEN reasons is the leak; absolute floor (A1) is just sanity.
// ----------------------------------------------------------------------------
resource alertA2PairDivergence 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${namePrefix}-canary-A2-pair-divergence-${environment}'
  location: location
  tags: {
    purpose: 'sec-chk-008-r6-canary'
    rule: 'A2-pair-divergence'
    severity: 'P2'
    'fraud-oracle-detector': 'true'
  }
  properties: {
    displayName: 'A2 — Canary pair-divergence > ${pairDivergenceCeilingMs}ms (${environment}) — FRAUD ORACLE LEAK'
    description: 'Pair-wise p99 divergence between any two terminal reasons exceeded ${pairDivergenceCeilingMs}ms for 5 min. This IS the fraud-oracle leak detector.'
    severity: 2
    enabled: true
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    scopes: [
      applicationInsightsId
    ]
    criteria: {
      allOf: [
        {
          query: '''
            customMetrics
            | where name == "checkout.canary_terminal_response_timing"
            | where customDimensions.slot == "${environment}"
            | summarize p99=percentile(value, 99) by tostring(customDimensions.reason_internal), tostring(customDimensions.provider)
            | summarize maxP99=max(p99), minP99=min(p99) by provider=tostring(customDimensions_provider)
            | extend divergence = maxP99 - minP99
            | where divergence > ${pairDivergenceCeilingMs}
          '''
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 5
            minFailingPeriodsToAlert: 5
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        pagerDutyP2ActionGroupId
      ]
    }
    autoMitigate: true
  }
}

// ----------------------------------------------------------------------------
// A3 — Mapper drift (PagerDuty P1)
// Test card stopped producing expected enum. Recalibrate bundle 9a before re-enabling.
// ----------------------------------------------------------------------------
resource alertA3MapperDrift 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${namePrefix}-canary-A3-mapper-drift-${environment}'
  location: location
  tags: {
    purpose: 'sec-chk-008-r6-canary'
    rule: 'A3-mapper-drift'
    severity: 'P1'
  }
  properties: {
    displayName: 'A3 — Canary mapper drift detected (${environment})'
    description: 'A test PAN stopped producing its expected terminal reason. Provider taxonomy may have changed. Recalibrate bundle 9a before re-enabling canary.'
    severity: 1
    enabled: true
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    scopes: [
      applicationInsightsId
    ]
    criteria: {
      allOf: [
        {
          query: '''
            customEvents
            | where name == "canary.mapper_drift"
            | where customDimensions.slot == "${environment}"
          '''
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        pagerDutyP1ActionGroupId
      ]
    }
    autoMitigate: false
  }
}

// ----------------------------------------------------------------------------
// A4 — Canary sample rate floor (PagerDuty P3)
// Canary runner itself broken (replica crash, kestrel timeout, etc.)
// ----------------------------------------------------------------------------
resource alertA4SampleRate 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${namePrefix}-canary-A4-sample-rate-${environment}'
  location: location
  tags: {
    purpose: 'sec-chk-008-r6-canary'
    rule: 'A4-sample-rate'
    severity: 'P3'
  }
  properties: {
    displayName: 'A4 — Canary sample rate < ${minSamplesPerMinute}/min (${environment})'
    description: 'Canary runner emitting fewer than expected samples — likely runner broken (replica crash, KV unreachable, MI auth failure).'
    severity: 3
    enabled: true
    evaluationFrequency: 'PT5M'
    windowSize: 'PT10M'
    scopes: [
      applicationInsightsId
    ]
    criteria: {
      allOf: [
        {
          query: '''
            customMetrics
            | where name == "checkout.canary_terminal_response_timing"
            | where customDimensions.slot == "${environment}"
            | summarize samples=count() by bin(timestamp, 1m), tostring(customDimensions.provider)
            | where samples < ${minSamplesPerMinute}
          '''
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 2
            minFailingPeriodsToAlert: 2
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        pagerDutyP3ActionGroupId
      ]
    }
    autoMitigate: true
  }
}

// ----------------------------------------------------------------------------
// A5 — Metric ingestion floor (PagerDuty P3)
// No samples at all — App Insights ingestion broken upstream of A4 detection.
// ----------------------------------------------------------------------------
resource alertA5MetricIngestion 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${namePrefix}-canary-A5-metric-ingestion-${environment}'
  location: location
  tags: {
    purpose: 'sec-chk-008-r6-canary'
    rule: 'A5-metric-ingestion'
    severity: 'P3'
  }
  properties: {
    displayName: 'A5 — Canary metric not received in 10 min (${environment})'
    description: 'No `checkout.canary_terminal_response_timing` samples received for 10 min. Application Insights ingestion likely broken.'
    severity: 3
    enabled: true
    evaluationFrequency: 'PT5M'
    windowSize: 'PT10M'
    scopes: [
      applicationInsightsId
    ]
    criteria: {
      allOf: [
        {
          query: '''
            customMetrics
            | where name == "checkout.canary_terminal_response_timing"
            | where customDimensions.slot == "${environment}"
            | summarize total=count()
            | where total == 0
          '''
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        pagerDutyP3ActionGroupId
      ]
    }
    autoMitigate: true
  }
}

output alertIds object = {
  a1: alertA1P99.id
  a2: alertA2PairDivergence.id
  a3: alertA3MapperDrift.id
  a4: alertA4SampleRate.id
  a5: alertA5MetricIngestion.id
}
