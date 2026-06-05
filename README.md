# POOC — ระบบบริหารจัดการกองทุน

ระบบจัดการกองทุนสหกรณ์ออมทรัพย์ สำหรับบริหารสมาชิก เงินกู้ เงินฝาก และรายงานบัญชี  
พัฒนาด้วย **ASP.NET Core 9 MVC** + **Entity Framework Core** + **SQLite**

---

## ฟีเจอร์หลัก

| โมดูล | รายละเอียด |
|---|---|
| **สมาชิก** | เพิ่ม / แก้ไข / ลบสมาชิก, นำเข้าข้อมูลจาก Excel |
| **เงินกู้** | สร้างสัญญาเงินกู้, คำนวณงวดชำระ, บันทึกชำระ, ปิดบัญชีก่อนกำหนด |
| **เงินฝาก** | ฝาก / ถอนเงินสด, คำนวณดอกเบี้ยเงินฝากรายปี |
| **เอกสารและบัญชี** | สมุดบัญชีรับ-จ่าย, Export CSV/PDF |
| **ภาพรวม** | Dashboard สรุปยอดเงินกู้, เงินฝาก, กราฟแนวโน้ม |
| **ใบเสร็จ PDF** | ออกใบเสร็จรับชำระ, ใบสำคัญจ่าย, ใบรับเงินฝาก |
| **ตั้งค่า** | อัตราค่าปรับ, อัตราดอกเบี้ยเงินฝากเริ่มต้น |
| **Audit Log** | บันทึกทุก Action ที่สำคัญโดยอัตโนมัติ |

---

## 🛠 Tech Stack

- **Backend:** ASP.NET Core 9 MVC, C# 13
- **ORM:** Entity Framework Core 9 (SQLite / SQL Server)
- **PDF:** QuestPDF 2026
- **Frontend:** Bootstrap 5, Bootstrap Icons, Chart.js, SweetAlert2
- **Auth:** Cookie Authentication + Role-based Authorization
- **Security:** Anti-Forgery Token, Rate Limiting (Login), Bcrypt Password Hashing
- **Backup:** SQLite Auto Backup (Background Service)

---

## การติดตั้งและรันโปรเจกต์

### ข้อกำหนดเบื้องต้น

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

### ขั้นตอน

```bash
# 1. Clone โปรเจกต์
git clone <repository-url>
cd POOC

# 2. Restore dependencies
dotnet restore

# 3. รัน Migration (สร้างฐานข้อมูล)
dotnet ef database update

# 4. รันโปรเจกต์
dotnet run
```

เปิดเบราว์เซอร์ที่ `http://localhost:7000`

---

## การตั้งค่า

แก้ไข `appsettings.json` เพื่อเปลี่ยน Connection String:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=loan_data.db"
  }
}
```

สำหรับ SQL Server:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=...;Database=POOC;..."
  }
}
```

---

## โครงสร้างโปรเจกต์

```
POOC/
├── Controllers/
│   ├── AuthController.cs         # Login / Logout
│   ├── HomeController.cs         # Dashboard, Member view
│   ├── MemberController.cs       # CRUD สมาชิก, เงินฝาก
│   ├── LoanController.cs         # CRUD เงินกู้, ชำระงวด
│   ├── AccountingController.cs   # รายงานบัญชี, PDF/CSV
│   └── SettingsController.cs     # ตั้งค่าระบบ
├── Models/                        # Entity Models
├── Data/
│   ├── ApplicationDbContext.cs   # EF Core DbContext
│   └── DatabaseInitializer.cs    # Seed ข้อมูลเริ่มต้น
├── Services/
│   ├── PasswordHashService.cs
│   ├── LoginRateLimiter.cs
│   └── SqliteBackupService.cs
├── Views/                         # Razor Views
├── wwwroot/                       # Static Assets
└── Migrations/                    # EF Core Migrations
```

---

## ระบบ Authentication

- ล็อกอินด้วย Username / Password
- Session หมดอายุใน **8 ชั่วโมง** (Sliding Expiration)
- มี Rate Limiting ป้องกัน Brute Force
- แยก Role: **Admin** / สมาชิกทั่วไป

---

## การ Export ข้อมูล

| รูปแบบ | รายละเอียด |
|---|---|
| **PDF ใบเสร็จ** | A5, ออกต่อรายการ (เงินกู้ / เงินฝาก) |
| **PDF รายงานบัญชี** | A4 Landscape, สรุปช่วงวันที่ที่เลือก |
| **CSV** | Export บัญชีรับ-จ่ายทั้งหมดในช่วงวันที่ที่เลือก |

---

## License

โปรเจกต์นี้ใช้ [QuestPDF Community License](https://www.questpdf.com/license/)
