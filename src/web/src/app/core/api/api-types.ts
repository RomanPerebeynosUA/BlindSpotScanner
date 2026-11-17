import { IntegrationMappedTo, IntegrationReport } from './report';

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
}

export interface Repository {
  id: string;
  owner: string;
  name: string;
  fullName: string;
  defaultBranch: string;
  htmlUrl: string;
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

export interface ProfileDraft {
  markdown: string;
  openQuestions: string[];
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
