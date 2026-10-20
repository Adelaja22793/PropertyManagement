import { expect, test, type Page } from '@playwright/test'

const password = 'correct-horse-42'
const unique = () => `${Date.now()}${Math.floor(Math.random() * 1000)}`

async function goTo(page: Page, link: string) {
  const menu = page.getByRole('button', { name: 'Menu' })
  if (await menu.isVisible()) await menu.click()
  await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: link }).click()
}

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

test('a lease is drafted, activated, renewed and ended', async ({ page }) => {
  const id = unique()
  await page.goto('/register')
  await page.getByLabel('Full name', { exact: true }).fill('Seun Adelaja')
  await page.getByLabel('Email', { exact: true }).fill(`lease-${id}@example.com`)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByLabel('Company or business name', { exact: true }).fill(`Ikoyi Lettings ${id}`)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByRole('heading', { name: 'Welcome, Seun' })).toBeVisible()

  // A property, unit and tenant with a guarantor, set up through the API to keep the journey on leases.
  const property = await api(page, '/api/v1/properties', {
    code: `IK-${id.slice(-6)}`, name: 'Bourdillon Court', type: 'Residential', addressLine: '5 Bourdillon Road', state: 'Lagos',
  })
  await api(page, `/api/v1/properties/${property.id}/units`, {
    code: 'B2', type: 'Flat', defaultRent: 3000000, billingFrequency: 'Annual', currency: 'NGN',
  })
  const tenant = await api(page, '/api/v1/tenants', { type: 'Individual', name: 'Tolu Bakare', phone: '08035550000' })
  await api(page, `/api/v1/tenants/${tenant.id}/guarantors`, { fullName: 'Kunle Bakare', phone: '08035551111', signedFormReceived: true })

  // Let the unit from the property page.
  await goTo(page, 'Properties')
  await page.getByRole('link', { name: /Bourdillon Court/ }).click()
  await page.getByRole('link', { name: 'Let unit B2' }).click()
  await expect(page.getByRole('heading', { name: 'New lease' })).toBeVisible()
  await expect(page.getByLabel('Unit', { exact: true }).locator('option:checked')).toHaveText('B2 · Flat · Vacant')
  await expect(page.getByLabel('Rent per period (₦)', { exact: true })).toHaveValue('3000000')
  await page.getByLabel('Find a tenant').fill('Tolu')
  await page.getByRole('button', { name: /Tolu Bakare/ }).click()
  await expect(page.getByText('(primary tenant)')).toBeVisible()
  await page.getByLabel(/Kunle Bakare/).check()
  await page.getByRole('button', { name: 'Add charge' }).click()
  await page.getByLabel('Amount (₦)', { exact: true }).fill('300000')
  await expect(page.getByText('₦3,300,000.00')).toBeVisible()
  await page.getByRole('button', { name: 'Create draft lease' }).click()

  await expect(page.getByRole('heading', { name: 'Tolu Bakare · unit B2' })).toBeVisible()
  await expect(page.getByText('This lease is a draft.')).toBeVisible()
  await page.getByRole('button', { name: 'Activate lease' }).click()
  await expect(page.getByRole('button', { name: 'Renew' })).toBeVisible()
  await expect(page.getByText('Kunle Bakare')).toBeVisible()

  // The unit is now occupied.
  await page.getByRole('link', { name: 'Bourdillon Court' }).click()
  await expect(page.getByRole('row').filter({ hasText: 'B2' })).toContainText('Occupied')
  await page.getByRole('link', { name: /Tolu Bakare/ }).click()

  // Renew at a higher rent.
  await page.getByRole('button', { name: 'Renew' }).click()
  await page.getByLabel('New rent per period (₦)', { exact: true }).fill('3600000')
  await expect(page.getByText('+20% on current rent')).toBeVisible()
  await page.getByRole('button', { name: 'Draft renewal' }).click()
  await expect(page.getByText(/Renews LSE-/)).toBeVisible()
  await expect(page.getByText('Rent change: 20%.')).toBeVisible()

  // Back on the current lease, end it early. The draft renewal is discarded with it.
  await page.getByRole('link', { name: /^LSE-/ }).first().click()
  await page.getByRole('button', { name: 'End lease' }).click()
  await expect(page.getByText('so this is an early termination')).toBeVisible()
  await page.getByLabel('Reason (required)').fill('Relocating to Abuja')
  await page.getByRole('button', { name: 'Terminate lease' }).click()
  await expect(page.getByText('Terminated', { exact: true })).toBeVisible()

  await goTo(page, 'Leases')
  await page.getByRole('button', { name: 'Terminated' }).click()
  await expect(page.getByRole('link', { name: /Tolu Bakare/ })).toHaveCount(1)
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
  expect(overflow).toBeLessThanOrEqual(0)
})
