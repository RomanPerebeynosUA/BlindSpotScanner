import { expect, test } from '@playwright/test';
import { mockApi, RUN_ID } from './mock-api';

/** M9 (doc 04 Jira dialog): a ticket is created only after an editable preview, and an existing ticket is shown first. */

test('report_createJira_fromFinding_showsTicketLink', async ({ page }) => {
  // Arrange
  const api = await mockApi(page, { registered: true, finished: true });
  await page.goto(`/runs/${RUN_ID}`);
  const finding = page.locator('[data-kind=FalsePositive] .finding');

  // Act
  await finding.getByRole('button', { name: 'Create Jira' }).click();
  await page.getByLabel('Summary').fill('Edited summary');
  await page.getByRole('button', { name: 'Create ticket' }).click();

  // Assert
  await expect(finding.getByRole('link', { name: 'BSS-1 · To Do' })).toBeVisible();
  await expect(page.locator('app-actions-log')).toContainText('BSS-1');
  expect(api.jiraBodies().map((b) => b.summary)).toEqual(['Edited summary']);
});

test('report_ticketedFinding_dialogWarnsBeforeCreateAnyway', async ({ page }) => {
  // Arrange
  await mockApi(page, { registered: true, finished: true, ticketed: true });
  await page.goto(`/runs/${RUN_ID}`);

  // Act
  await page.locator('[data-kind=FalsePositive] .finding').getByRole('button', { name: 'Create Jira' }).click();

  // Assert
  await expect(page.getByRole('alert')).toContainText('Already ticketed: BSS-1');
  await expect(page.getByRole('button', { name: 'Create anyway' })).toBeVisible();
});

test('report_untestedEndpoint_hasCreateJira', async ({ page }) => {
  // Arrange
  await mockApi(page, { registered: true, finished: true });

  // Act
  await page.goto(`/runs/${RUN_ID}`);

  // Assert
  await expect(page.locator('tr.endpoint[data-key="POST api/orders/{id}/cancel"]').getByRole('button', { name: 'Create Jira' })).toBeVisible();
});
