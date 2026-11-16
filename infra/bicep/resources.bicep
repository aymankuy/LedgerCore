// Everything inside the resource group. Deployed by main.bicep.

param location string
param sqlAdminLogin string
param sqlAdminObjectId string
param sqlAdminPrincipalType string
param deployerObjectId string
param deployerPrincipalType string
param budgetContactEmail string
param budgetStartDate string

// Globally unique names are derived from the resource group, so a redeploy finds the same
// resources instead of creating new ones.
var suffix = uniqueString(resourceGroup().id)
var databaseName = 'ledgercore'

// Built-in role ids: https://learn.microsoft.com/azure/role-based-access-control/built-in-roles
var keyVaultSecretsUser = '4633458b-17de-408a-b874-0445c86b69e6'
var keyVaultSecretsOfficer = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
var storageBlobDataOwner = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
var storageBlobDataReader = '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'

// ---------------------------------------------------------------------------------------------
// Key Vault: the only place the connection string and the JWT signing key live. Access is by
// Azure RBAC, so the App Service reads secrets with its managed identity and nothing else can.
// ---------------------------------------------------------------------------------------------
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-ledgercore-${take(suffix, 8)}'
  location: location
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

// ---------------------------------------------------------------------------------------------
// Azure SQL: Microsoft Entra authentication only, so no SQL password exists anywhere.
// ---------------------------------------------------------------------------------------------
resource sqlServer 'Microsoft.Sql/servers@2023-08-01' = {
  name: 'sql-ledgercore-${suffix}'
  location: location
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: sqlAdminLogin
      sid: sqlAdminObjectId
      principalType: sqlAdminPrincipalType
      tenantId: tenant().tenantId
    }
  }
}

// The free App Service tier has no virtual network integration, so the server accepts traffic
// from Azure services. Every connection still has to authenticate through Microsoft Entra.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// The free offer: 100,000 vCore seconds and 32 GB a month. When the allowance runs out the
// database pauses until the next month instead of billing. It also pauses after 15 idle minutes.
resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: databaseName
  location: location
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    maxSizeBytes: 34359738368
    requestedBackupStorageRedundancy: 'Local'
    zoneRedundant: false
  }
}

// Passwordless: the App Service signs in to the database with its managed identity.
resource connectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'ConnectionStrings--Ledger'
  properties: {
    value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${databaseName};Authentication=Active Directory Managed Identity;Encrypt=True;'
  }
}

// ---------------------------------------------------------------------------------------------
// App Service: the API on the free Linux tier. Settings point at Key Vault, never at values.
// ---------------------------------------------------------------------------------------------
resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'asp-ledgercore'
  location: location
  kind: 'linux'
  sku: {
    name: 'F1'
    tier: 'Free'
  }
  properties: {
    reserved: true
  }
}

resource webApp 'Microsoft.Web/sites@2024-04-01' = {
  name: 'app-ledgercore-${suffix}'
  location: location
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    keyVaultReferenceIdentity: 'SystemAssigned'
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|9.0'
      alwaysOn: false
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      // Liveness only: pinging readiness every minute would keep the database from pausing.
      healthCheckPath: '/health/live'
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'ConnectionStrings__Ledger'
          value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=${connectionStringSecret.name})'
        }
        {
          // Created by the deploy script, never by this template, so it is not in any
          // deployment history and survives a redeploy unchanged.
          name: 'Jwt__SigningKey'
          value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=Jwt--SigningKey)'
        }
      ]
    }
  }
}

resource webAppReadsSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, webApp.id, keyVaultSecretsUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUser)
    principalId: webApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource deployerWritesSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, deployerObjectId, keyVaultSecretsOfficer)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsOfficer)
    principalId: deployerObjectId
    principalType: deployerPrincipalType
  }
}

// ---------------------------------------------------------------------------------------------
// Storage: the Function's own state and deployment package, and the reconciliation reports.
// Shared keys are disabled, so every client signs in with Microsoft Entra; no key can leak.
// ---------------------------------------------------------------------------------------------
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'stledgercore${take(suffix, 10)}'
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'function-deployment'
}

resource reportsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'reconciliation'
}

// ---------------------------------------------------------------------------------------------
// Function App on Flex Consumption: scales to zero between its nightly runs, so it costs
// nothing while idle, and its free monthly grant covers two short runs a night.
// ---------------------------------------------------------------------------------------------
resource functionPlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'asp-ledgercore-functions'
  location: location
  kind: 'functionapp'
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  properties: {
    reserved: true
  }
}

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: 'func-ledgercore-${suffix}'
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: functionPlan.id
    httpsOnly: true
    keyVaultReferenceIdentity: 'SystemAssigned'
    siteConfig: {
      minTlsVersion: '1.2'
      appSettings: [
        {
          // Identity-based host storage: no connection string with a key.
          name: 'AzureWebJobsStorage__accountName'
          value: storage.name
        }
        {
          name: 'ConnectionStrings__Ledger'
          value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=${connectionStringSecret.name})'
        }
        {
          name: 'Reports__ContainerUri'
          value: '${storage.properties.primaryEndpoints.blob}${reportsContainer.name}'
        }
      ]
    }
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deploymentContainer.name}'
          authentication: {
            type: 'SystemAssignedIdentity'
          }
        }
      }
      // One instance at most: the nightly jobs never run twice side by side, and cost stays bounded.
      scaleAndConcurrency: {
        maximumInstanceCount: 1
        instanceMemoryMB: 512
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '9.0'
      }
    }
  }
}

resource functionOwnsBlobs 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, functionApp.id, storageBlobDataOwner)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataOwner)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource functionReadsSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, functionApp.id, keyVaultSecretsUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUser)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// The deployer can read the reports, for example from the portal, but not change them.
resource deployerReadsReports 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, deployerObjectId, storageBlobDataReader)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataReader)
    principalId: deployerObjectId
    principalType: deployerPrincipalType
  }
}

// ---------------------------------------------------------------------------------------------
// Budget: an email as soon as this resource group costs anything at all.
// ---------------------------------------------------------------------------------------------
resource budget 'Microsoft.Consumption/budgets@2023-11-01' = if (!empty(budgetContactEmail)) {
  name: 'budget-ledgercore'
  properties: {
    category: 'Cost'
    amount: 5
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: budgetStartDate
    }
    notifications: {
      anySpend: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 1
        thresholdType: 'Actual'
        contactEmails: [budgetContactEmail]
      }
      forecastOverBudget: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [budgetContactEmail]
      }
    }
  }
}

output webAppName string = webApp.name
output webAppUrl string = 'https://${webApp.properties.defaultHostName}'
output sqlServerName string = sqlServer.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output databaseName string = databaseName
output keyVaultName string = keyVault.name
output functionAppName string = functionApp.name
output storageAccountName string = storage.name
