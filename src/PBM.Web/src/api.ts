import axios from 'axios'

export const api = axios.create({ baseURL: '/api/v1', timeout: 15000 })

const authStorageKeys = [
  'pbm_token',
  'pbm_display_name',
  'pbm_roles',
  'pbm_writable_company_ids'
] as const

const accessTokenCookieName = 'pbm_access_token'
let handlingUnauthorized = false

function syncAccessTokenCookie(token: string | null) {
  const secure = window.location.protocol === 'https:' ? '; Secure' : ''
  if (token) {
    document.cookie = `${accessTokenCookieName}=${encodeURIComponent(token)}; Path=/; Max-Age=28800; SameSite=Strict${secure}`
  } else {
    document.cookie = `${accessTokenCookieName}=; Path=/; Max-Age=0; SameSite=Strict${secure}`
  }
}

export function setAccessToken(token: string | null) {
  if (token) api.defaults.headers.common.Authorization = `Bearer ${token}`
  else delete api.defaults.headers.common.Authorization
  syncAccessTokenCookie(token)
}

export function clearClientSession() {
  authStorageKeys.forEach(key => localStorage.removeItem(key))
  setAccessToken(null)
}

// Always attach the latest stored token immediately before dispatch. Axios 1.x
// represents request headers as AxiosHeaders, so use its set() API when present.
// The same token is mirrored to a same-site cookie as a browser fallback; nginx
// converts that cookie back to Authorization if a browser omits the header.
api.interceptors.request.use(config => {
  const token = localStorage.getItem('pbm_token')
  if (token) {
    syncAccessTokenCookie(token)
    const headers = config.headers
    if (headers && typeof headers.set === 'function') headers.set('Authorization', `Bearer ${token}`)
    else if (headers) headers.Authorization = `Bearer ${token}`
  }
  return config
})

// Restore authentication synchronously on a hard refresh before React mounts.
const storedToken = localStorage.getItem('pbm_token')
if (storedToken) setAccessToken(storedToken)

api.interceptors.response.use(
  response => response,
  error => {
    const status = error?.response?.status
    const requestUrl = String(error?.config?.url ?? '')
    const isLoginRequest = requestUrl.includes('/auth/login')
    const hadAuthenticatedSession = Boolean(localStorage.getItem('pbm_token'))

    if (status === 401 && !isLoginRequest && hadAuthenticatedSession && !handlingUnauthorized) {
      handlingUnauthorized = true
      clearClientSession()
      window.location.reload()
    }

    return Promise.reject(error)
  }
)
