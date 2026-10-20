import { readFile } from 'node:fs/promises'
import { expect, test, type Page } from '@playwright/test'

const password = 'correct-horse-42'
const unique = () => `${Date.now()}${Math.floor(Math.random() * 1000)}`

async function api(page: Page, path: string, body: unknown) {
  const organizationId = await page.evaluate(() => localStorage.getItem('pms.organizationId'))
  const response = await page.request.post(path, {
    data: body,
    headers: { 'X-Requested-With': 'XMLHttpRequest', 'X-Organization-Id': organizationId ?? '' },
  })
  expect(response.ok(), await response.text()).toBeTruthy()
  return response.json()
}

/** A date in Lagos (UTC+1) as YYYY-MM-DD, so day counts match the server's. */
function inDays(days: number) {
  return new Date(Date.now() + 60 * 60 * 1000 + days * 24 * 60 * 60 * 1000).toISOString().slice(0, 10)
}

test('a manager reads occupancy and lease expiries and downloads a report', async ({ page }) => {
  const id = unique()
  await page.goto('/register')
  await page.getByLabel('Full name', { exact: true }).fill('Seun Adelaja')
  await page.getByLabel('Email', { exact: true }).fill(`reports-${id}@example.com`)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByLabel('Company or business name', { exact: true }).fill(`Yaba Homes ${id}`)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByRole('heading', { name: 'Welcome, Seun' })).toBeVisible()

  const property = await api(page, '/api/v1/properties', {
    code: `YB-${id.slice(-6)}`, name: 'Herbert Macaulay Court', type: 'Residential', addressLine: '20 Herbert Macaulay Way', state: 'Lagos',
  })
  const unitBody = { type: 'Flat', defaultRent: 1800000, billingFrequency: 'Annual', currency: 'NGN' }
  const let1 = await api(page, `/api/v1/properties/${property.id}/units`, { ...unitBody, code: 'C1' })
  await api(page, `/api/v1/properties/${property.id}/units`, { ...unitBody, code: 'C2' })
  const tenant = await api(page, '/api/v1/tenants', { type: 'Individual', name: 'Ifeoma Obi', email: `tenant-${id}@example.com` })
  const lease = await api(page, '/api/v1/leases', {
    unitId: let1.id, tenantIds: [tenant.id], guarantorIds: [], startDate: inDays(-330), endDate: inDays(34),
    rentAmount: 1800000, currency: 'NGN', billingFrequency: 'Annual', dueDaysBeforePeriod: 0, graceDays: 7, charges: [],
  })
  await api(page, `/api/v1/leases/${lease.id}/activate`, {})

  // Reports is in the menu (behind the menu button on a phone).
  const menu = page.getByRole('button', { name: 'Menu' })
  if (await menu.isVisible()) await menu.click()
  await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Reports' }).click()
  await expect(page.getByRole('heading', { name: 'Reports', exact: true })).toBeVisible()

  // Occupancy opens first: one of two flats is let.
  await expect(page.getByRole('heading', { name: 'Occupancy and vacancy' })).toBeVisible()
  await expect(page.getByText('50%').first()).toBeVisible()
  await expect(page.getByRole('cell', { name: 'Herbert Macaulay Court' })).toBeVisible()

  // Lease expiries lists the lease ending in 34 days.
  const picker = page.getByLabel('Report', { exact: true })
  if (await picker.isVisible()) await picker.selectOption('lease-expiries')
  else await page.getByRole('button', { name: 'Lease expiry and renewals' }).click()
  await expect(page.getByRole('heading', { name: 'Lease expiry and renewals' })).toBeVisible()
  await expect(page.getByRole('cell', { name: 'Ifeoma Obi' })).toBeVisible()
  await expect(page.getByRole('cell', { name: 'Not started' })).toBeVisible()
  await expect(page.getByRole('cell', { name: '₦1,800,000' })).toBeVisible()

  // A later period leaves it out.
  await page.getByLabel('From').fill(inDays(60))
  await page.getByLabel('To').fill(inDays(120))
  await expect(page.getByText('Nothing to show for this period.')).toBeVisible()
  await page.getByLabel('From').fill(inDays(0))
  await page.getByLabel('To').fill(inDays(60))
  await expect(page.getByRole('cell', { name: 'Ifeoma Obi' })).toBeVisible()

  const downloading = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Download CSV' }).click()
  const download = await downloading
  expect(download.suggestedFilename()).toMatch(/^lease-expiries-\d{4}-\d{2}-\d{2}\.csv$/)
  const csv = (await readFile((await download.path())!, 'utf8')).replace(/^﻿/, '')
  expect(csv.split('\r\n')[0]).toBe('Lease,Property,Unit,Tenant,Ends,Days left,Rent,Paid,Renewal,Latest notice')
  expect(csv).toContain(`Herbert Macaulay Court,C1,Ifeoma Obi,${inDays(34)},34,1800000`)
})
