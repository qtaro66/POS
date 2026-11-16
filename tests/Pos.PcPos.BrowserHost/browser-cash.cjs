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

  const orderRequestIds = []
  async function verifyCentral(browser, context, recoveredHeaders, ...outboxIds) {
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
              received.events.length === outboxIds.length * 4 && received.duplicates === 1) {
            assert.deepEqual(received.events.map(event => event.branchSequence), Array.from({ length: outboxIds.length * 4 }, (_, index) => index + 1))
            assert.deepEqual(received.events.filter(event => event.action === 'CompletePayment').map(event => event.eventId),
              outboxIds)
            assert.deepEqual(received.events.filter(event => event.action === 'SubmitOrder').map(event => event.requestId),
              orderRequestIds)
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
    const setShift = async (operation, amount, employeePin = pin) => {
      await page.locator('select[name=operation]').selectOption(operation)
      await page.getByLabel('เงินตั้งต้นหรือยอดนับจริง').fill(String(amount))
      await page.getByLabel('ยืนยัน PIN').fill(employeePin)
      await button(page, 'บันทึกกะทดสอบ').click()
    }
    await setShift('open', 500)
    await page.getByText('เปิดกะทดสอบแล้ว', { exact: true }).waitFor()
    await setShift('open', 999)
    await page.getByText('Branch ไม่ยืนยันผล', { exact: true }).waitFor()
    const openLabel = 'เปิดโต๊ะ Browser table A01'
    const openTable = button(page, openLabel)
    const openRoute = '**/tables/open'
    const mode = process.argv[4] ?? 'Concurrent'
    if (mode === 'LostResponse' || mode === 'ReloadUnsent') {
      let original
      let committed
      await page.route(openRoute, async route => {
        original = route.request().postDataJSON()
        if (mode === 'LostResponse') committed = (await route.fetch()).status()
        await route.abort()
      })
      await openTable.click()
      await page.getByText('ยังยืนยันผลเปิดโต๊ะไม่ได้', { exact: true }).waitFor()
      assert.ok(original)
      assert.equal(committed, mode === 'LostResponse' ? 200 : undefined)
      await page.unroute(openRoute)
      await page.reload()
      await page.getByLabel('PIN', { exact: true }).fill(pin)
      const login = page.waitForResponse(response => response.url().endsWith('/employee-sessions'))
      await button(page, 'เข้าสู่ระบบ').click()
      const renewed = await login
      assert.equal(renewed.status(), 200)
      session.sessionToken = (await renewed.json()).sessionToken
      await button(page, 'โต๊ะ').click()
      const recovered = page.waitForResponse(response => mode === 'LostResponse'
        ? response.url().endsWith('/commands/' + original.context.requestId)
        : response.url().endsWith('/tables/open'))
      await button(page, 'กู้คำขอเปิดโต๊ะเดิม').click()
      const result = await recovered
      assert.equal(result.status(), 200)
      if (mode === 'ReloadUnsent') assert.deepEqual(result.request().postDataJSON(), original)
    } else if (mode === 'Concurrent' || mode === 'EmployeeIsolation') {
      const otherOpen = button(observer, openLabel)
      await otherOpen.waitFor()
      const replies = [page, observer].map(tab => tab.waitForResponse(response => response.url().endsWith('/tables/open')))
      if (mode === 'Concurrent') {
        await Promise.all([openTable.click(), otherOpen.click()])
      } else {
        await openTable.click()
        assert.equal((await replies[0]).status(), 200)
        await otherOpen.click()
      }
      const opened = await Promise.all(replies)
      assert.deepEqual(opened.map(response => response.status()), [200, 200])
      const results = await Promise.all(opened.map(response => response.json()))
      assert.ok(results[0].result.tableSessionId)
      assert.equal(results[0].result.tableSessionId, results[1].result.tableSessionId)
      for (const tab of [page, observer]) {
        await button(tab, 'รีเฟรชข้อมูล').click()
        const active = tab.getByRole('button', { name: /^A01 / })
        await active.waitFor()
        assert.equal(await active.count(), 1)
        await button(tab, openLabel).waitFor({ state: 'hidden' })
        await active.click()
      }
    } else {
      await openTable.click()
    }
    await page.getByRole('heading', { name: 'โต๊ะ: A01', exact: true }).waitFor()
    await button(page, 'เปิดหน้ารายการ').click()
    await page.getByRole('button', { name: /Browser test item/ }).first().click()
    await button(page, 'ลงตะกร้า').click()
    await button(page, 'เพิ่มจำนวน Browser test item').click()
    const orderRoute = '**/orders'
    let submittedOrder
    let orderPosts = 0
    page.on('request', request => {
      if (request.method() === 'POST' && request.url() === origin + '/api/v1/ordering/orders') {
        orderPosts++
        const id = request.postDataJSON().context.requestId
        if (!orderRequestIds.includes(id)) orderRequestIds.push(id)
      }
    })
    const recoverOrder = mode === 'LostResponse' || mode === 'ReloadUnsent'
    if (recoverOrder) {
      await page.route(orderRoute, async route => {
        submittedOrder = route.request().postDataJSON()
        if (mode === 'LostResponse') assert.equal((await route.fetch()).status(), 200)
        await route.abort()
      })
    }
    await button(page, 'ส่งออเดอร์ 1 รายการ').click()
    if (recoverOrder) {
      await button(page, 'ตรวจสอบผลคำขอเดิม').waitFor()
      assert.ok(submittedOrder)
      await page.unroute(orderRoute)
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
      session.sessionToken = (await renewed.json()).sessionToken
      await button(page, 'โต๊ะ').click()
      await page.getByRole('button', { name: /^A01 / }).click()
      await button(page, 'เปิดหน้ารายการ').click()
      const orderResult = page.waitForResponse(response => mode === 'LostResponse'
        ? response.url().endsWith('/commands/' + submittedOrder.context.requestId)
        : response.url().endsWith('/orders'))
      await button(page, mode === 'LostResponse' ? 'ตรวจสอบผลคำขอเดิม' : 'ตรวจสอบและลองส่งคำขอเดิม').click()
      const recoveredOrder = await orderResult
      assert.equal(recoveredOrder.status(), 200)
      assert.equal((await recoveredOrder.json()).result.requestId, submittedOrder.context.requestId)
      if (mode === 'ReloadUnsent') assert.deepEqual(recoveredOrder.request().postDataJSON(), submittedOrder)
      assert.equal(orderPosts, mode === 'LostResponse' ? 1 : 2)
    }
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
    const recoveryMode = process.argv[4] ?? 'Concurrent'
    assert.ok(['ShiftHandoff', 'MultiSale', 'Concurrent', 'LostResponse', 'ReloadLost', 'ReloadUnsent', 'EmployeeIsolation', 'EmployeeIsolationUnsent']
      .includes(recoveryMode), 'Unknown cash recovery mode')
    const employeeIsolation = ['EmployeeIsolation', 'EmployeeIsolationUnsent'].includes(recoveryMode)
    const unsent = ['ReloadUnsent', 'EmployeeIsolationUnsent'].includes(recoveryMode)
    if (employeeIsolation || unsent || ['LostResponse', 'ReloadLost'].includes(recoveryMode)) {
      let lostResponse
      const drop = async route => {
        if (unsent) {
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
      assert.equal(lostResponse?.status, unsent ? undefined : 200)
      await page.unroute('**' + cashPath, drop)
      if (recoveryMode !== 'LostResponse') {
        host.kill()
        await stopped
        host = startBranch()
        stopped = once(host, 'close')
        ;({ pin } = await waitForBranch())
        await page.reload()
        await page.getByLabel('PIN', { exact: true }).fill(pin)
        if (employeeIsolation) {
          await page.getByLabel('PIN', { exact: true }).fill('000001')
          const otherLogin = page.waitForResponse(response => response.url().endsWith('/employee-sessions'))
          await button(page, 'เข้าสู่ระบบ').click()
          assert.equal((await (await otherLogin).json()).employeeId, 'TEST-EMPLOYEE-B')
          await button(page, 'โต๊ะ').click()
          if (unsent) await page.getByRole('button', { name: /A01.*รอชำระ/ }).waitFor()
          else await page.getByText('ไม่มีโต๊ะที่เปิดอยู่', { exact: true }).waitFor()
          assert.equal(await button(page, 'กู้ผลรับเงินจากคำขอเดิม').count(), 0)
          assert.equal(cashRequests, 1)
          await page.reload()
          await page.getByLabel('PIN', { exact: true }).fill(pin)
        }
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
    async function addSale(tableId) {
      await button(page, 'เปิดโต๊ะ Browser table ' + tableId).click()
      await page.getByRole('heading', { name: 'โต๊ะ: ' + tableId, exact: true }).waitFor()
      await button(page, 'เปิดหน้ารายการ').click()
      await page.getByRole('button', { name: /Browser test item/ }).first().click()
      await button(page, 'ลงตะกร้า').click()
      const submitted = page.waitForResponse(response => response.url().endsWith('/orders'))
      await button(page, 'ส่งออเดอร์ 1 รายการ').click()
      assert.equal((await submitted).status(), 200)
      await button(page, 'กลับผังโต๊ะ').click()
    }

    const shiftHandoff = recoveryMode === 'ShiftHandoff'
    const multiSale = shiftHandoff || recoveryMode === 'MultiSale'
    if (multiSale) {
      await button(page, 'รีเฟรชข้อมูล').click()
      await addSale('A02')
    } else {
      await setShift('close', counted)
      await page.getByText('ปิดกะแล้ว · ยอดที่ควรมี 700 · ยอดนับ ' + counted + ' · ผลต่าง ' + difference, { exact: true }).waitFor()
    }
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
      if (multiSale) {
        await device.page.getByRole('button', { name: /^A02 / }).waitFor()
        assert.equal(await device.page.getByRole('button', { name: /^A01 / }).count(), 0)
      } else {
        await device.page.getByText('ไม่มีโต๊ะที่เปิดอยู่', { exact: true }).waitFor()
      }
    }
    let recoveredHeaders = {
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
    if (multiSale) {
      const settlements = [originalSale.receiptNumber]
      const paymentRequests = [winningReply.request().postDataJSON().context.requestId]
      const tableSessions = [winningReply.request().postDataJSON().context.tableSessionId]
      for (const [tableId, received] of [['A02', 100], ['A01', 125]]) {
        if (tableId === 'A02') await page.getByRole('button', { name: /^A02 / }).click()
        else await addSale(tableId)
        await button(page, 'ขอชำระเงิน').click()
        await page.getByLabel('เงินสดที่รับ').fill(String(received))
        const completed = page.waitForResponse(response => response.url().endsWith(cashPath))
        await button(page, 'ปิดบิลเงินสด (ทดสอบ)').click()
        const response = await completed
        assert.equal(response.status(), 200)
        const { receiptNumber, changeAmount, shiftId, employeeId } = await response.json()
        assert.ok(originalSale.shiftId)
        assert.equal(shiftId, originalSale.shiftId)
        assert.equal(employeeId, originalSale.employeeId)
        assert.ok(receiptNumber)
        assert.equal(changeAmount, received - 100)
        await page.getByText(new RegExp('ใบเสร็จทดสอบ .*เงินทอน ' + changeAmount + '$')).waitFor()
        settlements.push(receiptNumber)
        paymentRequests.push(response.request().postDataJSON().context.requestId)
        tableSessions.push(response.request().postDataJSON().context.tableSessionId)
        await button(page, 'รีเฟรชข้อมูล').click()
        await page.getByRole('button', { name: new RegExp('^' + tableId + ' ') }).waitFor({ state: 'hidden' })
      }
      assert.equal(new Set(settlements).size, 3)
      assert.equal(new Set(tableSessions).size, 3)
      const closeCash = 900 + difference
      const shiftPath = '/test-cash-cycle'
      if (shiftHandoff) {
        const denied = page.waitForResponse(response => response.url().endsWith(shiftPath))
        await setShift('close', closeCash, '000000')
        assert.equal((await denied).status(), 401)
        await page.getByText('Branch ไม่ยืนยันผล', { exact: true }).waitFor()
      }
      const closed = page.waitForResponse(response => response.url().endsWith(shiftPath))
      await setShift('close', closeCash)
      const closeResponse = await closed
      assert.equal(closeResponse.status(), 200)
      await page.getByText('ปิดกะแล้ว · ยอดที่ควรมี 900 · ยอดนับ ' + closeCash + ' · ผลต่าง ' + difference, { exact: true }).waitFor()
      if (shiftHandoff) {
        const secondPin = '000001'
        async function signInNextEmployee() {
          await page.reload()
          await page.getByLabel('PIN', { exact: true }).fill(secondPin)
          const login = page.waitForResponse(response => response.url().endsWith('/employee-sessions'))
          await button(page, 'เข้าสู่ระบบ').click()
          const response = await login
          assert.equal(response.status(), 200)
          const nextEmployee = await response.json()
          assert.equal(nextEmployee.employeeId, 'TEST-EMPLOYEE-B')
          await button(page, 'โต๊ะ').click()
          return {
            Authorization: 'Device ' + device.deviceCredential,
            'X-POS-Employee-Session': nextEmployee.sessionToken,
          }
        }

        const firstCloseRecord = await closeResponse.json()
        const firstShiftId = closeResponse.request().postDataJSON().shiftId
        const changedClose = {
          ...closeResponse.request().postDataJSON(),
          cash: closeCash + 1,
        }
        const duplicate = await context.request.post(closeResponse.url(), {
          headers: recoveredHeaders,
          data: changedClose,
        })
        assert.equal(duplicate.status(), 409)

        host.kill()
        await stopped
        host = startBranch()
        stopped = once(host, 'close')
        await waitForBranch()
        await signInNextEmployee()
        const opening = page.waitForResponse(response => response.url().endsWith(shiftPath))
        await setShift('open', 300, secondPin)
        const openingResponse = await opening
        assert.equal(openingResponse.status(), 200)
        const secondOpening = openingResponse.request().postDataJSON()
        await page.getByText('เปิดกะทดสอบแล้ว', { exact: true }).waitFor()
        host.kill()
        await stopped
        host = startBranch()
        stopped = once(host, 'close')
        ;({ pin } = await waitForBranch())
        await winningDevice.page.reload()
        await winningDevice.page.getByLabel('PIN', { exact: true }).fill(pin)
        const originalLogin = winningDevice.page.waitForResponse(response => response.url().endsWith('/employee-sessions'))
        await button(winningDevice.page, 'เข้าสู่ระบบ').click()
        const originalEmployee = await originalLogin
        assert.equal(originalEmployee.status(), 200)
        winningHeaders['X-POS-Employee-Session'] = (await originalEmployee.json()).sessionToken

        const original = await winningDevice.context.request.post(winningReply.url(), {
          headers: winningHeaders,
          data: winningReply.request().postDataJSON(),
        })
        assert.equal(original.status(), 200)
        assert.deepEqual(await original.json(), originalSale)

        recoveredHeaders = await signInNextEmployee()
        const staleClose = await winningDevice.context.request.post(closeResponse.url(), {
          headers: winningHeaders,
          data: { operation: 'close', shiftId: firstShiftId, cash: closeCash + 1, pin },
        })
        assert.equal(staleClose.status(), 409, 'A stale close must leave the current shift open')
        const otherEmployeeClose = await context.request.post(closeResponse.url(), {
          headers: recoveredHeaders,
          data: {
            operation: 'close',
            shiftId: secondOpening.shiftId,
            cash: 300,
            pin,
          },
        })
        assert.equal(otherEmployeeClose.status(), 401, 'Another employee PIN must not authorize closing the current shift')
        await addSale('A02')
        await button(page, 'ขอชำระเงิน').click()
        await page.getByLabel('เงินสดที่รับ').fill('120')
        const completed = page.waitForResponse(response => response.url().endsWith(cashPath))
        await button(page, 'ปิดบิลเงินสด (ทดสอบ)').click()
        const response = await completed
        assert.equal(response.status(), 200)
        const nextSale = await response.json()
        assert.ok(nextSale.receiptNumber)
        assert.ok(!settlements.includes(nextSale.receiptNumber))
        settlements.push(nextSale.receiptNumber)
        paymentRequests.push(response.request().postDataJSON().context.requestId)
        assert.equal(nextSale.changeAmount, 20)
        assert.equal(nextSale.appliedAmount, 100)
        assert.equal(nextSale.employeeId, 'TEST-EMPLOYEE-B')
        assert.ok(nextSale.shiftId)
        assert.notEqual(nextSale.shiftId, originalSale.shiftId)
        tableSessions.push(response.request().postDataJSON().context.tableSessionId)
        assert.equal(new Set(tableSessions).size, 4)
        const nextScenario = process.argv[5]
        const nextDifferences = new Map([['Balanced', 0], ['Short', -10], ['Over', 10]])
        assert.ok(nextScenario === undefined || nextDifferences.has(nextScenario), 'Unknown second shift outcome')
        const nextDifference = nextScenario === undefined ? -difference : nextDifferences.get(nextScenario)
        const nextCounted = 400 + nextDifference
        const nextClosed = page.waitForResponse(reply => reply.url().endsWith(shiftPath))
        await setShift('close', nextCounted, secondPin)
        const nextCloseResponse = await nextClosed
        assert.equal(nextCloseResponse.status(), 200)
        const nextCloseRecord = await nextCloseResponse.json()
        const nextShiftId = nextCloseResponse.request().postDataJSON().shiftId
        await page.getByText('ปิดกะแล้ว · ยอดที่ควรมี 400 · ยอดนับ ' + nextCounted + ' · ผลต่าง ' + nextDifference, { exact: true }).waitFor()
        const replayNext = await context.request.post(response.url(), {
          headers: recoveredHeaders,
          data: response.request().postDataJSON(),
        })
        assert.equal(replayNext.status(), 200)
        assert.deepEqual(await replayNext.json(), nextSale)
        assert.equal(firstShiftId, originalSale.shiftId)
        assert.equal(nextShiftId, nextSale.shiftId)
        assert.equal(nextShiftId, secondOpening.shiftId)
        const snapshots = new Map()
        for (const [index, number] of settlements.entries()) {
          const url = origin + '/api/v1/ordering/test-cash-cycle/receipts/' + encodeURIComponent(number)
          const fail = index === 0 ? scenario === 'Short' : index === 2
          const printed = await context.request.post(url + '/simulate', { headers: recoveredHeaders, data: { fail } })
          assert.equal(printed.status(), 200)
          const outcome = await printed.json()
          assert.equal(outcome.status, fail ? 'Failed' : 'Simulated')
          snapshots.set(number, { url, fail, outcome })
        }

        host.kill()
        await stopped
        host = startBranch()
        stopped = once(host, 'close')
        await waitForBranch()
        recoveredHeaders = await signInNextEmployee()

        const { DatabaseSync } = require('node:sqlite')
        const database = new DatabaseSync(join(tmpdir(), 'pc-pos-browser-' + fixtureId, 'branch.db'), { readOnly: true })
        let persistedSales
        try {
          const history = database.prepare('SELECT shift_id, result_json FROM test_cash_close_history WHERE branch_id = ?')
            .all('BROWSER-TEST')
          assert.equal(history.length, 2)
          const ledger = database.prepare(`
            SELECT sale.request_id, sale.table_session_id, sale.result_json, job.receipt_number
            FROM cash_sales AS sale
            LEFT JOIN simulated_cash_receipt_jobs AS job
              ON job.branch_id = sale.branch_id AND job.request_id = sale.request_id
            WHERE sale.branch_id = ?
          `).all('BROWSER-TEST')
          assert.equal(ledger.length, paymentRequests.length)
          const sales = persistedSales = paymentRequests.map((requestId, index) => {
            const row = ledger.find(entry => entry.request_id === requestId)
            assert.ok(row, 'Expected a persisted sale for each payment request')
            assert.equal(row.table_session_id, tableSessions[index])
            assert.equal(row.receipt_number, settlements[index])
            const sale = JSON.parse(row.result_json)
            assert.equal(sale.ReceiptNumber, row.receipt_number)
            assert.equal(sale.TenderedAmount - sale.ChangeAmount, sale.AppliedAmount)
            return sale
          })
          for (const [shiftId, expected, employeeId] of [
            [firstShiftId, firstCloseRecord, originalSale.employeeId],
            [nextShiftId, nextCloseRecord, nextSale.employeeId],
          ]) {
            const row = history.find(item => item.shift_id === shiftId)
            assert.ok(row, 'Expected a durable close record for the settled shift')
            const stored = JSON.parse(row.result_json)
            assert.deepEqual({
              expectedCash: stored.ExpectedCash,
              countedCash: stored.CountedCash,
              difference: stored.Difference,
              employeeId: stored.EmployeeId,
              closedAt: stored.ClosedAt,
            }, expected)
            assert.equal(stored.EmployeeId, employeeId)
            const opening = database.prepare('SELECT opening_cash FROM test_cash_open_history WHERE branch_id = ? AND shift_id = ?')
              .get('BROWSER-TEST', shiftId)
            assert.ok(opening)
            const shiftSales = sales.filter(sale => sale.ShiftId === shiftId)
            assert.equal(shiftSales.length, shiftId === firstShiftId ? 3 : 1)
            assert.ok(shiftSales.every(sale => sale.EmployeeId === employeeId))
            assert.equal(Number(opening.opening_cash), shiftId === firstShiftId ? 500 : 300)
            assert.equal(Number(opening.opening_cash) + shiftSales.reduce((sum, sale) => sum + sale.AppliedAmount, 0),
              stored.ExpectedCash)
            assert.ok(Number.isFinite(Date.parse(stored.ClosedAt)))
          }
          assert.equal(database.prepare('SELECT COUNT(*) AS count FROM cash_sale_open_shifts WHERE branch_id = ?')
            .get('BROWSER-TEST').count, 0)
        } finally {
          database.close()
        }
        for (const sale of persistedSales) {
          const { url, fail, outcome } = snapshots.get(sale.ReceiptNumber)
          const receipt = await context.request.get(url, { headers: recoveredHeaders })
          assert.equal(receipt.status(), 200)
          const { text } = await receipt.json()
          assert.ok(text.includes('ใบเสร็จทดสอบ ' + sale.ReceiptNumber))
          assert.ok(text.includes('Browser test item'))
          const totals = 'ยอดชำระ ' + sale.AppliedAmount.toFixed(2) + ' · รับ ' +
            sale.TenderedAmount.toFixed(2) + ' · ทอน ' + sale.ChangeAmount.toFixed(2)
          assert.ok(text.includes(totals))
          if (!fail) assert.equal(outcome.text, text)
          const recalled = await context.request.post(url + '/simulate', {
            headers: recoveredHeaders,
            data: { fail: !fail },
          })
          assert.equal(recalled.status(), 200)
          assert.deepEqual(await recalled.json(), outcome)
        }
      }
      const status = await context.request.get(origin + '/api/v1/ordering/cloud-outbox-status', { headers: recoveredHeaders })
      assert.equal(status.status(), 200)
      const cashEvents = (await status.json()).items.filter(event => event.action === 'CompletePayment')
      assert.deepEqual(cashEvents.map(event => event.tableSessionId), tableSessions)
      assert.deepEqual(cashEvents.map(event => event.requestId), paymentRequests)
      await verifyCentral(browser, context, recoveredHeaders, ...cashEvents.map(event => event.outboxId))
    } else {
      await verifyCentral(browser, context, recoveredHeaders, payments[0].outboxId)
    }
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
