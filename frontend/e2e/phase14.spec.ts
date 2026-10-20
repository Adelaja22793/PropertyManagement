import { expect, test, type Browser } from '@playwright/test'

const password = 'correct-horse-42'
const unique = () => `${Date.now()}${Math.floor(Math.random() * 1000)}`

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

test('an admin makes a Front desk role and a new member gets exactly that access', async ({ page, browser }) => {
  const id = unique()
  await page.goto('/register')
  await page.getByLabel('Full name', { exact: true }).fill('Seun Adelaja')
  await page.getByLabel('Email', { exact: true }).fill(`roles-${id}@example.com`)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByLabel('Company or business name', { exact: true }).fill(`Yaba Homes ${id}`)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByRole('heading', { name: 'Welcome, Seun' })).toBeVisible()

  // Make the role from the permission list.
  await page.goto('/app/roles')
  await expect(page.getByRole('heading', { name: 'Roles', exact: true })).toBeVisible()
  await expect(page.getByText('Organization admin')).toBeVisible()
  await page.getByRole('button', { name: 'New role' }).click()
  await page.getByLabel('Role name').fill('Front desk')
  await page.getByLabel('Description (optional)').fill('Welcomes new tenants')
  await page.getByLabel('See tenants').check()
  await page.getByLabel('Add tenants').check()
  await page.getByLabel('See identity numbers (NIN, BVN)').check()
  await expect(page.getByRole('status').filter({ hasText: 'must turn on two-step sign-in' })).toBeVisible()
  await page.getByLabel('See identity numbers (NIN, BVN)').uncheck()
  await page.getByRole('button', { name: 'Create role' }).click()
  await expect(page.getByText('Welcomes new tenants')).toBeVisible()
  await expect(page.getByText('0 members · 3 permissions')).toBeVisible()

  // Invite someone into it from the Team page.
  await page.goto('/app/team')
  await page.getByRole('button', { name: 'Invite someone' }).click()
  await page.getByLabel('Email').fill(`desk-${id}@example.com`)
  await page.getByLabel('Role', { exact: true }).selectOption({ label: 'Front desk' })
  await page.getByRole('button', { name: 'Create invitation' }).click()
  const link = await page.getByLabel('Invitation link').inputValue()
  await page.getByRole('button', { name: 'Done' }).click()
  await expect(page.getByText(/Front desk · sent/)).toBeVisible()

  // The new member sees tenants, and nothing the role doesn't allow.
  const desk = await join(browser, link, 'Bisi Front')
  await expect(desk.getByRole('heading', { name: 'Welcome, Bisi' })).toBeVisible()
  await expect(desk.getByText(/Front desk/)).toBeVisible()
  await desk.goto('/app/tenants')
  await expect(desk.getByRole('heading', { name: 'Tenants', exact: true })).toBeVisible()
  await desk.goto('/app/leases')
  await expect(desk.getByRole('heading', { name: 'Leases', exact: true })).toHaveCount(0)

  // The role now has a member, so it can't be deleted.
  await page.goto('/app/roles')
  await expect(page.getByText('1 member · 3 permissions')).toBeVisible()
  page.once('dialog', (d) => d.accept())
  await page.getByRole('button', { name: 'Delete Front desk' }).click()
  await expect(page.getByRole('alert')).toContainText('Give the members with this role another role first.')
})
