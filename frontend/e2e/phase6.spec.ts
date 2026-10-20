import { expect, test, type Browser, type Page } from '@playwright/test'

const password = 'correct-horse-42'
const unique = () => `${Date.now()}${Math.floor(Math.random() * 1000)}`

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

/** Opens a portal invitation link in a fresh browser and creates the person's login. */
async function join(browser: Browser, link: string, name: string) {
  const context = await browser.newContext({ ...test.info().project.use })
  const page = await context.newPage()
  await page.goto(link)
  await page.getByRole('button', { name: 'Create a login' }).click()
  await page.getByLabel('Full name').fill(name)
  await page.getByLabel('Password').fill(password)
  await page.getByRole('button', { name: 'Create login and join' }).click()
  return page
}

test('a tenant reports a repair and the owner approves the estimate in their portals', async ({ page, browser }) => {
  const id = unique()
  await page.goto('/register')
  await page.getByLabel('Full name', { exact: true }).fill('Seun Adelaja')
  await page.getByLabel('Email', { exact: true }).fill(`portal-${id}@example.com`)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByLabel('Company or business name', { exact: true }).fill(`Yaba Lettings ${id}`)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByRole('heading', { name: 'Welcome, Seun' })).toBeVisible()

  // Set up a let unit with an owner (repair limit ₦50,000) and a tenant, both invited to their portals.
  const property = await api(page, '/api/v1/properties', {
    code: `YB-${id.slice(-6)}`, name: 'Herbert Macaulay Court', type: 'Residential', addressLine: '40 Herbert Macaulay Way', state: 'Lagos',
  })
  const unit = await api(page, `/api/v1/properties/${property.id}/units`, {
    code: 'D4', type: 'Flat', defaultRent: 1800000, billingFrequency: 'Annual', currency: 'NGN',
  })
  const ownerEmail = `owner-${id}@example.com`
  const owner = await api(page, '/api/v1/owners', { type: 'Individual', name: 'Chief Okafor', email: ownerEmail })
  await api(page, `/api/v1/properties/${property.id}/owners`, { effectiveFrom: '2026-01-01', shares: [{ ownerId: owner.id, sharePercent: 100 }] }, 'put')
  await api(page, `/api/v1/owners/${owner.id}/agreements`, {
    propertyId: property.id, feeBasis: 'PercentOfRentCollected', feeValue: 10, payoutFrequency: 'Monthly',
    advanceRentRelease: 'OnReceipt', reserveAmount: 0, startDate: '2026-01-01', repairApprovalLimit: 50000,
  })
  const tenantEmail = `tenant-${id}@example.com`
  const tenant = await api(page, '/api/v1/tenants', { type: 'Individual', name: 'Amaka Eze', email: tenantEmail })
  const lease = await api(page, '/api/v1/leases', {
    unitId: unit.id, tenantIds: [tenant.id], guarantorIds: [], startDate: '2026-01-01',
    endDate: '2026-12-31', rentAmount: 1800000, currency: 'NGN', billingFrequency: 'Annual', dueDaysBeforePeriod: 0, graceDays: 7, charges: [],
  })
  await api(page, `/api/v1/leases/${lease.id}/activate`, {})
  const link = (invite: { acceptToken: string; invitation: { email: string } }) =>
    `/join?token=${encodeURIComponent(invite.acceptToken)}&email=${encodeURIComponent(invite.invitation.email)}`
  const tenantInvite = await api(page, `/api/v1/tenants/${tenant.id}/portal-invite`, {})
  const ownerInvite = await api(page, `/api/v1/owners/${owner.id}/portal-invite`, {})

  // The tenant sees their lease and reports a leak.
  const tenantPage = await join(browser, link(tenantInvite), 'Amaka Eze')
  await expect(tenantPage.getByRole('heading', { name: 'Welcome, Amaka Eze' })).toBeVisible()
  await expect(tenantPage.getByText('Herbert Macaulay Court, unit D4')).toBeVisible()
  await tenantPage.getByRole('button', { name: 'Report a repair' }).click()
  await tenantPage.getByLabel("What's wrong?").fill('Water heater not working')
  await tenantPage.getByLabel('More details').fill('No hot water since Monday.')
  await tenantPage.getByRole('button', { name: 'Send request' }).click()
  await expect(tenantPage.getByText('Request received')).toBeVisible()

  // Staff estimate it above the owner's limit.
  const requests = await (await page.request.get('/api/v1/maintenance?open=true', {
    headers: { 'X-Organization-Id': (await page.evaluate(() => localStorage.getItem('pms.organizationId'))) ?? '' },
  })).json()
  const requestId = requests.items[0].id
  await api(page, `/api/v1/maintenance/${requestId}/estimate`, { amount: 85000, notes: 'New heating element' })

  // The owner approves it in their portal.
  const ownerPage = await join(browser, link(ownerInvite), 'Chief Okafor')
  await expect(ownerPage.getByRole('heading', { name: 'Welcome, Chief Okafor' })).toBeVisible()
  await expect(ownerPage.getByText('Amaka Eze')).toBeVisible()
  await ownerPage.getByRole('button', { name: /Water heater not working/ }).first().click()
  await expect(ownerPage.getByText('Your approval is needed')).toBeVisible()
  await ownerPage.getByRole('button', { name: 'Approve estimate' }).click()
  await expect(ownerPage.getByText("Owner's approval recorded")).toBeVisible()
  await expect(ownerPage.getByText('Your approval is needed')).toHaveCount(0)

  // Staff can now approve the work, and the tenant sees the progress.
  await page.goto(`/app/maintenance/${requestId}`)
  await page.getByRole('button', { name: 'Approve work' }).click()
  await expect(page.getByText('Approved', { exact: true })).toBeVisible()
  // The tenant is still on the request; reloading shows the new step.
  await tenantPage.reload()
  await expect(tenantPage.getByText('Approved', { exact: true }).first()).toBeVisible()
})
