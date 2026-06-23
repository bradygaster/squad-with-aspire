// INF-2: Azure OpenAI account + model deployments.
// Parameterized by region so fallback regions in aoai-regions.md can be swapped without code changes.
@description('AOAI account name (must be globally unique).')
param name string

@description('Location. See docs/ops/aoai-regions.md for primary + fallbacks.')
param location string

@description('Custom subdomain for the account (required for MI auth + private endpoints).')
param customSubDomainName string = name

@description('Model deployments. Each entry: { name, model: { name, version }, sku: { name, capacity } }.')
param deployments array = [
  {
    name: 'gpt-4o'
    model: { format: 'OpenAI', name: 'gpt-4o', version: '2024-08-06' }
    sku: { name: 'GlobalStandard', capacity: 50 }
  }
  {
    name: 'gpt-4o-mini'
    model: { format: 'OpenAI', name: 'gpt-4o-mini', version: '2024-07-18' }
    sku: { name: 'GlobalStandard', capacity: 200 }
  }
  {
    name: 'text-embedding-3-small'
    model: { format: 'OpenAI', name: 'text-embedding-3-small', version: '1' }
    sku: { name: 'Standard', capacity: 100 }
  }
]

@description('Disable local (key) auth — force Entra ID + MI.')
param disableLocalAuth bool = true

@description('Container Apps env outbound IPs to allowlist when networkAcls defaultAction=Deny. DELTA-3 fix: prefer deny-by-default + explicit allowlist over Allow-all. Empty array + denyByDefault=true makes the AOAI endpoint reachable only from inside the trust boundary (and the deployer principal for management plane).')
param allowedOutboundIps array = []

@description('Toggle networkAcls.defaultAction. true=Deny (production posture, requires allowedOutboundIps populated); false=Allow (dev fallback). DELTA-3 fix: default to true.')
param denyByDefault bool = true

param tags object = {}

resource account 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: name
  location: location
  kind: 'OpenAI'
  sku: { name: 'S0' }
  tags: tags
  properties: {
    customSubDomainName: customSubDomainName
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: disableLocalAuth
    networkAcls: {
      defaultAction: denyByDefault ? 'Deny' : 'Allow'
      ipRules: [for ip in allowedOutboundIps: { value: ip }]
      virtualNetworkRules: []
    }
  }
  identity: { type: 'SystemAssigned' }
}

@batchSize(1)
resource deploys 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = [for d in deployments: {
  parent: account
  name: d.name
  sku: d.sku
  properties: {
    model: d.model
    raiPolicyName: 'Microsoft.DefaultV2'
    versionUpgradeOption: 'OnceCurrentVersionExpired'
  }
}]

output accountId string = account.id
output endpoint string = account.properties.endpoint
output name string = account.name
