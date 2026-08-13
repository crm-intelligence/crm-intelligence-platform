# Container Apps infrastructure

`main.bicep` prepares up to two Container Apps in one managed environment. The API
ingress is controlled by `apiIngressExternal` and has at least one replica because
it owns outbox and Service Bus background processing. Teams has public HTTPS
ingress for `/api/messages` and uses the API's resolved HTTPS FQDN. Set
`deployTeams=false` when its required
application secrets are unavailable. Both apps keep the supplied user-assigned
runtime identity for ACR pull, Key Vault references, and Azure service
authentication. Each app also receives a system-assigned identity for future
app-specific use.

## Resource ownership

The template creates:

- one Container Apps managed environment when `manageContainerAppsEnvironment=true`;
- the API Container App with configurable ingress and the external Teams Container App;
- a Log Analytics workspace only when no existing workspace or workspace-based
  Application Insights component is selected; and
- least-privilege secret-scope Key Vault role assignments when
  `keyVaultIntegrationEnabled=true`.

The template expects these resources to exist:

- ACR (`acrLoginServer`);
- runtime UAMI (`runtimeIdentityResourceId` and `runtimeIdentityClientId`);
- Key Vault and named secrets;
- Azure SQL application/query databases represented by Key Vault connection
  string references;
- Service Bus namespace and queue;
- Entra registrations, Teams OAuth connection, Power BI/Fabric identifiers; and
- optionally an existing Log Analytics workspace and Application Insights
  component.

It does not create ACR, a runtime UAMI, Azure SQL, Service Bus, Azure Bot, Key
Vault/secret values, Application Insights, Fabric capacity/workspaces/items,
Power BI artifacts, Entra registrations, or the legacy App Service.

## Existing observability resources

Set either `logAnalyticsWorkspaceResourceId` or the workspace
name/resource-group pair. When selected, `${namePrefix}-logs` is not created and
the Container Apps Environment is connected to the existing workspace. The
workspace customer ID and shared key are resolved at deployment runtime. The
shared key is not a Bicep parameter or output and must never be logged.

An existing workspace-based Application Insights component can be supplied by
resource ID or name/resource group. When an explicit Log Analytics workspace is
not supplied, its linked workspace is used for the Container Apps Environment.
The target `CrmAnalytics.Api` and `CrmAnalytics.Teams` projects do not currently
register the Application Insights SDK, so the existing
`ApplicationInsightsConnectionString` Key Vault secret is intentionally not
mapped to an environment variable. Adding that variable alone would not enable
telemetry. Application telemetry requires a separate application-code change;
the legacy Web App integration is not copied into the new services.

## Identity and provider model

Key Vault references are versionless and use the supplied runtime UAMI.
Role assignments grant `Key Vault Secrets User` only at the required secret
scope. This avoids the first-deployment dependency cycle in which a new system
identity cannot resolve a secret until after the app resource exists.

`teamsClientSecretIntegrationEnabled=false` keeps the Teams client-secret Key
Vault reference, `Teams__ClientSecret` environment mapping, and its role
assignment absent until the Teams/Bot registration and secret are ready. The
shared internal API key can be gated separately with
`internalApiKeyIntegrationEnabled`. Copilot Studio's Direct Line secret is
independently gated by `copilotStudioDirectLineSecretIntegrationEnabled` and is
mapped only to the Teams host. `queryDwhServer` and
`queryDwhInitialCatalog` are non-secret deployment metadata; the application
continues to consume the DWH connection only through
`ConnectionStrings__QueryDwh`.

`analyticsProvider=Direct` disables FabricJob selection. Because Production
normally rejects Direct, the pilot must explicitly set
`allowDirectAnalyticsInProtectedEnvironments=true`. `queryOltpEnabled=false`
keeps OLTP disabled.

## Local validation

Run these commands from the repository root. They compile only local files and
do not contact Azure Resource Manager for a deployment validation:

```powershell
az bicep build --file infra/azure/bicep/main.bicep
az bicep lint --file infra/azure/bicep/main.bicep

# Bicep requires the final filename extension to be .bicepparam and resolves
# using paths relative to that file. Keep the temporary copy beside main.bicep.
$pilotParamsFile = 'infra/azure/bicep/main.pilot.local.bicepparam'
Copy-Item infra/azure/bicep/main.bicepparam.pilot.example $pilotParamsFile
try {
  az bicep build-params --file $pilotParamsFile --stdout
}
finally {
  Remove-Item -LiteralPath $pilotParamsFile
}
```

Azure deployment validation and what-if belong to the separately authorized
pre-deployment gate. Never add secret values or credentials to Bicep parameter
files. See `../../../docs/deployment/KEY_VAULT_CONFIGURATION.md` and
`../../../docs/deployment/AZURE_PILOT_DEPLOYMENT_PLAN.md`.
