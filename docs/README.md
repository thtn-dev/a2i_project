# Shell — modular monolith multi-tenant (.NET 10)

Bộ khung để xây hệ thống nghiệp vụ **multi-tenant** dạng **modular monolith**:

- **Module** = một mảng nghiệp vụ tách riêng trong code (project, DbContext, schema, migration riêng), khai báo bằng một class `IModule`.
- **Multi-tenant Hybrid**: mỗi tenant hoặc dùng **DB chung** (Pool, cách ly bằng `TenantId` + global query filter) hoặc **DB riêng** (Silo, cách ly bằng connection string). Module không phải viết dòng code nào về tenant.
- Tenant xác định theo **domain** của request (`tenant1.app.com`, `shop.khachhang.com`), có cache.
- Scale ngang: instance stateless, migration chạy bằng lệnh riêng, health check, log JSON, Docker.

Đọc tiếp: [architecture.md](architecture.md) · [module-guide.md](module-guide.md) · [adr.md](adr.md) · [extension-points.md](extension-points.md)

## Yêu cầu

- .NET SDK 10.0
- Docker (cho `docker compose` và integration test)

## Quick start

### Chạy cả cụm (2 instance + Nginx + Postgres)

```bash
docker compose up --build -d
```

Thứ tự: `postgres` → `migrate` (tạo/migrate DB Catalog, seed 2 tenant demo, migrate DB chung và DB riêng của tenant B, rồi thoát) → `host1`, `host2` → `nginx` tại http://localhost:8080.

Hai tenant demo:

| Domain | Kiểu | Dữ liệu nằm ở |
|---|---|---|
| `tenant-a.localhost` | Pool | DB `shell_shared` (chung) |
| `tenant-b.localhost` | Silo | DB `shell_shared_tenant_b` (riêng) |

```bash
# Chọn tenant bằng header X-Tenant-Host ...
curl -X POST localhost:8080/api/products -H "X-Tenant-Host: tenant-a.localhost" \
     -H "Content-Type: application/json" -d '{"name":"A-Laptop","price":1200}'
# ... hoặc bằng Host header thật (giống production)
curl -X POST localhost:8080/api/products -H "Host: tenant-b.localhost" \
     -H "Content-Type: application/json" -d '{"name":"B-Phone","price":800}'

curl localhost:8080/api/products -H "X-Tenant-Host: tenant-a.localhost"   # chỉ thấy A-Laptop
curl localhost:8080/api/products -H "X-Tenant-Host: tenant-b.localhost"   # chỉ thấy B-Phone
curl -i localhost:8080/api/products -H "X-Tenant-Host: unknown.localhost" # 404

# Kiểm tra dữ liệu nằm ở DB nào
docker compose exec postgres psql -U shell -d shell_shared          -c "select name, tenant_id from products.products"
docker compose exec postgres psql -U shell -d shell_shared_tenant_b -c "select name, tenant_id from products.products"

docker compose down -v   # dọn dẹp (xoá cả dữ liệu)
```

### Chạy local + Swagger

```bash
docker compose up -d postgres                  # Postgres ở localhost:5432
dotnet tool restore                            # dotnet-ef (local tool)
dotnet run --project src/Host -- migrate       # tạo DB shell_dev_*, seed 2 tenant, rồi thoát
dotnet run --project src/Host                  # http://localhost:5080/swagger
```

Trong Swagger UI, mỗi endpoint có ô **X-Tenant-Host**: nhập `tenant-a.localhost` hoặc `tenant-b.localhost`.
Môi trường Development dùng DB riêng (`shell_dev_catalog`, `shell_dev_shared`, `shell_dev_shared_tenant_b`) để không lẫn với cụm docker compose.

### Test

```bash
dotnet test --solution Shell.slnx              # unit + architecture + integration (cần Docker)
```

## Cấu trúc thư mục

```
src/
  Host/                    Program.cs (pipeline, lệnh migrate, Swagger), appsettings
  Shell.Abstractions/      Thứ duy nhất module tham chiếu từ shell:
    IModule.cs               hợp đồng module
    ModuleDbContextExtensions.cs  AddModuleDbContext (chọn DB theo tenant)
    Tenancy/                 IMustHaveTenant, TenantInfo, ITenantContext, TenantDbContext
  Shell.Core/
    Modules/                 ModuleLoader, ModuleCatalog (thứ tự dependency, fail fast)
    Tenancy/                 CatalogDbContext, TenantStore (cache), TenantResolutionMiddleware, DemoTenantSeeder
    Data/                    MigrationRunner (lệnh migrate), DatabaseHealthCheck
  Modules/
    Products/                Module mẫu: Product (IMustHaveTenant), ProductsDbContext, GET/POST /api/products
    Products.Contracts/      DTO công khai cho module khác
tests/
  Shell.UnitTests/         ModuleCatalog + architecture test (NetArchTest)
  Shell.IntegrationTests/  Testcontainers: quy tắc TenantDbContext, migrate, Pool vs Silo qua HTTP
deploy/nginx.conf          Load balancer cho docker compose
Dockerfile, docker-compose.yml, Directory.Packages.props, dotnet-tools.json
```

## Endpoint

| Endpoint | Cần tenant | Mô tả |
|---|---|---|
| `GET /health/live` | Không | Process còn sống |
| `GET /health/ready` | Không | DB Catalog và DB chung trả lời, lỗi → 503 |
| `/swagger` | Không | Chỉ ở Development |
| `/api/{moduleId}/...` | **Có** | Endpoint nghiệp vụ, ví dụ `GET/POST /api/products` |

Request cần tenant: domain không có trong Catalog → **404**, tenant bị khoá (`is_active = false`) → **403**.

## Cấu hình

`appsettings.json` → `appsettings.{Environment}.json` → biến môi trường (`__` thay cho `:`). Sai cấu hình → Host dừng ngay khi khởi động.

| Khoá | Mặc định | Ý nghĩa |
|---|---|---|
| `ConnectionStrings:Catalog` | *(bắt buộc)* | DB master: danh sách tenant, domain, connection string Silo |
| `ConnectionStrings:DefaultShared` | *(bắt buộc)* | DB chung của mọi tenant Pool (mỗi module một schema) |
| `Tenancy:AllowHostHeaderOverride` | `false` | Cho phép header `X-Tenant-Host` thay Host thật. **Chỉ bật khi dev/demo** |
| `Tenancy:CacheMinutes` | 10 | Cache tenant đã tìm thấy (mỗi instance) |
| `Tenancy:NotFoundCacheSeconds` | 60 | Cache domain không tồn tại (chống spam DB) |
| `Tenancy:SeedDemoTenants` | `false` | Lệnh `migrate` tạo 2 tenant demo nếu Catalog trống |
| `Shell:ShutdownTimeoutSeconds` | 30 | Thời gian chờ request đang chạy khi nhận SIGTERM |
| `Serilog:MinimumLevel:Default` | Information | Log ra console dạng JSON |

## Lưu ý vận hành

- ⚠️ **Chưa có xác thực.** Bất kỳ ai gọi đúng domain đều đọc/ghi được dữ liệu tenant đó. Với `AllowHostHeaderOverride = true` thì chỉ cần đổi header. Xem [extension-points.md](extension-points.md#1-identity--phân-quyền).
- Thêm/khoá tenant hoặc đổi domain trong Catalog: mỗi instance thấy thay đổi sau tối đa `CacheMinutes` (domain mới bị 404 trước đó: tối đa `NotFoundCacheSeconds`).
- Thêm tenant Silo: tạo dòng trong Catalog rồi chạy `migrate`. Lệnh này tự tạo DB riêng và migrate mọi module trên DB đó.
- Migration **không** tự chạy khi khởi động: chạy `dotnet Host.dll migrate` (hoặc `docker run <image> migrate`) trước khi deploy.
- Khi `migrate` tạo database mới, EF Core log vài dòng `Failed executing DbCommand` / `An error occurred using the connection...`: đó là lúc EF kiểm tra DB và bảng lịch sử *trước khi tạo*. Migration vẫn thành công, lần sau không còn.
- Connection string của tenant Silo đang lưu dạng plain text trong Catalog. Xem phần secret store trong [extension-points.md](extension-points.md).
