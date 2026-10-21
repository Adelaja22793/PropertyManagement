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

test('a company sets its own notice periods and reminder days', async ({ page }) => {
  const id = unique()
  await page.goto('/register')
  await page.getByLabel('Full name', { exact: true }).fill('Seun Adelaja')
  await page.getByLabel('Email', { exact: true }).fill(`notice-rules-${id}@example.com`)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByLabel('Company or business name', { exact: true }).fill(`Abeokuta Homes ${id}`)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByRole('heading', { name: 'Welcome, Seun' })).toBeVisible()

  await page.goto('/app/settings')
  const reminders = page
    .locator('section, div')
    .filter({ has: page.getByRole('heading', { name: 'Lease reminders', exact: true }) })
    .last()
  await expect(reminders.getByLabel('Remind staff this many days before a lease ends')).toHaveValue('90, 60, 30')

  // Reminder days: a bad value is explained, and tenant reminders can be turned on.
  await reminders.getByLabel('Remind staff this many days before a lease ends').fill('90, soon')
  await expect(page.getByText('Use whole numbers separated by commas, such as 90, 60, 30.')).toBeVisible()
  await reminders.getByLabel('Remind staff this many days before a lease ends').fill('30, 120')
  await reminders.getByLabel('Also email tenants before their lease ends').check()
  await reminders.getByLabel('Remind tenants this many days before their lease ends').fill('14')
  await reminders.getByRole('button', { name: 'Save reminders' }).click()
  await expect(reminders.getByLabel('Remind staff this many days before a lease ends')).toHaveValue('120, 30')
  await expect(reminders.getByText('Saved.')).toBeVisible()

  // A notice period for Ogun.
  await expect(page.getByText("You haven't set any of your own yet")).toBeVisible()
  await page.getByRole('button', { name: 'Add notice period' }).click()
  const form = page.getByRole('form', { name: 'New notice period' })
  await form.getByLabel('State').selectOption('Ogun')
  await form.getByLabel('Rent frequency').selectOption('Annual')
  await form.getByLabel('Days').fill('150')
  await form.getByLabel('Legal basis (optional)').fill('Advice from Barr. Bello')
  await form.getByRole('button', { name: 'Add notice period' }).click()
  const list = page.getByRole('list', { name: 'Your notice periods' })
  await expect(list.getByText('Notice to quit: 150 days')).toBeVisible()
  await expect(list.getByText('Ogun, yearly rent')).toBeVisible()

  await list.getByRole('button', { name: 'Edit Notice to quit, Ogun, yearly rent' }).click()
  await page.getByRole('form', { name: 'Edit notice period' }).getByLabel('Days').fill('160')
  await page.getByRole('button', { name: 'Save notice period' }).click()
  await expect(list.getByText('Notice to quit: 160 days')).toBeVisible()

  // A lease on an Ogun property is offered that period when a notice is recorded.
  const today = new Date()
  const iso = (d: Date) => d.toISOString().slice(0, 10)
  const start = new Date(today)
  start.setUTCDate(start.getUTCDate() - 100)
  const end = new Date(start)
  end.setUTCFullYear(end.getUTCFullYear() + 1)
  const property = await api(page, '/api/v1/properties', {
    code: `AB-${id.slice(-6)}`,
    name: 'Oke Ilewo Court',
    type: 'Residential',
    addressLine: '4 Oke Ilewo Road',
    state: 'Ogun',
  })
  const unit = await api(page, `/api/v1/properties/${property.id}/units`, {
    code: 'A1',
    type: 'Flat',
    defaultRent: 900000,
    billingFrequency: 'Annual',
  })
  const tenant = await api(page, '/api/v1/tenants', { type: 'Individual', name: 'Kunle Bello', phone: '08036660000' })
  const lease = await api(page, '/api/v1/leases', {
    unitId: unit.id,
    tenantIds: [tenant.id],
    guarantorIds: [],
    startDate: iso(start),
    endDate: iso(end),
    rentAmount: 900000,
    currency: 'NGN',
    billingFrequency: 'Annual',
    dueDaysBeforePeriod: 0,
    graceDays: 7,
    noticePeriodDays: null,
    charges: [],
  })
  await api(page, `/api/v1/leases/${lease.id}/activate`, {})
  await page.goto(`/app/leases/${lease.id}`)
  await page.getByRole('button', { name: 'Record a notice' }).click()
  await expect(page.getByText(/\(160 days\)\. Advice from Barr\. Bello/)).toBeVisible()

  // Removing it brings back the default.
  await page.goto('/app/settings')
  page.once('dialog', (d) => d.accept())
  await page.getByRole('button', { name: 'Remove Notice to quit, Ogun, yearly rent' }).click()
  await expect(page.getByText("You haven't set any of your own yet")).toBeVisible()
})
