// All StaySphere resources for one environment. Every service is reached with a user-assigned Managed Identity:
// no connection-string secrets for SQL, Service Bus or Storage. Only app secrets (JWT/quote/webhook keys) live in Key Vault.
param name string
param location string
param tags object
param imageTag string
param skuTier string
param minReplicas int
param sqlAdminGroupObjectId string
param sqlAdminGroupName string
param enableFrontDoor bool
param webOrigins array
param aiProvider string
param aiModel string
param aiApiKeyInKeyVault bool

var compact = replace(name, '-', '')
var isPremium = skuTier == 'Premium'

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${name}'
  location: location
  tags: tags
}

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${name}'
  location: location
  tags: tags
  properties: { sku: { name: 'PerGB2018' }, retentionInDays: 30 }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${name}'
  location: location
  tags: tags
  kind: 'web'
  properties: { Application_Type: 'web', WorkspaceResourceId: logs.id }
}

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  #disable-next-line BCP334
  name: 'acr${compact}'
  location: location
  tags: tags
  sku: { name: isPremium ? 'Premium' : 'Basic' }
  properties: { adminUserEnabled: false }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  #disable-next-line BCP334
  name: 'kv-${take(compact, 20)}'
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    enablePurgeProtection: true
  }
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: 'sql-${name}'
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: sqlAdminGroupName
      sid: sqlAdminGroupObjectId
      tenantId: subscription().tenantId
      principalType: 'Group'
    }
  }
}

resource sqlDb 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: 'staysphere'
  location: location
  tags: tags
  sku: isPremium ? { name: 'GP_Gen5_2', tier: 'GeneralPurpose' } : { name: 'S0', tier: 'Standard' }
  properties: { zoneRedundant: isPremium, requestedBackupStorageRedundancy: isPremium ? 'Geo' : 'Local' }
}

resource sqlAllowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

resource redis 'Microsoft.Cache/redis@2024-03-01' = {
  name: 'redis-${name}'
  location: location
  tags: tags
  properties: {
    sku: { name: isPremium ? 'Premium' : 'Standard', family: isPremium ? 'P' : 'C', capacity: 1 }
    minimumTlsVersion: '1.2'
    enableNonSslPort: false
    redisConfiguration: { 'aad-enabled': 'true' }
  }
}

resource serviceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: 'sb-${name}'
  location: location
  tags: tags
  sku: { name: isPremium ? 'Premium' : 'Standard' }
  properties: { disableLocalAuth: true, minimumTlsVersion: '1.2' }
}

resource topic 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  parent: serviceBus
  name: 'staysphere.events'
  properties: { requiresDuplicateDetection: true, duplicateDetectionHistoryTimeWindow: 'PT10M', defaultMessageTimeToLive: 'P7D' }
}

resource subscriptionWorkers 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: topic
  name: 'staysphere-workers'
  properties: { maxDeliveryCount: 10, lockDuration: 'PT1M', deadLetteringOnMessageExpiration: true }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  #disable-next-line BCP334
  name: 'st${take(compact, 22)}'
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: isPremium ? 'Standard_ZRS' : 'Standard_LRS' }
  properties: { minimumTlsVersion: 'TLS1_2', allowSharedKeyAccess: false, allowBlobPublicAccess: true, supportsHttpsTrafficOnly: true }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource imagesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'property-images'
  properties: { publicAccess: 'Blob' }
}

resource publicContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'public'
  properties: { publicAccess: 'Blob' }
}

// ---------- RBAC for the workload identity ----------
var roles = {
  acrPull: '7f951dda-4ed3-4680-a7ca-43fe172d538d'
  serviceBusDataOwner: '090c5cfd-751d-490a-894a-3ce6f1109419'
  blobDataContributor: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
  keyVaultSecretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, identity.id, roles.acrPull)
  scope: acr
  properties: { principalId: identity.properties.principalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.acrPull) }
}

resource sbRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBus.id, identity.id, roles.serviceBusDataOwner)
  scope: serviceBus
  properties: { principalId: identity.properties.principalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.serviceBusDataOwner) }
}

resource blobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, identity.id, roles.blobDataContributor)
  scope: storage
  properties: { principalId: identity.properties.principalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.blobDataContributor) }
}

resource kvRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, identity.id, roles.keyVaultSecretsUser)
  scope: keyVault
  properties: { principalId: identity.properties.principalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsUser) }
}

resource redisAccess 'Microsoft.Cache/redis/accessPolicyAssignments@2024-03-01' = {
  parent: redis
  name: 'staysphere-app'
  properties: { accessPolicyName: 'Data Contributor', objectId: identity.properties.principalId, objectIdAlias: 'staysphere-app' }
}

// ---------- Compute ----------
resource containerEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${name}'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: { destination: 'log-analytics', logAnalyticsConfiguration: { customerId: logs.properties.customerId, sharedKey: logs.listKeys().primarySharedKey } }
    zoneRedundant: false
  }
}

var sqlConnection = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${sqlDb.name};Authentication=Active Directory Managed Identity;User Id=${identity.properties.clientId};Encrypt=True'
var commonEnv = concat([
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId }
  { name: 'ConnectionStrings__Sql', value: sqlConnection }
  { name: 'ConnectionStrings__Redis', value: '${redis.properties.hostName}:6380,ssl=true,abortConnect=false' }
  { name: 'Storage__Provider', value: 'Blob' }
  { name: 'Storage__AccountUrl', value: storage.properties.primaryEndpoints.blob }
  { name: 'Messaging__Transport', value: 'ServiceBus' }
  { name: 'Messaging__ServiceBusNamespace', value: '${serviceBus.name}.servicebus.windows.net' }
  { name: 'ExternalServices__Mode', value: 'Live' }
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
  { name: 'Jwt__SigningKey', secretRef: 'jwt-signing-key' }
  { name: 'Quotes__SigningKey', secretRef: 'quote-signing-key' }
  { name: 'Payments__Fake__WebhookSecret', secretRef: 'payment-webhook-secret' }
  { name: 'AI__Provider', value: aiProvider }
  { name: 'AI__Model', value: aiModel }
], aiApiKeyInKeyVault ? [{ name: 'AI__ApiKey', secretRef: 'ai-api-key' }] : [])
var secrets = concat([
  { name: 'jwt-signing-key', keyVaultUrl: '${keyVault.properties.vaultUri}secrets/jwt-signing-key', identity: identity.id }
  { name: 'quote-signing-key', keyVaultUrl: '${keyVault.properties.vaultUri}secrets/quote-signing-key', identity: identity.id }
  { name: 'payment-webhook-secret', keyVaultUrl: '${keyVault.properties.vaultUri}secrets/payment-webhook-secret', identity: identity.id }
], aiApiKeyInKeyVault ? [{ name: 'ai-api-key', keyVaultUrl: '${keyVault.properties.vaultUri}secrets/ai-api-key', identity: identity.id }] : [])

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-${name}-api'
  location: location
  tags: tags
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identity.id}': {} } }
  dependsOn: [acrPull, kvRole]
  properties: {
    managedEnvironmentId: containerEnv.id
    configuration: {
      ingress: { external: true, targetPort: 8080, transport: 'auto', stickySessions: { affinity: 'sticky' } }
      registries: [{ server: acr.properties.loginServer, identity: identity.id }]
      secrets: secrets
    }
    template: {
      containers: [{
        name: 'api'
        image: '${acr.properties.loginServer}/staysphere-api:${imageTag}'
        resources: { cpu: json('1.0'), memory: '2Gi' }
        env: concat(commonEnv, [
          // The API serves HTTP; outbox/consumers/jobs run in the workers app.
          { name: 'Workers__Outbox', value: 'false' }
          { name: 'Workers__Consumers', value: 'false' }
          { name: 'Workers__Jobs', value: 'false' }
          { name: 'Cors__Origins__0', value: length(webOrigins) > 0 ? webOrigins[0] : 'https://${staticWeb.properties.defaultHostname}' }
        ])
        probes: [
          { type: 'Liveness', httpGet: { path: '/health/live', port: 8080 }, periodSeconds: 15 }
          { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080 }, periodSeconds: 10 }
        ]
      }]
      scale: { minReplicas: minReplicas, maxReplicas: 10, rules: [{ name: 'http', http: { metadata: { concurrentRequests: '80' } } }] }
    }
  }
}

resource workers 'Microsoft.App/containerApps@2024-10-02-preview' = {
  name: 'ca-${name}-workers'
  location: location
  tags: tags
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identity.id}': {} } }
  dependsOn: [acrPull, kvRole, sbRole]
  properties: {
    managedEnvironmentId: containerEnv.id
    configuration: {
      registries: [{ server: acr.properties.loginServer, identity: identity.id }]
      secrets: secrets
    }
    template: {
      containers: [{
        name: 'workers'
        image: '${acr.properties.loginServer}/staysphere-workers:${imageTag}'
        resources: { cpu: json('0.5'), memory: '1Gi' }
        env: commonEnv
      }]
      // KEDA scales consumers on subscription backlog (jobs are idempotent across replicas).
      scale: {
        minReplicas: 1
        maxReplicas: 5
        rules: [{
          name: 'servicebus-backlog'
          custom: {
            type: 'azure-servicebus'
            metadata: { namespace: serviceBus.name, topicName: topic.name, subscriptionName: subscriptionWorkers.name, messageCount: '50' }
            identity: identity.id
          }
        }]
      }
    }
  }
}

resource staticWeb 'Microsoft.Web/staticSites@2023-12-01' = {
  name: 'swa-${name}'
  location: 'westeurope'
  tags: tags
  sku: { name: isPremium ? 'Standard' : 'Free', tier: isPremium ? 'Standard' : 'Free' }
  properties: {}
}

module frontDoor 'frontdoor.bicep' = if (enableFrontDoor) {
  name: 'frontdoor'
  params: { name: name, tags: tags, apiHost: api.properties.configuration.ingress.fqdn, webHost: staticWeb.properties.defaultHostname }
}

output apiUrl string = 'https://${api.properties.configuration.ingress.fqdn}'
output webUrl string = enableFrontDoor ? frontDoor!.outputs.endpoint : 'https://${staticWeb.properties.defaultHostname}'
output acrLoginServer string = acr.properties.loginServer
