using A2I.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace A2I.Infrastructure.Database.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("Customers");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id)
            .HasColumnType("uuid")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.Email).IsRequired().HasMaxLength(255);
        b.Property(x => x.StripeCustomerId).HasMaxLength(100);
        b.Property(x => x.FirstName).HasMaxLength(100);
        b.Property(x => x.LastName).HasMaxLength(100);
        b.Property(x => x.Phone).HasMaxLength(20);
        b.Property(x => x.CompanyName).HasMaxLength(200);
        b.Property(x => x.TaxId).HasMaxLength(50);
        b.Property(x => x.Currency).HasMaxLength(3).HasDefaultValue("usd");
        b.Property(x => x.UserId).IsRequired().HasMaxLength(256);

        // JSONB
        b.Property(x => x.Metadata).HasColumnType("jsonb");

        // Indexes
        b.HasIndex(x => x.StripeCustomerId).IsUnique();
        b.HasIndex(x => x.Email);
        b.HasIndex(x => x.UserId);

        // Soft delete column
        b.Property(x => x.IsDeleted).HasDefaultValue(false);
    }
}

public sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> b)
    {
        b.ToTable("Plans");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id)
            .HasColumnType("uuid")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.Name).IsRequired().HasMaxLength(100);
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.StripePriceId).IsRequired().HasMaxLength(100);
        b.Property(x => x.StripeProductId).IsRequired().HasMaxLength(100);

        b.Property(x => x.Amount).HasColumnType("numeric(18,2)");
        b.Property(x => x.Currency).HasMaxLength(3).HasDefaultValue("usd");

        // Enum to string
        b.Property(x => x.BillingInterval)
            .HasConversion<string>()
            .HasMaxLength(16);

        b.Property(x => x.IntervalCount).HasDefaultValue(1);
        b.Property(x => x.TrialPeriodDays);
        b.Property(x => x.IsActive).HasDefaultValue(true);
        b.Property(x => x.SortOrder).HasDefaultValue(0);

        // JSONB
        b.Property(x => x.Features).HasColumnType("jsonb");
        b.Property(x => x.Metadata).HasColumnType("jsonb");

        // Indexes
        b.HasIndex(x => x.StripePriceId).IsUnique();
        b.HasIndex(x => x.StripeProductId);
        // filtered/partial index (only active plans)
        b.HasIndex(x => x.IsActive).HasFilter("\"IsActive\" = TRUE");

        b.Property(x => x.IsDeleted).HasDefaultValue(false);
    }
}

public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> b)
    {
        b.ToTable("Subscriptions");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id)
            .HasColumnType("uuid")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.CustomerId).HasColumnType("uuid");
        b.Property(x => x.PlanId).HasColumnType("uuid");

        b.Property(x => x.StripeSubscriptionId).IsRequired().HasMaxLength(100);

        // Enum to string
        b.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        b.Property(x => x.Quantity).HasDefaultValue(1);
        b.Property(x => x.CancelAtPeriodEnd).HasDefaultValue(false);

        // JSONB
        b.Property(x => x.Metadata).HasColumnType("jsonb");

        // FKs
        b.HasOne(x => x.Customer)
            .WithMany(c => c.Subscriptions)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict); // Cascade delete NO

        b.HasOne(x => x.Plan)
            .WithMany(p => p.Subscriptions)
            .HasForeignKey(x => x.PlanId)
            .OnDelete(DeleteBehavior.Restrict); // Restrict

        // Indexes
        b.HasIndex(x => x.StripeSubscriptionId).IsUnique();
        b.HasIndex(x => new { x.CustomerId, x.Status });
        b.HasIndex(x => x.CurrentPeriodEnd);

        b.Property(x => x.IsDeleted).HasDefaultValue(false);
    }
}

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.ToTable("Invoices");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id)
            .HasColumnType("uuid")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.CustomerId).HasColumnType("uuid");
        b.Property(x => x.SubscriptionId).HasColumnType("uuid");

        b.Property(x => x.StripeInvoiceId).IsRequired().HasMaxLength(100);
        b.Property(x => x.StripePaymentIntentId).HasMaxLength(100);
        b.Property(x => x.InvoiceNumber).HasMaxLength(50);

        // Enum to string
        b.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        b.Property(x => x.Amount).HasColumnType("numeric(18,2)");
        b.Property(x => x.AmountPaid).HasColumnType("numeric(18,2)");
        b.Property(x => x.AmountDue).HasColumnType("numeric(18,2)");
        b.Property(x => x.Currency).HasMaxLength(3);

        // JSONB
        b.Property(x => x.Metadata).HasColumnType("jsonb");

        b.Property(x => x.HostedInvoiceUrl).HasMaxLength(500);
        b.Property(x => x.InvoicePdf).HasMaxLength(500);

        // FKs
        b.HasOne(x => x.Customer)
            .WithMany(c => c.Invoices)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict); // Cascade delete NO

        b.HasOne(x => x.Subscription)
            .WithMany(s => s.Invoices)
            .HasForeignKey(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.SetNull); // Set null

        // Indexes
        b.HasIndex(x => x.StripeInvoiceId).IsUnique();
        b.HasIndex(x => new { x.CustomerId, x.PaidAt });
        b.HasIndex(x => x.SubscriptionId);
        b.HasIndex(x => new { x.Status, x.DueDate });

        b.Property(x => x.IsDeleted).HasDefaultValue(false);
    }
}