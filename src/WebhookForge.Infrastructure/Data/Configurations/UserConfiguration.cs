using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebhookForge.Domain.Entities;
using WebhookForge.Domain.Enums;

namespace WebhookForge.Infrastructure.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email)       .IsRequired().HasMaxLength(256);
        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(512);
        builder.Property(u => u.DisplayName) .IsRequired().HasMaxLength(100);
        builder.Property(u => u.IsActive)    .IsRequired().HasDefaultValue(true);
        builder.Property(u => u.CreatedAt)   .IsRequired().HasDefaultValueSql("GETUTCDATE()");

        // Store AiProvider enum as its name string ("Claude"/"Gemini"/"Groq") not as integer
        builder.Property(u => u.AiProvider)
               .HasConversion<string>()
               .HasMaxLength(20);

        builder.Property(u => u.AiApiKey).HasMaxLength(500);

        builder.HasIndex(u => u.Email).IsUnique().HasDatabaseName("UX_Users_Email");
    }
}
