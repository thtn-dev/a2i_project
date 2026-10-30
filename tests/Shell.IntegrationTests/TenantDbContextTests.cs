using Microsoft.EntityFrameworkCore;
using Shell.Abstractions.Tenancy;

namespace Shell.IntegrationTests;

/// <summary>Pool isolation rules of <see cref="TenantDbContext"/>, on a dedicated database.</summary>
[Collection(PostgresCollection.Name)]
public class TenantDbContextTests(PostgresFixture db) : IAsyncLifetime
{
    private static readonly TenantInfo TenantA = new(Guid.CreateVersion7(), "a.test", false, null, true);
    private static readonly TenantInfo TenantB = new(Guid.CreateVersion7(), "b.test", false, null, true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await using var context = CreateContext(null);
        await context.Database.EnsureCreatedAsync(Ct);
    }

    [Fact]
    public async Task Insert_stamps_the_current_tenant()
    {
        await using var context = CreateContext(TenantA);
        var note = new Note { Id = Guid.CreateVersion7(), Text = "stamped" };

        context.Notes.Add(note);
        await context.SaveChangesAsync(Ct);

        Assert.Equal(TenantA.Id, note.TenantId);
    }

    [Fact]
    public async Task Queries_only_see_rows_of_the_current_tenant()
    {
        var text = $"shared-{Guid.NewGuid()}";
        foreach (var tenant in new[] { TenantA, TenantB })
        {
            await using var writer = CreateContext(tenant);
            writer.Notes.Add(new Note { Id = Guid.CreateVersion7(), Text = text });
            await writer.SaveChangesAsync(Ct);
        }

        // Several context instances on the same cached model: the filter must use each instance's tenant.
        await using var asA = CreateContext(TenantA);
        await using var asB = CreateContext(TenantB);
        await using var asNobody = CreateContext(null);

        Assert.Equal([TenantA.Id], await asA.Notes.Where(n => n.Text == text).Select(n => n.TenantId).ToListAsync(Ct));
        Assert.Equal([TenantB.Id], await asB.Notes.Where(n => n.Text == text).Select(n => n.TenantId).ToListAsync(Ct));
        Assert.Empty(await asNobody.Notes.Where(n => n.Text == text).ToListAsync(Ct));
        Assert.Equal(2, await asNobody.Notes.IgnoreQueryFilters().CountAsync(n => n.Text == text, Ct));
    }

    [Fact]
    public async Task Changing_TenantId_is_rejected()
    {
        await using var context = CreateContext(TenantA);
        var note = new Note { Id = Guid.CreateVersion7(), Text = "move me" };
        context.Notes.Add(note);
        await context.SaveChangesAsync(Ct);

        note.TenantId = TenantB.Id;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(Ct));
        Assert.Contains("Cannot change TenantId", ex.Message);
    }

    [Fact]
    public async Task Adding_a_row_for_another_tenant_is_rejected()
    {
        await using var context = CreateContext(TenantA);
        context.Notes.Add(new Note { Id = Guid.CreateVersion7(), Text = "foreign", TenantId = TenantB.Id });

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Saving_without_a_tenant_fails_clearly()
    {
        await using var context = CreateContext(null);
        context.Notes.Add(new Note { Id = Guid.CreateVersion7(), Text = "orphan" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(Ct));
        Assert.Contains("no tenant has been resolved", ex.Message);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private NotesDbContext CreateContext(TenantInfo? tenant) =>
        new(new DbContextOptionsBuilder<NotesDbContext>().UseNpgsql(db.ForDatabase("shell_rules")).Options, new FixedTenant(tenant));

    private sealed class FixedTenant(TenantInfo? tenant) : ITenantContext
    {
        public TenantInfo? Current => tenant;
    }

    public sealed class Note : IMustHaveTenant
    {
        public Guid Id { get; set; }

        public required string Text { get; set; }

        public Guid TenantId { get; set; }
    }

    public sealed class NotesDbContext(DbContextOptions<NotesDbContext> options, ITenantContext tenantContext)
        : TenantDbContext(options, tenantContext)
    {
        public DbSet<Note> Notes => Set<Note>();

        protected override void ConfigureModel(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("notes");
            modelBuilder.Entity<Note>().ToTable("notes");
        }
    }
}
