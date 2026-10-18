import { expect, test } from '@playwright/test';

// The compose stack runs the simulated identity provider (IdentityVerification__Provider=Simulated): the "capture" step
// returns straight to the site and the check passes, which exercises the full redirect → return → refresh → badge flow.
test('host verifies with an ID document and gets the badge', async ({ page }) => {
  await page.goto('/login');
  await page.getByLabel('Email').fill('host@example.local');
  await page.getByLabel('Password').fill('Passw0rd!Demo');
  await page.getByRole('button', { name: 'Log in' }).click();
  await expect(page.getByRole('heading', { name: 'Find your next stay' })).toBeVisible();

  await page.goto('/host/verification');
  await expect(page.getByRole('heading', { name: 'Host verification' })).toBeVisible();
  const verify = page.getByRole('button', { name: /verify my id/i });
  if (await verify.isVisible()) await verify.click(); // reruns against the same stack start out verified

  await expect(page.getByText('Verified host')).toBeVisible();
  await expect(page).not.toHaveURL(/identity=return/);
});
