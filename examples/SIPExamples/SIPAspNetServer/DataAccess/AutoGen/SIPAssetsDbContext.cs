using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

#nullable disable

namespace SIPAspNetServer.DataAccess
{
    public partial class SIPAssetsDbContext : DbContext
    {
        public SIPAssetsDbContext()
        {
        }

        public SIPAssetsDbContext(DbContextOptions<SIPAssetsDbContext> options)
            : base(options)
        {
        }

        public virtual DbSet<SIPAccount> SIPAccounts { get; set; }
        public virtual DbSet<SIPDomain> SIPDomains { get; set; }
        public virtual DbSet<SIPRegistrarBinding> SIPRegistrarBindings { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see http://go.microsoft.com/fwlink/?LinkId=723263.
                optionsBuilder.UseSqlServer("Data Source=localhost;Initial Catalog=SIPAssets;Persist Security Info=True;User ID=appuser;Password=password");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasAnnotation("Relational:Collation", "Latin1_General_CI_AS");

            modelBuilder.Entity<SIPAccount>(entity =>
            {
                entity.HasIndex(e => new { e.SIPUsername, e.DomainID }, "UQ__SIPAccou__6E36B5B5E2EC7FF1")
                    .IsUnique();

                entity.Property(e => e.ID).ValueGeneratedNever();

                entity.Property(e => e.Inserted).HasDefaultValueSql("(sysdatetimeoffset())");

                entity.Property(e => e.SIPPassword)
                    .IsRequired()
                    .HasMaxLength(32);

                entity.Property(e => e.SIPUsername)
                    .IsRequired()
                    .HasMaxLength(32);

                entity.HasOne(d => d.Domain)
                    .WithMany(p => p.SIPAccounts)
                    .HasForeignKey(d => d.DomainID)
                    .HasConstraintName("FK__SIPAccoun__Domai__1AD3FDA4");
            });

            modelBuilder.Entity<SIPDomain>(entity =>
            {
                entity.HasIndex(e => e.Domain, "UQ__SIPDomai__FD349E53D9BC0D1B")
                    .IsUnique();

                entity.Property(e => e.ID).ValueGeneratedNever();

                entity.Property(e => e.AliasList).HasMaxLength(1024);

                entity.Property(e => e.Domain)
                    .IsRequired()
                    .HasMaxLength(128);

                entity.Property(e => e.Inserted).HasDefaultValueSql("(sysdatetimeoffset())");
            });

            modelBuilder.Entity<SIPRegistrarBinding>(entity =>
            {
                entity.Property(e => e.ID).ValueGeneratedNever();

                entity.Property(e => e.ContactURI)
                    .IsRequired()
                    .HasMaxLength(767);

                entity.Property(e => e.MangledContactURI)
                    .HasMaxLength(767)
                    .IsUnicode(false);

                entity.Property(e => e.ProxySIPSocket).HasMaxLength(64);

                entity.Property(e => e.RegistrarSIPSocket).HasMaxLength(64);

                entity.Property(e => e.RemoteSIPSocket)
                    .IsRequired()
                    .HasMaxLength(64);

                entity.Property(e => e.UserAgent).HasMaxLength(1024);

                entity.HasOne(d => d.SIPAccount)
                    .WithMany(p => p.SIPRegistrarBindings)
                    .HasForeignKey(d => d.SIPAccountID)
                    .HasConstraintName("FK__SIPRegist__SIPAc__1DB06A4F");
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
