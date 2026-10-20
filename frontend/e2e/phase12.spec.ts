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

async function downloadZip(page: Page, button: string, name: RegExp) {
  const downloading = page.waitForEvent('download')
  await page.getByRole('button', { name: button }).click()
  const download = await downloading
  expect(download.suggestedFilename()).toMatch(name)
  const bytes = await readFile((await download.path())!)
  expect(bytes.subarray(0, 2).toString()).toBe('PK')
}

test('an admin downloads all company data and one tenant’s data', async ({ page }) => {
  const id = unique()
  await page.goto('/register')
  await page.getByLabel('Full name', { exact: true }).fill('Seun Adelaja')
  await page.getByLabel('Email', { exact: true }).fill(`export-${id}@example.com`)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByLabel('Company or business name', { exact: true }).fill(`Ikorodu Homes ${id}`)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByRole('heading', { name: 'Welcome, Seun' })).toBeVisible()
  const tenant = await api(page, '/api/v1/tenants', { type: 'Individual', name: 'Ngozi Nwosu', email: `ngozi-${id}@example.com` })

  await page.goto('/app/settings')
  await expect(page.getByRole('heading', { name: 'Download your data' })).toBeVisible()
  await page.getByLabel('Include uploaded files and photos').check()
  await downloadZip(page, 'Download all data', /^organization-data-\d{4}-\d{2}-\d{2}\.zip$/)

  await page.goto(`/app/tenants/${tenant.id}`)
  await expect(page.getByRole('heading', { name: 'Ngozi Nwosu' })).toBeVisible()
  await downloadZip(page, 'Download their data', /^tenant-ngozi-nwosu-\d{4}-\d{2}-\d{2}\.zip$/)

  // Both downloads are in the audit log.
  await page.goto('/app/audit')
  await expect(page.getByText('data.exported').first()).toBeVisible()
  await expect(page.getByText('person.exported').first()).toBeVisible()
})
