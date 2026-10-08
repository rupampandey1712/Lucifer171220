using '../main.bicep'

param environmentName = 'prod'
param skuTier = 'Premium'
param minReplicas = 2
param enableFrontDoor = true
// Entra ID group that administers Azure SQL (set per tenant).
param sqlAdminGroupObjectId = '00000000-0000-0000-0000-000000000000'
param sqlAdminGroupName = 'staysphere-sql-admins-prod'
