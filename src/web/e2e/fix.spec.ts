import { expect, test } from '@playwright/test';
import { mockApi, RUN_ID } from './mock-api';

/** M10 (doc 04 Fix-with-PR dialog): a draft PR only after the diff preview, read-only mode hides the button, and changed files offer Regenerate. */

test('report_fixWithPr_opensDraftPrAndShowsLink', async ({ page }) => {
  // Arrange
  const api = await mockApi(page, { registered: true, finished: true });
  await page.goto(`/runs/${RUN_ID}`);
  const finding = page.locator('[data-kind=FalsePositive] .finding');

  // Act
  await finding.getByRole('button', { name: 'Fix with PR' }).click();
  await expect(page.locator('.diff .add').first()).toBeVisible();
  await page.getByRole('button', { name: 'Open draft PR' }).click();

  // Assert
  await expect(finding.getByRole('link', { name: '#7 · Open' })).toBeVisible();
  expect(api.pullRequestBodies().map((b) => b.branch)).toEqual(['coverage/fix-false-positive-tests-bookstore-integrationtests-booksapitest-a1b2c3d']);
});

test('report_readOnly_hidesFixWithPr', async ({ page }) => {
  // Arrange
  await mockApi(page, { registered: true, finished: true, readOnly: true });

  // Act
  await page.goto(`/runs/${RUN_ID}`);

  // Assert
  await expect(page.getByRole('button', { name: 'Create Jira' }).first()).toBeVisible();
  await expect(page.getByRole('button', { name: 'Fix with PR' })).toHaveCount(0);
});

test('report_filesChanged_offersRegenerate', async ({ page }) => {
  // Arrange
  await mockApi(page, { registered: true, finished: true, filesChanged: true });
  await page.goto(`/runs/${RUN_ID}`);
  await page.locator('[data-kind=FalsePositive] .finding').getByRole('button', { name: 'Fix with PR' }).click();

  // Act
  await page.getByRole('button', { name: 'Open draft PR' }).click();

  // Assert
  await expect(page.getByRole('alert').filter({ hasText: 'Files changed since the scan' })).toContainText('tests/Bookstore.IntegrationTests/BooksApiTests.cs');
  await expect(page.getByRole('button', { name: 'Regenerate' })).toBeVisible();
});

test.describe('long fix draft on a 1280x720 screen', () => {
  test.use({ viewport: { width: 1280, height: 720 } });

  test('report_longFixDraft_editAndOpenStayReachable', async ({ page }) => {
    // Arrange
    const api = await mockApi(page, { registered: true, finished: true, longFix: true });
    await page.goto(`/runs/${RUN_ID}`);
    await page.locator('[data-kind=FalsePositive] .finding').getByRole('button', { name: 'Fix with PR' }).click();
    await expect(page.locator('.diff')).toBeVisible();

    // Act
    await page.getByRole('button', { name: 'Edit' }).click({ timeout: 3000 });
    await page.getByRole('button', { name: 'Open draft PR' }).click({ timeout: 3000 });

    // Assert
    expect(api.pullRequestBodies()).toHaveLength(1);
  });
});
