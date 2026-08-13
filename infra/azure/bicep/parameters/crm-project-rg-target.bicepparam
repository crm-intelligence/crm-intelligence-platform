using '../main.bicep'

param location = 'swedencentral'
param targetSubscriptionId = '971bb0d9-7f3a-4872-9bc4-b602f3b682a8'
param namePrefix = 'crm-analytics-pilot'

param apiImage = 'crmprojectacr634c.azurecr.io/crm-analytics-api@sha256:6718bd34543c1154198545affa9abe9c0624938ad84bc730ac2edb875faf9233'
param teamsImage = 'crmprojectacr634c.azurecr.io/crm-analytics-teams@sha256:70d144a49a3b2235e87f4c81bc1c884755957c86484d1e8635d418c6cc2fe358'
param acrLoginServer = 'crmprojectacr634c.azurecr.io'

param runtimeIdentityResourceId = '/subscriptions/971bb0d9-7f3a-4872-9bc4-b602f3b682a8/resourceGroups/crm-project-rg-target/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-crm-analytics-runtime'
param runtimeIdentityClientId = '4fad5a78-d908-43d8-8efa-844ce4452da0'
param runtimeIdentityPrincipalId = '8046bb79-eace-4f0b-a742-d636e074ecc0'

// Phase 1 deploys and validates the API without weakening secret handling.
// Set these four flags to true only after the two named application secrets
// documented in docs/deployment/AZURE_CONTAINER_APPS.md are populated.
param deployTeams = true
param internalApiKeyIntegrationEnabled = true
param teamsClientSecretIntegrationEnabled = true
param teamsNotificationsEnabled = true
param apiIngressExternal = true

// Authoritative published Power Platform agent. Enable only after the existing
// Direct Line secret is supplied to Key Vault by an approved operator.
param copilotStudioEnabled = true
param copilotStudioDirectLineSecretIntegrationEnabled = true
param copilotStudioDirectLineSecretName = 'crm-analytics-copilot-direct-line-secret'
param copilotStudioDirectLineBaseUri = 'https://europe.directline.botframework.com'
param copilotStudioAgentName = 'CRM Semantic Planner Service2'

param keyVaultIntegrationEnabled = true
param keyVaultName = 'crmprojectkv634c'
param keyVaultResourceGroupName = 'crm-project-rg-target'

param logAnalyticsWorkspaceName = 'crm-analytics-pilot-logs'
param logAnalyticsWorkspaceResourceGroupName = 'crm-project-rg-target'
param applicationInsightsName = ''
param manageContainerAppsEnvironment = false
param containerAppsEnvironmentResourceId = '/subscriptions/971bb0d9-7f3a-4872-9bc4-b602f3b682a8/resourceGroups/crm-project-rg-target/providers/Microsoft.App/managedEnvironments/crm-analytics-pilot-env'
param containerAppsEnvironmentDefaultDomain = 'jollyplant-0f656895.swedencentral.azurecontainerapps.io'

param azureAdTenantId = '2e010224-86ea-4b34-93ea-f9833137c80e'
param azureAdClientId = '809393e5-ff46-4422-a4b3-084ed1b91d98'
param azureAdAudience = 'api://809393e5-ff46-4422-a4b3-084ed1b91d98'
param teamsTenantId = '2e010224-86ea-4b34-93ea-f9833137c80e'
param teamsAppType = 'SingleTenant'
param teamsClientId = '3e277fe2-0da0-4149-80da-2168ac44e7b9'
param teamsOAuthConnectionName = 'crm-analytics-teams-oauth'

param enablePilotAzureSqlAzureServicesRule = false
param azureSqlServerName = 'crmprojectsql634c'

param analyticsProvider = 'Direct'
param allowDirectAnalyticsInProtectedEnvironments = true
param queryDwhEnabled = true
param queryDwhServer = 'eqbaclxkqy2exe7k7gbtcn6iby-5es6c723oane3lbx5mhcfaa6q4.datawarehouse.fabric.microsoft.com'
param queryDwhInitialCatalog = 'wh_crm_analytics'
param queryOltpEnabled = true

param fabricWorkspaceId = ''
param fabricItemId = ''
param fabricJobType = ''
param powerBiWorkspaceId = '7fe125e9-705b-4d1a-ac37-eb0e22801e87'
param powerBiReportId = '36b69ded-e421-412b-b187-70caa70d4292'
param powerBiSemanticModelId = 'bdc46a8f-03cb-4bd8-a9c2-2dde70dd5432'

param serviceBusNamespace = 'crmprojectsb634c.servicebus.windows.net'
param serviceBusQueueName = 'crm-report-processing'

param apiMinReplicas = 1
param apiMaxReplicas = 2
param teamsMinReplicas = 1
param teamsMaxReplicas = 1
