import { expect, test, type Page } from '@playwright/test'

const password = 'correct-horse-42'
const unique = () => `${Date.now()}${Math.floor(Math.random() * 1000)}`
// A 1x1 PNG.
const png = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=',
  'base64',
)

async function api(page: Page, path: string, body: unknown) {
  const organizationId = await page.evaluate(() => localStorage.getItem('pms.organizationId'))
  const response = await page.request.post(path, {
    data: body,
    headers: { 'X-Requested-With': 'XMLHttpRequest', 'X-Organization-Id': organizationId ?? '' },
  })
  expect(response.ok(), await response.text()).toBeTruthy()
  return response.json()
}

/** Tomorrow at 10:00 as a date-time input value. */
function tomorrowAtTen() {
  const d = new Date(Date.now() + 24 * 60 * 60 * 1000)
  return `${d.toISOString().slice(0, 10)}T10:00`
}

test('staff advertise a vacant unit, the public enquires, and staff book a viewing', async ({ page, browser }) => {
  const id = unique()
  await page.goto('/register')
  await page.getByLabel('Full name', { exact: true }).fill('Seun Adelaja')
  await page.getByLabel('Email', { exact: true }).fill(`listings-${id}@example.com`)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByLabel('Company or business name', { exact: true }).fill(`Lekki Lettings ${id}`)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByRole('heading', { name: 'Welcome, Seun' })).toBeVisible()

  const property = await api(page, '/api/v1/properties', {
    code: `LK-${id.slice(-6)}`, name: 'Admiralty Gardens', type: 'Residential', addressLine: '4 Admiralty Way',
    city: 'Lekki', state: 'Lagos',
  })
  await api(page, `/api/v1/properties/${property.id}/units`, {
    code: 'B2', type: 'Flat', bedrooms: 2, bathrooms: 2, defaultRent: 4500000, billingFrequency: 'Annual', currency: 'NGN',
  })

  // A photo goes on the property, then the vacant unit is advertised from the unit list.
  await page.goto(`/app/properties/${property.id}`)
  const files = page.locator('section').filter({ has: page.getByRole('heading', { name: 'Documents and photos' }) })
  await files.getByRole('button', { name: 'Add file' }).click()
  await files.getByLabel('File').setInputFiles({ name: 'living-room.png', mimeType: 'image/png', buffer: png })
  await files.getByLabel('Title').fill('Living room')
  await files.getByRole('button', { name: 'Upload' }).click()
  await expect(files.getByRole('img', { name: 'Living room' })).toBeVisible()

  await page.getByRole('link', { name: 'Advertise unit B2' }).click()
  await expect(page.getByRole('heading', { name: 'New listing' })).toBeVisible()
  await expect(page.getByLabel('Headline')).toHaveValue('2-bedroom flat · 2 bathrooms')
  await expect(page.getByLabel('Asking rent (₦)')).toHaveValue('4500000')
  await page.getByLabel('Headline').fill('Bright 2-bedroom flat in Lekki Phase 1')
  await page.getByLabel('Features').fill('Prepaid meter\n24-hour security')
  await page.getByLabel('Contact phone on the listing').fill('0803 123 4567')
  await page.getByRole('button', { name: 'Create listing' }).click()

  // It can't go live without a description and a photo.
  await expect(page.getByRole('heading', { name: 'Bright 2-bedroom flat in Lekki Phase 1' })).toBeVisible()
  await page.getByRole('button', { name: 'Publish' }).click()
  await expect(page.getByRole('alert')).toContainText('description')
  await page.getByRole('button', { name: 'Edit' }).click()
  await page.getByLabel('Description').fill('Newly renovated flat, five minutes from the expressway.')
  await page.getByRole('button', { name: 'Save changes' }).click()
  await page.getByRole('button', { name: 'Choose photos' }).click()
  await page.getByRole('checkbox', { name: 'Use Living room' }).check()
  await page.getByRole('button', { name: 'Save photos' }).click()
  await expect(page.getByRole('img', { name: 'Living room' })).toBeVisible()
  await page.getByRole('button', { name: 'Publish' }).click()
  const publicLink = page.getByRole('link', { name: /\/listing\// })
  await expect(publicLink).toBeVisible()
  const listingPath = new URL((await publicLink.textContent())!).pathname

  // Someone not signed in finds it, sees only public details, and sends an enquiry.
  const visitor = await (await browser.newContext({ ...test.info().project.use })).newPage()
  await visitor.goto(listingPath)
  await expect(visitor.getByRole('heading', { name: 'Bright 2-bedroom flat in Lekki Phase 1' })).toBeVisible()
  await expect(visitor.getByText('₦4,500,000 per year')).toBeVisible()
  await expect(visitor.getByText('Lekki, Lagos')).toBeVisible()
  await expect(visitor.getByText('4 Admiralty Way')).toHaveCount(0)
  await expect(visitor.getByText('✓ Prepaid meter')).toBeVisible()
  const cover = visitor.getByRole('img', { name: /Photo 1 of/ })
  await expect.poll(() => cover.evaluate((img: HTMLImageElement) => img.complete && img.naturalWidth)).toBeGreaterThan(0)
  await expect(visitor.getByRole('link', { name: '0803 123 4567' })).toBeVisible()
  await visitor.getByRole('button', { name: 'Send enquiry' }).click()
  await expect(visitor.getByText('Enter a phone number or email so we can reply.')).toBeVisible()
  await visitor.getByLabel('Your name').fill('Kemi Adeyemi')
  await visitor.getByLabel('Phone').fill('08099998888')
  await visitor.getByLabel('Message').fill('Can I view it on Saturday?')
  await visitor.getByRole('button', { name: 'Send enquiry' }).click()
  await expect(visitor.getByText('Thank you, your enquiry has been sent.')).toBeVisible()
  await visitor.getByRole('link', { name: 'All properties' }).click()
  await expect(visitor.getByRole('heading', { name: 'Properties to let' })).toBeVisible()
  await expect(visitor.getByRole('link', { name: /Bright 2-bedroom flat/ })).toBeVisible()

  // Staff see the enquiry, book a viewing and move it on.
  await page.reload()
  const enquiries = page.locator('section').filter({ has: page.getByRole('heading', { name: 'Enquiries' }) })
  await enquiries.getByRole('link', { name: /Kemi Adeyemi/ }).click()
  await expect(page.getByText('Can I view it on Saturday?')).toBeVisible()
  await page.getByRole('button', { name: 'Book a viewing' }).click()
  await page.getByLabel('Date and time (Lagos)').fill(tomorrowAtTen())
  await page.getByLabel('Viewing notes').fill('Meet at the gate')
  await page.getByRole('button', { name: 'Book viewing' }).click()
  const viewings = page.locator('section').filter({ has: page.getByRole('heading', { name: 'Viewings' }) })
  await expect(viewings.getByText('Meet at the gate')).toBeVisible()
  await expect(page.getByText('Viewing booked').first()).toBeVisible()
  await expect(page.getByLabel('Move to')).toHaveValue('ViewingBooked')
  await viewings.getByRole('button', { name: 'Record outcome' }).click()
  await viewings.getByLabel('How it went').selectOption('Cancelled')
  await viewings.getByLabel('Outcome notes').fill('Moved to Sunday')
  await viewings.getByRole('button', { name: 'Save outcome' }).click()
  await expect(viewings.getByText('Moved to Sunday')).toBeVisible()

  // The enquiries list shows it too.
  await page.goto('/app/listings?tab=enquiries')
  await expect(page.getByRole('link', { name: /Kemi Adeyemi/ })).toBeVisible()

  // Withdrawing the listing takes it off the public site.
  await page.goto('/app/listings')
  await page.getByRole('link', { name: /Bright 2-bedroom flat/ }).click()
  await page.getByRole('button', { name: 'Withdraw' }).click()
  await expect(page.getByText('Closed · Withdrawn')).toBeVisible()
  await visitor.goto(listingPath)
  await expect(visitor.getByRole('heading', { name: "This listing isn't available" })).toBeVisible()
})
