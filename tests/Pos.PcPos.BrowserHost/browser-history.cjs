const assert = require('node:assert/strict')
const { spawn } = require('node:child_process')
const { createHash, randomUUID } = require('node:crypto')
const { createInterface } = require('node:readline')
const { resolve, join, sep, basename } = require('node:path')
const { tmpdir } = require('node:os')
const { existsSync } = require('node:fs')
const { rm, realpath, lstat } = require('node:fs/promises')
const { once } = require('node:events')

const cases = ['Flow', 'Filters', 'PrintPaging', 'Reload', 'BranchRestart', 'SourceFailure', 'StaleRead']
const origin = 'https://127.0.0.1:17443'
const businessTables = ['cash_sales', 'cash_sale_open_shifts', 'simulated_cash_receipt_jobs', 'test_receipt_simulation_results',
  'table_sessions', 'table_session_audit', 'command_receipts', 'order_batches', 'order_lines', 'order_line_options',
  'table_session_billing_policy', 'bill_preview_snapshots', 'production_order_snapshots', 'print_jobs', 'print_attempts', 'rendered_print_payloads',
  'cancellation_requests', 'cancellation_request_lines', 'cancellation_request_reasons', 'cancellation_confirmations',
  'cancellation_production_checks', 'cancellation_inspection_snapshots', 'printer_profiles', 'printer_profile_audit',
  'store_lifecycle_commands', 'store_lifecycle_sessions', 'store_lifecycle_cash_snapshots', 'cloud_sync_outbox',
  'closing_report_outbox', 'closing_report_lifecycle_snapshots', 'closing_report_context_outbox',
  'kitchen_reprint_snapshots', 'kitchen_reprint_requests', 'kitchen_reprint_audit']
const pause = ms => new Promise(resolve => setTimeout(resolve, ms))
const escaped = value => value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
const deferred = () => { let resolve; const promise = new Promise(done => { resolve = done }); return { promise, resolve } }
async function bounded(promise, phase, milliseconds = 30000) {
  let timer
  try { return await Promise.race([promise, new Promise((_, reject) => { timer = setTimeout(() => reject(new Error(phase)), milliseconds) })]) }
  finally { clearTimeout(timer) }
}

async function run(dist, caseName) {
  const result = { Case: caseName, State: 'Failed', EdgeVersion: null, ReadOnly: false, Phase: 'arguments', Assertions: 0 }
  const fixtureId = randomUUID().replaceAll('-', '')
  const directory = join(tmpdir(), 'pc-pos-browser-' + fixtureId)
  const databasePath = join(directory, 'branch.db')
  let host, browser, context, page, before, watchdog
  let expectedSessionEnd = false, sessionEnds = 0
  const holds = []
  const businessWrites = [], badCache = [], external = [], pageErrors = []
  const failedCheck = code => { result.Phase = ('check-' + code.replace(/[^A-Za-z0-9._-]+/g, '-')).slice(0, 64) }
  const check = (condition, code) => { try { assert.ok(condition, code) } catch (error) { failedCheck(code); throw error } result.Assertions++ }
  const equal = (actual, expected, code) => { try { assert.equal(actual, expected, code) } catch (error) { failedCheck(code); throw error } result.Assertions++ }
  const stage = phase => { result.Phase = phase }
  const readDatabase = action => {
    const { DatabaseSync } = require('node:sqlite')
    const db = new DatabaseSync(databasePath, { readOnly: true })
    try { return action(db) } finally { db.close() }
  }
  const fingerprint = () => readDatabase(db => {
    const existing = new Set(db.prepare("SELECT name FROM sqlite_master WHERE type='table'").all().map(row => row.name))
    check(businessTables.every(name => existing.has(name)), 'all expected business fingerprint sources exist')
    const tables = [...new Set([...businessTables.filter(name => existing.has(name)),
      ...[...existing].filter(name => /^(master_data_|table_registry|branch_table|branch_catalog)/.test(name))])].sort()
    const rows = tables.map(name => [name, db.prepare('SELECT * FROM "' + name + '" ORDER BY rowid').all()])
    const schema = db.prepare('SELECT type,name,sql FROM sqlite_master ORDER BY type,name').all()
    return createHash('sha256').update(JSON.stringify({ schema, rows }, (_, value) => typeof value === 'bigint' ? value.toString() : value)).digest('hex')
  })
  function manifest() {
    return readDatabase(db => {
      const get = request => {
        const row = db.prepare('SELECT result_json,bill_snapshot_json FROM cash_sales WHERE request_id=?').get(request)
        check(!!row, 'history capture exists')
        const sale = JSON.parse(row.result_json), bill = JSON.parse(row.bill_snapshot_json)
        const production = db.prepare('SELECT * FROM production_order_snapshots WHERE table_session_id=? ORDER BY rowid LIMIT 1').get(sale.TableSessionId)
        check(!!production, 'original production exists')
        return { sale, bill, production, payload: JSON.parse(production.content_json) }
      }
      return { one: get('HISTORY-CASH-ONE'), two: get('HISTORY-CASH-TWO') }
    })
  }
  function startHost() {
    const child = spawn('dotnet', [resolve(__dirname, 'bin/Debug/net10.0/Pos.PcPos.BrowserHost.dll'), resolve(dist), fixtureId],
      { env: { ...process.env, POS_BROWSER_HISTORY_FIXTURE: '1', POS_BROWSER_ALTERNATE_BRANCH: '0' }, stdio: ['ignore', 'pipe', 'pipe'] })
    const closed = once(child, 'close').catch(() => [])
    // Consume host output without publishing its ephemeral pairing links or PIN.
    const readiness = deferred(), links = []; let pin
    const lines = createInterface({ input: child.stdout })
    lines.on('line', line => {
      if (line.startsWith('Fixture PIN: ')) pin = line.slice('Fixture PIN: '.length)
      if (/^Fixture (second )?pairing: /.test(line)) links.push(line.slice(line.indexOf(': ') + 2))
      if (pin && links.length === 2) readiness.resolve({ pin, links })
    })
    child.stderr.resume()
    child.on('close', () => { lines.close(); readiness.resolve(null) })
    child.on('error', () => readiness.resolve(null))
    host = { child, closed }
    return bounded(readiness.promise, 'fixture readiness').then(value => { check(!!value, 'fixture ready'); return value })
  }
  async function stopHost() {
    if (!host) return
    const current = host; host = null
    if (current.child.exitCode === null && current.child.signalCode === null) current.child.kill()
    await bounded(current.closed, 'fixture shutdown')
  }
  const button = (root, name) => root.getByRole('button', { name, exact: true })
  const history = () => page.getByRole('region', { name: 'ประวัติรายการชำระเงินสด', exact: true })
  const list = () => history().getByRole('region', { name: 'รายการธุรกรรมเงินสด', exact: true })
  const detail = () => history().getByRole('region', { name: 'รายละเอียดธุรกรรมเงินสด', exact: true })
  const production = () => detail().getByRole('region', { name: 'งานผลิตที่บันทึกตอนส่ง', exact: true })
  const original = () => production().getByRole('region', { name: 'รายละเอียดงานผลิตเดิม', exact: true })
  const print = () => original().getByRole('region', { name: 'หลักฐานงานพิมพ์จากข้อมูลตอนส่ง', exact: true })
  const attempts = () => print().getByRole('region', { name: 'ครั้งที่พยายามส่งงานพิมพ์', exact: true })
  const receipt = () => detail().getByRole('region', { name: 'หลักฐานผลจำลองใบเสร็จ', exact: true })
  async function hasText(root, text) { await root.getByText(text, { exact: false }).first().waitFor(); check((await root.innerText()).includes(text), 'expected visible history text') }
  async function signIn(pin, pairing) {
    if (pairing) {
      await page.goto(origin)
      await page.getByLabel('ลิงก์จับคู่เครื่อง').fill(pairing)
      await button(page, 'จับคู่เครื่อง').click()
    }
    await page.getByLabel('PIN', { exact: true }).fill(pin)
    await button(page, 'เข้าสู่ระบบ').click()
    await button(page, 'ประวัติธุรกรรม').waitFor()
  }
  async function openHistory(data) {
    const response = page.waitForResponse(value => new URL(value.url()).pathname === '/api/v1/ordering/transaction-history/cash-sales')
    await button(page, 'ประวัติธุรกรรม').click()
    const body = await (await response).json()
    check(/^\d{4}-\d{2}-\d{2}$/.test(body.businessDate), 'default branch business date')
    // A fixture started across midnight can belong to the preceding business day.
    if (body.businessDate !== data.one.sale.BusinessDate) {
      await history().getByLabel('วันธุรกิจ', { exact: true }).fill(data.one.sale.BusinessDate)
      await button(history(), 'ค้นหาธุรกรรม').click()
    }
    await list().locator(':scope > ol > li').nth(1).waitFor()
    equal(await list().locator(':scope > ol > li').count(), 2, 'two isolated history captures')
    await hasText(history(), 'อ่านอย่างเดียว')
  }
  async function select(value) {
    await list().getByRole('button', { name: new RegExp(escaped(value.sale.ReceiptNumber)) }).click()
    await detail().getByRole('heading', { name: value.sale.ReceiptNumber, exact: true }).waitFor()
    await detail().getByRole('region', { name: 'รายการสินค้าจากใบคิดเงินเดิม', exact: true }).waitFor()
  }
  async function openProduction(value, cancellation = false) {
    if (cancellation) {
      const events = detail().getByRole('region', { name: 'หลักฐานเหตุการณ์ที่เกี่ยวข้อง', exact: true })
      await button(events, 'หลักฐานยืนยันการยกเลิก').click()
      const cancelled = value.payload.lines.find(line => !value.bill.items.some(item => item.orderLineId === line.orderLineId))
      check(!!cancelled, 'fully cancelled original line exists')
      await events.getByRole('heading', { name: 'รหัสรายการ ' + cancelled.orderLineId, exact: true }).locator('..').getByRole('button', { name: 'ดูข้อมูลตอนส่งรายการนี้', exact: true }).click()
      equal(await production().getByLabel('รหัสรายการตอนส่ง', { exact: true }).inputValue(), cancelled.orderLineId, 'cancellation focuses original line')
    } else {
      const bill = detail().getByRole('region', { name: 'รายการสินค้าจากใบคิดเงินเดิม', exact: true })
      await button(bill, 'ดูข้อมูลตอนส่งรายการนี้').first().click()
      equal(await production().getByLabel('รหัสรายการตอนส่ง', { exact: true }).inputValue(), value.bill.items[0].orderLineId, 'bill focuses original line')
    }
    await production().getByRole('button', { name: /^ดูงานผลิตเดิม / }).click()
    await hasText(original(), 'ข้อมูลรายการตอนส่ง')
    await hasText(print(), 'สถานะงานที่อ่าน:')
    await attempts().locator(':scope > ol > li').nth(19).waitFor()
  }
  async function noCommands() {
    const actions = await history().getByRole('button', { name: /^(?:🔒\s*)?(พิมพ์|ส่งซ้ำ|ลองส่ง|จำลองใบเสร็จ|เปิดลิ้นชัก|ยกเลิกบิล|คืนเงิน)/ }).allTextContents()
    check(actions.every(name => name === '🔒 พิมพ์ครัวซ้ำรายการนี้'), 'only explicit PIN-guarded reprint entry is offered')
  }
  async function holdNext(pattern) {
    const entered = deferred(), release = deferred(), done = deferred(); let used = false
    const handler = async route => {
      if (used) return route.continue()
      used = true
      try {
        const response = await route.fetch(), body = await response.body()
        entered.resolve(); await release.promise
        await route.fulfill({ status: response.status(), headers: response.headers(), body })
      } catch { /* The old request can be aborted after selection or lock. */ }
      finally { done.resolve() }
    }
    await page.route(pattern, handler)
    const held = { entered: () => bounded(entered.promise, 'held read entered'), release: async () => { release.resolve(); await bounded(done.promise, 'held read released'); await page.unroute(pattern, handler) } }
    holds.push(() => release.resolve())
    return held
  }
  try {
    check(cases.includes(caseName) && existsSync(join(resolve(dist), 'index.html')), 'valid runner arguments')
    stage('fixture')
    let ready = await startHost()
    const data = manifest()
    equal(data.one.sale.AppliedAmount, 100, 'first known fixture cash amount')
    equal(data.two.sale.AppliedAmount, 200, 'second known fixture cash amount')
    equal(data.one.bill.items.length, 1, 'cancelled line excluded from bill')
    equal(data.one.payload.lines.length, 2, 'original production retains both lines')
    stage('browser-launch')
    const { chromium } = require('playwright')
    browser = await chromium.launch({ channel: 'msedge', headless: true })
    result.EdgeVersion = browser.version()
    context = await browser.newContext({ ignoreHTTPSErrors: true, serviceWorkers: 'block' })
    await context.route('**/*', async route => {
      const url = new URL(route.request().url())
      if (url.origin !== origin) { external.push('unexpected origin'); return route.abort() }
      return route.continue()
    })
    page = await context.newPage(); page.setDefaultTimeout(20000)
    page.on('pageerror', () => pageErrors.push('page error'))
    page.on('request', request => {
      const path = new URL(request.url()).pathname
      if (path === '/api/v1/ordering/session/end' && request.method() === 'POST' && expectedSessionEnd) sessionEnds++
      else if (path.startsWith('/api/v1/ordering/') && request.method() !== 'GET') businessWrites.push('ordering mutation')
    })
    page.on('response', response => {
      if (new URL(response.url()).pathname.includes('/transaction-history/')) {
        if (response.headers()['cache-control'] !== 'no-store') badCache.push('missing no-store')
      }
    })
    stage('pair-login'); await signIn(ready.pin, ready.links[0])
    before = fingerprint()
    const execute = async () => {
      stage('history'); await openHistory(data)
      if (caseName === 'Flow') {
        stage('flow-bill'); await select(data.one)
        const bill = detail().getByRole('region', { name: 'รายการสินค้าจากใบคิดเงินเดิม', exact: true })
        equal(await bill.locator(':scope > ol > li').count(), 1, 'one chargeable line visible')
        await hasText(bill, 'จำนวนที่คิดเงิน 1')
        check(!(await history().innerText()).includes('CURRENT-MENU-NAME'), 'history keeps original name')
        stage('flow-receipt'); await hasText(receipt(), 'ผลจำลองที่บันทึก: จำลองสำเร็จ')
        await hasText(detail().getByRole('region', { name: 'หลักฐานรอบร้านและกะ', exact: true }), 'ไม่ได้บันทึกยอดนับเงินสด')
        await hasText(detail().getByRole('region', { name: 'หลักฐานรอบร้านและกะ', exact: true }), '600.00 บาท')
        stage('flow-production'); await openProduction(data.one)
        await hasText(original(), 'จำนวนตอนส่ง 2')
        stage('flow-cancellation'); await openProduction(data.one, true)
        await hasText(original(), 'จำนวนตอนส่ง 2')
        stage('flow-print'); await hasText(print(), 'ห้ามส่งซ้ำอัตโนมัติ')
        await hasText(print(), 'ยังไม่ยืนยันว่ากระดาษออกจริง')
        await noCommands()
        stage('flow-second-capture'); await select(data.two)
        await hasText(receipt(), 'ผลจำลองที่บันทึก: จำลองไม่สำเร็จ')
        await hasText(detail().getByRole('region', { name: 'หลักฐานรอบร้านและกะ', exact: true }), '800.00 บาท')
        await hasText(detail().getByRole('region', { name: 'หลักฐานรอบร้านและกะ', exact: true }), '790.00 บาท')
        await hasText(detail().getByRole('region', { name: 'หลักฐานรอบร้านและกะ', exact: true }), '-10.00 บาท')
      } else if (caseName === 'Filters') {
        stage('filters')
        const search = async () => { const response = page.waitForResponse(value => new URL(value.url()).pathname === '/api/v1/ordering/transaction-history/cash-sales'); await button(history(), 'ค้นหาธุรกรรม').click(); equal((await response).status(), 200, 'filter read succeeds') }
        await history().getByLabel('วันธุรกิจ', { exact: true }).fill(data.one.sale.BusinessDate)
        stage('filters-table')
        await history().getByLabel('รหัสโต๊ะ ณ คิดเงิน', { exact: true }).fill(data.one.bill.tableId)
        await search(); await list().locator(':scope > ol > li').first().waitFor()
        equal(await list().locator(':scope > ol > li').count(), 1, 'original table filter')
        await history().getByLabel('รหัสโต๊ะ ณ คิดเงิน', { exact: true }).fill('')
        stage('filters-batch')
        await history().getByLabel('รหัสชุดออเดอร์เดิม', { exact: true }).fill(data.two.bill.items[0].orderBatchId)
        await search(); await list().locator(':scope > ol > li').first().waitFor()
        equal(await list().locator(':scope > ol > li').count(), 1, 'original batch filter')
        await select(data.two)
        stage('filters-receipt')
        await history().getByLabel('รหัสชุดออเดอร์เดิม', { exact: true }).fill('')
        stage('filters-receipt-number')
        await history().getByLabel('เลขใบเสร็จ', { exact: true }).fill(data.one.sale.ReceiptNumber)
        stage('filters-method')
        await history().getByRole('combobox', { name: /^ช่องทางชำระ/ }).selectOption('Cash')
        stage('filters-status')
        await history().getByRole('combobox', { name: /^สถานะธุรกรรม/ }).selectOption('Completed')
        stage('filters-receipt-search')
        await search(); await list().locator(':scope > ol > li').first().waitFor()
        equal(await list().locator(':scope > ol > li').count(), 1, 'receipt method status filters')
        stage('filters-empty')
        await history().getByLabel('เลขใบเสร็จ', { exact: true }).fill('NO-MATCH-HISTORY')
        await search(); await hasText(list(), 'ไม่พบธุรกรรมตามตัวกรองนี้')
        equal(await detail().count(), 0, 'filter refresh clears old detail')
      } else if (caseName === 'PrintPaging') {
        stage('print-paging'); await select(data.one); await openProduction(data.one)
        equal(await attempts().locator(':scope > ol > li').count(), 20, 'initial print attempt page')
        await button(attempts(), 'อ่านครั้งที่พยายามส่งเพิ่มเติม').click()
        await attempts().locator(':scope > ol > li').nth(21).waitFor()
        equal(await attempts().locator(':scope > ol > li').count(), 22, 'all recorded print attempts')
        const numbers = await attempts().locator(':scope > ol > li > p:first-child').allTextContents()
        equal(new Set(numbers.map(text => /^ครั้งที่ (\d+)/.exec(text)?.[1])).size, 22, 'no duplicated attempt numbers')
        equal(await button(attempts(), 'อ่านครั้งที่พยายามส่งเพิ่มเติม').count(), 0, 'last attempt page has no continuation')
        await noCommands()
      } else if (caseName === 'Reload' || caseName === 'BranchRestart') {
        stage('before-reload'); await select(data.one); await openProduction(data.one)
        if (caseName === 'BranchRestart') { stage('branch-restart'); await stopHost(); ready = await startHost() }
        stage('reload-login'); await page.reload(); await page.getByLabel('PIN', { exact: true }).waitFor()
        equal(await page.getByLabel('ลิงก์จับคู่เครื่อง').count(), 0, 'pairing survives reload/restart')
        await signIn(ready.pin); await openHistory(data); await select(data.one); await openProduction(data.one)
        await hasText(receipt(), 'ผลจำลองที่บันทึก: จำลองสำเร็จ')
        equal(await attempts().locator(':scope > ol > li').count(), 20, 'original attempts survive reload/restart')
      } else if (caseName === 'SourceFailure') {
        stage('source-http-failure')
        const receiptPattern = /\/cash-sales\/HISTORY-CASH-ONE\/receipt-evidence$/
        const fail = route => route.fulfill({ status: 503, headers: { 'content-type': 'application/json', 'cache-control': 'no-store' }, body: '{}' })
        await page.route(receiptPattern, fail); await select(data.one)
        await hasText(receipt(), 'ยังอ่านหลักฐานใบเสร็จไม่ได้')
        await hasText(detail(), 'ชำระเงินสดสำเร็จ')
        await hasText(detail().getByRole('region', { name: 'หลักฐานรอบร้านและกะ', exact: true }), '600.00 บาท')
        await openProduction(data.one)
        stage('source-corrupt-response')
        const printPattern = /\/print-evidence$/
        const corrupt = async route => { const response = await route.fetch(), body = await response.json(); body.cashRequestId = 'MISMATCH-RESPONSE'; await route.fulfill({ response, json: body }) }
        await page.route(printPattern, corrupt); await button(print(), 'อ่านหลักฐานงานพิมพ์ใหม่').click()
        await hasText(print(), 'ยังอ่านหลักฐานงานพิมพ์ไม่ได้')
        await hasText(original(), 'จำนวนตอนส่ง 2'); await hasText(detail(), 'ชำระเงินสดสำเร็จ')
        await page.unroute(printPattern, corrupt); await page.unroute(receiptPattern, fail)
        await button(print(), 'อ่านหลักฐานงานพิมพ์ใหม่').click(); await hasText(print(), 'ห้ามส่งซ้ำอัตโนมัติ')
        await button(receipt(), 'อ่านหลักฐานใบเสร็จใหม่').click(); await hasText(receipt(), 'ผลจำลองที่บันทึก: จำลองสำเร็จ')
      } else if (caseName === 'StaleRead') {
        stage('stale-selection'); await select(data.one); await hasText(receipt(), 'ผลจำลองที่บันทึก: จำลองสำเร็จ')
        const held = await holdNext(/\/cash-sales\/HISTORY-CASH-ONE\/receipt-evidence$/)
        await button(receipt(), 'อ่านหลักฐานใบเสร็จใหม่').click(); await held.entered()
        await select(data.two); await hasText(receipt(), 'ผลจำลองที่บันทึก: จำลองไม่สำเร็จ'); await held.release()
        check(!(await detail().innerText()).includes(data.one.sale.ReceiptNumber), 'old receipt cannot reappear after selection')
        stage('stale-lock'); await select(data.one); await openProduction(data.one)
        const pagination = await holdNext(/\/attempts\?[^#]*cursor=/)
        await button(attempts(), 'อ่านครั้งที่พยายามส่งเพิ่มเติม').click(); await pagination.entered()
        expectedSessionEnd = true
        await button(page, 'ล็อกหน้าจอ').click(); await page.getByLabel('PIN', { exact: true }).waitFor(); await pagination.release()
        equal(await history().count(), 0, 'lock removes history evidence')
        await signIn(ready.pin); expectedSessionEnd = false
        await openHistory(data); await select(data.one); await openProduction(data.one)
        equal(await attempts().locator(':scope > ol > li').count(), 20, 'old continuation cannot reappear after lock')
        check(await button(attempts(), 'อ่านครั้งที่พยายามส่งเพิ่มเติม').isEnabled(), 'new continuation loading state released')
      }
      stage('read-only-check')
      equal(fingerprint(), before, 'business rows and schema unchanged by history')
      equal(businessWrites.length, 0, 'history sends no ordering commands')
      equal(sessionEnds, caseName === 'StaleRead' ? 1 : 0, 'only explicit lock ends an authentication session')
      equal(badCache.length, 0, 'history responses use no-store')
      equal(external.length, 0, 'browser fixture remains loopback only')
      equal(pageErrors.length, 0, 'no uncaught browser errors')
      result.ReadOnly = true; result.State = 'Passed'; result.Phase = 'completed'
    }
    await Promise.race([execute(), new Promise((_, reject) => { watchdog = setTimeout(() => reject(new Error('case timeout')), 180000) })])
  } catch {
    // Do not print Playwright call logs, which can contain ephemeral fixture PINs.
    result.State = 'Failed'
    process.exitCode = 1
  } finally {
    clearTimeout(watchdog)
    for (const release of holds) release()
    try { await browser?.close() } catch { result.State = 'Failed'; result.Phase = 'browser-cleanup'; process.exitCode = 1 }
    try { await stopHost() } catch { result.State = 'Failed'; result.Phase = 'fixture-cleanup'; process.exitCode = 1 }
    try {
      if (existsSync(directory)) {
        const root = await realpath(tmpdir()), target = await realpath(directory), info = await lstat(directory)
        check(/^[0-9a-f]{32}$/.test(fixtureId) && !info.isSymbolicLink() &&
          target.toLowerCase().startsWith((root + sep).toLowerCase()) && basename(target) === 'pc-pos-browser-' + fixtureId, 'safe isolated fixture cleanup')
        await rm(target, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 })
      }
    } catch { result.State = 'Failed'; result.Phase = 'directory-cleanup'; process.exitCode = 1 }
    console.log('HISTORY_RESULT_JSON: ' + JSON.stringify(result))
  }
}

if (require.main === module) run(process.argv[2], process.argv[3])
module.exports = { cases }
