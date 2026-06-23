// Subagent: bicep-foundation — Application Insights wired to LA workspace
param name string
param location string
param tags object
param workspaceId string

resource appi 'Microsoft.Insights/components@2020-02-02' = {
  name: name
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspaceId
    IngestionMode: 'LogAnalytics'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

#disable-next-line outputs-should-not-contain-secrets
output connectionString string = appi.properties.ConnectionString
output instrumentationKey string = appi.properties.InstrumentationKey
