// GitHub Actions OIDC federated identity for travel-assistant
// Creates a user-assigned managed identity + federated credentials for the
// repo's main branch + PR + staging/prod environments, and grants Contributor
// on the target resource group. Apply at subscription scope or RG scope as noted.
//
// Usage (RG scope):
//   az deployment group create -g <rg> -f github-oidc.bicep \
//     -p githubOrg=tamirdresher repoName=travel-assistant envName=staging

targetScope = 'resourceGroup'

@description('GitHub org/user that owns the repo')
param githubOrg string = 'tamirdresher'

@description('GitHub repository name')
param repoName string = 'travel-assistant'

@description('Azure environment name (staging | prod)')
@allowed([ 'staging', 'prod' ])
param envName string = 'staging'

@description('Location for the managed identity')
param location string = resourceGroup().location

@description('Name of the user-assigned managed identity')
param identityName string = 'id-gha-${repoName}-${envName}'

var issuer = 'https://token.actions.githubusercontent.com'
var audiences = [ 'api://AzureADTokenExchange' ]

resource uami 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
}

// Federated credential: deploys triggered by pushes to main
resource fedMain 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  parent: uami
  name: 'gha-main'
  properties: {
    issuer: issuer
    subject: 'repo:${githubOrg}/${repoName}:ref:refs/heads/main'
    audiences: audiences
  }
}

// Federated credential: GitHub Environment (staging or prod) — used by deploy-staging.yml
resource fedEnv 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  parent: uami
  name: 'gha-env-${envName}'
  properties: {
    issuer: issuer
    subject: 'repo:${githubOrg}/${repoName}:environment:${envName}'
    audiences: audiences
  }
}

// Federated credential: pull-request preview deploys (optional, used by CI)
resource fedPr 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  parent: uami
  name: 'gha-pr'
  properties: {
    issuer: issuer
    subject: 'repo:${githubOrg}/${repoName}:pull_request'
    audiences: audiences
  }
}

// Contributor on the RG so azd can deploy ACA revisions + update Container Apps
var contributorRoleId = 'b24988ac-6180-42a0-ab88-20f7382dd24c'

resource raContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, uami.id, contributorRoleId)
  properties: {
    principalId: uami.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', contributorRoleId)
  }
}

output AZURE_CLIENT_ID string = uami.properties.clientId
output AZURE_TENANT_ID string = uami.properties.tenantId
output AZURE_SUBSCRIPTION_ID string = subscription().subscriptionId
output identityResourceId string = uami.id
