import { deflateSync } from 'node:zlib';
import { expect, test } from '@playwright/test';

/** A valid 400×300 solid-colour PNG, generated so no binary fixture is committed. */
function png(width = 400, height = 300): Buffer {
  const crcTable = Array.from({ length: 256 }, (_, n) => {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    return c >>> 0;
  });
  const crc = (buf: Buffer) => { let c = 0xffffffff; for (const b of buf) c = crcTable[(c ^ b) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
  const chunk = (type: string, data: Buffer) => {
    const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
    const td = Buffer.concat([Buffer.from(type, 'ascii'), data]);
    const c = Buffer.alloc(4); c.writeUInt32BE(crc(td));
    return Buffer.concat([len, td, c]);
  };
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0); ihdr.writeUInt32BE(height, 4); ihdr[8] = 8; ihdr[9] = 2;
  const row = Buffer.concat([Buffer.from([0]), Buffer.alloc(width * 3, 0x40)]);
  const raw = Buffer.concat(Array.from({ length: height }, () => row));
  return Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), chunk('IHDR', ihdr), chunk('IDAT', deflateSync(raw)), chunk('IEND', Buffer.alloc(0))]);
}

test('guest messages a host and sends a photo', async ({ page }) => {
  await page.goto('/login');
  await page.getByLabel('Email').fill('guest@example.local');
  await page.getByLabel('Password').fill('Passw0rd!Demo');
  await page.getByRole('button', { name: 'Log in' }).click();
  await expect(page.getByRole('heading', { name: 'Find your next stay' })).toBeVisible();

  await page.goto('/search?location=Lisbon');
  await page.locator('article a').first().click();
  await page.getByRole('button', { name: 'Message the host' }).click();
  await page.getByRole('dialog').getByLabel('Message', { exact: true }).fill('Hi! Is there parking nearby?');
  await page.getByRole('button', { name: 'Send message' }).click();
  await expect(page).toHaveURL(/\/messages\//);

  await page.locator('input[type="file"]').setInputFiles({ name: 'street.png', mimeType: 'image/png', buffer: png() });
  await expect(page.getByRole('img', { name: 'street.jpg' })).toBeVisible();
});
