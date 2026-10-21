import { execFileSync } from 'node:child_process'
import { expect, test, type Page } from '@playwright/test'
import { totp } from './totp'

const password = 'correct-horse-42'
const unique = () => `${Date.now()}${Math.floor(Math.random() * 1000)}`

/** Makes a user platform staff, as phase11.spec.ts does: set in the database on purpose. */
function makePlatformStaff(email: string) {
  const sql = `SET QUOTED_IDENTIFIER ON; UPDATE AspNetUsers SET IsPlatformAdmin = 1 WHERE NormalizedEmail = '${email.toUpperCase()}'`
  const command = process.env.PMS_E2E_SQLCMD?.split(' ') ?? [
    'docker',
    'exec',
    'mssql',
    '/opt/mssql-tools18/bin/sqlcmd',
    '-C',
    '-S',
    'localhost',
    '-U',
    'sa',
    '-P',
    'Dev_Passw0rd!',
    '-d',
    'PropertyManagement',
    '-b',
    '-Q',
  ]
  execFileSync(command[0], [...command.slice(1), sql])
}

async function register(page: Page, email: string, company: string) {
  await page.goto('/register')
  await page.getByLabel('Full name', { exact: true }).fill(company === 'Platform HQ' ? 'Tobi Staff' : 'Seun Adelaja')
  await page.getByLabel('Email', { exact: true }).fill(email)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByLabel('Company or business name', { exact: true }).fill(company)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByRole('heading', { name: /^Welcome,/ })).toBeVisible()
}

async function api(page: Page, path: string, body: unknown = {}) {
  const organizationId = await page.evaluate(() => localStorage.getItem('pms.organizationId'))
  const response = await page.request.post(path, {
    data: body,
    headers: { 'X-Requested-With': 'XMLHttpRequest', 'X-Organization-Id': organizationId ?? '' },
  })
  expect(response.ok(), await response.text()).toBeTruthy()
  return response.json()
}

test('platform staff make a company read-only and then cancel it, leaving only the data download', async ({ browser }) => {
  const id = unique()
  const company = `Festac Homes ${id}`
  const admin = await (await browser.newContext()).newPage()
  await register(admin, `owner-${id}@example.com`, company)

  // The company sees its plan and how much of it is used.
  await admin.goto('/app/settings')
  await expect(admin.getByText('Free trial', { exact: true })).toBeVisible()
  await expect(admin.getByRole('progressbar', { name: 'Team members (including pending invitations)' })).toHaveAttribute(
    'aria-valuenow',
    '1',
  )

  const staff = await (await browser.newContext()).newPage()
  const staffEmail = `platform-${id}@example.com`
  await register(staff, staffEmail, 'Platform HQ')
  makePlatformStaff(staffEmail)
  const setup = await api(staff, '/api/v1/auth/two-factor/setup')
  await api(staff, '/api/v1/auth/two-factor/enable', { code: totp(setup.sharedKey) })

  await staff.reload()
  await staff.getByRole('link', { name: 'Companies' }).click()
  await staff.getByLabel('Search companies').fill(company)
  await staff.getByRole('button', { name: `Manage ${company}` }).click()
  await staff.getByLabel('Reason (goes in their audit log)').fill('Unpaid since March')
  await staff.getByRole('button', { name: 'Make read-only' }).click()
  await expect(staff.getByText('Suspended (read-only)')).toBeVisible()

  await admin.reload()
  await expect(admin.getByRole('status').filter({ hasText: 'is read-only because its subscription is suspended' })).toBeVisible()

  await staff.getByLabel('Reason (goes in their audit log)').fill('Customer asked to close the account')
  staff.once('dialog', (d) => d.accept())
  await staff.getByRole('button', { name: 'Cancel subscription' }).click()
  await expect(staff.getByText(/export open until/)).toBeVisible()

  await admin.goto('/app')
  await expect(admin.getByRole('heading', { name: "This company's subscription has ended" })).toBeVisible()
  const downloading = admin.waitForEvent('download')
  await admin.getByRole('button', { name: 'Download records', exact: true }).click()
  expect((await downloading).suggestedFilename()).toMatch(/\.zip$/)
})
