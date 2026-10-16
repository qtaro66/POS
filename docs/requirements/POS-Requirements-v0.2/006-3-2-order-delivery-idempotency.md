
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
