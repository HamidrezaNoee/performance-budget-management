import axios from 'axios'

export const api = axios.create({ baseURL: '/api/v1', timeout: 15000 })

export function setAccessToken(token: string | null) {
  if (token) api.defaults.headers.common.Authorization = `Bearer ${token}`
  else delete api.defaults.headers.common.Authorization
}
