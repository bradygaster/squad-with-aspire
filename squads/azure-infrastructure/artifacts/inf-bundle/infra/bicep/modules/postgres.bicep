// INF-1: Postgres Flexible Server. Entra-only auth; no admin password stored.
@description('Server name (globally unique).')
param name string
param location string
param tags object = {}

@description('Entra admin object ID (UAMI or user/group principal).')
param entraAdminObjectId string

@description('Entra admin principal name (display).')
param entraAdminName string

@description('Entra admin principal type.')
@allowed([ 'User', 'Group', 'ServicePrincipal' ])
param entraAdminType string = 'ServicePrincipal'

@description('Postgres version.')
param version string = '16'

@description('Compute tier sku name (e.g. Standard_B1ms for dev, Standard_D2ds_v5 for staging+).')
param skuName string = 'Standard_B1ms'

@allowed([ 'Burstable', 'GeneralPurpose', 'MemoryOptimized' ])
param tier string = 'Burstable'

@description('Storage in GiB.')
param storageSizeGB int = 32

@description('Database names to create.')
param databases array = [ 'travelassistant' ]

@description('Container Apps env outbound IPs (or other explicit allowlist). Empty array = no public firewall rules (private endpoint path). DELTA-1 fix: do NOT use 0.0.0.0/0.0.0.0 (AllowAllAzureServicesAndResourcesWithinAzureIps) — it permits TCP from any Azure tenant.')
param allowedOutboundIps array = []

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: name
  location: location
  tags: tags
  sku: { name: skuName, tier: tier }
  properties: {
    version: version
    storage: { storageSizeGB: storageSizeGB, autoGrow: 'Enabled' }
    authConfig: {
      activeDirectoryAuth: 'Enabled'
      passwordAuth: 'Disabled'
      tenantId: subscription().tenantId
    }
    highAvailability: { mode: 'Disabled' }
    backup: { backupRetentionDays: 7, geoRedundantBackup: 'Disabled' }
    network: { publicNetworkAccess: 'Enabled' }
  }
}

resource allowAzure 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = [for (ip, i) in allowedOutboundIps: {
  parent: server
  name: 'AllowContainerAppsOutbound-${i}'
  properties: { startIpAddress: ip, endIpAddress: ip }
}]

resource entraAdmin 'Microsoft.DBforPostgreSQL/flexibleServers/administrators@2024-08-01' = {
  parent: server
  name: entraAdminObjectId
  properties: {
    principalType: entraAdminType
    principalName: entraAdminName
    tenantId: subscription().tenantId
  }
}

resource dbs 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = [for db in databases: {
  parent: server
  name: db
  properties: { charset: 'UTF8', collation: 'en_US.utf8' }
}]

output fqdn string = server.properties.fullyQualifiedDomainName
output name string = server.name
output databases array = databases
