// @vitest-environment node
/// <reference types="node" />
import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

// Read from disk: Vitest replaces CSS imports, including `?raw`, with empty modules.
const appCss = readFileSync(new URL('./App.css', import.meta.url), 'utf8')
const indexCss = readFileSync(new URL('./index.css', import.meta.url), 'utf8')

/*
 * Static checks of the design tokens and touch-target sizes against WCAG 2.2
 * AA. They complement, but do not replace, a manual check in a real browser.
 */

function tokens(block: string): Record<string, string> {
  const result: Record<string, string> = {}
  for (const match of block.matchAll(/--([\w-]+):\s*(#[0-9a-fA-F]{6})\s*;/g)) {
    result[match[1]] = match[2]
  }
  return result
}

const light = tokens(indexCss.slice(0, indexCss.indexOf('@media (prefers-color-scheme: dark)')))
const dark = {
  ...light,
  ...tokens(indexCss.slice(indexCss.indexOf('@media (prefers-color-scheme: dark)'))),
}

function luminance(hex: string): number {
  const [r, g, b] = [1, 3, 5].map((offset) => {
    const channel = Number.parseInt(hex.slice(offset, offset + 2), 16) / 255
    return channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4
  })
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

function contrast(foreground: string, background: string): number {
  const [high, low] = [luminance(foreground), luminance(background)].sort((a, b) => b - a)
  return (high + 0.05) / (low + 0.05)
}

/** [foreground, background, minimum ratio]: 4.5 for text, 3 for focus and control boundaries. */
const pairs: readonly (readonly [string, string, number])[] = [
  ['text', 'bg', 4.5],
  ['text', 'surface', 4.5],
  ['text-muted', 'bg', 4.5],
  ['text-muted', 'surface', 4.5],
  ['accent-text', 'accent', 4.5],
  ['accent', 'surface', 4.5],
  ['online', 'surface', 4.5],
  ['offline', 'surface', 4.5],
  ['text', 'alert-bg', 4.5],
  ['text', 'notice-bg', 4.5],
  ['focus', 'bg', 3],
  ['focus', 'surface', 3],
  ['control-border', 'surface', 3],
  ['control-border', 'bg', 3],
  ['accent', 'surface', 3],
]

describe.each([
  ['light', light],
  ['dark', dark],
] as const)('%s color scheme', (_scheme, palette) => {
  it.each(pairs)('%s on %s meets %d:1', (foreground, background, minimum) => {
    expect(palette[foreground], `--${foreground}`).toMatch(/^#/)
    expect(palette[background], `--${background}`).toMatch(/^#/)
    expect(contrast(palette[foreground], palette[background])).toBeGreaterThanOrEqual(minimum)
  })
})

function minHeightPx(selector: string): number {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const rule = new RegExp(`(?:^|\\n)${escaped}\\s*\\{([^}]*)\\}`).exec(appCss)
  const value = rule ? /min-height:\s*(\d+)px/.exec(rule[1]) : null
  return value ? Number(value[1]) : 0
}

describe('touch targets', () => {
  it.each([
    '.button',
    '.button--compact',
    '.pin-input',
    '.text-input',
    '.table-card__open',
    '.bottom-nav__button',
  ])('%s is at least 44 px high', (selector) => {
    expect(minHeightPx(selector)).toBeGreaterThanOrEqual(44)
  })
})

describe('focus indicator', () => {
  it('draws a 3 px focus outline for keyboard focus', () => {
    expect(indexCss).toMatch(/:focus-visible\s*\{\s*outline:\s*3px solid var\(--focus\)/)
  })
})
