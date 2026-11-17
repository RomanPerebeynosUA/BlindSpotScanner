/** The integration report (contracts/integration-report.schema.json). Every headline number is computed by the backend. */

export type CoverageStatus = 'covered' | 'weak' | 'skipped' | 'none' | 'unverified';
export type Severity = 'Critical' | 'High' | 'Medium' | 'Low';
export type Confidence = 'High' | 'Med' | 'Low';
export type IntegrationMappedTo = 'real-seam' | 'e2e-mocked-data' | 'e2e-real-backend' | 'contract' | 'other';
export type Verdict = 'strong' | 'moderate' | 'partial' | 'weak';
export type FindingKind = 'UncoveredEndpoint' | 'WeakTest' | 'FalsePositive' | 'SkippedTest' | 'UncoveredSeam' | 'Risk';

export const COVERAGE_STATUSES: readonly CoverageStatus[] = ['covered', 'weak', 'skipped', 'none', 'unverified'];

export interface StatusCounts {
  total: number;
  covered: number;
  weak: number;
  skipped: number;
  none: number;
  unverified: number;
}

export interface ReportMeta {
  schemaVersion: string;
  runId: string;
  repo: { owner: string; name: string; url: string };
  branch: string;
  commitSha: string;
  generatedAt: string;
  modelId: string;
  promptSetVersion: string;
  profileVersion: number;
  integrationMappedTo: IntegrationMappedTo;
  costUsd?: number | null;
}

export interface Inventory {
  controllerFiles: number;
  endpointsTotal: number;
  testFilesTotal: number;
  integrationTestFiles: number;
  classification?: Record<string, number>;
  readInFull?: number;
  unverifiedReasons?: string[];
}

export interface EndpointCoverage {
  method: string;
  route: string;
  status: CoverageStatus;
  verified: boolean;
  evidence: string[];
  falsePositive?: boolean;
  notes?: string;
}

export interface ControllerCoverage {
  controller: string;
  sourcePath: string;
  routePrefix?: string;
  status: CoverageStatus;
  endpoints: EndpointCoverage[];
}

export interface ScorecardRow {
  id: number;
  dimension: string;
  weight: number;
  score: number | null;
  scorable: boolean;
  confidence?: Confidence;
  justificationBullets: string[];
  evidence?: string[];
}

export interface Finding {
  id: string;
  kind: FindingKind;
  severity: Severity;
  stableKey: string;
  subject?: string;
  headline: string;
  detail?: string[];
  evidence: string[];
}

export interface TopRisk {
  rank: number;
  headline: string;
  whyItMatters: string;
  severity: Severity;
  detail: string[];
  evidence?: string[];
}

export interface ReportSummary {
  endpoints: StatusCounts;
  controllers: StatusCounts;
  qualityScore: number | null;
  weightAssessablePct: number;
  scoreLabel: string;
  integrationMappedTo: IntegrationMappedTo;
  falsePositiveCount?: number;
  skippedTestCount?: number;
  uncoveredRealSeams?: number;
  verdict: Verdict;
  bullets: { label: string; text: string }[];
  previousRunId?: string | null;
}

export interface Validation {
  passed: boolean;
  method: string;
  violations: string[];
  citationsChecked?: number;
  citationsResolved?: number;
}

/** M8 renders the delta; M7 only carries it. */
export type RunDelta = Record<string, unknown>;

export interface IntegrationReport {
  meta: ReportMeta;
  inventory: Inventory;
  endpointCoverage: ControllerCoverage[];
  scorecard: ScorecardRow[];
  findings: Finding[];
  topRisks: TopRisk[];
  summary: ReportSummary;
  delta?: RunDelta | null;
  validation: Validation;
}
