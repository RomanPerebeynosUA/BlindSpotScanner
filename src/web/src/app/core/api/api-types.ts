import { IntegrationMappedTo, IntegrationReport, RunDelta } from './report';

/** Shapes of the API's JSON (the C# response records, camelCase). */

export type RunStatus = 'Queued' | 'Running' | 'Succeeded' | 'Failed' | 'Cancelled';

export interface RepositorySummary {
  id: string;
  fullName: string;
  defaultBranch: string;
  lastRunId: string | null;
  lastScore: number | null;
  lastCoveredShare: number | null;
  lastScanAt: string | null;
  activeRunId: string | null;
  /** The newest run's status, whatever it is. */
  latestRunStatus: RunStatus | null;
  /** The last succeeded runs (at most 12), oldest first (D23). */
  trend: TrendPoint[];
  llmSharingAcknowledged: boolean;
}

export interface TrendPoint {
  runId: string;
  generatedAt: string;
  score: number | null;
  coveredShare: number | null;
}

export interface RunHeadline {
  runId: string;
  branch: string;
  commitSha: string;
  generatedAt: string;
  score: number | null;
}

/** GET /api/runs/compare: always old → new (D23). */
export interface Comparison {
  base: RunHeadline;
  target: RunHeadline;
  swapped: boolean;
  adHoc: boolean;
  delta: RunDelta;
}

export interface Repository {
  id: string;
  owner: string;
  name: string;
  fullName: string;
  defaultBranch: string;
  htmlUrl: string;
  /** Doc 10: scans need this accepted first (D26). */
  llmSharingAcknowledged: boolean;
}

export interface RunHistoryItem {
  runId: string;
  status: RunStatus;
  branch: string;
  commitSha: string;
  queuedAt: string | null;
  generatedAt: string | null;
  score: number | null;
  weightAssessablePct: number | null;
  endpointsTotal: number | null;
  endpointsCovered: number | null;
  endpointsWeak: number | null;
  endpointsNone: number | null;
  endpointsUnverified: number | null;
  costUsd: number;
  failureReasons: string[];
}

export interface Profile {
  version: number;
  markdown: string;
  integrationMappedTo: IntegrationMappedTo;
  testGlobs: string[];
  controllerGlobs: string[];
  minimalApiGlobs: string[];
  integrationGlobs: string[];
  integrationAttributes: string[];
  integrationBaseTypes: string[];
  notIntegrationGlobs: string[];
}

export type ProfileInput = Omit<Profile, 'version'>;

/** The typed classification rules (D18) an AI draft proposes (D29). */
export type ProfileRules = Omit<ProfileInput, 'markdown'>;

/** How many repository files one glob matches; `field` is the rule's key, e.g. `minimalApiGlobs`. */
export interface PatternMatch {
  field: string;
  pattern: string;
  files: number;
}

export interface ProfileDraft {
  markdown: string;
  openQuestions: string[];
  rules: ProfileRules;
  matches: PatternMatch[];
}

/** POST …/profile/matches: counts per glob, plus syntax errors. */
export interface PatternCheck {
  matches: PatternMatch[];
  errors: string[];
}

/** GET /api/runs/{id} for a run without a report. */
export interface RunStatusBody {
  runId: string;
  status: RunStatus;
  failureReasons: string[];
}

export interface ScanStep {
  phase: string;
  status: RunStatus;
  startedAt: string;
  completedAt: string | null;
  inputTokens: number;
  outputTokens: number;
  costUsd: number;
  readInFull: number;
  notes: string | null;
}

export type RunBody = IntegrationReport | RunStatusBody;

export const isReport = (body: RunBody): body is IntegrationReport => 'meta' in body;

/** A Jira ticket (or, from M10, a PR) created from a finding (doc 09, D24). */
export interface RemediationAction {
  id: string;
  type: 'JiraTicket' | 'PullRequest';
  stableKey: string;
  externalKey: string;
  url: string;
  status: string;
  createdAt: string;
  createdBy: string;
}

/** POST …/jira-preview: the editable draft; `existing` is a ticket already made for this stable key. */
export interface JiraDraft {
  summary: string;
  description: string;
  projectKey: string;
  issueType: string;
  priority: string;
  labels: string[];
  stableKey: string;
  existing: RemediationAction | null;
}

/** POST …/jira body; `force` is "Create anyway". */
export interface JiraCreate {
  summary: string;
  description: string;
  projectKey: string;
  issueType: string;
  priority: string;
  labels: string[];
  force: boolean;
}

/** GET /api/features (D25): what the UI may offer. */
export interface Features {
  pullRequests: boolean;
  jira: boolean;
}

export type FixFileAction = 'create' | 'modify';

/** A generated file; `original` is the content at the scanned commit, for a modify. */
export interface FixDraftFile {
  path: string;
  action: FixFileAction;
  content: string;
  original: string | null;
}

/** POST …/fix-preview: the guarded, editable fix (doc 09). */
export interface FixDraft {
  files: FixDraftFile[];
  rationale: string;
  assumptions: string[];
  riskNotes: string[];
  branch: string;
  title: string;
  body: string;
  costUsd: number;
  existing: RemediationAction | null;
  /** The commit the fix was generated from: the scanned one, or the default branch head after Regenerate (D25). */
  baseSha: string;
}

/** POST …/pull-request body; `force` is "Open anyway". */
export interface FixCreate {
  files: { path: string; action: FixFileAction; content: string }[];
  branch: string;
  title: string;
  body: string;
  force: boolean;
  baseSha: string;
}

/** GET /api/settings/connections (D26): what is configured, secrets masked as ••••last4. */
export interface ConnectionStatus {
  kind: 'github' | 'jira' | 'llm';
  configured: boolean;
  masked: string | null;
  details: Record<string, string>;
}

export interface ConnectionTestResult {
  ok: boolean;
  message: string;
}
