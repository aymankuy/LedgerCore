// LedgerCore on Azure: one resource group holds everything, so the whole environment can be
// removed with a single `az group delete`. Every service is on a free tier (ADR 0011).
//
// Deploy (from the main LedgerCore folder):
//   az deployment sub create --location swedencentral --template-file infra/bicep/main.bicep \
//     --parameters sqlAdminLogin=<name> sqlAdminObjectId=<id> deployerObjectId=<id>
// Preview first with `az deployment sub what-if` and the same arguments.

targetScope = 'subscription'

@description('Region for every resource. Sweden Central accepts free-tier Azure SQL for new subscriptions.')
param location string = 'swedencentral'

param resourceGroupName string = 'rg-ledgercore'

@description('Display name of the Microsoft Entra administrator of the SQL server.')
param sqlAdminLogin string

@description('Object id of that administrator: a user, a group or a service principal.')
param sqlAdminObjectId string

@allowed(['User', 'Group', 'Application'])
param sqlAdminPrincipalType string = 'User'

@description('Object id of whoever runs the deployment; it may write Key Vault secrets.')
param deployerObjectId string

@allowed(['User', 'Group', 'ServicePrincipal'])
param deployerPrincipalType string = 'User'

@description('Who is emailed when the subscription spends anything. Empty: no budget alert.')
param budgetContactEmail string = ''

@description('A budget must start on the first day of a month.')
param budgetStartDate string = utcNow('yyyy-MM-01')

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
}

module resources 'resources.bicep' = {
  scope: rg
  name: 'ledgercore-resources'
  params: {
    location: location
    sqlAdminLogin: sqlAdminLogin
    sqlAdminObjectId: sqlAdminObjectId
    sqlAdminPrincipalType: sqlAdminPrincipalType
    deployerObjectId: deployerObjectId
    deployerPrincipalType: deployerPrincipalType
    budgetContactEmail: budgetContactEmail
    budgetStartDate: budgetStartDate
  }
}

output resourceGroupName string = rg.name
output webAppName string = resources.outputs.webAppName
output webAppUrl string = resources.outputs.webAppUrl
output sqlServerName string = resources.outputs.sqlServerName
output sqlServerFqdn string = resources.outputs.sqlServerFqdn
output databaseName string = resources.outputs.databaseName
output keyVaultName string = resources.outputs.keyVaultName
output functionAppName string = resources.outputs.functionAppName
output storageAccountName string = resources.outputs.storageAccountName
