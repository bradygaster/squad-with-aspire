// SEC-CHK-008 R6 bundle 9a — Synthetic canary runner for checkout terminal timing
// Decision Q-9a-1: Container App runner (NOT Azure Monitor synthetic).
//   Rationale: Azure Monitor synthetic supports GET/multistep but POST with custom JSON body,
//   per-request HTTPS headers (Authorization bearer, idempotency key, provider stripe-test mode
//   header), and CSPRNG amount jitter is awkward/limited. Container App gives full control
//   and the same VNet posture as the checkout API, so the canary measures the path real
//   customers take (front-door → WAF → CDN → kestrel → app), not a synthetic shortcut.
// Decision Q-9a-2: include `session_id` dimension on canary metric.
//   Cardinality is bounded: 16/min × 60 × 24 × 4 reasons × 2 slots ≈ 184k unique session_ids/day
//   per slot — well under App Insights custom-dimension limits. Debug value > cardinality cost.
//
// Owner: azure-infrastructure-squad
// Reviewers: security-hardening-squad, qa, review-deployment-squad

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

@description('Container Apps Environment resource ID (shared with checkout API for same-network posture).')
param containerAppsEnvironmentId string

@description('Resource ID of the canary runner user-assigned managed identity (from managed-identity.bicep).')
param canaryRunnerMiId string

@description('Client ID of the canary runner managed identity (for DefaultAzureCredential disambiguation).')
param canaryRunnerMiClientId string

@description('Connection string for Application Insights canary namespace.')
@secure()
param applicationInsightsConnectionString string

@description('Target checkout API base URL for the slot under test (canary or prod).')
param checkoutApiBaseUrl string

@description('Canary KV URI holding sandbox provider credentials + test PANs.')
param canaryKeyVaultUri string

@description('Container image for the canary runner (built from synthetic-monitors/runner/).')
param canaryRunnerImage string

@description('Provider for this canary instance (one runner per provider to keep failure attribution clean).')
@allowed([
  'stripe'
  'adyen'
])
param paymentProvider string

@description('Samples per minute per provider per slot. Default 16 = ~3.75s cadence.')
@minValue(8)
@maxValue(32)
param samplesPerMinute int = 16

// ----------------------------------------------------------------------------
// Container App canary runner
// One replica, always-on, low resource footprint. Posts to /checkout/confirm
// on the configured slot every (60 / samplesPerMinute) seconds, rotating
// through 4 terminal test cards per provider.
// ----------------------------------------------------------------------------
resource canaryRunner 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-canary-${paymentProvider}-${environment}'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${canaryRunnerMiId}': {}
    }
  }
  tags: {
    purpose: 'sec-chk-008-r6-canary'
    provider: paymentProvider
    slot: environment
    'metric-namespace': 'checkout.canary_terminal_response_timing'
  }
  properties: {
    environmentId: containerAppsEnvironmentId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: null
      secrets: []
    }
    template: {
      revisionSuffix: 'canary-${uniqueString(deployment().name)}'
      containers: [
        {
          name: 'canary-runner'
          image: canaryRunnerImage
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            {
              name: 'AZURE_CLIENT_ID'
              value: canaryRunnerMiClientId
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: applicationInsightsConnectionString
            }
            {
              name: 'CANARY_TARGET_URL'
              value: '${checkoutApiBaseUrl}/api/checkout/confirm'
            }
            {
              name: 'CANARY_PROVIDER'
              value: paymentProvider
            }
            {
              name: 'CANARY_SLOT'
              value: environment
            }
            {
              name: 'CANARY_SAMPLES_PER_MINUTE'
              value: string(samplesPerMinute)
            }
            {
              name: 'CANARY_METRIC_NAMESPACE'
              value: 'checkout.canary_terminal_response_timing'
            }
            {
              name: 'CANARY_KEY_VAULT_URI'
              value: canaryKeyVaultUri
            }
            // Test card mapping: read from KV at startup (sandbox PANs, no security value
            // but kept out of bicep/src per grep gate G6 from QA bundle 9a).
            // Expected mapping per security spec:
            //   stripe-hard-decline-pan → hard_decline
            //   stripe-fraud-block-pan → fraud_block
            //   stripe-insufficient-funds-terminal-pan → insufficient_funds_terminal
            //   stripe-provider-rejected-permanent-pan + amount>10000 → provider_rejected_permanent
            //   (adyen equivalents read from corresponding adyen-*-pan secrets)
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: 8080
              }
              initialDelaySeconds: 30
              periodSeconds: 30
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: {
        // Exactly one replica. Two replicas = double sample rate = alert math breaks.
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

output canaryRunnerId string = canaryRunner.id
output canaryRunnerName string = canaryRunner.name
output canaryRunnerFqdn string = canaryRunner.properties.configuration.ingress == null ? '' : canaryRunner.properties.configuration.ingress.fqdn
