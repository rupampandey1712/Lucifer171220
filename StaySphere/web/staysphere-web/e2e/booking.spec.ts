import { expect, test } from '@playwright/test';

/** Full guest journey: login → search → property → dates → reserve (hold) → pay with test card → confirmed. */
test('guest books and pays for a stay', async ({ page }) => {
  await page.goto('/login');
  await page.getByLabel('Email').fill('guest@example.local');
  await page.getByLabel('Password').fill('Passw0rd!Demo');
  await page.getByRole('button', { name: 'Log in' }).click();
  await expect(page.getByRole('heading', { name: 'Find your next stay' })).toBeVisible();

  const offset = 120 + Math.floor(Math.random() * 200);
  const day = (n: number) => new Date(Date.now() + n * 86_400_000).toISOString().slice(0, 10);
  await page.goto(`/search?location=Barcelona`);
  await page.locator('article a').first().click();
  await expect(page.getByRole('button', { name: /Reserve|Check availability/ })).toBeVisible();

  const url = new URL(page.url());
  url.searchParams.set('checkIn', day(offset));
  url.searchParams.set('checkOut', day(offset + 3));
  await page.goto(url.toString());
  await expect(page.getByLabel('Price breakdown')).toBeVisible();
  await page.getByRole('button', { name: 'Reserve' }).click();

  await expect(page.getByRole('heading', { name: 'Confirm and pay' })).toBeVisible();
  await expect(page.getByRole('timer')).toContainText('held for you');
  await page.getByRole('button', { name: /Confirm and pay/ }).click();
  await page.getByRole('button', { name: 'Pay now' }).click();
  await expect(page.getByRole('heading', { name: /You're going to/ })).toBeVisible({ timeout: 15_000 });
});

test('host sees dashboard and admin console is protected', async ({ page }) => {
  await page.goto('/login');
  await page.getByLabel('Email').fill('host@example.local');
  await page.getByLabel('Password').fill('Passw0rd!Demo');
  await page.getByRole('button', { name: 'Log in' }).click();
  await expect(page.getByRole('heading', { name: 'Find your next stay' })).toBeVisible();
  await page.goto('/host');
  await expect(page.getByText('Total bookings')).toBeVisible();
  await page.goto('/admin');
  await expect(page.getByText("You don't have access to this page")).toBeVisible();
});
