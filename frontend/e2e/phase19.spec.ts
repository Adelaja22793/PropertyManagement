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

test('an admin downloads the data held about one guarantor', async ({ page }) => {
  const id = unique()
  await page.goto('/register')
  await page.getByLabel('Full name', { exact: true }).fill('Seun Adelaja')
  await page.getByLabel('Email', { exact: true }).fill(`guarantor-${id}@example.com`)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByLabel('Company or business name', { exact: true }).fill(`Surety Homes ${id}`)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByRole('heading', { name: 'Welcome, Seun' })).toBeVisible()

  const tenant = await api(page, '/api/v1/tenants', { type: 'Individual', name: 'Ngozi Obi', phone: '08035550001' })
  await api(page, `/api/v1/tenants/${tenant.id}/guarantors`, { fullName: 'Chidi Obi', phone: '08035550002', relationship: 'Father' })
  await page.goto(`/app/tenants/${tenant.id}`)

  const downloading = page.waitForEvent('download')
  await page.getByRole('button', { name: "Download Chidi Obi's data" }).click()
  expect((await downloading).suggestedFilename()).toMatch(/^guarantor-chidi-obi-.*\.zip$/)
})
