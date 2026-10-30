# Điểm mở rộng

Những thứ **cố ý chưa làm**, kèm nơi gắn vào khi cần. Nguyên tắc: thêm vào `Host/Program.cs` hoặc `Shell.Core`, hạn chế thay đổi `Shell.Abstractions` (mọi module phụ thuộc vào nó).

## 1. Identity / phân quyền

**Bắt buộc trước production.** Hiện tại ai gọi đúng domain đều đọc/ghi được dữ liệu tenant đó.

```csharp
// Host/Program.cs
builder.Services.AddAuthentication(...).AddJwtBearer(...);   // hoặc cookie/OIDC
builder.Services.AddAuthorization();

app.UseTenantResolution();
app.UseAuthentication();
app.UseAuthorization();
```

- **Ràng buộc user ↔ tenant**: sau khi xác thực, kiểm tra claim tenant của user (ví dụ `tenant_id`) khớp `ITenantContext.Current.Id`, lệch thì trả 403. Đặt kiểm tra này trong một middleware ngay sau `UseAuthorization`.
- Tắt `Tenancy:AllowHostHeaderOverride` ở production. Nếu cần chọn tenant qua header (mobile app, API key), chỉ chấp nhận khi header khớp claim.
- Endpoint module: `.RequireAuthorization(...)` trong `MapEndpoints`. Muốn mọi endpoint module mặc định cần đăng nhập: thêm `.RequireAuthorization()` cho group trong `ShellServiceCollectionExtensions.MapModules`.

## 2. Quản lý tenant

- **API quản trị tenant** (tạo tenant, gán domain, khoá, chuyển Pool ↔ Silo): thêm endpoint trong Shell.Core dùng `CatalogDbContext`, có phân quyền riêng. Tạo tenant Silo xong thì chạy `migrate` (hoặc gọi `MigrationRunner` cho đúng tenant đó).
- **Đồng bộ cache tenant tức thời giữa instance**: hiện mỗi instance tự hết hạn sau `CacheMinutes`. Muốn tức thời: Postgres `LISTEN/NOTIFY` (hoặc Redis pub/sub), khi nhận thông báo thì `IMemoryCache.Remove($"tenant:{domain}")`.
- **Connection string Silo**: đang lưu plain text trong `catalog.tenants`. Nên lưu tên secret (Key Vault, AWS Secrets Manager, Vault) và resolve trong `TenantStore` (kết quả được cache sẵn).
- **Nhiều tenant Silo**: `MigrationRunner` đang migrate tuần tự. Có thể chạy song song có giới hạn (`Parallel.ForEachAsync` + `MaxDegreeOfParallelism`) hoặc tách thành job theo từng tenant.
- **Chuyển tenant Pool → Silo**: script copy dữ liệu theo `tenant_id` từ DB chung sang DB mới, rồi đổi `is_isolated_db`/`connection_string`.
- **Feature flag theo tenant**: thêm bảng `tenant_features` trong Catalog, đưa vào `TenantInfo`, module kiểm tra qua `ITenantContext`.

## 3. Event bus / Outbox

- **Event trong process**: event (record) trong `Modules.X.Contracts`; thêm `IEventPublisher` + `IEventHandler<T>` vào `Shell.Abstractions`, implement trong `Shell.Core`.
- **Outbox**: bảng `outbox` trong schema của từng module, ghi cùng transaction với dữ liệu nghiệp vụ, kèm `tenant_id`. `BackgroundService` đọc outbox của DB chung và **mọi DB Silo** (danh sách lấy từ Catalog), set `TenantContext` trước khi xử lý.
- **Broker ngoài (RabbitMQ, Kafka...)**: chỉ thay phần phát đi của outbox; message luôn mang `TenantId`.

## 4. Cache

- Cache dữ liệu nghiệp vụ: `HybridCache`/`IDistributedCache`. **Key phải có TenantId** (`$"{tenantId}:products:{id}"`) để không lộ dữ liệu giữa tenant.
- Không lưu state người dùng trong memory của process: sẽ phá scale ngang.

## 5. Observability

- **Log**: Serilog đọc section `Serilog` trong cấu hình. Thêm sink (Seq, Elasticsearch, OTLP...) bằng cấu hình + package sink. Log trong request đã có `TenantId` (log scope).
- **Tracing/metrics**: OpenTelemetry trong `Program.cs` (`AddAspNetCoreInstrumentation`, `AddNpgsql`), thêm tag `tenant.id` trong `TenantResolutionMiddleware`.
- **Health**: thêm check bằng `AddHealthChecks().AddCheck<T>(name, tags: ["ready"])`. Không nên check từng DB Silo trong readiness (một tenant lỗi không nên làm mọi instance "not ready").

## 6. Khác

| Nhu cầu | Gắn ở đâu |
|---|---|
| Cấu hình riêng cho module | Trong `ConfigureServices`: `services.AddOptions<OrdersOptions>().BindConfiguration("Modules:Orders").ValidateOnStart()` |
| Background job của module | `services.AddHostedService<...>()`; job phải duyệt tenant (từ Catalog) và set `TenantContext` trong từng scope |
| Header `X-Forwarded-*` từ Nginx | `app.UseForwardedHeaders(...)` đầu pipeline. Cần nếu proxy đổi `Host` |
| Rate limiting theo tenant | `AddRateLimiter` với partition key = `TenantId` |
| API versioning, OpenAPI production | Thêm vào Host |
| Kubernetes | Image hiện có: liveness `/health/live`, readiness `/health/ready`, `migrate` thành Job, `terminationGracePeriodSeconds` > `ShutdownTimeoutSeconds` |
