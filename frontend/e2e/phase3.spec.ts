import { expect, test, type Page } from '@playwright/test'

const password = 'correct-horse-42'
const unique = () => `${Date.now()}${Math.floor(Math.random() * 1000)}`

async function openMenuIfCollapsed(page: Page) {
  const menu = page.getByRole('button', { name: 'Menu' })
  if (await menu.isVisible()) await menu.click()
}

async function goTo(page: Page, link: string) {
  await openMenuIfCollapsed(page)
  await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: link }).click()
}

async function expectNoHorizontalScroll(page: Page) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
  expect(overflow).toBeLessThanOrEqual(0)
}

test('a company adds a property with a unit and an owner, and a tenant with a guarantor', async ({ page }) => {
  const id = unique()
  const code = `LKK-${id.slice(-6)}`

  // Sign up.
  await page.goto('/register')
  await page.getByLabel('Full name', { exact: true }).fill('Seun Adelaja')
  await page.getByLabel('Email', { exact: true }).fill(`admin-${id}@example.com`)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByLabel('Company or business name', { exact: true }).fill(`Lekki Homes ${id}`)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByRole('heading', { name: 'Welcome, Seun' })).toBeVisible()

  // A property.
  await goTo(page, 'Properties')
  await expect(page.getByText('No properties yet.')).toBeVisible()
  await page.getByRole('button', { name: 'Add property' }).click()
  await page.getByLabel('Code', { exact: true }).fill(code)
  await page.getByLabel('Name', { exact: true }).fill('Palm Court')
  await page.getByLabel('Address', { exact: true }).fill('12 Admiralty Way')
  await page.getByLabel('City', { exact: true }).fill('Lekki')
  await page.getByLabel('State', { exact: true }).selectOption('Lagos')
  await page.getByLabel('LGA', { exact: true }).fill('Eti-Osa')
  await page.getByLabel('Amenities', { exact: true }).fill('Borehole, generator')
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByRole('heading', { name: 'Palm Court' })).toBeVisible()
  await expect(page.getByText(`${code.toUpperCase()} · 12 Admiralty Way, Lekki, Lagos`)).toBeVisible()

  // A unit.
  await page.getByRole('button', { name: 'Add unit' }).click()
  await page.getByLabel('Unit code', { exact: true }).fill('A1')
  await page.getByLabel('Bedrooms', { exact: true }).fill('3')
  await page.getByLabel('Bathrooms', { exact: true }).fill('3')
  await page.getByLabel('Default rent (₦)', { exact: true }).fill('2500000')
  await page.getByRole('button', { name: 'Save unit' }).click()
  const unitRow = page.getByRole('row').filter({ hasText: 'A1' })
  await expect(unitRow).toContainText('₦2,500,000.00')
  await expect(unitRow).toContainText('Vacant')
  await expectNoHorizontalScroll(page)

  // An owner.
  await goTo(page, 'Owners')
  await page.getByRole('button', { name: 'Add owner' }).click()
  await page.getByLabel('Name', { exact: true }).fill('Chief Okafor')
  await page.getByLabel('Email', { exact: true }).fill(`okafor-${id}@example.com`)
  await page.getByLabel('Phone', { exact: true }).fill('08031234567')
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByRole('heading', { name: 'Chief Okafor' })).toBeVisible()

  // The owner holds 100% of the property.
  await goTo(page, 'Properties')
  await page.getByRole('link', { name: /Palm Court/ }).click()
  await page.getByRole('button', { name: 'Set owners' }).click()
  await page.getByLabel('Owner 1', { exact: true }).selectOption({ label: 'Chief Okafor' })
  await page.getByLabel('Share %', { exact: true }).fill('60')
  await expect(page.getByText('Total: 60%')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Save owners' })).toBeDisabled()
  await page.getByLabel('Share %', { exact: true }).fill('100')
  await expect(page.getByText('Total: 100%')).toBeVisible()
  await page.getByRole('button', { name: 'Save owners' }).click()
  const owners = page.locator('section').filter({ has: page.getByRole('heading', { name: 'Owners' }) })
  await expect(owners.getByRole('link', { name: 'Chief Okafor' })).toBeVisible()
  await expect(owners).toContainText('100%')

  // The owner's page lists the property.
  await owners.getByRole('link', { name: 'Chief Okafor' }).click()
  await expect(page.getByRole('link', { name: 'Palm Court' })).toBeVisible()

  // A tenant with a guarantor.
  await goTo(page, 'Tenants')
  await page.getByRole('button', { name: 'Add tenant' }).click()
  await page.getByLabel('Full name', { exact: true }).fill('Ngozi Eze')
  await page.getByLabel('Email', { exact: true }).fill(`ngozi-${id}@example.com`)
  await page.getByLabel('Phone', { exact: true }).fill('08098765432')
  await page.getByLabel('Employer', { exact: true }).fill('Dangote Group')
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByRole('heading', { name: 'Ngozi Eze' })).toBeVisible()
  await expect(page.getByText('Not recorded')).toBeVisible()

  await page.getByRole('button', { name: 'Add guarantor' }).click()
  await page.getByLabel('Full name', { exact: true }).fill('Emeka Eze')
  await page.getByLabel('Relationship', { exact: true }).fill('Brother')
  await page.getByLabel('Phone', { exact: true }).fill('08055555555')
  await page.getByLabel('Signed guarantor form received', { exact: true }).check()
  await page.getByRole('button', { name: 'Save guarantor' }).click()
  const guarantor = page.getByRole('listitem').filter({ hasText: 'Emeka Eze' })
  await expect(guarantor).toContainText('Brother')
  await expect(guarantor).toContainText('Signed form received')
  await expectNoHorizontalScroll(page)

  // The tenant list shows the guarantor count.
  await goTo(page, 'Tenants')
  await expect(page.getByRole('link', { name: /Ngozi Eze/ })).toContainText('1 guarantor')
})
