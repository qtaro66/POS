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
  const startBranch = () => spawn('dotnet', [resolve(__dirname, 'bin/Debug/net10.0/Pos.PcPos.BrowserHost.dll'), resolve(process.argv[2]), fixtureId])
  let host = startBranch()
  let stopped = once(host, 'close')
  let central
  let browser
  let timeout
  async function waitForBranch() {
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
    clearTimeout(timeout)
    return { pin, links }
  }

  async function verifyCentral(browser, context, recoveredHeaders, outboxId) {
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
            headers: recoveredHeaders,
            timeout: 1500,
          })
          if (pending.ok() && (await pending.json()).summary.unacknowledgedCount === 0 &&
              received.events.length === 4 && received.duplicates === 1) {
            assert.deepEqual(received.events.map(event => event.branchSequence), [1, 2, 3, 4])
            assert.deepEqual(received.events.filter(event => event.action === 'CompletePayment').map(event => event.eventId),
              [outboxId])
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
  }

  try {
    let { pin, links } = await waitForBranch()
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

    let replies
    let winningIndex
    const recoveryMode = process.argv[4]
    if (['LostResponse', 'ReloadLost', 'ReloadUnsent'].includes(recoveryMode)) {
      let lostResponse
      const drop = async route => {
        if (recoveryMode === 'ReloadUnsent') {
          lostResponse = { request: route.request().postDataJSON() }
          return route.abort()
        }
        const upstream = await route.fetch()
        lostResponse = { status: upstream.status(), sale: await upstream.json(), request: route.request().postDataJSON() }
        if (upstream.status() === 200) await route.abort()
        else await route.fulfill({ response: upstream })
      }
      await page.route('**' + cashPath, drop)
      await button(page, 'ปิดบิลเงินสด (ทดสอบ)').click()
      await page.getByText('ยังยืนยันผลไม่ได้ ตรวจสอบสถานะก่อนทำรายการใหม่', { exact: true }).waitFor()
      assert.equal(lostResponse?.status, recoveryMode === 'ReloadUnsent' ? undefined : 200)
      await page.unroute('**' + cashPath, drop)
      if (recoveryMode !== 'LostResponse') {
        host.kill()
        await stopped
        host = startBranch()
        stopped = once(host, 'close')
        ;({ pin } = await waitForBranch())
        await page.reload()
        await page.getByLabel('PIN', { exact: true }).fill(pin)
        const login = page.waitForResponse(response => response.url().endsWith('/employee-sessions'))
        await button(page, 'เข้าสู่ระบบ').click()
        const renewed = await login
        assert.equal(renewed.status(), 200)
        const freshSession = await renewed.json()
        await button(page, 'โต๊ะ').click()
        const retry = page.waitForResponse(reply => reply.url().endsWith(cashPath))
        await button(page, 'กู้ผลรับเงินจากคำขอเดิม').click()
        const reply = await retry
        assert.equal(reply.status(), 200)
        if (lostResponse.sale) assert.deepEqual(await reply.json(), lostResponse.sale)
        assert.deepEqual(reply.request().postDataJSON(), lostResponse.request)
        await page.getByText(new RegExp('ใบเสร็จทดสอบ .*เงินทอน ' + (tender - 200) + '$')).waitFor()
        const queue = await context.request.get(origin + '/api/v1/ordering/cloud-outbox-status', {
          headers: { Authorization: 'Device ' + device.deviceCredential, 'X-POS-Employee-Session': freshSession.sessionToken },
        })
        assert.equal(queue.status(), 200)
        const cashEvents = (await queue.json()).items.filter(item => item.action === 'CompletePayment')
        assert.equal(cashEvents.length, 1)
        assert.equal(cashEvents[0].requestId, lostResponse.request.context.requestId)
        await setShift('close', counted)
        await page.getByText('ปิดกะแล้ว · ยอดที่ควรมี 700 · ยอดนับ ' + counted + ' · ผลต่าง ' + difference, { exact: true }).waitFor()
        await verifyCentral(browser, context, {
          Authorization: 'Device ' + device.deviceCredential,
          'X-POS-Employee-Session': freshSession.sessionToken,
        }, cashEvents[0].outboxId)
        console.log('PC POS Edge cash acceptance (' + recoveryMode + '): PASS')
        return
      }
      await page.getByLabel('เงินสดที่รับ').fill(String(tender + 1))
      replies = await Promise.all(racers.map(async devicePage => {
        const response = devicePage.waitForResponse(reply => reply.url().endsWith(cashPath))
        await button(devicePage, 'ปิดบิลเงินสด (ทดสอบ)').click()
        return response
      }))
      assert.deepEqual(replies.map(reply => reply.status()), [200, 409])
      assert.equal(cashRequests, 2)
      assert.deepEqual(replies[0].request().postDataJSON(), lostResponse.request)
      winningIndex = 0
    } else {
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
      replies = await Promise.all(racers.map(async devicePage => {
        const response = devicePage.waitForResponse(reply => reply.url().endsWith(cashPath))
        await button(devicePage, 'ปิดบิลเงินสด (ทดสอบ)').click()
        return response
      }))
      assert.deepEqual(replies.map(reply => reply.status()).sort(), [200, 409])
      assert.equal(cashRequests, 1)
      winningIndex = replies.findIndex(reply => reply.status() === 200)
    }
    const winningReply = replies[winningIndex]
    const originalSale = await winningReply.json()
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
    host.kill()
    await stopped
    host = startBranch()
    stopped = once(host, 'close')
    ;({ pin } = await waitForBranch())
    for (const device of devices) {
      await device.page.reload()
      await device.page.getByLabel('PIN', { exact: true }).fill(pin)
      const login = device.page.waitForResponse(response => response.url().endsWith('/employee-sessions'))
      await button(device.page, 'เข้าสู่ระบบ').click()
      const response = await login
      assert.equal(response.status(), 200)
      device.session = await response.json()
      await button(device.page, 'โต๊ะ').click()
      await device.page.getByText('ไม่มีโต๊ะที่เปิดอยู่', { exact: true }).waitFor()
    }
    const recoveredHeaders = {
      Authorization: 'Device ' + device.deviceCredential,
      'X-POS-Employee-Session': devices[0].session.sessionToken,
    }
    const recovered = await context.request.get(origin + '/api/v1/ordering/cloud-outbox-status', { headers: recoveredHeaders })
    assert.equal(recovered.status(), 200)
    const pendingCash = (await recovered.json()).items.filter(event => event.action === 'CompletePayment')
    assert.deepEqual(pendingCash.map(event => [event.outboxId, event.branchSequence]),
      [[payments[0].outboxId, payments[0].branchSequence]])
    const winningDevice = devices[winningIndex]
    const winningHeaders = {
      Authorization: 'Device ' + winningDevice.device.deviceCredential,
      'X-POS-Employee-Session': winningDevice.session.sessionToken,
    }
    const replay = await winningDevice.context.request.post(winningReply.url(), {
      headers: winningHeaders,
      data: winningReply.request().postDataJSON(),
    })
    assert.equal(replay.status(), 200)
    assert.deepEqual(await replay.json(), originalSale)
    const receiptUrl = origin + '/api/v1/ordering/test-cash-cycle/receipts/' + encodeURIComponent(originalSale.receiptNumber)
    const receipt = await winningDevice.context.request.get(receiptUrl, { headers: winningHeaders })
    assert.equal(receipt.status(), 200)
    assert.match((await receipt.json()).text, /200\.00/)
    const simulation = await winningDevice.context.request.post(receiptUrl + '/simulate', {
      headers: winningHeaders,
      data: { fail: scenario !== 'Short' },
    })
    assert.equal(simulation.status(), 200)
    assert.equal((await simulation.json()).status, scenario === 'Short' ? 'Failed' : 'Simulated')
    await verifyCentral(browser, context, recoveredHeaders, payments[0].outboxId)
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
