/** The integration report (contracts/integration-report.schema.json). Every headline number is computed by the backend. */

export type CoverageStatus = 'covered' | 'weak' | 'skipped' | 'none' | 'unverified';
export type Severity = 'Critical' | 'High' | 'Medium' | 'Low';
export type Confidence = 'High' | 'Med' | 'Low';
export type IntegrationMappedTo = 'real-seam' | 'e2e-mocked-data' | 'e2e-real-backend' | 'contract' | 'other';
export type Verdict = 'strong' | 'moderate' | 'partial' | 'weak';
export type FindingKind =
  | 'UncoveredEndpoint' | 'WeakTest' | 'FalsePositive' | 'SkippedTest' | 'UncoveredSeam' | 'Risk'
  | 'NoAssertion' | 'UncheckedFields' | 'ErrorWithoutStateCheck';

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

/** One integration test's assertion depth, computed by the backend (D27). */
export interface TestAssertion {
  test: string;
  /** 0–5, or null when the level could not be decided. */
  level: number | null;
  levelSource: 'code' | 'llm' | 'unknown';
  readBack?: 'api' | 'db';
  errorPath: boolean;
  sentFields: string[];
  checkedFields: string[];
  uncheckedFields: string[];
  fieldsResolved: boolean;
}

export interface AssertionLevels {
  l0: number;
  l1: number;
  l2: number;
  l3: number;
  l4: number;
  l5: number;
  unknown: number;
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
  /** Integration tests per assertion level (D27); absent before report schema 1.1. */
  assertionLevels?: AssertionLevels;
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

export type DeltaDirection = 'improved' | 'regressed' | 'unchanged';

export interface DeltaMetric {
  name: string;
  prev: number | null;
  curr: number | null;
  change: number | null;
  direction: DeltaDirection;
}

/** Computed by the backend's RunComparer (doc 08); the UI only renders it. */
export interface RunDelta {
  baseRunId: string;
  baseCommitSha: string;
  baseGeneratedAt: string;
  comparable: boolean;
  /** False when the quality score and dimension 5 were measured differently (D27); absent in older deltas. */
  scoreComparable?: boolean;
  comparability: string[];
  metrics: DeltaMetric[];
  endpoints: { newlyCovered: string[]; regressed: string[]; added: string[]; removed: string[]; confidenceChanged?: string[] };
  scorecard: { dimension: string; prev: number | null; curr: number | null; change: number | null }[];
  findings: { fixed: string[]; new: string[] };
  risks: { dropped: string[]; new: string[] };
}

export interface IntegrationReport {
  meta: ReportMeta;
  inventory: Inventory;
  endpointCoverage: ControllerCoverage[];
  scorecard: ScorecardRow[];
  findings: Finding[];
  topRisks: TopRisk[];
  /** Per-test assertion depth (D27); absent before report schema 1.1. */
  testAssertions?: TestAssertion[];
  summary: ReportSummary;
  delta?: RunDelta | null;
  validation: Validation;
}
