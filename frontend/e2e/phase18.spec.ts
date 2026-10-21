import { expect, test, type Page } from '@playwright/test'

const password = 'correct-horse-42'
const unique = () => `${Date.now()}${Math.floor(Math.random() * 1000)}`

/** Calls the API as the signed-in user, the same way the app does. */
async function api(page: Page, path: string, body: unknown) {
  const organizationId = await page.evaluate(() => localStorage.getItem('pms.organizationId'))
  const response = await page.request.post(path, {
    data: body,
    headers: { 'X-Requested-With': 'XMLHttpRequest', 'X-Organization-Id': organizationId ?? '' },
  })
  expect(response.ok(), await response.text()).toBeTruthy()
  return response.json()
}

test('a unit let room by room takes several leases up to its limit', async ({ page }) => {
  const id = unique()
  await page.goto('/register')
  await page.getByLabel('Full name', { exact: true }).fill('Seun Adelaja')
  await page.getByLabel('Email', { exact: true }).fill(`shared-${id}@example.com`)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByLabel('Company or business name', { exact: true }).fill(`Akoka Rooms ${id}`)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByRole('heading', { name: 'Welcome, Seun' })).toBeVisible()

  const property = await api(page, '/api/v1/properties', {
    code: `AK-${id.slice(-6)}`,
    name: 'Akoka Shared House',
    type: 'Residential',
    addressLine: '9 Akoka Road',
    state: 'Lagos',
  })
  await page.goto(`/app/properties/${property.id}`)

  // A shared unit taking two leases at once.
  await page.getByRole('button', { name: 'Add unit' }).click()
  await page.getByLabel('Unit code', { exact: true }).fill('S1')
  await page.getByLabel('Default rent (₦)').fill('450000')
  await page.getByLabel('Let room by room (shared)').check()
  await page.getByLabel('Leases at once').fill('1')
  await page.getByRole('button', { name: 'Save unit' }).click()
  await expect(page.getByText('Enter a number from 2 to 50.')).toBeVisible()
  await page.getByLabel('Leases at once').fill('2')
  await page.getByRole('button', { name: 'Save unit' }).click()
  const row = page.getByRole('row').filter({ hasText: 'S1' })
  await expect(row).toContainText('Shared, 0 of 2 let')

  const units = await (
    await page.request.get(`/api/v1/properties/${property.id}/units`, {
      headers: { 'X-Organization-Id': (await page.evaluate(() => localStorage.getItem('pms.organizationId'))) ?? '' },
    })
  ).json()
  const unitId = units[0].id
  const today = new Date().toISOString().slice(0, 10)
  for (const name of ['Room One', 'Room Two']) {
    const tenant = await api(page, '/api/v1/tenants', { type: 'Individual', name, phone: '08031112222' })
    const lease = await api(page, '/api/v1/leases', {
      unitId,
      tenantIds: [tenant.id],
      guarantorIds: [],
      startDate: today,
      rentAmount: 450000,
      currency: 'NGN',
      billingFrequency: 'Annual',
      dueDaysBeforePeriod: 0,
      graceDays: 7,
      noticePeriodDays: null,
      charges: [],
      endDate: new Date(Date.now() + 364 * 86400000).toISOString().slice(0, 10),
    })
    await api(page, `/api/v1/leases/${lease.id}/activate`, {})
    await page.reload()
    if (name === 'Room One') {
      await expect(row).toContainText('Shared, 1 of 2 let')
      await expect(row.getByRole('link', { name: 'Let unit S1' })).toBeVisible()
    }
  }
  await expect(row).toContainText('Shared, 2 of 2 let')
  await expect(row.getByRole('link', { name: 'Let unit S1' })).toHaveCount(0)
})
