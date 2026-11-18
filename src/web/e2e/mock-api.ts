import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import type { Page, WebSocketRoute } from '@playwright/test';

/** The blueprint's synthetic example report; its run id is the mocked run's id. */
const report = JSON.parse(readFileSync(join(__dirname, '../../../BlindSpotScanner-Blueprint/examples/sample-report.json'), 'utf8'));

const previousReport = JSON.parse(readFileSync(join(__dirname, '../../../BlindSpotScanner-Blueprint/examples/sample-report-previous.json'), 'utf8'));

export const RUN_ID: string = report.meta.runId;
export const PREVIOUS_RUN_ID: string = previousReport.meta.runId;

/** A run-history row built from an example report's headline. */
const historyItem = (r: typeof report) => ({
  runId: r.meta.runId, status: 'Succeeded', branch: r.meta.branch, commitSha: r.meta.commitSha, queuedAt: r.meta.generatedAt, generatedAt: r.meta.generatedAt,
  score: r.summary.qualityScore, weightAssessablePct: r.summary.weightAssessablePct, endpointsTotal: r.summary.endpoints.total,
  endpointsCovered: r.summary.endpoints.covered, endpointsWeak: r.summary.endpoints.weak, endpointsNone: r.summary.endpoints.none,
  endpointsUnverified: r.summary.endpoints.unverified, costUsd: r.meta.costUsd ?? 0, failureReasons: [],
});

const headline = (r: typeof report) => ({ runId: r.meta.runId, branch: r.meta.branch, commitSha: r.meta.commitSha, generatedAt: r.meta.generatedAt, score: r.summary.qualityScore });
export const REPO_ID = '0f0f0f0f-0000-4000-8000-000000000001';

const SEPARATOR = '\u001e';

const repository = {
  id: REPO_ID,
  owner: 'example-org',
  name: 'bookstore-api',
  fullName: 'example-org/bookstore-api',
  defaultBranch: 'main',
  htmlUrl: 'https://github.com/example-org/bookstore-api',
  llmSharingAcknowledged: true,
};

const profile = {
  version: 1,
  markdown: '# Repo profile — example-org/bookstore-api',
  integrationMappedTo: 'real-seam',
  testGlobs: ['tests/**/*.cs'],
  controllerGlobs: ['src/**/Controllers/**/*Controller.cs'],
  minimalApiGlobs: ['src/**/Endpoints/**/*.cs'],
  integrationGlobs: ['tests/*.IntegrationTests/**'],
  integrationAttributes: ['Trait("Category","Integration")'],
  integrationBaseTypes: ['WebApplicationFactory'],
  notIntegrationGlobs: ['**/*Fixture.cs'],
};

export interface MockApi {
  /** From now on GET /api/runs/{id} returns the example report. */
  completeRun(): void;
  sendProgress(phase: string, pct: number, message: string): Promise<void>;
  sendCompleted(status: string): Promise<void>;
  /** The bodies POSTed to …/findings/{id}/jira, in order. */
  jiraBodies(): { summary: string; force: boolean }[];
  /** The bodies POSTed to …/findings/{id}/pull-request, in order. */
  pullRequestBodies(): { branch: string; title: string; force: boolean }[];
}

/**
 * Serves the REST API (`page.route`) and the `/hubs/scans` SignalR hub (`page.routeWebSocket`, JSON
 * protocol: records end with U+001E) in memory, per test.
 */
export async function mockApi(page: Page, options: { registered?: boolean; finished?: boolean; notComparable?: boolean; ticketed?: boolean; readOnly?: boolean; filesChanged?: boolean; role?: 'Viewer' | 'Member' | 'Admin' | null; localMode?: boolean } = {},
): Promise<MockApi> {
  let registered = options.registered ?? false;
  let finished = options.finished ?? false;
  let hub: WebSocketRoute | null = null;
  const jiraBodies: { summary: string; force: boolean }[] = [];
  const actions: unknown[] = [];
  const pullRequestBodies: { branch: string; title: string; force: boolean }[] = [];
  let subscribed: () => void = () => undefined;
  const subscription = new Promise<void>((resolve) => (subscribed = resolve));

  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const method = request.method();
    const json = (body: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });

    if (path === '/api/repositories' && method === 'GET') {
      const trend = finished
        ? [previousReport, report].map((r) => ({ runId: r.meta.runId, generatedAt: r.meta.generatedAt, score: r.summary.qualityScore, coveredShare: r.summary.endpoints.covered / r.summary.endpoints.total }))
        : [];
      return json(registered ? [{ id: REPO_ID, fullName: repository.fullName, defaultBranch: 'main', lastRunId: null, lastScore: null, lastCoveredShare: null, lastScanAt: null, activeRunId: null, latestRunStatus: null, trend }] : []);
    }
    if (path === '/api/repositories' && method === 'POST') {
      registered = true;
      return json(repository, 201);
    }
    if (path === `/api/repositories/${REPO_ID}`) {
      return json(repository);
    }
    if (path === `/api/repositories/${REPO_ID}/runs`) {
      return json(finished ? [historyItem(report), historyItem(previousReport)] : []);
    }
    if (path === '/api/runs/compare') {
      const delta = options.notComparable ? { ...report.delta, comparable: false, comparability: ['integrationMappedTo changed'] } : report.delta;
      return json({ base: headline(previousReport), target: headline(report), swapped: false, adHoc: false, delta });
    }
    if (path === `/api/runs/${RUN_ID}/repository` || path === `/api/runs/${PREVIOUS_RUN_ID}/repository`) {
      return json(repository);
    }
    if (path === `/api/runs/${PREVIOUS_RUN_ID}`) {
      return json(previousReport);
    }
    if (path === `/api/repositories/${REPO_ID}/profile`) {
      return json(profile);
    }
    if (path === `/api/repositories/${REPO_ID}/profile/versions`) {
      return json([1]);
    }
    if (path === `/api/repositories/${REPO_ID}/scans`) {
      return json({ runId: RUN_ID }, 202);
    }
    if (path === `/api/runs/${RUN_ID}`) {
      return json(finished ? report : { runId: RUN_ID, status: 'Running', failureReasons: [] });
    }
    if (path === `/api/runs/${RUN_ID}/steps`) {
      return json([]);
    }
    const jira = /^\/api\/runs\/[^/]+\/findings\/([^/]+)\/(jira-preview|jira)$/.exec(path);
    if (jira && method === 'POST') {
      const finding = (report.findings as { id: string; stableKey: string; headline: string; severity: string }[]).find((f) => f.id === jira[1])!;
      const ticket = (key: string) => ({
        id: key, type: 'JiraTicket', stableKey: finding.stableKey, externalKey: key,
        url: `https://example.atlassian.net/browse/${key}`, status: 'To Do', createdAt: '2026-11-17T10:00:00Z',
      });
      if (jira[2] === 'jira-preview') {
        return json({
          summary: `[Integration coverage] ${finding.headline}`, description: `## Problem\n${finding.headline}`, projectKey: 'BSS', issueType: 'Task',
          priority: finding.severity, labels: ['integration-coverage'], stableKey: finding.stableKey, existing: options.ticketed ? ticket('BSS-1') : null,
        });
      }
      jiraBodies.push(request.postDataJSON() as { summary: string; force: boolean });
      const created = ticket(`BSS-${jiraBodies.length}`);
      actions.unshift(created);
      return json(created, 201);
    }
    if (path === '/api/me') {
      const role = options.role === undefined ? 'Admin' : options.role;
      return role
        ? json(options.localMode ? { name: 'local-admin', roles: [role], localMode: true } : { name: 'Dev Example', roles: [role], localMode: false })
        : json({ title: 'Unauthorized' }, 401);
    }
    if (path === '/api/settings/connections' && method === 'GET') {
      return json([
        { kind: 'github', configured: true, masked: '••••abcd', details: { apiBaseUrl: 'https://api.github.com' } },
        { kind: 'jira', configured: true, masked: '••••oken', details: { baseUrl: 'https://example.atlassian.net', email: 'dev@example.com', projectKey: 'BSS', issueType: 'Task' } },
        { kind: 'llm', configured: true, masked: '••••wxyz', details: { model: 'claude-sonnet-5-5', maxCostUsdPerScan: '5', promptSetVersion: '1.0.0' } },
      ]);
    }
    if (path === `/api/repositories/${REPO_ID}/llm-sharing`) {
      return route.fulfill({ status: 204 });
    }
    if (path === '/api/features') {
      return json({ pullRequests: !options.readOnly, jira: true });
    }
    const fix = /^\/api\/runs\/[^/]+\/findings\/([^/]+)\/(fix-preview|pull-request)$/.exec(path);
    if (fix && method === 'POST') {
      const finding = (report.findings as { id: string; stableKey: string }[]).find((f) => f.id === fix[1])!;
      const slug = finding.stableKey.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 60).replace(/-+$/, '');
      if (fix[2] === 'fix-preview') {
        return json({
          files: [{ path: 'tests/Bookstore.IntegrationTests/BooksApiTests.cs', action: 'modify', content: 'class BooksApiTests\n{\n    // asserts the title\n}', original: 'class BooksApiTests\n{\n}' }],
          rationale: 'Asserts the returned book, not only the status.',
          assumptions: [],
          riskNotes: [],
          branch: `coverage/fix-${slug}-${String(report.meta.commitSha).slice(0, 7)}`,
          title: 'test: assert the returned book',
          body: 'Strengthens the test.',
          costUsd: 0.04,
          existing: null,
          baseSha: String(report.meta.commitSha),
        });
      }
      if (options.filesChanged) {
        return json({ title: 'Files changed since the scan', detail: 'Regenerate the fix against the current code.', paths: ['tests/Bookstore.IntegrationTests/BooksApiTests.cs'] }, 409);
      }
      pullRequestBodies.push(request.postDataJSON() as { branch: string; title: string; force: boolean });
      const created = { id: 'pr7', type: 'PullRequest', stableKey: finding.stableKey, externalKey: '#7', url: 'https://github.com/example-org/bookstore-api/pull/7', status: 'Open', createdAt: '2026-11-17T10:00:00Z' };
      actions.unshift(created);
      return json(created, 201);
    }
    if (path === `/api/repositories/${REPO_ID}/actions`) {
      return json(actions);
    }
    if (path === `/api/runs/${RUN_ID}/cancel`) {
      return route.fulfill({ status: 202 });
    }
    return json({ title: `Not mocked: ${method} ${path}` }, 404);
  });

  await page.routeWebSocket(/\/hubs\/scans/, (ws) => {
    hub = ws;
    ws.onMessage((data) => {
      for (const record of String(data).split(SEPARATOR).filter((r) => r.length > 0)) {
        const message = JSON.parse(record) as { protocol?: string; type?: number; target?: string; invocationId?: string };
        if (message.protocol) {
          ws.send('{}' + SEPARATOR);
        } else if (message.type === 1 && message.invocationId) {
          ws.send(JSON.stringify({ type: 3, invocationId: message.invocationId, result: null }) + SEPARATOR);
          if (message.target === 'Subscribe') {
            subscribed();
          }
        }
      }
    });
  });

  const invoke = async (target: string, args: unknown[]) => {
    await subscription;
    hub?.send(JSON.stringify({ type: 1, target, arguments: args }) + SEPARATOR);
  };

  return {
    completeRun: () => (finished = true),
    sendProgress: (phase, pct, message) => invoke('ScanProgress', [RUN_ID, phase, pct, message]),
    jiraBodies: () => jiraBodies,
    pullRequestBodies: () => pullRequestBodies,
    sendCompleted: (status) => invoke('ScanCompleted', [RUN_ID, status]),
  };
}
