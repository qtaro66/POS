
#### D12.5 Transaction และ Outbox — ยืนยัน

- Branch บันทึก order change, request deduplication, audit event, print outbox และ cloud-sync outbox ใน database transaction เดียวกัน
- ตอบ `BranchAccepted` หลัง transaction commit ลง persistent storage แล้วเท่านั้น
- Branch ต้องมี inbox/deduplication table, print outbox, cloud-sync outbox, retry count, next retry time, last error และ processing status
