# Teams App Installation

## Package

Import the generated `deploy/teams/crm-analytics-pilot-teams-app.zip`. The ZIP root
contains only:

- `manifest.json`
- `color.png`
- `outline.png`

The manifest uses the GA Microsoft Teams app manifest schema 1.28. Manifest app
ID `2dd37671-1b40-467e-9143-61bf104b67da` is distinct from Bot App ID
`3e277fe2-0da0-4149-80da-2168ac44e7b9`. Do not replace either ID during import.

## Developer Portal validation

1. Open the Teams Developer Portal and choose **Apps**.
2. Import `crm-analytics-pilot-teams-app.zip` as an existing app package.
3. Run package validation and review every error and warning. Do not publish or
   distribute the app while a validation error remains.
4. Confirm the bot scopes are `personal`, `team`, and schema-defined `groupChat`; the bot ID is
   `3e277fe2-0da0-4149-80da-2168ac44e7b9`; and the privacy and terms links open.

Developer Portal automation is intentionally not used because it would require
an interactive tenant session and external publication state. The import and
portal validation are manual gates.

## Sideload and smoke test

1. In Teams, open **Apps** > **Manage your apps**.
2. Choose **Upload a custom app** and select the ZIP package.
3. Install the app and open the bot in personal chat.
4. Start the OAuth sign-in flow and complete sign-in with an authorized pilot
   account. Confirm that the connection uses `crm-analytics-teams-oauth`.
5. Send a real report request that is valid for the signed-in user's assigned
   data scope.
6. Confirm the bot acknowledges the request and returns the expected report
   progress/result without exposing tokens, credentials, connection strings, or
   another user's data.

## Live URLs

- Website: <https://crm-analytics-pilot-teams.mangodesert-3e89f5b7.swedencentral.azurecontainerapps.io/>
- Privacy: <https://crm-analytics-pilot-teams.mangodesert-3e89f5b7.swedencentral.azurecontainerapps.io/privacy>
- Terms: <https://crm-analytics-pilot-teams.mangodesert-3e89f5b7.swedencentral.azurecontainerapps.io/terms>

No secret, OAuth credential, connection string, Graph permission, RSC
permission, device permission, calling/video capability, or
`webApplicationInfo` is included in the package.
