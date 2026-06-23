// INF-1: Redis (Basic for dev, Standard+ for staging/prod).
// Used for semantic + provider caches (see FinOps refinement-chip design).
@description('Redis name (globally unique).')
param name string
param location string
param tags object = {}

@allowed([ 'Basic', 'Standard', 'Premium' ])
param skuName string = 'Basic'

@description('Capacity 0-6 (Basic/Standard) or 1-5 (Premium).')
param capacity int = 0

@description('Enable Entra ID (AAD) auth as an additional auth method on Redis. NOTE: this does NOT remove existing access keys — see disableAccessKeyAuthentication for true key disablement (Premium SKU only). DELTA-2 fix: renamed from misleading "disableAccessKeys".')
param enableAadAuth bool = true

@description('TRUE access-key disablement. Only honored on Premium SKU (Microsoft.Cache API requirement, GA 2024). On Basic/Standard, set false and rely on RBAC policy denying listKeys to non-break-glass principals (see docs/security/sec-3/redis-residual-key-risk.md).')
param disableAccessKeyAuthentication bool = false

resource redis 'Microsoft.Cache/redis@2024-11-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    sku: {
      name: skuName
      family: skuName == 'Premium' ? 'P' : 'C'
      capacity: capacity
    }
    enableNonSslPort: false
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    disableAccessKeyAuthentication: skuName == 'Premium' ? disableAccessKeyAuthentication : false
    redisConfiguration: {
      'aad-enabled': enableAadAuth ? 'true' : 'false'
    }
  }
}

output hostName string = redis.properties.hostName
output name string = redis.name
output sslPort int = redis.properties.sslPort
