import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import type { Page, WebSocketRoute } from '@playwright/test';

/** The blueprint's synthetic example report; its run id is the mocked run's id. */
const report = JSON.parse(readFileSync(join(__dirname, '../../../BlindSpotScanner-Blueprint/examples/sample-report.json'), 'utf8'));

export const RUN_ID: string = report.meta.runId;
export const REPO_ID = '0f0f0f0f-0000-4000-8000-000000000001';

const SEPARATOR = '\u001e';

const repository = {
  id: REPO_ID,
  owner: 'example-org',
  name: 'bookstore-api',
  fullName: 'example-org/bookstore-api',
  defaultBranch: 'main',
  htmlUrl: 'https://github.com/example-org/bookstore-api',
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
}

/**
 * Serves the REST API (`page.route`) and the `/hubs/scans` SignalR hub (`page.routeWebSocket`, JSON
 * protocol: records end with U+001E) in memory, per test.
 */
export async function mockApi(page: Page, options: { registered?: boolean; finished?: boolean } = {}): Promise<MockApi> {
  let registered = options.registered ?? false;
  let finished = options.finished ?? false;
  let hub: WebSocketRoute | null = null;
  let subscribed: () => void = () => undefined;
  const subscription = new Promise<void>((resolve) => (subscribed = resolve));

  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const method = request.method();
    const json = (body: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });

    if (path === '/api/repositories' && method === 'GET') {
      return json(registered ? [{ id: REPO_ID, fullName: repository.fullName, defaultBranch: 'main', lastRunId: null, lastScore: null, lastCoveredShare: null, lastScanAt: null, activeRunId: null }] : []);
    }
    if (path === '/api/repositories' && method === 'POST') {
      registered = true;
      return json(repository, 201);
    }
    if (path === `/api/repositories/${REPO_ID}`) {
      return json(repository);
    }
    if (path === `/api/repositories/${REPO_ID}/runs`) {
      return json([]);
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
    sendCompleted: (status) => invoke('ScanCompleted', [RUN_ID, status]),
  };
}
