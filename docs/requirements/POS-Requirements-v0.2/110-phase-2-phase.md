
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
