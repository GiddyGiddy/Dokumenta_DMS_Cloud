targetScope = 'resourceGroup'

@description('Short lowercase prefix used in resource names (3-10 chars).')
@minLength(3)
@maxLength(10)
param prefix string = 'dokumenta'

param location string = resourceGroup().location

@description('Static Web Apps are only available in a few regions.')
@allowed(['westeurope', 'westus2', 'centralus', 'eastus2', 'eastasia'])
param staticWebAppLocation string = 'westeurope'

@description('Registered parent domain for the Azure DNS public zone.')
param dnsZoneName string = 'giddyitsolutions.com'

@description('Custom hostname to use for the Static Web App.')
param swaCustomDomain string = 'documenta.giddyitsolutions.com'

@description('false = infra only (ACR, DB, env, RabbitMQ). Build/push images, then redeploy with true.')
param deployApps bool = false

@description('Image tag pushed to ACR for all services.')
param imageTag string = '1'

param pgAdminUser string = 'dokumentaadmin'
@secure()
param pgAdminPassword string
@secure()
param rabbitPassword string
@description('Shared secret: ExtractionService -> DocuManagementApp downloads (ServiceDownload:ApiKey).')
@secure()
param downloadApiKey string
@description('Shared secret: SearchService -> ExtractionService artifacts.')
@secure()
param artifactApiKey string
@description('Shared secret: DocuManagementApp -> SearchService.')
@secure()
param searchApiKey string

@description('Document Intelligence endpoint (e.g. https://<name>.cognitiveservices.azure.com). Leave empty until created.')
param documentIntelligenceEndpoint string = ''
@secure()
param documentIntelligenceKey string = ''
@description('Google AI Studio API key (Gemini) used by ExtractionService.')
@secure()
param geminiKey string = ''
@description('A current Gemini model, e.g. gemini-3.8-flash (see ai.google.dev/gemini-api/docs/models).')
param geminiModel string = 'gemini-3.8-flash'
@description('OpenAI API key used by SearchService for embeddings.')
@secure()
param openAiKey string = ''
param embeddingsModel string = 'text-embedding-3-small'
@description('API key for the Azure OpenAI Agentic Search deployment. Agentic Search remains disabled when this is empty.')
@secure()
param agenticSearchApiKey string = ''
@description('Enable Agentic Search when its API key is supplied.')
param agenticSearchEnabled bool = true
@description('Azure OpenAI Agentic Search endpoint.')
param agenticSearchEndpoint string = 'https://dokumenta-fr.cognitiveservices.azure.com/'
param agenticSearchApiVersion string = '2024-12-01-preview'
param agenticSearchModel string = 'gpt-5.4-mini'

var rabbitUser = 'dms'
var uniq = uniqueString(resourceGroup().id)
var acrName = toLower('${prefix}${uniq}')
var pgName = toLower('${prefix}-pg-${uniq}')
var swaName = '${prefix}-web-${uniq}'
var storageName = toLower('st${take(prefix, 8)}${take(uniq, 12)}')
var pgHost = '${pgName}.postgres.database.azure.com'
var databases = ['documanagementdb', 'orchestratordb', 'extractiondb', 'searchdb']

func pgConn(host string, db string, user string, pwd string) string =>
  'Host=${host};Port=5432;Database=${db};Username=${user};Password=${pwd};Ssl Mode=Require;Trust Server Certificate=true'

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    workspaceCapping: { dailyQuotaGb: json('0.5') }
  }
}

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: acrName
  location: location
  sku: { name: 'Basic' }
  properties: { adminUserEnabled: false }
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${prefix}-id'
  location: location
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: acr
  name: guid(acr.id, identity.id, 'acrpull')
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource documentsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'documents'
  properties: { publicAccess: 'None' }
}

resource blobContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, identity.id, 'blobcontributor')
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  }
}

resource pg 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: pgName
  location: location
  sku: { name: 'Standard_B1ms', tier: 'Burstable' }
  properties: {
    version: '16'
    administratorLogin: pgAdminUser
    administratorLoginPassword: pgAdminPassword
    storage: { storageSizeGB: 32 }
    backup: { backupRetentionDays: 7, geoRedundantBackup: 'Disabled' }
    highAvailability: { mode: 'Disabled' }
    network: { publicNetworkAccess: 'Enabled' }
  }
}

resource pgExtensions 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: pg
  name: 'azure.extensions'
  properties: { value: 'VECTOR,PG_TRGM', source: 'user-override' }
}

// 0.0.0.0 = "Allow Azure services" (cheapest; no VNet). Tighten with VNet integration later.
resource pgAllowAzure 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: pg
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

@batchSize(1)
resource dbs 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = [for d in databases: {
  parent: pg
  name: d
  dependsOn: [pgExtensions]
}]

resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${prefix}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

resource rabbit 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'rabbitmq'
  location: location
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      ingress: { external: false, transport: 'tcp', targetPort: 5672, exposedPort: 5672 }
      secrets: [{ name: 'rabbit-pass', value: rabbitPassword }]
    }
    template: {
      containers: [{
        name: 'rabbitmq'
        image: 'docker.io/library/rabbitmq:3.13-alpine'
        resources: { cpu: json('0.25'), memory: '0.5Gi' }
        env: [
          { name: 'RABBITMQ_DEFAULT_USER', value: rabbitUser }
          { name: 'RABBITMQ_DEFAULT_PASS', secretRef: 'rabbit-pass' }
        ]
      }]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

var registry = [{ server: acr.properties.loginServer, identity: identity.id }]
var appIdentity = { type: 'UserAssigned', userAssignedIdentities: { '${identity.id}': {} } }
var rabbitEnv = [
  { name: 'RabbitMq__HostName', value: 'rabbitmq' }
  { name: 'RabbitMq__UserName', value: rabbitUser }
  { name: 'RabbitMq__Password', secretRef: 'rabbit-pass' }
]
var rabbitSecret = { name: 'rabbit-pass', value: rabbitPassword }

resource docmanager 'Microsoft.App/containerApps@2024-03-01' = if (deployApps) {
  name: 'docmanager'
  location: location
  identity: appIdentity
  dependsOn: [acrPull, dbs, blobContributor]
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      registries: registry
      ingress: { external: true, targetPort: 8080, transport: 'auto', allowInsecure: false }
      secrets: [
        rabbitSecret
        { name: 'db', value: pgConn(pgHost, 'documanagementdb', pgAdminUser, pgAdminPassword) }
        { name: 'download-key', value: downloadApiKey }
        { name: 'search-key', value: searchApiKey }
      ]
    }
    template: {
      containers: [{
        name: 'docmanager'
        image: '${acr.properties.loginServer}/docmanager:${imageTag}'
        resources: { cpu: json('0.5'), memory: '1Gi' }
        env: concat(rabbitEnv, [
          { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
          { name: 'ConnectionStrings__DefaultConnection', secretRef: 'db' }
          { name: 'ServiceDownload__ApiKey', secretRef: 'download-key' }
          { name: 'SearchProxy__Enabled', value: 'true' }
          { name: 'SearchProxy__BaseUrl', value: 'http://search' }
          { name: 'SearchProxy__ApiKey', secretRef: 'search-key' }
          { name: 'BlobStorage__AccountUrl', value: storage.properties.primaryEndpoints.blob }
          { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId }
          { name: 'Cors__AllowedOrigins__0', value: 'https://${swa.properties.defaultHostname}' }
          { name: 'Cors__AllowedOrigins__1', value: 'https://${swaCustomDomain}' }
          { name: 'Database__MigrateOnStartup', value: 'true' }
        ])
      }]
      scale: { minReplicas: 1, maxReplicas: 2 }
    }
  }
}

resource orchestrator 'Microsoft.App/containerApps@2024-03-01' = if (deployApps) {
  name: 'orchestrator'
  location: location
  identity: appIdentity
  dependsOn: [acrPull, dbs]
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      registries: registry
      secrets: [
        rabbitSecret
        { name: 'db', value: pgConn(pgHost, 'orchestratordb', pgAdminUser, pgAdminPassword) }
      ]
    }
    template: {
      containers: [{
        name: 'orchestrator'
        image: '${acr.properties.loginServer}/orchestrator:${imageTag}'
        resources: { cpu: json('0.25'), memory: '0.5Gi' }
        env: concat(rabbitEnv, [
          { name: 'ConnectionStrings__Orchestrator', secretRef: 'db' }
        ])
      }]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

resource extraction 'Microsoft.App/containerApps@2024-03-01' = if (deployApps) {
  name: 'extraction'
  location: location
  identity: appIdentity
  dependsOn: [acrPull, dbs]
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      registries: registry
      ingress: { external: false, targetPort: 8080, transport: 'auto' }
      secrets: [
        rabbitSecret
        { name: 'db', value: pgConn(pgHost, 'extractiondb', pgAdminUser, pgAdminPassword) }
        { name: 'download-key', value: downloadApiKey }
        { name: 'artifact-key', value: artifactApiKey }
        { name: 'di-key', value: empty(documentIntelligenceKey) ? 'unset' : documentIntelligenceKey }
        { name: 'gemini-key', value: empty(geminiKey) ? 'unset' : geminiKey }
      ]
    }
    template: {
      containers: [{
        name: 'extraction'
        image: '${acr.properties.loginServer}/extraction:${imageTag}'
        resources: { cpu: json('0.5'), memory: '1Gi' }
        env: concat(rabbitEnv, [
          { name: 'ConnectionStrings__Extraction', secretRef: 'db' }
          { name: 'ExtractionArtifactApi__ListenUrl', value: 'http://0.0.0.0:8080' }
          { name: 'ExtractionArtifactApi__PublicBaseUrl', value: 'http://extraction' }
          { name: 'ExtractionArtifactApi__ApiKey', secretRef: 'artifact-key' }
          { name: 'DocumentContentApi__BaseUrl', value: 'http://docmanager/' }
          { name: 'DocumentContentApi__ApiKey', secretRef: 'download-key' }
          { name: 'DocumentIntelligence__Endpoint', value: documentIntelligenceEndpoint }
          { name: 'DocumentIntelligence__ApiKey', secretRef: 'di-key' }
          { name: 'Foundry__Provider', value: 'Gemini' }
          { name: 'Foundry__Gemini__ModelName', value: geminiModel }
          { name: 'Foundry__Gemini__ApiKey', secretRef: 'gemini-key' }
        ])
      }]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

resource search 'Microsoft.App/containerApps@2024-03-01' = if (deployApps) {
  name: 'search'
  location: location
  identity: appIdentity
  dependsOn: [acrPull, dbs]
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      registries: registry
      ingress: { external: false, targetPort: 8080, transport: 'auto' }
      secrets: [
        rabbitSecret
        { name: 'db', value: pgConn(pgHost, 'searchdb', pgAdminUser, pgAdminPassword) }
        { name: 'search-key', value: searchApiKey }
        { name: 'artifact-key', value: artifactApiKey }
        { name: 'emb-key', value: empty(openAiKey) ? 'unset' : openAiKey }
        { name: 'agentic-key', value: empty(agenticSearchApiKey) ? 'unset' : agenticSearchApiKey }
      ]
    }
    template: {
      containers: [{
        name: 'search'
        image: '${acr.properties.loginServer}/search:${imageTag}'
        resources: { cpu: json('0.5'), memory: '1Gi' }
        env: [
          { name: 'ConnectionStrings__Search', secretRef: 'db' }
          { name: 'Search__ListenUrl', value: 'http://+:8080' }
          { name: 'Search__ApiKey', secretRef: 'search-key' }
          { name: 'Search__SourceDocumentBaseUrl', value: 'http://docmanager/' }
          { name: 'Search__RabbitMq__HostName', value: 'rabbitmq' }
          { name: 'Search__RabbitMq__UserName', value: rabbitUser }
          { name: 'Search__RabbitMq__Password', secretRef: 'rabbit-pass' }
          { name: 'Search__ArtifactApi__BaseUrl', value: 'http://extraction' }
          { name: 'Search__ArtifactApi__ApiKey', secretRef: 'artifact-key' }
          { name: 'Search__Embeddings__Provider', value: 'OpenAI' }
          { name: 'Search__Embeddings__Endpoint', value: 'https://api.openai.com/v1' }
          { name: 'Search__Embeddings__ModelName', value: embeddingsModel }
          { name: 'Search__Embeddings__ApiKey', secretRef: 'emb-key' }
          { name: 'Search__AgenticSearch__Provider', value: 'AzureOpenAI' }
          { name: 'Search__AgenticSearch__Enabled', value: string(agenticSearchEnabled && !empty(agenticSearchApiKey)) }
          { name: 'Search__AgenticSearch__Endpoint', value: agenticSearchEndpoint }
          { name: 'Search__AgenticSearch__ApiVersion', value: agenticSearchApiVersion }
          { name: 'Search__AgenticSearch__ModelName', value: agenticSearchModel }
          { name: 'Search__AgenticSearch__ApiKey', secretRef: 'agentic-key' }
        ]
      }]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

resource swa 'Microsoft.Web/staticSites@2023-12-01' = {
  name: swaName
  location: staticWebAppLocation
  sku: { name: 'Free', tier: 'Free' }
  properties: {}
}

resource dnsZone 'Microsoft.Network/dnsZones@2018-05-01' = {
  name: dnsZoneName
  location: 'global'
}

output acrName string = acr.name
output acrLoginServer string = acr.properties.loginServer
output postgresHost string = pgHost
output staticWebAppName string = swa.name
output staticWebAppUrl string = 'https://${swa.properties.defaultHostname}'
output staticWebAppCustomDomain string = swaCustomDomain
output staticWebAppDnsZoneName string = dnsZoneName
output staticWebAppDnsNameServers array = dnsZone.properties.nameServers
output apiUrl string = deployApps ? 'https://${docmanager!.properties.configuration.ingress.fqdn}' : ''
