
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
