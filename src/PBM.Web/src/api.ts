import axios from 'axios'
import { clearAuthSession, getStoredAccessToken } from './authSession'

export const api = axios.create({ baseURL: '/api/v1', timeout: 15000 })

let handlingUnauthorized = false

export function setAccessToken(token: string | null) {
  if (token) api.defaults.headers.common.Authorization = `Bearer ${token}`
  else delete api.defaults.headers.common.Authorization
}

export function clearClientSession() {
  clearAuthSession()
  setAccessToken(null)
}

// Always attach the latest stored token immediately before dispatch. Axios 1.x
// represents request headers as AxiosHeaders, so use its set() API when present.
api.interceptors.request.use(config => {
  const token = getStoredAccessToken()
  if (token) {
    const headers = config.headers
    if (headers && typeof headers.set === 'function') headers.set('Authorization', `Bearer ${token}`)
    else if (headers) headers.Authorization = `Bearer ${token}`
  }
  return config
})

// Restore authentication synchronously on a hard refresh before React mounts.
const storedToken = getStoredAccessToken()
if (storedToken) setAccessToken(storedToken)

api.interceptors.response.use(
  response => response,
  error => {
    const status = error?.response?.status
    const requestUrl = String(error?.config?.url ?? '')
    const isLoginRequest = requestUrl.includes('/auth/login')
    const hadAuthenticatedSession = Boolean(getStoredAccessToken())

    if (status === 401 && !isLoginRequest && hadAuthenticatedSession && !handlingUnauthorized) {
      handlingUnauthorized = true
      clearClientSession()
      window.location.reload()
    }

    return Promise.reject(error)
  }
)
