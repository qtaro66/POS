const assert = require('node:assert/strict')
const { spawn } = require('node:child_process')
const { createInterface } = require('node:readline')
const { resolve, join } = require('node:path')
const { tmpdir } = require('node:os')
const { rm } = require('node:fs/promises')
const { once } = require('node:events')
const { chromium } = require('playwright')

async function run() {
  const origin = 'https://127.0.0.1:17443'
  const fixtureId = crypto.randomUUID().replaceAll('-', '')
  const host = spawn('dotnet', [resolve(__dirname, 'bin/Debug/net10.0/Pos.PcPos.BrowserHost.dll'), resolve(process.argv[2]), fixtureId])
  const stopped = once(host, 'close')
  let browser
  let timeout
  try {
    let pin
    const links = []
    await Promise.race([
      (async () => {
        for await (const line of createInterface({ input: host.stdout })) {
          if (line.startsWith('Fixture PIN: ')) pin = line.slice('Fixture PIN: '.length)
          if (/^Fixture (second )?pairing: /.test(line)) links.push(line.split(': ')[1])
          if (pin && links.length === 2) return
        }
        throw new Error('Branch fixture stopped before reporting readiness')
      })(),
      new Promise((_, reject) => { timeout = setTimeout(() => reject(new Error('Branch fixture startup timed out')), 15000) }),
    ])
    browser = await chromium.launch({ channel: 'msedge' })
    const button = (page, name) => page.getByRole('button', { name, exact: true })
    const devices = []
    for (const link of links) {
      const context = await browser.newContext({ ignoreHTTPSErrors: true })
      const page = await context.newPage()
      page.setDefaultTimeout(8000)
      await page.goto(origin)
      await page.getByLabel('ลิงก์จับคู่เครื่อง').fill(link)
      const paired = page.waitForResponse(r => r.url().endsWith('/pairing/redemptions'))
      await button(page, 'จับคู่เครื่อง').click()
      const device = await (await paired).json()
      await page.getByLabel('PIN', { exact: true }).fill('000000')
      await button(page, 'เข้าสู่ระบบ').click()
      await page.getByText('เข้าสู่ระบบไม่ได้', { exact: true }).waitFor()
      await page.getByLabel('PIN', { exact: true }).fill(pin)
      const loggedIn = page.waitForResponse(r => r.url().endsWith('/employee-sessions'))
      await button(page, 'เข้าสู่ระบบ').click()
      const response = await loggedIn
      assert.equal(response.status(), 200)
      const session = await response.json()
      await button(page, 'โต๊ะ').click()
      devices.push({ page, context, device, session })
    }
    const [{ page, context, device, session }, { page: observer }] = devices
    const setShift = async (operation, amount) => {
      await page.locator('select[name=operation]').selectOption(operation)
      await page.getByLabel('เงินตั้งต้นหรือยอดนับจริง').fill(String(amount))
      await page.getByLabel('ยืนยัน PIN').fill(pin)
      await button(page, 'บันทึกกะทดสอบ').click()
    }
    await setShift('open', 500)
    await page.getByText('เปิดกะทดสอบแล้ว', { exact: true }).waitFor()
    const commandContext = {
      requestId: crypto.randomUUID(), branchId: 'BROWSER-TEST', deviceId: device.deviceId,
      employeeId: session.employeeId, tableSessionId: crypto.randomUUID(), clientCreatedAt: new Date().toISOString(),
    }
    const opened = await context.request.post(origin + '/api/v1/ordering/tables/open', {
      headers: { Authorization: 'Device ' + device.deviceCredential, 'X-POS-Employee-Session': session.sessionToken },
      data: { schemaVersion: 1, context: commandContext, tableId: 'A01' },
    })
    assert.equal(opened.status(), 200, 'Fixture table setup')
    await button(page, 'รีเฟรชข้อมูล').click()
    await page.getByRole('button', { name: /A01/ }).click()
    await button(page, 'เปิดหน้ารายการ').click()
    await page.getByRole('button', { name: /Browser test item/ }).first().click()
    await button(page, 'ลงตะกร้า').click()
    await button(page, 'เพิ่มจำนวน Browser test item').click()
    await button(page, 'ส่งออเดอร์ 1 รายการ').click()
    await page.getByRole('status').filter({ hasText: 'สาขารับออร์เดอร์แล้ว · Request ID:' }).waitFor()
    await button(page, 'กลับผังโต๊ะ').click()
    await button(page, 'ขอชำระเงิน').click()
    await page.getByLabel('เงินสดที่รับ').fill('250')
    await button(page, 'ปิดบิลเงินสด (ทดสอบ)').click()
    await page.getByText(/ใบเสร็จทดสอบ .*เงินทอน 50/).waitFor()
    await button(page, 'จำลองพิมพ์สำเร็จ').click()
    await page.locator('pre[role=status]').filter({ hasText: '200.00' }).waitFor()
    await button(observer, 'รีเฟรชข้อมูล').click()
    await observer.getByText('ไม่มีโต๊ะที่เปิดอยู่', { exact: true }).waitFor()
    await setShift('close', 700)
    await page.getByText('ปิดกะแล้ว · ยอดที่ควรมี 700 · ยอดนับ 700 · ผลต่าง 0', { exact: true }).waitFor()
    await page.reload()
    await page.getByLabel('PIN', { exact: true }).waitFor()
    console.log('PC POS Edge ' + browser.version() + ' cash acceptance: PASS')
  } finally {
    clearTimeout(timeout)
    try { await browser?.close() }
    finally {
      host.kill()
      await stopped
      await rm(join(tmpdir(), 'pc-pos-browser-' + fixtureId), { recursive: true, force: true, maxRetries: 3 })
    }
  }
}

run().catch(error => { console.error(error.message); process.exitCode = 1 })
