import { expect, test } from '@playwright/test';

// The compose stack turns the Experiences flag on and seeds experiences with sessions over the next 30 days.
test('guest books an experience session and cancels it', async ({ page }) => {
  await page.goto('/login');
  await page.getByLabel('Email').fill('guest@example.local');
  await page.getByLabel('Password').fill('Passw0rd!Demo');
  await page.getByRole('button', { name: 'Log in' }).click();
  await expect(page.getByRole('heading', { name: 'Find your next stay' })).toBeVisible();

  await page.getByRole('navigation', { name: 'Primary' }).getByRole('link', { name: 'Experiences' }).click();
  await expect(page.getByRole('heading', { name: 'Experiences', level: 1 })).toBeVisible();
  const card = page.locator('main article').first();
  const title = await card.getByRole('heading').innerText();
  await card.click();

  // The last session is weeks away, so it's still inside the free-cancellation window.
  await page.getByRole('radiogroup', { name: 'Sessions' }).getByRole('radio').and(page.locator(':not([disabled])')).last().click();
  await page.getByLabel('Guests').selectOption('2');
  await page.getByRole('button', { name: 'Book', exact: true }).click();

  await expect(page).toHaveURL(/\/trips\/experiences$/);
  const booking = page.getByRole('listitem').filter({ hasText: title }).filter({ has: page.getByRole('button', { name: 'Cancel' }) }).first();
  await expect(booking).toContainText('2 guests');
  page.once('dialog', (d) => d.accept());
  await booking.getByRole('button', { name: 'Cancel' }).click();
  await expect(page.getByText('Booking cancelled')).toBeVisible();
});
