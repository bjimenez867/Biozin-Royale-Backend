using Microsoft.EntityFrameworkCore;
using Biozin_Royale_Backend.Dominio.Entities;

namespace Biozin_Royale_Backend.AccesoDatos.Contexto
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public DbSet<Profile> Profiles => Set<Profile>();
        public DbSet<Wallet> Wallets => Set<Wallet>();
        public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
        public DbSet<UserStatistics> UserStatistics => Set<UserStatistics>();
        public DbSet<GamesHistory> GamesHistory => Set<GamesHistory>();
        public DbSet<Promotion> Promotions => Set<Promotion>();
        public DbSet<PromotionClaim> PromotionClaims => Set<PromotionClaim>();
        public DbSet<StaffMember> StaffMembers => Set<StaffMember>();
        public DbSet<UserBlock> UserBlocks => Set<UserBlock>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Profile>(entity =>
            {
                entity.ToTable("profiles");
                entity.HasKey(p => p.Id);
                entity.Property(p => p.Id).HasColumnName("id");
                entity.Property(p => p.UserId).HasColumnName("user_id");
                entity.Property(p => p.Username).HasColumnName("username");
                entity.Property(p => p.DisplayName).HasColumnName("display_name");
                entity.Property(p => p.AvatarId).HasColumnName("avatar_id");
                entity.Property(p => p.IsGuest).HasColumnName("is_guest");
                entity.Property(p => p.Status).HasColumnName("status");
                entity.Property(p => p.CreatedAt).HasColumnName("created_at");
                entity.Property(p => p.UpdatedAt).HasColumnName("updated_at");
                entity.Property(p => p.Phone).HasColumnName("phone");
                entity.Property(p => p.Email).HasColumnName("email");
                entity.Property(p => p.Country).HasColumnName("country");
                entity.Property(p => p.Birthdate).HasColumnName("birthdate");
                entity.Property(p => p.Password).HasColumnName("password");
                entity.Property(p => p.ResetCode).HasColumnName("reset_code");
                entity.Property(p => p.ResetCodeExpiresAt).HasColumnName("reset_code_expires_at");
                entity.Property(p => p.EmailVerified).HasColumnName("email_verified");
                entity.Property(p => p.VerifyCode).HasColumnName("verify_code");
                entity.Property(p => p.VerifyCodeExpiresAt).HasColumnName("verify_code_expires_at");
                entity.Property(p => p.PinHash).HasColumnName("pin_hash");
                entity.Property(p => p.PinEnabled).HasColumnName("pin_enabled");
                entity.HasIndex(p => p.UserId).IsUnique();
                entity.HasIndex(p => p.Username).IsUnique();
            });

            modelBuilder.Entity<Wallet>(entity =>
            {
                entity.ToTable("wallets");
                entity.HasKey(w => w.Id);
                entity.Property(w => w.Id).HasColumnName("id");
                entity.Property(w => w.UserId).HasColumnName("user_id");
                entity.Property(w => w.Balance).HasColumnName("balance").HasPrecision(18, 2);
                entity.Property(w => w.CreatedAt).HasColumnName("created_at");
                entity.Property(w => w.UpdatedAt).HasColumnName("updated_at");
                entity.HasIndex(w => w.UserId).IsUnique();
            });

            modelBuilder.Entity<WalletTransaction>(entity =>
            {
                entity.ToTable("wallet_transactions");
                entity.HasKey(t => t.Id);
                entity.Property(t => t.Id).HasColumnName("id");
                entity.Property(t => t.WalletId).HasColumnName("wallet_id");
                entity.Property(t => t.TransactionType).HasColumnName("transaction_type");
                entity.Property(t => t.Status).HasColumnName("status");
                entity.Property(t => t.Amount).HasColumnName("amount").HasPrecision(18, 2);
                entity.Property(t => t.BalanceBefore).HasColumnName("balance_before").HasPrecision(18, 2);
                entity.Property(t => t.BalanceAfter).HasColumnName("balance_after").HasPrecision(18, 2);
                entity.Property(t => t.ReferenceType).HasColumnName("reference_type");
                entity.Property(t => t.ReferenceId).HasColumnName("reference_id");
                entity.Property(t => t.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
                entity.Property(t => t.CreatedAt).HasColumnName("created_at");
            });

            modelBuilder.Entity<UserStatistics>(entity =>
            {
                entity.HasNoKey();
                entity.ToView("user_statistics");
                entity.Property(s => s.UserId).HasColumnName("user_id");
                entity.Property(s => s.PartidasJugadas).HasColumnName("partidas_jugadas");
                entity.Property(s => s.PartidasGanadas).HasColumnName("partidas_ganadas");
                entity.Property(s => s.ApostadoTotal).HasColumnName("apostado_total");
                entity.Property(s => s.GananciasNetas).HasColumnName("ganancias_netas");
            });

            modelBuilder.Entity<GamesHistory>(entity =>
            {
                entity.ToTable("bets");
                entity.HasKey(b => b.Id);
                entity.Property(b => b.Id).HasColumnName("id");
                entity.Property(b => b.UserId).HasColumnName("user_id");
                entity.Property(b => b.GameType).HasColumnName("game_type");
                entity.Property(b => b.RoundId).HasColumnName("round_id");
                entity.Property(b => b.Amount).HasColumnName("amount");
                entity.Property(b => b.Payout).HasColumnName("payout");
                entity.Property(b => b.Profit).HasColumnName("profit").ValueGeneratedOnAddOrUpdate();
                entity.Property(b => b.Result).HasColumnName("result");
                entity.Property(b => b.Status).HasColumnName("status");
                entity.Property(b => b.CreatedAt).HasColumnName("created_at");
                entity.Property(b => b.Selections).HasColumnName("selections");
                entity.Property(b => b.SettledAt).HasColumnName("settled_at");
            });

            modelBuilder.Entity<Promotion>(entity =>
            {
                entity.ToTable("promotions");
                entity.HasKey(p => p.Id);
                entity.Property(p => p.Id).HasColumnName("id");
                entity.Property(p => p.Title).HasColumnName("title");
                entity.Property(p => p.Description).HasColumnName("description");
                entity.Property(p => p.IsActive).HasColumnName("is_active");
                entity.Property(p => p.Amount).HasColumnName("amount").HasPrecision(18, 2);
                entity.Property(p => p.EndsAt).HasColumnName("ends_at");
                entity.Property(p => p.CreatedAt).HasColumnName("created_at");
            });

            modelBuilder.Entity<PromotionClaim>(entity =>
            {
                entity.ToTable("promotion_claims");
                entity.HasKey(c => c.Id);
                entity.Property(c => c.Id).HasColumnName("id");
                entity.Property(c => c.PromotionId).HasColumnName("promotion_id");
                entity.Property(c => c.UserId).HasColumnName("user_id");
                entity.Property(c => c.Status).HasColumnName("status");
                entity.Property(c => c.ClaimedAt).HasColumnName("claimed_at");
                entity.Property(c => c.CompletedAt).HasColumnName("completed_at");
            });
            modelBuilder.Entity<StaffMember>(entity =>
            {
                entity.ToTable("staff_members");
                entity.HasKey(s => s.Id);
                entity.Property(s => s.Id).HasColumnName("id");
                entity.Property(s => s.Username).HasColumnName("username");
                entity.Property(s => s.DisplayName).HasColumnName("display_name");
                entity.Property(s => s.Email).HasColumnName("email");
                entity.Property(s => s.Phone).HasColumnName("phone");
                entity.Property(s => s.PasswordHash).HasColumnName("password_hash");
                entity.Property(s => s.Status).HasColumnName("status");
                entity.Property(s => s.MustChangePassword).HasColumnName("must_change_password");
                entity.Property(s => s.ResetCode).HasColumnName("reset_code");
                entity.Property(s => s.ResetCodeExpiresAt).HasColumnName("reset_code_expires_at");
                entity.Property(s => s.CreatedBy).HasColumnName("created_by");
                entity.Property(s => s.CreatedAt).HasColumnName("created_at");
                entity.Property(s => s.UpdatedAt).HasColumnName("updated_at");
                entity.HasIndex(s => s.Email).IsUnique();
                entity.HasIndex(s => s.Username).IsUnique();
            });

            modelBuilder.Entity<UserBlock>(entity =>
            {
                entity.ToTable("user_blocks");
                entity.HasKey(b => b.Id);
                entity.Property(b => b.Id).HasColumnName("id");
                entity.Property(b => b.ProfileId).HasColumnName("profile_id");
                entity.Property(b => b.BlockedBy).HasColumnName("blocked_by");
                entity.Property(b => b.Reason).HasColumnName("reason");
                entity.Property(b => b.Message).HasColumnName("message");
                entity.Property(b => b.BlockedAt).HasColumnName("blocked_at");
                entity.Property(b => b.UnblockedAt).HasColumnName("unblocked_at");
                entity.Property(b => b.UnblockedBy).HasColumnName("unblocked_by");
                entity.Property(b => b.IsActive).HasColumnName("is_active");
            });
        }
    }
}