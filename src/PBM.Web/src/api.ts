import axios from 'axios'

export const api = axios.create({ baseURL: '/api/v1', timeout: 15000 })

const authStorageKeys = [
  'pbm_token',
  'pbm_display_name',
  'pbm_roles',
  'pbm_writable_company_ids'
] as const

let handlingUnauthorized = false

export function setAccessToken(token: string | null) {
  if (token) api.defaults.headers.common.Authorization = `Bearer ${token}`
  else delete api.defaults.headers.common.Authorization
}

export function clearClientSession() {
  authStorageKeys.forEach(key => localStorage.removeItem(key))
  setAccessToken(null)
}

// Always attach the latest stored token immediately before dispatch. Axios 1.x
// represents request headers as AxiosHeaders, so use its set() API when present
// rather than relying only on plain-object property assignment.
api.interceptors.request.use(config => {
  const token = localStorage.getItem('pbm_token')
  if (token) {
    const headers = config.headers
    if (headers && typeof headers.set === 'function') headers.set('Authorization', `Bearer ${token}`)
    else if (headers) headers.Authorization = `Bearer ${token}`
  }
  return config
})

// Restore the default header synchronously on a hard refresh before React mounts.
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
