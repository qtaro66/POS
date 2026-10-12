# Requirement เบื้องต้น — ระบบร้านอาหารหลายสาขา

ฉบับ 0.2 • 20 กันยายน 2026 • ปรับปรุงผลทดสอบเครื่องพิมพ์ 12 ตุลาคม 2026 • สถานะ: ข้อกำหนดสำหรับออกแบบและทำต้นแบบ ยังไม่ใช่ข้อกำหนดพร้อมใช้งานจริง

การเปลี่ยนแปลงสำคัญจาก v0.1: เพิ่มหลักการ offline-first, state machine ของออร์เดอร์, idempotency, source of truth, ข้อกำหนด HTTPS/PWA, Direct Network PrintGateway, Technology Stack ที่ยืนยัน, Non-functional Requirements และเกณฑ์ผ่านของต้นแบบแรก

## 1. ที่มาและวิธีรวมข้อมูล

รวมเนื้อหาบทสนทนาจาก:

- S1 — [niceloop_dev](https://chatgpt.com/s/cx_6aaf8b0a6124819181f96b06042fd733): การศึกษาเว็บ POS, APK, เส้นทางเครือข่าย และเครื่องมือวิเคราะห์
- S2 — [ทำแผนผัง Mobile](https://chatgpt.com/share/6aaf8adc-8d80-83ec-bace-38aea0085291): การวิเคราะห์ Mobile และ Printer Interface รุ่นเดิม
- S3 — [วิเคราะห์ไฟล์ PrintServiceV2](https://chatgpt.com/s/cx_6aaf8b2ab9ac819194d2db5b8e2335ac): สำเนาแชทปัจจุบันถึงช่วงเริ่มสรุป requirement
- S4 — แชทปัจจุบัน: ข้อกำหนดและการตัดสินใจล่าสุด รวมภาพหน้าจอและไฟล์ C# ที่ตรวจไว้

S3 ซ้ำกับ S4 เป็นส่วนใหญ่ จึงนับแต่ละข้อกำหนดครั้งเดียว อ่านข้อความที่หน้าแชร์เปิดเผยได้แล้ว แต่ไฟล์แนบที่ระบุเพียง Uploaded a file ในแชทต้นทางไม่ได้ถูกตรวจใหม่ทั้งหมด ผลวิเคราะห์ในแชทเก่าจึงเป็นข้อมูลอ้างอิง ไม่ใช่การรับรองซอฟต์แวร์เดิมอีกครั้ง

สถานะในเอกสาร:

- **ยืนยัน**: ผู้ใช้ระบุหรือยอมรับโดยตรง
- **แนวทางทดลองที่ยอมรับ**: ใช้เริ่มออกแบบได้ ปรับได้ตามผลต้นแบบ
- **อ้างอิง/รอรายละเอียด**: เห็นจากระบบเดิมหรือภาพ แต่กฎธุรกิจยังไม่ยืนยัน
- **ข้อเสนอ**: ผู้ช่วยเสนอ ยังไม่ใช่ขอบเขตที่อนุมัติ

เมื่อมีความต่าง ใช้ข้อกำหนดล่าสุดที่ผู้ใช้ยืนยัน ไม่ยกข้อเสนอในคำตอบเก่าให้กลายเป็น requirement อัตโนมัติ

## 2. เป้าหมายและขอบเขตหลัก

| ID | Requirement | สถานะ |
|---|---|---|
| CORE-01 | สร้างระบบใหม่โดยอ้างอิงแนวคิดและ workflow ของ Niceloop ไม่จำเป็นต้องใช้โค้ด หน้าตา หรือ API เดิมทั้งระบบ | ยืนยัน |
| CORE-02 | มี Branch PC 1 เครื่องต่อสาขา; เครื่องเดิมใช้ Windows และระบบใหม่ต้องออกแบบ cross-platform เพื่อทดลอง Linux บน Mini PC; Mobile รองรับ Android/iOS | ยืนยัน |
| CORE-03 | POS ทำงานได้ทั้งออนไลน์และออฟไลน์ | ยืนยัน; รายฟังก์ชันต้องกำหนด |
| CORE-04 | ช่องทางขายเฉพาะโต๊ะ ไม่รวม TOGO และ Delivery | ยืนยัน |
| CORE-05 | รองรับ Master Data เมนูหลายสาขาและการเชื่อม API ภายนอก | ยืนยัน |
| CORE-06 | Back Office ส่วนกลางแยกจาก POS เข้าถึงได้โดยไม่ผ่านหน้าขายของสาขา | ยืนยัน |
| CORE-07 | ระบบใหม่ไม่ใช้ `PrinterInterface_335.exe`; Branch Service ส่งงานไป Thermal Printer ผ่าน LAN/RJ45 โดยตรงผ่าน `PrintGateway` และ Printer Profile; protocol จริงต้องยืนยันรายรุ่น | ยืนยันสถาปัตยกรรม; Epson TM-T82X/M352A ผ่านการทดสอบแบบ Provisional ตาม D13 แต่ยังไม่ Production-certified |
| CORE-08 | ต้องพัฒนา Mobile application สำหรับใช้งานจริงบนทั้ง Android และ iOS; รูปแบบ Native, Cross-platform หรือ Installable PWA ต้องตัดสินใจและพิสูจน์ตาม D17 โดยไม่ถือว่าการเปิดผ่าน browser เพียงอย่างเดียวปิด requirement นี้แล้ว | ยืนยันเป้าหมายผลิตภัณฑ์; รอเลือก implementation |
| CORE-09 | Source code ของทุกองค์ประกอบในระบบ รวมถึง Mobile, PC POS, Branch/Central Service, Back Office, PrintGateway, schema, tests, deployment definition และเอกสาร ต้องจัดเก็บใน Git และตั้งใจเผยแพร่แบบ Open Source | ยืนยันเจตนา; ต้องเลือก License ก่อนเผยแพร่ |

อินเทอร์เน็ตขาดกับ LAN/Wi-Fi ขาดเป็นคนละกรณี: แนวทางที่ยอมรับรองรับการขายและส่งออร์เดอร์ภายในร้านเมื่ออินเทอร์เน็ตขาด แต่เครือข่ายร้านและบริการสาขายังทำงาน

## 3. โครงสร้างระบบที่ยอมรับเบื้องต้น

```text
Back Office / บริการส่วนกลาง
        ↕ ซิงก์เมื่อออนไลน์
บริการสาขาแบบ cross-platform + ฐานข้อมูลในสาขา
        ├── หน้าจอ PC POS
        ├── Mobile application ผ่าน Wi-Fi/LAN (Android / iOS)
        └── Direct Network PrintGateway → Thermal Printer ผ่าน LAN/RJ45
```

- Phase 0 ยังใช้ Web app/PWA เป็นแนวทางทดลองได้ แต่เป้าหมายผลิตภัณฑ์ต้องมี Mobile application สำหรับ Android และ iOS; รูปแบบ Native, Cross-platform หรือ Installable PWA ต้องตัดสินใจใน D17 และทดสอบบนอุปกรณ์จริงตั้งแต่ต้น
- บริการสาขาทำงานแยกจากหน้าจอ POS; ปิดหน้าขายแล้วบริการยังต้องทำงานได้
- เครื่องที่รันบริการสาขาต้องเปิดอยู่ หากเครื่องดับ มือถือไม่สามารถส่งงานเข้าสาขานั้นได้
- Mobile ส่งออร์เดอร์ผ่านเครือข่ายร้าน ไม่จำเป็นต้องผ่าน Cloud ก่อน
- อินเทอร์เน็ตขาด: รับออร์เดอร์และส่งพิมพ์ในร้านได้ เก็บข้อมูลเพื่อซิงก์เมื่อเชื่อมต่อกลับ
- Wi-Fi มือถือหลุด: เก็บร่างและแสดงว่ายังไม่ส่ง; วิธีส่งต่ออัตโนมัติหรือให้กดยืนยันยังต้องกำหนด
- ใช้รหัสคำขอเพื่อป้องกันออร์เดอร์ซ้ำและตรวจสถานะหลังการเชื่อมต่อขาด
- แสดงสถานะแยก: บันทึกในมือถือ → สาขารับแล้ว → ส่งงานพิมพ์แล้ว
- สถานะส่งงานพิมพ์ไม่เท่ากับยืนยันว่ากระดาษออกจริง
- รายละเอียด HTTPS, การค้นหาบริการสาขา, การจับคู่/เพิกถอนอุปกรณ์ และการกู้คืนหลังแอปปิด ต้องออกแบบและทดสอบ

### 3.1 Source of Truth และขอบเขต Offline

- ระหว่างอินเทอร์เน็ตขาด ฐานข้อมูลของบริการสาขาเป็นแหล่งข้อมูลหลักของธุรกรรมขายภายในสาขา
- Cloud/Back Office เป็นแหล่งข้อมูลหลักของ Master Data และการกำหนดค่าส่วนกลาง แต่บริการสาขาต้องเก็บ version ที่เผยแพร่แล้วไว้ใช้งานโดยไม่พึ่งอินเทอร์เน็ต
- บิลที่เปิดอยู่ต้องเก็บ snapshot ของชื่อสินค้า ราคา Topping ภาษี ส่วนลด สูตรคำนวณ และ Master Data version ที่ใช้ ห้ามเปลี่ยนยอดย้อนหลังเพียงเพราะ Master Data ส่วนกลางเปลี่ยน
- การซิงก์ธุรกรรมจากสาขาไป Cloud ต้องใช้ event/record identifier ที่ไม่ซ้ำและรองรับการส่งซ้ำ ห้ามใช้ last-write-wins กับธุรกรรมการเงินโดยไม่มีกฎเฉพาะ
- เวลาจากอุปกรณ์ไม่ถือเป็นลำดับธุรกรรมที่เชื่อถือได้เพียงอย่างเดียว บริการสาขาต้องกำหนดเวลารับและลำดับของตนเอง

### 3.2 Order Delivery และ Idempotency

สถานะขั้นต่ำของคำขอจาก Mobile:

```text
Draft → Pending → Submitted
                      ├──→ BranchAccepted
                      ├──→ Rejected
                      └──→ Unknown ──status query ด้วย requestId เดิม──→ BranchAccepted / Rejected / Pending
```

- Mobile สร้าง `requestId` ครั้งเดียวเมื่อเริ่มส่ง และเก็บไว้ใน persistent local outbox
- การ retry ต้องใช้ `requestId` เดิม ห้ามสร้างออร์เดอร์ใหม่เพียงเพราะ Mobile ไม่ได้รับ response
- บริการสาขาต้องมี unique constraint บน `requestId` ภายในขอบเขตสาขา และคืนผลเดิมเมื่อได้รับคำขอซ้ำ
- Mobile ต้องถามสถานะด้วย `requestId` ได้หลัง Wi-Fi หลุด แอปถูกปิด หรือ response สูญหาย
- `BranchAccepted` หมายถึงบริการสาขาบันทึกธุรกรรมลง persistent storage สำเร็จแล้ว ไม่ใช่เพียงรับ HTTP request
- Print retry ต้องมี `printJobId` และกฎ deduplication แยกจาก `requestId` ของออร์เดอร์
- ห้ามแสดง “พิมพ์สำเร็จ” หากหลักฐานมีเพียงเขียนข้อมูลลง TCP socket หรือส่งงานเข้า print service ให้ใช้สถานะตามหลักฐานจริง เช่น `Queued`, `Dispatched`, `Failed`, `Unknown`
- `Unknown` ใน flow นี้เป็นสถานะความรู้ของ Mobile ไม่ใช่ข้อสรุปว่าธุรกรรมล้มเหลว; ห้ามสร้าง `requestId` ใหม่ก่อนตรวจสถานะเดิม

### 3.3 PWA, HTTPS และการค้นหาบริการสาขา

- Mobile ที่เปิดบริการสาขาผ่าน LAN ไม่ได้อยู่บน `localhost` ของโทรศัพท์ จึงต้องใช้ HTTPS ที่ Android และ iOS เชื่อถือเพื่อรองรับ PWA/Service Worker และป้องกันการดักแก้ข้อมูลในเครือข่ายร้าน
- **D11 ยืนยันสำหรับ Phase 0:** ใช้ Private CA โดยติดตั้ง Root CA certificate ให้เป็น trusted certificate บนอุปกรณ์ Android/iOS ที่ใช้ทดสอบ
- Private CA private key ต้องเก็บบนเครื่องดูแลระบบที่ควบคุมสิทธิ์และมี backup ที่เข้ารหัส ห้ามคัดลอก private key ของ CA ไปยัง PC สาขา, Mobile, source code หรือ repository
- Network Gateway เช่น TP-Link ER707-M2 ไม่เก็บ Root/Intermediate CA private key ใช้เฉพาะงานเครือข่าย เช่น DHCP reservation, firewall, VLAN, routing และ VPN ตามความจำเป็น
- แต่ละสาขาได้รับ server certificate และ private key ของสาขาตนเอง โดย certificate ต้องมี SAN ตรงกับ hostname ที่ Mobile ใช้จริง เช่น `branch-001.pos.internal`
- PC สาขาเก็บเฉพาะ private key ของ server certificate ตนเอง ไม่สามารถออก certificate ให้สาขาหรืออุปกรณ์อื่น
- การติดตั้ง Root CA ใน Phase 0 ทำด้วยตนเองได้ แต่ต้องบันทึกขั้นตอนติดตั้ง/เปิด Full Trust/ถอน certificate สำหรับ Android และ iOS และต้องทดสอบใหม่เมื่อเปลี่ยนอุปกรณ์
- ห้ามใช้การกดยอมรับ certificate warning, ปิด certificate validation หรือ fallback ไป HTTP หาก certificate ผิด หมดอายุ หรือยังไม่ trusted
- ก่อน production ต้องทบทวนว่าจะใช้ Private CA ต่อพร้อมระบบจัดการอุปกรณ์/CA lifecycle หรือเปลี่ยนเป็น public domain และ Public CA
- ต้องกำหนด hostname ของบริการสาขา วิธีออก/ติดตั้ง/ต่ออายุ certificate, local DNS หรือ discovery, port และ firewall rule ของระบบปฏิบัติการที่ใช้
- **D11 ยืนยันสำหรับ Phase 0:** ใช้ QR pairing เป็นวิธีจับคู่ Mobile กับสาขา
- QR ต้องสร้างโดย Branch Service ภายในร้านและเปิดแสดงผ่านหน้าที่มีสิทธิ์บน PC POS ประกอบด้วย HTTPS URL/branch identifier, `pairingId` และ random one-time token อายุ 5 นาที
- ผู้ใช้ทุกคนที่ authentication เข้าใช้งาน POS สำเร็จมีสิทธิ์สร้าง QR pairing ไม่กำหนด role หรือ PIN ยืนยันซ้ำเป็นพิเศษ แต่ต้องบันทึก employee/user ID ผู้สร้างใน audit log
- QR ห้ามบรรจุ PIN พนักงาน, CA private key, server private key, รหัสผ่านฐานข้อมูล หรือ device credential แบบถาวร
- Token ต้องใช้ได้ครั้งเดียว ตรวจสอบและใช้โดย Branch Service ได้แม้อินเทอร์เน็ตขาดแต่ LAN ยังทำงาน และต้องปฏิเสธเมื่อหมดอายุหรือถูกใช้แล้ว
- ผู้สร้าง QR แต่ละคนมี token ที่ยัง active ได้ครั้งละหนึ่งชุด การสร้าง QR ใหม่ต้องยกเลิก token เก่าที่ยังไม่ได้ใช้ของผู้สร้างคนนั้นโดยอัตโนมัติ
- การจับคู่ Mobile หลายเครื่องต้องสร้าง QR แยกต่ออุปกรณ์ ห้ามใช้ QR เดียวแบบหลายครั้ง
- Token ต้องสร้างจาก cryptographically secure random อย่างน้อย 128 บิต และ Branch Service เก็บเฉพาะ hash หลังแสดง QR
- เมื่อ pairing สำเร็จ Branch Service สร้าง device record และออก device credential ที่เพิกถอนได้ให้ Mobile โดยบันทึก device ID, ชื่ออุปกรณ์, สาขา, ผู้อนุมัติ และเวลา
- Device authentication แยกจาก employee login; การ pair อุปกรณ์ไม่ได้ให้สิทธิ์ทำรายการแทนพนักงานโดยอัตโนมัติ
- หากผู้ใช้ล้าง browser data, สูญเสีย device credential หรือเปลี่ยนอุปกรณ์ ต้อง pair ใหม่
- การจับคู่ครั้งแรกต้องยืนยันตัวตนของสาขาและอุปกรณ์ ไม่เชื่อถือเพียงการอยู่ใน Wi-Fi เดียวกัน
- **D11 ยืนยันสำหรับ Phase 0:** Branch Service ใช้ IP ที่ทำ DHCP reservation เป็น URL เช่น `https://192.168.1.10` และ server certificate ต้องมี IP address นั้นใน Subject Alternative Name (SAN) ห้ามเชื่อมด้วย IP อื่นที่ certificate ไม่ครอบคลุม
- Phase 0 ยังไม่บังคับ Local DNS/hostname เมื่อเปลี่ยน IP ต้องออก server certificate ใหม่และอัปเดต QR/การตั้งค่าที่เกี่ยวข้องก่อนใช้งาน
- Branch Database เก็บ employee record, role/status และ PIN verifier ล่าสุดไว้โดยไม่กำหนดวันหมดอายุ เพื่อให้ login ได้ขณะ Cloud/อินเทอร์เน็ตใช้งานไม่ได้
- การเพิกถอนพนักงานจากส่วนกลางมีผลทันทีเมื่อ Branch Service ออนไลน์และได้รับการเปลี่ยนแปลง หากสาขา offline จะมีผลทันทีในการซิงก์ครั้งแรกหลังเชื่อมต่อกลับ การรับผลจากส่วนกลางแบบทันทีขณะไม่มีเส้นทางสื่อสารเป็นไปไม่ได้
- การเพิกถอนพนักงานจาก PC POS ภายในสาขามีผลต่อ Branch Service ได้ทันทีแม้อินเทอร์เน็ตขาด และต้องซิงก์เหตุการณ์กลับส่วนกลางเมื่อออนไลน์
- POS/Mobile ต้องแสดงเวลา sync ข้อมูลพนักงานล่าสุดและสถานะ Offline เพื่อให้ผู้ใช้ทราบว่าการเพิกถอนจากส่วนกลางอาจยังมาไม่ถึง
- **D11 ยืนยันสำหรับ Phase 0:** Device credential ไม่มีวันหมดอายุตายตัว ใช้ได้จนถูกเพิกถอน สูญหายจาก browser storage หรือมีการ reset ระบบ โดย Branch Service ต้องตรวจสถานะ Active/Revoked ของ device ในทุก protected request และสามารถ rotate credential แบบโปร่งใสได้
- ต้องเพิกถอน Mobile รายเครื่องได้ โดยการเพิกถอนในสาขามีผลทันที และการเพิกถอนจากส่วนกลางอยู่ภายใต้ข้อจำกัดการเชื่อมต่อเดียวกับข้อมูลพนักงาน
- Device record มีสถานะขั้นต่ำ `Active`, `Revoked`, `Lost` และเก็บ device ID, ชื่ออุปกรณ์, ผู้สร้าง QR, วันที่ pair, ผู้ใช้ล่าสุด, last seen, ผู้เพิกถอน, เวลาและเหตุผล
- ผู้ใช้ที่ authentication เข้า POS สำเร็จทุกคนมีสิทธิ์เพิกถอนอุปกรณ์ตามนโยบายเดียวกับการสร้าง QR และทุกการกระทำต้องมี audit log
- Credential ที่ถูก revoke ห้ามนำกลับมา Active อีก หากต้องการใช้ Mobile เดิมต้อง pair ใหม่และออก credential ใหม่ โดยไม่ลบประวัติ device record เดิม
- หาก Mobile ล้าง browser data หรือ credential สูญหาย ให้ mark/revoke record เก่าและ pair ใหม่
- **D11 ยืนยัน CA lifecycle สำหรับ Phase 0:** ใช้ Root CA อายุ 10 ปี, Intermediate Issuing CA อายุ 3 ปี และ Branch Server Certificate อายุ 1 ปี ค่าเหล่านี้เป็นนโยบายของต้นแบบและต้องทบทวนอีกครั้งก่อน production
- Root CA private key เก็บ offline พร้อม backup เข้ารหัส 2 ชุดแยกสถานที่; Intermediate private key เก็บบนเครื่องดูแลระบบ; Branch PC เก็บเฉพาะ server certificate/private key ของสาขา; Mobile เก็บเฉพาะ Root CA public certificate
- ต้องมี certificate inventory ซึ่งบันทึก serial number, fingerprint, SAN/IP, สาขา, วันที่ออก, วันหมดอายุ และสถานะ
- แจ้งเตือน certificate ล่วงหน้า 60, 30, 14 และ 7 วัน และต่ออายุ Branch certificate อย่างช้าที่ 30 วันก่อนหมดอายุ โดยมีช่วงทับซ้อนเพื่อทดสอบ certificate ใหม่ก่อนสลับ
- การต่ออายุ Branch certificate ภายใต้ Root เดิมต้องไม่บังคับติดตั้ง Root CA ใหม่บน Mobile
- หาก Branch private key รั่วให้ออก key/certificate ใหม่และหยุดใช้ของเดิม; หาก Intermediate key รั่วให้ออก Intermediate และ Branch certificate ใหม่; หาก Root key สูญหายหรือรั่วให้สร้าง Root ใหม่ ติดตั้ง trust ใหม่บน Mobile และ reissue certificate ทั้งหมด โดยกรณีรั่วต้องนำ Root เดิมออกจากทุกอุปกรณ์
- ห้ามสมมติว่า browser จะส่งงานเบื้องหลังได้เสมอ Mobile ต้องแสดง outbox และตรวจสถานะอีกครั้งเมื่อผู้ใช้กลับเข้าแอป

### 3.4 ความพร้อมใช้งานของบริการสาขา

- บริการสาขาต้องทำงานเป็น Windows Service หรือ Linux systemd service เริ่มอัตโนมัติหลัง restart และไม่ขึ้นกับการเปิดหน้าจอ POS
- ต้องมี health check, log rotation, การตรวจพื้นที่ดิสก์ และหน้าแสดงสถานะบริการ/ฐานข้อมูล/Printer Adapter
- ต้องกำหนดวิธี backup/restore, RPO, RTO และพฤติกรรมเมื่อเครื่องหลักของสาขาดับ
- **D02 ยืนยัน:** ต่อหนึ่งสาขามี Branch PC 1 เครื่อง ซึ่งรัน PC POS, บริการสาขา และฐานข้อมูลสาขา รองรับ Mobile พร้อมกันไม่เกิน 20 เครื่อง; เครื่องเดิมเป็น Windows ส่วน Mini PC ใหม่สามารถใช้ Linux หลังผ่าน pilot ตาม D17
- Hardware เป้าหมายต้องรองรับ NVMe 2 ลูกแบบ RAID 1 สำหรับระบบสดและ Internal SSD/HDD ลูกที่ 3 แยกจาก RAID สำหรับ Local Backup; หาก Mini PC ที่ผ่านเงื่อนไขมีราคา/ความร้อนไม่เหมาะสม ให้ใช้ Compact/SFF PC ได้ และไม่ใช้ USB storage เป็น Local Backup ประจำระบบ
- PC เครื่องเดียวนี้เป็น single point of failure ตามขอบเขตปัจจุบัน หากเครื่องดับ Mobile จะส่งงานเข้าสาขาไม่ได้จนกว่าบริการจะกลับมาทำงาน ระบบจึงต้องกู้คืนอัตโนมัติหลัง restart และรักษาข้อมูลที่บันทึกยืนยันแล้วไว้
- High availability หรือ PC สำรองยังไม่อยู่ในขอบเขตที่ยืนยัน หากเพิ่มภายหลังต้องออกแบบ ownership/failover ของฐานข้อมูลโดยตรง ห้ามให้แต่ละเครื่องสร้างฐานข้อมูลแยกโดยไม่มีกฎรวมข้อมูล

### 3.5 D12 — Offline consistency, transaction, sync และ recovery

#### D12.1 Source of Truth — ยืนยัน

- Branch Database เป็น source of truth ของธุรกรรมขายภายในสาขา
- Cloud เป็น source of truth ของ Master Data และค่าตั้งส่วนกลาง
- Cloud ห้ามเขียนทับธุรกรรมสาขาด้วย last-write-wins
- ตอน `OpenTable` Branch ต้องตรึง `sessionMasterDataVersion` สำหรับชื่อสินค้า ราคา โครงสร้าง Topping/Option ภาษี ส่วนลด และสูตรคำนวณของ table session นั้นจนจบบิลตาม D09; แต่ทุก `SubmitOrder` ยังต้องตรวจสถานะ session, Option และสิทธิ์ขายปัจจุบัน

#### D12.2 เมื่อ PC/Branch Service ดับ — ยืนยัน

- Mobile สร้าง/แก้ Draft และเก็บคำขอเป็น `Pending` ใน IndexedDB ได้ แต่ยังไม่ถือว่า Branch รับแล้ว ไม่ตัดสต็อก และไม่ส่งพิมพ์
- เมื่อ Branch Service กลับมา Mobile ต้อง query ด้วย `requestId` เดิมก่อน หาก Branch ยังไม่มีจึงส่งคำขอเดิม
- Phase 0 ไม่มี Mobile เครื่องใดทำหน้าที่เป็น server สำรอง

#### D12.3 Mobile หลายเครื่องส่งออร์เดอร์เข้าโต๊ะเดียวกัน — ยืนยัน Auto-merge แบบ Append

- บน Mobile การเพิ่มสินค้า เปลี่ยนจำนวน และใส่หมายเหตุเป็นการแก้ Draft ภายในอุปกรณ์เดียวกัน ยังไม่เป็น command แยกที่ส่งเข้า Branch
- เมื่อผู้ใช้กดส่ง Mobile ส่ง `SubmitOrder` เพียงคำสั่งเดียว โดย payload มีรายการสินค้า จำนวน Topping และหมายเหตุทั้งหมดของ Draft ชุดนั้น
- เมื่อ Branch ตอบ `BranchAccepted` แล้ว order batch ชุดนั้นถือเป็น immutable สำหรับ Mobile หากต้องเพิ่มสินค้าให้สร้าง Draft/`SubmitOrder` ชุดใหม่ ส่วนการยกเลิกรายการที่ส่งแล้วทำที่ PC
- **ยืนยันใช้ทางเลือก 3 — รวมอัตโนมัติ:** Mobile หลายเครื่องส่ง order batch คนละชุดเข้าโต๊ะเดียวกันพร้อมกันได้ Branch ต้อง serialize การรับและ append ทุก batch ที่ผ่าน validation เข้า bill/table session เดียวกันโดยอัตโนมัติ
- การรวมอัตโนมัติหมายถึงรวมผลเพื่อแสดงและคำนวณเป็นบิลเดียว แต่ห้ามรวม/เขียนทับ field ภายใน batch เดิม แต่ละ batch และแต่ละ line ต้องรักษาขอบเขตและที่มาของตนเอง
- ทุก batch ต้องมี `orderBatchId`, `requestId`, `deviceId`, `employeeId`, `tableSessionId`, เวลาจาก Mobile, `serverReceivedAt` และ `branchSequence`; ทุก line ต้องมี `orderLineId` ที่ unique เพื่อ trace back ได้
- `SubmitOrder` ทุกครั้งต้องมี `requestId` แบบ unique เพื่อป้องกัน batch เดิมถูก append ซ้ำ
- ทุกโต๊ะที่เปิดต้องมี `tableSessionId`; `SubmitOrder` ต้องอ้าง session นี้ และ Branch ต้องปฏิเสธหากโต๊ะถูกปิด ย้าย session หรือเปิด session ใหม่แล้ว เพื่อไม่ให้ Draft เก่าเข้าโต๊ะรอบใหม่
- ไม่ใช้ `orderVersion` conflict กับการ append order batch ปกติ Branch กำหนดลำดับด้วย `branchSequence` และคืน `tableVersion` ล่าสุดเพื่อให้ Mobile refresh มุมมอง
- เมื่อ `BranchAccepted` แล้ว Mobile แก้ไขหรือลบ batch เดิมไม่ได้ การยกเลิกรายการทำได้เฉพาะ PC โดยสร้าง cancellation event ที่อ้าง `orderBatchId`/`orderLineId`; ห้าม physical delete
- Audit/traceability ต้องค้นย้อนจากบิล → batch → line → request → device/employee และจาก `requestId` กลับมายังผลลัพธ์เดิมได้
- เมื่อ Branch รับ `RequestPayment` สำเร็จ ต้องเปลี่ยน table session จาก `Ordering` เป็น `PaymentPending` ภายใน database transaction เดียวกัน และตอบ `BranchAccepted` หลัง commit เท่านั้น
- ตั้งแต่สถานะ `PaymentPending` Branch ต้องปฏิเสธ `SubmitOrder` ใหม่จากทั้ง Mobile และ PC ด้วยผลลัพธ์ที่ระบุว่าโต๊ะกำลังชำระเงิน; Draft บน Mobile ต้องคงอยู่เพื่อให้ผู้ใช้ส่งใหม่ได้หาก PC เปิดรับออร์เดอร์อีกครั้ง
- หาก `SubmitOrder` และ `RequestPayment` มาถึงพร้อมกัน Branch ต้อง serialize ตามลำดับที่ commit: ถ้า `SubmitOrder` commit ก่อน ให้นับรายการนั้นในยอดชำระ; ถ้า `RequestPayment` commit ก่อน ให้ปฏิเสธ `SubmitOrder` โดยห้าม append บางส่วน
- การออกจาก `PaymentPending` ทำได้สองทางเท่านั้น: PC ใช้ `CompletePayment` เพื่อปิดบิล หรือ PC ใช้ `CancelPaymentRequest` เพื่อกลับเป็น `Ordering` ก่อนเพิ่ม/รับออร์เดอร์ใหม่ ทุกการเปลี่ยนสถานะต้องมี audit event
- `RequestPayment`, `CancelPaymentRequest` และ `CompletePayment` ต้องรองรับ idempotency ด้วย `requestId`; คำขอซ้ำต้องคืนผลเดิมโดยไม่เปลี่ยนสถานะหรือบันทึกธุรกรรมซ้ำ

#### D12.4 Command และขอบเขตช่องทาง — ยืนยันเบื้องต้น

การกระทำภายใน Mobile Draft ซึ่งไม่ส่งเป็น Branch command แยก:

```text
AddOrderItem
ChangeQuantity
AddComment
```

คำสั่งจาก Mobile ไป Branch:

```text
OpenTable
SubmitOrder     # ส่ง Draft ทั้งชุดเป็น order batch เดียว
RequestPayment
```

คำสั่งที่ดำเนินการที่ PC เท่านั้น โดย PC ยังสามารถทำงานขายและเพิ่มออร์เดอร์โดยตรงได้:

```text
CancelOrderItem
CancelPaymentRequest
CompletePayment
```

ทุก Branch command ต้องมีอย่างน้อย `requestId`, `branchId`, `deviceId`, `employeeId`, `tableSessionId`, `clientCreatedAt` และ payload; Branch เพิ่ม identifier ของ record/batch, `branchSequence`, `serverReceivedAt` และสถานะ/เวอร์ชันผลลัพธ์ คำสั่งที่แก้ shared state แบบไม่ใช่ append ต้องมี expected state/version ตามชนิดคำสั่ง

#### D12.5 Transaction และ Outbox — ยืนยัน

- Branch บันทึก order change, request deduplication, audit event, print outbox และ cloud-sync outbox ใน database transaction เดียวกัน
- ตอบ `BranchAccepted` หลัง transaction commit ลง persistent storage แล้วเท่านั้น
- Branch ต้องมี inbox/deduplication table, print outbox, cloud-sync outbox, retry count, next retry time, last error และ processing status

#### D12.6 การลบและยกเลิก — ยืนยันเบื้องต้น

- ห้ามลบธุรกรรมขายจริง ใช้ cancellation event/tombstone พร้อมผู้ทำ เวลา เหตุผล และ version
- สิทธิ์หลักอยู่ที่ PC สาขา โดยยกเลิกรายการสินค้าและบิลขายได้
- Mobile มีสิทธิ์สร้างคำขอตรวจสอบการยกเลิกรายการ แต่ไม่มีสิทธิ์ทำให้รายการถูกยกเลิกจริง; พนักงานเช็คอาหารตรวจสถานะการผลิตกับครัว และการยกเลิกมีผลเมื่อ PC POS บันทึกสถานะครบแล้วใช้ `ConfirmCancellation` ตาม D06 เท่านั้น โดยครัวไม่ได้เป็นผู้อนุมัติสิทธิ์ยกเลิก
- ส่วนกลางยกเลิกได้เฉพาะบิลขายสถานะ `Completed` ทั้งบิลตาม D06.2 ไม่ยกเลิกรายการสินค้ารายบรรทัด และห้ามยกเลิกบิลที่ยังดำเนินการอยู่โดยตรง
- `CentralBillCancellation` มีผลต่อสถานะและรายงานส่วนกลางเมื่อส่วนกลางบันทึก event สำเร็จ ไม่ต้องรอ Branch ออนไลน์; Branch รับสถานะแบบอ่านอย่างเดียวเมื่อซิงก์ได้ โดยไม่ย้อน Payment, เงินสด, สต็อก หรือ snapshot เดิม
- Draft ที่ไม่เคยส่งสามารถลบจริงได้ตาม retention policy ที่จะกำหนดภายหลัง
- กฎคืนเงิน คืนสต็อก เอกสารบัญชี และสิทธิ์ยืนยันขั้นสุดท้ายยังอยู่ใน D01/D06/D07

#### D12.7 การซิงก์ Cloud — ยืนยัน

- Branch ส่ง event ตาม `branchSequence`; Cloud บังคับ event ID แบบ unique และรองรับการส่งซ้ำ
- Branch เก็บ outbox จน Cloud ยืนยันการบันทึกแบบ durable และต้องส่ง sequence ที่ขาดใหม่ได้
- Cloud ส่ง Master Data เป็น version/package; Branch ตรวจความครบถ้วน/checksum และ activate แบบ atomic พร้อมเก็บ version ก่อนหน้าเพื่อ rollback
- Event ที่ยังไม่ได้รับ acknowledgment ห้ามถูกลบเพียงเพราะครบเวลา

#### D12.8 Backup, Restore, RPO และ RTO — ยืนยัน

- ธุรกรรมที่ตอบ `BranchAccepted` แล้วต้องไม่สูญหายเมื่อ process crash หรือระบบปฏิบัติการ restart
- สำรองอัตโนมัติไป Cloud เมื่อออนไลน์ และมี encrypted/versioned local backup บน Internal physical disk ลูกที่ 3 ซึ่งแยกจาก live RAID; ไม่ใช้ USB storage เป็นสื่อ Local Backup ประจำระบบ
- Backup บนดิสก์เดียวกับฐานข้อมูลหลักไม่ถือว่าป้องกัน disk failure
- ต้องทดสอบ restore จริงและตรวจจำนวนออร์เดอร์ ยอดเงิน outbox และ audit log
- ชุดข้อมูลที่อยู่ภายใต้ RPO ต้องกู้คืนอย่างสอดคล้องกัน ได้แก่ ธุรกรรมที่ `BranchAccepted`, payment/cancellation state, table session, request deduplication, audit log, print outbox, cloud-sync outbox และผลกระทบต่อสต็อก ห้ามกู้เฉพาะยอดขายโดยสูญเสียข้อมูลประกอบเหล่านี้
- เป้าหมายเมื่อ Branch Service/process ล่ม: `RPO = 0`, `RTO <= 5 นาที`
- เป้าหมายเมื่อระบบปฏิบัติการ restart หรือไฟดับตามปกติ: `RPO = 0`, `RTO <= 15 นาที`
- เป้าหมายเมื่อ SSD หรือ PC เสียทั้งเครื่องขณะออนไลน์: `RPO <= 5 นาที`, `RTO <= 4 ชั่วโมง` โดยต้องมี Cloud backup/sync ที่ตรวจสอบได้และมี hardware ทดแทนพร้อม
- เป้าหมายเมื่อ SSD หรือ PC เสียทั้งเครื่องขณะอินเทอร์เน็ตขาด: `RPO <= 15 นาที`, `RTO <= 4 ชั่วโมง` โดยต้องสำรองอย่างน้อยทุก 15 นาทีไปยังสื่อคนละ physical device และมี hardware ทดแทนพร้อม
- หากไม่มี hardware ทดแทนหรือสื่อสำรองตามเงื่อนไข จะไม่สามารถรับรอง `RTO <= 4 ชั่วโมง` หรือ RPO ของกรณีเครื่องเสียทั้งเครื่องได้ ต้องแสดงข้อจำกัดนี้ในแผนปฏิบัติการสาขา
- รุ่น/ชนิด/ความจุของ Internal Backup disk ยังต้องเลือกและทดสอบก่อน production แต่ต้องเป็นคนละ physical device และไม่เป็นสมาชิกของ live RAID

ประมาณการ Capacity ต่อสาขาจากข้อมูลตัวอย่างปัจจุบัน:

- ฐานคำนวณคือ 8,156 บิลใน 31 วัน หรือเฉลี่ยประมาณ 263 บิล/วัน และ Activity Log ที่ตรวจสอบความถูกต้องได้ 4 วัน ซึ่งมีประมาณ 3.69 activity events และ 5.66 product-detail records ต่อบิล
- ปริมาณฐานข้อมูลก่อนเผื่อ capacity ประมาณ `100–300 MB/เดือน` รวม transaction, order/payment, activity/audit, deduplication, print/cloud outbox, indexes และ database overhead แต่ไม่รวมรูปภาพ/ไฟล์แนบและ application log ที่เก็บเกิน retention
- **ยืนยันเผื่อ Capacity เพิ่ม 15%:** ใช้ค่าประมาณสำหรับวางแผน `115–345 MB/เดือน/สาขา` หรือ `1.38–4.14 GB/ปี/สาขา` สำหรับข้อมูลใหม่
- เพื่อใช้ขนาดจัดสรรที่ปฏิบัติได้ ให้ปัดเป็น `120–350 MB/เดือน` และ `1.5–4.2 GB/ปี` ต่อสาขา
- Local backup usable capacity ขั้นต่ำคำนวณจาก 20 GB + 15% = 23 GB จึงกำหนดขนาดจัดสรรขั้นต่ำ `25 GB/สาขา`
- Cloud backup usable capacity เริ่มต้นคำนวณจาก 30–50 GB + 15% = 34.5–57.5 GB จึงกำหนดขนาดจัดสรรเริ่มต้น `40–60 GB/สาขา`
- Capacity ข้างต้นเป็น planning baseline ไม่ใช่เพดานถาวร ต้องวัดจากฐานข้อมูลและไฟล์ backup จริงหลังใส่ข้อมูลจำลองอย่างน้อย 10,000 บิล และทบทวนใหม่เมื่อกำหนด retention, compression, จำนวน full snapshot และการเก็บ application log

**หมายเหตุสถานะการตัดสินใจ Backup:** แนวทางระดับสถาปัตยกรรมได้รับการยืนยันแล้ว แต่ Backup Specification ยังไม่พร้อมสำหรับการจัดซื้อหรือติดตั้งจริง และให้พักการตัดสินใจรายละเอียดต่อไปนี้ไว้ก่อน

- ระยะเวลาเก็บ Daily, Weekly และ Monthly Backup
- อุปกรณ์และสถานที่สำหรับ Offline/Offsite copy
- จำนวน HDD, RAID level และ usable capacity ของ NAS
- RPO/RTO ของระบบ Master Data ส่วนกลาง
- ผู้ถือกุญแจเข้ารหัสและขั้นตอนกู้คืนกุญแจ
- ความถี่และขั้นตอนการทดสอบ Restore
- Database engine, backup format และเครื่องมือ Backup ที่ใช้จริง
- วิธีทำ immutable snapshot และการป้องกันไม่ให้สาขาแก้ไขหรือลบ Backup เก่า

ข้อสรุปที่คงไว้ระหว่างพักรายละเอียดคือ NAS ส่วนกลางเก็บ Master Data Backup และ Branch Recovery Bundle แยกคนละ Storage Pool; แต่ละ Pool ใช้ดิสก์แบบ Mirror/RAID; มี Offline หรือ Offsite copy เพิ่มอีกหนึ่งชุด; สาขาสื่อสารผ่าน VPN พร้อม TLS และแยกสิทธิ์รายสาขา; Master Data ที่ Publish แล้วไหลจากส่วนกลางไปสาขาทางเดียว โดยสาขาเก็บ Active และ Previous Version สำหรับ Offline operation/rollback และไม่มีรูปสินค้า/รูปเมนูในขอบเขต

#### D12.9 เวลาและลำดับข้อมูล — ยืนยัน

- ใช้เวลาของ Branch Service เป็นเวลาทางธุรกิจ; เวลา Mobile เป็นข้อมูลอ้างอิงเท่านั้น
- ใช้ `branchSequence` เป็นลำดับ event ห้ามใช้ timestamp จาก Mobile เป็น unique ID
- เก็บเวลาเป็น UTC และแสดงตาม `Asia/Bangkok`
- ร้านเปิดและปิดภายในวันเดียวกัน จึงกำหนด business date ตามวันปฏิทินท้องถิ่นของสาขา ไม่มีกะที่ข้ามเที่ยงคืนในขอบเขตปัจจุบัน
- ต้องตรวจและแจ้งเตือนเมื่อเวลา Branch PC ต่างจากแหล่งเวลาเกินเกณฑ์ที่ยังต้องกำหนด

#### D17 — Technology Stack และรูปแบบติดตั้ง — ยืนยัน

Technology Stack ที่ล็อกสำหรับเริ่มโครงสร้างโปรเจกต์:

| ส่วน | ข้อสรุป |
|---|---|
| PC POS | Web application เปิดด้วย Edge/Chromium แบบ kiosk; ไม่ใช้ Electron |
| Mobile | Phase 0 ใช้ React + TypeScript แบบ PWA เพื่อพิสูจน์ workflow/offline ได้; เป้าหมายต้องรองรับ Mobile application บน Android และ iOS โดยรูปแบบ Native, Cross-platform หรือ Installable PWA ยังต้องตัดสินใจก่อนล็อก production stack |
| Back Office | React + TypeScript |
| Frontend build | Vite; ใช้ OpenAPI-generated TypeScript client และ IndexedDB สำหรับ Mobile draft/outbox |
| Branch/Central backend | C# + ASP.NET Core บน .NET 10 LTS |
| Branch database | SQLite แบบ WAL บน local RAID 1; Branch Service เป็นผู้เปิดฐานข้อมูลเพียง process เดียว |
| Central database | PostgreSQL 18 |
| API | REST/JSON ผ่าน HTTPS, OpenAPI, schema/API version และ `requestId` สำหรับคำสั่งเปลี่ยนข้อมูล |
| Printing | Direct Network PrintGateway; ไม่ใช้ `PrinterInterface_335.exe` |
| Central deployment | Linux server/VM หรือ container; database อยู่บน application/database host และส่ง backup ไป NAS |
| Branch deployment | Cross-platform self-contained build; Windows Service สำหรับเครื่องเดิม และ Linux systemd package สำหรับ pilot/new Mini PC |

ข้อกำหนดเชิงโครงสร้าง:

- Domain/Application layer ต้องไม่อ้าง Windows Registry, WMI, drive letter, systemd, Windows Service หรือคำสั่งเครื่องพิมพ์เฉพาะรุ่น
- แยก `Pos.Printing.Abstractions`, `Pos.Printing.EscPosTcp`, `Pos.Printing.Ipp`, `Pos.Platform.Windows` และ `Pos.Platform.Linux`
- UI และ domain logic ห้ามเปิด SQLite หรือส่งข้อมูลไปเครื่องพิมพ์โดยตรง ทุกคำสั่งต้องผ่าน Branch API/Application layer
- Branch Service ต้องบันทึก transaction, audit และ persistent outbox ภายใน transaction boundary ที่กำหนดใน D12/D20
- SQLite ต้องอยู่บน local storage เท่านั้น ห้ามวาง live database บน NAS/network share; ใช้ WAL, durability policy, checkpoint, integrity check และ SQLite Backup API
- เปลี่ยน Branch database เป็น client/server database เมื่อมี Branch Service หลาย instance, writer หลาย process, ต้องทำ database HA หรือผล load test ไม่ผ่าน

Printing stack:

- `IPrintGateway` ต้องรองรับ adapter อย่างน้อย `EscPosTcpPrintGateway` และ `IppPrintGateway`; LPR ใช้ได้เฉพาะรุ่นที่จำเป็น
- ค่า IP, port, protocol, paper width, DPI, Thai encoding, cutter, cash drawer, status command และ timeout/retry อยู่ใน Printer Profile รายรุ่น ห้าม hard-code ใน business logic
- เส้นทางหลักที่เสนอคือ ESC/POS ผ่าน TCP port 9100 เฉพาะเครื่องที่พิสูจน์ว่ารองรับ; การมี RJ45 เพียงอย่างเดียวไม่ถือเป็นหลักฐานว่าใช้ ESC/POS/9100 ได้
- Printer อยู่ใน VLAN/segment ของสาขาและ firewall อนุญาตเฉพาะ Branch PC; Mobile ห้ามเชื่อม Printer โดยตรง
- การส่งลง socket สำเร็จรับรองได้เพียง `PrintDispatched`; ห้ามถือว่า `Completed` หากไม่มี status protocol ที่ยืนยันผลจริง

รูปแบบระบบปฏิบัติการและ rollout:

- Core, Branch API, SQLite, sync, backup และ PrintGateway ต้อง build/test ได้ทั้ง `win-x64` และ `linux-x64`
- เครื่อง AIO เดิมใช้ Windows 11 Pro ใน Phase 0; Mini PC ใหม่ทดลอง Linux แบบ pinned LTS distribution อย่างน้อยหนึ่งสาขาก่อนกำหนดเป็น production standard
- Linux ใช้ systemd, Chromium kiosk, mdadm RAID 1 และ LUKS; Windows ใช้ Windows Service, Edge kiosk, RAID ที่ผู้ผลิตรองรับ และ BitLocker
- ไม่ใช้ Docker ที่ Branch ในระยะแรก; Central สามารถใช้ container ได้
- Linux จะถือว่า officially supported หลังผ่าน printer compatibility, 20-Mobile load, HTTPS/Private CA, RAID degraded/rebuild, power-loss, backup/restore, update/rollback และ operational pilot
- การรองรับสอง OS ไม่ลดข้อกำหนด QA: release ต้องมี artifact, signature/checksum, application/API/schema/printer-profile version, migration, backup ก่อน upgrade และ rollback policy แยกตาม OS

ข้อกำหนด Mobile เพิ่มเติม:

- Android และ iOS ต้องเป็น first-class supported clients ของระบบ ไม่ใช่รองรับแบบ best-effort ผ่าน browser เท่านั้น
- ฟังก์ชันหลักและกฎทางธุรกิจต้องอยู่ที่ Branch API/Application layer ห้ามแยก implementation จน Android และ iOS ตัดสินผลธุรกิจต่างกัน
- ต้องมี automated contract/integration tests ชุดเดียวกันสำหรับ client ทั้งสองระบบ และทดสอบอุปกรณ์จริงอย่างน้อยหนึ่งรุ่นต่อระบบตั้งแต่ Phase 0
- ก่อนล็อก production stack ต้องเปรียบเทียบ Installable PWA, Cross-platform app และ Native app ตาม offline persistence, background/resume, QR pairing, certificate/private CA, device credential, deployment/update, printer isolation และต้นทุนดูแลระยะยาว

### 3.6 D20 — State Machine, identifier และ recovery — ยืนยัน

D20 แยก state machine อย่างน้อยสามชุด ห้ามรวมสถานะการส่งคำขอ สถานะโต๊ะ และสถานะงานพิมพ์เป็นค่าเดียว เพราะแต่ละส่วนสำเร็จหรือล้มเหลวแยกกันได้

#### D20.1 Mobile Request

```text
Draft → Pending → Submitted
                      ├──→ BranchAccepted
                      ├──→ Rejected
                      └──→ Unknown ──status query──→ BranchAccepted / Rejected / Pending
```

- `Draft` อยู่บน Mobile และยังไม่เริ่มส่ง; `Pending` อยู่ใน persistent local outbox; `Submitted` เริ่มส่งแล้วแต่ยังไม่มีผลยืนยัน
- `BranchAccepted` เกิดเมื่อ Branch commit คำสั่งและ outbox ที่เกี่ยวข้องลง persistent storage สำเร็จแล้ว
- `Rejected` ใช้เมื่อ Branch ปฏิเสธอย่างชัดเจน เช่น table session อยู่ใน `PaymentPending`; Draft/ข้อมูลต้นทางบน Mobile ต้องไม่สูญหาย
- `Unknown` หมายถึง Mobile ไม่ทราบผลหลังเริ่มส่ง ไม่ใช่สถานะธุรกรรมจริง ต้อง query ด้วย `requestId` เดิมและห้ามสร้างออร์เดอร์ใหม่
- `AddOrderItem`, `ChangeQuantity`, `AddComment` และ `SubmitOrder` จากการกดส่งหนึ่งครั้งเป็น `SubmitOrder` batch เดียว Branch ต้องรับหรือปฏิเสธทั้งชุด ห้าม partial commit

#### D20.2 Table Session

```text
Available ──OpenTable──→ Ordering
Ordering ──SubmitOrder──→ Ordering
Ordering ──ChangeTable (PC; ปลายทาง Available)──→ Ordering [คง tableSessionId]
Ordering ──RequestPayment──→ PaymentPending
Ordering ──CloseTable (PC; ไม่มีรายการ/ยอดค้าง)──→ Closed
PaymentPending ──CancelPaymentRequest (PC)──→ Ordering
PaymentPending ──StartPayment (PC)──→ PaymentInProgress
PaymentInProgress ──PaymentFailed / CancelPayment──→ PaymentPending
PaymentInProgress ──ยืนยัน Payment Line แต่ยอดยังไม่ครบ──→ PartiallyPaid
PartiallyPaid ──ยืนยันยอดที่เหลือครบ──→ Completed
PartiallyPaid ──ย้อน Payment Line ที่ยืนยันแล้วทั้งหมด──→ PaymentPending
PaymentInProgress ──CompletePayment (PC)──→ Completed
Ordering / PaymentPending ──CancelBill (ตามสิทธิ์)──→ Cancelled
```

- `SubmitOrder` รับได้เฉพาะ `Ordering`; หลัง Branch commit `RequestPayment` แล้วต้องปฏิเสธ `SubmitOrder` ที่มาภายหลังทั้ง batch
- `RequestPayment` ต้องถูกปฏิเสธหากยังมี cancellation request สถานะ `CancellationRequested`, `ProductionStatusChecking` หรือ `StatusRecorded` ที่ยังไม่ปิดด้วย `ConfirmCancellation`/`RejectCancellation`
- `CancelPaymentRequest` กลับไป `Ordering` ได้ก่อนเริ่มรับเงิน; เมื่อเป็น `PaymentInProgress` การล้มเหลวหรือยกเลิกการรับเงินให้กลับ `PaymentPending` ไม่กลับ `Ordering` โดยอัตโนมัติ
- D04.3 ยืนยัน SplitTender แล้ว จึงใช้ `PartiallyPaid` เมื่อมี Payment Line ที่ยืนยันแล้วแต่ยอดรวมยังไม่ครบ; ห้ามกลับ `Ordering` และห้ามลบ Payment Line ที่ยืนยันแล้ว ต้องใช้ reversal event
- เมื่อ `SubmitOrder` แข่งกับ `RequestPayment` ให้ผลตามลำดับ commit ของ Branch: batch ที่ commit ก่อนรวมในยอด ส่วน batch ที่ commit หลัง `PaymentPending` ถูกปฏิเสธ
- `ChangeTable` เปลี่ยนเฉพาะ `currentTableId` ของ session เดิม จึงไม่สร้าง session/บิลใหม่และไม่เปลี่ยนส่วนลดท้ายบิล; โต๊ะต้นทางเป็น `Available` เมื่อ commit สำเร็จ
- `TransferItems` ย้ายรายการข้าม `tableSessionId`; session ต้นทางและปลายทางยังคงเป็นคนละ session และใช้กฎส่วนลดของตนเอง
- หลัง `Completed` หรือ `Cancelled` ห้ามเพิ่ม/แก้รายการใน `tableSessionId` เดิม
- `CloseTable` ทำได้เฉพาะ PC POS เมื่อ session ไม่มีรายการคงเหลือและไม่มียอดค้างชำระ; เมื่อสำเร็จ session เป็น `Closed` และโต๊ะเป็น `Available` หากยังมีรายการต้องใช้กระบวนการยกเลิกบิลแทน
- การยกเลิกรายการสินค้าทำที่ PC เท่านั้น; การยกเลิกสินค้า/บิลสร้าง immutable cancellation event พร้อมผู้ดำเนินการ เวลา เหตุผล และข้อมูลอ้างอิง ห้ามลบหรือเขียนทับ payload ต้นฉบับ

#### D20.3 Print Job

```text
PrintQueued → Dispatching
                 ├──→ PrintDispatched
                 ├──→ Failed ──safe retry──→ PrintQueued
                 └──→ Unknown ──manual reprint──→ ReprintQueued
```

- มี persistent queue และ worker แบบ serialize แยกต่อเครื่องพิมพ์
- `Failed` ใช้เมื่อมีหลักฐานว่ายังไม่เริ่มส่งผลทางกายภาพ เช่นเชื่อมต่อไม่ได้ก่อนส่ง byte แรก จึง retry อัตโนมัติได้ตาม policy
- `Unknown` ใช้เมื่อ timeout/connection loss เกิดหลังเริ่มส่ง เพราะอาจมีกระดาษออกแล้ว ห้าม retry อัตโนมัติ
- การพิมพ์ใหม่จาก `Unknown` ต้องสั่งจาก PC สร้าง `reprintJobId`/print job ใหม่ที่อ้างอิงงานเดิม และบันทึกผู้สั่ง เหตุผล และเวลา
- Phase 0 ใช้ `PrintDispatched` เป็นสถานะอัตโนมัติสูงสุด การส่ง payload ลง TCP สำเร็จไม่ใช่หลักฐานว่ากระดาษออกจริง; พักการใช้ `Completed` ของงานพิมพ์จนกว่าจะทดสอบ status capability ของ Epson TM-T82 รุ่นจริง

#### D20.4 Identifier และ invariant

| Identifier | ขอบเขต |
|---|---|
| `tableSessionId` | รอบการใช้งานโต๊ะหนึ่งรอบ |
| `requestId` | คำสั่งหนึ่ง batch จาก Mobile และ idempotency ที่ Branch |
| `orderBatchId` | ชุดรายการ immutable ที่ Branch รับแล้ว |
| `printJobId` | เอกสารหนึ่งชุดต่อเครื่องพิมพ์หนึ่งเครื่อง |
| `printAttemptId` | ความพยายาม dispatch แต่ละครั้ง |
| `paymentId` | ธุรกรรมชำระเงิน |
| `cancellationId` | เหตุการณ์ยกเลิก |

- `requestId` ต้อง unique ภายในสาขา; การส่งค่าเดิมซ้ำต้องคืนผลเดิมและไม่สร้าง order batch ซ้ำ
- ออร์เดอร์หนึ่ง batch ที่ต้องออกหลายสถานีใช้ `requestId` เดียว แต่มี `printJobId` แยกต่อเครื่อง เพราะผลการส่งแต่ละเครื่องเป็นอิสระ
- การ commit ธุรกรรมธุรกิจ, audit record และ persistent outbox ที่จำเป็นต้องอยู่ใน transaction boundary เดียวกัน
- ทุก transition เก็บ Branch time, `branchSequence`, actor, device และเหตุผลตามที่เกี่ยวข้อง และต้องกู้สถานะที่ commit แล้วกลับได้หลัง process/OS restart

### 3.7 Repository, Open Source และ Local Workspace — ยืนยัน

- ทุกองค์ประกอบของโครงการต้องอยู่ภายใต้ Git และมี Remote Repository เดียวในรูปแบบ Monorepo ตาม D21.1
- หาก remote repository มีอยู่แล้ว ให้สร้าง working copy ด้วย `git clone`; ห้าม `git init` ซ้ำแล้วสร้างประวัติอิสระในโฟลเดอร์เดียวกัน
- เผยแพร่ Source Code ของโครงการภายใต้ **Mozilla Public License 2.0 (MPL-2.0)** ตาม D21.2; การทำ Repository เป็น Public อย่างเดียวไม่ทดแทนการใส่ License และ Copyright Notice ที่ถูกต้อง
- ก่อน Public release ต้องตรวจ license ของ dependency, สร้าง SBOM และกำหนด policy สำหรับ source/binary redistribution, copyright notice และ third-party notices
- ห้าม commit runtime database, WAL/SHM, transaction/customer/employee data, backup, log จริง, private key, certificate secret, token, password, `.env`, signing key หรือ binary ภายนอกที่ไม่มีสิทธิ์แจกจ่าย
- repository ต้องมี sanitized example configuration และ test fixture แทนข้อมูลจริง; IP, hostname, credential และข้อมูลสาขาจริงต้องเป็น deployment configuration ภายนอก Git
- Development workspace หลักต้องอยู่บน non-system volume เช่น `D:\Dev\NicePOS` หรือ volume อื่นที่กำหนด ไม่ใช้ `C:\Downloads`, user temporary folder หรือ runtime deployment directory เป็น working repository หลัก
- Source code ห้าม hard-code drive letter; path ของ source, runtime data, log, certificate และ backup ต้องกำหนดผ่าน configuration/environment และรองรับ Windows/Linux
- ตำแหน่งติดตั้ง/runtime ต้องแยกจาก Git working copy เช่น source อยู่ `D:\Dev\NicePOS` ขณะที่ runtime data อยู่ใน volume/path ที่กำหนดสำหรับบริการ
- การวาง source บน `D:` ช่วยให้ติดตั้งระบบปฏิบัติการใหม่โดยไม่กระทบ working copy เมื่อ volume ยังสมบูรณ์ แต่ไม่ถือเป็น Backup หาก `C:` และ `D:` เป็น partition ของ physical disk ลูกเดียวกัน; source กู้จาก Git ได้ ส่วน runtime data ยังต้องใช้ RAID และ Local/Offsite Backup ตาม D12/NFR-05
- `main` ต้องเป็น protected branch; งานพัฒนาใช้ feature branch, Pull Request และ CI ที่ผ่านก่อน merge โดยห้ามเก็บ build artifact หรือข้อมูล runtime ใน Git history

#### D21.1 — Repository Topology — ยืนยัน

- ใช้ **Monorepo** หนึ่ง Repository สำหรับระบบ POS ทั้งโครงการ เพื่อให้ Contract, Schema, Version และการทดสอบข้ามองค์ประกอบเปลี่ยนพร้อมกันได้
- โครงสร้างเริ่มต้นของ Repository กำหนดดังนี้

```text
pos-platform/
├─ apps/
│  ├─ pc-pos/
│  ├─ mobile/
│  └─ back-office/
├─ services/
│  ├─ branch-service/
│  ├─ central-service/
│  └─ print-gateway/
├─ packages/
│  ├─ contracts/
│  ├─ schemas/
│  └─ ui/
├─ tests/
│  ├─ integration/
│  ├─ end-to-end/
│  ├─ performance/
│  └─ hardware/
├─ deploy/
│  ├─ windows/
│  ├─ linux/
│  ├─ docker/
│  └─ database/
├─ docs/
├─ tools/
├─ .github/
├─ LICENSE
├─ SECURITY.md
├─ CONTRIBUTING.md
└─ README.md
```

- `packages/contracts` และ `packages/schemas` เป็นแหล่งอ้างอิงกลางของ API, Event, Command และข้อมูลที่แลกเปลี่ยนระหว่าง Mobile, PC POS, Branch Service, Central Service และ PrintGateway
- การเปลี่ยน Contract หรือ Schema ต้องมี Compatibility Check และ Integration Test ใน Pull Request เดียวกับองค์ประกอบที่ได้รับผลกระทบ
- แยก Hardware Test ออกจาก CI ปกติอย่างชัดเจน การทดสอบที่ติดต่อเครื่องพิมพ์หรือลิ้นชักเงินจริงต้องเริ่มด้วยคำสั่งยืนยันเฉพาะและห้ามรันอัตโนมัติบน Runner ทั่วไป
- แต่ละ App/Service มี Version และ Release Artifact ของตนเอง แม้อยู่ใน Repository เดียวกัน เพื่อไม่บังคับให้ทุกองค์ประกอบต้อง Deploy พร้อมกัน
- ใช้ Feature Branch, Pull Request, Automated Test และ Protected Main Branch; ห้าม Commit Secret, Certificate Private Key, Production Database, Runtime Log, Backup หรือ Build Artifact
- Workspace หลักควรอยู่บนไดรฟ์ที่ไม่ใช่ System Drive แต่ Script และ Configuration ต้องไม่ผูกกับอักษรไดรฟ์ตายตัว

#### D21.2 — Open-source License — ยืนยัน

- Source Code ที่โครงการเป็นเจ้าของใน Monorepo ใช้ **Mozilla Public License 2.0 (MPL-2.0)**
- Repository ต้องมีไฟล์ `LICENSE` ซึ่งใช้ข้อความ MPL 2.0 ฉบับทางการครบถ้วนโดยไม่ดัดแปลง และระบุ SPDX Identifier เป็น `MPL-2.0`
- ไฟล์ Source ที่อยู่ภายใต้ License ต้องมี License Notice ตาม Exhibit A หรือรูปแบบ Header/REUSE ที่ให้ผลเทียบเท่าและตรวจสอบอัตโนมัติได้
- เมื่อแจกจ่าย Executable หรือ Source ที่แก้ไขไฟล์ซึ่งอยู่ภายใต้ MPL ผู้แจกจ่ายต้องเปิดเผย Source ของไฟล์ที่อยู่ภายใต้ MPL และไฟล์ที่แก้ไขตามเงื่อนไขของ MPL; ขอบเขต Copyleft อยู่ในระดับไฟล์ ไม่บังคับให้ไฟล์ใหม่ทั้งหมดใน Larger Work ต้องใช้ MPL
- Code จากบุคคลภายนอกยังคงใช้ License เดิมของเจ้าของสิทธิ์ ห้ามเปลี่ยนเป็น MPL โดยไม่มีสิทธิ์ และต้องบันทึกไว้ใน `THIRD_PARTY_NOTICES` กับ SBOM
- ก่อน Merge Dependency ใหม่ ต้องตรวจ License Compatibility กับ MPL-2.0 รวมถึงข้อจำกัดในการแจกจ่ายบน Windows, Linux, Android และ iOS
- Build Artifact และ Release Package ต้องแนบหรือชี้ไปยัง License Notice และวิธีเข้าถึง Corresponding Source ตามที่ MPL กำหนด
- MPL-2.0 ไม่ให้สิทธิ์ใช้ชื่อโครงการ โลโก้ หรือเครื่องหมายการค้าโดยอัตโนมัติ; Policy ของชื่อและเครื่องหมายการค้าต้องกำหนดแยกก่อน Public Release
- License ของเอกสาร รูปภาพ ตัวอย่างข้อมูล และทรัพย์สินที่ไม่ใช่ Source Code ต้องกำหนดแยกใน Public-release Checklist; ห้ามถือว่า MPL ครอบคลุมโดยอัตโนมัติโดยไม่มีประกาศชัดเจน

#### D21.3 — Public-release Checklist — ยืนยัน

- ก่อนเปลี่ยน Repository เป็น Public ต้องตรวจ Secret ทั้ง Working Tree และ Git History รวมถึง Token, Password, `.env`, Private Key, Certificate Key และ Signing Key; หากข้อมูลลับเคยถูก Commit ต้องลบออกจาก History และ Rotate Credential เดิมก่อนเปิด Public
- ห้ามมีข้อมูลลูกค้า พนักงาน ธุรกรรม ยอดขาย Production Database, Backup, Runtime Log, IP, Hostname หรือข้อมูลสาขาจริง; Test Fixture และ Example Configuration ต้องเป็นข้อมูลจำลองหรือผ่านการทำให้ไม่สามารถระบุตัวจริงได้
- ต้องมี `LICENSE` ฉบับ MPL 2.0, License Notice/SPDX ที่ตรวจสอบได้, `THIRD_PARTY_NOTICES` และผลตรวจ License ของ Dependency
- ต้องสร้าง SBOM ที่ผูกกับ Release Artifact ของ PC POS, Branch Service, Central Service, PrintGateway, Android, iOS และ Container Image ตามองค์ประกอบที่ Release จริง
- ทุกองค์ประกอบต้อง Build ได้จาก Clean Clone โดยไม่พึ่งไฟล์เฉพาะในเครื่องผู้พัฒนา; Android/iOS Build ห้ามรวม Signing Key และ Configuration ตัวอย่างต้องไม่มี Credential จริง
- Unit, Contract, Integration และ End-to-End Test ที่เกี่ยวข้องต้องผ่านก่อน Release; Hardware Test ต้องแยกจาก CI ปกติและห้ามติดต่อเครื่องพิมพ์หรือลิ้นชักเงินจริงโดยปริยาย
- ต้องทำ Dependency, Source, Secret และ Container Scan; ช่องโหว่ระดับ Critical/High ต้องได้รับการแก้ไข หรือมี Risk Acceptance พร้อมเหตุผล เจ้าของความเสี่ยง มาตรการควบคุม และกำหนดแก้ไขก่อน Release
- เอกสารขั้นต่ำประกอบด้วย `README.md`, `SECURITY.md`, `CONTRIBUTING.md`, วิธีติดตั้ง/พัฒนา, Architecture, ขอบเขต Component และช่องทางรายงานช่องโหว่แบบไม่เปิดเผยต่อสาธารณะก่อนแก้ไข
- `main` ต้องมี Branch Protection; Merge ผ่าน Pull Request และ CI พร้อมกำหนดผู้อนุมัติสำหรับ Contract, Payment, Security และ Printing ตามความเสี่ยง
- แต่ละ Component มี Version ของตนเอง และ Release ต้องมี Changelog, Checksum, Migration Note (ถ้ามี) รวมถึงการเชื่อมโยงกลับไปยัง Commit และ SBOM ที่ใช้สร้าง Artifact
- License ของเอกสาร รูปภาพ ตัวอย่างข้อมูล ชื่อโครงการ โลโก้ และเครื่องหมายการค้าให้กำหนดแยกใน D21.4
- การอนุมัติเปิด Public ต้องบันทึก Checklist, ผู้ตรวจ, วันที่, Commit SHA และผลการตรวจให้ตรวจย้อนหลังได้ ก่อนเปลี่ยน Visibility ของ Repository

#### D21.4 — License ของเนื้อหาและเครื่องหมายการค้า — ยืนยัน

- Source Code, Script, Schema และ UI Source ที่โครงการเป็นเจ้าของใช้ `MPL-2.0`
- เอกสาร คู่มือ แผนภาพ และภาพหน้าจอที่โครงการสร้างเองใช้ `CC BY-SA 4.0`; การนำไปใช้หรือดัดแปลงต้องระบุที่มา ระบุการเปลี่ยนแปลง และเผยแพร่งานดัดแปลงภายใต้ License เดียวกัน
- ข้อมูลตัวอย่างและ Test Fixture ที่เป็นข้อมูลสังเคราะห์ใช้ `CC0-1.0` เพื่อลดข้อจำกัดในการนำไปใช้กับ Test และเครื่องมืออัตโนมัติ
- ชื่อโครงการ โลโก้ และเครื่องหมายการค้าไม่รวมอยู่ในสิทธิ์ที่ให้ตาม MPL/CC และต้องกำหนดนโยบายใน `TRADEMARKS.md`
- อนุญาตให้อ้างชื่อโครงการตามข้อเท็จจริง แต่ห้ามใช้ชื่อหรือโลโก้ในลักษณะที่ทำให้เข้าใจว่า Fork, Distribution หรือบริการของบุคคลอื่นเป็นผลิตภัณฑ์ทางการหรือได้รับการรับรอง
- ทรัพย์สินจากบุคคลภายนอกต้องคง License และ Notice เดิม บันทึกไว้ใน `THIRD_PARTY_NOTICES` และห้ามเปลี่ยน License โดยไม่มีสิทธิ์จากเจ้าของ
- Repository ต้องระบุขอบเขต License ของแต่ละกลุ่มไฟล์อย่างชัดเจน เช่นผ่าน `LICENSES/`, SPDX Identifier, File Header หรือไฟล์ Notice ประจำ Directory เพื่อไม่ให้เกิดข้อสันนิษฐานว่า License หนึ่งครอบคลุมทุกประเภททรัพย์สิน

#### D21.5 — Local Workspace และ Repository Bootstrap — ยืนยัน

- จากการตรวจเครื่องพัฒนา ณ วันที่กำหนดข้อ requirement พบว่า `C:` เป็น Fixed Drive เพียงไดรฟ์เดียว ส่วน `D:` เป็น FAT32 Removable Drive และ `E:` เป็น NTFS Removable Drive ที่ Windows รายงานสถานะ `Warning`; จึงห้ามใช้ `D:` หรือ `E:` เป็น Workspace หลัก
- รอเพิ่ม Internal SSD ก่อนกำหนด Drive Letter จริง โดยวางโครงสร้างเป้าหมายเป็น `<InternalDrive>:\Dev\pos-platform`, `<InternalDrive>:\NicePOSData\dev` และ `<InternalDrive>:\NicePOSCache`
- Source Code ห้ามอ้างอักษรไดรฟ์ตายตัว ให้กำหนดรากผ่าน `POS_DEV_ROOT`, `POS_DATA_ROOT` และ `POS_CACHE_ROOT` หรือ Configuration ที่ให้ผลเทียบเท่า
- Remote Repository ที่สร้างไว้แล้วต้องนำมาเป็น Working Copy ด้วย `git clone`; ห้ามใช้ `git init` สร้างประวัติใหม่ซ้ำ
- `C:\Users\qtaro\Downloads\Niceloop_dev` เป็นพื้นที่จัดเตรียม Requirements ชั่วคราว ไม่ใช่ตำแหน่ง Monorepo หลัก
- ก่อนเริ่มพัฒนาใน Working Copy หลัก ต้องตรวจ Remote URL และ Default Branch, Clone Repository, สร้างโครงสร้าง Monorepo, เพิ่ม License/เอกสารพื้นฐาน, ทำ Secret Scan ก่อน Baseline Commit และเปิด Branch Protection หลัง Push Baseline
- Source, Runtime Data และ Backup ต้องแยกทางตรรกะอย่างชัดเจน; แม้ Development Source/Data/Cache จะอยู่บน Internal SSD ลูกเดียวกันในระยะแรก SSD ลูกนั้นไม่ถือเป็น Backup
- เมื่อติดตั้ง Internal SSD แล้ว ต้องบันทึก Drive Letter, Volume Label, File System, ตำแหน่ง Workspace จริง และผลทดสอบ Clone/Build จาก Clean Working Copy ก่อนปิดรายการ D21.5 ทางปฏิบัติการ

## 4. PC POS: หน้าจอและฟังก์ชัน

### 4.1 เมนูหลัก

ภาพอ้างอิงเดิมมี Home, การขาย/โต๊ะ, ค่าใช้จ่าย, สต็อก, รายงาน, เมนู, ตั้งค่า และจัดการใบเสร็จ

#### D18.1 — Navigation หลักและหน้าโต๊ะของ PC POS — ยืนยัน

- Navigation หลักของ PC POS มี `หน้าหลัก`, `โต๊ะ`, `ประวัติธุรกรรม`, `ร้านและกะ` และ `สถานะระบบ`
- หน้า `โต๊ะ` ใช้ Split View ในหน้าเดียว: ด้านหนึ่งแสดงโซนและผังโต๊ะ อีกด้านแสดง Order Line, Topping, หมายเหตุ ยอด และคำสั่งของโต๊ะที่เลือก โดยไม่ต้องเปลี่ยนไปหน้า Payment แยก
- Workflow เพิ่มรายการ ย้ายโต๊ะ/รายการ แยกรายการ/บิล ส่วนลด หมายเหตุ ตรวจยกเลิก และชำระเงินต้องอยู่ภายใต้ `tableSessionId` ของโต๊ะที่เลือก
- Payment ไม่เป็น Navigation หลักแยกต่างหาก และระบบมีช่องทางขายเฉพาะโต๊ะตาม CORE-04 จึงไม่มี TOGO/Delivery ในหน้า PC POS
- ไม่แสดงค่าใช้จ่าย สต็อก รายงานรวมหลายสาขา การจัดการเมนู/Master Data การตั้งค่าส่วนกลาง ผู้ใช้ส่วนกลาง Refund หรือการยกเลิกบิลที่ชำระแล้วใน Navigation หลัก งานเหล่านี้อยู่ใน Back Office หรือขอบเขตหน้าที่แยกตามข้อกำหนดของแต่ละฟังก์ชัน
- `หน้าหลัก` แสดงสถานะร้าน/กะ Notify สถานะ Online/Offline เวลาซิงก์ล่าสุด และงานค้างที่ต้องดำเนินการ
- `ประวัติธุรกรรม` ใช้กฎ D01.4–D01.4.1; การเปิดหน้าไม่ต้องใช้ PIN และการ Reprint ยังตรวจสิทธิ์ตามประเภทเอกสาร
- `ร้านและกะ` แสดงสถานะปัจจุบันและคำสั่ง `OpenStore`, `OpenShift`, `CloseShift`, `CloseStore`; การเปิดดูหน้าไม่ต้องใช้ PIN แต่การดำเนินคำสั่งใช้กฎ D04.11.5
- `สถานะระบบ` แสดง Branch Service, Database, Cloud Sync, PrintGateway และ Print Job ผิดปกติแบบ Read-only สำหรับผู้ใช้ทั่วไป ส่วน Retry/Reprint/ทดสอบลิ้นชัก/แก้ Printer Profile ต้องตรวจสิทธิ์ของคำสั่งนั้นแยกต่างหาก

ปุ่มล็อก/ปลดล็อกโต๊ะเป็น UI ของ Payment State Machine ไม่สร้างฟิลด์ `isLocked` หรือสถานะล็อกคู่ขนาน:

- เมื่อ session เป็น `Ordering` ให้แสดง `ล็อกโต๊ะเพื่อชำระ`; คำสั่งนี้มีความหมายเป็น `RequestPayment` และเมื่อ Branch commit สำเร็จจึงเปลี่ยนเป็น `PaymentPending`
- เมื่อ session เป็น `PaymentPending` และยังไม่มี Payment Line ที่ยืนยัน ให้แสดง `ปลดล็อกเพื่อรับออร์เดอร์เพิ่ม`; คำสั่งนี้มีความหมายเป็น `CancelPaymentRequest` และเมื่อ commit สำเร็จให้กลับเป็น `Ordering`
- การปลดล็อกต้องยกเลิก Bill Preview/Payment Snapshot ที่ยังไม่เริ่มรับเงิน แต่คง `tableSessionId`, `sessionMasterDataVersion`, Order Line, ส่วนลดรายการ และส่วนลดท้ายบิลเดิม จากนั้น Mobile จึงส่ง Draft/Order Batch เพิ่มได้อีก
- ทำได้เฉพาะ PC POS ไม่บังคับกรอกเหตุผล และต้องบันทึกผู้ดำเนินการ เวลา Branch, device/terminal, สถานะก่อน–หลัง และ `tableSessionId` ใน audit log
- `PaymentInProgress` หรือ `PartiallyPaid` ปลดล็อกโดยตรงไม่ได้ ต้อง Reverse Payment Line ที่ยังมีผลทั้งหมดตามสิทธิ์ `ReversePayment` แล้วจึงยกเลิก Payment Flow ตาม D04; `Completed` ปลดล็อกไม่ได้
- ปุ่มและสิทธิ์ต้องใช้สถานะจริงจาก Branch Service ห้ามเก็บหรือสลับสถานะเฉพาะใน Browser

#### D18.2 — ผู้ใช้งาน PC POS และสิทธิ์รายคำสั่ง — ยืนยัน

- PC POS เป็นประเภท Terminal และตำแหน่งติดตั้งที่จุดแคชเชียร์ ไม่ใช่ Role หรือตำแหน่งงานของพนักงาน
- พนักงาน Active ทุกคนที่ authentication เข้า POS สำเร็จสามารถใช้ฟังก์ชันพื้นฐานบน PC POS ได้ ไม่จำกัดเฉพาะผู้ที่มีชื่อตำแหน่งงาน “แคชเชียร์” และไม่ต้องมี Role `Cashier`
- การเปิดโต๊ะ รับออร์เดอร์ และรับชำระตามปกติใช้ตัวตนของพนักงานที่ Login อยู่ โดยไม่ต้องกรอก PIN ซ้ำ เว้นแต่ข้อกำหนดของคำสั่งนั้นระบุเป็นอย่างอื่น
- การไม่จำกัดตำแหน่งงานไม่อนุญาตการใช้งานแบบ Anonymous; ทุกธุรกรรมต้องระบุพนักงาน Active หรือผู้ที่ยืนยัน PIN สำหรับคำสั่งนั้นได้
- คำสั่งที่กำหนดสิทธิ์เฉพาะ เช่น `ApplyDiscount`, `OverrideItemPrice`, `ReversePayment`, `ManualDrawerOpen`, `ReprintKitchenOrder` รวมถึงการทดสอบอุปกรณ์หรือแก้ Printer Profile ต้องตรวจสิทธิ์/PIN ตามข้อกำหนดของคำสั่งเดิม
- Branch Service ต้องบังคับ authorization ทุกคำสั่ง ห้ามใช้การแสดง ซ่อน หรือ Disable ปุ่มใน PC POS เป็น Security Boundary
- Audit และ Payment Flow ต้องบันทึกพนักงานที่ Login อยู่ ผู้ยืนยัน PIN หรือ Operator ที่รับช่วงตามกฎของคำสั่งนั้น

#### D18.3 — การแสดงปุ่มและการขอสิทธิ์บน PC POS — ยืนยัน

- คำสั่งที่พนักงานปัจจุบันไม่มีสิทธิ์แต่พนักงานผู้มีสิทธิ์สามารถเข้ามายืนยันได้ ให้แสดงปุ่มพร้อมสัญลักษณ์กุญแจ เมื่อกดจึงเปิดหน้ารับ PIN ของพนักงานผู้มีสิทธิ์โดยไม่ต้อง Logout หรือเปลี่ยนผู้ใช้ทั้งระบบ
- การยืนยัน PIN ให้สิทธิ์เฉพาะคำสั่งและข้อมูลเป้าหมายครั้งนั้น ห้ามทำให้ Session ปัจจุบันได้รับสิทธิ์นั้นถาวรหรือใช้ต่อกับคำสั่งอื่น
- ปุ่มที่ทำไม่ได้เพราะสถานะของ table session, Payment Flow หรือข้อมูลไม่ครบ ให้ Disable และแสดงเหตุผลที่ชัดเจน เช่นใช้ไม่ได้หลัง `PaymentPending`; ห้ามปล่อยให้กดแล้วล้มเหลวโดยไม่มีคำอธิบาย
- ปุ่มที่ไม่เกี่ยวข้องกับ Hardware หรือ Configuration ของสาขาจึงสามารถซ่อนได้ เช่นไม่มี Cash Drawer หรือไม่ได้เปิดใช้ความสามารถนั้น
- การซ่อนหรือ Disable เป็นเพียงพฤติกรรม UI; Branch Service ต้องตรวจ authentication, authorization, สถานะ และ invariant อีกครั้งทุกคำสั่ง
- Audit ต้องแยกผู้ใช้ที่ Login อยู่ ผู้กดเริ่มคำสั่ง และผู้ยืนยัน PIN เมื่อเป็นคนละบุคคล

#### D18.4 — หน้าจอล็อกและการเปลี่ยนพนักงาน — ยืนยัน

- PC POS ต้องมีคำสั่ง `ล็อกหน้าจอ` ที่ใช้ได้ตลอดเวลา การล็อกไม่ปิดโปรแกรม ไม่ปิดกะ/ร้าน และไม่หยุด Branch Service, PrintGateway, persistent worker หรือการซิงก์
- หน้าล็อกแสดงเฉพาะข้อมูลขั้นต่ำ เช่นชื่อสาขา สถานะระบบ และช่อง PIN ห้ามแสดงโต๊ะ รายการ ยอดขาย Payment หรือข้อมูลลูกค้า
- พนักงาน Active คนใดก็ได้ปลดล็อกด้วย PIN ของตนเอง และเมื่อสำเร็จให้เปลี่ยน Current Operator ของ UI เป็นพนักงานนั้น โดยไม่จำกัดตำแหน่งงานตาม D18.2
- Table session, ธุรกรรม, Print Job และข้อมูลที่ Commit แล้วต้องคงอยู่ทั้งหมดหลังล็อก/ปลดล็อก
- หากมี Payment Flow ค้าง การปลดล็อกโดยพนักงานคนอื่นไม่ถือเป็น Operator Change หรือการรับช่วง Payment อัตโนมัติ ต้องดำเนินการตามกฎรับช่วงและบันทึก Operator Change ใน D04
- รองรับ Auto-lock เมื่อไม่มีการใช้งาน โดยระยะเวลาเป็นค่าที่ส่วนกลางกำหนดและยังรอกำหนดค่าเริ่มต้น; ต้องไม่นับเป็น Idle ขณะผู้ใช้กำลังพิมพ์ กดค้าง หรือมี Dialog ยืนยันคำสั่งสำคัญเปิดอยู่
- เก็บเวลาและ employee ID ของการล็อก/ปลดล็อกเพื่อวินิจฉัย Session แต่ไม่ต้องสร้าง Audit เชิงธุรกรรม เว้นแต่เหตุการณ์นั้นทำให้เกิด Operator Change ใน Workflow ที่กำลังค้าง

#### D18.5 — รวม Home และ Dashboard — ยืนยัน

- ใช้ `หน้าหลัก` เป็น Operational Dashboard เพียงหน้าเดียว ห้ามมีแท็บ `Dashboard` ซ้ำในหน้า `โต๊ะ`
- หน้าหลักแสดงสถานะร้าน/กะ Current Operator, Online/Offline, เวลาซิงก์ล่าสุด และปุ่มล็อกหน้าจอตาม D18.4
- แสดงจำนวนโต๊ะตามสถานะอย่างน้อย `Available`, `Ordering` และ `PaymentPending` พร้อมเปิดไปยังรายการโต๊ะที่เกี่ยวข้องได้
- แสดงสรุปเฉพาะ business date ปัจจุบัน ได้แก่จำนวนใบเสร็จที่ชำระแล้ว ยอดขายที่ชำระแล้ว ยอดสินค้ารอชำระ และจำนวนรายการยกเลิก
- แสดงงานที่ต้องดำเนินการ ได้แก่ cancellation request, Payment Flow ค้าง และ Print Job สถานะผิดปกติ พร้อมเปิดไปยังข้อมูลเป้าหมายได้
- แสดง Notify จากส่วนกลางตามกฎ 72 ชั่วโมงและแสดงสถานะ Branch Service, Database, PrintGateway และ Cloud Sync แบบสรุป
- แสดง Panel `รายการ Sold Out ของสาขา` ซึ่งมีจำนวนทั้งหมดและรายการล่าสุดประมาณ 5–8 รายการ โดยแสดงอย่างน้อยชื่อเมนู หมวดหมู่/สถานีเมื่อมี ผู้ตั้งสถานะ และเวลาที่ตั้ง พร้อมปุ่ม `ดูทั้งหมด / จัดการ Sold Out`
- หน้ารวม Sold Out ต้องค้นหาและกรองตามหมวดหมู่ได้; PC POS ตั้งหรือยกเลิกสถานะด้วยพนักงานที่ Login อยู่โดยไม่ต้องกรอก PIN เพิ่มตาม D08.2 และต้องมี Dialog ยืนยันก่อนยกเลิก Sold Out เพื่อป้องกันการกดผิด
- Sold Out เป็น Branch Operational Override ไม่แสดงหรืออนุมานจำนวนสต็อก ไม่เปลี่ยนตามวัตถุดิบอัตโนมัติ และมีผลต่อการ Validation `SubmitOrder` ทันทีแม้อินเทอร์เน็ตภายนอกขาด
- ยอดขายตามกลุ่มสินค้า รายละเอียด/อันดับสินค้าขาย กราฟ และรายงานวิเคราะห์ไม่อยู่บนหน้าหลัก ให้จัดไว้ในรายงาน/Back Office เพื่อไม่ให้หน้าปฏิบัติงานแคชเชียร์มีข้อมูลเกินจำเป็น

#### D18.6 — หน้าเพิ่มรายการ ตะกร้า และรายการที่ส่งแล้ว — ยืนยัน

- หน้าเพิ่มรายการของ PC POS ใช้ Split View สองฝั่ง: ฝั่งซ้ายเป็น `ตะกร้า`/`ส่งแล้ว` ของโต๊ะที่เลือก และฝั่งขวาเป็นหมวดหมู่กับรายการอาหาร โดยไม่แสดงรูปเมนู
- หมวดหมู่ เช่น `ทั้งหมด`, `ผัด`, `แกง`, `ทอด`, `ยำ`, `อาหารจานเดียว`, `เครื่องดื่ม` และ `ของหวาน` แสดงเป็นแถบแนวนอนเหนือรายการอาหาร; หน้านี้ไม่มีช่องค้นหาและไม่มีคอลัมน์หมวดหมู่แนวตั้ง
- รายการอาหารแสดงรหัส ชื่อ ราคา และสถานะ Sold Out; รายการ Sold Out ต้อง Disable และห้ามลงตะกร้า
- ถ้าเมนูมี Sub Item เมื่อเลือกเมนูหลักให้เปิดคอลัมน์ `เลือกรายการย่อย` ทางขวาของรายการอาหารก่อน เมื่อเลือก Sub Item แล้วจึงเปิด `Menu Item Options Popover` ที่มีเฉพาะ Topping และปุ่ม `ลงตะกร้า`
- ถ้าเมนูไม่มี Sub Item เมื่อเลือกเมนูให้เปิด `Menu Item Options Popover` ที่มีเฉพาะ Topping และปุ่ม `ลงตะกร้า` ทันที; ห้ามนำตัวเลือก Sub Item หรือวัตถุดิบหลักไปปะปนใน Popover
- UI ใช้คำว่า `ตะกร้า` แทน `Draft`; สถานะข้อมูลภายในยังเป็น Draft จนกด `ส่งออเดอร์` และไม่มีปุ่ม `บันทึก Draft` แยกต่างหาก
- แท็บ `ตะกร้า` แสดงเฉพาะรายการที่ยังไม่ส่งและอนุญาตเพิ่ม/ลดจำนวน แก้ไข หรือลบก่อน Submit; ปุ่มหลักด้านล่างคือ `ส่งออเดอร์ N รายการ`
- แท็บ `ส่งแล้ว` แสดงเฉพาะ Order Line ที่ Branch รับแล้วแบบ Read-only โดยไม่แสดงปุ่มเพิ่ม/ลดจำนวน แก้ไข ลบ หรือส่งออเดอร์ซ้ำ และจัดรายการตาม Order Batch/รอบส่ง

การรองรับลูกค้าที่สั่งทานที่ร้านและกลับบ้านพร้อมกัน:

- เก็บรูปแบบการรับสินค้าเป็น `fulfillmentType` ระดับ Order Line ได้แก่ `DineIn` และ `Takeaway`; ทั้งสองแบบยังอยู่ใน `tableSessionId`, Order Batch และบิลเดียวกัน
- เมื่อเปิดโต๊ะ ค่าเริ่มต้นของรายการใหม่เป็น `DineIn`; เหนือตะกร้ามีตัวเลือก `ทานที่ร้าน`/`กลับบ้าน` ซึ่งมีผลเฉพาะรายการที่ลงตะกร้าหลังจากเลือก ไม่ย้อนกลับไปเปลี่ยนรายการเดิม
- พนักงานไม่ต้องเลือกรูปแบบก่อนกดอาหารทุกครั้ง เปลี่ยนตัวเลือกเฉพาะเมื่อจะเริ่มเพิ่มรายการอีกประเภท และสามารถเปลี่ยน `fulfillmentType` เป็นรายรายการได้ขณะที่ยังอยู่ในตะกร้าก่อน Submit
- ตะกร้าและแท็บ `ส่งแล้ว` ต้องจัดกลุ่มรายการเป็น `ทานที่ร้าน` และ `กลับบ้าน` พร้อม Badge ระดับ Order Line; แท็บ `ส่งแล้ว` ต้องแสดงรอบส่ง, `orderBatchId`, สถานะ Branch รับคำสั่ง และยอดรวมของรอบนั้น
- `Takeaway` ในข้อนี้เป็นรูปแบบการรับสินค้าของ Order Line ภายในช่องทางขายแบบโต๊ะ ไม่ใช่ช่องทางขาย TOGO หรือบิลแยก จึงไม่ขัดกับ CORE-04; หากลูกค้าต้องการชำระแยกให้ใช้ Workflow แยกบิลก่อน `PaymentPending`

#### D18.7 — ผลหลัง CompletePayment และ Overlay เงินทอน — ยืนยัน

- เมื่อ Branch commit `CompletePayment` สำเร็จ ให้ปิด `tableSessionId`, เปลี่ยนโต๊ะเป็น `Available` และให้ PC POS กลับหน้าผังโต๊ะทันที โดยไม่ค้างอยู่ใน Payment Workspace
- หาก Payment Flow มี Payment Line เงินสด รวมถึง Split Payment ที่มีเงินสด ให้แสดง Non-modal Overlay บนหน้าผังโต๊ะเป็นเวลา 10 วินาที โดยแสดงยอดเงินทอนขนาดเด่นและระบุเลขโต๊ะ/Receipt เดิมเพื่อป้องกันความสับสน
- Overlay ไม่มีตัวนับถอยหลัง ไม่มีปุ่ม `กลับผังโต๊ะทันที` และหายเองเมื่อครบเวลา; ต้องไม่บังทั้งหน้าจอและไม่ขัดขวางการเลือกหรือเปิดโต๊ะถัดไป
- เริ่มจับเวลา 10 วินาทีหลัง Branch commit `CompletePayment` สำเร็จ ไม่ใช่เมื่อผู้ใช้กดปุ่ม และยอดเงินทอนต้องอ่านจาก Payment Result ที่ Branch commit แล้ว ห้ามให้ UI คำนวณใหม่
- การแสดง Overlay ต้องไม่ขัดขวางการสร้าง Receipt, Print Job หรือการเปิดลิ้นชักเงิน
- หาก Print Job ล้มเหลวขณะ Overlay ยังแสดง ให้เปลี่ยนข้อความเป็น `พิมพ์ใบเสร็จไม่สำเร็จ`; หากเกิดหลัง Overlay หาย ให้สร้างการแจ้งเตือนค้างบนหน้าผังโต๊ะและใน `สถานะระบบ` โดยห้ามย้อนผลการชำระเงิน
- หาก Payment Flow ไม่มี Payment Line เงินสด ให้กลับผังโต๊ะทันทีและแสดงเพียงข้อความ `ชำระเงินสำเร็จ` ประมาณ 2–3 วินาที
- ตำแหน่ง ขนาด สี typography และ responsive behavior ของ Overlay รอกำหนดใน UI Design System และทดสอบกับจอ PC POS จริง โดยต้องคงข้อกำหนดด้านพฤติกรรมและการไม่ปิดกั้นการใช้งานข้างต้น

#### D18.8 — หน้าประวัติธุรกรรมของ PC POS — ยืนยัน

- หน้า `ประวัติธุรกรรม` ใช้ Split View โดยฝั่งซ้ายแสดงรายการธุรกรรมเรียงล่าสุดก่อน และฝั่งขวาแสดงรายละเอียดของรายการที่เลือกโดยไม่ต้องเปิดหน้าต่างใหม่
- แถบตัวกรองรองรับอย่างน้อยวันที่ เลข Receipt/Order เลขโต๊ะ ช่องทางชำระ และสถานะ; ค่าเริ่มต้นเป็น Business Date ปัจจุบัน และการเปิดหน้า/ค้นหาไม่ต้องใช้ PIN
- PC POS แสดงเฉพาะข้อมูลของสาขาตนเองที่มีอยู่ใน Branch Database และยังค้นดูข้อมูลดังกล่าวได้เมื่ออินเทอร์เน็ตภายนอกขาด
- รายละเอียดรองรับ Order, Receipt, Order Line, Payment Line, Cancellation, Transfer, Discount, Price Override และ Shift ที่เกี่ยวข้อง โดยจัดเป็นแท็บหรือส่วนข้อมูลที่อ่านต่อเนื่องได้
- หน้านี้เป็น Read-only และห้ามมีคำสั่ง Cancel, Refund, Reverse Payment, แก้ไข หรือลบธุรกรรม; การค้นหา เปิดหน้า หรือดูรายละเอียดไม่สร้าง Audit Log
- อนุญาต `พิมพ์สำเนาใบเสร็จ` ตาม D04.9 โดยใช้ Receipt Snapshot เดิม ระบุว่าเป็นสำเนา ไม่เปิดลิ้นชักเงิน และบันทึก Reprint Audit
- อนุญาต `พิมพ์ใบครัวซ้ำ` เฉพาะ Order Line ที่ผู้ใช้เลือกทีละรายการ ห้าม Reprint ทั้งบิล และต้องยืนยัน PIN ของพนักงานที่มีสิทธิ์ `ReprintKitchenOrder`; ต้องบันทึก Reprint Audit ตามข้อกำหนดเดิม
- UI ต้องแสดงสถานะ Read-only และเงื่อนไขสิทธิ์ของปุ่ม Reprint ให้ชัดเจน โดยการซ่อนหรือ Disable ปุ่มไม่แทนที่การตรวจ authorization ที่ Branch Service

#### D18.9 — หน้า “ร้านและกะ” — ยืนยัน

- หน้า `ร้านและกะ` แสดง Business Date, สถานะร้าน, เวลา/ผู้เปิดร้าน, กะปัจจุบัน, เวลา/ผู้เปิดกะ, ระยะเวลากะ และจำนวนกะที่เปิดในวันนั้น โดยการเปิดดูหน้าไม่ต้องใช้ PIN
- แสดงสรุปกะปัจจุบันอย่างน้อยยอดขายสุทธิ จำนวนใบเสร็จ จำนวนโต๊ะ/รายการรอชำระ และยอดรับชำระแยกตามช่องทาง
- แสดง Timeline กะของ Business Date ปัจจุบัน เรียงตามลำดับ พร้อมสถานะ ผู้เปิดกะ และลิงก์ดูรายงาน `CloseShift` ของกะที่ปิดแล้ว
- สถานะการเชื่อมต่อ/ซิงก์ไม่ใช้การ์ดในพื้นที่หลัก ให้แสดงที่ด้านล่างของ Sidebar เป็น `Data Synced` พร้อมเวลาล่าสุด
- สถานะการส่งข้อมูลธุรกรรม/รายงานออกจาก Branch ไปส่วนกลางไม่ใช้การ์ดในพื้นที่หลัก ให้แสดงใต้ `Data Synced` เป็น `Data Sent` พร้อมเวลาล่าสุด; ห้ามใช้ข้อความ `Data Sended`
- เมื่อร้านเปิดและมีกะ Active หน้าหลักแสดงเฉพาะปุ่มคำสั่ง `ปิดกะ` และ `ปิดร้าน` ที่มุมล่างขวา โดยไม่มีกรอบ/หัวข้อการ์ดคำสั่ง คำอธิบาย ช่องกรอก หรือช่อง PIN แสดงค้างบนหน้า
- เมื่อกด `ปิดกะ` จึงเปิด Pop-up อธิบายผลของคำสั่ง รับยอดเงินในลิ้นชักแบบ Optional และขอ PIN ของพนักงาน Active ตาม D04.11.5
- เมื่อกด `ปิดร้าน` จึงเปิด Pop-up อธิบายผลของคำสั่ง บังคับกรอกยอดเงินในลิ้นชักแม้เป็น 0 และขอ PIN พนักงานตาม D04.11.5 ก่อนสร้าง `CloseStore`
- คำสั่งในสถานะอื่น เช่น `เปิดร้าน` และ `เปิดกะ` ต้องแสดงตาม State Machine และใช้กฎ PIN/ยอดเงินที่ยืนยันไว้ใน D04.11.5; ห้ามแสดงคำสั่งที่ใช้ไม่ได้ในสถานะปัจจุบัน
- Google Sheets เป็นงานส่วนกลางตามข้อกำหนดเดิม หน้า PC POS ไม่มีปุ่มส่ง Google Sheets โดยตรง

#### D18.10 — หน้า “สถานะระบบ” — ยืนยัน

- หน้า `สถานะระบบ` เปิดดูได้แบบ Read-only โดยไม่ต้องใช้ PIN และแสดงสถานะรวมเป็น `ปกติ`, `มีคำเตือน` หรือ `ต้องดำเนินการ` พร้อมเวลา Health Check ล่าสุด
- แสดงสถานะอย่างน้อยของ Branch Service, Branch Database, Cloud Sync, PrintGateway และ Backup ล่าสุด โดยไม่เปิดเผย Password, Token, Connection String, Secret หรือ Raw Stack Trace
- แสดงเครื่องพิมพ์ของสาขาแยกตามรหัส สถานี และ IP พร้อมสถานะที่ตรวจได้จริงจาก Printer Profile/Protocol ของรุ่นนั้น
- สถานะละเอียด เช่น `Paper Out`, `Cover Open` หรือ Cutter Error แสดงได้เฉพาะเมื่ออุปกรณ์และ Protocol รองรับและ PrintGateway ตรวจยืนยันได้; หากตรวจได้เพียงการเชื่อมต่อให้แสดงเฉพาะ `Online`, `Offline` หรือ `Unknown` โดยห้ามอนุมานสถานะอุปกรณ์
- แสดงสรุปอย่างน้อยจำนวน Outbox รอส่ง, Print Job ผิดปกติ, เวลา Database Checkpoint และพื้นที่ Local Backup
- ส่วน `งานที่ต้องดำเนินการ` แสดง Print Job/Outbox ที่เป็น `Queued`, `Failed`, `Unknown` หรือสถานะผิดปกติ พร้อมเวลา เอกสาร อุปกรณ์ และผลล่าสุด
- ผู้ใช้ทั่วไปดูรายละเอียดได้ แต่คำสั่ง `Retry`, `Reprint`, ทดสอบเครื่องพิมพ์/ลิ้นชัก หรือแก้ Printer Profile ต้องแสดงสัญลักษณ์กุญแจและตรวจ PIN/สิทธิ์ของคำสั่งนั้นที่ Branch Service
- หน้าเริ่มต้นไม่มีคำสั่งทำลายข้อมูลและไม่เปิดให้แก้ Configuration โดยตรง; การเข้าสู่หน้ารายละเอียดหรือเครื่องมือดูแลต้องแยกสิทธิ์อย่างชัดเจน
- Sidebar ยังคงแสดง `Data Synced` และ `Data Sent` พร้อมเวลาล่าสุดตาม D18.9 เพื่อให้เห็นสถานะย่อได้จากทุกหน้า

### 4.2 Home และ Notify

**ยืนยัน:** ทุกการเปลี่ยนแปลงข้อมูลของสาขาต้องมี Notify บนหน้าแรก POS ของสาขาที่ได้รับผลกระทบ เป็นเวลา 72 ชั่วโมงนับตั้งแต่เวลาเปลี่ยนแปลงทันที รวมเรื่องการเผยแพร่และเวลาเริ่มใช้

รายละเอียดที่เคยเสนอให้แสดง: สิ่งที่เปลี่ยน ผู้แก้ไข เวลาเปลี่ยน สถานะเผยแพร่ และเวลาเริ่มมีผล — รอออกแบบหน้าตาและกฎรายละเอียด

Notify ไม่ได้กำหนดว่าต้องรอ 72 ชั่วโมงจึงใช้ค่าใหม่ และไม่ได้หมายความว่าพนักงานต้องอนุมัติก่อนใช้ค่าใหม่

ต้องตัดสินใจกรณี POS ออฟไลน์จนพ้น 72 ชั่วโมง จะให้เห็นประวัติ/รายการตกค้างอย่างไร รวมถึงการอ่านแล้ว ซ่อนแล้ว และการเปลี่ยนซ้ำ

### 4.3 การขาย / โต๊ะ

**ยืนยัน:** เมนู `โต๊ะ` เปิดหน้าโซน/ผังโต๊ะและรายละเอียดโต๊ะโดยตรงตาม D18.1 ไม่มีแท็บ Dashboard ซ้ำและไม่มี TOGO/Delivery; สรุปการปฏิบัติงานอยู่ที่ `หน้าหลัก` ตาม D18.5

หน้าโต๊ะใช้อ้างอิงแนวคิดเลือกโซน/โต๊ะแล้วเห็นรายการและยอดบิล มีข้อมูลเวลาเริ่ม ระยะเวลา จำนวนลูกค้า ราคา Topping และหมายเหตุ

#### 4.3.0 D01 — สิทธิ์การใช้งาน POS — ยืนยันบางส่วน

##### D01.1 — สิทธิ์ให้ส่วนลด — ยืนยัน

- การเพิ่ม เปลี่ยน หรือลบส่วนลดระดับสินค้าและส่วนลดท้ายบิลทำได้เฉพาะ PC POS และเฉพาะก่อน table session เข้าสถานะ `PaymentPending`
- ทุก action ต้องยืนยันด้วย PIN ของพนักงาน Active ที่ส่วนกลางกำหนดสิทธิ์ `ApplyDiscount`; การมีสิทธิ์เข้า POS ทั่วไปไม่ให้สิทธิ์ส่วนลดโดยอัตโนมัติ
- Mobile ดูส่วนลดที่มีผลได้ แต่เพิ่ม เปลี่ยน หรือลบส่วนลดไม่ได้
- ทุกการเพิ่ม เปลี่ยน หรือลบต้องบันทึกระดับส่วนลด/รายการที่เกี่ยวข้อง ค่าเดิม ค่าใหม่ employee ID, เวลา Branch, device/terminal และ `tableSessionId` ใน audit log โดยห้ามเก็บ PIN แบบ plaintext
- ไม่บังคับกรอกเหตุผลในการเพิ่ม เปลี่ยน หรือลบส่วนลด
- สถานะ Active/PIN verifier/สิทธิ์ใช้ค่าล่าสุดที่ Branch Service ซิงก์ได้ตาม D11 เพื่อให้ใช้งานขณะอินเทอร์เน็ตขาดได้ภายใต้ข้อจำกัดการเพิกถอนจากส่วนกลาง

##### D01.2 — การเปลี่ยนราคาที่หน้า POS — ยืนยัน

- การเปลี่ยนราคาทำได้เฉพาะ PC POS และต้องยืนยันด้วย PIN ของพนักงาน Active ที่ส่วนกลางกำหนดสิทธิ์ `OverrideItemPrice`; Mobile ดูราคาที่มีผลได้แต่แก้ไม่ได้
- ราคาใหม่มีผลเฉพาะ Order Line ในบิลนั้น ไม่แก้ Master Data ราคา ไม่เปลี่ยนรายการอื่น และไม่เปลี่ยนประวัติย้อนหลัง
- ราคาใหม่อนุญาต 0 บาท แต่ต้องไม่ต่ำกว่า 0
- เปลี่ยนราคาได้กับ Order Line ทั้งที่ยังไม่ส่งและส่งด้วย `SubmitOrder` แล้ว ตราบใดที่ table session ยังไม่เป็น `PaymentPending`; หลังเข้าสถานะดังกล่าวหรือชำระเสร็จแล้วแก้ราคาไม่ได้
- Price Override หลัง `SubmitOrder` เป็นการเปลี่ยนข้อมูลทางการเงินเท่านั้น ไม่แก้ payload ของงานพิมพ์เดิม ไม่สร้างออร์เดอร์ใหม่ และไม่ส่งใบครัว/สถานีผลิตซ้ำ; ใบเสร็จเมื่อชำระต้องใช้ราคาที่มีผลล่าสุดก่อน `PaymentPending`
- ราคา Override เป็นราคาต่อหน่วยของ Order Line ทั้งจำนวน หากต้องเปลี่ยนเพียงบางจำนวนต้องแยก Order Line ก่อน
- Order Line มีราคา Override ที่ active ได้ครั้งละหนึ่งค่า การเปลี่ยนครั้งใหม่แทนค่าครั้งก่อน ไม่บวกหรือสะสม
- ช่องเหตุผล/หมายเหตุเป็น optional ไม่บังคับกรอก
- ทุกการเปลี่ยนต้องบันทึกราคาจาก Master Data, ราคาเดิมที่มีผล, ราคาใหม่, เหตุผลเมื่อมี, employee ID, เวลา Branch, device/terminal, `tableSessionId` และ order-line reference ใน audit log
- สถานะ Active/PIN verifier/สิทธิ์ใช้ค่าล่าสุดที่ Branch Service ซิงก์ได้ตาม D11

##### D01.3 — การยกเลิกทั้งบิลก่อนชำระที่สาขา — ยืนยัน

- ฝั่งสาขาทำได้เฉพาะ PC POS และต้องยืนยันด้วย PIN ของพนักงาน Active ที่ส่วนกลางกำหนดสิทธิ์ `CancelBill`; Mobile ไม่มีสิทธิ์ยกเลิกทั้งบิล
- D01.3 ไม่ยกเลิกสิทธิ์ส่วนกลางที่ยกเลิกได้เฉพาะทั้งบิลตาม D12.6 ซึ่งเป็น Workflow แยกและยังต้องกำหนดรายละเอียดเพิ่มเติม
- ต้องดำเนินการก่อน `CompletePayment`; หาก table session เป็น `PaymentPending` ต้อง `CancelPaymentRequest` ก่อน และหากเป็น `PartiallyPaid` ต้องสร้าง reversal ของ Payment Lines ที่ยืนยันแล้วทั้งหมดก่อน
- หากไม่มี Order Line ใดเคย `SubmitOrder` สามารถยกเลิกบิลได้ทันที
- หากมี Order Line ที่ส่งแล้ว ต้องนำรายการ Active ทั้งหมดเข้าสู่กระบวนการตรวจสอบและบันทึกสถานะการผลิตตาม D06 ห้ามยกเลิกทั้งบิลทันที
- ยกเลิกบิลสำเร็จได้เมื่อไม่มี Order Line สถานะ Active และไม่มี Payment Line ที่มีผลเหลืออยู่ จากนั้นปิด `tableSessionId` และเปลี่ยนโต๊ะเป็น `Available`
- บิลและเหตุการณ์ยกเลิกต้องคงอยู่ในประวัติ ห้ามลบหรือเขียนทับ
- ช่องเหตุผล/หมายเหตุเป็น optional ไม่บังคับกรอก
- Audit ต้องเก็บผู้ดำเนินการ เวลา Branch, device/terminal, `tableSessionId`, สถานะ/รายการ/ยอดก่อนยกเลิก และเหตุผลเมื่อมี
- สถานะ Active/PIN verifier/สิทธิ์ใช้ค่าล่าสุดที่ Branch Service ซิงก์ได้ตาม D11

##### D01.4 — สิทธิ์ดูประวัติธุรกรรมที่สาขา — ยืนยัน

- หน้าประวัติธุรกรรมมีเฉพาะ PC POS และไม่ต้องยืนยัน PIN เพิ่มเมื่อเข้าสู่หน้า
- Mobile ดูได้เฉพาะโต๊ะและงานที่กำลังดำเนินการ ไม่สามารถดูประวัติของ table session/ธุรกรรมที่ปิดแล้ว
- PC POS ดูได้เฉพาะข้อมูลของสาขาตนเอง; การดูหลายสาขาใช้สิทธิ์ Back Office แยกต่างหาก
- ประวัติต้องแสดง Order, Receipt, Payment, Cancellation, Transfer, Discount, Price Override และ Shift ที่เกี่ยวข้อง
- หน้าประวัติเป็น Read-only ยกเว้น Reprint ใบเสร็จตาม D04.9 และ Kitchen Order รายการเดียวตาม D01.4.1; ไม่ให้สิทธิ์ Refund, Cancel หรือแก้ข้อมูล
- Reprint ต้องใช้ receipt snapshot เดิม ระบุ `สำเนา`, ห้ามเปิดลิ้นชัก และสร้าง reprint job/audit ตาม D04.9
- ขณะออฟไลน์ดูได้เฉพาะข้อมูลที่อยู่ใน Branch Database
- ไม่บังคับกรอกเหตุผลเพื่อเข้าสู่หรือดูหน้าประวัติ และไม่สร้าง audit log สำหรับการค้นหา เปิดหน้า หรือเปิดดูเอกสาร

##### D01.4.1 — Reprint Kitchen Order จากหน้าประวัติ — ยืนยัน

- ทำได้เฉพาะ PC POS และต้องยืนยันด้วย PIN ของพนักงาน Active ที่ส่วนกลางกำหนดสิทธิ์ `ReprintKitchenOrder` ทุกครั้งที่สั่งพิมพ์
- พิมพ์ซ้ำได้ครั้งละหนึ่ง Order Line ที่เคย `SubmitOrder` และมี original kitchen/production print snapshot เท่านั้น ไม่มีคำสั่ง Reprint ทั้งบิลหรือทั้ง `tableSessionId`
- Reprint ต้องใช้ immutable snapshot เดิมของ Order Line รวมจำนวน Topping หมายเหตุ เลขโต๊ะ และ Production Station ณ เวลาที่ส่งเดิม ห้ามดึง Master Data ปัจจุบันหรือเปลี่ยน payload เดิม
- ใบที่พิมพ์ซ้ำต้องระบุ `สำเนา / พิมพ์ซ้ำ` อย่างชัดเจน และส่งเฉพาะ Production Station เดิมของรายการนั้น
- ต้องสร้าง `printJobId` ใหม่ที่อ้างอิง original `printJobId`, `orderBatchId`, `tableSessionId` และ order-line reference; การ Reprint ไม่สร้าง Order ใหม่ ไม่ตัดสต็อกซ้ำ และไม่เปลี่ยนยอดบิล
- ทุกคำสั่ง Reprint ต้องบันทึก employee ID, เวลา Branch, device/terminal, รายการ สถานี original/reprint identifiers และผลการพิมพ์ใน audit log; การเปิดดูประวัติยังคงไม่สร้าง audit ตาม D01.4
- ตรวจ PIN/สถานะ/สิทธิ์จากค่าล่าสุดที่ Branch Service ซิงก์ไว้ตาม D11 และใช้กฎสถานะงานพิมพ์/`Unknown` ตาม D20

##### D01.5 — การพิมพ์ Bill Preview — ยืนยัน

- PC POS สั่งพิมพ์ Bill Preview ด้วยตนเองขณะ table session เป็น `Ordering` ได้โดยไม่ใช้ PIN และการพิมพ์ไม่เปลี่ยนสถานะโต๊ะ
- เมื่อ Branch commit `RequestPayment` และเปลี่ยน table session เป็น `PaymentPending` สำเร็จ ต้องสร้าง Bill Preview snapshot และ persistent print job ไปเครื่องแคชเชียร์หนึ่งงานใน transaction/outbox ที่สอดคล้องกัน
- `RequestPayment` ที่ส่งซ้ำด้วย `requestId` เดิมต้องคืนผลเดิมและห้ามสร้าง Preview snapshot/print job ซ้ำ
- Preview อัตโนมัติต้องใช้ข้อมูล Snapshot ณ transaction ที่รับ `RequestPayment` และแสดงรายการ จำนวน Topping หมายเหตุ ส่วนลด Charges VAT Rounding และยอดสุทธิ
- เอกสารต้องระบุ `ใบตรวจสอบรายการ / ยังไม่ใช่ใบเสร็จ`, ไม่มีเลขใบเสร็จ และไม่สร้าง Payment, ยอดขาย, drawer event หรือสถานะ `Completed`
- ความล้มเหลวหรือ `Unknown` ของงานพิมพ์หลัง commit ไม่ย้อน `PaymentPending`; PC ต้องเห็นสถานะตาม D20 และสามารถสั่ง Reprint Preview ได้
- Reprint Preview สร้าง `printJobId` ใหม่ อ้างอิง Preview snapshot เดิม และระบุว่าเป็นสำเนา ห้ามคำนวณจากข้อมูลปัจจุบันใหม่
- การพิมพ์และ Reprint Preview ไม่ต้องใช้ PIN ไม่บังคับเหตุผล และไม่สร้าง Business Audit ของผู้สั่ง แต่ต้องเก็บ Print Job/attempt/status ตาม D20
- Bill Preview ส่งเฉพาะเครื่องแคชเชียร์ ห้ามสร้าง print job ไปครัว เค้ก หรือ Production Station

##### D01.6 — สิทธิ์รับชำระเงิน — ยืนยัน

- `StartPayment`, เพิ่ม/ยืนยัน Payment Line และ `CompletePayment` ทำได้เฉพาะ PC POS; Mobile ทำได้เพียง `RequestPayment` และไม่มีสิทธิ์รับหรือยืนยันเงิน
- การเริ่มและดำเนิน Payment Flow ตามปกติไม่ต้องใช้ PIN เพิ่มและไม่ใช้สิทธิ์ `ProcessPayment` แยก ให้ผูกพนักงานที่ authentication อยู่บน PC POS ตอน `StartPayment` เป็น `paymentOperatorEmployeeId`
- ผู้รับเงินเลือก Payment Method, กรอกจำนวน และยืนยันผลแบบ Manual ตาม D04; `CompletePayment` ทำได้เมื่อผลรวม effective `appliedAmount` ครบ `amountDue` เท่านั้น
- หากพนักงานคนอื่นรับช่วงต่อ ต้องกรอก PIN ของพนักงาน Active คนนั้นเพื่อเปลี่ยน operator และบันทึก Operator Change; PIN ใช้ระบุตัวตน ไม่ต้องมี permission `ProcessPayment` เพิ่มเติม
- การรับช่วงไม่บังคับกรอกเหตุผล
- Process/PC restart ต้องไม่ทำให้ Payment Lines ที่ persist แล้วสูญหาย แต่ก่อนดำเนิน Payment Flow ต่อ ต้องกรอก PIN ของพนักงาน Active เพื่อระบุ operator ใหม่/ผู้ดำเนินการต่อ
- Audit ต้องเก็บผู้เริ่ม Payment Flow, ผู้ยืนยันแต่ละ Payment Line, การเปลี่ยน operator และผู้ `CompletePayment` พร้อมเวลา Branch/device/terminal โดยห้ามเก็บ PIN แบบ plaintext
- การ Reverse Payment Line ที่ยืนยันแล้วไม่รวมอยู่ในสิทธิ์รับชำระปกติ ให้ใช้สิทธิ์ `ReversePayment` ตาม D01.7
- สถานะ Active/PIN verifier ใช้ค่าล่าสุดที่ Branch Service ซิงก์ได้ตาม D11

##### D01.7 — การ Reverse Payment Line — ยืนยัน

- ทำได้เฉพาะ PC POS และต้องยืนยันด้วย PIN ของพนักงาน Active ที่ส่วนกลางกำหนดสิทธิ์ `ReversePayment` ทุกครั้ง
- Reverse ได้เฉพาะ Payment Line ที่ยืนยันแล้วใน Payment Flow ที่ยังไม่ `Completed`; หลัง `CompletePayment` ห้าม Reverse ที่สาขาและต้องใช้กระบวนการ Refund ส่วนกลางตาม D04.12
- Reverse ได้เต็ม Payment Line เท่านั้น ไม่รองรับ Partial Reversal หากจำนวนหรือช่องทางผิดต้องสร้าง immutable Reversal Event ของรายการเดิมแล้วจึงสร้าง Payment Line ใหม่
- ห้ามแก้ ลบ หรือเขียนทับ Payment Line เดิม Reversal Event ต้องอ้างอิง original `paymentLineId` และมี identifier ของตนเอง
- หาก Reverse แล้วไม่เหลือ effective Payment Line ให้ table session กลับ `PaymentPending`; หากยังมียอดที่ยืนยันแล้วเหลืออยู่ให้คง `PartiallyPaid`
- สำหรับ QR, โอน, Credit Card และ Debit Card ระบบบันทึกผลแบบ Manual เท่านั้น ผู้ดำเนินการต้องจัดการการยกเลิกกับธนาคาร/EDC/ช่องทางภายนอกเอง ระบบไม่อ้างว่า External Reversal สำเร็จอัตโนมัติ
- การ Reverse เงินสดระหว่าง Payment Flow เป็นการคืนเงินรับที่บิลยังไม่ Completed ไม่ใช่ Refund หลังชำระ และไม่สร้าง Cash Movement แยก; effective Cash `appliedAmount` ต้องตัดรายการที่ถูก Reverse ออกจากสูตร expected cash
- ช่องเหตุผล/หมายเหตุเป็น optional ไม่บังคับกรอก
- Audit ต้องเก็บ original Payment Line/Reversal Event, ช่องทาง, จำนวน, employee ID, เวลา Branch, device/terminal และเหตุผลเมื่อมี โดยห้ามเก็บ PIN แบบ plaintext
- สถานะ Active/PIN verifier/สิทธิ์ใช้ค่าล่าสุดที่ Branch Service ซิงก์ได้ตาม D11

#### 4.3.1 D03 — การเปิดและล็อกโต๊ะ — ยืนยัน

- ทั้ง PC POS และ Mobile เปิดโต๊ะได้
- เมื่อสองอุปกรณ์เปิดโต๊ะเดียวกันพร้อมกัน Branch ต้องสร้าง `tableSessionId` สำเร็จเพียงหนึ่งค่า คำขอที่ตามหลังต้องคืน session ที่เปิดอยู่ ห้ามสร้างบิลซ้ำ
- ไม่ล็อกโต๊ะไว้กับ Mobile เครื่องใดเครื่องหนึ่ง หลายอุปกรณ์เพิ่มรายการพร้อมกันได้ด้วยกฎ Auto-merge แบบ append และ trace ย้อนกลับได้ตาม D12
- เมื่อ table session เป็น `PaymentPending` ต้องล็อกการเพิ่ม แก้ไข ย้าย แยก และรวมรายการทั้งหมด จนกว่า PC จะ `CancelPaymentRequest` หรือ `CompletePayment`

#### 4.3.2 D03 — `ChangeTable` และ `TransferItems` — ยืนยัน

- ดำเนินการได้เฉพาะ PC POS และต้องเกิดก่อน `PaymentPending`
- `ChangeTable` ใช้แก้กรณีเปิดผิดโต๊ะหรือเปลี่ยนโต๊ะไปยังโต๊ะปลายทางที่เป็น `Available`: คง `tableSessionId` เดิมและเปลี่ยนเฉพาะ `currentTableId`; ไม่สร้างบิล/session ใหม่และคงรายการกับส่วนลดทั้งหมด
- เมื่อ `ChangeTable` commit สำเร็จ โต๊ะต้นทางเป็น `Available` และโต๊ะปลายทางผูกกับ session เดิม กรณีนี้เป็นข้อยกเว้นของกฎที่ session ว่างไม่ปิดอัตโนมัติ เพราะไม่มี session ว่างถูกทิ้งไว้ที่ต้นทาง
- หากโต๊ะปลายทางมี active session อยู่แล้ว ต้องปฏิเสธ `ChangeTable` และใช้ `TransferItems`/การรวมโต๊ะแทน
- `TransferItems` ใช้ย้ายบางรายการหรือรายการทั้งหมดไปยัง `tableSessionId` ปลายทางที่เปิดอยู่ โดย session ต้นทางและปลายทางยังคงแยกกัน
- การย้ายต้องสร้าง immutable transfer event ระบุ session/โต๊ะต้นทางและปลายทาง รายการ จำนวน ผู้ดำเนินการ เวลา และเหตุผล โดยห้ามลบหรือเขียนทับประวัติต้นทาง
- หากย้ายเพียงบางจำนวนจากรายการเดียว Branch ต้องสร้างส่วนที่ย้ายและส่วนที่เหลืออย่างตรวจสอบย้อนหลังได้; Topping และหมายเหตุต้องติดไปกับสินค้าหลัก
- ทั้ง session ต้นทางและปลายทางต้องอยู่ในสถานะที่แก้ไขได้ การย้ายเข้าสู่หรือออกจาก `PaymentPending`, `Completed` หรือ `Cancelled` ต้องถูกปฏิเสธ
- เมื่อ `TransferItems` ย้ายรายการทั้งหมดจน session ต้นทางไม่มีรายการ ห้ามปิด session หรือเปลี่ยนโต๊ะต้นทางเป็น `Available` อัตโนมัติ ให้คง session/โต๊ะอยู่ในสถานะเปิดจนได้รับคำสั่ง `CloseTable`
- จำนวนรายการเป็นศูนย์ไม่ใช่เงื่อนไขว่าโต๊ะ `Available`; โต๊ะเปลี่ยนเป็น `Available` อัตโนมัติเฉพาะหลัง `CompletePayment` สำเร็จ ส่วนการปิดโต๊ะที่ยังไม่ชำระต้องเกิดจากคำสั่งปิดโต๊ะแบบชัดเจน
- `CloseTable` ทำได้เฉพาะ PC POS และเฉพาะ session ที่ไม่มีรายการคงเหลือและไม่มียอดค้างชำระ เมื่อสำเร็จจึงเปลี่ยนโต๊ะเป็น `Available`; หากยังมีรายการต้องใช้กระบวนการยกเลิกบิล ไม่ใช่ `CloseTable`
- ทุก `CloseTable` ต้องบันทึกผู้ดำเนินการ เวลา เหตุผล และ session/โต๊ะใน audit log

#### 4.3.3 D03 — การแยกสินค้า/แยกบิล — ยืนยันบางส่วน

- แยกได้ทั้งรายการเต็มและแบ่งจำนวน เช่น 3 ชิ้นแยกออก 1 ชิ้น
- Topping และหมายเหตุต้องติดไปกับสินค้าหลัก ไม่แยกเป็นรายการอิสระ
- ต้องดำเนินการก่อน `PaymentPending`; หลังเข้าสถานะนี้โต๊ะถูกล็อกและแก้ไขไม่ได้
- รายละเอียดที่ยังเปิด: วิธีจัดการส่วนลด/Service Charge/VAT หลังแยกตาม D05

#### 4.3.4 D03 — เนื้อหาใบแจ้งย้าย — ยืนยัน

- ใช้หัวเอกสาร `ใบแจ้งย้ายรายการ` สำหรับ `TransferItems` และ `ใบแจ้งย้ายโต๊ะ` สำหรับ `ChangeTable` โดยจัดหัวให้เด่นกว่าส่วนอื่น
- แสดงโต๊ะต้นทาง → โต๊ะปลายทาง และประเภทการย้าย
- แสดงจำนวนที่ย้ายจากจำนวนเดิม ชื่อสินค้า Topping และหมายเหตุ; กรณีแบ่งจำนวนให้แสดงจำนวนคงเหลือที่โต๊ะต้นทาง
- แสดงเวลา เหตุผล และหมายเลขอ้างอิง `transferEventId` แบบอ่านได้ เช่น `TRF-000184`
- ไม่พิมพ์ข้อความ `ไม่ใช่ออร์เดอร์ใหม่ / ไม่ต้องทำซ้ำ`, `รายการนี้เคยส่งครัวแล้ว` หรือ `เปลี่ยนเฉพาะโต๊ะปลายทาง`
- ไม่พิมพ์ชื่อ/รหัสผู้ย้ายบนใบแจ้ง แต่ระบบยังต้องบันทึก operator/account ของผู้ใช้ PC POS ใน immutable transfer event และ audit log เพราะสิทธิ์แบบ PC-only ไม่ได้ระบุว่าบุคคลใดใช้เครื่อง
- งานออร์เดอร์เดิมเป็น immutable document และต้องคงเลขโต๊ะ ณ เวลาสร้างงาน ไม่แก้ payload, ไม่ supersede และไม่สร้าง replacement เพราะ `ChangeTable`/`TransferItems`; งาน `PrintQueued` หรือ `Failed` ที่ retry ภายหลังยังใช้เลขโต๊ะเดิม
- ทุกคำสั่ง `ChangeTable`/`TransferItems` ต้องสร้างใบรวมหนึ่งชุดที่เครื่องแคชเชียร์โดยไม่ขึ้นกับสถานะงานออร์เดอร์เดิม เพื่อให้พนักงานเช็คอาหารใช้ประสานงาน
- PC POS ให้เลือก `notifyProductionStation` เป็นรายรายการ/จำนวนที่ย้าย ค่าเริ่มต้นเป็นเลือกสำหรับรายการที่มี Production Station; แคชเชียร์ยกเลิกการเลือกได้ รายการที่ไม่มีสถานีไม่แสดงตัวเลือก
- `ChangeTable` แสดงรายการทั้งหมดใน session ให้เลือกแจ้ง ส่วน `TransferItems` แสดงเฉพาะรายการและจำนวนที่ย้าย
- รายการที่เลือกแจ้งต้องถูกรวมตาม Production Station เป็นหนึ่งใบต่อสถานี ไม่สร้างหนึ่งใบต่อหนึ่งรายการ; รายการที่ไม่เลือกไม่มี print job ไปสถานีและให้พนักงานเช็คอาหารดำเนินการจากใบรวมแคชเชียร์
- ใบรวมแคชเชียร์ต้องแสดงรายการทั้งหมด พร้อมผลว่า `แจ้ง <สถานี>` หรือ `พนักงานเช็คอาหารดำเนินการ` สำหรับแต่ละรายการ
- ตัวเลือกและจำนวนที่แจ้งแต่ละสถานีต้องถูก snapshot ใน immutable transfer event/audit log งานใบรวมและใบรายสถานีแต่ละเครื่องมี `printJobId` ของตนเองและอ้างอิง `transferEventId`; retry และ `Unknown` ใช้กฎ D20
- การ commit การย้ายและ persistent print outbox ต้องอยู่ใน transaction เดียวกัน แต่ความล้มเหลวของงานพิมพ์หลัง commit ไม่ย้อนการย้าย PC ต้องแสดงสถานะและอนุญาต reprint ตามกฎ D20

ตัวอย่างรูปแบบ:

```text
*** ใบแจ้งย้ายรายการ ***

จาก: A05  →  ไป: B12
ประเภท: ย้ายบางรายการ

ย้าย 1 จากเดิม 3
ข้าวผัดกุ้ง
 + เพิ่มไข่ดาว
 หมายเหตุ: ไม่ใส่ต้นหอม
คงเหลือโต๊ะ A05: 2

เวลา: 20:14:32
เหตุผล: ลูกค้าขอย้ายโต๊ะ
Transfer: TRF-000184
```

#### 4.3.5 D05 — ส่วนลด VAT ค่าธรรมเนียม และการปัดเศษ — ยืนยันบางส่วน

- ราคาเมนูและ Topping เป็นราคารวม VAT; VAT เริ่มต้น 7% แต่ต้องเป็นค่าแบบ versioned/effective-dated ที่ปรับได้ และบิลต้อง snapshot อัตราที่ใช้
- Service Charge ปัจจุบัน 0% และ Card Charge ปัจจุบัน 0%; หากเปิดใช้ภายหลัง ค่าที่นำมาบวกในสูตรนี้ต้องเป็นยอดรวม VAT ตาม policy เดียวกับราคาเมนู เว้นแต่มีข้อกำหนดทางบัญชีที่ยืนยันต่างออกไป
- ส่วนลดระดับสินค้ารองรับทั้งเปอร์เซ็นต์และจำนวนเงิน สินค้าแต่ละ order line มีส่วนลดที่ active ได้หนึ่งค่า การกรอกใหม่เป็นการแทนค่าเดิม ไม่ใช่นำมาบวกหรือคำนวณซ้อน แต่ประวัติการเปลี่ยนต้องอยู่ใน audit log
- ใช้ส่วนลดระดับสินค้าได้กับกี่รายการในบิลก็ได้ แต่ละรายการคำนวณแยกกันและไม่นับเป็นส่วนลดท้ายบิล
- ส่วนลดท้ายบิลรองรับทั้งเปอร์เซ็นต์และจำนวนเงิน และคำนวณหลังส่วนลดระดับสินค้า
- บิลหนึ่งใบมีส่วนลดท้ายบิลที่ active ได้หนึ่งค่า การกรอกส่วนลดท้ายบิลใหม่เป็นการแทนค่าเดิม ไม่สะสมเปอร์เซ็นต์หรือจำนวนเงินเข้าด้วยกัน และต้องเก็บค่าก่อน/หลัง ผู้ดำเนินการ เวลา และเหตุผลใน audit log
- ส่วนลดท้ายบิลผูกกับ `tableSessionId` ไม่ผูกกับรายการ: รายการที่ยังอยู่ใน session เดียวกันใช้ส่วนลดท้ายบิลเดียวกันตามฐานที่คำนวณใหม่
- การย้ายบางรายการหรือย้ายรายการทั้งหมดออกไปยัง `tableSessionId` อื่นไม่นำส่วนลดท้ายบิลของต้นทางติดไปด้วย หาก session ปลายทางไม่มีส่วนลดท้ายบิล รายการที่ย้ายไปจะไม่ได้ส่วนลดท้ายบิล; หากปลายทางมีส่วนลดท้ายบิล ให้ใช้กฎของ session ปลายทาง
- การย้ายทั้งโต๊ะแบบเปลี่ยนหมายเลขโต๊ะแต่คง `tableSessionId` เดิมไม่ถือเป็นการย้ายรายการข้าม session จึงคงส่วนลดท้ายบิลเดิม
- ส่วนลดระดับสินค้าผูกกับ `orderLineId` และติดไปกับรายการเมื่อย้ายโต๊ะ/แยกบิล รวมถึงเมื่อแบ่งจำนวนต้องแบ่งมูลค่าส่วนลดของรายการอย่างตรวจสอบย้อนหลังได้
- หลังย้ายรายการ Branch ต้องคำนวณส่วนลดท้ายบิลของ session ต้นทางและปลายทางใหม่ก่อนแสดงยอด ผลรวมยอดหลังย้ายอาจต่างจากก่อนย้ายตาม policy นี้ และต้องแสดงยอดใหม่ให้ผู้ใช้ PC POS เห็นก่อนยืนยัน
- ลำดับยอดก่อน Global Rounding คือ `ผลรวมราคาสินค้ารวม VAT - ส่วนลดระดับสินค้า - ส่วนลดท้ายบิล + Card Charge รวม VAT + Service Charge รวม VAT`
- VAT เป็นส่วนหนึ่งของยอดดังกล่าว ให้แยกเพื่อรายงานด้วยสูตร inclusive ตามอัตราที่ snapshot ไว้ ห้ามบวก VAT ซ้ำอีกครั้ง
- ทุกช่องทางชำระใช้ยอดที่ปัดเศษสตางค์ลงเป็นจำนวนเงินบาทเต็ม เช่น 100.10–100.99 เป็น 100.00 โดยหลังสร้าง Draft Payment Plan และคำนวณ Card Charge แล้วให้ปัดยอดสุทธิของบิลหนึ่งครั้ง ห้ามปัดแต่ละ Payment Line แยกกัน และบันทึกส่วนต่างเป็น `roundingAdjustment` แยกจากส่วนลดตาม D05.9
- เมื่อเข้าสู่ `PaymentPending` ต้อง freeze ราคา ส่วนลด จำนวน Service Charge อัตรา VAT สูตรคำนวณ และ policy version; สำหรับ Card Charge ให้ freeze เฉพาะอัตราและ policy ส่วนจำนวนเงินจริงคำนวณและ freeze แยกเมื่อยืนยันแต่ละ Payment Line ตาม D05.8

##### D05.6 — ส่วนลดสูงกว่าฐานที่ใช้ลด — ยืนยัน

- ส่วนลดแบบจำนวนเงินอนุญาตให้กรอกค่ามากกว่าฐานที่มีสิทธิ์ลดได้ ระบบต้องเก็บค่าที่กรอกเป็น `requestedDiscountAmount`
- จำนวนที่ใช้จริงคือ `appliedDiscountAmount = min(requestedDiscountAmount, eligibleDiscountBase)` โดยค่าที่ใช้คำนวณต้องไม่ต่ำกว่า 0
- ห้ามส่วนลดทำให้ยอด Order Line, ยอดสินค้าหลังส่วนลดระดับรายการ, ฐานส่วนลดท้ายบิล, ฐาน VAT หรือยอดสุทธิติดลบ; ค่าต่ำสุดเป็น 0
- เมื่อเพิ่ม ลด ยกเลิก แยก หรือย้ายรายการก่อน `PaymentPending` ระบบต้องคำนวณ eligible base และ `appliedDiscountAmount` ใหม่ โดยไม่เปลี่ยน requested amount ที่ผู้ใช้ตั้งไว้
- หากฐานเพิ่มขึ้น Applied Discount เพิ่มตามได้แต่ไม่เกิน Requested; หากฐานลดลง Applied Discount ต้องลดลงตามฐาน
- ส่วนลดแบบเปอร์เซ็นต์ต้องอยู่ในช่วง 0–100% และใช้หลักไม่ให้ Applied Discount เกิน eligible base เช่นเดียวกัน
- ใช้กฎเดียวกันกับส่วนลดระดับ Order Line และส่วนลดท้ายบิล โดยส่วนลดท้ายบิลใช้ฐานหลังส่วนลดระดับรายการ
- Receipt/Preview/report snapshot และ audit ต้องเก็บ Requested กับ Applied แยกกัน และแสดงทั้งสองค่าเมื่อไม่เท่ากัน

##### D05.7 — ฐานคำนวณ Service Charge — ยืนยัน

- ค่าเริ่มต้นของ Service Charge เป็น 0%; หากเปิดใช้ภายหลังให้คำนวณจากยอดสินค้าและ Topping หลังหักส่วนลดระดับ Order Line และส่วนลดท้ายบิล
- ห้ามนำ Card Charge หรือ `roundingAdjustment` มาเป็นฐานคำนวณ Service Charge
- Service Charge เป็นยอดรวม VAT ตาม policy เดียวกับราคาอาหาร และต้องรวมอยู่ในยอดที่ใช้แยก VAT สำหรับรายงาน
- เมื่อรายการหรือส่วนลดเปลี่ยนก่อน `PaymentPending` ระบบต้องคำนวณฐานและจำนวน Service Charge ใหม่
- เมื่อเข้าสู่ `PaymentPending` ต้อง freeze ฐาน อัตรา จำนวน Service Charge และ policy version ที่ใช้
- อัตรา Service Charge ต้องรองรับทศนิยมและจัดเก็บแบบ versioned/effective-dated

##### D05.8 — ฐานและ VAT ของ Card Charge — ยืนยัน

- ค่าเริ่มต้นเป็น 0% และปิดใช้งาน เปิดใช้ได้เฉพาะ Payment Method ที่ส่วนกลางกำหนด `cardChargeApplicable = true`
- พนักงานกรอก `principalAppliedAmount` ซึ่งเป็นเงินต้นที่ Payment Line นำไปตัดยอดบิลก่อน Card Charge; ระบบคำนวณ `cardChargeAmount` จากเงินต้นของ Payment Line นั้น ไม่ใช่จากยอดทั้งบิล
- กรณี Split Payment หรือใช้บัตรหลายใบ ต้องคำนวณ Card Charge แยกตามแต่ละ Payment Line และอัตราที่ snapshot สำหรับ Payment Method นั้น
- ฐานของ Card Charge คือ `principalAppliedAmount`; ห้ามนำ Service Charge มาเพิ่มเป็นฐานซ้ำและห้ามคิด Card Charge ทบ Card Charge
- Card Charge ที่เรียกจากลูกค้าเป็นยอดรวม VAT ตามอัตราที่ snapshot ของบิล ระบบต้องแยกส่วนมูลค่าก่อน VAT และ VAT เพื่อรายงานโดยไม่บวก VAT เพิ่มจากยอด Card Charge อีกครั้ง
- Card Charge ที่ร้านเรียกจากลูกค้าต้องเก็บแยกจาก Merchant Fee ที่ธนาคารหรือผู้ให้บริการรับชำระหักจากร้านค้า
- ใน Payment Line ให้เก็บอย่างน้อย `principalAppliedAmount`, `cardChargeRate`, `cardChargeAmount`, `roundingAllocation` และ `appliedAmount`; โดย `appliedAmount` เป็นยอดที่รับผ่านช่องทางนั้นหลังรวม Card Charge และผลปัดเศษที่จัดสรรให้ line
- ระบบต้องแสดงทั้งยอดที่นำไปตัดบิลและยอดที่ต้องเรียกเก็บผ่านเครื่องรับบัตรอย่างชัดเจน ห้ามให้ระบบอนุมานเงินต้นย้อนกลับจากยอดรวมที่พนักงานกรอก
- เก็บ Card Charge แยกจากยอดสินค้า ส่วนลด Service Charge และ `roundingAdjustment` ใน bill/payment snapshot และรายงาน
- อัตราและกฎ Card Charge ต้องเป็น versioned/effective-dated และ snapshot ตอนเข้า `PaymentPending`; จำนวน Card Charge คำนวณเมื่อสร้าง Payment Line และ freeze เมื่อยืนยัน Payment Line นั้น
- ก่อนยืนยัน Payment Line หากเปลี่ยน Payment Method หรือ `principalAppliedAmount` ต้องคำนวณ Card Charge ใหม่
- หาก Reverse Payment Line ต้อง Reverse Card Charge ที่ผูกกับ Payment Line เดียวกันทั้งหมด; ห้าม Card Charge ติดลบ
- ก่อนเปิดใช้งานจริงต้องยืนยันเงื่อนไขการเรียกเก็บกับธนาคาร/ผู้ให้บริการรับบัตรและวิธีลงบัญชีกับผู้รับผิดชอบด้านบัญชี

##### D05.9 — การจัดสรรเศษปัดลง — ยืนยัน

- ก่อนรับเงินจริง ระบบต้องสร้าง Draft Payment Plan ที่ระบุ Payment Method และ `principalAppliedAmount` ของทุก Payment Line ในแผน
- ระบบคำนวณ Card Charge ของแต่ละ Payment Line แล้วรวมยอดทั้งหมดก่อนปัดยอดสุทธิลงเป็นจำนวนเงินบาทเต็มหนึ่งครั้งที่ระดับบิล
- ผลต่างจากการปัดต้องเก็บเป็น `roundingAdjustment` ระดับบิล และแยกจากส่วนลด โดยมีค่าเป็น 0 หรือติดลบ
- ให้จัดสรร `roundingAdjustment` ทั้งหมดไปยัง Card Payment Line สุดท้ายที่ก่อให้เกิด Card Charge; หากไม่มี Card Charge ให้จัดสรรไปยัง Payment Line สุดท้าย
- Payment Line ที่รับการจัดสรรต้องเก็บค่าใน `roundingAllocation`; ผลรวม `roundingAllocation` ของ effective Payment Lines ต้องเท่ากับ `roundingAdjustment` ระดับบิล
- `roundingAllocation` ต้องไม่มากกว่า 0 และห้ามทำให้ `appliedAmount` ของ Payment Line ติดลบ
- เมื่อ Payment Line ถูกยืนยันแล้ว ห้ามแก้ไขค่าภายใน หากต้องเปลี่ยนแผนหลังมี Line ที่ยืนยันแล้ว ให้ Reverse Line ที่ได้รับผลกระทบตาม D01.7 แล้วสร้าง Payment Line/แผนใหม่
- Receipt และรายงานแสดง `roundingAdjustment` เพียงครั้งเดียวที่ระดับบิล ไม่แสดงเป็นส่วนลด; รายละเอียดการจัดสรรระดับ Payment Line เก็บไว้สำหรับ audit และ reconciliation

#### 4.3.6 D06 — การขอตรวจสอบและยกเลิกรายการ — ยืนยัน

บทบาทที่เกี่ยวข้องคือพนักงานรับออร์เดอร์บน Mobile, แคชเชียร์บน PC POS และพนักงานเช็คอาหาร การขอยกเลิกไม่ทำให้รายการถูกยกเลิกทันที พนักงานเช็คอาหารต้องตรวจสถานะการผลิตกับครัว และแคชเชียร์เป็นผู้ตัดสินใจยืนยันจำนวนที่จะยกเลิกในระบบ ครัวไม่ได้เป็นผู้อนุมัติสิทธิ์ยกเลิก

```text
Active
  ↓ Mobile สร้างคำขอ
CancellationRequested
  ↓ แคชเชียร์ออกใบตรวจสอบ
ProductionStatusChecking
  ↓ แคชเชียร์บันทึกสถานะครบทุกจำนวน
StatusRecorded
  ├──→ ConfirmCancellation ──→ Confirmed / PartiallyConfirmed
  └──→ RejectCancellation  ──→ Rejected
                                  ↓ ปิดคำขอ
                           Cancelled / Active
```

- Mobile เลือกรายการ/จำนวน ระบุเหตุผลและหมายเหตุ แล้วสร้าง `cancellationRequestId`; Mobile ไม่มีสิทธิ์เปลี่ยน order line เป็น `Cancelled`
- ระหว่างตรวจสอบ `orderLineStatus` ยังคง `Active` และมี cancellation status แยก รายการ/จำนวนที่ถูกร้องขอต้องถูกล็อกจากการแก้จำนวน ย้ายโต๊ะ แยกบิล และคำขอยกเลิกซ้ำ แต่รายการอื่นในโต๊ะยังดำเนินการได้
- แคชเชียร์ออกใบตรวจสอบจาก PC POS ให้พนักงานเช็คอาหารนำไปตรวจสถานะกับครัว ครัวรายงานเฉพาะจำนวนที่ `NotStarted`, `InProgress` หรือ `Completed` ไม่ได้อนุมัติหรือปฏิเสธสิทธิ์ยกเลิก
- สำหรับรายการที่มีหลายจำนวน ต้องบันทึก `checkedQuantityByProductionStatus` แยกตามสถานะและผลรวมต้องเท่ากับจำนวนที่ขอตรวจสอบ; รายการที่ไม่มีกระบวนการผลิตใช้ `NotApplicable` และห้ามใช้ค่านี้กับอาหารที่มีงานผลิต
- แคชเชียร์ต้องบันทึกสถานะครบทุกจำนวนก่อนตัดสินใจ ห้ามใช้ค่า `Unknown` หรือปล่อยว่าง
- คำสั่งสุดท้ายมีสองแบบ: `ConfirmCancellation` ซึ่งยกเลิกได้ทั้งหมดหรือบางจำนวนตามการตัดสินใจของแคชเชียร์ และ `RejectCancellation` ซึ่งปิดคำขอโดยคงรายการเป็น `Active`; สถานะ `NotStarted`, `InProgress` หรือ `Completed` ห้ามทำให้ระบบปฏิเสธการยกเลิกโดยอัตโนมัติ
- หากยกเลิกเพียงบางจำนวน แคชเชียร์ต้องระบุ `cancelledQuantityByProductionStatus` โดยแต่ละค่าห้ามเกินจำนวนที่ตรวจพบในสถานะนั้น และผลรวมต้องเท่ากับจำนวนที่ยกเลิกจริง
- `ConfirmCancellation` ต้องสร้าง immutable cancellation event, คำนวณส่วนลด/VAT/ยอดบิลใหม่ และเก็บผู้ขอ ผู้ยืนยัน เวลา เหตุผล จำนวนที่ขอ/ยกเลิกจริง/ไม่ยกเลิก, `checkedQuantityByProductionStatus` และ `cancelledQuantityByProductionStatus`; ห้ามลบ order line ต้นฉบับ
- ห้ามเข้า `PaymentPending` ขณะที่มีคำขอที่ยังไม่ปิด ระบบต้องปฏิเสธ `RequestPayment` และแสดงคำขอค้างให้แคชเชียร์ดำเนินการก่อน ห้ามปิดหรือปฏิเสธคำขอโดยอัตโนมัติ
- ผลทางบัญชีหลังชำระใช้กระบวนการ Refund ส่วนกลางตาม D04.12–D04.12.1 ไม่ใช่การยกเลิกรายการที่สาขา

##### D06.1 — ผลต่อสต็อกเมื่อยกเลิกรายการ — ยืนยัน

- เมื่อ `ConfirmCancellation` สำหรับอาหารที่ติดตามสต็อก ระบบต้องสร้างผลด้านสต็อกจาก `cancelledQuantityByProductionStatus` ที่แคชเชียร์ยืนยัน ห้ามปล่อยสถานะว่าง
- จำนวน `NotStarted` ให้สร้าง Compensating Stock Movement เพื่อคืน/ย้อน Stock Movement เดิม
- จำนวน `InProgress` หรือ `Completed` ไม่คืนสต็อก เพราะเริ่มใช้วัตถุดิบหรือผลิตเสร็จแล้ว และต้องคงผลเป็นการใช้ไป/ของเสียตาม cancellation event
- `NotTracked` ใช้กับรายการที่ไม่ได้เปิดใช้การตัดสต็อกและต้องไม่สร้าง Stock Movement
- ห้ามใช้ค่าเริ่มต้นแทนสถานะที่ไม่ทราบ หากยังตรวจสถานะอาหารไม่ครบต้องปฏิเสธ `ConfirmCancellation`
- การยกเลิกบางจำนวนต้องคำนวณผลสต็อกเฉพาะจำนวนที่ยกเลิก โดยใช้ Recipe Snapshot ของรายการเดิม ห้ามคำนวณจาก Master Data ปัจจุบันซึ่งอาจเปลี่ยนแล้ว
- การคืนสต็อกต้องใช้ Compensating Stock Movement ที่อ้างอิง original Stock Movement และ cancellation event ห้ามลบ แก้ไข หรือเขียนทับ movement ต้นฉบับ
- ผลทางการเงินและผลทางสต็อกต้องแยกกัน รายการอาจถูกยกเลิกจากบิลแต่สต็อกยังคงถูกใช้เมื่ออาหารผลิตแล้วหรือวัตถุดิบถูกใช้ไป
- กฎนี้ให้สร้างการย้อนสต็อกเฉพาะเมื่อพบ original Stock Movement ที่มีผลจริง หากยังไม่มี movement ต้นทางต้องไม่สร้างสต็อกเพิ่ม
- สำหรับรายการ `NotApplicable` ที่ติดตามสต็อกแต่ไม่มีกระบวนการผลิต ให้แคชเชียร์ตัดสินใจเป็นรายรายการ/จำนวนว่าจะใช้ `ReturnToStock` หรือ `NoStockReturn`; ต้องเลือกก่อน `ConfirmCancellation` และห้ามระบบอนุมานให้อัตโนมัติ
- `ReturnToStock` ต้องสร้าง Compensating Stock Movement อ้างอิง movement ต้นทาง ส่วน `NoStockReturn` ไม่คืนสต็อกและคงการใช้ไปตามเดิม
- Cancellation event ต้องเก็บ inventory decision ของแคชเชียร์ ผู้ดำเนินการ เวลา จำนวน และหมายเหตุเมื่อมี เพื่อให้ตรวจย้อนหลังได้ โดยไม่ต้องใช้ PIN เพิ่มจากสิทธิ์ที่ใช้ดำเนินการยกเลิก

##### D06.2 — การยกเลิกทั้งบิลจากส่วนกลาง — ยืนยัน

- ส่วนกลางห้ามยกเลิกบิลสถานะ `Ordering`, `PaymentPending`, `PaymentInProgress` หรือ `PartiallyPaid` โดยตรง ต้องให้สาขาดำเนินการตาม D01.3 และ D06
- ส่วนกลางสร้าง `CentralBillCancellation` ได้เฉพาะบิลสถานะ `Completed` และต้องยกเลิกทั้งบิล ห้ามเลือกบาง Order Line
- การยกเลิกต้องสร้าง immutable cancellation event/tombstone ห้ามลบ แก้ไข หรือเขียนทับบิลและ snapshot ต้นฉบับ
- `CentralBillCancellation` ไม่ทำ Refund อัตโนมัติ ไม่ Reverse Payment ไม่เปลี่ยนเงินในลิ้นชัก และไม่คืนสต็อก
- ห้ามแก้ `CloseStore` snapshot เดิมหรือข้อมูล Google Sheets ที่ส่งไปแล้ว รายงานส่วนกลางต้องแสดงยอดขายเดิมและยอดยกเลิกเป็น adjustment แยกเพื่อให้ตรวจย้อนหลังได้
- การยกเลิกนี้ไม่ถือเป็นการออกใบลดหนี้หรือเอกสารภาษีโดยอัตโนมัติ กระบวนการเอกสารทางบัญชีต้องแยกต่างหาก
- ผู้ดำเนินการต้องเป็นผู้ใช้ส่วนกลางที่มีสิทธิ์ `CancelCompletedBill` และต้องกรอกเหตุผล
- Event ต้องเก็บผู้ดำเนินการ เวลา เหตุผล บิล/receipt ต้นทาง ยอดและ snapshot ที่เกี่ยวข้อง และ `centralCancellationId`
- การ Offline ของสาขาไม่ขัดขวางการบันทึก event ที่ส่วนกลาง เมื่อ Branch ออนไลน์ให้ซิงก์สถานะมาแสดงในประวัติแบบอ่านอย่างเดียว
- คำสั่งซ้ำด้วย `centralCancellationId` เดิมต้องคืนผลเดิมและห้ามสร้าง event ซ้ำ
- อนุญาตให้ยกเลิก `CentralBillCancellation` หรือคืนสถานะบิลได้จากส่วนกลางตาม D06.3 โดยผู้ดำเนินการต้องเป็นคนละบุคคลกับผู้ที่สร้างการยกเลิกเดิม

##### D06.3 — ยกเลิกการยกเลิกบิลจากส่วนกลาง — ยืนยัน

- ใช้คำสั่ง `ReinstateCancelledBill` ได้เฉพาะบิลที่ถูก `CentralBillCancellation` เท่านั้น ห้ามใช้กับการยกเลิกที่เกิดจากสาขา
- ผู้ดำเนินการต้องมีสิทธิ์ส่วนกลาง `ReinstateCancelledBill` ซึ่งแยกจาก `CancelCompletedBill`
- `centralUserId` ของผู้คืนสถานะต้องไม่ตรงกับผู้สร้างการยกเลิกเดิม แม้บัญชีเดียวกันมีทั้งสองสิทธิ์ระบบต้องปฏิเสธ; หากบัญชีผูกกับบุคคล/พนักงานต้องตรวจ `personId`/`employeeId` ด้วยเพื่อป้องกันบุคคลเดียวกันใช้หลายบัญชี
- ต้องกรอกเหตุผลบังคับ
- ต้องสร้าง immutable `BillReinstatementEvent` อ้างอิง `centralCancellationId` ห้ามลบ แก้ไข หรือเขียนทับ event การยกเลิกเดิม
- การคืนสถานะไม่เปิดโต๊ะหรือบิลกลับมาแก้ไข สถานะธุรกรรมต้นทางยังเป็น `Completed`; ผลคือยกเลิก cancellation adjustment ในรายงานส่วนกลางและแสดง event chain จากยอดขายเดิม → `CentralBillCancellation` → `BillReinstatementEvent`
- ห้ามสร้าง Payment, Refund, Stock Movement หรือ Cash Movement เพิ่ม และห้ามแก้ `CloseStore` snapshot หรือข้อมูล Google Sheets ที่ส่งไปแล้ว
- เมื่อ Branch ออนไลน์ให้ส่งสถานะล่าสุดไปแสดงในประวัติแบบอ่านอย่างเดียว
- Event ต้องเก็บ `billReinstatementId`, ผู้ดำเนินการ เวลา เหตุผล และ snapshot ของสถานะก่อนดำเนินการ
- คำสั่งซ้ำด้วย `billReinstatementId` เดิมต้องคืนผลเดิม บิลที่คืนสถานะแล้วห้าม Reinstate ซ้ำ
- หากต้องยกเลิกบิลเดิมอีกครั้ง ต้องสร้าง `CentralBillCancellation` ใหม่ พร้อมเหตุผลและ audit chain ใหม่ทั้งหมด

งานพิมพ์เกี่ยวกับการยกเลิก:

- ไม่ส่งใบตรวจสอบหรือใบแจ้งยกเลิกไปยังเครื่องพิมพ์ครัว เค้ก หรือสถานีผลิตใด
- ใบตรวจสอบก่อนตัดสินใจพิมพ์ที่เครื่องแคชเชียร์เพียงจุดเดียว โดยแสดงโต๊ะ รายการ/จำนวน Topping/หมายเหตุ เหตุผล เวลา และ `cancellationRequestId` พร้อมช่องบันทึกจำนวน `NotStarted`, `InProgress`, `Completed` หรือ `NotApplicable`
- เมื่อ `ConfirmCancellation` สำเร็จ ให้รวมรายการและจำนวนที่ยกเลิกจริงในการยืนยันครั้งนั้นเป็นใบสรุปหนึ่งใบ แล้วส่งไปเครื่องแคชเชียร์เพียงจุดเดียว โดยแสดงจำนวนที่ยกเลิกแยกตามสถานะการผลิตและผลคืน/ไม่คืนสต็อก; จำนวนที่แคชเชียร์ไม่ยกเลิกไม่รวมในยอดยกเลิก
- การ commit การยกเลิกและ persistent print outbox ต้องอยู่ใน transaction เดียวกัน แต่ความล้มเหลวของเครื่องพิมพ์หลัง commit ไม่ย้อนการยกเลิก ระบบต้องแสดงสถานะงานและใช้ retry/`Unknown` ตาม D20
- ใบตรวจสอบและใบสรุปผลต้องมี `printJobId` แยกและอ้างอิง `cancellationRequestId`; การพิมพ์ซ้ำต้องมีสถานะ reprint และ audit

#### 4.3.7 D04.1 — Payment State Machine — ยืนยัน

```text
Ordering
  ↓ RequestPayment
PaymentPending
  ├── CancelPaymentRequest ──→ Ordering
  ↓ StartPayment โดย PC POS
PaymentInProgress
  ├── PaymentFailed / CancelPayment ──→ PaymentPending
  └── CompletePayment ──→ Completed ──→ โต๊ะ Available
```

- `RequestPayment` ต้องผ่านเงื่อนไข D06 ว่าไม่มี cancellation request ค้าง และเมื่อ commit แล้วต้อง freeze รายการ ราคา ส่วนลด ค่าธรรมเนียม VAT และ calculation policy
- `StartPayment`, `CancelPayment` และ `CompletePayment` ทำที่ PC POS เท่านั้น
- หากมีเงินบางส่วนถูกรับหรือยืนยันจากช่องทางภายนอกแล้ว ห้ามกลับ `Ordering` จนกว่าจะบันทึกการย้อนรายการรับเงินสำเร็จ
- D04.3 ยืนยันการรับหลายวิธีในบิลเดียวแล้ว จึงเปิดใช้ `PartiallyPaid` สำหรับกรณีมี Payment Line ที่ยืนยันแล้วแต่ยอดยังไม่ครบ
- หลัง `CompletePayment` ห้ามแก้ session เดิมและโต๊ะเปลี่ยนเป็น `Available` อัตโนมัติ

#### 4.3.8 D04.2 — วิธีชำระเงิน — ยืนยัน

ช่องทางที่ต้องรองรับ:

- เงินสด (`Cash`)
- QR (`QR`)
- โอนเงินผ่านเลขบัญชี (`BankTransfer`)
- บัตรเครดิต (`CreditCard`)
- บัตรเดบิต (`DebitCard`)
- ช่องทางอื่นแบบกำหนดชื่อ (`Custom`)

ข้อกำหนดของช่องทาง `Custom` — ยืนยัน:

- จัดการเป็น Payment Method Master Data โดยมี `paymentMethodId` คงที่, ชื่อแสดงผล, สถานะ active/inactive และลำดับแสดง ไม่ใช้ชื่อเป็น primary key
- แคชเชียร์เลือกจากรายการที่ตั้งไว้ ไม่กรอกชื่ออิสระใหม่ทุกธุรกรรม เพื่อให้รายงานจัดกลุ่มได้
- Payment ต้อง snapshot ชื่อช่องทางที่ใช้ ณ เวลาชำระ การเปลี่ยนชื่อหรือปิดใช้ภายหลังต้องไม่เปลี่ยนประวัติเดิม
- ค่าเริ่มต้นของ `Custom` เป็น non-cash/manual: ยอดต้องตรงส่วนที่จัดสรร, ไม่มีเงินทอน และไม่เปิดลิ้นชัก เว้นแต่กำหนดพฤติกรรมเพิ่มเติม; ใช้ยอดบิลหลัง Global Rounding เช่นเดียวกับทุกช่องทาง
- Branch ต้อง cache Payment Method Master Data ที่ publish แล้วเพื่อเลือกใช้งานขณะอินเทอร์เน็ตขาด

การยืนยัน QR, โอนผ่านเลขบัญชี, บัตรเครดิตและบัตรเดบิตในระยะแรกเป็น Manual confirmation โดยแคชเชียร์:

- ระบบไม่อ้างว่าตรวจสอบยอดหรือสถานะจากธนาคาร/Payment Gateway/เครื่อง EDC อัตโนมัติ
- แคชเชียร์ตรวจสอบจากหลักฐานหรืออุปกรณ์ภายนอก แล้วกดยืนยันจำนวนเงินและช่องทางใน PC POS
- เก็บผู้ยืนยัน เวลา จำนวนเงิน ช่องทาง และเลขอ้างอิง/หมายเหตุถ้ามีใน payment audit
- หากแคชเชียร์ยังยืนยันผลไม่ได้ ห้าม `CompletePayment`; ให้คง `PaymentInProgress` หรือกลับ `PaymentPending` ตาม State Machine
- การเชื่อม API/Payment Gateway/EDC ภายหลังต้องเพิ่ม adapter และ state/หลักฐานของตนเอง โดยไม่เปลี่ยนประวัติ payment แบบ manual เดิม

#### 4.3.9 D04.3 — SplitTender — ยืนยัน

- บิลเดียวรองรับหลายวิธีชำระ เช่นเงินสด 400 บาทและ QR 600 บาทสำหรับยอดรวม 1,000 บาท
- แต่ละส่วนเป็น Payment Line แยก มี `paymentLineId`, `paymentMethodId`, จำนวนเงิน, สถานะยืนยัน, ผู้ยืนยัน, เวลา และ reference/หมายเหตุถ้ามี
- เมื่อ Payment Line แรกได้รับการยืนยันแต่ยอดยังไม่ครบ ให้ session เป็น `PartiallyPaid` และ persist ข้อมูลทันทีเพื่อกู้คืนได้หลัง process/OS restart
- `CompletePayment` ทำได้เมื่อยอด Payment Line ที่มีผลรวมครบยอดที่ต้องชำระตามกฎเงินสด/เงินทอนที่ยังต้องกำหนด
- Payment Line ที่ยืนยันแล้วห้ามแก้หรือลบตรง ๆ หากบันทึกผิดต้องสร้าง reversal event; ต้องย้อนทุกรายการสำเร็จก่อนกลับ `PaymentPending` หรือ `Ordering`
- ระหว่าง `PartiallyPaid` ห้ามแก้สินค้า ส่วนลด ย้าย/แยกโต๊ะ หรือเพิ่มคำขอยกเลิก
- Payment Method เดียวกันใช้ได้สูงสุดหนึ่ง Payment Line ต่อบิล ตาม D04.6

#### 4.3.10 D04.4 — Global Rounding — ยืนยัน

- ปัดเศษสตางค์ลงทุกช่องทาง โดย `amountDue = floor(netAmount)` เป็นจำนวนเงินบาทเต็ม
- คำนวณ VAT และยอดก่อนปัดจากสูตร D05 ก่อน จากนั้นเก็บผลต่างเป็น `roundingAdjustment = amountDue - netAmount`; ห้ามบันทึกเป็นส่วนลด
- สำหรับ SplitTender ให้ปัดยอดบิลหนึ่งครั้งก่อนแบ่งยอด และผลรวม Payment Line ทุกช่องทางต้องเท่ากับ `amountDue` ห้าม floor แต่ละ Payment Line เพราะจะลดเศษซ้ำ
- ตัวอย่าง `netAmount = 100.90` ได้ `roundingAdjustment = -0.90` และ `amountDue = 100.00` ไม่ว่าจะชำระเงินสด QR โอน บัตร Custom หรือหลายช่องทางร่วมกัน
- Payment Line ที่ยืนยันแล้วอ้างอิง calculation snapshot และยอดหลังปัดเดียวกัน ห้ามคำนวณ rounding ใหม่ระหว่างรับชำระ

#### 4.3.11 D04.5 — เงินรับเกินและเงินทอน — ยืนยัน

- เงินสดรับมากกว่ายอดคงเหลือได้และคำนวณเงินทอน โดยเก็บ `tenderedAmount`, `appliedAmount` และ `changeAmount` แยกกัน
- QR, โอน, บัตรเครดิต, บัตรเดบิต และ Custom ต้องมีจำนวนไม่เกินยอดที่จัดสรรและไม่มีเงินทอน
- สำหรับ SplitTender อนุญาตให้เฉพาะ Payment Line เงินสดลำดับสุดท้ายรับเกินยอดคงเหลือ; Payment Line ก่อนหน้าทุกช่องทางต้องไม่ทำให้ยอด applied เกิน `amountDue`
- `CompletePayment` ตรวจจากผลรวม `appliedAmount` เท่ากับ `amountDue`; เงินรับส่วนเกินที่คืนเป็นเงินทอนไม่ใช่ยอดขายและห้ามรวมในรายรับ
- ตัวอย่างยอดคงเหลือ 150 บาท รับเงินสด 200 บาท ให้ `appliedAmount = 150`, `changeAmount = 50` และยอดขายเพิ่มเพียง 150 บาท

#### 4.3.12 D04.6 — Payment Method ซ้ำในบิล — ยืนยัน

- หนึ่งบิลมี Payment Line ได้สูงสุดหนึ่งรายการต่อ `paymentMethodId`; ห้ามใช้วิธีเดียวกันซ้ำ เช่น QR สองรายการหรือบัตรเครดิตสองใบในบิลเดียว
- Credit Card และ Debit Card เป็นคนละ `paymentMethodId` จึงใช้ร่วมกันได้; Custom แต่ละรายการที่สร้างใน Master Data ก็เป็นคนละวิธี
- Payment Line ที่ยังไม่ยืนยันสามารถแก้จำนวนหรือแทนค่าได้ ส่วนรายการที่ยืนยันแล้วต้องใช้ reversal event ก่อนสร้างค่าทดแทน ห้ามแก้หรือลบตรง ๆ; สิทธิ์และกฎอ้างอิง D01.7
- เงินสดมี Payment Line เดียวและเป็นรายการสุดท้ายที่รับเกินยอดคงเหลือได้ตาม D04.5

#### 4.3.13 D04.7 — ค้างจ่าย — ยืนยันว่าไม่รองรับ

- ห้าม `CompletePayment` หากผลรวม `appliedAmount` ของ Payment Line ที่มีผลไม่เท่ากับ `amountDue`
- `PartiallyPaid` เป็นสถานะระหว่างดำเนินการเท่านั้น ไม่ใช่เครดิตลูกค้าหรือบิลค้างจ่าย และห้ามปิด table session/เปลี่ยนโต๊ะเป็น `Available`
- ต้องรับยอดที่เหลือให้ครบ หรือสร้าง reversal ของ Payment Line ที่ยืนยันแล้วทั้งหมดก่อนยกเลิก payment flow
- ห้ามปิดกะหรือปิดระบบธุรกิจประจำวันโดยปล่อย `PartiallyPaid` ไว้โดยไม่มีการแจ้งเตือนและขั้นตอนแก้ไข; รายละเอียดกะจะกำหนดภายหลังใน D04
- หากอนาคตต้องรองรับลูกหนี้/เครดิต ต้องออกแบบเป็น Accounts Receivable แยก ไม่ใช้ `PartiallyPaid` แทน

#### 4.3.14 D04.8 — ลิ้นชักเงิน — ยืนยัน

- หลัง `CompletePayment` commit สำเร็จ ให้สร้างคำสั่งเปิดลิ้นชักอัตโนมัติเฉพาะบิลที่มี Payment Line เงินสด; QR, โอน, Credit Card, Debit Card และ Custom ไม่เปิดลิ้นชัก
- คำสั่งเปิดลิ้นชักต้องมี `drawerEventId` และ persistent outbox แยกจาก `receiptPrintJobId`; retry/reprint ใบเสร็จห้ามเปิดลิ้นชักซ้ำ
- การเปิดลิ้นชักด้วยตนเองทำได้เฉพาะ PC POS และต้องยืนยันด้วย PIN ของพนักงาน Active ที่ส่วนกลางกำหนดสิทธิ์ `ManualDrawerOpen`; พนักงานทั่วไปที่ไม่มีสิทธิ์นี้สั่งเปิดไม่ได้
- Manual Drawer Open ไม่บังคับเลือกหรือกรอกเหตุผล เพราะระบบพิสูจน์วัตถุประสงค์หรือการเคลื่อนย้ายเงินจริงหลังลิ้นชักเปิดไม่ได้
- ทุกคำขอเปิดด้วยตนเองต้องสร้าง `drawerEventId` และ audit log โดยบันทึก employee ID, `storeSessionId`, `shiftId` เมื่อมี, เวลา Branch, device/terminal, ชนิด `ManualOpen` และผล `Dispatched`/`Failed`/`Unknown`; ห้ามบันทึก PIN แบบ plaintext
- สิทธิ์และสถานะพนักงานใช้ค่าล่าสุดที่ Branch Service ซิงก์ได้ตาม D11 เพื่อให้ทำงานออฟไลน์ได้ภายใต้ข้อจำกัดการเพิกถอนจากส่วนกลาง
- ความล้มเหลวหรือ `Unknown` ของคำสั่งลิ้นชักหลัง payment commit ไม่ย้อน Payment/ยอดขาย ระบบต้องแสดงสถานะให้แคชเชียร์และห้าม retry แบบที่อาจเปิดซ้ำโดยไม่มีการยืนยัน
- การทดสอบ D13 ต้องยืนยัน drawer port/pulse ของ Epson TM-T82 รุ่นจริง และพิสูจน์ว่าใบเสร็จ reprint ไม่ส่ง drawer pulse

##### D04.8.1 — ช่วงเวลาที่อนุญาตให้เปิดลิ้นชักด้วยตนเอง — ยืนยัน

- `ManualDrawerOpen` สำหรับงานขายทำได้เฉพาะเมื่อทั้ง `storeSessionId` และ `shiftId` อยู่สถานะ `Open`
- ช่วงหลัง `CloseShift` และก่อน `OpenShift` ถัดไป รวมถึงหลัง `CloseStore` ต้องปฏิเสธคำสั่งเปิดลิ้นชักด้วยตนเอง
- กรณีต้องแก้การทอนเงินผิดต้องดำเนินการก่อนปิดกะ และยังต้องใช้ PIN ที่มีสิทธิ์ `ManualDrawerOpen` ตาม D04.8
- ขณะอินเทอร์เน็ตขาดยังใช้ได้ โดย Branch Service ตรวจ employee status/PIN verifier/สิทธิ์ล่าสุดที่ซิงก์ไว้ตาม D11 และบันทึก audit/outbox เพื่อซิงก์ภายหลัง
- การทดสอบลิ้นชักระหว่างติดตั้งหรือซ่อมบำรุงต้องเป็นคำสั่งแยกจาก `ManualDrawerOpen` ของงานขาย อยู่ในหน้าตั้งค่า/วินิจฉัย ใช้สิทธิ์เฉพาะ และสร้าง drawer event/audit ทุกครั้ง

#### 4.3.15 D04.9 — การพิมพ์ใบเสร็จ — ยืนยัน

- เมื่อ `CompletePayment` commit สำเร็จ ต้องสร้าง receipt snapshot/เลขใบเสร็จและ persistent print job สำหรับเครื่องแคชเชียร์หนึ่งงานภายใน transaction เดียวกัน
- พิมพ์ใบเสร็จไม่สำเร็จหรือผลเป็น `Unknown` ไม่ย้อน Payment, ยอดขาย หรือสถานะ `Completed`; PC ต้องแสดงสถานะงานและให้ดำเนินการตาม D20
- การพิมพ์ครั้งแรกจาก original `receiptPrintJobId` เป็นใบต้นฉบับ การสั่งพิมพ์ภายหลังต้องสร้าง reprint job ใหม่ที่อ้างอิง receipt เดิมและระบุ `สำเนา`
- Reprint ต้องใช้ snapshot เดิมทั้งหมด ห้ามดึงราคา Master Data ใหม่หรือคำนวณส่วนลด VAT rounding และ Payment Lines ใหม่
- ทุก reprint เก็บผู้สั่ง เวลา เหตุผล และผลการพิมพ์ใน audit log และห้ามส่ง drawer pulse ตาม D04.8
- รายละเอียดข้อมูลบนใบเสร็จกำหนดใน D04.10 ส่วนรูปแบบ visual/ความกว้างคอลัมน์ทดสอบกับ D13

#### 4.3.16 D04.10 — ข้อมูลบนใบเสร็จ — ยืนยัน

ใบเสร็จ snapshot ต้องมีข้อมูลอย่างน้อย:

- ชื่อร้าน สาขา ที่อยู่ และเลขประจำตัวผู้เสียภาษีจาก Master Data version ที่ใช้ตอนออกเอกสาร
- เลขใบเสร็จ วันเวลา และ business date
- โต๊ะและแคชเชียร์
- ชื่อสินค้า จำนวน ราคาต่อหน่วย Topping/หมายเหตุ และยอดต่อรายการ
- ส่วนลดระดับสินค้าและส่วนลดท้ายบิล
- ยอดก่อนปัด `roundingAdjustment` และยอดสุทธิที่ต้องชำระ
- ฐานภาษีและ VAT ที่แยกจากราคารวม VAT รวมถึง Service Charge/Card Charge เมื่อมี
- วิธีชำระและจำนวน `appliedAmount` แต่ละช่องทาง รวม `tenderedAmount`/`changeAmount` สำหรับเงินสด
- สำหรับ reprint ต้องมีคำว่า `สำเนา` และเวลาพิมพ์ซ้ำ โดยยังคงเลขใบเสร็จ/ข้อมูล snapshot เดิม

ข้อกำหนดข้อมูลปลอดภัย:

- ไม่พิมพ์ UUID ภายใน เช่น `tableSessionId`, `paymentId` หรือ `printJobId`; ใช้เลขเอกสารที่อ่านได้สำหรับงานหน้าร้าน
- ห้ามเก็บหรือพิมพ์เลขบัตรเต็ม CVV หรือข้อมูลรับรองการชำระที่อ่อนไหว เลขอ้างอิงที่จำเป็นต้องแสดงต้องใช้ค่าที่ปลอดภัยหรือ masking
- การแก้ Master Data ร้าน สาขา เมนู ราคา ภาษี หรือชื่อ Payment Method ภายหลังห้ามเปลี่ยน receipt snapshot เดิม

#### 4.3.17 D04.11 — รูปแบบกะ — ยืนยันบางส่วน

- `OpenStore` เป็นการเปิดกะแรกชนิดพิเศษ: สร้าง `storeSessionId` สำหรับรอบการเปิดร้านและสร้าง `shiftId` แรกภายในรอบนั้น ไม่ใช้ `OpenShift` ปกติแทนการเปิดร้าน
- `OpenShift` ใช้เปิดกะถัดไประหว่างที่ `storeSessionId` ยังอยู่สถานะ `Open`; รองรับหลายกะภายใน business date เดียว แต่หนึ่งสาขามีกะสถานะ `Open` ได้พร้อมกันสูงสุดหนึ่งกะ
- ทุก Payment/receipt/drawer event ต้องผูก `shiftId` และ `storeSessionId`; `CompletePayment` ต้องถูกปฏิเสธหากไม่มีกะเปิด
- `CloseShift` แล้ว `OpenShift` ใหม่ได้โดยไม่เปลี่ยน `storeSessionId` หรือ business date ตราบใดที่ร้านยังเปิดและอยู่ภายในวันปฏิทินท้องถิ่นของสาขาตาม D12.9
- Process/OS restart ไม่ปิดกะอัตโนมัติ เมื่อระบบกลับมาต้อง resume กะเดิมและรักษา `shiftId`
- ห้ามปิดกะเมื่อมี table session สถานะ `PartiallyPaid`; ระบบต้องแสดงรายการค้างให้แก้ไขก่อน
- แยกการจบกะเป็น `CloseShift` สำหรับปิดกะระหว่างวันโดยคง `storeSessionId` และ `CloseStore` สำหรับปิดกะสุดท้ายพร้อมปิด `storeSessionId`; ห้ามอนุมานว่าการปิดกะใดเป็นกะสุดท้ายจากลำดับเพียงอย่างเดียว ผู้ใช้ต้องเลือก `CloseStore` อย่างชัดเจน
- ยังต้องกำหนดการแจ้งเตือนส่วนกลางเมื่อยอดต่างและเนื้อหารายงานปิดกะ/ปิดร้าน

##### D04.11.1 — การเปิดร้าน การเปิดกะ และเงินสดตั้งต้น — ยืนยัน

- `OpenStore` ต้องกรอก `openingCashAmount` ทุกครั้งและอนุญาตค่า 0 บาท ห้ามเว้นเป็น null; เงินนี้เป็นยอดตั้งต้นของ `storeSessionId` และไม่ใช่ยอดขายหรือรายรับ
- `OpenShift` ระหว่างวันให้ `openingCashAmount` เป็น optional หากไม่กรอกต้องเก็บเป็น null ห้ามตีความเป็น 0 บาท; หากกรอกให้เป็นข้อมูลประกอบการส่งมอบกะเท่านั้นและห้ามนำไปรีเซ็ตหรือคำนวณ `expectedCashAmount`
- เงินสดคงต่อเนื่องตลอด `storeSessionId`; `expectedCashAmount` เริ่มจากยอดของ `OpenStore` แล้วบวกยอดสุทธิของ Cash Payment Lines ที่ commit และยังมีผลจากทุกกะในรอบร้านนั้น โดยใช้ `appliedAmount` ไม่ใช้ `tenderedAmount` และไม่บวก/ลบเงินทอนซ้ำ
- ระบบไม่รองรับการเติม ถอน ส่งมอบเงินจริง หรือนำเงินออกจากลิ้นชักระหว่างที่ร้านเปิด พนักงานเปลี่ยนเงินในลิ้นชักได้เฉพาะการรับเงินและทอนเงินของการชำระเงินสด
- เก็บ `storeSessionId`, `shiftId`, จำนวนเงินเมื่อมี, employee ID ของผู้เปิด, เวลา Branch และชนิดคำสั่งใน audit
- หลังยืนยันแล้วห้ามแก้ `openingCashAmount` ตรง ๆ และไม่มี adjustment/correction function; หากยืนยันค่าผิด expected, difference และรายงานต้องคงตามข้อมูลที่ยืนยันนั้น

##### D04.11.2 — การนับเงินสดและผลต่าง — ยืนยัน

- สำหรับ `CloseShift` ระหว่างวัน `actualCashAmount` เป็น optional เพราะไม่ได้หมายถึงการปิดร้าน หากไม่กรอกต้องเก็บเป็น null และระบุว่า `NotReconciled` ห้ามตีความเป็น 0 บาทหรือสร้าง `cashDifference`
- หากผู้ปิดกะระหว่างวันเลือกกรอก `actualCashAmount` ให้ใช้กฎการคำนวณ การตรวจซ้ำ และความเป็น immutable เดียวกับการปิดร้าน
- สำหรับ `CloseStore` ต้องกรอก `actualCashAmount` ซึ่งเป็นเงินสดที่นับได้จริง ห้ามเว้นเป็น null และอนุญาตค่า 0 บาท
- สำหรับ `CloseShift` ระบบคำนวณ `expectedCashAmount` จาก `openingCashAmount` ของ `OpenStore` บวก Cash `appliedAmount` สุทธิที่ commit ถึงเวลาปิดกะ; สำหรับ `CloseStore` ให้รวม Cash `appliedAmount` สุทธิของทุก `shiftId` ใน `storeSessionId` แล้วคำนวณ `cashDifference = actualCashAmount - expectedCashAmount`
- การกรอก actual ตอน `CloseShift` ระหว่างวันเป็นการกระทบยอดเท่านั้น ไม่รีเซ็ตหรือแก้ expected cash ของ `storeSessionId`
- `cashDifference > 0` คือเงินเกิน, `< 0` คือเงินขาด และ `= 0` คือยอดตรง
- ผลต่างเป็นข้อมูล reconciliation/audit แยกต่างหาก ห้ามนำไปแก้ยอดขาย Payment VAT หรือ `openingCashAmount` อัตโนมัติ
- เก็บ expected, actual, difference, ผู้กรอก และเวลาไว้ใน shift closing snapshot; กฎเหตุผล/อนุมัติเมื่อยอดต่างยังต้องกำหนด
- ก่อน final confirmation แคชเชียร์แก้ค่าที่กรอกได้ตามปกติ แต่หน้าจอต้องแสดง `actualCashAmount` ให้ตรวจซ้ำและเตือนชัดเจนว่าเมื่อยืนยันแล้วแก้ไม่ได้
- หลัง final confirmation ค่า `actualCashAmount` เป็น immutable ห้ามแก้ เขียนทับ สร้าง recount/correction attempt หรือเปิดฟังก์ชัน “แก้ไขยอดที่กรอกผิด”; หากผู้ใช้ยืนยันค่าผิด ผลต่างและรายงานต้องคงตามข้อมูลที่ยืนยันนั้น

##### D04.11.3 — การแสดงยอดที่คาดไว้ — ยืนยัน

- หน้าปิดกะแสดง `expectedCashAmount` ให้แคชเชียร์เห็นก่อนกรอกและยืนยัน `actualCashAmount`; ไม่ใช้ Blind Count
- แสดงองค์ประกอบที่คำนวณได้อย่างน้อยเงินสดตั้งต้นของ `OpenStore` และยอด Cash `appliedAmount` สุทธิที่มีผล เพื่อให้ตรวจสอบที่มาของ expected ได้
- ค่า expected ที่ใช้ `CloseShift` ต้องคำนวณจากข้อมูลที่ commit แล้วถึงเวลาปิดกะ ส่วน `CloseStore` ต้องครอบคลุมทุก `shiftId` ใน `storeSessionId`; ค่าที่เกี่ยวข้องถูก snapshot พร้อม actual/difference ตอน final confirmation
- การแสดง expected ไม่เปลี่ยนกฎว่า actual แก้ได้ก่อน final confirmation เท่านั้น และ immutable หลังยืนยัน

##### D04.11.4 — เงินขาด/เกิน — ยืนยัน

- หาก `cashDifference != 0` ยังอนุญาตให้ final confirmation และปิดกะได้ ไม่บังคับให้ยอดตรงก่อนปิด
- ช่องเหตุผล/หมายเหตุเป็น optional ไม่บังคับกรอก
- ต้องบันทึก expected, actual, difference, ผู้ปิดกะ, เวลา และเหตุผลเมื่อมีใน shift closing snapshot/audit และแสดงผลต่างในรายงานปิดกะ
- ห้ามนำผลต่างไปแก้ยอดขาย Payment VAT หรือ `expectedCashAmount` อัตโนมัติ

##### D04.11.5 — การยืนยันตัวตนสำหรับเปิดกะ ปิดกะ และปิดร้าน — ยืนยัน

- `OpenStore` ต้องรับ PIN ทุกครั้ง และอนุญาตเฉพาะพนักงาน Active ที่ส่วนกลางกำหนดสิทธิ์เปิดร้าน
- `OpenShift` ระหว่างวันต้องรับ PIN ทุกครั้ง และพนักงาน Active ทั่วไปเปิดกะได้ แม้ไม่มีสิทธิ์เข้าใช้ POS; สิทธิ์นี้จำกัดเฉพาะคำสั่งเปิดกะระหว่างวันที่ร้านยังเปิดอยู่
- `CloseShift` ต้องรับ PIN ทุกครั้ง แต่พนักงาน Active ทั่วไปปิดกะได้ แม้ไม่มีสิทธิ์เข้าใช้ POS; การอนุญาตนี้จำกัดเฉพาะคำสั่งปิดกะและไม่ให้สิทธิ์ใช้งาน POS อื่นโดยปริยาย
- `CloseStore` ต้องรับ PIN ของพนักงาน Active ทุกครั้งเพื่อระบุตัวผู้ปิดร้าน และต้องบังคับกรอก `actualCashAmount` ตาม D04.11.2
- สถานะ Active และสิทธิ์ใช้ค่าล่าสุดที่ Branch Service ซิงก์ได้ตาม D11: การเปลี่ยนจากส่วนกลางมีผลทันทีเมื่อสาขาออนไลน์และได้รับข้อมูล แต่ไม่สามารถมีผลที่สาขาขณะไม่มีเส้นทางสื่อสาร
- PIN ที่ใช้ต้องระบุ employee record ได้อย่างชัดเจน ระบบต้องบันทึก employee ID, เวลา Branch, device/terminal, ประเภทคำสั่ง (`OpenStore`, `OpenShift`, `CloseShift` หรือ `CloseStore`), `storeSessionId` และ `shiftId` ใน audit log โดยห้ามเก็บ PIN แบบ plaintext
- การปิดกะระหว่างวันโดยไม่กรอกยอดเงินจริงต้องบันทึกว่าข้ามการกระทบยอด (`NotReconciled`) พร้อมผู้ปิดและเวลา เพื่อไม่ให้รายงานตีความผิดว่าเงินตรงหรือเงินเป็นศูนย์
- รายละเอียดรูปแบบ PIN การป้องกันการลองผิดซ้ำ และการจัดการ PIN ซ้ำระหว่างพนักงานให้กำหนดต่อใน D01

##### D04.11.6 — การแจ้งผลต่างเงินสดส่วนกลาง — ยืนยัน

- ทุก `CloseStore` ต้องซิงก์ closing snapshot ซึ่งมีสาขา วันธุรกิจ `storeSessionId`, `shiftId`, `expectedCashAmount`, `actualCashAmount`, `cashDifference`, ผู้ปิดร้าน เวลา และหมายเหตุเมื่อมีไปยังส่วนกลาง ไม่ว่ายอดจะตรงหรือไม่
- หาก `cashDifference != 0` ให้สร้างรายการแจ้งเพื่อทราบใน Notification Center ของ Back Office ส่วนกลาง และระยะแรกให้แจ้งทุกยอดที่ไม่ตรงโดยยังไม่ใช้ threshold
- รายการแจ้งเป็นข้อมูลเพื่อทราบเท่านั้น ไม่มีปุ่มหรือสถานะ `รับทราบ`, ไม่มีขั้นตอนอนุมัติ และการไม่เปิดดูไม่กระทบการปิดร้าน ยอดขาย หรือข้อมูลบัญชี
- ต้องเก็บรายการแจ้งย้อนหลังและควบคุมการมองเห็นตามสิทธิ์/ขอบเขตสาขาของผู้ใช้ Back Office
- หาก Branch Service ออฟไลน์ ให้เก็บเหตุการณ์ใน persistent outbox และสร้างรายการแจ้งเมื่อซิงก์ถึงส่วนกลางสำเร็จ ห้ามทำให้ `CloseStore` ที่สาขาล้มเหลวเพราะ Cloud ติดต่อไม่ได้
- `CloseShift` ระหว่างวันที่ไม่ได้กรอก `actualCashAmount` ให้รายงานเป็น `NotReconciled` แต่ไม่สร้างการแจ้งเงินขาด/เกิน
- Email, Web Push หรือช่องทางข้อความภายนอกเป็นเพียงช่องทางเสริมในอนาคต ไม่ใช่ source of truth; Notification Center และ closing snapshot ส่วนกลางเป็นข้อมูลหลัก

##### D04.11.7 — รายงานปิดกะและปิดร้าน — ยืนยัน

รายงาน `CloseShift` ต้องสร้างทุกครั้งที่ปิดกะระหว่างวันและมีข้อมูลอย่างน้อย:

- สาขา วันธุรกิจ หมายเลขกะ เวลาเปิด–ปิด และพนักงานผู้เปิด/ปิด
- ยอดขายสุทธิ จำนวนบิลที่ชำระสำเร็จ และยอด `appliedAmount` แยกตาม Payment Method ภายในกะ
- เงินสดรับจากการขาย โดยใช้ Cash `appliedAmount` สุทธิภายในกะ
- `expectedCashAmount` ณ เวลาปิด รวม `actualCashAmount`/`cashDifference` เมื่อมี; หากไม่กรอก actual ให้แสดง `ไม่ได้กระทบยอด (NotReconciled)`
- จำนวน table sessions ที่ยังเปิดและส่งต่อไปกะถัดไป โดยไม่รวมเป็นยอดขายของกะจนกว่าจะชำระสำเร็จ
- จำนวนและมูลค่ารายการที่ยกเลิก รวมจำนวนครั้งเปิดลิ้นชักด้วยตนเอง

รายงาน `CloseStore` ต้องสรุปทั้ง `storeSessionId` และมีข้อมูลอย่างน้อย:

- สาขา วันธุรกิจ เวลาเปิด–ปิดร้าน พนักงานผู้เปิด/ปิด และรายการกะทั้งหมดในรอบร้าน
- ยอดขายรวม จำนวนบิล และยอด `appliedAmount` แยกตาม Payment Method
- ส่วนลดระดับสินค้า/ท้ายบิล, Charges, Rounding, ฐาน VAT และ VAT
- `openingCashAmount` ของ `OpenStore` และ Cash `appliedAmount` สุทธิของทุกกะ
- `expectedCashAmount`, `actualCashAmount`, `cashDifference` และรายการกะที่เป็น `NotReconciled`
- จำนวนและมูลค่ารายการที่ยกเลิก จำนวนครั้งเปิดลิ้นชักด้วยตนเอง และสถานะการซิงก์ข้อมูลไปส่วนกลาง

กฎการสร้างและใช้งานรายงาน:

- เมื่อ `CloseShift`/`CloseStore` commit สำเร็จ ต้องสร้าง immutable report snapshot โดยอัตโนมัติใน transaction/persistent outbox ที่สอดคล้องกัน
- ไม่บังคับพิมพ์อัตโนมัติ ผู้ใช้ดูและสั่งพิมพ์ภายหลังได้; การพิมพ์ซ้ำต้องใช้ snapshot เดิมและห้ามคำนวณจากข้อมูล Master Data หรือสถานะปัจจุบันใหม่
- รายงานและ snapshot ห้ามแก้ไขหรือเขียนทับ การส่งออก PDF/Excel รองรับภายหลังโดยใช้ snapshot เดิมเป็นต้นทาง

##### D04.11.8 — Cash Movement ระหว่างวัน — ไม่ใช้

- ระบบไม่สร้างฟังก์ชัน `CashIn`, `CashOut`, `CashAdjustmentIncrease`, `CashAdjustmentDecrease` หรือการนำส่งเงินระหว่างที่ `storeSessionId` ยังเปิด
- การส่งมอบพนักงานระหว่างกะไม่เปลี่ยนจำนวนเงินจริงและไม่เปลี่ยน `expectedCashAmount`; `OpenShift`/`CloseShift` เป็นการเปลี่ยนผู้รับผิดชอบ ไม่ใช่การเคลื่อนย้ายเงิน
- ผลสุทธิจากการรับและทอนเงินลูกค้าใช้ Cash `PaymentLine.appliedAmount` ที่ commit และยังมีผล โดย `tenderedAmount - changeAmount` ต้องเท่ากับ applied amount ของรายการนั้น
- การเปิดลิ้นชักด้วยตนเองตาม D04.8 ไม่สร้าง Cash Movement และไม่เปลี่ยน expected; ใช้ PIN ที่ได้รับสิทธิ์และสร้าง audit log ทุกครั้ง แต่ระบบไม่อ้างว่าสามารถตรวจว่ามีการนำเงินจริงออกหรือไม่
- สูตร expected ไม่รวม cash refund เพราะการคืนเงินหลังชำระเป็นกระบวนการ Manual ของส่วนกลางนอก Workflow ของ POS ตาม D04.12 และไม่มีการจ่ายจากลิ้นชักสาขา

#### 4.3.18 D04.12 — การคืนเงินหลังชำระ — Manual ที่ส่วนกลาง / นอกขอบเขต POS

- การคืนเงินหลัง `CompletePayment` ให้ส่วนกลางดำเนินการแบบ Manual ทั้งหมด ไม่ออกแบบ Refund workflow, `FullRefund`, `ItemRefund` หรือ `FreeAmountRefund` ใน POS/Branch Service รอบนี้
- PC POS และ Mobile ไม่มีสิทธิ์สร้าง อนุมัติ ยืนยัน หรือจ่าย Refund และห้ามจ่ายเงินคืนจากลิ้นชักสาขาตาม D04.11.8
- บิล ใบเสร็จ Payment และ closing snapshots เดิมใน POS ยังคง immutable และระบบสาขาไม่แก้ยอดย้อนหลังจากกระบวนการ Manual ของส่วนกลาง
- POS/Branch Service ไม่ปรับเงินสด สต็อก VAT รายงานปิดกะ/ปิดร้าน หรือ Google Sheets export ให้อัตโนมัติจาก Refund Manual; ส่วนกลางต้องจัดการผลทางบัญชี เอกสาร ภาษี และการจ่ายเงินจริงด้วยกระบวนการของส่วนกลางเอง
- ไม่กำหนดขอบเขต Full/Partial/Free Amount, เพดานยอด หรือกฎคืนซ้ำใน D04 รอบนี้ และไม่สร้างผลอัตโนมัติต่อข้อมูลสาขา

##### D04.12.1 — ข้อมูลอ้างอิงและ Log ของ Refund — ยืนยัน

- ทุก Refund ที่ส่วนกลางดำเนินการต้องอ้างอิง receipt เดิม และ/หรือ order line เดิมที่เกี่ยวข้อง
- Log ของส่วนกลางต้องเก็บช่องทาง Refund, จำนวนเงินที่ Refund และเหตุผลที่ Refund
- D04.12.1 ไม่กำหนดประเภท `FullRefund`, `ItemRefund` หรือ `FreeAmountRefund` และไม่เพิ่ม workflow อื่นใน POS/Branch Service
- ระบบส่วนกลางต้องไม่ปฏิเสธ Refund เพียงเพราะเป็นการ Refund ซ้ำหรือยอด Refund สะสมเกินยอดเดิม การดำเนินการเป็นดุลยพินิจของผู้มีสิทธิ์ส่วนกลาง
- Refund ทุกครั้ง รวมกรณีซ้ำหรือเกินยอดเดิม ต้องสร้าง Refund log/event รายการใหม่ที่ไม่ซ้ำ ห้ามแก้ไขหรือเขียนทับรายการ Refund ก่อนหน้า และต้องเก็บผู้ดำเนินการกับเวลาทำรายการใน audit log ตาม D04.12

สิทธิ์ส่วนลด/เปลี่ยนราคา/ยกเลิกทั้งบิล/ดูประวัติ/Reprint/Bill Preview/รับชำระ/Reverse Payment กำหนดใน D01.1–D01.7 และ Refund หลังชำระไม่อยู่ในขอบเขต POS ตาม D04.12

### 4.4 ค่าใช้จ่าย

**ยืนยัน:** แสดง List รายการที่บันทึกแล้ว และเพิ่มรายการใหม่ได้

ยังต้องกำหนดช่องกรอก หมวดหมู่ หลักฐานแนบ สิทธิ์แก้ไข/ลบ และพฤติกรรมออฟไลน์

### 4.5 สต็อกสาขา

**ยืนยัน:**

- รายการสินค้า/วัตถุดิบและยอดคงเหลือของสาขา
- รายงานสต็อก
- ปรับเพิ่มและลดสต็อก
- Logs การเคลื่อนไหว/ปรับสต็อก
- สูตรตัดสต็อกผูกกับเมนู
- ตัดสต็อกอัตโนมัติเมื่อขาย

#### D07.1 — เวลาตัดสต็อก — ยืนยัน

- ตัดสต็อกเมื่อ Branch commit `SubmitOrder` สำเร็จ รายการ Draft ที่ยังไม่ Submit ไม่มีผลต่อสต็อก
- การ commit `SubmitOrder`, Order Lines, Recipe Snapshot และ Stock Movements ต้องอยู่ใน Database Transaction เดียวกัน หาก transaction ล้มเหลวหรือ rollback ต้องไม่มี Stock Movement เกิดขึ้น
- ใช้ `orderBatchId + orderLineId + stockItemId` เป็นส่วนหนึ่งของ idempotency key เพื่อป้องกันการตัดซ้ำจาก retry
- การเพิ่มออร์เดอร์รอบใหม่ใน `tableSessionId` เดิมตัดเฉพาะรายการและจำนวนที่อยู่ใน batch ใหม่
- การลดหรือยกเลิกจำนวนที่เคย Submit แล้วต้องใช้ D06 และ Compensating Stock Movement ห้ามแก้หรือลบ Stock Movement ต้นฉบับ
- `ChangeTable`, `TransferItems` และการแยกบิลไม่สร้าง Stock Movement ใหม่ เพราะไม่ได้เพิ่มจำนวนที่ผลิต
- ส่วนลด Price Override และการชำระเงินไม่มีผลต่อจำนวนสต็อก; `CompletePayment` ต้องไม่ตัดสต็อกซ้ำ
- Recipe และจำนวนวัตถุดิบต้อง snapshot ตอน `SubmitOrder` การเปลี่ยน Master Data ภายหลังไม่มีผลย้อนหลัง
- ทุก Stock Movement ต้องอ้างอิงอย่างน้อย `branchId`, `businessDate`, `tableSessionId`, `orderBatchId`, `orderLineId`, Recipe Snapshot และ source event

#### D07.2 — รูปแบบ Recipe ของ Item/SubItem/Topping — เลื่อนการตัดสินใจ

- ยังไม่ยืนยันรูปแบบการ resolve Recipe ระหว่าง Item, SubItem และ Topping รวมถึงแนวคิด `InheritItemRecipe`/`ReplaceItemRecipe`
- ห้ามนำข้อเสนอเรื่องการสืบทอดหรือแทน Recipe ไปใช้เป็นข้อกำหนด implementation จนกว่าจะทบทวนรูปแบบข้อมูลเมนูและ Recipe ใหม่

#### D07.3 — หน่วยนับและการแปลงหน่วย — เลื่อนการตัดสินใจ

- ยังไม่ยืนยัน Base Unit, หน่วยซื้อ/หน่วยนับ, Conversion factor, precision หรือ rounding policy เนื่องจากข้อมูลดิบของสินค้าและวัตถุดิบยังไม่ครบ
- ห้ามนำข้อเสนอเรื่องการ Normalize หน่วยหรือโครงสร้าง Conversion ไปใช้เป็นข้อกำหนด implementation จนกว่าจะรวบรวมและวิเคราะห์ข้อมูลหน่วยจริง

#### D07.4 — ยอดสต็อกติดลบ — ยืนยัน

- อนุญาตให้ยอดคงเหลือของ Stock Item ติดลบได้ และยอดไม่เพียงพอต้องไม่บล็อก `SubmitOrder` หรือการขาย
- ทุก Stock Movement ต้องคำนวณและเก็บยอดหลังรายการตามจริง ห้าม clamp ยอดติดลบกลับเป็น 0 หรือแก้ไข movement ต้นทางอัตโนมัติ
- หน้าสต็อกและรายงานต้องแสดงยอดติดลบอย่างชัดเจนเพื่อให้ส่วนกลางตรวจสอบและปรับยอดภายหลัง
- การปรับยอดภายหลังต้องสร้าง Stock Adjustment/Movement ใหม่พร้อม audit ห้ามเขียนทับประวัติที่ทำให้ยอดติดลบ

#### D07.5 — สิทธิ์และ Workflow การปรับสต็อก — ยืนยัน

- ปรับเพิ่มหรือลดสต็อกได้เฉพาะผู้ใช้ Back Office ส่วนกลางที่มีสิทธิ์ `AdjustStock`
- PC POS ที่สาขาดูยอดและ Stock Movement ของสาขาตนเองได้ แต่ปรับยอดไม่ได้; Mobile ไม่มีสิทธิ์ดูหรือปรับสต็อก
- คำว่า Read-only ใช้กับผู้ใช้และหน้าจอ PC POS ที่สาขาเท่านั้น ไม่ได้ใช้กับ Branch Service; Branch Service ไม่ใช่ผู้ใช้และยังต้องสร้าง/commit Stock Movement อัตโนมัติจาก `SubmitOrder`, D06.1 และคำสั่ง Adjustment ที่ถูกต้องจากส่วนกลาง
- งานปรับสต็อกไม่ใช้ PIN พนักงานสต็อก แต่ใช้ identity และสิทธิ์ของบัญชีผู้ใช้ส่วนกลาง
- ส่วนกลางเป็นผู้สร้างคำสั่งปรับสต็อก แต่ Branch Service เป็นผู้ตรวจสอบและ commit Stock Movement จริงลง Branch Database
- เมื่อ Branch ติดต่อไม่ได้ คำสั่งส่วนกลางยังไม่มีผลต่อยอดสต็อกจริงและต้องแสดงสถานะรอดำเนินการ ห้ามส่วนกลางแสดงว่า commit สำเร็จก่อน Branch ยืนยันผล

รูปแบบคำสั่ง:

- `AdjustStockByDelta` ใช้เพิ่มหรือลดจากยอดล่าสุดที่ Branch มีอยู่ เช่น `+10` หรือ `-3`; การที่ยอดเปลี่ยนหลังส่วนกลางเปิดหน้าจอไม่ทำให้เกิด Conflict โดยตัวมันเอง เพราะ Branch นำ delta ไปต่อท้าย movement ล่าสุด
- `StockCountCorrection` ใช้บันทึกยอดที่นับจริง และต้องส่ง `countedQuantity`, `observedBalance`, `observedBalanceVersion` และ `countedAt` เพื่อให้ Branch ตรวจว่าข้อมูลอ้างอิงยังเป็นปัจจุบัน

ข้อมูลบังคับของคำสั่ง:

- ต้องมี `stockAdjustmentId`, `branchId`, `stockItemId`, `adjustmentType`, จำนวน, หน่วย, `reasonCode`, `requestedByCentralUserId` และ `requestedAt`
- `AdjustStockByDelta` ต้องมี `deltaQuantity`; `StockCountCorrection` ต้องมีข้อมูลยอดนับและ Balance Version ตามข้างต้น
- เหตุผลเป็นข้อมูลบังคับ หมายเหตุและเลขเอกสารอ้างอิงเป็น Optional; หากเลือก `reasonCode = OTHER` ต้องกรอกหมายเหตุ
- ความหมายของหน่วย Conversion และ Precision ขั้นสุดท้ายยังรอ D07.3 แต่คำสั่งต้องเก็บหน่วยที่ผู้ใช้ระบุไว้เพื่อ Audit

Conflict:

- `AdjustStockByDelta` ไม่ Conflict เพียงเพราะ Balance Version เปลี่ยน แต่ Branch ยังปฏิเสธได้หาก Stock Item ไม่มี/ปิดใช้ หน่วยใช้ไม่ได้ หรือ Payload ไม่ถูกต้อง
- `StockCountCorrection` ต้องเปรียบเทียบ `observedBalanceVersion` กับ Version ล่าสุด หากไม่ตรงให้สถานะ `Conflict` และห้ามสร้าง Stock Movement
- ส่วนกลางต้องแสดงยอดที่อ้างอิง ยอดที่นับ ยอดล่าสุด และ Movement ที่เกิดหลังเวลานับ แล้วให้ผู้ใช้ตรวจสอบและสร้างคำสั่งใหม่; ไม่รองรับการบังคับเขียนทับโดยข้าม Conflict

การแก้ Adjustment ที่ผิด:

- ไม่ใช้ `ReverseStockAdjustment` และไม่มีคำสั่ง Reverse เฉพาะ
- Stock Movement ที่ Applied แล้วคง immutable หากกรอกผิดต้องสร้าง `AdjustStockByDelta` หรือ `StockCountCorrection` รายการใหม่เพื่อแก้ผล โดยต้องระบุเหตุผลและบันทึก Audit Log
- ห้ามแก้ ลบ หรือเขียนทับ Adjustment/Movement เดิม รายการแก้ไขต้องเก็บผู้ดำเนินการ เวลา เหตุผล และข้อมูลก่อน/หลัง

Idempotency และสถานะ:

- ทุกคำสั่งต้องใช้ `stockAdjustmentId` เดิมตลอดการ Retry และ Branch ต้องบังคับ Unique Constraint ด้วย ID นี้
- หาก Branch เคยประมวลผล ID เดิมแล้ว ให้คืนผลเดิมโดยไม่สร้าง Movement ซ้ำ; หาก ID เดิมแต่ Payload ต่างกันให้ปฏิเสธเป็น `IdempotencyConflict`
- ใช้สถานะ `Pending` → `Dispatched` → `Applied`/`Rejected`/`Conflict`; หากส่วนกลางไม่ทราบผลหลังส่งให้ใช้ `Unknown` และ Status Query ด้วย `stockAdjustmentId` เดิม ห้ามสร้าง ID ใหม่เพียงเพราะ Timeout

**แยกบทบาทตาม D07.5:** Back Office ส่วนกลางที่มี `AdjustStock` เป็นผู้สั่งปรับ, PC POS สาขาดูได้อย่างเดียว และ Mobile ไม่มีสิทธิ์ดูหรือปรับ; ตำแหน่งและรายละเอียดหน้าจอเป็นเรื่องออกแบบ UI ภายหลัง

Stock Adjustment Log ต้องเก็บยอดก่อนปรับ จำนวนที่เพิ่ม/ลดหรือยอดนับจริง ยอดหลังปรับ เหตุผล ผู้ทำรายการ เวลา สถานะคำสั่ง และ Audit ตาม D07.5

รอตัดสินใจ: D07.2 รูปแบบ Recipe และ D07.3 หน่วยนับ/การแปลงหน่วย/Precision; เวลาตัด การคืนเมื่อยกเลิก ยอดติดลบ และ Workflow ปรับสต็อกใช้ D07.1/D06.1/D07.4/D07.5

### 4.6 รายงาน 11 รายการ

ผู้ใช้ยืนยันใช้ทั้งหมดตามภาพ:

| ลำดับ | รายงาน |
|---|---|
| 1 | สรุปภาพรวมการขาย แยกประเภทชำระเงิน |
| 2 | ใบเสร็จรายวัน จำนวนลูกค้าและยอดรวม |
| 3 | สินค้าขาย แยกรายการ/หมวดหมู่ |
| 4 | ยอดขายสินค้าแยกตามวัน |
| 5 | สินค้าที่ยกเลิกและเหตุผล |
| 6 | การเปลี่ยนราคาที่หน้า POS |
| 7 | สรุปยอดขายเมนูเปิด/กรอกอิสระ |
| 8 | สต็อก: การเคลื่อนไหวและคงเหลือ |
| 9 | ค่าใช้จ่ายตามหมวดหมู่ |
| 10 | ประวัติการใช้งาน ใครทำอะไรและเมื่อใด |
| 11 | การสั่งตามช่วงเวลา: Heatmap ชั่วโมง × วัน ของการเพิ่ม/ยกเลิก/ชำระเงิน |

สูตรคำนวณ ตัวกรอง วันธุรกิจ ขอบเขตสาขา สิทธิ์เข้าถึง และรูปแบบส่งออกยังต้องกำหนด การมีรายงานเมนูเปิดไม่ได้ยืนยันวิธีสร้างเมนูเปิดที่ POS โดยละเอียด

### 4.7 จัดการใบเสร็จ

อ้างอิงหน้ารายการ ค้นหาวันที่ แยกบิลที่ออก/ยกเลิก แสดงเลขบิล เวลา ประเภทชำระ กะ แคชเชียร์ จำนวนลูกค้า ยอดรวม หมายเหตุ และเปิดรายละเอียด

กฎการยกเลิก คืนสต็อก และ Refund ใช้ D06.1–D06.3 และ D04.12–D04.12.1; รายละเอียดหน้าจัดการใบเสร็จและเอกสารบัญชียังต้องกำหนด

## 5. Master Data ส่วนกลาง

| ID | ข้อมูล | ความหมาย |
|---|---|---|
| MD-01 | Item | เมนูหลัก |
| MD-02 | SubItem | เมนูย่อย/ตัวเลือกของเมนูหลัก |
| MD-03 | Price | ราคาขายของ Item/SubItem |
| MD-04 | Topping | ส่วนเพิ่มเติมจาก Item/SubItem เช่น ไข่ดาว ไข่เจียว มีราคาบวกเพิ่ม |
| MD-05 | หมายเหตุ | ข้อความที่ลูกค้าใช้ประจำ เช่น ไม่หวาน ไม่เผ็ด เผ็ดน้อย ไม่มีราคา |
| MD-06 | สถานีพิมพ์ | ปลายทางของเมนู อ่านเข้าใจง่าย รองรับหลายสถานี |

**ยืนยัน:** ใช้ข้อมูลร่วมกันหลายสาขา แยก Topping ที่มีราคาออกจากหมายเหตุที่ไม่มีราคาอย่างชัดเจน

### D08.1 — Source of Truth และการ Publish Master Data — ยืนยัน

- Master Data หลักมี Source of Truth ที่ระบบส่วนกลาง สร้าง แก้ ปิดใช้งาน และ Publish ได้เฉพาะผู้ใช้ Back Office ส่วนกลางที่มีสิทธิ์
- PC POS และ Mobile อ่าน Master Data หลักอย่างเดียว ข้อยกเว้นคือ Branch Operational Override เช่น `SoldOut` ตาม D08.2 ซึ่งไม่ถือเป็นการแก้ Master Data
- ข้อมูลเฉพาะสาขาที่ส่วนกลางกำหนดสามารถรวมใน Package เป้าหมายของสาขานั้นได้ แต่ Branch ห้ามแก้ Canonical Master Data และส่งกลับไปทับส่วนกลาง
- Branch Service รับ Master Data ทางเดียวจาก Central Service/API และเก็บ Local Cache เพื่อใช้งานขณะอินเทอร์เน็ตหรือ VPN ขาด; ห้ามเปิดไฟล์ฐานข้อมูลบน NAS ผ่าน VPN โดยตรง
- Package ต้องมี `masterDataVersion`, กลุ่มสาขาเป้าหมาย, เวลา Publish, Schema Version และ Checksum
- Branch ต้องดาวน์โหลด Package ให้ครบ ตรวจ Schema/Checksum แล้ว Activate ทั้ง Package แบบ Atomic หากตรวจไม่ผ่านให้ใช้ Active Version เดิมต่อและห้าม Activate บางส่วน
- Branch ต้องเก็บ Active Version และ Previous Version อย่างน้อยหนึ่งชุดสำหรับ Rollback
- Master Data Version ใหม่ต้องไม่ลบหรือเขียนทับ Branch Operational Override เช่น `SoldOut`
- เมื่อ Branch Offline ส่วนกลางต้องแสดงว่าสาขายังไม่ได้รับหรือ Activate Version ใหม่ ห้ามแสดงว่าใช้งานแล้วก่อน Branch ยืนยันผล
- ทุกการ Publish ต้องเก็บผู้ดำเนินการ เวลา Version สาขาเป้าหมาย และรายการเปลี่ยนแปลงใน Audit Log
- NAS ส่วนกลางใช้เก็บฐานข้อมูลหรือ Backup ได้ แต่การกระจาย Master Data ให้สาขาต้องผ่าน Central Service/API

### D08.2 — Branch Sold Out Override — ยืนยัน

- `SoldOut` เป็น Branch Operational Override ที่สาขากำหนดเอง แยกจาก Master Data และไม่คำนวณอัตโนมัติจากยอดสต็อกหรือวัตถุดิบ
- วัตถุดิบเป็น 0 หรือติดลบไม่ทำให้สินค้า `SoldOut` อัตโนมัติ สินค้ายังขายได้จนกว่าสาขาจะตั้งสถานะเอง
- สถานะต้องผูกกับ `branchId + sellableItemId` เพื่อให้แต่ละสาขากำหนด Sold Out ต่างกันได้ และการ Publish Master Data ต้องไม่ลบ Branch Override โดยไม่ตั้งใจ
- ผู้ใช้ PC POS ที่สาขาสามารถตั้งและยกเลิก Sold Out ด้วย identity ของพนักงานที่ Login อยู่ โดยไม่ต้องกรอก PIN เพิ่ม
- Mobile แสดงสถานะ Sold Out และสามารถตั้ง/ยกเลิกได้เฉพาะพนักงาน Active ที่ส่วนกลางกำหนดสิทธิ์ `SetSoldOut`; Mobile ที่ไม่มีสิทธิ์เป็น Read-only
- สิทธิ์ Mobile ใช้ค่าล่าสุดที่ Branch Service ซิงก์และ cache ได้ตาม D11 เพื่อรองรับการทำงานใน LAN ขณะอินเทอร์เน็ตภายนอกขาด
- Audit ต้องเก็บผู้เปลี่ยน เวลา Branch, device, สินค้า และสถานะก่อน/หลังทุกครั้ง

### D08.3 — ราคาและเมนูเฉพาะสาขา — ยืนยัน

- Item/SubItem/Topping ใช้รหัสกลางร่วมกัน ไม่สร้างรายการซ้ำเพียงเพราะคนละสาขา; ส่วนกลางกำหนดเป้าหมายเป็นทุกสาขา กลุ่มสาขา หรือสาขาที่เลือก
- รองรับสินค้าเฉพาะสาขา แต่ยังสร้างและดูแลจาก Back Office ส่วนกลาง สาขาห้ามสร้างหรือแก้ Canonical Menu
- ราคาใช้ `BranchPriceOverride` ก่อน และใช้ `DefaultPrice` เมื่อไม่มี Override; Override ต้องผูกกับ `branchId + sellableItemId`
- ราคา 0 บาทอนุญาตได้แต่ห้ามติดลบ หากไม่มีทั้ง Default Price และ Branch Price สินค้านั้นขายไม่ได้
- ราคาและ Override ต้องมี Version/Effective Date และ Branch Package ต้องมีราคาที่ resolve แล้วสำหรับสาขานั้น
- Order Line ต้อง snapshot ราคาที่ใช้เพื่อไม่ให้การเปลี่ยนราคาในอนาคตแก้ประวัติย้อนหลัง
- แยก `CentralActive`, `BranchCatalogEnabled` และ `SoldOut`: `CentralActive = false` ปิดทุกสาขา; ถ้า Active แต่ `BranchCatalogEnabled = false` ให้ปิดเฉพาะสาขานั้น; หากทั้งสองอนุญาตจึงตรวจ `SoldOut` ของสาขาตาม D08.2
- Branch Package ต้องมีอย่างน้อย `sellableItemId`, ชื่อแสดงผล, `resolvedPrice`, `centralActive`, `branchCatalogEnabled` และ `masterDataVersion`; `SoldOut` เก็บแยกและห้าม Package เขียนทับ
- ราคาและ Branch Catalog แก้ได้เฉพาะ Back Office ส่วนกลาง PC POS และ Mobile อ่านอย่างเดียว
- Audit ต้องเก็บค่าเดิม ค่าใหม่ สาขาเป้าหมาย ผู้แก้ เวลา และ Effective Date
- การเปลี่ยนราคา/สิทธิ์ขายพร้อมใช้งานเมื่อ Publish และ Branch Activate Package สำเร็จ; ราคาและกฎคำนวณใหม่ใช้กับ table session ที่เปิดใหม่ตาม D09 ส่วนสิทธิ์ขายปัจจุบันต้องตรวจทุก `SubmitOrder`

### D08.4 — กฎการเลือก SubItem และ Topping — ยืนยัน

- ใช้ `OptionGroup` สำหรับ SubItem และ Topping โดยมีอย่างน้อย `optionGroupId`, ชื่อ, `optionType`, `minSelections`, `maxSelections`, `allowRepeatedOption` และ `maxQuantityPerOption`
- `minSelections = 0` หมายถึง Optional และ `minSelections > 0` หมายถึง Required โดยไม่ต้องมี Required Boolean แยก
- จำนวน Selection ให้นับตามจำนวนจริง ไม่ใช่จำนวนชนิด เช่น ไข่ดาว ×2 และชีส ×1 นับเป็น 3 Selections
- `allowRepeatedOption = false` จำกัดปริมาณต่อ Option ไม่เกิน 1; หากอนุญาตให้ซ้ำต้องไม่เกิน `maxQuantityPerOption` และผลรวมทั้ง Group ต้องไม่เกิน `maxSelections`
- PC POS และ Mobile ต้องตรวจตั้งแต่หน้าจอเลือก และ Branch Service ต้องตรวจซ้ำตอน `SubmitOrder` โดย Branch เป็นผู้ตัดสินขั้นสุดท้าย
- หากเลือกต่ำกว่าขั้นต่ำ เกินเพดาน หรือ Option ไม่สามารถขายได้ ให้ปฏิเสธทั้ง Submit batch และเก็บ Draft บน Mobile ไว้ให้แก้
- Option ที่ `CentralActive = false`, `BranchCatalogEnabled = false` หรือ `SoldOut = true` ห้าม Submit; หากสถานะเปลี่ยนหลังเพิ่มลง Draft ต้องปฏิเสธพร้อมระบุรายการที่ต้องแก้
- กำหนด Default Option ได้ แต่ Default ต้องยัง Active และขายได้
- ราคา Item/SubItem/Topping ต้อง Snapshot ตอน Branch รับ Order Line และหมายเหตุข้อความไม่ถูกนับรวมกับ Option Selection
- ตอน Publish ต้องตรวจว่า `minSelections >= 0`, `maxSelections >= minSelections`, `maxQuantityPerOption > 0` และ Required Group มีตัวเลือกที่ขายได้เพียงพอต่อขั้นต่ำ; ห้าม Publish Group ที่ไม่ผ่าน Validation

แนวคิดเพิ่มเติมจากภาพสำหรับออกแบบ: กลุ่มเมนู การเรียงลำดับ รหัสสินค้า ส่วนลดเริ่มต้น การส่งออก CSV และการจัดกลุ่ม Topping ต้องยืนยันรายละเอียด ไม่ถือข้อจำกัดจำนวน 50/100 หรือชื่อข้อมูลในภาพเป็นข้อจำกัดระบบใหม่

กฎหลักของ Master Data, Sold Out, ราคา/เมนูรายสาขา และ Option Selection ใช้ D08.1–D08.4; รายละเอียด Recipe/หน่วยสต็อกยังเลื่อนตาม D07.2–D07.3

### D09 — ผลของ Master Data ต่อ Table Session และ Notify ล่าช้า — ยืนยัน

- ตอน `OpenTable` Branch ต้องบันทึก `sessionMasterDataVersion` จาก Active Package ล่าสุด และตรึงชื่อสินค้า ราคา โครงสร้าง Topping/Option ภาษี ส่วนลด และกฎคำนวณของ table session นั้นจนเป็น `Completed`, `Cancelled` หรือ `Closed`
- `tableSessionId` ที่เปิดใหม่ใช้ Active Master Data Version ล่าสุดของ Branch; คำว่า “รายการใหม่ใช้ Version ล่าสุด” หมายถึงรายการใน table session ที่เปิดใหม่ ไม่ใช่ Order Batch ใหม่ที่เพิ่มเข้า session เดิม
- Order Batch ใหม่ที่เพิ่มเข้า `tableSessionId` เดิมยังใช้ราคาและกฎคำนวณของ `sessionMasterDataVersion` เดิม แม้ Branch จะ Activate Package ใหม่แล้ว; Order Line ที่ Branch รับต้องเก็บ Snapshot และห้ามเปลี่ยนย้อนหลัง
- Branch ต้องตรวจทุก `SubmitOrder` โดยไม่ขึ้นกับอายุของ session: ตรวจว่า session ยังเป็น `Ordering`, `requestId` ไม่ซ้ำ, Option ผ่าน `minSelections`/`maxSelections`, จำนวนและ payload ถูกต้อง และ Item/Option ยังได้รับอนุญาตให้ขาย
- `CentralActive`, `BranchCatalogEnabled` และ Branch `SoldOut` เป็นสถานะการขายปัจจุบันที่ต้องตรวจทุก `SubmitOrder`; การตรึงราคาไม่ใช่การอนุญาตให้ขายสินค้าที่ถูกปิดหรือ Sold Out
- หาก Draft อยู่ใน session เดิม Branch ไม่ต้องเปลี่ยนราคาให้ตรง Package ล่าสุด แต่ยังต้อง Validation ตามข้อข้างต้น; หากไม่ผ่านให้ปฏิเสธทั้ง Batch และเก็บ Draft ไว้พร้อมระบุรายการที่ต้องแก้
- Draft ที่อ้าง `tableSessionId` ซึ่งปิดแล้วหรือถูกแทนด้วย session ใหม่ต้องถูกปฏิเสธ แม้เลขโต๊ะเดิมจะถูกเปิดใช้งานอีกครั้ง
- `ChangeTable` คง `tableSessionId` จึงคง `sessionMasterDataVersion`; `TransferItems` ต้องคง Snapshot ของ Order Line ที่ย้าย ห้ามคำนวณราคาใหม่ตาม session ปลายทาง ส่วน Order Batch ใหม่ใน session ปลายทางใช้ Version ที่ตรึงกับ session ปลายทาง
- Price Override ตาม D01.2 ยังทำได้ก่อน `PaymentPending` และมีผลเฉพาะ Order Line โดยไม่เปลี่ยน `sessionMasterDataVersion`
- Recipe และ Stock Movement ยังคง Snapshot ตอน `SubmitOrder` ตาม D07; D09 ไม่เปลี่ยนจุด Snapshot Recipe จนกว่าจะตัดสินใจ D07.2–D07.3
- Notify เป็นเพียงสัญญาณว่ามี Master Data รุ่นใหม่ ไม่ใช่คำสั่งแก้ฐานข้อมูลโดยตรง; Branch ต้องเปรียบเทียบ Version, ดาวน์โหลด ตรวจ Schema/Checksum และ Activate Package แบบ Atomic ตาม D08/D12
- Notify ที่มีอายุเกิน 72 ชั่วโมง โดยคำนวณจาก `issuedAt` ของ Server ต้องไม่ Apply Payload เก่าโดยตรง แต่ต้องทำ Full Version Check และดึง Package ล่าสุด; Notify ซ้ำหรือลำดับย้อนต้องไม่ทำให้ Branch rollback ไป Version เก่า
- หาก Branch ออฟไลน์เกิน 72 ชั่วโมง ให้ขายต่อด้วย Active Package ที่มีอยู่และแสดง Version/เวลาที่ Sync สำเร็จล่าสุด; เมื่อเชื่อมต่อกลับให้ Full Version Check ทันที โดยข้อจำกัดคือการปิดขายจากส่วนกลางไม่อาจมีผลระหว่างที่สาขาไม่มีเส้นทางสื่อสาร

### D10.1 — ข้อมูลยอดเงินที่ Mobile มองเห็น — ยืนยัน

- Mobile ดูรายการในโต๊ะ จำนวน ราคา Topping หมายเหตุ ส่วนลดที่มีผล ยอดสินค้า ส่วนลดท้ายบิล Service Charge, VAT ที่รวมอยู่ และยอดสุทธิปัจจุบันได้แบบ Read-only
- ยอดทั้งหมดต้องคำนวณและส่งจาก Branch Service; Mobile ห้ามคำนวณยอดอ้างอิงเอง
- ระหว่าง `Ordering` ต้องระบุว่ายอดเป็นยอดประมาณการปัจจุบัน เพราะยังเปลี่ยนได้จากการเพิ่ม/ย้ายรายการ ส่วนลด หรือ Price Override
- Mobile ดูสถานะ `Ordering`, `PaymentPending`, `PaymentInProgress`, `PartiallyPaid` และ `Completed` ได้
- Mobile ห้ามแสดง `tenderedAmount`, เงินทอน, Payment Line และรายละเอียดช่องทางรับเงิน; ข้อมูลเหล่านี้เป็นขอบเขตของ PC POS
- Card Charge ยังไม่แสดงบน Mobile เพราะจำนวนจริงคำนวณเมื่อ PC สร้าง Payment Line ตามช่องทางและจำนวนที่ชำระ
- เมื่อข้อมูลโต๊ะเปลี่ยน Mobile ต้อง Refresh ยอดจาก Branch ด้วย `tableVersion`; ห้ามใช้ยอด Cache เก่าเป็นยอดแจ้งลูกค้า
- Mobile ไม่มีสิทธิ์แก้ราคา ใส่ส่วนลด รับเงิน หรือยืนยันการชำระ

### D10.2 — `RequestPayment` จาก Mobile — ยืนยัน

- พนักงาน Active ที่ Login บน Mobile สามารถส่ง `RequestPayment` ได้โดยไม่ต้องกรอก PIN ซ้ำและไม่ต้องมีสิทธิ์พิเศษเพิ่มเติม
- ทำได้เฉพาะ table session สถานะ `Ordering` และต้องมี Order Line ที่ยังมีผลอย่างน้อยหนึ่งรายการ
- หาก Mobile เครื่องที่กดมี Draft ที่ยังไม่ Submit ต้องให้ผู้ใช้ส่ง Draft หรือทิ้ง Draft ก่อน ห้ามส่ง `RequestPayment` ข้าม Draft ของเครื่องนั้น
- Branch ไม่สามารถทราบ Draft ที่ยังอยู่บน Mobile เครื่องอื่น; หากเครื่องอื่นส่งภายหลังที่ session เป็น `PaymentPending` ให้ปฏิเสธทั้ง Batch และเก็บ Draft ไว้
- ต้องปฏิเสธ `RequestPayment` หากมี cancellation request ที่ยังไม่ปิดตาม D06
- เมื่อรับคำสั่ง Branch ต้องตรวจยอด/สถานะล่าสุด สร้าง Bill Preview Snapshot และ persistent print job ที่แคชเชียร์ แล้วเปลี่ยน session เป็น `PaymentPending` ภายใน transaction/outbox ที่สอดคล้องกัน
- Mobile แสดงผล `Accepted`, `Rejected` หรือ `Unknown` และสถานะโต๊ะล่าสุด; กรณี `Unknown` ต้อง Status Query ด้วย `requestId` เดิม
- Mobile ไม่มีสิทธิ์เลือกช่องทางชำระ กรอกยอด รับเงิน หรือยืนยัน Payment Line และไม่สามารถใช้ `CancelPaymentRequest`; การกลับไป `Ordering` ทำได้เฉพาะ PC POS
- `RequestPayment` ต้องมี `requestId` และเป็น idempotent; Retry หรือคำสั่งซ้ำต้องไม่สร้าง Preview Snapshot/print job ซ้ำ

### D10.3 — คำขอตรวจสอบยกเลิกรายการจาก Mobile — ยืนยัน

- พนักงาน Active ที่ Login บน Mobile สร้างคำขอได้โดยไม่ต้องกรอก PIN ซ้ำและไม่ต้องมีสิทธิ์พิเศษเพิ่มเติม
- ทำได้เฉพาะ table session สถานะ `Ordering`; เลือก Order Line ได้หนึ่งหรือหลายรายการใน session เดียวกัน พร้อมจำนวนที่ต้องการยกเลิกของแต่ละรายการ
- เลือกได้เฉพาะ Order Line ที่ Branch รับจาก `SubmitOrder` แล้ว ห้ามใช้กับ Mobile Draft
- จำนวนที่ขอต้องมากกว่า 0 และไม่เกินจำนวน Active ที่ยังไม่ถูกล็อกโดย cancellation request อื่น; เหตุผล/หมายเหตุจากลูกค้าเป็น Optional
- Branch ต้องรับหรือปฏิเสธคำขอทั้งชุดแบบ Atomic และสร้าง `cancellationRequestId`
- เมื่อรับแล้วให้ล็อกเฉพาะรายการและจำนวนที่ขอจากการย้าย แยก แก้จำนวน หรือขอยกเลิกซ้ำ; รายการอื่นใน session ยังดำเนินการได้
- คำขอต้องปรากฏในคิว PC POS พร้อมการแจ้งเตือนบนหน้าจอ แต่ไม่พิมพ์อัตโนมัติจนกว่าแคชเชียร์เปิดคำขอและสั่งออกใบตรวจสอบ
- Mobile ดูสถานะคำขอได้ แต่บันทึกสถานะการผลิต ยืนยันการยกเลิกจริง ปฏิเสธ หรือถอนคำขอเองไม่ได้; หากลูกค้าเปลี่ยนใจให้แคชเชียร์ใช้ `RejectCancellation`
- คำขอไม่มีการหมดอายุหรือปิดอัตโนมัติ ต้องจบด้วย `ConfirmCancellation` หรือ `RejectCancellation` ที่ PC POS
- ต้องมี `requestId` สำหรับ idempotency; กรณี `Unknown` ให้ Status Query ด้วย ID เดิม และห้ามสร้างคำขอใหม่เพราะ Timeout
- ขณะที่ยังมีคำขอค้าง Branch ต้องปฏิเสธ `RequestPayment` ตาม D06

### D10.4 — การเปิดโต๊ะและส่งออร์เดอร์จาก Mobile — ยืนยัน

- ใช้งานได้เฉพาะอุปกรณ์ที่ Pair กับสาขาแล้วและพนักงาน Active ที่ Login สำเร็จ; พนักงาน Active ทุกคนสามารถ `OpenTable` และ `SubmitOrder` ได้โดยไม่ต้องมีสิทธิ์พิเศษหรือกรอก PIN ซ้ำ
- Mobile ดูโต๊ะและ table session ที่กำลังดำเนินการทั้งหมดของสาขาได้ ไม่จำกัดว่าใครเป็นผู้เปิด และไม่ล็อก session ไว้กับพนักงานหรืออุปกรณ์ใด
- หากโต๊ะเป็น `Available`, `OpenTable` ต้องสร้าง `tableSessionId` ใหม่และตรึง `sessionMasterDataVersion`; หากโต๊ะมี session เปิดอยู่แล้วให้คืน session เดิมและห้ามสร้างซ้ำ
- หลาย Mobile สร้าง Draft และ `SubmitOrder` เข้า session เดียวกันได้ตามกฎ Auto-merge; Mobile แก้หรือลบได้เฉพาะ Draft ที่ Branch ยังไม่รับ
- หลัง `BranchAccepted` ห้าม Mobile แก้หรือลบ Order Batch เดิม ต้องใช้ cancellation request ตาม D10.3
- `SubmitOrder` ทำได้เฉพาะ session สถานะ `Ordering` และทุก Batch ต้องบันทึก `deviceId`, `employeeId`, `tableSessionId`, `requestId` และ `orderBatchId`
- หากติดต่อ Branch Service ไม่ได้ Mobile เก็บ Draft/Pending ได้ แต่ห้ามแสดงว่าออร์เดอร์สำเร็จ ห้ามตัดสต็อก และห้ามส่งพิมพ์
- Mobile ไม่มีสิทธิ์ `ChangeTable`, `TransferItems`, `CloseTable`, `CancelBill`, ใส่ส่วนลด, Override ราคา, รับชำระ, Reprint หรือดูประวัติที่ปิดแล้ว
- `SetSoldOut` เป็นข้อยกเว้นที่ทำได้เฉพาะพนักงานที่ส่วนกลางกำหนดสิทธิ์ตาม D08.2

### D10.5 — Draft lifecycle เมื่อ Logout เปลี่ยนพนักงาน หรือปิดแอป — ยืนยัน

- Draft และ Pending Outbox ต้องเก็บแบบ Persistent บนอุปกรณ์ เช่น IndexedDB ห้ามเก็บเฉพาะในหน่วยความจำ และต้องผูก `deviceId`, `employeeId`, `tableSessionId` และ `sessionMasterDataVersion`
- การปิดแอป Process/เครื่อง Restart หรือเครือข่ายหลุดต้องไม่ทำให้ Draft/Pending สูญหาย
- `Draft` ที่ยังไม่เริ่มส่งสามารถแก้ไขหรือลบได้; `Pending` หรือคำสั่งที่เริ่มส่งแล้วห้ามลบและต้องติดตามผลด้วย `requestId` เดิมจนได้ `BranchAccepted` หรือ `Rejected`
- เมื่อ Logout หากมี Draft/Pending ต้องแสดงจำนวนรายการค้าง แต่ไม่บังคับลบและไม่ห้าม Logout
- พนักงานเดิมที่ Login กลับบนเครื่องเดิมทำ Draft ต่อได้; พนักงาน Active คนอื่นรับช่วง Draft ได้เมื่อกดยืนยันอย่างชัดเจน โดยระบบต้องเก็บผู้สร้างเดิมและผู้รับช่วง
- Pending ที่เริ่มส่งแล้วห้ามเปลี่ยนเจ้าของและต้องคง `employeeId` ของผู้ส่งเดิม
- หาก session เดิมเป็น `PaymentPending`, `Completed`, `Cancelled` หรือ `Closed` ให้ Draft เป็น `Blocked/NeedsReview` และห้าม Retry เข้า session เดิม
- การ Copy รายการจาก Draft ที่ Blocked ไปสร้าง Draft ใน session ใหม่ต้องให้ผู้ใช้ยืนยัน และตรวจราคา Option และสิทธิ์ขายใหม่ทั้งหมด
- หากพนักงานถูกเพิกถอนก่อน Branch รับคำสั่ง Branch ต้องปฏิเสธคำสั่งนั้น; Draft ที่ยังไม่เริ่มส่งสามารถให้พนักงาน Active คนอื่นรับช่วงได้
- Draft ที่ยังไม่เคยส่งเป็นข้อมูลเฉพาะอุปกรณ์และอาจสูญหายหากถอนแอป ล้างข้อมูล หรืออุปกรณ์เสีย จึงห้ามแสดงว่าเป็นออร์เดอร์ที่ Branch รับแล้ว
- ระยะเวลาเก็บและการลบ Draft เก่ากำหนดภายหลังใน Retention Policy

## 6. Back Office และค่าตั้งรายสาขา

**ยืนยัน:** เป็นระบบแยก ส่วนกลางไม่ต้องผ่าน POS เพื่อแก้ค่าตั้ง ผู้จัดการหลักเป็นส่วนกลาง

ขอบเขตที่ระบุ:

- ข้อมูลร้าน/สาขา
- โซนและจำนวนโต๊ะในแต่ละโซน ต้องปรับได้ยืดหยุ่นกว่าระบบเดิม
- ประเภทการชำระเงิน
- อุปกรณ์ Mobile ของสาขา
- พนักงานและ PIN สำหรับ POS
- การผูกสถานีพิมพ์กับเครื่องพิมพ์จริงในแต่ละสาขา
- Master Data เมนูร่วมหลายสาขา

ค่าตั้งจากภาพสำหรับพิจารณารายละเอียด: ค่าบริการ เวลาเปิด–ปิด การปัดเศษ การแสดงส่วนลด ภาษี ช่องทางรับรายงาน ตัวจับเวลา การแสดงผล และบัญชีผู้ใช้/สิทธิ์ ไม่ถือว่าผู้ใช้ได้ยืนยันทุกช่องและทุกพฤติกรรมในภาพแล้ว

PIN พนักงานสต็อกไม่อยู่ในขอบเขตเบื้องต้น การที่ระบบเดิมไม่ใช้ authorization tool สำหรับงานนี้ไม่ได้หมายความว่าระบบใหม่ควรเปิด Back Office โดยไม่มีการตรวจตัวตน การออกแบบสิทธิ์รวมยังเป็นประเด็นเปิด

### 6.1 การส่งยอดตามช่องทางไป Google Sheets — ยืนยัน

- งานส่ง Google Sheets เป็นความรับผิดชอบของบริการส่วนกลาง ไม่ใช่ Branch Service และไม่อยู่ใน transaction ของ `CloseStore`
- `CloseStore` ต้อง commit local closing/report snapshot และ persistent outbox ได้สำเร็จแม้อินเทอร์เน็ตหรือ Google ใช้งานไม่ได้ จากนั้นสาขาซิงก์ snapshot ไปส่วนกลางตาม D12
- เมื่อส่วนกลางรับ `CloseStore` snapshot แบบ durable แล้ว Central Integration Worker จึงรวมยอดตาม `branchId + businessDate + paymentMethodId` และเขียนหรืออัปเดต Google Sheets
- ยอดของแต่ละช่องทางต้องรวมจาก `PaymentLine.appliedAmount`; เงินสดห้ามใช้ `tenderedAmount` และห้ามนับ `changeAmount` เป็นยอดขาย กรณี Split Tender ต้องจัดจำนวนของแต่ละ Payment Line ให้ช่องทางของตนเท่านั้น
- รูปแบบข้อมูลใช้หนึ่งแถวต่อสาขา/วันธุรกิจ/Payment Method เพื่อรองรับ Custom Payment Method โดยมีอย่างน้อย Business Date, Branch ID/Name, Payment Method ID/Name, Amount, Payment Count และเวลาส่งออก
- หากหนึ่งสาขามี `CloseStore` มากกว่าหนึ่ง `storeSessionId` ใน business date เดียว ส่วนกลางต้อง aggregate snapshot ที่ไม่ซ้ำทั้งหมดแล้ว upsert ยอดรวม ห้าม append จนเกิดการนับซ้ำ
- Integration Worker ต้องเก็บสถานะ `Pending`, `Exported` หรือ `Failed`, รองรับ retry และ manual replay แบบ idempotent โดยอ้างอิง source snapshot IDs/export version
- Google credentials ต้องเก็บและใช้งานที่ส่วนกลางเท่านั้น ห้ามกระจายไป Branch PC; Google Sheets ไม่ใช่ source of truth ข้อมูลหลักยังเป็น closing snapshots/ฐานข้อมูลส่วนกลาง
- Google outage หรือ export failure ไม่ย้อน `CloseStore` และไม่แก้ยอดขาย ระบบต้องเก็บข้อผิดพลาดและลองส่งใหม่ได้

## 7. สถานีพิมพ์และ Direct Network PrintGateway

**ข้อกำหนดที่ยืนยัน:** ระบบใหม่ไม่ใช้ `PrinterInterface_335.exe`, Chrome extension หรือ Windows Printer Interface เดิม Branch Service ต้องส่งงานไป Thermal Printer ที่เชื่อมผ่าน LAN/RJ45 โดยตรงผ่าน `IPrintGateway`

**D13 — เครื่องพิมพ์ที่ยืนยันเบื้องต้น:**

- ใช้เครื่องพิมพ์ตระกูล Epson TM-T82 เป็นหลัก เชื่อมต่อผ่านพอร์ต RJ45 ในตัว และใช้กระดาษ 80 mm
- แต่ละสาขามีอย่างน้อย 6 เครื่อง แยกเป็นครัว 4 เครื่อง, เค้ก 1 เครื่อง และแคชเชียร์/ใบเสร็จ 1 เครื่อง
- PrintGateway ต้องมี persistent queue และ worker แยกต่อเครื่อง โดย serialize งานของเครื่องเดียวกันเพื่อไม่ให้ payload จากหลายงานปะปนกัน
- เครื่องแคชเชียร์มีลิ้นชักเงินต่อกับพอร์ต drawer ของเครื่องพิมพ์ การเปิดลิ้นชักต้องเกิดจากคำสั่งที่ได้รับอนุญาตและมี audit log; ห้ามถือว่าพอร์ตลิ้นชักเป็นพอร์ตเครือข่าย
- **ผลตรวจเครื่องจริง 12 ตุลาคม 2026:** เครื่องที่ใช้ทดสอบคือ Epson **TM-T82X, Model M352A**, Firmware `13.07 ESC/POS`, Network Firmware `01.04`, Ethernet ในตัว และกระดาษ 80 mm ตามป้ายเครื่องและ Self-test
- ผลนี้รับรองเฉพาะ Hardware/Profile ชุด `epson-tm-t82x-m352a-v1` ที่ทดสอบ ไม่ครอบคลุมเครื่องตระกูล TM-T82 รุ่นอื่นโดยอัตโนมัติ และยังเป็น **Provisional** จนกว่าจะปิดรายการทดสอบที่เหลือใน D13.2

### D13.1 — การระบุตัวตนและ IP ของเครื่องพิมพ์ — ยืนยัน

- เครื่องพิมพ์แต่ละเครื่องต้องมี `printerId` ถาวรของระบบซึ่งไม่ใช่ IP address; IP เป็นค่าปลายทางที่แก้ได้และห้ามใช้เป็น Primary Key
- ใช้ DHCP Reservation ที่ Network Gateway เป็นแนวทางหลักเพื่อให้เครื่องเดิมได้รับ IP เดิมและลดความเสี่ยง IP ซ้ำ; หากจำเป็นต้องใช้ Static IP ต้องอยู่นอก DHCP Pool และจัดทำทะเบียนเดียวกัน
- Printer Profile ต้องเก็บอย่างน้อย `printerId`, ชื่อแสดงผล, `branchId`, Production Station, IP/hostname, port, protocol/profile, รุ่น/Product Code, ความสามารถเปิดลิ้นชัก และสถานะ Active
- Branch Service/PrintGateway เป็นผู้เชื่อมต่อเครื่องพิมพ์เพียงระบบเดียว; PC Browser และ Mobile ห้ามเชื่อมเครื่องพิมพ์โดยตรง
- สำหรับ Profile `epson-tm-t82x-m352a-v1` ยืนยันแล้วว่า RAW TCP port `9100` รับการเชื่อมต่อและงาน ESC/POS ที่ใช้ทดสอบสามารถพิมพ์ ตัดกระดาษ และสั่งลิ้นชักได้; ผลนี้ห้ามนำไปอนุมานกับรุ่นอื่น และการส่งลง socket สำเร็จยังไม่ใช่หลักฐานว่าเอกสารถูกพิมพ์สำเร็จ
- การ Ping หรือเปิด TCP connection สำเร็จหมายถึงเพียงติดต่อปลายทางได้ ไม่ใช่หลักฐานว่าเอกสารถูกพิมพ์สำเร็จ และต้องใช้สถานะงานตาม D20
- เมื่อเปลี่ยนเครื่องจริงให้แก้ปลายทางและ Hardware Profile ที่ผูกกับ `printerId`/สถานีโดยไม่แก้กฎ Routing ของเมนู
- ต้องบันทึก Audit ของการเปลี่ยน IP/hostname, port, protocol/profile, Production Station และสถานะ Active
- การพิมพ์ภายในสาขาต้องทำงานผ่าน LAN ได้แม้อินเทอร์เน็ตภายนอกล่ม

### D13.2 — Printer Compatibility Profile — ยืนยันแบบ Provisional

- เครื่องเป้าหมายระยะแรกคือ **Epson TM-T82X, Model M352A**, Firmware `13.07 ESC/POS`, Network Firmware `01.04`, Ethernet ในตัว ตามป้ายเครื่องและ Self-test ที่ตรวจจากเครื่องจริง
- Self-test แสดง Resident Character `Thai 1 pass` และ `Thai 3 pass` แต่ Production Profile ยังคงใช้ Bitmap/Raster เป็นหลัก ไม่ใช้ข้อมูลดังกล่าวเป็นหลักฐานรับรอง Native Thai Code Page
- Network Test ที่ยืนยันล่าสุดใช้ Printer IP `192.168.2.3` และ PC LAN IP `192.168.2.2` แบบเชื่อมต่อสาย LAN โดยตรง; ค่านี้เป็น Test Configuration ไม่ใช่ Production Address และยังไม่ได้พิสูจน์ DHCP/Static persistence หลัง reboot บนเครือข่ายสาขาจริง
- Profile `epson-tm-t82x-m352a-v1` ผ่านผลทดสอบแบบ **Provisional** ดังนี้: RAW TCP 9100, Thai Raster ด้วย Noto Sans Thai Looped ที่ความกว้าง 576 dots/threshold 180, Paper Cut, Drawer Pulse, Drawer Open/Closed Status, Cover Open/Closed ผ่าน SNMP, Paper Out/Restored ผ่าน SNMP และการล้าง Active Paper-Out Alert หลังใส่กระดาษกลับ
- `DLE EOT 2` ไม่ตอบ byte ขณะฝาเปิดในการทดสอบ จึงห้ามใช้เป็นวิธีเดียวสำหรับตรวจ Cover Status ของ Profile นี้; วิธีหลักที่พิสูจน์แล้วคือ SNMP Printer MIB
- ผล SNMP ที่พิสูจน์กับเครื่องทดสอบ: Cover `3 = open`, `4 = closed`; Paper Out รายงาน Current Level `0`/Status `19`; เมื่อใส่กระดาษกลับรายงาน Current Level `-3`/Status `0`; Alert Code `808` พร้อมคำอธิบาย `Roll Paper End` ต้องหายเมื่อสถานะกลับปกติ
- Drawer Pulse บน drive pin 2 เปิดลิ้นชักจริง และ `GS r 2` ให้สถานะ pin 3 `HIGH = closed`, `LOW = open` กับลิ้นชักที่ทดสอบ
- ระยะ Phase 0 ให้สถานะอัตโนมัติสูงสุดเป็น `PrintDispatched`; ห้ามใช้ `Completed` จนกว่าจะพิสูจน์ Status/ACK ที่เชื่อมโยงกับ Print Attempt นั้นและยืนยันผลตาม D13/D20 ได้
- รายการที่ยังไม่ผ่านการรับรองและต้องปิดก่อน Production-certified ได้แก่ Cutter Error/Alert Mapping, Windows Production Service Path, Linux Production Service Path, ความเทียบเท่า Output ระหว่าง Windows/Linux, DHCP/Static persistence หลัง reboot, Offline/Restart และ Timeout แบบ End-to-End กับ persistent queue รวมถึง Job-specific Print Completion
- Serial Number และ MAC Address ใช้ในทะเบียนอุปกรณ์ภายในได้ แต่ต้อง Redact หรือแทนด้วยข้อมูลจำลองใน Public Repository, Test Fixture, Screenshot และเอกสารสาธารณะ
- หาก Status Protocol ไม่สามารถยืนยันว่ากระดาษของงานนั้นออกจริง ให้คง `PrintDispatched`; เมื่อขาดการเชื่อมต่อหรือไม่ทราบผลหลังเริ่มส่งให้ใช้ `Unknown` และห้าม Retry อัตโนมัติ

### D13.3 — Printer Status และผลของ Print Attempt — ยืนยัน

- ก่อนส่ง byte แรก หากตรวจพบ `CoverOpen`, `PaperOut`, `CutterError` หรือ `Offline` ให้คงงานเป็น `PrintQueued`, บันทึก `blockedReason` และยังไม่เพิ่มจำนวน Print Attempt; Worker ส่งต่อได้เมื่อเครื่องกลับเป็น `Ready`
- หากเชื่อมต่อไม่ได้ก่อนส่ง byte แรกให้เป็น `Failed` และ Retry อัตโนมัติได้ตาม Backoff Policy
- เมื่อเริ่มส่ง byte แรกให้เปลี่ยนเป็น `Dispatching`; หาก Connection หลุดหรือ Timeout หลังจากจุดนี้ให้เป็น `Unknown` และห้าม Retry อัตโนมัติ
- เมื่อส่ง Payload ครบโดยไม่มี Transport Error ให้เป็น `PrintDispatched` ซึ่งยืนยันเฉพาะผลระดับ Transport ไม่ใช่หลักฐานว่าเอกสารถูกพิมพ์หรือกระดาษออกจริง
- `DLE EOT` หรือ Automatic Status Back ใช้ตรวจ Printer Condition เช่น Cover, Paper และ Error เท่านั้น ห้ามใช้ยกระดับเป็น `Completed` เว้นแต่พิสูจน์ได้ว่า Status/ACK นั้นผูกกับ `printAttemptId` ของงานโดยเฉพาะ
- หากเกิด Printer Error ระหว่างหรือทันทีหลัง Dispatch และไม่ทราบว่างานออกบางส่วนหรือทั้งหมดหรือไม่ ให้เป็น `Unknown` และต้องตรวจเครื่องจริงก่อน Reprint
- หน้าสถานะระบบต้องแสดงอย่างน้อย `Ready`, `Offline`, `Cover Open`, `Paper Out`, `Cutter Error`, `Status Unsupported` และ `Unknown`
- หลังตรวจเครื่องจริง การตัดสินของพนักงานให้บันทึกเป็น Resolution แยกจากสถานะต้นฉบับ ได้แก่ `ConfirmedPrinted`, `ReprintRequested` หรือ `DuplicateRiskAccepted` พร้อมผู้ดำเนินการและเวลา
- ห้ามแก้หรือลบ Print Attempt เดิม; Reprint ต้องสร้าง Print Job/Attempt ใหม่และอ้างอิงงานต้นทางเพื่อรักษา Audit Trail
- Profile `epson-tm-t82x-m352a-v1` ยังไม่รองรับสถานะอัตโนมัติ `Completed` จนกว่าจะผ่านการพิสูจน์ Job-specific ACK ตามเกณฑ์ข้างต้น

### D13.4 — ภาษาไทยและ Print Rendering — ยืนยัน

- เอกสารต้นทางใช้ Unicode UTF-8 เสมอ ห้ามจัดเก็บข้อความธุรกิจเป็น Printer Code Page
- Phase 0 ใช้ Bitmap/Raster Rendering ทั้งใบเป็นค่าเริ่มต้น โดย Render ภาษาไทย ตัวเลข ราคา Topping และหมายเหตุก่อนส่งเป็นภาพ Monochrome ผ่าน ESC/POS
- ใช้ Font แบบตรึง Version เช่น `Noto Sans Thai Looped` ภายใต้ SIL Open Font License 1.1 และต้องบันทึก Version/License ไว้ใน Third-party Notices และ SBOM
- PrintGateway ต้อง Bundle Font และ Text Shaping Engine ที่กำหนด ห้ามพึ่ง Font ที่ติดตั้งอยู่ใน Windows/Linux; Input และ Profile เดียวกันต้องสร้าง Raster Output ที่เทียบเท่ากันทั้งสองระบบ
- ความกว้าง Raster, DPI และ Printable Area ต้องอ่านจาก Printer Profile ที่ผ่านการทดสอบ ห้ามอนุมานค่าจากชื่อรุ่นเพียงอย่างเดียว
- Native Text/Code Page เป็น Experimental Optimization เท่านั้น เปิด Production ได้เมื่อยืนยัน Code Page, Byte Mapping และชุดอักขระภาษาไทยครบ; ห้าม Fallback จาก Bitmap ไป Native Text โดยอัตโนมัติ
- หาก Render ไม่สำเร็จให้เป็น `Failed` ก่อนส่ง byte แรกและห้ามส่งเอกสารบางส่วน
- ชุดทดสอบภาษาไทยต้องครอบคลุมสระ/วรรณยุกต์บน–ล่าง, `ำ`, `ญ`, `ฐ`, `ป`, `ฝ`, `ฟ`, ภาษาไทยผสมอังกฤษ/ตัวเลข/สัญลักษณ์, ชื่อเมนูยาว, Topping หลายรายการ, หมายเหตุหลายบรรทัด, ตัวหนา/ขนาดต่างกัน และการจัดตำแหน่งยอดเงิน
- Thai Raster จาก Windows ทดสอบจริงแล้วว่าสระและวรรณยุกต์ถูกต้องที่ความกว้าง 576 dots และ threshold 180; ยังต้องมี Golden Raster Test และพิมพ์จาก Linux ด้วย Fixture/Font/Renderer version เดียวกันเพื่อเปรียบเทียบ Payload Hash และผลบนกระดาษก่อนรับรอง Profile
- ยอมรับ Payload และเวลา Render/Print ที่มากกว่า Native Text เพื่อแลกกับความถูกต้องและความสม่ำเสมอของภาษาไทย โดยยังต้องผ่าน Performance Baseline ที่กำหนด

แนวทางสถานีพิมพ์:

1. Master Data ระบุว่าเมนูไปสถานีใด เช่น ครัวร้อน บาร์น้ำ ตู้เค้ก
2. สาขาผูกสถานีกับ Printer Profile และเครื่องพิมพ์จริง
3. Printer Profile แปลงเอกสารมาตรฐานของระบบเป็น protocol/command ของรุ่นนั้น
4. ผู้ใช้เลือกสถานี ไม่ต้องจำ IP, port หรือคำสั่ง ESC/POS
5. Branch Service สร้าง persistent print job ก่อน dispatch และติดตามด้วย `printJobId`

Protocol ที่รองรับในสถาปัตยกรรม:

- ESC/POS ผ่าน RAW TCP/port 9100 เป็นเส้นทางหลักเมื่อรุ่นจริงรองรับ
- IPP/IPPS เป็น adapter มาตรฐานสำหรับรุ่นที่รองรับ
- LPR/LPD ใช้เป็น fallback เฉพาะรุ่นที่จำเป็น
- Vendor SDK ใช้ได้เมื่อมี Linux/Windows support ที่ยังได้รับการดูแลและซ่อนอยู่หลัง `IPrintGateway`
- การมี RJ45 ไม่ได้ยืนยันว่าเครื่องรองรับ ESC/POS, port 9100, status แบบสองทาง หรือภาษาไทย ต้องตรวจคู่มือและทดสอบรายรุ่น

Printer Profile ขั้นต่ำต้องเก็บ manufacturer/model, firmware, protocol, IP/port, paper width, DPI, printable columns, Thai encoding/render mode, cutter command, drawer pulse, status command, timeout และ retry policy

ข้อกำหนดสถานะ:

- `PrintQueued`: persistent print job ถูกบันทึกแล้ว
- `PrintDispatched`: adapter ส่ง payload ลง transport ตาม contract แล้ว แต่ไม่รับรองว่ากระดาษออก
- `Completed`: ใช้ได้เฉพาะเมื่อมีหลักฐานจาก protocol/อุปกรณ์ตามที่ D13 กำหนด
- `Failed`: มีหลักฐานชัดเจนว่าส่งไม่ได้และยังไม่เกิดผลทางกายภาพตาม contract
- `Unknown`: timeout/connection loss หลังเริ่มส่งหรือกรณีผลสุดท้ายไม่ทราบ; retry ต้องเตือนความเสี่ยงพิมพ์ซ้ำ

Compatibility test ขั้นต่ำ:

1. บันทึกยี่ห้อ รุ่น firmware MAC/IP protocol และ programming/technical manual ของเครื่องเป้าหมาย
2. พิสูจน์ ESC/POS/TCP 9100, IPP/IPPS หรือ LPR ที่ใช้จริง; ปิด protocol ที่ไม่ใช้
3. ทดสอบภาษาไทยแบบ text/code page และ bitmap, Topping, หมายเหตุ, จำนวนมากกว่าหนึ่ง, กระดาษ 80 mm และหลายสถานี
4. ทดสอบ cut, cash drawer, buzzer ถ้ามี, cover open, paper out, printer offline และ restart
5. ทดสอบ timeout ก่อนส่ง ระหว่างส่ง และหลังส่ง พร้อมกำหนดว่าแต่ละกรณีเป็น `Failed` หรือ `Unknown`
6. ทดสอบ `printJobId` เดิมซ้ำและระบุว่าป้องกัน/ตรวจพบกระดาษซ้ำได้ระดับใด
7. ทดสอบจาก Windows และ Linux กับรุ่นที่จะประกาศรองรับ
8. แยก Printer VLAN/segment และ firewall ให้รับงานจาก Branch PC เท่านั้น; เปลี่ยน default password และห้าม Mobile เชื่อม Printer โดยตรง

**สถานะ Commissioning ณ 12 ตุลาคม 2026:** ผลทดสอบและค่าที่ผ่านการตรวจเก็บใน Public Repository `qtaro66/PrintGateway` ภายใต้ `Tests/Hardware/Profiles/epson-tm-t82x-m352a-v1.test.json` และ `Tests/Hardware/Reports/epson-tm-t82x-m352a-v1-commissioning.md` ที่ merge ผ่าน PR #3; ข้อมูล Serial Number, MAC Address และหลักฐานเฉพาะสถานที่ต้องคงอยู่นอก Public Repository

## 8. API ภายนอกและ FlowAccount

- **ยืนยัน:** ต้องรองรับการเชื่อม API ภายนอก
- **ข้อเท็จจริงจากผู้ใช้:** ปัจจุบันออกใบกำกับภาษีใน FlowAccount แบบ manual
- **ความต้องการอนาคต:** พิจารณาเชื่อม POS กับ FlowAccount
- **ข้อเสนอที่ยังต้องยืนยัน:** ระยะแรกส่งเฉพาะบิลที่ขอใบกำกับภาษีและเก็บเลขอ้างอิงกลับ
- คำตอบ “รับทราบ” ยังไม่ถือว่าอนุมัติรายละเอียดส่งเฉพาะบางบิลอย่างเด็ดขาด ต้องยืนยันกับ workflow บัญชี
- การส่งทุกบิล/เฉพาะบิล ประเภทเอกสาร ผู้อนุมัติ ข้อมูลผู้ซื้อ การยกเลิก และป้องกันบันทึกยอดซ้ำ ยังเปิดอยู่
- FlowAccount ไม่จำเป็นต้องอยู่ในต้นแบบ POS รอบแรก เว้นแต่ผู้ใช้กำหนดเพิ่ม

## 9. ข้อสรุปที่แก้ไขจากแชทเก่า

| ประเด็นเก่า | ใช้ข้อสรุปใดในเอกสารนี้ |
|---|---|
| สร้าง Mobile Android ด้วย Kotlin/Compose | เป็นข้อเสนอช่วงสำรวจ; ล่าสุดยอมรับ PWA รองรับ Android/iOS |
| สร้าง Print Agent ใหม่รองรับ USB/Serial/หลาย protocol | ล่าสุดยืนยันไม่ใช้ Interface เดิมและไม่ใช้ USB เป็นเส้นทางพิมพ์หลัก; ใช้ Direct Network PrintGateway ไปยัง Thermal Printer ผ่าน LAN/RJ45 โดย protocol ต้องยืนยันรายรุ่น |
| มือถือส่งผ่าน Cloud ก่อน แล้วค่อย fallback LAN | ล่าสุดใช้บริการสาขาผ่าน LAN เป็นเส้นทางงานร้าน ซิงก์ส่วนกลางเมื่อออนไลน์ |
| ต้องแกะระบบเดิมทุกส่วนก่อนเริ่มสร้างใหม่ | ไม่ใช่ข้อกำหนดล่าสุด ใช้ระบบเดิมเป็น concept; ศึกษารายละเอียดเฉพาะจุดที่ต้องเชื่อมต่อ |
| ตั้งค่าผ่านเมนู POS | แยก Back Office ส่วนกลางตามคำขอภายหลัง |
| TOGO/Delivery | ตัดออก ช่องทางขายเฉพาะโต๊ะ |
| PIN สต็อก | ไม่ใช้ในขอบเขตเบื้องต้น |
| Tag ให้พนักงานจัดการเอง | เปลี่ยนเป็นสถานีพิมพ์ที่ผูกกับ Printer Profile; ผู้ใช้ไม่ต้องจำ tag/IP/port |
| API version 335 ในผลวิเคราะห์เครื่องอื่น กับ 2.1.3 ในเครื่องนี้ | เป็นข้อมูลระบบเดิมเพื่ออ้างอิงเท่านั้น ไม่อยู่ใน runtime path ของระบบใหม่ |
| Mode 1 อาจหมายถึงวิธีเชื่อมเครื่องพิมพ์ | เป็นพฤติกรรมระบบเดิม; ระบบใหม่ต้องนิยาม print layout/mode ใน Printer Profile และ D13 |
| log มีเฉพาะเปิด/ปิดโปรแกรม | เป็นข้อมูลระบบเดิม; ระบบใหม่ต้องมี structured log, print job status และ audit ตาม NFR |

S1/S2 ใช้เป็นเหตุผลประกอบการปรับสถาปัตยกรรม เช่นข้อจำกัดเมื่ออินเทอร์เน็ตขาด ไม่ยก Firebase, IMEI, Chrome extension หรือ Android SDK เดิมมาเป็นข้อกำหนดระบบใหม่

## 10. รายการตัดสินใจที่ยังค้าง

| ID | เรื่องที่ต้องตัดสินใจ | ต้องได้คำตอบก่อน |
|---|---|---|
| D01 | ยืนยัน D01.1–D01.7: รับชำระปกติไม่ใช้ PIN เพิ่ม; Reverse confirmed Payment Line ก่อน Completed ทำเฉพาะ PC ด้วย PIN `ReversePayment`, Reverse เต็ม line เท่านั้น, เหตุผล optional, original immutable และ audit ทุกครั้ง; หลัง Completed ใช้ Refund ส่วนกลาง | สิทธิ์ POS หลักที่ระบุครบ; ทบทวนเมื่อพบ action เพิ่ม |
| D02 | ต่อหนึ่งสาขามี Branch PC 1 เครื่อง รัน PC POS/บริการสาขา/ฐานข้อมูล และรองรับ Mobile พร้อมกันไม่เกิน 20 เครื่อง; เครื่องเดิมใช้ Windows ส่วน Mini PC ใหม่ทดลอง Linux ตาม D17; รายละเอียดรุ่นฮาร์ดแวร์ยังรอกำหนด | ตัดสินใจบางส่วน — ใช้ออกแบบ topology และ load test ได้ |
| D03 | ยืนยัน workflow เปิด/ล็อก/แยก/ปิดโต๊ะ, `ChangeTable` ไปโต๊ะว่างโดยคง session และ `TransferItems` ข้าม session; งานออร์เดอร์เดิมคงเลขโต๊ะเดิมเสมอ ทุกการย้ายพิมพ์ใบรวมที่แคชเชียร์ และ PC เลือกแจ้ง Production Station เป็นรายรายการ โดยรวมหนึ่งใบต่อสถานีและ snapshot ตัวเลือกใน transfer event | Workflow และ print routing หลักครบ; ผลทางการเงินอ้างอิง D05 |
| D04 | ยืนยัน Payment workflow และกฎกะถึง D04.12.1: Refund หลังชำระเป็น Manual ของส่วนกลางนอก POS ไม่ปรับข้อมูลสาขาอัตโนมัติและห้ามจ่ายจากลิ้นชัก; ทุก Refund อ้างอิง receipt/order line และ log ช่องทาง จำนวนเงิน เหตุผล; Refund ซ้ำหรือยอดสะสมเกินยอดเดิมอนุญาตตามดุลยพินิจส่วนกลางและต้องสร้าง log ใหม่ทุกครั้ง | Payment/กะหลักครบ; Refund integration ไม่อยู่ในขอบเขตรอบนี้ เหลือสิทธิ์ส่วนอื่น |
| D05 | ยืนยันราคา/Topping รวม VAT, VAT เริ่มต้น 7%, ส่วนลด/Price Override, D05.6 Requested/Applied Discount, D05.7 Service Charge, D05.8 Card Charge ต่อ Payment Line และ D05.9: สร้าง Draft Payment Plan, ปัดลงหนึ่งครั้งระดับบิล และจัดสรร `roundingAdjustment` ไปยัง Card Line สุดท้ายที่มี Charge หรือ Payment Line สุดท้าย | ตัดสินใจกฎการคำนวณหลักครบ; ก่อนเปิด Card Charge จริงต้องยืนยันสัญญาผู้รับบัตรและวิธีลงบัญชี |
| D06 | ยืนยัน workflow ยกเลิกรายการก่อนชำระที่สาขา: ครัวรายงาน `NotStarted`/`InProgress`/`Completed`, แคชเชียร์ตัดสินใจ Confirm/Partial/Reject และสถานะการผลิตไม่ห้ามยกเลิก; D06.1 คืนสต็อกอาหารเฉพาะ `NotStarted` และรายการ `NotApplicable` ที่ติดตามสต็อกใช้การตัดสินใจ `ReturnToStock`/`NoStockReturn` ของแคชเชียร์; D06.2–D06.3 ใช้ maker-checker และ immutable event | ตัดสินใจ workflow การยกเลิกและผลสต็อกหลักครบ; Refund integration และเอกสารบัญชีอยู่นอกขอบเขตรอบนี้ |
| D07 | ยืนยัน D07.1 ตัดสต็อกตอน `SubmitOrder`, D07.4 อนุญาตยอดติดลบ และ D07.5: ผู้ใช้ PC POS สาขา Read-only แต่ Branch Service ยัง commit movement อัตโนมัติ; ส่วนกลางสั่ง Adjustment, รองรับ Delta/Count Correction, Conflict, idempotency และแก้รายการผิดด้วย Adjustment ใหม่; D07.2–D07.3 เลื่อน | เหลือรูปแบบ Recipe และรายละเอียดหน่วย/Conversion/Precisionเมื่อมีข้อมูลดิบครบ |
| D08 | ยืนยัน D08.1 Source of Truth/atomic package, D08.2 Sold Out เป็น Branch Override, D08.3 เมนูกลาง/ราคาและ Catalog รายสาขา และ D08.4 OptionGroup: min/max, Required/Optional, repeated quantity, Branch validation และ reject ทั้ง batch พร้อมเก็บ Draft | ตัดสินใจกฎ Master Data หลักครบ; Recipe และหน่วยสต็อกเลื่อนตาม D07.2–D07.3 |
| D09 | ยืนยันตรึง `sessionMasterDataVersion` ตอน `OpenTable`: ราคา/กฎคำนวณเดิมใช้จนปิด session แต่ทุก `SubmitOrder` ยังตรวจสถานะ, Option และสิทธิ์ขายปัจจุบัน; `ChangeTable` คง Version, `TransferItems` คง Line Snapshot และ Notify เกิน 72 ชั่วโมงใช้ Full Version Check | ตัดสินใจกฎผลของ Master Data ต่อบิลเปิดและ Notify ล่าช้าครบ |
| D10 | ยืนยัน D10.1–D10.5: Mobile ดูยอดจาก Branch, ส่ง `RequestPayment`, สร้าง cancellation request, เปิดโต๊ะ/ส่งออร์เดอร์แบบไม่ล็อก session และเก็บ Draft/Pending แบบ Persistent; Draft รับช่วงได้แต่ Pending คงผู้ส่งเดิม ส่วน Action ที่กระทบบิลหลัง Submit จำกัดที่ PC ยกเว้น `SetSoldOut` ตามสิทธิ์ | ตัดสินใจกฎ Mobile หลักครบ; ระยะเวลาเก็บ Draft รอกำหนดใน Retention Policy |
| D11 | Phase 0 ยืนยัน Private CA, URL เป็น DHCP-reserved IP พร้อม IP SAN, QR pairing, offline employee cache, revocable device credential, device revocation และ CA lifecycle แล้ว; เหลือจัดทำคู่มือติดตั้ง/ถอน Root CA และพิสูจน์บน Android/iOS จริง | ตัดสินใจครบสำหรับ Phase 0 — พร้อมทำ HTTPS/pairing/offline-login spike |
| D12 | ยืนยัน source of truth, พฤติกรรมเมื่อ PC ดับ, Mobile Draft → `SubmitOrder` batch, Auto-merge แบบ append ที่ trace ย้อนกลับได้, การยกเลิกจริงยืนยันเฉพาะ PC ตาม D06, payment freeze, atomic transaction/outbox, cloud sync, เวลา และค่า RPO/RTO แล้ว; เหลือเลือกและพิสูจน์สื่อ backup จริง | ตัดสินใจ workflow และ recovery target ครบ — เหลือ implementation validation |
| D13 | ยืนยัน Epson TM-T82, RJ45, กระดาษ 80 mm, อย่างน้อย 6 เครื่อง/สาขาและลิ้นชักเงิน; D13.1 ใช้ `printerId` ถาวรและ DHCP Reservation; D13.2 ยืนยัน Provisional Profile `epson-tm-t82x-m352a-v1`; D13.3 แยก Printer Condition จากผล Print Attempt และยังไม่รองรับ `Completed` อัตโนมัติ; D13.4 ใช้ Unicode → Bitmap/Raster ทั้งใบพร้อม Bundle Font/Shaping Engine เป็นค่าเริ่มต้น | ต้องทดสอบความกว้าง/DPI/ภาษาไทย, cut/drawer, error/timeout, duplicate และ Windows/Linux ก่อน Production Certification |
| D14 | ยืนยัน Google Sheets export บางส่วน: Central Integration Worker ส่งยอด `appliedAmount` แบบหนึ่งแถวต่อสาขา/วันธุรกิจ/Payment Method หลังรับ `CloseStore` snapshot, รองรับ aggregate หลาย store sessions, idempotent retry และไม่กระทบการปิดร้าน; สูตรรายงานอื่น ตัวกรอง สิทธิ์ และ export อื่นยังเปิดอยู่ | ใช้รายงานดำเนินธุรกิจและเชื่อม Google Sheets |
| D15 | การแก้/ลบค่าใช้จ่ายและหลักฐานแนบ | เปิดค่าใช้จ่ายจริง |
| D16 | FlowAccount ส่งทุกบิลหรือเฉพาะบิล ออกเอกสารชนิดใด ใครดำเนินการ | เริ่มเชื่อม FlowAccount |
| D17 | ยืนยัน React + TypeScript + Vite/PWA สำหรับ Phase 0, ASP.NET Core .NET 10 LTS, SQLite WAL ที่สาขา, PostgreSQL 18 ส่วนกลาง, browser kiosk, Direct Network PrintGateway, cross-platform `win-x64`/`linux-x64`, Windows Phase 0 และ Linux pilot บน Mini PC; เพิ่มเป้าหมาย Mobile application บน Android/iOS | Backend/PC/Printing stack เพียงพอสำหรับเริ่มโครงสร้าง; ต้องเลือก Native/Cross-platform/Installable PWA ก่อนล็อก Mobile production stack |
| D18 | ยืนยัน D18.1 Navigation หลัก 5 หน้าและหน้าโต๊ะแบบ Split View; Payment อยู่ใน `tableSessionId`; ปุ่มล็อก/ปลดล็อกเป็น UI ของ `RequestPayment`/`CancelPaymentRequest`; D18.2 กำหนด PC POS เป็น Terminal ที่จุดแคชเชียร์ ไม่ใช่ Role; D18.3 รับ PIN แบบรายคำสั่ง; D18.4 มี Manual/Auto-lock ที่ไม่หยุดบริการ; D18.5 รวม Home/Dashboard เป็นหน้าหลักสำหรับสถานะร้าน โต๊ะ งานค้าง Notify สรุปวันปัจจุบัน และ Sold Out Summary/Management ตาม D08.2 โดยย้าย Analytics ไป Back Office; D18.6 ยืนยันหน้ารายการอาหารแบบสองฝั่ง, Sub Item column, Topping Popover, ตะกร้า/ส่งแล้ว และ `fulfillmentType` ระดับ Order Line สำหรับทานที่ร้าน/กลับบ้านในบิลโต๊ะเดียวกัน; D18.7 ให้กลับผังโต๊ะทันทีหลัง `CompletePayment` และแสดง Non-modal Overlay เงินทอน 10 วินาทีเมื่อมีเงินสด; D18.8 ยืนยันประวัติธุรกรรมแบบ Split View/Read-only และ Reprint ตามสิทธิ์; D18.9 ยืนยันหน้าร้านและกะพร้อม Timeline, สรุปช่องทางชำระ, Sidebar `Data Synced`/`Data Sent` และคำสั่งที่เปิด Pop-up เมื่อกด; D18.10 ยืนยันหน้าสถานะระบบแบบ Read-only พร้อมสถานะบริการ เครื่องพิมพ์ งานผิดปกติ และคำสั่งที่ตรวจสิทธิ์ | Navigation และหน้าปฏิบัติงานหลักทั้ง 5 หน้าของ PC POS มีข้อกำหนดเพียงพอสำหรับ Vertical Slice; รายละเอียดเชิงภาพระดับ Design System และหน้าดูแลเชิงลึกแยกเป็นงานออกแบบ/สิทธิ์ภายหลัง |
| D19 | D19.1 ยืนยัน Performance Baseline ชั่วคราวสำหรับ Vertical Slice; D19.2 ยืนยัน Throughput Test Baseline ชั่วคราว 1,000 บิล/วัน, 20 Batch พร้อมกัน, Burst/Sustained Load, Payment และ Offline backlog 8 ชั่วโมง; D19.3 ยืนยัน Software Availability ≥ 99.9% เมื่อ Hardware/OS/LAN ปกติ พร้อม Recovery Target/Offline behavior; D19.4 ยืนยัน Retention และ Capacity Planning Baseline: Branch 24 เดือน, Central 7 ปี, Branch operational allocation ≥ 60 GB และ Central+Backup envelope ประมาณ 120 GB usable/สาขา; RPO/RTO ยืนยันตาม D12.8 | Performance/Throughput/Capacity ยังต้องแทนด้วยผลทดสอบ Windows/Linux และข้อมูล 10,000 บิลจริง; End-to-end Branch SLA รอ Redundancy, Backup retention ขั้นสุดท้ายและข้อกำหนดกฎหมายยังต้องยืนยัน |
| D20 | ยืนยันการแยก Mobile Request, Table Session และ Print Job state machine; กำหนด `requestId`/`orderBatchId`/`printJobId`/`printAttemptId`, atomic transaction/outbox, status query, safe retry และ manual reprint จาก `Unknown`; Phase 0 ใช้ `PrintDispatched` เป็นสถานะอัตโนมัติสูงสุดและพัก print `Completed` จนกว่าจะทดสอบ TM-T82 รุ่นจริง | ตัดสินใจเพียงพอสำหรับเริ่มต้นแบบ offline-first; เหลือ implementation/failure test |
| D21 | ยืนยันให้ทุกองค์ประกอบอยู่ใน Git และเผยแพร่ Source Code ภายใต้ MPL-2.0; D21.1 ยืนยันใช้ Monorepo; D21.2 ยืนยัน License และ Notice/Third-party/SBOM; D21.3 ยืนยัน Public-release Checklist; D21.4 กำหนดเอกสารเป็น CC BY-SA 4.0, Synthetic Test Data เป็น CC0-1.0 และสงวนชื่อ/โลโก้; D21.5 ยืนยัน Path Policy และ Clone/Bootstrap โดยรอ Internal SSD ก่อนกำหนด Drive Letter จริง | เมื่อติดตั้ง Internal SSD ต้องบันทึกตำแหน่งจริงและยืนยัน Clean Clone/Build |

ยังไม่ต้องตอบทุกข้อพร้อมกัน D02, D11, D12 และ D17 ตัดสินใจเพียงพอสำหรับ Phase 0 แล้ว ข้อที่ยังเป็น architecture gate ก่อนเปิดพิมพ์จริงคือ D13 และ D20 จากนั้นจึงล็อก D03, D04 และ D05 สำหรับ vertical slice แรก ส่วนสิทธิ์/การยกเลิกที่ผู้ใช้ขอเลื่อน ยังคงเป็นข้อเปิด ไม่กำหนดแทน

## 11. ข้อเสนอการแบ่งเฟส — ยังไม่ใช่ขอบเขตที่อนุมัติ

### Phase 0 — Architecture Spike

1. Branch Service แบบ cross-platform + SQLite WAL + health check โดย build ทั้ง `win-x64` และ `linux-x64`; Phase 0 เริ่มบน Windows เครื่องเดิม
2. Mobile PWA บน Android/iOS เชื่อมผ่าน LAN ด้วย HTTPS จาก Private CA ที่อุปกรณ์ทดสอบติดตั้งและ trust แล้ว
3. persistent outbox, `requestId`, status query และ idempotent order endpoint
4. Direct Network PrintGateway ที่บันทึก `printJobId`/ผลตอบกลับและส่งไป Thermal Printer ทดสอบผ่าน protocol ที่ D13 ยืนยัน
5. ทดสอบ failure matrix: อินเทอร์เน็ตขาด, Wi-Fi หลุด, process restart, socket timeout, printer offline, paper out และผลไม่ทราบหลังเริ่มส่ง
6. ทดลอง Linux บน Mini PC อย่างน้อยหนึ่งสาขาหลัง Direct Print ผ่าน compatibility test แล้ว

Phase 0 ยังไม่ต้องมีหน้าจอครบ เป้าหมายคือพิสูจน์โครงสร้าง offline-first, Direct Network Printing และ recovery ก่อนขยาย business feature

### Phase 1 — Vertical Slice การขาย

1. ข้อมูลเมนู/โซน/โต๊ะทดสอบ
2. เปิดโต๊ะ → Item/SubItem/Topping/หมายเหตุ → ส่งออร์เดอร์
3. ชำระแบบพื้นฐานหนึ่งวิธีตามกฎที่ตกลง → ใบเสร็จ
4. แสดงสถานะออร์เดอร์และงานพิมพ์ตามหลักฐานจริง
5. เปิดระบบกลับหลัง Mobile/POS/Branch Service restart แล้วข้อมูลที่ยืนยันแล้วต้องไม่สูญหาย

### Phase 2 เป็นต้นไป

1. Back Office และ Master Data หลายสาขา พร้อมเผยแพร่ version และ Notify 72 ชั่วโมง
2. เพิ่มสต็อก ค่าใช้จ่าย รายงาน สิทธิ์ และจัดการใบเสร็จตามลำดับที่ผู้ใช้เลือก
3. เชื่อม FlowAccount หลังยืนยัน workflow บัญชี

เกณฑ์ผ่านขั้นต่ำของ Phase 0/1:

- Android และ iPhone ที่ติดตั้ง Root CA แล้วต้องเปิด PWA ผ่าน hostname ของสาขาได้โดยไม่มี certificate warning
- อุปกรณ์ที่ยังไม่ trust Root CA, certificate หมดอายุ หรือ hostname ไม่ตรง SAN ต้องเชื่อมต่อไม่ได้ ห้าม fallback ไป HTTP
- ตรวจยืนยันว่า PC สาขาและ Mobile ไม่มี Private CA private key และมีเฉพาะ certificate/leaf key ที่จำเป็น
- QR pairing ต้องทำงานได้เมื่ออินเทอร์เน็ตขาดแต่ LAN/Branch Service ยังทำงาน
- QR/token ที่ใช้แล้ว ถูกยกเลิก ถูกแก้ไข หรือเกิน 5 นาทีต้องใช้ pair ซ้ำไม่ได้ และเหตุการณ์ต้องถูกบันทึกใน audit log
- Mobile ที่ยังไม่ pair หรือถูก revoke ต้องเรียก protected Branch API ไม่ได้
- Mobile ต้องเปิด Branch Service ผ่าน DHCP-reserved IP ที่อยู่ใน certificate SAN ได้โดยไม่มี warning และการใช้ IP อื่นต้องล้มเหลว
- พนักงานที่เคยซิงก์ไว้และยัง Active ต้อง login ที่ Branch ได้ขณะอินเทอร์เน็ตขาด
- การ revoke ที่ PC POS ต้องมีผลทันที ส่วนการ revoke จากส่วนกลางขณะสาขา offline ต้องมีผลในการซิงก์ครั้งแรกหลังเชื่อมต่อกลับและแสดงเวลา sync ล่าสุดได้
- Device credential ที่ไม่มีวันหมดอายุต้องใช้ไม่ได้ทันทีหลัง Branch บันทึกสถานะ Revoked
- ต้องต่ออายุ Branch certificate และสลับ certificate ใหม่ได้โดย Mobile ที่ trust Root เดิมไม่ต้องติดตั้ง Root CA ซ้ำ
- ต้องกู้คืน CA inventory/backup ที่เข้ารหัสในการซ้อมกู้คืน และตรวจยืนยันว่าไม่มี CA private key อยู่บน Network Gateway, Branch PC หรือ Mobile
- Mobile หลายเครื่องส่ง `SubmitOrder` เข้า `tableSessionId` เดียวกันพร้อมกันแล้วทุก batch ต้องปรากฏในบิลครั้งละหนึ่งชุด ไม่มี batch สูญหายหรือซ้ำ และ trace กลับไปยัง request/device/employee ได้
- หลัง Branch Activate Master Data รุ่นใหม่ Order Batch ที่เพิ่มเข้า session เดิมต้องยังได้ราคาและกฎคำนวณจาก `sessionMasterDataVersion` เดิม แต่ session ที่เปิดใหม่ต้องใช้รุ่นใหม่; ทั้งสองกรณียังต้องปฏิเสธ Item/Option ที่สิทธิ์ขายปัจจุบันปิดหรือ Sold Out
- Notify ที่เก่ากว่า 72 ชั่วโมงหรือมาผิดลำดับต้องนำไปสู่ Full Version Check และห้ามทำให้ Branch Apply Payload เก่าหรือย้อน Active Package
- Mobile ต้องแสดงยอดจาก Branch และ Refresh เมื่อ `tableVersion` เปลี่ยน; ห้ามแสดงยอด Cache เก่าเป็นยอดปัจจุบัน และต้องไม่เปิดเผย Payment Lines, เงินรับ หรือเงินทอน
- Mobile ที่มี Draft ค้างในเครื่องต้องส่งหรือทิ้ง Draft ก่อน `RequestPayment`; คำสั่งซ้ำด้วย `requestId` เดิมต้องสร้าง Bill Preview/print job เพียงชุดเดียว และ Mobile เครื่องอื่นที่ Submit หลัง `PaymentPending` ต้องได้รับการปฏิเสธโดย Draft ไม่สูญหาย
- Mobile ต้องสร้าง cancellation request หลายรายการใน session เดียวได้แบบ Atomic; จำนวนที่ขอต้องถูกล็อกและคำสั่งซ้ำต้องไม่สร้างคำขอใหม่ ขณะที่ Mobile ไม่มีสิทธิ์ Confirm/Reject/ถอนคำขอ
- Mobile สองเครื่องเปิดโต๊ะเดียวกันพร้อมกันต้องได้ `tableSessionId` เดียวกันและ Submit แบบ Auto-merge ได้; หลัง Branch รับ Batch แล้ว Mobile ต้องแก้/ลบ Batch นั้นไม่ได้
- ปิดแอป/Restart/Logout แล้ว Draft และ Pending ต้องยังอยู่; Draft ให้พนักงาน Active คนอื่นรับช่วงได้พร้อม trace ผู้สร้างเดิม แต่ Pending ต้องคงผู้ส่งและ `requestId` เดิม และ Draft ของ session ที่ปิดแล้วต้องเป็น `Blocked/NeedsReview`
- การยกเลิกรายการที่รับแล้วต้องเกิดจาก PC เป็น cancellation event ซึ่งไม่แก้หรือลบ payload ต้นฉบับ
- Mobile สร้าง cancellation request แล้วรายการต้องยัง `Active`; PC ต้องบันทึกจำนวนตามสถานะการผลิตครบทุกจำนวนโดยไม่มี `Unknown` และ `RequestPayment` ต้องถูกปฏิเสธจนกว่า PC จะ `ConfirmCancellation` หรือ `RejectCancellation` ปิดทุกคำขอ
- `ConfirmCancellation` ต้องอนุญาตการยกเลิกอาหารได้ทุกสถานะการผลิต พิมพ์สรุปจำนวนที่ยกเลิกจริงแยกตามสถานะเฉพาะเครื่องแคชเชียร์ และต้องไม่สร้าง print job ไปยังครัว/เค้ก/สถานีผลิต
- `ChangeTable`/`TransferItems` ต้องไม่แก้ payload หรือเลขโต๊ะของงานออร์เดอร์เดิม แม้งานเดิมยัง `PrintQueued` หรือถูก retry หลัง `Failed`
- ทุกการย้ายต้องสร้างใบรวมแคชเชียร์หนึ่งชุด และสร้างใบรายสถานีเฉพาะรายการที่ PC เลือก `notifyProductionStation`; หลายรายการของสถานีเดียวกันต้องรวมเป็นหนึ่งใบและตรวจย้อนกลับด้วย `transferEventId` ได้
- เมื่อ Branch รับ `RequestPayment` แล้ว table session ต้องเป็น `PaymentPending` และ `SubmitOrder` ที่มาภายหลังต้องถูกปฏิเสธทั้งชุดโดย Draft บน Mobile ไม่สูญหาย
- เมื่อ `SubmitOrder` ชนกับ `RequestPayment` ผลต้องตรงกับลำดับ commit เสมอ: batch อยู่ในยอดชำระหาก commit ก่อน หรือถูกปฏิเสธทั้งชุดหาก `RequestPayment` commit ก่อน ห้ามเกิด partial append
- หลัง PC ใช้ `CancelPaymentRequest` โต๊ะต้องกลับเป็น `Ordering` และรับ `SubmitOrder` ได้อีกครั้ง; หลัง `CompletePayment` ต้องรับคำสั่งของ `tableSessionId` เดิมไม่ได้
- คำขอ `requestId` เดิมที่ส่งซ้ำ 10 ครั้งสร้างออร์เดอร์เพียงหนึ่งรายการ
- response สูญหายหลัง Branch บันทึกแล้ว Mobile ต้องตรวจพบรายการเดิมจาก status query ไม่สร้างรายการใหม่
- อินเทอร์เน็ตขาดแต่ LAN ทำงาน Mobile ยังส่งออร์เดอร์และพิมพ์ภายในร้านได้
- Wi-Fi หลุดก่อน Branch รับต้องแสดง `Pending`; เมื่อเชื่อมใหม่ต้องส่งต่อด้วย `requestId` เดิม
- Wi-Fi หลุดหลัง Branch รับแต่ก่อน Mobile ได้ response ต้องคืนสถานะของออร์เดอร์เดิมได้
- restart Mobile และ Branch Service แล้ว outbox, order และ print job ที่บันทึกแล้วไม่สูญหาย
- การทดสอบ kill process ต้องกู้ข้อมูลที่ `BranchAccepted` แล้วครบถ้วนภายใน 5 นาทีโดยมี `RPO = 0`
- การทดสอบ restart ระบบปฏิบัติการ/ไฟดับจำลองต้องกู้ข้อมูลที่ `BranchAccepted` แล้วครบถ้วนภายใน 15 นาทีโดยมี `RPO = 0`
- การซ้อมเปลี่ยน SSD/PC และ restore จาก Cloud ขณะออนไลน์ต้องพิสูจน์ `RPO <= 5 นาที` และ `RTO <= 4 ชั่วโมง`
- การซ้อม restore จาก encrypted local backup โดยตัดอินเทอร์เน็ตต้องพิสูจน์ `RPO <= 15 นาที` และ `RTO <= 4 ชั่วโมง`
- การทดสอบข้อมูลอย่างน้อย 10,000 บิลต้องวัดขนาด database, incremental backup และ compressed full backup จริง แล้วตรวจว่าขนาดจัดสรรหลังเผื่อ 15% รองรับ retention ที่กำหนดได้
- Topping เพิ่มราคาตามข้อมูล snapshot และหมายเหตุไม่เพิ่มราคา
- สถานีพิมพ์ถูกต้อง และ print retry ไม่สร้างสำเนาโดยไม่มีคำเตือน/การตัดสินใจของผู้ใช้
- printer offline หรือผลไม่แน่นอนต้องแสดง `Failed`/`Unknown` ไม่แสดง “พิมพ์สำเร็จ”

### D19.1 — Performance Target เบื้องต้น — ยืนยันสำหรับการออกแบบ/ทดสอบ

ค่าต่อไปนี้เป็น Baseline ชั่วคราวสำหรับ Vertical Slice ระหว่างที่ยังติดตั้งฮาร์ดแวร์สาขาไม่ครบ ไม่ใช่ค่ารับรอง Production และต้องทบทวนจากผลทดสอบบนฮาร์ดแวร์จริงทั้ง Windows และ Linux pilot:

| การทำงานภายใน LAN | เป้าหมาย p95 |
|---|---:|
| เปิดผังโต๊ะหรือเลือกโต๊ะ | ≤ 500 ms |
| เพิ่มรายการลงตะกร้า | ≤ 100 ms |
| เปิด Sub Item/Topping | ≤ 200 ms |
| Branch รับและ commit `SubmitOrder` | ≤ 1 วินาที |
| เริ่มส่ง Print Job หลังรับออร์เดอร์ | ≤ 2 วินาที |
| เปิด Payment Workspace | ≤ 500 ms |
| ยืนยัน Payment Line | ≤ 1 วินาที |
| `CompletePayment` | ≤ 2 วินาที |
| ค้นประวัติของ Business Date ปัจจุบัน | ≤ 1 วินาที |

เงื่อนไขการวัด:

- ใช้โหลด 1 PC POS และ Mobile พร้อมกันไม่เกิน 20 เครื่องต่อสาขา ข้อมูลอย่างน้อย 10,000 บิล และ Printer Profile อย่างน้อย 6 เครื่อง
- รายงานอย่างน้อย p50, p95, จำนวนคำขอทั้งหมด อัตราล้มเหลว และจำนวนคำขอซ้ำ/สูญหาย โดยแยกตาม Operation
- เวลา Print Job วัดถึงจุดที่ PrintGateway เริ่มส่งข้อมูลไปเครื่องพิมพ์ ไม่รวมเวลาทางกลที่เครื่องพิมพ์ใช้พิมพ์กระดาษ
- ทดสอบทั้งภาวะปกติและช่วงที่ Mobile ส่งคำขอพร้อมกันครบ 20 เครื่อง โดย idempotency และความถูกต้องของข้อมูลต้องผ่านก่อนพิจารณาว่า Performance ผ่าน
- เมื่อกำหนด CPU, RAM, Storage, OS และ Network ของสาขาครบ ต้องจัดทำ Baseline ใหม่และขออนุมัติก่อนใช้เป็น Production SLO

### D19.2 — Throughput Target เบื้องต้น — ยืนยันจนกว่าจะทดสอบจริง

ฐานข้อมูลตัวอย่างปัจจุบันมี 8,156 บิลใน 31 วัน เฉลี่ยประมาณ 263 บิล/วัน และประมาณ 5.66 Product-detail records ต่อบิล จึงกำหนด Test Baseline ที่สูงกว่าค่าเฉลี่ยดังนี้ โดยยังไม่ถือเป็น Production Limit:

| รายการทดสอบ | เป้าหมายเบื้องต้น |
|---|---:|
| ปริมาณรายวัน | 1,000 บิล/สาขา/วัน |
| Mobile ส่ง `SubmitOrder` พร้อมกัน | 20 Batch |
| Order Line ต่อ Batch สำหรับ Load Test | 20 รายการ |
| Burst | 20 Batch ภายใน 10 วินาที |
| Sustained Load | 30 Batch/นาที ต่อเนื่อง 15 นาที |
| Payment | 10 `CompletePayment`/นาที |
| Print enqueue | รับงานจาก Burst ทั้งหมดเข้าคิวโดยไม่สูญหาย |
| Offline backlog | สะสมธุรกรรม 8 ชั่วโมงและส่งตามหลังได้ครบ |

เกณฑ์ผ่าน:

- ไม่มีคำสั่ง Order Line, Payment, Stock Movement, Audit Event, Print Job หรือ Cloud Outbox สูญหาย และยอดหลังจบการทดสอบต้องกระทบกันครบ
- `requestId`/idempotency key เดิมต้องคืนผลเดิมและห้ามสร้างผลซ้ำ; แต่ละ Batch ต้อง commit แบบ Atomic
- PC POS ต้องยังใช้งานได้ขณะ Central Sync ระบาย Backlog และการ Sync ต้องไม่แย่งทรัพยากรจน Response Time หลุด D19.1
- Print Job ต้องเข้าคิวครบแม้เครื่องพิมพ์จริงพิมพ์ไม่ทัน; สถานะรอคิวไม่ถือเป็น Failure เพียงเพราะอัตราทางกลของเครื่องพิมพ์ต่ำกว่าอัตรา enqueue
- ต้องทดสอบทั้ง Windows และ Linux pilot บน Hardware สาขาจริง บันทึก Peak จริง แล้วทบทวนค่าทั้งหมดก่อนกำหนด Production Capacity/Limit

### D19.3 — Availability Target เบื้องต้น — ยืนยัน

- วัด Availability เฉพาะช่วง `OpenStore` ถึง `CloseStore` และแยกอย่างน้อยเป็น PC POS/Branch Service, Mobile ผ่าน LAN/Wi-Fi, ระบบพิมพ์ภายในร้าน และ Cloud/Central connectivity
- Cloud หรืออินเทอร์เน็ตภายนอกขาดไม่ถือว่า POS สาขา Unavailable หากยังเปิดโต๊ะ รับออร์เดอร์ ชำระเงิน และพิมพ์ผ่าน LAN ได้; ต้องเก็บ Outbox และซิงก์ตามหลังตาม D12
- เป้าหมาย Software Availability ของ PC POS/Branch Service คืออย่างน้อย 99.9% ในช่วงเปิดร้าน ภายใต้เงื่อนไขว่า Hardware, ระบบปฏิบัติการ และ LAN ของสาขายังปกติ; ค่านี้ไม่ใช่ End-to-end Branch SLA
- Process ล่มต้อง Auto-restart และกลับมาใช้งานภายใน 5 นาที; OS restart หรือไฟดับตามปกติต้องกลับมาใช้งานภายใน 15 นาที; SSD/PC เสียต้องกู้ภายใน 4 ชั่วโมงเมื่อมี Hardware ทดแทนและ Backup พร้อม ตาม D12.8
- เมื่ออินเทอร์เน็ต/Cloud ขาด งานขายภายในสาขาต้องทำต่อได้โดยไม่กำหนดเวลาสูงสุดล่วงหน้า ตราบเท่าที่ Hardware/LAN และพื้นที่ฐานข้อมูลสาขายังปกติ
- เมื่อ LAN/Wi-Fi ขาด PC POS ที่อยู่บน Branch PC ยังขายได้ แต่ Mobile และ Network Printer อาจใช้งานไม่ได้; UI ต้องแสดงขอบเขตผลกระทบและห้ามรายงานว่าระบบสาขาทั้งหมดปกติ
- Planned Maintenance และการ Update ที่ต้องหยุดบริการต้องทำนอกช่วง `OpenStore`; Update ล้มเหลวต้อง Rollback ได้โดยไม่สูญเสียธุรกรรมที่ commit แล้ว
- เนื่องจาก Phase 0 มี Branch PC เดียวและยังไม่มี Hardware/LAN redundancy จึงยังไม่กำหนด End-to-end Availability SLA ของสาขา; ต้องทบทวนหลังเลือก UPS, Network, Hardware สำรอง และกระบวนการเปลี่ยนเครื่องจริง

### D19.4 — Retention Policy และ Capacity Estimate — ยืนยันเป็น Planning Baseline

Retention ที่ยืนยัน:

- Branch Database เก็บธุรกรรมที่ค้นดูได้ย้อนหลัง 24 เดือน
- Central Database เก็บ Order, Receipt, Payment, Cancellation, Transfer, Stock Movement และ Audit อย่างน้อย 7 ปี
- Receipt Snapshot เก็บเท่ากับ Receipt/ธุรกรรมต้นทาง เพื่อให้ Reprint จาก Snapshot เดิมได้
- Idempotency/Deduplication Record ของออร์เดอร์และธุรกรรมการเงินเก็บเท่ากับธุรกรรมต้นทาง; Outbox ที่ยังไม่ได้ Acknowledgment ห้ามลบเพราะครบอายุ
- Print Job/Print Attempt metadata เก็บใน Branch อย่างน้อย 90 วัน ส่วน Binary spool/rendered payload ที่อยู่ในสถานะปลายทางแล้วให้แยก Retention/Quota และวัดขนาดจริง เพราะรูปแบบ Text/ESC-POS กับ Raster มีขนาดต่างกันมาก; Payload ของงานที่ยัง `Queued`, `Dispatching` หรือ `Unknown` ห้ามลบ
- Application Log เก็บใน Branch 30 วันและส่วนกลาง 90 วัน พร้อม Size quota และ automatic rotation
- Mobile Draft ที่ไม่เคยส่งเก็บ 30 วัน จากนั้นเปลี่ยนเป็น `Expired/NeedsReview` ก่อนลบตาม Workflow ที่จะออกแบบ; Pending/Submitted request ที่เริ่มส่งแล้วห้ามหมดอายุแบบ Draft
- Retention ของ Daily/Weekly/Monthly Backup ยังคงแยกไว้ตัดสินใจภายหลังตาม D12.8
- ระยะเวลา 7 ปีเป็น Technical Planning Baseline ไม่ใช่คำรับรองด้านกฎหมาย/ภาษี ต้องตรวจสอบกับฝ่ายบัญชีหรือที่ปรึกษากฎหมายก่อน Production

ฐานคำนวณที่มีหลักฐานจากข้อมูลตัวอย่าง:

- 8,156 บิลใน 31 วัน เฉลี่ยประมาณ 263 บิล/วัน, 3.69 Activity Events และ 5.66 Product-detail Records ต่อบิล
- ปริมาณข้อมูลใหม่หลังเผื่อ 15% เท่ากับประมาณ `120–350 MB/เดือน/สาขา` หรือ `1.5–4.2 GB/ปี/สาขา` ซึ่งรวม transaction, order/payment, activity/audit, deduplication, print/cloud outbox, indexes และ database overhead แต่ไม่รวม Binary print spool, application log ที่เกิน quota และไฟล์แนบ

ประมาณการแยกตามตำแหน่งจัดเก็บ:

| ตำแหน่ง/ข้อมูล | Retention/หลักคำนวณ | ปริมาณต่อสาขา |
|---|---|---:|
| Branch live database | 24 เดือน × 1.5–4.2 GB/ปี | `3.0–8.4 GB` |
| Branch application log | 30 วัน; เป็นโควตา ไม่ใช่ค่าที่วัดแล้ว | `ไม่เกิน 2 GB` |
| Branch terminal print spool | Binary payload หลังจบงาน; โควตาชั่วคราวจนวัด Text/Raster จริง | `ไม่เกิน 5 GB` ภาวะปกติ |
| Branch runtime/update/rollback packages | เก็บ current + previous version | `ประมาณ 10 GB` |
| Branch DB working headroom | WAL, checkpoint, migration, integrity check, temporary backup | `อย่างน้อย 20 GB` |
| **Branch operational allocation** | รวมรายการข้างต้นและปัดเผื่อ | **อย่างน้อย 60 GB usable** |
| Local encrypted backup disk | ค่าขั้นต่ำตาม D12.8; retention รุ่นจริงยังไม่กำหนด | **อย่างน้อย 25 GB usable/สาขา** |
| Central live database | 7 ปี × 1.5–4.2 GB/ปี | `10.5–29.4 GB/สาขา` |
| Central DB operational headroom | เผื่อประมาณ 50% สำหรับ vacuum/index rebuild/migration | `5.3–14.7 GB/สาขา` |
| Central live allocation | Live data + 50% headroom | `ประมาณ 16–45 GB/สาขา` |
| Central/NAS backup allocation เริ่มต้น | ค่าที่วางแผนไว้ใน D12.8; ยังไม่รวม policy Daily/Weekly/Monthly ขั้นสุดท้าย | `40–60 GB/สาขา` |
| **Central + Backup planning envelope** | Live allocation + backup และปัดเผื่อ | **ประมาณ 120 GB usable/สาขา** |

ตัวอย่าง Capacity ส่วนกลาง โดยใช้ Planning Envelope `20 GB` สำหรับระบบ/Master Data พื้นฐาน และ `120 GB` ต่อสาขา:

| จำนวนสาขา | Usable capacity ขั้นต้น | Raw disk โดยประมาณเมื่อใช้ Mirror/RAID 1 เฉพาะชุดหลัก* |
|---:|---:|---:|
| 1 | `140 GB` | `280 GB` |
| 5 | `620 GB` | `1.24 TB` |
| 10 | `1.22 TB` | `2.44 TB` |
| 20 | `2.42 TB` | `4.84 TB` |
| 50 | `6.02 TB` | `12.04 TB` |

\* Raw disk เป็นเพียงการคูณสองสำหรับ Mirror แบบง่าย ยังไม่รวม Filesystem reserve, RAID metadata, Snapshot, Spare disk, Offline/Offsite copy หรือความต่างระหว่าง TB/TiB; การจัดซื้อจริงต้องคิดจาก usable capacity หลัง RAID และต้องมี Offline/Offsite copy เพิ่มอีกชุดตาม D12.8

ข้อสรุปสำหรับ Hardware Planning:

- ข้อมูลธุรกรรมไม่ใช่เหตุผลที่ต้องใช้ Branch SSD ขนาดใหญ่มาก; คู่ NVMe 500 GB แบบ Mirror ให้พื้นที่มากกว่า Baseline หลายเท่าและควรเลือกจาก endurance/reliability มากกว่าความจุเพียงอย่างเดียว แต่รุ่น/ขนาดจริงยังต้องยืนยันหลังทดสอบ
- Internal Local Backup disk ควรมีความจุสูงกว่าขั้นต่ำ 25 GB อย่างมากเพื่อรองรับ Retention ที่จะกำหนด; ขนาดจัดซื้อ 250–500 GB เป็น Planning Option ไม่ใช่ Requirement ที่ยืนยัน
- Central/NAS ต้องแยก Master Data Backup Pool และ Branch Backup Pool ตาม D12.8 และคำนวณจากจำนวนสาขาจริง, Retention, Snapshot ratio และ Offsite copy ก่อนจัดซื้อ
- ต้องวัดขนาด SQLite/PostgreSQL, Index, WAL, compressed backup, application log และ Text/Raster print spool จากข้อมูลจำลองอย่างน้อย 10,000 บิล แล้วแทนค่าประมาณทั้งหมดก่อน Production

## 12. Non-functional Requirements ที่ต้องกำหนดค่า

ตารางนี้กำหนดหัวข้อที่ต้องมี แต่ค่าตัวเลขยังต้องตอบใน D02/D19 ไม่ให้ผู้พัฒนาสมมติเอง

| ID | หัวข้อ | ค่าที่ต้องกำหนด |
|---|---|---|
| NFR-01 | Concurrent usage | ยืนยัน 1 PC POS/Branch Service และ Mobile พร้อมกันไม่เกิน 20 เครื่องต่อสาขา ต้องทดสอบการเชื่อมต่อและการส่งคำขอพร้อมกันครบ 20 เครื่องโดยไม่สูญหายหรือสร้างรายการซ้ำ |
| NFR-02 | Performance | ใช้ Baseline ชั่วคราวตาม D19.1: p95 เพิ่มรายการ ≤ 100 ms, เปิด Sub Item/Topping ≤ 200 ms, เปิด/เลือกโต๊ะและ Payment Workspace ≤ 500 ms, `SubmitOrder`/ยืนยัน Payment Line/ค้นประวัติวันนี้ ≤ 1 วินาที, เริ่มส่ง Print Job/`CompletePayment` ≤ 2 วินาที; ต้องทบทวนบน Hardware จริงก่อนกำหนด Production SLO |
| NFR-03 | Throughput | ใช้ Test Baseline ชั่วคราวตาม D19.2: 1,000 บิล/วัน, 20 Batch พร้อมกัน, Batch ละ 20 Order Line, Burst 20 Batch/10 วินาที, Sustained 30 Batch/นาที 15 นาที, 10 `CompletePayment`/นาที และ Offline backlog 8 ชั่วโมง; ต้องแทนด้วยผลทดสอบจริงก่อน Production |
| NFR-04 | Availability | วัดช่วง `OpenStore`–`CloseStore`; Software Availability ของ PC POS/Branch Service ≥ 99.9% เมื่อ Hardware/OS/LAN ปกติ; Process recovery ≤ 5 นาที, OS restart/ไฟดับปกติ ≤ 15 นาที และ Hardware failure recovery ≤ 4 ชั่วโมงเมื่อมีเครื่อง/Backup พร้อม; Cloud outage ไม่ทำให้ Local POS unavailable; End-to-end Branch SLA รอกำหนดหลังมี Hardware/LAN redundancy |
| NFR-05 | Recovery และ backup capacity | Service crash: RPO 0/RTO ≤ 5 นาที; OS restart/ไฟดับปกติ: RPO 0/RTO ≤ 15 นาที; SSD/PC เสียขณะออนไลน์: RPO ≤ 5 นาที/RTO ≤ 4 ชั่วโมง; SSD/PC เสียขณะ offline: RPO ≤ 15 นาที/RTO ≤ 4 ชั่วโมง; encrypted local backup อย่างน้อยทุก 15 นาทีลง Internal physical disk ที่แยกจาก live RAID; planning baseline หลังเผื่อ 15% คือข้อมูลใหม่ 120–350 MB/เดือนหรือ 1.5–4.2 GB/ปี, Local backup ขั้นต่ำ 25 GB และ Cloud backup เริ่มต้น 40–60 GB ต่อสาขา; ต้องพิสูจน์ด้วย restore test และข้อมูลอย่างน้อย 10,000 บิล |
| NFR-06 | Retention | ตาม D19.4: Branch transaction/searchable history 24 เดือน; Central transaction/receipt/payment/cancellation/transfer/stock/audit อย่างน้อย 7 ปี; Receipt Snapshot และ deduplication เท่าธุรกรรมต้นทาง; Print metadata 90 วัน; Branch log 30 วัน/Central log 90 วัน; Mobile unsent Draft 30 วันก่อน `Expired/NeedsReview`; Backup retention ยังรอกำหนด |
| NFR-07 | Security | authentication, authorization, session offline, encryption, secret rotation และ audit |
| NFR-08 | Deployment | Windows 11 Pro สำหรับเครื่องเดิม; pinned Linux LTS สำหรับ pilot/new Mini PC; Windows Service/systemd auto-start, signed package, update/rollback, RAID 1 และ Internal Backup disk แยก; development workspace อยู่บน non-system volume และ runtime path แยกจาก Git working copy |
| NFR-09 | Observability | health check, structured logs, metrics, alert และ log rotation |
| NFR-10 | Compatibility | Android/iOS client และ OS รุ่นต่ำสุด, browser/runtime ที่เกี่ยวข้อง, Windows/Linux support matrix และ Thermal Printer รุ่น/firmware/protocol ที่รับรอง |
| NFR-11 | Time | timezone, business day, clock drift และลำดับ event |
| NFR-12 | Data integrity | unique constraints, transaction boundary, idempotency และ reconciliation |

## 13. คำศัพท์สถานะสำคัญ

| คำ | ความหมาย |
|---|---|
| Draft | ข้อมูลที่ยังอยู่บน Mobile และผู้ใช้ยังไม่เริ่มส่ง |
| Pending | Mobile บันทึกลง local outbox แล้ว แต่ยังไม่มีหลักฐานว่า Branch รับ |
| Submitted | Mobile เริ่มส่งคำขอแล้ว ผลอาจยังไม่ทราบ |
| BranchAccepted | Branch บันทึกออร์เดอร์ลง persistent storage สำเร็จและผูกกับ `requestId` แล้ว |
| PrintQueued | Branch สร้าง print job แบบ persistent แล้ว |
| PrintDispatched | PrintGateway ส่ง payload ลง TCP/IPP/LPR transport ตาม contract แล้ว แต่ไม่รับรองกระดาษออก |
| Completed | workflow ทางธุรกิจที่กำหนดสำหรับงานนั้นเสร็จ ไม่ใช้แทนหลักฐานทางกายภาพเกินกว่าที่ระบบมี |
| Failed | มีหลักฐานชัดเจนว่างานล้มเหลวและต้อง retry/แก้ไข |
| Unknown | ผลสุดท้ายไม่ทราบ เช่น timeout หลังส่งงาน ห้ามสรุปเป็น Failed หรือ Success โดยอัตโนมัติ |

เอกสารนี้ใช้เริ่มทบทวนขอบเขตและทำต้นแบบได้ การเริ่มพัฒนาหรือเลือกเทคโนโลยีไม่ได้เกิดขึ้นจากการจัดทำสรุปครั้งนี้
