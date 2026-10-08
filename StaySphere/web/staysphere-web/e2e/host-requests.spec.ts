import { expect, test } from '@playwright/test';

/** Host inbox: seeded request-to-book requests can be accepted, and payouts show balances + an account. */
test('host accepts a booking request and sees payouts', async ({ page }) => {
  await page.goto('/login');
  await page.getByLabel('Email').fill('host@example.local');
  await page.getByLabel('Password').fill('Passw0rd!Demo');
  await page.getByRole('button', { name: 'Log in' }).click();
  await expect(page.getByRole('heading', { name: 'Find your next stay' })).toBeVisible();

  await page.goto('/host/reservations?scope=requests');
  const accept = page.getByRole('button', { name: 'Accept' }).first();
  await expect(accept.or(page.getByText('No pending requests'))).toBeVisible();
  if (await accept.isVisible()) {
    const before = await page.getByRole('button', { name: 'Accept' }).count();
    await accept.click();
    await expect(page.getByText(/stay is confirmed and the payment captured/)).toBeVisible();
    await expect(page.getByRole('button', { name: 'Accept' })).toHaveCount(before - 1);
  } else {
    await expect(page.getByText('No pending requests')).toBeVisible();
  }

  await page.goto('/host/earnings');
  await expect(page.getByRole('heading', { name: 'Earnings & payouts' })).toBeVisible();
  await expect(page.getByText('Payout account')).toBeVisible();
  await expect(page.getByText(/•••• 0154/).first()).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Payout history' })).toBeVisible();
});
