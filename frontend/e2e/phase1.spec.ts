import { expect, test, type Page } from '@playwright/test'

const password = 'correct-horse-42'
const unique = () => `${Date.now()}${Math.floor(Math.random() * 1000)}`

async function openMenuIfCollapsed(page: Page) {
  const menu = page.getByRole('button', { name: 'Menu' })
  if (await menu.isVisible()) await menu.click()
}

test('a company signs up, adds a branch, invites a manager limited to that branch, and removes them', async ({ page, browser }) => {
  const id = unique()
  const adminEmail = `admin-${id}@example.com`
  const managerEmail = `manager-${id}@example.com`

  // Sign up and land on the dashboard.
  await page.goto('/register')
  await page.getByLabel('Full name').fill('Seun Adelaja')
  await page.getByLabel('Email').fill(adminEmail)
  await page.getByLabel('Password').fill(password)
  await page.getByLabel('Company or business name').fill(`Lekki Homes ${id}`)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByRole('heading', { name: 'Welcome, Seun' })).toBeVisible()

  // Two branches.
  await openMenuIfCollapsed(page)
  await page.getByRole('link', { name: 'Branches' }).click()
  for (const name of ['Ikeja', 'Lekki']) {
    await page.getByRole('button', { name: 'Add branch' }).click()
    await page.getByLabel('Name').fill(name)
    await page.getByLabel('Address').fill(`${name}, Lagos`)
    await page.getByRole('button', { name: 'Save' }).click()
    await expect(page.getByText(`${name}, Lagos`)).toBeVisible()
  }

  // Invite a property manager limited to Lekki.
  await openMenuIfCollapsed(page)
  await page.getByRole('link', { name: 'Team' }).click()
  await page.getByRole('button', { name: 'Invite someone' }).click()
  await page.getByLabel('Email').fill(managerEmail)
  await page.getByLabel('Role', { exact: true }).selectOption('PropertyManager')
  await page.getByLabel('Lekki').check()
  await page.getByRole('button', { name: 'Create invitation' }).click()
  const link = await page.getByLabel('Invitation link').inputValue()
  await page.getByRole('button', { name: 'Done' }).click()
  await expect(page.getByText(managerEmail)).toBeVisible()

  // The manager opens the link in their own browser and creates a login.
  const managerContext = await browser.newContext({ ...test.info().project.use })
  const manager = await managerContext.newPage()
  await manager.goto(link)
  await manager.getByRole('button', { name: 'Create a login' }).click()
  await manager.getByLabel('Full name').fill('Ada Manager')
  await manager.getByLabel('Password').fill(password)
  await manager.getByRole('button', { name: 'Create login and join' }).click()
  await expect(manager.getByRole('heading', { name: 'Welcome, Ada' })).toBeVisible()

  // Limited to Lekki, and no Team or Audit pages.
  await openMenuIfCollapsed(manager)
  const nav = manager.getByRole('navigation', { name: 'Main' })
  await expect(nav).not.toContainText('Team')
  await expect(nav).not.toContainText('Audit log')
  await nav.getByRole('link', { name: 'Branches' }).click()
  await expect(manager.getByText('Lekki, Lagos')).toBeVisible()
  await expect(manager.getByText('Ikeja, Lagos')).toHaveCount(0)

  // The admin removes the manager, whose very next request is refused.
  await page.reload()
  await expect(page.getByText('Ada Manager', { exact: true })).toBeVisible()
  page.once('dialog', (d) => d.accept())
  await page.getByRole('listitem').filter({ hasText: 'Ada Manager' }).getByRole('button', { name: 'Remove' }).click()
  await expect(page.getByText('Ada Manager', { exact: true })).toHaveCount(0)

  const refused = await manager.evaluate(async () => {
    const res = await fetch('/api/v1/branches', {
      headers: { 'X-Organization-Id': localStorage.getItem('pms.organizationId') ?? '' },
    })
    return res.status
  })
  expect(refused).toBe(403)

  // The audit log recorded it.
  await openMenuIfCollapsed(page)
  await page.getByRole('link', { name: 'Audit log' }).click()
  await expect(page.getByText('member.revoked')).toBeVisible()
  await managerContext.close()
})
