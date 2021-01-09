using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

#nullable disable

namespace demo.DataAccess
{
    public partial class SIPAssetsDbContext : DbContext
    {
        //public SIPAssetsDbContext()
        //{
        //}

        public SIPAssetsDbContext(DbContextOptions<SIPAssetsDbContext> options)
            : base(options)
        {
        }

        public virtual DbSet<CDR> CDRs { get; set; }
        public virtual DbSet<SIPAccount> SIPAccounts { get; set; }
        public virtual DbSet<SIPCall> SIPCalls { get; set; }
        public virtual DbSet<SIPDialPlan> SIPDialPlans { get; set; }
        public virtual DbSet<SIPDomain> SIPDomains { get; set; }
        public virtual DbSet<SIPRegistrarBinding> SIPRegistrarBindings { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlServer("Name=ConnectionStrings.SIPAssets");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasAnnotation("Relational:Collation", "Latin1_General_CI_AS");

            modelBuilder.Entity<CDR>(entity =>
            {
                entity.ToTable("CDR");

                entity.Property(e => e.ID).ValueGeneratedNever();

                entity.Property(e => e.AnsweredAt).HasColumnType("datetime");

                entity.Property(e => e.AnsweredReason)
                    .HasMaxLength(512)
                    .IsUnicode(false);

                entity.Property(e => e.CallID)
                    .IsRequired()
                    .HasMaxLength(256)
                    .IsUnicode(false);

                entity.Property(e => e.Created).HasColumnType("datetime");

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

                entity.Property(e => e.HungupAt).HasColumnType("datetime");

                entity.Property(e => e.HungupReason)
                    .HasMaxLength(512)
                    .IsUnicode(false);

                entity.Property(e => e.InProgressAt).HasColumnType("datetime");

                entity.Property(e => e.InProgressReason)
                    .HasMaxLength(512)
                    .IsUnicode(false);

                entity.Property(e => e.Inserted).HasColumnType("datetime");

                entity.Property(e => e.LocalSocket)
                    .HasMaxLength(64)
                    .IsUnicode(false);

                entity.Property(e => e.RemoteSocket)
                    .HasMaxLength(64)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<SIPAccount>(entity =>
            {
                entity.HasIndex(e => new { e.SIPUsername, e.DomainID }, "UQ__SIPAccou__6E36B5B5A326D306")
                    .IsUnique();

                entity.Property(e => e.ID).ValueGeneratedNever();

                entity.Property(e => e.Inserted)
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("(sysdatetime())");

                entity.Property(e => e.SIPPassword)
                    .IsRequired()
                    .HasMaxLength(32);

                entity.Property(e => e.SIPUsername)
                    .IsRequired()
                    .HasMaxLength(32);

                entity.HasOne(d => d.Domain)
                    .WithMany(p => p.SIPAccounts)
                    .HasForeignKey(d => d.DomainID)
                    .HasConstraintName("FK__SIPAccoun__Domai__719CDDE7");

                entity.HasOne(d => d.SIPDialPlan)
                    .WithMany(p => p.SIPAccounts)
                    .HasForeignKey(d => d.SIPDialPlanID)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("FK__SIPAccoun__SIPDi__72910220");
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

                entity.Property(e => e.Inserted).HasColumnType("datetime");

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
                    .HasConstraintName("FK__SIPCalls__CDRID__681373AD");
            });

            modelBuilder.Entity<SIPDialPlan>(entity =>
            {
                entity.HasIndex(e => e.DialPlanName, "UQ__SIPDialP__C45E274AD3F90B04")
                    .IsUnique();

                entity.Property(e => e.ID).ValueGeneratedNever();

                entity.Property(e => e.DialPlanName)
                    .IsRequired()
                    .HasMaxLength(64)
                    .IsUnicode(false);

                entity.Property(e => e.DialPlanScript).IsUnicode(false);

                entity.Property(e => e.Inserted).HasColumnType("datetime");

                entity.Property(e => e.LastUpdate).HasColumnType("datetime");
            });

            modelBuilder.Entity<SIPDomain>(entity =>
            {
                entity.HasIndex(e => e.Domain, "UQ__SIPDomai__FD349E538EF10FC5")
                    .IsUnique();

                entity.Property(e => e.ID).ValueGeneratedNever();

                entity.Property(e => e.AliasList).HasMaxLength(1024);

                entity.Property(e => e.Domain)
                    .IsRequired()
                    .HasMaxLength(128);

                entity.Property(e => e.Inserted)
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("(sysdatetime())");
            });

            modelBuilder.Entity<SIPRegistrarBinding>(entity =>
            {
                entity.Property(e => e.ID).ValueGeneratedNever();

                entity.Property(e => e.ContactURI)
                    .IsRequired()
                    .HasMaxLength(767);

                entity.Property(e => e.ExpiryTime).HasColumnType("datetime");

                entity.Property(e => e.LastUpdate).HasColumnType("datetime");

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
                    .HasConstraintName("FK__SIPRegist__SIPAc__756D6ECB");
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
