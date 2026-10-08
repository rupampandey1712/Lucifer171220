using '../main.bicep'

param environmentName = 'staging'
param skuTier = 'Standard'
param minReplicas = 1
param enableFrontDoor = false
// Entra ID group that administers Azure SQL (set per tenant).
param sqlAdminGroupObjectId = '00000000-0000-0000-0000-000000000000'
param sqlAdminGroupName = 'staysphere-sql-admins-staging'
