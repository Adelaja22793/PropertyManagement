import { expect, test } from '@playwright/test'
import { totp } from './totp'

const password = 'correct-horse-42'
const unique = () => `${Date.now()}${Math.floor(Math.random() * 1000)}`

test('an admin turns on two-step sign-in and then needs a code to sign in', async ({ page }) => {
  const id = unique()
  const email = `secure-${id}@example.com`
  await page.goto('/register')
  await page.getByLabel('Full name', { exact: true }).fill('Seun Adelaja')
  await page.getByLabel('Email', { exact: true }).fill(email)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByLabel('Company or business name', { exact: true }).fill(`Ikoyi Homes ${id}`)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByRole('heading', { name: 'Welcome, Seun' })).toBeVisible()

  // An admin is reminded to turn it on before the grace period ends.
  await expect(page.getByRole('status').filter({ hasText: 'Your role needs two-step sign-in' })).toBeVisible()
  await page.getByRole('link', { name: 'Security' }).click()
  await expect(page.getByRole('heading', { name: 'Security' })).toBeVisible()
  await page.getByRole('button', { name: 'Set up two-step sign-in' }).click()
  await expect(page.getByRole('img', { name: 'QR code for your authenticator app' })).toBeVisible()
  const key = (await page.getByTestId('shared-key').textContent())!.trim()

  await page.getByLabel('Code from the app').fill('000000' === totp(key) ? '111111' : '000000')
  await page.getByRole('button', { name: 'Turn on' }).click()
  await expect(page.getByRole('alert')).toBeVisible()
  await page.getByLabel('Code from the app').fill(totp(key))
  await page.getByRole('button', { name: 'Turn on' }).click()

  // Recovery codes are shown once.
  const codes = page.getByRole('list', { name: 'Recovery codes' }).getByRole('listitem')
  await expect(codes).toHaveCount(10)
  const recovery = (await codes.first().textContent())!.trim()
  const downloading = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Download' }).click()
  expect((await downloading).suggestedFilename()).toBe('recovery-codes.txt')
  await page.getByRole('button', { name: "I've saved them" }).click()
  await expect(page.getByText('10 recovery codes')).toBeVisible()
  await expect(page.getByText("can't be turned off")).toBeVisible()
  await expect(page.getByRole('status').filter({ hasText: 'Your role needs two-step sign-in' })).toHaveCount(0)

  // Signing in now takes the password and then a code.
  await page.getByRole('button', { name: 'Sign out' }).click()
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Password').fill(password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page.getByRole('heading', { name: 'Two-step sign-in' })).toBeVisible()
  await page.getByLabel('Code', { exact: true }).fill(totp(key))
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page.getByRole('heading', { name: 'Welcome, Seun' })).toBeVisible()

  // A lost phone: a recovery code works instead.
  await page.getByRole('button', { name: 'Sign out' }).click()
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Password').fill(password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await page.getByRole('button', { name: 'Lost your phone? Use a recovery code' }).click()
  await page.getByLabel('Recovery code').fill(recovery)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page.getByRole('heading', { name: 'Welcome, Seun' })).toBeVisible()
  await page.getByRole('link', { name: 'Security' }).click()
  await expect(page.getByText('9 recovery codes')).toBeVisible()
})
