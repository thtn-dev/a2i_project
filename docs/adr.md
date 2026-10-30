# Architecture Decision Records

Định dạng: Bối cảnh, Quyết định, Hệ quả. Trạng thái: tất cả **Accepted**.

---

## ADR-001: Tự xây shell tối giản thay vì dùng framework có sẵn

**Bối cảnh.** Cần nền chung cho nhiều hệ thống nghiệp vụ multi-tenant. Các framework có sẵn (ABP, Orchard Core...) mang theo nhiều quy ước và tính năng không cần, khó nâng cấp, khó hiểu hết.

**Quyết định.** Tự viết shell nhỏ trên ASP.NET Core Minimal API: `IModule` + loader có topological sort + tenancy + migration theo module. Mọi thứ khác dùng API chuẩn .NET (DI, Options, HealthChecks, ProblemDetails, EF Core). Không dùng MediatR, AutoMapper, repository bọc EF.

**Hệ quả.**
- (+) Đọc hiểu toàn bộ shell trong một buổi; không bị khoá vào framework.
- (+) Package runtime: Npgsql EF Core, Serilog.AspNetCore, Swashbuckle (chỉ Development).
- (−) Identity, event bus, outbox... phải tự gắn khi cần (xem [extension-points.md](extension-points.md)).

---

## ADR-002: Module là ranh giới code, nạp một lần lúc khởi động

**Bối cảnh.** Cần tách các mảng nghiệp vụ để code dễ hiểu, dễ giao cho từng nhóm, và có thể tách thành service sau này. Nạp hoặc gỡ assembly lúc chạy (`AssemblyLoadContext` collectible) chỉ thành công khi không còn tham chiếu nào (DI, EF model cache, routing...), rất khó đảm bảo, lại phải đồng bộ DLL giữa các instance.

**Quyết định.**
- Mỗi module là một cặp project `Modules.X` (internal) và `Modules.X.Contracts` (public), có một class `IModule` khai báo `Id` và `DependsOn` ngay trong code. Không có file cấu hình module.
- Module được biên dịch cùng Host, được quét và nạp một lần lúc khởi động. Thiếu dependency hoặc có vòng lặp → Host từ chối khởi động.
- Module luôn chạy. Endpoint nằm dưới `/api/{id}`, dữ liệu nằm trong schema riêng (ADR-005).
- Module chỉ dùng module khác qua `*.Contracts`; quy tắc này được kiểm tra bằng architecture test.
- Thêm hoặc sửa module = build lại và deploy.

**Hệ quả.**
- (+) Ít khái niệm, không có trạng thái runtime phải đồng bộ giữa instance; mọi instance chạy cùng một image.
- (+) Lỗi dependency giữa module phát hiện ngay lúc khởi động (và trong CI).
- (−) Không thêm module mà không deploy. Cần tắt tính năng theo tenant thì làm feature flag theo tenant (extension-points).

---

## ADR-003: Multi-tenant Hybrid (Pool + Silo), xác định tenant theo domain

**Bối cảnh.** Phần lớn tenant nhỏ nên dùng chung hạ tầng cho rẻ (Pool). Một số tenant cần cô lập dữ liệu (hợp đồng, quy mô, pháp lý), nên cần DB riêng (Silo). Code nghiệp vụ không được phụ thuộc vào việc tenant thuộc loại nào.

**Quyết định.**
- **Catalog DB** cố định: `tenants` (`is_active`, `is_isolated_db`, `connection_string`) và `tenant_domains` (domain → tenant).
- **Xác định tenant** ở middleware theo `Host`. Header `X-Tenant-Host` chỉ được dùng khi bật trong cấu hình, cho dev/demo. Cache trong bộ nhớ: tìm thấy thì 10 phút, không thấy thì 1 phút. Domain lạ → 404, tenant bị khoá → 403.
- **`TenantDbContext`** (base class cho DbContext của module):
  - Connection string theo tenant: Silo → DB riêng, Pool → `DefaultShared`. Dùng connection string, không dùng `NpgsqlDataSource` chung, để mỗi DB có pool Npgsql riêng.
  - Global query filter có tên `Tenant` trên mọi entity `IMustHaveTenant`, áp dụng cả trong DB Silo (an toàn hai lớp).
  - `SaveChanges` tự gán `TenantId`, chặn sửa `TenantId`, chặn ghi khi chưa có tenant.

**Hệ quả.**
- (+) Module không viết code tenant; chuyển tenant Pool ↔ Silo không đổi code.
- (+) An toàn mặc định: không có tenant thì không đọc được gì, không ghi được gì.
- (−) Thay đổi trong Catalog có độ trễ tối đa bằng TTL cache trên mỗi instance.
- (−) Chuyển dữ liệu một tenant từ Pool sang Silo cần script riêng.
- (−) Chưa có xác thực: domain quyết định tenant. Phải thêm auth ràng buộc user ↔ tenant trước production.

---

## ADR-004: Migration và seed chạy bằng lệnh riêng, có advisory lock

**Bối cảnh.** Migrate lúc khởi động gây race giữa các instance, chậm khởi động, và một lỗi migration làm sập mọi instance. Với multi-tenant còn phải migrate cả DB chung lẫn từng DB Silo.

**Quyết định.** Host **không bao giờ** migrate hay seed lúc khởi động. `dotnet Host.dll migrate`:
1. tạo DB Catalog nếu thiếu, giữ `pg_advisory_lock` trên DB Catalog;
2. migrate Catalog, seed 2 tenant demo nếu `Tenancy:SeedDemoTenants` bật;
3. migrate mọi module trên DB chung;
4. migrate mọi module trên DB của từng tenant Silo (tự tạo DB nếu chưa có).

Seed nằm trong lệnh này vì DB riêng của tenant Silo vừa seed phải được migrate ngay sau đó.

**Hệ quả.**
- (+) An toàn khi nhiều job chạy đồng thời; instance khởi động nhanh, không cần quyền DDL.
- (+) Thêm tenant Silo = thêm dòng vào Catalog + chạy `migrate`.
- (−) Deploy phải có bước migrate; migration phải tương thích ngược (expand → deploy → contract).
- (−) Nhiều tenant Silo → migrate tuần tự, lâu dần (extension-points: chạy song song).

---

## ADR-005: Mỗi module một schema, trong mỗi DB tenant

**Bối cảnh.** Cần cô lập dữ liệu giữa các module nhưng vận hành đơn giản.

**Quyết định.** Trong mỗi DB (chung hoặc Silo), mỗi module có schema riêng (`products`, ...) và bảng `__ef_migrations_history` riêng trong schema đó. Module không truy vấn schema của module khác; trao đổi qua Contracts.

**Hệ quả.**
- (+) Migration của các module độc lập; ranh giới module rõ cả ở tầng DB.
- (+) Tách một module ra service riêng sau này chỉ cần tách schema.
- (−) Không có transaction xuyên module "chính thống"; cần nhất quán thì dùng outbox/event.
