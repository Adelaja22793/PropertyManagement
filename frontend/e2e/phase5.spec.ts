import { expect, test, type Page } from '@playwright/test'

const password = 'correct-horse-42'
const unique = () => `${Date.now()}${Math.floor(Math.random() * 1000)}`

async function goTo(page: Page, link: string) {
  const menu = page.getByRole('button', { name: 'Menu' })
  if (await menu.isVisible()) await menu.click()
  await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: link }).click()
}

/** Calls the API as the signed-in user, the same way the app does. */
async function api(page: Page, path: string, body: unknown, method: 'post' | 'put' = 'post') {
  const organizationId = await page.evaluate(() => localStorage.getItem('pms.organizationId'))
  const response = await page.request[method](path, {
    data: body,
    headers: { 'X-Requested-With': 'XMLHttpRequest', 'X-Organization-Id': organizationId ?? '' },
  })
  expect(response.ok(), await response.text()).toBeTruthy()
  return response.json()
}

test('a repair is reported, estimated with owner approval, assigned and completed', async ({ page }) => {
  const id = unique()
  await page.goto('/register')
  await page.getByLabel('Full name', { exact: true }).fill('Seun Adelaja')
  await page.getByLabel('Email', { exact: true }).fill(`repair-${id}@example.com`)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByLabel('Company or business name', { exact: true }).fill(`Lekki Homes ${id}`)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByRole('heading', { name: 'Welcome, Seun' })).toBeVisible()

  // A property with an owner whose agreement caps repairs at ₦100,000, and a vendor.
  const property = await api(page, '/api/v1/properties', {
    code: `LK-${id.slice(-6)}`, name: 'Admiralty Court', type: 'Residential', addressLine: '12 Admiralty Way', state: 'Lagos',
  })
  await api(page, `/api/v1/properties/${property.id}/units`, {
    code: 'C3', type: 'Flat', defaultRent: 2500000, billingFrequency: 'Annual', currency: 'NGN',
  })
  const owner = await api(page, '/api/v1/owners', { type: 'Individual', name: 'Mrs Funmi Adebayo', phone: '08031112222' })
  await api(page, `/api/v1/properties/${property.id}/owners`, {
    effectiveFrom: '2026-01-01', shares: [{ ownerId: owner.id, sharePercent: 100 }],
  }, 'put')
  await api(page, `/api/v1/owners/${owner.id}/agreements`, {
    propertyId: property.id, feeBasis: 'PercentOfRentCollected', feeValue: 10, payoutFrequency: 'Monthly',
    advanceRentRelease: 'OnReceipt', reserveAmount: 0, startDate: '2026-01-01', repairApprovalLimit: 100000,
  })

  // Add the vendor on the vendors page.
  await goTo(page, 'Maintenance')
  await page.getByRole('link', { name: 'Vendors' }).click()
  await page.getByRole('button', { name: 'Add vendor' }).click()
  await page.getByLabel('Name', { exact: true }).fill('Bayo Plumbing Works')
  await page.getByLabel('Trade', { exact: true }).fill('Plumbing')
  await page.getByLabel('Phone', { exact: true }).fill('08030000000')
  await page.getByRole('button', { name: 'Save vendor' }).click()
  await expect(page.getByText('Bayo Plumbing Works')).toBeVisible()

  // Report the repair from the property page.
  await goTo(page, 'Properties')
  await page.getByRole('link', { name: /Admiralty Court/ }).click()
  await page.getByRole('link', { name: 'Report a repair' }).click()
  await expect(page.getByLabel('Property', { exact: true }).locator('option:checked')).toHaveText(/Admiralty Court/)
  await page.getByLabel('Unit', { exact: true }).selectOption({ label: 'C3' })
  await page.getByLabel('Problem', { exact: true }).fill('Kitchen sink leaking')
  await page.getByLabel('Reported by', { exact: true }).fill('Tenant by phone')
  await page.getByLabel('Urgency', { exact: true }).selectOption('High')
  await page.getByRole('button', { name: 'Save request' }).click()

  await expect(page.getByRole('heading', { name: 'Kitchen sink leaking' })).toBeVisible()
  await expect(page.getByText('Submitted', { exact: true })).toBeVisible()

  // Estimate above the owner's limit: approval must be recorded first.
  await page.getByRole('button', { name: 'Add estimate' }).click()
  await expect(page.getByText('The owner approves anything above ₦100,000.00.')).toBeVisible()
  await page.getByLabel('Estimated cost (₦)').fill('180000')
  await page.getByRole('button', { name: 'Save estimate' }).click()
  await expect(page.getByText(/above the owner's repair approval limit/)).toBeVisible()

  await page.getByRole('button', { name: 'Approve work' }).click()
  await page.getByLabel('Approval', { exact: true }).fill('Mrs Adebayo approved by WhatsApp')
  await page.getByRole('button', { name: 'Record and approve work' }).click()
  await expect(page.getByText('Approved', { exact: true })).toBeVisible()

  await page.getByRole('button', { name: 'Assign', exact: true }).click()
  await page.getByLabel('Vendor', { exact: true }).selectOption({ label: 'Bayo Plumbing Works · Plumbing' })
  await page.getByRole('button', { name: 'Assign', exact: true }).click()
  await expect(page.getByText('Assigned to Bayo Plumbing Works')).toBeVisible()

  await page.getByRole('button', { name: 'Start work' }).click()
  await expect(page.getByText('In progress', { exact: true })).toBeVisible()
  await page.getByLabel('Add an update').fill('Plumber on site, replacing the trap.')
  await page.getByRole('button', { name: 'Post update' }).click()
  await expect(page.getByText('Plumber on site, replacing the trap.')).toBeVisible()

  await page.getByRole('button', { name: 'Mark completed' }).click()
  await page.getByLabel('Final cost (₦)').fill('175000')
  await page.getByRole('button', { name: 'Mark completed' }).click()
  await expect(page.getByText('Completed', { exact: true })).toBeVisible()
  await expect(page.getByText('₦175,000.00')).toBeVisible()

  // It no longer shows as open.
  await goTo(page, 'Maintenance')
  await expect(page.getByText('Nothing here.')).toBeVisible()
  await page.getByRole('button', { name: 'Completed' }).click()
  await expect(page.getByRole('link', { name: /Kitchen sink leaking/ })).toBeVisible()
})
