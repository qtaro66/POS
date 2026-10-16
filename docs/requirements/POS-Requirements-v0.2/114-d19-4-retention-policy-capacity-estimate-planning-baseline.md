
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
