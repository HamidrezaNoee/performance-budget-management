export type AuthSession = {
  accessToken: string
  displayName: string
  roles: string[]
  writableCompanyIds: string[]
}

const keys = {
  accessToken: 'pbm_token',
  displayName: 'pbm_display_name',
  roles: 'pbm_roles',
  writableCompanyIds: 'pbm_writable_company_ids'
} as const

function readArray(storage: Storage, key: string): string[] {
  try {
    const value = JSON.parse(storage.getItem(key) ?? '[]')
    return Array.isArray(value) && value.every(item => typeof item === 'string') ? value : []
  } catch {
    return []
  }
}

function readFrom(storage: Storage): AuthSession | null {
  const accessToken = storage.getItem(keys.accessToken)
  if (!accessToken) return null

  return {
    accessToken,
    displayName: storage.getItem(keys.displayName) ?? '',
    roles: readArray(storage, keys.roles),
    writableCompanyIds: readArray(storage, keys.writableCompanyIds)
  }
}

function clearFrom(storage: Storage) {
  Object.values(keys).forEach(key => storage.removeItem(key))
}

export function readAuthSession(): AuthSession | null {
  const session = readFrom(sessionStorage)
  if (session) return session

  // One-time compatibility migration from older PBM builds. Authentication
  // data is no longer kept across browser restarts in localStorage.
  const legacySession = readFrom(localStorage)
  if (!legacySession) return null
  saveAuthSession(legacySession)
  clearFrom(localStorage)
  return legacySession
}

export function saveAuthSession(session: AuthSession) {
  sessionStorage.setItem(keys.accessToken, session.accessToken)
  sessionStorage.setItem(keys.displayName, session.displayName)
  sessionStorage.setItem(keys.roles, JSON.stringify(session.roles))
  sessionStorage.setItem(keys.writableCompanyIds, JSON.stringify(session.writableCompanyIds))
  clearFrom(localStorage)
}

export function clearAuthSession() {
  clearFrom(sessionStorage)
  clearFrom(localStorage)
}

export function getStoredAccessToken() {
  return sessionStorage.getItem(keys.accessToken) ?? localStorage.getItem(keys.accessToken)
}
