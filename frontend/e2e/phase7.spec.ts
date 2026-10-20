import { expect, test, type Browser, type Page } from '@playwright/test'

const password = 'correct-horse-42'
const unique = () => `${Date.now()}${Math.floor(Math.random() * 1000)}`
const pdf = Buffer.from('%PDF-1.7\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n')
// A 1x1 PNG.
const png = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=',
  'base64',
)

async function api(page: Page, path: string, body: unknown, method: 'post' | 'put' = 'post') {
  const organizationId = await page.evaluate(() => localStorage.getItem('pms.organizationId'))
  const response = await page.request[method](path, {
    data: body,
    headers: { 'X-Requested-With': 'XMLHttpRequest', 'X-Organization-Id': organizationId ?? '' },
  })
  expect(response.ok(), await response.text()).toBeTruthy()
  return response.json()
}

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

function inDays(days: number) {
  const d = new Date()
  d.setDate(d.getDate() + days)
  return d.toISOString().slice(0, 10)
}

test('staff file documents, share them, and a tenant adds a repair photo', async ({ page, browser }) => {
  const id = unique()
  await page.goto('/register')
  await page.getByLabel('Full name', { exact: true }).fill('Seun Adelaja')
  await page.getByLabel('Email', { exact: true }).fill(`docs-${id}@example.com`)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByLabel('Company or business name', { exact: true }).fill(`Ikoyi Homes ${id}`)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByRole('heading', { name: 'Welcome, Seun' })).toBeVisible()

  const property = await api(page, '/api/v1/properties', {
    code: `IK-${id.slice(-6)}`, name: 'Bourdillon Court', type: 'Residential', addressLine: '12 Bourdillon Road', state: 'Lagos',
  })
  const unit = await api(page, `/api/v1/properties/${property.id}/units`, {
    code: 'A1', type: 'Flat', defaultRent: 2500000, billingFrequency: 'Annual', currency: 'NGN',
  })
  const owner = await api(page, '/api/v1/owners', { type: 'Individual', name: 'Mrs Coker', email: `owner-${id}@example.com` })
  await api(page, `/api/v1/properties/${property.id}/owners`, { effectiveFrom: '2026-01-01', shares: [{ ownerId: owner.id, sharePercent: 100 }] }, 'put')
  const tenant = await api(page, '/api/v1/tenants', { type: 'Individual', name: 'Tunde Bakare', email: `tenant-${id}@example.com` })
  const lease = await api(page, '/api/v1/leases', {
    unitId: unit.id, tenantIds: [tenant.id], guarantorIds: [], startDate: '2026-01-01',
    endDate: '2026-12-31', rentAmount: 2500000, currency: 'NGN', billingFrequency: 'Annual', dueDaysBeforePeriod: 0, graceDays: 7, charges: [],
  })
  await api(page, `/api/v1/leases/${lease.id}/activate`, {})

  // Staff upload the building insurance on the property page and share it with the owner.
  await page.goto(`/app/properties/${property.id}`)
  const panel = page.locator('section').filter({ has: page.getByRole('heading', { name: 'Documents and photos' }) })
  await panel.getByRole('button', { name: 'Add file' }).click()
  await panel.getByLabel('File').setInputFiles({ name: 'insurance.pdf', mimeType: 'application/pdf', buffer: pdf })
  await panel.getByLabel('Title').fill('Building insurance 2026')
  await panel.getByLabel('Type').selectOption('Insurance')
  await panel.getByLabel('Expires on').fill(inDays(20))
  await panel.getByLabel('Share with the owner').check()
  await panel.getByRole('button', { name: 'Upload' }).click()
  await expect(panel.getByRole('link', { name: 'Building insurance 2026' })).toBeVisible()
  await expect(panel.getByText('Shared with owner')).toBeVisible()

  // A file that isn't a real PDF is refused.
  await panel.getByRole('button', { name: 'Add file' }).click()
  await panel.getByLabel('File').setInputFiles({ name: 'fake.pdf', mimeType: 'application/pdf', buffer: Buffer.from('<html></html>') })
  await panel.getByRole('button', { name: 'Upload' }).click()
  await expect(panel.getByRole('alert')).toContainText("isn't accepted")
  await panel.getByRole('button', { name: 'Cancel' }).click()

  // The dashboard warns that it expires soon.
  await page.goto('/app')
  await expect(page.getByRole('heading', { name: 'Documents expiring soon' })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Building insurance 2026' })).toBeVisible()

  // The owner sees the shared file in their portal.
  const link = (invite: { acceptToken: string; invitation: { email: string } }) =>
    `/join?token=${encodeURIComponent(invite.acceptToken)}&email=${encodeURIComponent(invite.invitation.email)}`
  const ownerInvite = await api(page, `/api/v1/owners/${owner.id}/portal-invite`, {})
  const tenantInvite = await api(page, `/api/v1/tenants/${tenant.id}/portal-invite`, {})
  const ownerPage = await join(browser, link(ownerInvite), 'Mrs Coker')
  await expect(ownerPage.getByRole('heading', { name: 'Welcome, Mrs Coker' })).toBeVisible()
  await expect(ownerPage.getByRole('link', { name: 'Building insurance 2026' })).toBeVisible()

  // The tenant reports a repair and adds a photo to it.
  const tenantPage = await join(browser, link(tenantInvite), 'Tunde Bakare')
  await expect(tenantPage.getByRole('heading', { name: 'Welcome, Tunde Bakare' })).toBeVisible()
  await expect(tenantPage.getByRole('link', { name: 'Building insurance 2026' })).toHaveCount(0)
  await tenantPage.getByRole('button', { name: 'Report a repair' }).click()
  await tenantPage.getByLabel("What's wrong?").fill('Ceiling stain in bedroom')
  await tenantPage.getByRole('button', { name: 'Send request' }).click()
  await expect(tenantPage.getByText('Request received')).toBeVisible()
  await tenantPage.getByLabel('Choose a photo').setInputFiles({ name: 'ceiling.png', mimeType: 'image/png', buffer: png })
  await expect(tenantPage.getByText('Added by you')).toBeVisible()
  await expect(tenantPage.getByRole('img', { name: /Photo from Tunde Bakare/ })).toBeVisible()

  // Staff see the tenant's photo on the repair.
  const requests = await (
    await page.request.get('/api/v1/maintenance?open=true', {
      headers: { 'X-Organization-Id': (await page.evaluate(() => localStorage.getItem('pms.organizationId'))) ?? '' },
    })
  ).json()
  await page.goto(`/app/maintenance/${requests.items[0].id}`)
  await expect(page.getByRole('heading', { name: 'Photos and attachments' })).toBeVisible()
  await expect(page.getByText('from the tenant portal')).toBeVisible()
  await expect(page.getByRole('img', { name: /Photo from Tunde Bakare/ })).toBeVisible()
})
