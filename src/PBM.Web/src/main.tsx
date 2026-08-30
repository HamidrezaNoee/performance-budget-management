import React from 'react'
import ReactDOM from 'react-dom/client'
import { CssBaseline, ThemeProvider, createTheme } from '@mui/material'
import App from './App'
import './styles.css'
import './login-background.css'
import './login-ai.css'

document.documentElement.dir = 'rtl'
document.documentElement.lang = 'fa'

const uiFont = '"IRANYekan", Tahoma, Arial, sans-serif'

const theme = createTheme({
  direction: 'rtl',
  palette: {
    mode: 'light',
    primary: { main: '#0b5cad' },
    secondary: { main: '#00a6a6' },
    background: { default: '#f4f7fb', paper: '#ffffff' }
  },
  shape: { borderRadius: 14 },
  typography: { fontFamily: uiFont },
  components: {
    MuiCssBaseline: {
      styleOverrides: {
        body: { fontFamily: uiFont }
      }
    },
    MuiButton: { styleOverrides: { root: { fontFamily: uiFont } } },
    MuiInputBase: { styleOverrides: { root: { fontFamily: uiFont } } },
    MuiInputLabel: { styleOverrides: { root: { fontFamily: uiFont } } },
    MuiMenuItem: { styleOverrides: { root: { fontFamily: uiFont } } },
    MuiTableCell: { styleOverrides: { root: { fontFamily: uiFont } } }
  }
})

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <App />
    </ThemeProvider>
  </React.StrictMode>
)
