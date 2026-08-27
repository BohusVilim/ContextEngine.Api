using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using ContextEngine.Api.Models.Chunk;
using ContextEngine.Api.Models.Identity;
using System.Text.Json;

namespace ContextEngine.Api.Data
{
    /// <summary>
    /// EF Core database context for the ContextEngine SQLite store. Also owns the ASP.NET Core
    /// Identity schema (users, roles, tokens, etc. - via <see cref="IdentityDbContext{TUser}"/>) so
    /// application data and account data live in the same database.
    /// </summary>
    public class ContextEngineDbContext : IdentityDbContext<ApplicationUser>
    {
        public ContextEngineDbContext(DbContextOptions<ContextEngineDbContext> options)
            : base(options)
        {
        }

        /// <summary>All stored document chunks.</summary>
        public DbSet<Chunk> Chunks { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Builds the Identity schema (AspNetUsers, AspNetRoles, etc.) first, so the
            // configuration below only has to add the Chunk-specific pieces on top of it.
            base.OnModelCreating(modelBuilder);

            // SQLite has no array/map column type, so List<string>/Dictionary<string,string> properties
            // are stored as JSON text. These comparers tell EF Core how to detect changes in the
            // deserialized collections (by value, not by reference) when tracking entities.
            var stringListComparer = new ValueComparer<List<string>>(
                (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
                v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
                v => v.ToList());

            var stringDictComparer = new ValueComparer<Dictionary<string, string>>(
                (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
                v => v.Aggregate(0, (hash, kv) => HashCode.Combine(hash, kv.Key.GetHashCode(), kv.Value.GetHashCode())),
                v => v.ToDictionary(kv => kv.Key, kv => kv.Value));

            // Same reasoning as the comparers above, for the fixed-length float[] embedding vector
            // (see Chunk.Embedding / IEmbeddingService) that also has to round-trip through a JSON
            // TEXT column.
            var floatArrayComparer = new ValueComparer<float[]>(
                (a, b) => (a ?? Array.Empty<float>()).SequenceEqual(b ?? Array.Empty<float>()),
                v => v.Aggregate(0, (hash, f) => HashCode.Combine(hash, f.GetHashCode())),
                v => v.ToArray());

            modelBuilder.Entity<Chunk>(entity =>
            {
                entity.HasKey(c => c.Id);

                // Topics/Tags/Metadata are serialized to/from a single JSON TEXT column each.
                // Note: this means they can't be filtered or indexed at the SQL level.
                entity.Property(c => c.Topics)
                    .HasConversion(
                        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                        v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new())
                    .Metadata.SetValueComparer(stringListComparer);

                entity.Property(c => c.Tags)
                    .HasConversion(
                        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                        v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new())
                    .Metadata.SetValueComparer(stringListComparer);

                entity.Property(c => c.Metadata)
                    .HasConversion(
                        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                        v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null) ?? new())
                    .Metadata.SetValueComparer(stringDictComparer);

                entity.Property(c => c.Embedding)
                    .HasConversion(
                        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                        v => JsonSerializer.Deserialize<float[]>(v, (JsonSerializerOptions?)null) ?? Array.Empty<float>())
                    .Metadata.SetValueComparer(floatArrayComparer);

                // Self-referencing tree: deleting a chunk cascades to delete its whole subtree.
                entity.HasOne(c => c.Parent)
                    .WithMany(c => c.Children)
                    .HasForeignKey(c => c.ParentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
