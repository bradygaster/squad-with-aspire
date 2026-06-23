// Subagent: bicep-foundation — Log Analytics workspace (Aspire telemetry sink)
param name string
param location string
param tags object
@minValue(7)
@maxValue(730)
param retentionDays int = 30

resource ws 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: retentionDays
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

output workspaceId string = ws.id
output customerId string = ws.properties.customerId
#disable-next-line outputs-should-not-contain-secrets
output primarySharedKey string = ws.listKeys().primarySharedKey
