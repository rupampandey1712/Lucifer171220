import { expect, test } from '@playwright/test';

test('home page renders search and destinations', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Find your next stay' })).toBeVisible();
  await expect(page.getByRole('search', { name: 'Search stays' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Popular destinations' })).toBeVisible();
});

test('search shows results with filters and map', async ({ page }) => {
  await page.goto('/search?location=Lisbon');
  await expect(page.getByText(/\d+ stays in Lisbon/)).toBeVisible();
  await expect(page.locator('article').first()).toBeVisible();
});

test('unknown routes show the 404 page', async ({ page }) => {
  await page.goto('/definitely-not-a-page');
  await expect(page.getByText("We can't seem to find that page")).toBeVisible();
});
