import { expect, test } from '@playwright/test';
import { mockApi, PREVIOUS_RUN_ID, RUN_ID } from './mock-api';

/** M8 acceptance (doc 11): two example runs show the expected newly covered and regressed items, and the non-comparable banner works. */

test('report_compareWith_showsNewlyCoveredAndNoRegressions', async ({ page }) => {
  // Arrange
  await mockApi(page, { registered: true, finished: true });
  await page.goto(`/runs/${RUN_ID}`);

  // Act
  await page.getByRole('link', { name: 'Compare with…' }).click();

  // Assert
  await expect(page).toHaveURL(`/compare?base=${PREVIOUS_RUN_ID}&target=${RUN_ID}`);
  await expect(page.locator('.panel').filter({ has: page.getByRole('heading', { name: 'Newly covered' }) })).toContainText('POST api/orders');
  await expect(page.locator('.panel').filter({ has: page.getByRole('heading', { name: 'Regressed' }) })).toContainText('None.');
});

test('compare_notComparable_showsWarningBanner', async ({ page }) => {
  // Arrange
  await mockApi(page, { registered: true, finished: true, notComparable: true });

  // Act
  await page.goto(`/compare?base=${PREVIOUS_RUN_ID}&target=${RUN_ID}`);

  // Assert
  await expect(page.locator('.banner')).toContainText('Not comparable');
  await expect(page.locator('.banner')).toContainText('integrationMappedTo changed');
  await expect(page.locator('.metrics .up, .metrics .down')).toHaveCount(0);
});

test('report_kpiChips_showScoreDelta', async ({ page }) => {
  // Arrange
  await mockApi(page, { registered: true, finished: true });

  // Act
  await page.goto(`/runs/${RUN_ID}`);

  // Assert
  await expect(page.locator('.kpi').filter({ hasText: 'Quality score' }).locator('.delta')).toHaveText('▲ 0.4');
});

test('report_runPicker_opensPreviousRun', async ({ page }) => {
  // Arrange
  await mockApi(page, { registered: true, finished: true });
  await page.goto(`/runs/${RUN_ID}`);

  // Act
  await page.getByRole('button', { name: 'Previous run' }).click();

  // Assert
  await expect(page).toHaveURL(`/runs/${PREVIOUS_RUN_ID}`);
  await expect(page.locator('.kpi').filter({ hasText: 'Quality score' }).locator('.val')).toHaveText(/4\.7\s*\/10/);
});

test('repositories_trend_drawsSparkline', async ({ page }) => {
  // Arrange
  await mockApi(page, { registered: true, finished: true });

  // Act
  await page.goto('/');

  // Assert
  await expect(page.locator('td.trend svg')).toHaveAttribute('aria-label', 'Score trend: 4.5 to 4.9');
});
