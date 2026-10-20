import { execFileSync } from 'node:child_process'
import { expect, test, type Page } from '@playwright/test'
import { totp } from './totp'

const password = 'correct-horse-42'
const unique = () => `${Date.now()}${Math.floor(Math.random() * 1000)}`

/**
 * Makes a user platform support. There is no screen for this on purpose: it is set in the database. Locally that is
 * the SQL Server container from the README; set PMS_E2E_SQLCMD to run the statement another way.
 */
function makePlatformSupport(email: string) {
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
  await page.getByLabel('Full name', { exact: true }).fill(company === 'Platform HQ' ? 'Tobi Support' : 'Seun Adelaja')
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

test('support asks for access, the admin allows it, support reads and the admin ends it', async ({ browser }) => {
  const id = unique()
  const company = `Banana Island Homes ${id}`
  const admin = await (await browser.newContext()).newPage()
  await register(admin, `owner-${id}@example.com`, company)
  await admin.goto('/app/settings')
  const slug = await admin.getByLabel('Web address name').inputValue()

  const support = await (await browser.newContext()).newPage()
  const supportEmail = `support-${id}@example.com`
  await register(support, supportEmail, 'Platform HQ')
  makePlatformSupport(supportEmail)
  const setup = await api(support, '/api/v1/auth/two-factor/setup')
  await api(support, '/api/v1/auth/two-factor/enable', { code: totp(setup.sharedKey) })

  // Support asks from the support desk.
  await support.reload()
  await support.getByRole('link', { name: 'Support desk' }).click()
  await support.getByLabel("Company's web address name").fill(slug)
  await support.getByLabel('For how long').selectOption('2')
  await support.getByLabel('Why you need it').fill('Ticket 4411: units missing after import')
  await support.getByRole('button', { name: 'Send request' }).click()
  await expect(support.getByText('Sent. Their admins have been emailed.')).toBeVisible()
  await expect(support.getByText('Pending')).toBeVisible()

  // The admin allows it from the Team page.
  await admin.goto('/app/team')
  await expect(admin.getByText('Ticket 4411: units missing after import')).toBeVisible()
  await admin.getByRole('button', { name: 'Allow for 2 hours' }).click()
  await expect(admin.getByRole('button', { name: 'End access now' })).toBeVisible()

  // Support opens the company read-only.
  await support.reload()
  await support.getByRole('button', { name: 'Open' }).click()
  await expect(support.getByRole('status').filter({ hasText: `Support access to ${company}: read-only` })).toBeVisible()
  await support.goto('/app/properties')
  await expect(support.getByRole('heading', { name: 'Properties' })).toBeVisible()
  await expect(support.getByRole('button', { name: 'Add property' })).toHaveCount(0)

  // The admin ends it, and the company leaves support's list.
  await admin.getByRole('button', { name: 'End access now' }).click()
  await expect(admin.getByText('Revoked')).toBeVisible()
  await support.goto('/app/support-desk')
  await expect(support.getByText('Revoked')).toBeVisible()
  await expect(support.getByLabel('Organization')).not.toContainText(company)
})
