
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
