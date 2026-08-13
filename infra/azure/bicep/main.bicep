targetScope = 'resourceGroup'

param location string = resourceGroup().location
param targetSubscriptionId string = subscription().subscriptionId
param namePrefix string
param apiImage string
param teamsImage string
param acrLoginServer string
param runtimeIdentityResourceId string
param runtimeIdentityClientId string
param runtimeIdentityPrincipalId string
param deployTeams bool = true
param keyVaultIntegrationEnabled bool = false
param internalApiKeyIntegrationEnabled bool = true
param apiIngressExternal bool = false
param keyVaultName string
param keyVaultResourceGroupName string = resourceGroup().name
param logAnalyticsWorkspaceResourceId string = ''
param logAnalyticsWorkspaceName string = ''
param logAnalyticsWorkspaceResourceGroupName string = resourceGroup().name
param applicationInsightsResourceId string = ''
param applicationInsightsName string = ''
param applicationInsightsResourceGroupName string = resourceGroup().name
param manageContainerAppsEnvironment bool = true
param containerAppsEnvironmentResourceId string = ''
param containerAppsEnvironmentDefaultDomain string = ''
param applicationDbSecretName string = 'crm-analytics-application-db'
param queryDwhConnectionSecretName string = 'crm-analytics-query-dwh'
param queryOltpConnectionSecretName string = 'crm-analytics-query-oltp'
param internalApiKeySecretName string = 'crm-analytics-internal-api-key'
param teamsClientSecretName string = 'crm-analytics-teams-client-secret'
param teamsClientSecretIntegrationEnabled bool = false
param copilotStudioDirectLineSecretName string = 'crm-analytics-copilot-direct-line-secret'
param copilotStudioDirectLineSecretIntegrationEnabled bool = false
param copilotStudioEnabled bool = false
param copilotStudioDirectLineBaseUri string = 'https://europe.directline.botframework.com'
param copilotStudioAgentName string = ''
param teamsNotificationsEnabled bool = true
param apiMinReplicas int = 1
param apiMaxReplicas int = 2
param teamsMinReplicas int = 1
param teamsMaxReplicas int = 1
param queryDwhEnabled bool = true
param queryDwhServer string
param queryDwhInitialCatalog string
param queryOltpEnabled bool = false
param azureAdTenantId string
param azureAdClientId string
param azureAdAudience string
param teamsTenantId string
@allowed([
  'SingleTenant'
])
param teamsAppType string = 'SingleTenant'
param teamsClientId string
param teamsOAuthConnectionName string
@description('Pilot-only opt-in. Allows network access to this Azure SQL server from Azure-hosted resources; this is not a production private-network solution. Microsoft Entra authentication and SQL authorization continue to apply.')
param enablePilotAzureSqlAzureServicesRule bool = false
@description('Existing Azure SQL logical server name. Used only when enablePilotAzureSqlAzureServicesRule is true.')
param azureSqlServerName string = ''
@allowed([
  'Direct'
  'FabricJob'
])
param analyticsProvider string = 'FabricJob'
param allowDirectAnalyticsInProtectedEnvironments bool = false
param fabricWorkspaceId string = ''
param fabricItemId string = ''
param fabricJobType string = ''
param powerBiWorkspaceId string = ''
param powerBiReportId string = ''
param powerBiSemanticModelId string = ''
param serviceBusNamespace string
param serviceBusQueueName string = 'crm-report-processing'

var apiName = '${namePrefix}-api'
var teamsName = '${namePrefix}-teams'
var generatedLogAnalyticsWorkspaceName = '${namePrefix}-logs'
var applicationInsightsResourceIdFromName = !empty(applicationInsightsName)
  ? resourceId(
      targetSubscriptionId,
      applicationInsightsResourceGroupName,
      'Microsoft.Insights/components',
      applicationInsightsName)
  : ''
var resolvedApplicationInsightsResourceId = !empty(applicationInsightsResourceId)
  ? applicationInsightsResourceId
  : applicationInsightsResourceIdFromName
var explicitLogAnalyticsWorkspaceResourceId = !empty(logAnalyticsWorkspaceResourceId)
  ? logAnalyticsWorkspaceResourceId
  : !empty(logAnalyticsWorkspaceName)
    ? resourceId(
        targetSubscriptionId,
        logAnalyticsWorkspaceResourceGroupName,
        'Microsoft.OperationalInsights/workspaces',
        logAnalyticsWorkspaceName)
    : ''
var useExistingLogAnalyticsWorkspace = !empty(explicitLogAnalyticsWorkspaceResourceId) || !empty(resolvedApplicationInsightsResourceId)
var resolvedLogAnalyticsWorkspaceResourceId = !empty(explicitLogAnalyticsWorkspaceResourceId)
  ? explicitLogAnalyticsWorkspaceResourceId
  : !empty(resolvedApplicationInsightsResourceId)
    ? string(reference(resolvedApplicationInsightsResourceId, '2020-02-02').WorkspaceResourceId)
    : logs!.outputs.resourceId
var keyVaultBaseUrl = 'https://${keyVaultName}${az.environment().suffixes.keyvaultDns}'
var apiSecrets = keyVaultIntegrationEnabled ? concat([
  { name: 'application-db', keyVaultUrl: '${keyVaultBaseUrl}/secrets/${applicationDbSecretName}' }
], internalApiKeyIntegrationEnabled ? [
  { name: 'internal-api-key', keyVaultUrl: '${keyVaultBaseUrl}/secrets/${internalApiKeySecretName}' }
] : [], queryDwhEnabled ? [
  { name: 'query-dwh', keyVaultUrl: '${keyVaultBaseUrl}/secrets/${queryDwhConnectionSecretName}' }
] : [], queryOltpEnabled ? [
  { name: 'query-oltp', keyVaultUrl: '${keyVaultBaseUrl}/secrets/${queryOltpConnectionSecretName}' }
] : []) : []
var apiSecretEnvironment = keyVaultIntegrationEnabled ? concat([
  { name: 'ConnectionStrings__CrmAnalytics', secretRef: 'application-db' }
], internalApiKeyIntegrationEnabled ? [
  { name: 'TeamsNotifications__ApiKey', secretRef: 'internal-api-key' }
] : [], queryDwhEnabled ? [
  { name: 'ConnectionStrings__QueryDwh', secretRef: 'query-dwh' }
] : [], queryOltpEnabled ? [
  { name: 'ConnectionStrings__QueryOltp', secretRef: 'query-oltp' }
] : []) : []
var apiEnvironment = concat([
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  { name: 'CrmAnalyticsAuthentication__Mode', value: 'Entra' }
  { name: 'AzureAd__TenantId', value: azureAdTenantId }
  { name: 'AzureAd__ClientId', value: azureAdClientId }
  { name: 'AzureAd__Audience', value: azureAdAudience }
  { name: 'Persistence__Provider', value: 'SqlServer' }
  { name: 'QueryExecution__Provider', value: 'SqlClient' }
  { name: 'QueryExecution__Dwh__Enabled', value: string(queryDwhEnabled) }
  { name: 'QueryExecution__Dwh__AuthenticationMode', value: 'ManagedIdentity' }
  { name: 'QueryExecution__Dwh__ManagedIdentityClientId', value: runtimeIdentityClientId }
  { name: 'QueryExecution__Oltp__Enabled', value: string(queryOltpEnabled) }
  { name: 'QueryExecution__Oltp__AuthenticationMode', value: 'ManagedIdentity' }
  { name: 'QueryExecution__Oltp__ManagedIdentityClientId', value: runtimeIdentityClientId }
  { name: 'Messaging__Provider', value: 'AzureServiceBus' }
  { name: 'Messaging__AzureServiceBus__AuthenticationMode', value: 'ManagedIdentity' }
  { name: 'Messaging__AzureServiceBus__FullyQualifiedNamespace', value: serviceBusNamespace }
  { name: 'Messaging__AzureServiceBus__QueueName', value: serviceBusQueueName }
  { name: 'Messaging__AzureServiceBus__ManagedIdentityClientId', value: runtimeIdentityClientId }
  { name: 'Analytics__Provider', value: analyticsProvider }
  { name: 'Analytics__AllowDirectInProtectedEnvironments', value: string(allowDirectAnalyticsInProtectedEnvironments) }
  { name: 'Analytics__Fabric__WorkspaceId', value: fabricWorkspaceId }
  { name: 'Analytics__Fabric__ItemId', value: fabricItemId }
  { name: 'Analytics__Fabric__JobType', value: fabricJobType }
  { name: 'Analytics__Fabric__ManagedIdentityClientId', value: runtimeIdentityClientId }
  { name: 'Reporting__Provider', value: 'PowerBi' }
  { name: 'Reporting__PowerBi__WorkspaceId', value: powerBiWorkspaceId }
  { name: 'Reporting__PowerBi__ReportId', value: powerBiReportId }
  { name: 'Reporting__PowerBi__SemanticModelId', value: powerBiSemanticModelId }
  { name: 'Reporting__PowerBi__ManagedIdentityClientId', value: runtimeIdentityClientId }
  { name: 'TeamsNotifications__Enabled', value: string(teamsNotificationsEnabled) }
  { name: 'TeamsNotifications__BaseUrl', value: 'https://${teamsRuntimeFqdn}' }
], apiSecretEnvironment)

var teamsSecrets = keyVaultIntegrationEnabled ? concat(internalApiKeyIntegrationEnabled ? [
  { name: 'internal-api-key', keyVaultUrl: '${keyVaultBaseUrl}/secrets/${internalApiKeySecretName}' }
] : [], teamsClientSecretIntegrationEnabled ? [
  { name: 'teams-client-secret', keyVaultUrl: '${keyVaultBaseUrl}/secrets/${teamsClientSecretName}' }
] : [], copilotStudioDirectLineSecretIntegrationEnabled ? [
  { name: 'copilot-direct-line-secret', keyVaultUrl: '${keyVaultBaseUrl}/secrets/${copilotStudioDirectLineSecretName}' }
] : []) : []
var teamsSecretEnvironment = keyVaultIntegrationEnabled ? concat(internalApiKeyIntegrationEnabled ? [
  { name: 'BackendApi__InternalApiKey', secretRef: 'internal-api-key' }
  { name: 'ReportNotifications__ApiKey', secretRef: 'internal-api-key' }
] : [], teamsClientSecretIntegrationEnabled ? [
  { name: 'Teams__ClientSecret', secretRef: 'teams-client-secret' }
] : [], copilotStudioDirectLineSecretIntegrationEnabled ? [
  { name: 'CopilotStudio__DirectLineSecret', secretRef: 'copilot-direct-line-secret' }
] : []) : []

module logs 'modules/log-analytics.bicep' = if (!useExistingLogAnalyticsWorkspace) {
  name: 'log-analytics'
  params: {
    name: generatedLogAnalyticsWorkspaceName
    location: location
  }
}

module environment 'modules/container-environment.bicep' = if (manageContainerAppsEnvironment) {
  name: 'container-environment'
  params: {
    name: '${namePrefix}-env'
    location: location
    logAnalyticsWorkspaceResourceId: resolvedLogAnalyticsWorkspaceResourceId
  }
}

var resolvedContainerAppsEnvironmentResourceId = manageContainerAppsEnvironment
  ? environment!.outputs.resourceId
  : containerAppsEnvironmentResourceId
var resolvedContainerAppsEnvironmentDefaultDomain = manageContainerAppsEnvironment
  ? environment!.outputs.defaultDomain
  : containerAppsEnvironmentDefaultDomain

// Internal Container Apps ingress uses the `.internal` label; external ingress
// does not. Constructing hostnames from deterministic app names and the
// environment domain avoids a cross-app generated-property dependency.
var apiRuntimeFqdn = '${apiName}${apiIngressExternal ? '' : '.internal'}.${resolvedContainerAppsEnvironmentDefaultDomain}'
var teamsRuntimeFqdn = '${teamsName}.${resolvedContainerAppsEnvironmentDefaultDomain}'

module api 'modules/container-app.bicep' = {
  name: 'api-container-app'
  params: {
    name: apiName
    location: location
    environmentId: resolvedContainerAppsEnvironmentResourceId
    image: apiImage
    registryServer: acrLoginServer
    identityResourceId: runtimeIdentityResourceId
    keyVaultIdentityResourceId: runtimeIdentityResourceId
    ingressExternal: apiIngressExternal
    minReplicas: apiMinReplicas
    maxReplicas: apiMaxReplicas
    keyVaultSecrets: apiSecrets
    environmentVariables: apiEnvironment
  }
  dependsOn: [
    apiApplicationDbSecretsUser
    apiQueryDwhSecretsUser
    apiQueryOltpSecretsUser
    apiInternalApiKeySecretsUser
  ]
}

module teams 'modules/container-app.bicep' = if (deployTeams) {
  name: 'teams-container-app'
  params: {
    name: teamsName
    location: location
    environmentId: resolvedContainerAppsEnvironmentResourceId
    image: teamsImage
    registryServer: acrLoginServer
    identityResourceId: runtimeIdentityResourceId
    keyVaultIdentityResourceId: runtimeIdentityResourceId
    ingressExternal: true
    minReplicas: teamsMinReplicas
    maxReplicas: teamsMaxReplicas
    keyVaultSecrets: teamsSecrets
    environmentVariables: concat([
      { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
      { name: 'BackendApi__BaseUrl', value: 'https://${apiRuntimeFqdn}' }
      { name: 'ReportNotifications__Enabled', value: string(teamsNotificationsEnabled) }
      { name: 'Teams__SkipAuth', value: 'false' }
      { name: 'Teams__TenantId', value: teamsTenantId }
      { name: 'Teams__AppType', value: teamsAppType }
      { name: 'Teams__ClientId', value: teamsClientId }
      { name: 'TeamsUserAuthentication__Mode', value: 'Entra' }
      { name: 'TeamsUserAuthentication__OAuthConnectionName', value: teamsOAuthConnectionName }
      { name: 'CopilotStudio__Enabled', value: string(copilotStudioEnabled) }
      { name: 'CopilotStudio__DirectLineBaseUri', value: copilotStudioDirectLineBaseUri }
      { name: 'CopilotStudio__AgentName', value: copilotStudioAgentName }
    ], teamsSecretEnvironment)
  }
  dependsOn: [
    apiInternalApiKeySecretsUser
    teamsClientSecretSecretsUser
    teamsCopilotDirectLineSecretSecretsUser
  ]
}

module apiApplicationDbSecretsUser 'modules/key-vault-secret-role-assignment.bicep' = if (keyVaultIntegrationEnabled) {
  name: 'api-application-db-secrets-user'
  scope: resourceGroup(keyVaultResourceGroupName)
  params: {
    keyVaultName: keyVaultName
    secretName: applicationDbSecretName
    principalId: runtimeIdentityPrincipalId
  }
}

module apiQueryDwhSecretsUser 'modules/key-vault-secret-role-assignment.bicep' = if (keyVaultIntegrationEnabled && queryDwhEnabled) {
  name: 'api-query-dwh-secrets-user'
  scope: resourceGroup(keyVaultResourceGroupName)
  params: {
    keyVaultName: keyVaultName
    secretName: queryDwhConnectionSecretName
    principalId: runtimeIdentityPrincipalId
  }
}

module apiQueryOltpSecretsUser 'modules/key-vault-secret-role-assignment.bicep' = if (keyVaultIntegrationEnabled && queryOltpEnabled) {
  name: 'api-query-oltp-secrets-user'
  scope: resourceGroup(keyVaultResourceGroupName)
  params: {
    keyVaultName: keyVaultName
    secretName: queryOltpConnectionSecretName
    principalId: runtimeIdentityPrincipalId
  }
}

module apiInternalApiKeySecretsUser 'modules/key-vault-secret-role-assignment.bicep' = if (keyVaultIntegrationEnabled && internalApiKeyIntegrationEnabled) {
  name: 'api-internal-api-key-secrets-user'
  scope: resourceGroup(keyVaultResourceGroupName)
  params: {
    keyVaultName: keyVaultName
    secretName: internalApiKeySecretName
    principalId: runtimeIdentityPrincipalId
  }
}

module teamsClientSecretSecretsUser 'modules/key-vault-secret-role-assignment.bicep' = if (deployTeams && keyVaultIntegrationEnabled && teamsClientSecretIntegrationEnabled) {
  name: 'teams-client-secret-secrets-user'
  scope: resourceGroup(keyVaultResourceGroupName)
  params: {
    keyVaultName: keyVaultName
    secretName: teamsClientSecretName
    principalId: runtimeIdentityPrincipalId
  }
}

module teamsCopilotDirectLineSecretSecretsUser 'modules/key-vault-secret-role-assignment.bicep' = if (deployTeams && keyVaultIntegrationEnabled && copilotStudioDirectLineSecretIntegrationEnabled) {
  name: 'teams-copilot-direct-line-secret-secrets-user'
  scope: resourceGroup(keyVaultResourceGroupName)
  params: {
    keyVaultName: keyVaultName
    secretName: copilotStudioDirectLineSecretName
    principalId: runtimeIdentityPrincipalId
  }
}

resource pilotAzureSqlServer 'Microsoft.Sql/servers@2023-08-01' existing = if (enablePilotAzureSqlAzureServicesRule) {
  name: azureSqlServerName
}

resource pilotAzureSqlAzureServicesRule 'Microsoft.Sql/servers/firewallRules@2023-08-01' = if (enablePilotAzureSqlAzureServicesRule) {
  parent: pilotAzureSqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

output containerAppsEnvironmentName string = '${namePrefix}-env'
output apiContainerAppName string = apiName
output teamsContainerAppName string = teamsName
output apiFqdn string = apiRuntimeFqdn
output teamsFqdn string = deployTeams ? teamsRuntimeFqdn : ''
output apiLatestRevisionName string = api.outputs.latestRevisionName
output teamsLatestRevisionName string = deployTeams ? teams!.outputs.latestRevisionName : ''
output logAnalyticsWorkspaceResourceId string = resolvedLogAnalyticsWorkspaceResourceId
output applicationInsightsResourceId string = resolvedApplicationInsightsResourceId
output queryDwhServer string = queryDwhServer
output queryDwhInitialCatalog string = queryDwhInitialCatalog
