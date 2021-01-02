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

        public virtual DbSet<CDR> CDRs { get; set; }
        public virtual DbSet<SIPAccount> SIPAccounts { get; set; }
        public virtual DbSet<SIPCall> SIPCalls { get; set; }
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

            modelBuilder.Entity<CDR>(entity =>
            {
                entity.ToTable("CDR");

                entity.Property(e => e.ID).ValueGeneratedNever();

                entity.Property(e => e.AnsweredReason)
                    .HasMaxLength(512)
                    .IsUnicode(false);

                entity.Property(e => e.CallID)
                    .IsRequired()
                    .HasMaxLength(256)
                    .IsUnicode(false);

                entity.Property(e => e.Direction)
                    .IsRequired()
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.DstHost)
                    .IsRequired()
                    .HasMaxLength(128)
                    .IsUnicode(false);

                entity.Property(e => e.DstUri)
                    .IsRequired()
                    .HasMaxLength(1024)
                    .IsUnicode(false);

                entity.Property(e => e.DstUser)
                    .HasMaxLength(128)
                    .IsUnicode(false);

                entity.Property(e => e.FromHeader)
                    .HasMaxLength(1024)
                    .IsUnicode(false);

                entity.Property(e => e.FromName)
                    .HasMaxLength(128)
                    .IsUnicode(false);

                entity.Property(e => e.FromUser)
                    .HasMaxLength(128)
                    .IsUnicode(false);

                entity.Property(e => e.HungupReason)
                    .HasMaxLength(512)
                    .IsUnicode(false);

                entity.Property(e => e.InProgressReason)
                    .HasMaxLength(512)
                    .IsUnicode(false);

                entity.Property(e => e.LocalSocket)
                    .IsRequired()
                    .HasMaxLength(64)
                    .IsUnicode(false);

                entity.Property(e => e.RemoteSocket)
                    .IsRequired()
                    .HasMaxLength(64)
                    .IsUnicode(false);
            });

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

            modelBuilder.Entity<SIPCall>(entity =>
            {
                entity.Property(e => e.ID).ValueGeneratedNever();

                entity.Property(e => e.CallID)
                    .IsRequired()
                    .HasMaxLength(128)
                    .IsUnicode(false);

                entity.Property(e => e.Direction)
                    .IsRequired()
                    .HasMaxLength(3)
                    .IsUnicode(false);

                entity.Property(e => e.LocalTag)
                    .IsRequired()
                    .HasMaxLength(64)
                    .IsUnicode(false);

                entity.Property(e => e.LocalUserField)
                    .IsRequired()
                    .HasMaxLength(512)
                    .IsUnicode(false);

                entity.Property(e => e.ProxySIPSocket)
                    .HasMaxLength(64)
                    .IsUnicode(false);

                entity.Property(e => e.RemoteTag)
                    .IsRequired()
                    .HasMaxLength(64)
                    .IsUnicode(false);

                entity.Property(e => e.RemoteTarget)
                    .IsRequired()
                    .HasMaxLength(256)
                    .IsUnicode(false);

                entity.Property(e => e.RemoteUserField)
                    .IsRequired()
                    .HasMaxLength(512)
                    .IsUnicode(false);

                entity.Property(e => e.RouteSet)
                    .HasMaxLength(512)
                    .IsUnicode(false);

                entity.HasOne(d => d.CDR)
                    .WithMany(p => p.SIPCalls)
                    .HasForeignKey(d => d.CDRID)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("FK__SIPCalls__CDRID__3F115E1A");
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
