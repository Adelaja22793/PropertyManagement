import { describe, expect, it } from 'vitest'
import { Permissions, permissionLabels, roleLabel } from './permissions'

describe('roleLabel', () => {
  it('names built-in roles and uses the organization’s names for its own', () => {
    expect(roleLabel('BranchManager')).toBe('Branch manager')
    expect(roleLabel('custom:1f0c', { 'custom:1f0c': 'Front desk' })).toBe('Front desk')
    expect(roleLabel('custom:unknown')).toBe('Custom role')
  })
})

describe('permissionLabels', () => {
  it('has a plain description for every permission the screens use', () => {
    for (const code of Object.values(Permissions)) expect(permissionLabels[code]).toBeTruthy()
  })
})
