// Cosmos DB account, database and containers for the IPL store, for real (non-emulator) environments.
// Containers come from ../Schema/schema.json, the same file the maintenance tool verifies against,
// so names, partition keys, TTL and indexing are declared once.
//
// Deploy (through the pipeline, with approval):
//   az deployment group create -g <resource-group> -f main.bicep -p accountName=<name> dataContributorPrincipalIds='["<object-id>"]'
// Then:
//   dotnet run --project IplFranchise.DbMaintenance -- migrate   (verifies the schema first)

@description('Globally unique, lower-case account name.')
param accountName string

param location string = resourceGroup().location

@description('Serverless for dev and test. false = provisioned autoscale per container. Serverless cannot be converted in place.')
param serverless bool = true

@description('Autoscale maximum RU/s per container when serverless is false. Choose from measured request units.')
param autoscaleMaxThroughput int = 1000

@description('Continuous (point-in-time restore) cannot be turned off later; decide before the account is created.')
@allowed([
  'Periodic'
  'Continuous'
])
param backupPolicyType string = 'Periodic'

@description('Turn off account keys so only Entra ID can reach the data. Keep true outside local development.')
param disableLocalAuth bool = true

@description('Object ids of the identities (API, migration pipeline) that read and write documents.')
param dataContributorPrincipalIds array = []

@description('CanNotDelete lock, so a wrongly applied template cannot drop production data.')
param enableDeleteLock bool = true

var schema = loadJsonContent('../Schema/schema.json')

// Cosmos DB Built-in Data Contributor: documents only. Containers stay in this template.
var dataContributorRoleId = '00000000-0000-0000-0000-000000000002'

resource account 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' = {
  name: accountName
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    capabilities: serverless ? [{ name: 'EnableServerless' }] : []
    backupPolicy: backupPolicyType == 'Continuous'
      ? { type: 'Continuous', continuousModeProperties: { tier: 'Continuous7Days' } }
      : { type: 'Periodic' }
    disableLocalAuth: disableLocalAuth
    minimalTlsVersion: 'Tls12'
  }
}

resource deleteLock 'Microsoft.Authorization/locks@2020-05-01' = if (enableDeleteLock) {
  name: '${accountName}-cannot-delete'
  scope: account
  properties: {
    level: 'CanNotDelete'
    notes: 'IPL store data. Remove the lock deliberately before deleting the account.'
  }
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-11-15' = {
  parent: account
  name: schema.databaseName
  properties: {
    resource: {
      id: schema.databaseName
    }
  }
}

resource containers 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = [
  for spec in schema.containers: {
    parent: database
    name: spec.name
    properties: {
      resource: union(
        {
          id: spec.name
          partitionKey: {
            paths: [spec.partitionKeyPath]
            kind: 'Hash'
            version: 2
          }
          indexingPolicy: {
            indexingMode: 'consistent'
            automatic: true
            includedPaths: [{ path: '/*' }]
            excludedPaths: concat([{ path: '/"_etag"/?' }], map(spec.excludedPaths, path => { path: path }))
            compositeIndexes: spec.compositeIndexes
          }
        },
        // null in schema.json means TTL off, which is expressed by leaving defaultTtl out.
        spec.defaultTtlSeconds == null ? {} : { defaultTtl: spec.defaultTtlSeconds }
      )
      options: serverless ? {} : { autoscaleSettings: { maxThroughput: autoscaleMaxThroughput } }
    }
  }
]

resource dataContributors 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-11-15' = [
  for principalId in dataContributorPrincipalIds: {
    parent: account
    name: guid(account.id, principalId, dataContributorRoleId)
    properties: {
      roleDefinitionId: '${account.id}/sqlRoleDefinitions/${dataContributorRoleId}'
      principalId: principalId
      scope: account.id
    }
  }
]

@description('Use as the CosmosDb:ConnectionString "AccountEndpoint=<this>;" (no key: Entra ID).')
output endpoint string = account.properties.documentEndpoint
