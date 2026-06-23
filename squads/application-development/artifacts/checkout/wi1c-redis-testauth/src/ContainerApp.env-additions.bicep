// Patch fragment for infra/checkout/modules/containerApp.bicep
// Adds REDIS endpoint env-var per azure-infrastructure-squad's hand-off note.
// Apply: insert into the env: [] array of the api container definition.

// === Add to params block ===
@description('Redis cache endpoint (from main.bicep redis module output).')
param redisEndpoint string

@description('Idempotency backend selector: "memory" (dev) or "redis" (staging/prod).')
@allowed([ 'memory', 'redis' ])
param idempotencyBackend string = 'redis'

// === Add to the container env: [] block ===
{
  name: 'Checkout__IdempotencyBackend'
  value: idempotencyBackend
}
{
  name: 'Checkout__Redis__Endpoint'
  value: redisEndpoint
}
{
  name: 'Checkout__Redis__Port'
  value: '6380'
}

// === Add to main.bicep where containerApp module is instantiated ===
module containerApp 'modules/containerApp.bicep' = {
  // ...existing params...
  params: {
    // ...
    redisEndpoint: redis.outputs.redisEndpoint
    idempotencyBackend: environmentName == 'dev' ? 'memory' : 'redis'
  }
}
