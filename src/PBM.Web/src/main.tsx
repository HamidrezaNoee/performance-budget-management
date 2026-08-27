import React from 'react'
import ReactDOM from 'react-dom/client'
import { CssBaseline, ThemeProvider, createTheme } from '@mui/material'
import App from './App'
import './styles.css'

document.documentElement.dir = 'rtl'
document.documentElement.lang = 'fa'

const theme = createTheme({
  direction: 'rtl',
  palette: {
    mode: 'light',
    primary: { main: '#0b5cad' },
    secondary: { main: '#00a6a6' },
    background: { default: '#f4f7fb', paper: '#ffffff' }
  },
  shape: { borderRadius: 14 },
  typography: { fontFamily: 'Tahoma, Arial, sans-serif' }
})

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <App />
    </ThemeProvider>
  </React.StrictMode>
)
