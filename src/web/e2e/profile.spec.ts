import { expect, test } from '@playwright/test';
import { mockApi, REPO_ID } from './mock-api';

/** D29: Auto-draft fills the typed rules, not only the Markdown, and shows how many files each glob matches. */

test('profile_autoDraft_fillsRulesAndShowsCounts', async ({ page }) => {
  // Arrange
  await mockApi(page, { registered: true });
  await page.goto(`/repos/${REPO_ID}/profile`);

  // Act
  await page.getByRole('button', { name: 'Auto-draft with AI' }).click();

  // Assert
  await expect(page.locator('textarea[name=minimalApiGlobs]')).toHaveValue('**/*Api.cs');
  await expect(page.locator('.matches li').filter({ hasText: '**/*Api.cs' })).toHaveText('**/*Api.cs — 4 files');
  await expect(page.locator('.matches li.zero')).toHaveText('**/Nope.cs — 0 files');
});
