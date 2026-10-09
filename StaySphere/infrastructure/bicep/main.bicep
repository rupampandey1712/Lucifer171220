// StaySphere — Azure infrastructure entry point (subscription scope).
// az deployment sub create -l westeurope -f main.bicep -p env/staging.bicepparam -p imageTag=<sha>
targetScope = 'subscription'

@allowed(['dev', 'test', 'staging', 'prod'])
param environmentName string
param location string = deployment().location
@description('Container image tag to deploy (git sha).')
param imageTag string = 'latest'
@description('Basic | Standard | Premium — maps to per-service SKUs.')
@allowed(['Basic', 'Standard', 'Premium'])
param skuTier string = 'Basic'
param minReplicas int = 1
@description('Object id + login of the Entra group that administers Azure SQL (Entra-only auth, no SQL passwords).')
param sqlAdminGroupObjectId string
param sqlAdminGroupName string
@description('Front Door + WAF in front of the API and SPA (recommended for prod).')
param enableFrontDoor bool = false
@description('Origins allowed by CORS (the SPA URL).')
param webOrigins array = []
@description('AI assistant provider: Gemini (default), OpenAI (incl. Azure OpenAI) or Rules (offline).')
param aiProvider string = 'Gemini'
@description('Pinned model id for the AI provider. Empty = provider default.')
param aiModel string = ''
@description('True once the Key Vault secret "ai-api-key" exists. Without it the assistant runs the offline rule engine.')
param aiApiKeyInKeyVault bool = false

var name = 'staysphere-${environmentName}'
var tags = { application: 'StaySphere', environment: environmentName }

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-${name}'
  location: location
  tags: tags
}

module resources 'modules/resources.bicep' = {
  name: 'staysphere-resources'
  scope: rg
  params: {
    name: name
    location: location
    tags: tags
    imageTag: imageTag
    skuTier: skuTier
    minReplicas: minReplicas
    sqlAdminGroupObjectId: sqlAdminGroupObjectId
    sqlAdminGroupName: sqlAdminGroupName
    enableFrontDoor: enableFrontDoor
    webOrigins: webOrigins
    aiProvider: aiProvider
    aiModel: aiModel
    aiApiKeyInKeyVault: aiApiKeyInKeyVault
  }
}

output resourceGroup string = rg.name
output apiUrl string = resources.outputs.apiUrl
output webUrl string = resources.outputs.webUrl
output acrLoginServer string = resources.outputs.acrLoginServer
