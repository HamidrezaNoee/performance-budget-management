import { mkdir, readdir, readFile, writeFile } from 'node:fs/promises'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const webRoot = join(dirname(fileURLToPath(import.meta.url)), '..')
const embeddedDir = join(webRoot, 'embedded-assets')
const outputDir = join(webRoot, 'public', 'assets')

const assets = [
  'iranyekan-regular.woff2',
  'iranyekan-medium.woff2',
  'iranyekan-bold.woff2',
  'pbm-login-visual.webp'
]

await mkdir(outputDir, { recursive: true })
const files = await readdir(embeddedDir)

for (const asset of assets) {
  const prefix = `${asset}.part`
  const parts = files.filter(name => name.startsWith(prefix) && name.endsWith('.b64')).sort()
  if (!parts.length) throw new Error(`No embedded Base64 parts found for ${asset}`)

  let base64 = ''
  for (const part of parts) base64 += (await readFile(join(embeddedDir, part), 'utf8')).trim()

  const bytes = Buffer.from(base64, 'base64')
  if (!bytes.length) throw new Error(`Decoded asset is empty: ${asset}`)

  await writeFile(join(outputDir, asset), bytes)
  console.log(`Materialized ${asset} (${bytes.length} bytes from ${parts.length} part(s))`)
}
