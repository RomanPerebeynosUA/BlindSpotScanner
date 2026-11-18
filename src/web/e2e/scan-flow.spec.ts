import { expect, test } from '@playwright/test';
import { mockApi, REPO_ID, RUN_ID } from './mock-api';

/** M7 acceptance (doc 11): add repo → scan → live progress → report with filters and drill-down. */

test('addRepo_thenScan_opensLivePage', async ({ page }) => {
  // Arrange
  await mockApi(page);
  await page.goto('/');

  // Act
  await page.getByRole('button', { name: 'Add repository' }).click();
  await page.getByLabel('GitHub URL').fill('https://github.com/example-org/bookstore-api');
  await page.getByRole('button', { name: 'Add repository' }).last().click();
  await expect(page).toHaveURL(`/repos/${REPO_ID}/profile`);
  await page.getByRole('link', { name: 'Runs' }).click();
  await page.getByRole('button', { name: 'Scan now' }).click();

  // Assert
  await expect(page).toHaveURL(`/runs/${RUN_ID}/live`);
  await expect(page.getByRole('heading', { name: 'Scan in progress' })).toBeVisible();
});

test('liveProgress_thenCompletion_showsReport', async ({ page }) => {
  // Arrange
  const api = await mockApi(page, { registered: true });
  await page.goto(`/runs/${RUN_ID}/live`);

  // Act
  await api.sendProgress('P1', 0, 'P1 started');
  await api.sendProgress('P1', 12, 'P1 done');
  await api.sendProgress('P2', 12, 'P2 started');

  // Assert
  await expect(page.locator('.step[data-phase="P1"]')).toHaveAttribute('data-state', 'done');
  await expect(page.locator('.step[data-phase="P2"]')).toHaveAttribute('data-state', 'running');
  await expect(page.locator('.log')).toContainText('P2 started');

  // Act
  api.completeRun();
  await api.sendCompleted('Succeeded');

  // Assert
  await expect(page).toHaveURL(`/runs/${RUN_ID}`);
  await expect(page.getByRole('heading', { name: 'example-org/bookstore-api' })).toBeVisible();
  await expect(page.locator('.kpi').filter({ hasText: 'Quality score' }).locator('.val')).toHaveText(/4\.9\s*\/10/);
});

test('report_filterWeak_showsOnlyWeakEndpoint', async ({ page }) => {
  // Arrange
  await mockApi(page, { registered: true, finished: true });
  await page.goto(`/runs/${RUN_ID}`);

  // Act
  await page.locator('.filters button[data-status="weak"]').click();

  // Assert
  await expect(page.locator('.coverage tr.endpoint')).toHaveCount(1);
  await expect(page.locator('.coverage tr.endpoint')).toHaveAttribute('data-key', 'GET api/books/{id}');
});

test('report_search_findsCancelRoute', async ({ page }) => {
  // Arrange
  await mockApi(page, { registered: true, finished: true });
  await page.goto(`/runs/${RUN_ID}`);

  // Act
  await page.getByLabel('Search').fill('cancel');

  // Assert
  const rows = page.locator('.coverage tr.endpoint');
  await expect(rows).toHaveCount(1);
  await expect(rows).toHaveAttribute('data-key', 'POST api/orders/{id}/cancel');
  await expect(rows).toContainText('❌ None');
});

test('report_drillDown_showsEvidenceAtPinnedSha', async ({ page }) => {
  // Arrange
  await mockApi(page, { registered: true, finished: true });
  await page.goto(`/runs/${RUN_ID}`);

  // Act
  await page.locator('.coverage tr.endpoint[data-key="GET api/books"] button.expand').click();

  // Assert
  await expect(page.locator('.coverage tr.detail a.ev')).toHaveAttribute(
    'href',
    'https://github.com/example-org/bookstore-api/blob/a1b2c3d4e5f60718293a4b5c6d7e8f9012345678/tests/Bookstore.IntegrationTests/BooksApiTests.cs',
  );
});

test.describe('at 360px', () => {
  test.use({ viewport: { width: 360, height: 800 } });

  test('report_at360px_hasNoHorizontalScroll', async ({ page }) => {
    // Arrange
    await mockApi(page, { registered: true, finished: true });

    // Act
    await page.goto(`/runs/${RUN_ID}`);
    await expect(page.getByRole('heading', { name: 'example-org/bookstore-api' })).toBeVisible();

    // Assert
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(360);
  });

  test('repositories_at360px_hasNoHorizontalScroll', async ({ page }) => {
    // Arrange
    await mockApi(page, { registered: true });

    // Act
    await page.goto('/');
    await expect(page.getByRole('cell', { name: /example-org\/bookstore-api/ })).toBeVisible();

    // Assert
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(360);
  });
});
