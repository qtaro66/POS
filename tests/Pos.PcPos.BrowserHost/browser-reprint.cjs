const assert = require('node:assert/strict')
const { spawn } = require('node:child_process')
const { randomUUID, createHash } = require('node:crypto')
const { createInterface } = require('node:readline')
const { once } = require('node:events')
const { join, resolve, basename, sep } = require('node:path')
const { tmpdir } = require('node:os')
const { existsSync } = require('node:fs')
const { realpath, lstat, rm } = require('node:fs/promises')

const cases = ['Queue', 'RejectedPin', 'LostResponse', 'Unknown']
const origin = 'https://127.0.0.1:17443'
const originalTables = ['cash_sales', 'cash_sale_open_shifts', 'simulated_cash_receipt_jobs', 'test_receipt_simulation_results',
  'table_sessions', 'table_session_audit', 'command_receipts', 'order_batches', 'order_lines', 'order_line_options',
  'table_session_billing_policy', 'bill_preview_snapshots', 'production_order_snapshots', 'print_attempts', 'rendered_print_payloads',
  'cancellation_requests', 'cancellation_request_lines', 'cancellation_request_reasons', 'cancellation_confirmations',
  'cancellation_production_checks', 'cancellation_inspection_snapshots', 'printer_profiles', 'printer_profile_audit',
  'store_lifecycle_commands', 'store_lifecycle_sessions', 'store_lifecycle_cash_snapshots', 'cloud_sync_outbox',
  'closing_report_outbox', 'closing_report_lifecycle_snapshots', 'closing_report_context_outbox']
const pause = ms => new Promise(done => setTimeout(done, ms))
async function bounded(promise, phase, ms = 30000) {
  let timer
  try { return await Promise.race([promise, new Promise((_, reject) => { timer = setTimeout(() => reject(new Error(phase)), ms) })]) }
  finally { clearTimeout(timer) }
}
async function run(dist, caseName) {
  const result = { Case: caseName, State: 'Failed', EdgeVersion: null, Phase: 'arguments', Assertions: 0, OriginalBusinessUnchanged: false, HardwareFree: false }
  const fixtureId = randomUUID().replaceAll('-', ''), directory = join(tmpdir(), 'pc-pos-browser-' + fixtureId)
  const database = join(directory, 'branch.db')
  let browser, host, page, watchdog
  const external = [], pageErrors = [], badCache = [], writes = []
  let posts = 0, statusReads = 0
  const stage = phase => { result.Phase = phase }
  function check(value, code) {
    try { assert.ok(value, code) } catch (error) { stage('check-' + code.replace(/[^A-Za-z0-9._-]/g, '-').slice(0, 50)); throw error }
    result.Assertions++
  }
  function read(action) {
    const { DatabaseSync } = require('node:sqlite')
    const db = new DatabaseSync(database, { readOnly: true })
    try { return action(db) } finally { db.close() }
  }
  function fingerprint() {
    return read(db => {
      const rows = originalTables.map(table => [table, db.prepare('SELECT * FROM "' + table + '" ORDER BY rowid').all()])
      rows.push(['original-jobs', db.prepare('SELECT * FROM print_jobs WHERE is_reprint=0 ORDER BY rowid').all()])
      return createHash('sha256').update(JSON.stringify(rows)).digest('hex')
    })
  }
  function copyCount() { return read(db => db.prepare('SELECT count(*) AS n FROM kitchen_reprint_requests').get().n) }
  async function startHost() {
    const child = spawn('dotnet', [resolve(__dirname, 'bin/Debug/net10.0/Pos.PcPos.BrowserHost.dll'), resolve(dist), fixtureId],
      { env: { ...process.env, POS_BROWSER_HISTORY_FIXTURE: '1', POS_BROWSER_REPRINT_FIXTURE: '1', POS_BROWSER_ALTERNATE_BRANCH: '0' }, stdio: ['ignore', 'pipe', 'pipe'] })
    const closed = once(child, 'close').catch(() => [])
    let done, pin; const links = [], ready = new Promise(resolveReady => { done = resolveReady })
    const lines = createInterface({ input: child.stdout })
    lines.on('line', line => {
      if (line.startsWith('Fixture PIN: ')) pin = line.slice('Fixture PIN: '.length)
      if (/^Fixture (second )?pairing: /.test(line)) links.push(line.slice(line.indexOf(': ') + 2))
      if (pin && links.length === 2) done({ pin, link: links[0] })
    })
    child.stderr.resume()
    child.on('close', () => { lines.close(); done(null) }); child.on('error', () => done(null))
    host = { child, closed }
    const value = await bounded(ready, 'fixture-readiness'); check(!!value, 'fixture-ready'); return value
  }
  const button = (name, root = page) => root.getByRole('button', { name, exact: true })
  const history = () => page.getByRole('region', { name: 'ประวัติรายการชำระเงินสด', exact: true })
  const recovery = () => page.getByRole('region', { name: 'สถานะพิมพ์ครัวซ้ำ', exact: true })
  async function text(root, value) {
    const locator = root === page ? page.locator('body') : root
    await locator.getByText(value, { exact: false }).first().waitFor()
    check((await locator.innerText()).includes(value), 'visible-expected-state')
  }
  async function login(pin, link) {
    if (link) { await page.goto(origin); await page.getByLabel('ลิงก์จับคู่เครื่อง').fill(link); await button('จับคู่เครื่อง').click() }
    await page.getByLabel('PIN', { exact: true }).fill(pin); await button('เข้าสู่ระบบ').click()
    await button('ประวัติธุรกรรม').waitFor()
  }
  async function openHistory(data) {
    await button('ประวัติธุรกรรม').click()
    await history().getByLabel('วันธุรกิจ', { exact: true }).fill(data.sale.BusinessDate)
    await button('ค้นหาธุรกรรม', history()).click()
    await history().getByRole('button', { name: new RegExp(data.sale.ReceiptNumber.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')) }).click()
    await history().getByRole('region', { name: 'รายการสินค้าจากใบคิดเงินเดิม', exact: true }).getByRole('button', { name: 'ดูข้อมูลตอนส่งรายการนี้', exact: true }).first().click()
    await history().getByRole('button', { name: /^ดูงานผลิตเดิม / }).click()
    await history().getByRole('region', { name: 'รายละเอียดงานผลิตเดิม', exact: true }).getByText('สถานะงานที่อ่าน:', { exact: false }).waitFor()
  }
  async function openForm() { await button('🔒 พิมพ์ครัวซ้ำรายการนี้').first().click(); await page.getByLabel('PIN ผู้มีสิทธิ์พิมพ์ครัวซ้ำ', { exact: true }).waitFor() }
  async function submit(pin) {
    await page.getByLabel('เหตุผลพิมพ์ครัวซ้ำ', { exact: true }).fill('Isolated browser copy acceptance')
    await page.getByLabel('PIN ผู้มีสิทธิ์พิมพ์ครัวซ้ำ', { exact: true }).fill(pin)
    await button('ยืนยันพิมพ์สำเนารายการนี้').click()
  }
  async function assertJournalNoSecrets(pin) {
    const saved = await page.evaluate(async () => {
      return await new Promise((resolveSaved, reject) => {
        const request = indexedDB.open('pos-kitchen-reprint-recovery')
        request.onerror = () => reject(new Error('Journal read failed'))
        request.onsuccess = () => {
          const db = request.result
          if (!db.objectStoreNames.contains('pending')) { db.close(); resolveSaved([]); return }
          const transaction = db.transaction('pending', 'readonly'), get = transaction.objectStore('pending').getAll()
          get.onsuccess = () => resolveSaved(get.result); get.onerror = () => reject(new Error('Journal read failed'))
          transaction.oncomplete = () => db.close()
        }
      })
    })
    check(saved.length === 1, 'one-pending-journal')
    const encoded = JSON.stringify(saved)
    check(!encoded.includes(pin) && !encoded.includes('Isolated browser copy acceptance') && !encoded.includes('"pin"') && !encoded.includes('"reason"'), 'journal-has-no-pin-or-reason')
  }
  try {
    check(cases.includes(caseName) && existsSync(join(resolve(dist), 'index.html')), 'valid-arguments')
    stage('fixture'); const ready = await startHost()
    const data = read(db => {
      const row = db.prepare("SELECT result_json FROM cash_sales WHERE request_id='HISTORY-CASH-ONE'").get()
      return { sale: JSON.parse(row.result_json) }
    })
    check(read(db => db.prepare('SELECT host FROM printer_profiles').all().every(row => row.host.endsWith('.invalid'))), 'only-invalid-fixture-printer-hosts')
    const before = fingerprint()
    const { chromium } = require('playwright')
    browser = await chromium.launch({ channel: 'msedge', headless: true }); result.EdgeVersion = browser.version()
    const context = await browser.newContext({ ignoreHTTPSErrors: true, serviceWorkers: 'block' })
    await context.route('**/*', route => { if (new URL(route.request().url()).origin !== origin) { external.push(1); return route.abort() } return route.continue() })
    page = await context.newPage(); page.setDefaultTimeout(20000)
    page.on('pageerror', () => pageErrors.push(1))
    page.on('request', request => {
      const path = new URL(request.url()).pathname
      if (path.includes('/transaction-history/kitchen-reprints/') && request.method() === 'GET') statusReads++
      if (path.startsWith('/api/v1/ordering/') && request.method() !== 'GET') {
        if (path.endsWith('/reprints') && request.method() === 'POST') posts++
        else writes.push(1)
      }
    })
    page.on('response', response => {
      const path = new URL(response.url()).pathname
      if (path.includes('/transaction-history/') && response.headers()['cache-control'] !== 'no-store') badCache.push(1)
    })
    await login(ready.pin, ready.link)
    const execute = async () => {
      stage('open-history'); await openHistory(data); await openForm()
      if (caseName === 'RejectedPin') {
        const wrongPin = (ready.pin[0] === '9' ? '8' : '9') + ready.pin.slice(1)
        stage('rejected-pin'); await submit(wrongPin); await text(page, 'PIN ไม่ผ่าน กรุณายืนยันใหม่')
        check(copyCount() === 0, 'bad-pin-no-copy')
        check(await page.getByLabel('PIN ผู้มีสิทธิ์พิมพ์ครัวซ้ำ', { exact: true }).inputValue() === '', 'bad-pin-cleared')
        await submit(ready.pin)
      } else if (caseName === 'LostResponse') {
        stage('lost-response')
        await page.route(/\/production-orders\/[^/]+\/lines\/[^/]+\/reprints$/, async route => {
          const response = await route.fetch(); check(response.status() === 200, 'server-accepted-copy'); await route.abort('failed')
        })
        await submit(ready.pin); await text(page, 'ยังยืนยันผลคำขอพิมพ์ซ้ำไม่ได้ ห้ามสั่งซ้ำ')
        await assertJournalNoSecrets(ready.pin); check(copyCount() === 1, 'lost-response-one-copy')
        await page.reload(); await login(ready.pin)
        await button('ประวัติธุรกรรม').click()
        await text(recovery(), 'สาขารับคำขอพิมพ์สำเนาแล้ว')
        check(statusReads > 0, 'reload-reads-command-status')
      } else {
        stage('queue'); await submit(ready.pin)
      }
      if (caseName !== 'LostResponse') await text(recovery(), 'สาขารับคำขอพิมพ์สำเนาแล้ว')
      if (caseName === 'Unknown') {
        stage('unknown-observation')
        await page.route(/\/transaction-history\/kitchen-reprints\/[^/]+$/, async route => {
          const response = await route.fetch(), body = await response.json(); body.status = 'Unknown'; body.attemptCount = 1
          await route.fulfill({ status: 200, headers: { ...response.headers(), 'content-type': 'application/json', 'cache-control': 'no-store' }, body: JSON.stringify(body) })
        })
        await button('ตรวจสถานะพิมพ์ครัวซ้ำ', recovery()).click()
        await text(recovery(), 'ไม่ทราบผล อาจมีกระดาษออกแล้ว ห้ามส่งซ้ำอัตโนมัติ')
      }
      stage('invariants'); await pause(250)
      check(copyCount() === 1, 'one-accepted-copy')
      check(posts === (caseName === 'RejectedPin' ? 2 : 1), 'only-explicit-posts')
      const records = read(db => ['kitchen_reprint_snapshots', 'kitchen_reprint_audit'].map(table => db.prepare('SELECT count(*) AS n FROM ' + table).get().n))
      check(records.every(count => count === 1), 'one-copy-snapshot-and-audit')
      const copy = read(db => ({ snapshot: db.prepare('SELECT * FROM kitchen_reprint_snapshots').get(), job: db.prepare('SELECT * FROM print_jobs WHERE is_reprint=1').get() }))
      const payload = JSON.parse(copy.snapshot.content_json)
      check(payload.lines.length === 1 && copy.job.snapshot_id === copy.snapshot.snapshot_id && copy.job.original_print_job_id === copy.snapshot.original_print_job_id && copy.job.target_station === copy.snapshot.production_station_id, 'copy-target-and-station-links')
      const source = read(db => JSON.parse(db.prepare('SELECT content_json FROM production_order_snapshots WHERE snapshot_id=?').get(copy.snapshot.original_snapshot_id).content_json))
      check(JSON.stringify(payload.lines[0]) === JSON.stringify(source.lines.find(line => line.orderLineId === copy.snapshot.order_line_id)) &&
        payload.tableId === source.tableId && payload.productionStationId === source.productionStationId, 'copy-keeps-frozen-original-line')
      check(copy.job.status === 'PrintQueued' && copy.job.attempt_count === 0, 'no-hardware-dispatch')
      check(fingerprint() === before, 'original-business-unchanged')
      check(writes.length === 0 && external.length === 0 && pageErrors.length === 0 && badCache.length === 0, 'isolated-safe-browser')
      result.OriginalBusinessUnchanged = true; result.HardwareFree = true
      result.State = 'Passed'; stage('completed')
    }
    await Promise.race([execute(), new Promise((_, reject) => { watchdog = setTimeout(() => reject(new Error('case-timeout')), 180000) })])
  } catch {
    // Never publish Playwright call logs, request bodies or ephemeral fixture PINs.
    result.State = 'Failed'; process.exitCode = 1
  } finally {
    clearTimeout(watchdog)
    try { await browser?.close() } catch { result.State = 'Failed'; stage('browser-cleanup'); process.exitCode = 1 }
    try { if (host) { if (host.child.exitCode === null && host.child.signalCode === null) host.child.kill(); await bounded(host.closed, 'host-shutdown') } }
    catch { result.State = 'Failed'; stage('host-cleanup'); process.exitCode = 1 }
    try {
      if (existsSync(directory)) {
        const root = await realpath(tmpdir()), target = await realpath(directory), info = await lstat(directory)
        check(/^[0-9a-f]{32}$/.test(fixtureId) && !info.isSymbolicLink() && target.toLowerCase().startsWith((root + sep).toLowerCase()) && basename(target) === 'pc-pos-browser-' + fixtureId, 'safe-fixture-cleanup')
        await rm(target, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 })
      }
    } catch { result.State = 'Failed'; stage('directory-cleanup'); process.exitCode = 1 }
    console.log('REPRINT_RESULT_JSON: ' + JSON.stringify(result))
  }
}
if (require.main === module) run(process.argv[2], process.argv[3])
module.exports = { cases }
