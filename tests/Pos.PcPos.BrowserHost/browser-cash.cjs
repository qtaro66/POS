const assert = require('node:assert/strict')
const { spawn } = require('node:child_process')
const { createInterface } = require('node:readline')
const { resolve, join } = require('node:path')
const { tmpdir } = require('node:os')
const { rm } = require('node:fs/promises')
const { once } = require('node:events')
const { chromium } = require('playwright')

async function run() {
  const scenarios = {
    Balanced: { tender: 250, counted: 700, difference: 0 },
    Short: { tender: 200, counted: 690, difference: -10 },
    Over: { tender: 250, counted: 710, difference: 10 },
  }
  const scenario = process.argv[3] ?? 'Balanced'
  assert.ok(Object.hasOwn(scenarios, scenario), 'Expected Balanced, Short, or Over')
  const { tender, counted, difference } = scenarios[scenario]
  const origin = 'https://127.0.0.1:17443'
  const fixtureId = crypto.randomUUID().replaceAll('-', '')
  const host = spawn('dotnet', [resolve(__dirname, 'bin/Debug/net10.0/Pos.PcPos.BrowserHost.dll'), resolve(process.argv[2]), fixtureId])
  const stopped = once(host, 'close')
  let central
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
    let cashRequests = 0
    page.on('request', request => {
      if (request.url().endsWith('/tables/complete-cash-sale')) cashRequests++
    })
    await page.getByLabel('เงินสดที่รับ').fill('199.99')
    await button(page, 'ปิดบิลเงินสด (ทดสอบ)').click()
    await page.getByText('เงินสดที่รับต่ำกว่ายอดชำระ', { exact: true }).waitFor()
    assert.equal(cashRequests, 0)
    await button(observer, 'รีเฟรชข้อมูล').click()
    await observer.getByRole('button', { name: /A01/ }).click()
    const cashPath = '/tables/complete-cash-sale'
    const racers = [page, observer]
    await Promise.all(racers.map(devicePage => devicePage.getByLabel('เงินสดที่รับ').fill(String(tender))))

    // Hold both outgoing requests until both devices have submitted.
    let arrivals = 0
    let release
    const ready = new Promise(resolve => { release = resolve })
    for (const devicePage of racers) {
      await devicePage.route('**' + cashPath, async route => {
        if (++arrivals === racers.length) release()
        await ready
        await route.continue()
      })
    }
    const replies = await Promise.all(racers.map(async devicePage => {
      const response = devicePage.waitForResponse(reply => reply.url().endsWith(cashPath))
      await button(devicePage, 'ปิดบิลเงินสด (ทดสอบ)').click()
      return response
    }))
    assert.deepEqual(replies.map(reply => reply.status()).sort(), [200, 409])
    assert.equal(cashRequests, 1)
    const winningIndex = replies.findIndex(reply => reply.status() === 200)
    const cashier = racers[winningIndex]
    const rejected = racers[1 - winningIndex]
    await rejected.getByText('ยังยืนยันผลไม่ได้ ตรวจสอบสถานะก่อนทำรายการใหม่', { exact: true }).waitFor()
    await cashier.getByText(new RegExp('ใบเสร็จทดสอบ .*เงินทอน ' + (tender - 200) + '$')).waitFor()
    if (scenario === 'Short') {
      await button(cashier, 'จำลองพิมพ์ล้มเหลว').click()
      await cashier.getByText('จำลองพิมพ์ล้มเหลว · บิลปิดแล้ว', { exact: true }).waitFor()
    } else {
      await button(cashier, 'จำลองพิมพ์สำเร็จ').click()
      await cashier.locator('pre[role=status]').filter({ hasText: '200.00' }).waitFor()
    }
    const outbox = await context.request.get(origin + '/api/v1/ordering/cloud-outbox-status', {
      headers: { Authorization: 'Device ' + device.deviceCredential, 'X-POS-Employee-Session': session.sessionToken },
    })
    assert.equal(outbox.status(), 200)
    const payments = (await outbox.json()).items.filter(event => event.action === 'CompletePayment')
    assert.equal(payments.length, 1)
    assert.equal(payments[0].requestId, replies[winningIndex].request().postDataJSON().context.requestId)
    await button(observer, 'รีเฟรชข้อมูล').click()
    await observer.getByText('ไม่มีโต๊ะที่เปิดอยู่', { exact: true }).waitFor()
    await setShift('close', counted)
    await page.getByText('ปิดกะแล้ว · ยอดที่ควรมี 700 · ยอดนับ ' + counted + ' · ผลต่าง ' + difference, { exact: true }).waitFor()
    const centralOrigin = 'https://127.0.0.1:17444'
    central = spawn('dotnet', [resolve(__dirname, 'bin/Debug/net10.0/Pos.PcPos.BrowserHost.dll'), '--central', fixtureId],
      { stdio: 'ignore' })
    const centralRequest = await browser.newContext({ ignoreHTTPSErrors: true })
    const deadline = Date.now() + 60000
    let confirmed = false
    while (Date.now() < deadline && central.exitCode === null) {
      try {
        const response = await centralRequest.request.get(centralOrigin + '/fixture-events', { timeout: 1500 })
        if (response.ok()) {
          const received = await response.json()
          const pending = await context.request.get(origin + '/api/v1/ordering/cloud-outbox-status', {
            headers: { Authorization: 'Device ' + device.deviceCredential, 'X-POS-Employee-Session': session.sessionToken },
            timeout: 1500,
          })
          if (pending.ok() && (await pending.json()).summary.unacknowledgedCount === 0 &&
              received.events.length === 4 && received.duplicates === 1) {
            assert.deepEqual(received.events.map(event => event.branchSequence), [1, 2, 3, 4])
            assert.deepEqual(received.events.filter(event => event.action === 'CompletePayment').map(event => event.eventId),
              [payments[0].outboxId])
            confirmed = true
            break
          }
        }
      } catch (error) {
        if (error instanceof assert.AssertionError) throw error
      }
      await new Promise(resolve => setTimeout(resolve, 500))
    }
    assert.ok(confirmed, 'Expected Central to acknowledge all four events after retrying the cash event')
    await page.reload()
    await page.getByLabel('PIN', { exact: true }).waitFor()
    console.log('PC POS Edge ' + browser.version() + ' cash acceptance (' + scenario + '): PASS')
  } finally {
    clearTimeout(timeout)
    try { await browser?.close() }
    finally {
      if (central && central.exitCode === null) {
        const centralStopped = once(central, 'close')
        central.kill()
        await centralStopped
      }
      host.kill()
      await stopped
      await rm(join(tmpdir(), 'pc-pos-browser-' + fixtureId), { recursive: true, force: true, maxRetries: 3 })
    }
  }
}

run().catch(error => { console.error(error.message); process.exitCode = 1 })
