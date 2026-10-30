# Hướng dẫn tạo module mới

Module = một mảng nghiệp vụ tách riêng trong code: project, DbContext, schema DB, endpoint. Mọi khai báo của module nằm trong một class `IModule`.

Ví dụ: tạo module **Orders** (id `orders`) cần dùng module `products`. Cách nhanh nhất là copy module `Products` rồi đổi tên.

## 0. Quy ước

| Thứ | Quy ước | Ví dụ |
|---|---|---|
| Thư mục | `src/Modules/<Tên>` và `src/Modules/<Tên>.Contracts` | `src/Modules/Orders` |
| Project / assembly | `Modules.<Tên>` và `Modules.<Tên>.Contracts` (**bắt buộc** tiền tố `Modules.`: loader quét theo tên này) | `Modules.Orders` |
| Id module | chữ thường, số, `-`, bắt đầu bằng chữ; URL là `/api/{id}` | `orders` → `/api/orders` |
| Schema DB | chữ thường, số, `_` (thường = id) | `orders` |

## 1. Copy và đổi tên

```bash
cp -r src/Modules/Products           src/Modules/Orders
cp -r src/Modules/Products.Contracts src/Modules/Orders.Contracts
rm -rf src/Modules/Orders/bin src/Modules/Orders/obj src/Modules/Orders/Data/Migrations
rm -rf src/Modules/Orders.Contracts/bin src/Modules/Orders.Contracts/obj
mv src/Modules/Orders/Modules.Products.csproj                     src/Modules/Orders/Modules.Orders.csproj
mv src/Modules/Orders.Contracts/Modules.Products.Contracts.csproj src/Modules/Orders.Contracts/Modules.Orders.Contracts.csproj
```

Sau đó trong hai thư mục mới:

- Đổi `Products`/`Product` → `Orders`/`Order` trong tên file, class, namespace.
- Trong `Modules.Orders.csproj`, sửa đường dẫn `..\Products.Contracts\...` → `..\Orders.Contracts\Modules.Orders.Contracts.csproj`.
- Đổi tên route trong `WithName(...)` (ví dụ `orders.get`): tên route phải duy nhất trong toàn ứng dụng.

## 2. Lớp module

```csharp
public sealed class OrdersModule : IModule
{
    public string Id => "orders";

    public IReadOnlyList<string> DependsOn => ["products"];   // bỏ dòng này nếu không phụ thuộc module nào

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<OrdersDbContext>(OrdersDbContext.Schema);
        // services.AddScoped<...>();  service nội bộ của module
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // `endpoints` đã có tiền tố /api/orders và đã qua bước xác định tenant.
        endpoints.MapGet("", ...);            // → GET /api/orders
    }
}
```

- Mỗi assembly module có **đúng một** class implement `IModule`, với constructor không tham số.
- `DependsOn` thiếu module hoặc tạo vòng lặp → Host **không khởi động** và log rõ lý do.

## 3. Entity và DbContext (tenant tự động)

```csharp
internal sealed class Order : IMustHaveTenant      // dữ liệu thuộc một tenant
{
    public Guid Id { get; set; }
    public decimal Total { get; set; }
    public Guid TenantId { get; set; }               // KHÔNG tự gán: TenantDbContext lo
}

internal sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    public const string Schema = "orders";

    public DbSet<Order> Orders => Set<Order>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)   // thay cho OnModelCreating
    {
        modelBuilder.HasDefaultSchema(Schema);       // bắt buộc: mọi bảng nằm trong schema của module
        modelBuilder.Entity<Order>(o =>
        {
            o.ToTable("orders");
            o.Property(x => x.TenantId).HasColumnName("tenant_id");
            o.HasIndex(x => x.TenantId);             // hầu hết query đều lọc theo tenant
        });
    }
}

// Cho `dotnet ef migrations add` (không cần kết nối DB)
internal sealed class OrdersDbContextFactory : IDesignTimeDbContextFactory<OrdersDbContext>
{
    public OrdersDbContext CreateDbContext(string[] args) =>
        new(ModuleDbContextExtensions.CreateDesignTimeOptions<OrdersDbContext>(OrdersDbContext.Schema),
            ModuleDbContextExtensions.DesignTimeTenantContext);
}
```

Bạn **được miễn**:

- Không viết `.Where(x => x.TenantId == ...)`: filter tự thêm vào mọi entity `IMustHaveTenant`.
- Không chọn database: tenant Silo tự đi vào DB riêng, tenant Pool vào DB chung.
- Không gán `TenantId`: tự gán khi thêm mới; sửa `TenantId` hoặc ghi khi chưa có tenant sẽ ném lỗi.

Cần lưu ý:

- Muốn thêm query filter riêng (ví dụ soft delete), dùng filter **có tên** (`HasQueryFilter("SoftDelete", ...)`) để không đè filter `Tenant`.
- `IgnoreQueryFilters()` bỏ **cả** filter tenant. Chỉ dùng khi thật sự cần, và đã hiểu hệ quả.
- Bảng của module có mặt trong **mọi** DB tenant (chung và riêng). Dữ liệu dùng chung cho mọi tenant (danh mục hệ thống...) không nên để ở đây.
- Chỉ truy cập bảng trong schema của mình, không join hay tạo FK sang schema module khác.

## 4. Migration

```bash
dotnet tool restore
dotnet build src/Modules/Orders        # dotnet-ef cần project đã restore, nếu không sẽ báo "Unable to retrieve project metadata"
dotnet ef migrations add InitialOrders --project src/Modules/Orders --output-dir Data/Migrations
```

Không cần đăng ký gì thêm: lệnh `migrate` tự áp migration này lên DB chung và lên DB riêng của từng tenant Silo.

## 5. Đưa module vào solution và Host

```bash
dotnet sln Shell.slnx add src/Modules/Orders src/Modules/Orders.Contracts
dotnet add src/Host/Host.csproj                         reference src/Modules/Orders/Modules.Orders.csproj
dotnet add tests/Shell.UnitTests/Shell.UnitTests.csproj reference src/Modules/Orders/Modules.Orders.csproj
```

- Reference trong **Host** chỉ để DLL được copy ra output; Host không gọi code module trực tiếp.
- Reference trong **Shell.UnitTests** để architecture test kiểm tra module mới. Thiếu bước này thì test vẫn xanh nhưng module **không được kiểm tra**.
- Dockerfile không cần sửa.

## 6. Dùng module khác (qua Contracts)

Orders chỉ được tham chiếu `Modules.Products.Contracts`, **không** tham chiếu `Modules.Products`:

```bash
dotnet add src/Modules/Orders/Modules.Orders.csproj reference src/Modules/Products.Contracts/Modules.Products.Contracts.csproj
```

Mẫu chuẩn: module được dùng công bố interface trong Contracts và tự đăng ký implementation.

```csharp
// Modules.Products.Contracts
public interface IProductQueries
{
    Task<ProductDto?> FindAsync(Guid id, CancellationToken ct);
}

// Modules.Products (internal), trong ProductsModule.ConfigureServices:
services.AddScoped<IProductQueries, ProductQueries>();   // dùng ProductsDbContext → vẫn đúng tenant

// Modules.Orders: inject IProductQueries như service bình thường.
```

Nhớ khai báo `"products"` trong `DependsOn` của Orders.

## 7. Chạy và kiểm tra

```bash
docker compose up -d postgres
dotnet build Shell.slnx
dotnet run --project src/Host -- migrate     # migrate Orders trên DB chung và DB riêng của tenant B
dotnet run --project src/Host                # log "Loaded modules in order: [products, orders]"
curl localhost:5080/api/orders -H "X-Tenant-Host: tenant-a.localhost"
dotnet test --solution Shell.slnx
```

## Checklist

- [ ] Project tên `Modules.<Tên>` và `Modules.<Tên>.Contracts`
- [ ] Một class `: IModule` với `Id` duy nhất, `DependsOn` đúng
- [ ] Entity dữ liệu tenant implement `IMustHaveTenant`
- [ ] DbContext kế thừa `TenantDbContext`, cấu hình trong `ConfigureModel`, có `HasDefaultSchema`
- [ ] Đăng ký bằng `AddModuleDbContext`, có design-time factory
- [ ] Migration đã tạo trong `Data/Migrations`
- [ ] Đã thêm vào `Shell.slnx`, `Host.csproj`, `Shell.UnitTests.csproj`
- [ ] Chỉ tham chiếu `*.Contracts` của module khác
- [ ] `dotnet test` xanh
