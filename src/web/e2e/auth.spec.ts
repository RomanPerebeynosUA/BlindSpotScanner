import { expect, test } from '@playwright/test';
import { mockApi, RUN_ID } from './mock-api';

/** M11 (D26): what each role sees. The API enforces the same rules; these check the UI hides what a role can't do. */

test('viewer_seesReportWithoutActionButtons', async ({ page }) => {
  // Arrange
  await mockApi(page, { registered: true, finished: true, role: 'Viewer' });

  // Act
  await page.goto(`/runs/${RUN_ID}`);

  // Assert
  await expect(page.getByRole('heading', { name: 'Findings', level: 2 })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Create Jira' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Fix with PR' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Export JSON' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Settings' })).toHaveCount(0);
});

test('admin_opensSettings', async ({ page }) => {
  // Arrange
  await mockApi(page, { registered: true, finished: true });
  await page.goto('/');

  // Act
  await page.getByRole('link', { name: 'Settings' }).click();

  // Assert
  await expect(page.locator('[data-kind=jira] .masked')).toHaveText('••••oken');
});

test('signedOut_showsSignIn', async ({ page }) => {
  // Arrange
  await mockApi(page, { role: null });

  // Act
  await page.goto('/');

  // Assert
  await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
});
