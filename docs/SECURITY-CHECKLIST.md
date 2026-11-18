# Security checklist (doc 10)

Every rule in `BlindSpotScanner-Blueprint/docs/10-security-config-ops.md`, with the tests that prove
it (M11 AC: "verified by tests where possible"). Run them with `dotnet test`, `npx ng test --watch=false`
and `npx playwright test` (in `src/web`). "Manual" items say why a test can't prove them.

## Authentication and authorization

| Rule | Proven by |
|---|---|
| OIDC login with a configurable authority and client ID; a single local admin in development | `AuthApiTests.Login_ExternalReturnUrl_RedirectsHome` (Local mode). OIDC against a real provider is **manual** (no identity provider in CI); its role mapping is unit-tested: `RoleClaimsTests.Roles_UnknownClaimValue_FallsBackToDefault`, `Roles_ViewerAndAdmin_EffectiveIsAdmin` |
| No session → no access (API and live-progress hub) | With the real cookie + OIDC handlers against a fake provider: `OidcAuthApiTests.Me_NoSession_Returns401NotARedirect`, `Hub_NoSession_Returns401NotARedirect`. With the test scheme: `AuthApiTests.Me_Anonymous_Returns401`, `Hub_Anonymous_Returns401`; `/health` stays open: `Health_Anonymous_Returns200` |
| Viewer reads reports | `AuthApiTests.GetRepositories_Viewer_Returns200`, `ExportApiTests.Export_Json_ReturnsStoredReportAsAttachment` (as Viewer) |
| Member also scans and creates Jira tickets and PRs | `AuthApiTests.StartScan_Viewer_Returns403`, `StartScan_Member_Returns202`, `RepositoriesApiTests.AcknowledgeLlmSharing_Viewer_Returns403` |
| Admin also manages connections and settings | `SettingsApiTests.Connections_Member_Returns403`, `Connections_Admin_ListsMaskedValuesOnly` |
| The UI hides what a role can't do | `jira-action.spec` / `fix-action.spec` "Viewer sees no …", `repo-overview.spec` "Viewer sees no Scan now button", e2e `auth.spec.ts` `viewer_seesReportWithoutActionButtons`, `admin_opensSettings`, `signedOut_showsSignIn` |
| Sign-in only returns to local pages (no open redirect) | `AuthApiTests.Login_ExternalReturnUrl_RedirectsHome`, `Login_TabSmuggledReturnUrl_RedirectsHome`; OIDC uses the public callback URL: `OidcAuthApiTests.Login_RedirectsToProviderWithPublicCallbackUrl` |
| Another site can't trigger actions with the session cookie (CSRF) | `AuthApiTests.StartScan_CrossSiteBrowserRequest_Returns403`, `StartScan_SameOriginBrowserRequest_Returns202` |
| Sign-out ends the session (and the provider's, when it has an end-session endpoint) | `OidcAuthApiTests.Logout_ProviderWithEndSession_RedirectsToIt`, `OidcNoEndSessionAuthApiTests.Logout_ProviderWithoutEndSession_SignsOutHereAndGoesHome` |
| Local mode (everyone is Admin) is not exposed by accident | `RoleClaimsTests.LocalModeWarning_OutsideDevelopment_WarnsThatEveryoneIsAdmin`, `LocalModeWarning_DevelopmentOrOidc_IsNull`; compose binds its ports to 127.0.0.1 (**manual**: a config line) |
| Every remediation action records `CreatedBy` (a unique id: email, else subject) | `JiraTicketsTests.CreateAsync_New_RecordsCreatedBy`, `FixPullRequestsTests.CreateAsync_New_RecordsCreatedBy`, `AuthApiTests.CreateJira_AsMember_RecordsUserName`, `RoleClaimsTests.CreatedBy_PrefersEmailOverDisplayName` |

## Secrets

| Rule | Proven by |
|---|---|
| GitHub, Jira and Claude credentials come from env vars (D24, D26: no secrets in the database) | `ConfigurationHygieneTests.CommittedAppSettings_HoldNoSecretValues` (nothing in committed config). "Vault" storage is out of v1 (D26) |
| Secrets are never returned by the API (masked as `••••last4`) | `SecretMaskTests.Mask_LongSecret_ShowsLastFour`, `Mask_ShortSecret_RevealsNothing`, `Mask_EightCharacterSecret_RevealsNothing`, `Mask_Empty_ReturnsNull`, `SettingsApiTests.Connections_Admin_ListsMaskedValuesOnly`, `ConnectionTesterTests.TestJira_Unauthorized_ReturnsNotOkWithoutToken` |
| Secrets are never logged (destructuring policy redacts known keys) | `SecretRedactionTests.Log_ObjectWithApiToken_IsRedacted`, `Log_ExceptionMessageWithConnectionString_IsRedacted`, `Log_GitHubTokenInMessage_IsRedacted`, `Log_OrdinaryProperty_IsKept`; the hosts' own setup: `LoggingSetup_RealConfiguration_RedactsAndHonoursLevelOverrides` (configuration can set levels, never sinks) |
| Error messages never carry tokens | `GitHubClientTests.GetRepository_NotFound_ThrowsNotFoundWithoutToken`, `GitHubPullRequestsTests.CreateBlobAsync_Unauthorized_ThrowsWithoutToken`, `JiraClientTests.CreateIssueAsync_Unauthorized_ThrowsWithoutToken`, `CreateIssueAsync_Unreachable_ThrowsJiraExceptionWithoutToken` |
| Never commit `appsettings.*.json` with real values; ship `appsettings.Example.json` and `.env.example` | `ConfigurationHygieneTests.CommittedAppSettings_HoldNoSecretValues` (covers `src/BlindSpotScanner.Api/appsettings.Example.json`), `EnvExample_HoldsNoSecretValues` |

## Sending repository code to the LLM

| Rule | Proven by |
|---|---|
| `SecretScrubber` runs before every LLM call (AWS, GitHub, JWT, private keys, connection strings, `password=`) | `SecretScrubberTests` (`Scrub_KnownSecret_IsRedacted`, `Scrub_KeyValueSecret_KeepsKeyRedactsValue`, `Scrub_ConnectionString_KeepsKeyRedactsValue`, `Scrub_OrdinaryCode_IsUnchanged`), `ClaudeLlmClientTests.Complete_SecretInInput_IsScrubbedFromRequest`, `Complete_QuotedSecretsSerializedLikeThePhases_AreScrubbed` |
| Denylisted paths are never sent: `.env*`, `*.pem`, `*.pfx`, `appsettings.*.json`, `secrets.*` | `LlmContentPolicyTests.IsDenied_SensitivePath_IsTrue`, `IsDenied_OrdinaryPath_IsFalse`. The blueprint's "unless the profile allows it" exception is not built: these paths are always denied |
| A fix PR never commits a secret, nor a `«redacted»` marker in its place | `FixGuardTests.Check_Secret_Rejected`, `Check_RedactedValue_Rejected`, `CheckMessage_SecretInBody_Rejected` |
| "LLM data sharing acknowledged" before the first scan | `RepositoriesApiTests.StartScan_NotAcknowledged_Returns409`, `AcknowledgeLlmSharing_ShowsOnTheRepository`, `AcknowledgeLlmSharing_RecordsUser`, `llm-sharing-dialog.spec`, `repo-overview.spec` "unacknowledged Scan now asks for consent and scans after it", "declined consent starts no scan"; AI profile drafts too: `ProfileDrafterTests.DraftAsync_NotAcknowledged_RefusesWithoutCallingTheLlm`, `ProfilesApiTests.Post_Draft_NotAcknowledged_Returns409WithoutCallingClaude` |

## Outward-facing actions (principle 8)

| Rule | Proven by |
|---|---|
| Jira tickets and PRs only from an explicit action after a preview; the server re-checks what it is sent | `FixPullRequestsTests.CreateAsync_EditedFileOutsideTestProject_Returns422WithoutCallingGitHub`, `FixApiTests.PullRequest_ProductionEdit_Returns422`, `JiraTicketsTests.CreateAsync_EditedDraftWithoutLabel_StillSendsLabelAndFooter` |
| Read-only mode hides PR creation | `fix-action.spec` "read-only hides the Fix with PR button", e2e `report_readOnly_hidesFixWithPr`, `ReadOnlyFixApiTests.Preview_ReadOnly_Returns403` |

## Report output

| Rule | Proven by |
|---|---|
| The printable export can't run injected markup from LLM-authored text | `ReportHtmlRendererTests.Render_FindingWithMarkup_EncodesItAndHasNoScript`; served with `nosniff` and a no-script CSP: `ExportApiTests.Export_Html_ReturnsTextHtmlAttachment` |

## Observability (doc 10)

| Rule | Proven by |
|---|---|
| JSON logs with `runId` and `phase` | `ScanOrchestratorTests.RunAsync_LogsWithRunIdAndPhaseScopes` |
| One span per phase, child spans per GitHub and LLM call | `ScanOrchestratorTests.RunAsync_EmitsRunAndPhaseSpans`, `RunAsync_CancelledInsideAPhase_EndsThatPhaseSpan`. HttpClient instrumentation adds a span per GitHub, Jira and Claude call; they nest under the run span, not the phase (a recorded ruling) |
| Metrics: duration, tokens, cost, failure reasons (a fixed category), GitHub rate-limit remaining | `ScanOrchestratorTests.RunAsync_RecordsTokensAndCost`, `GitHubRateLimitHandlerTests.Send_RecordsRateLimitRemaining`, `ScanTelemetryTests.FailureCategory_KnownReasons_MapToFixedTags`. Exporting to a collector is **manual** (set `OTEL_EXPORTER_OTLP_ENDPOINT`) |
