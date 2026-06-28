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
        public DbSet<UserStatistics> UserStatistics => Set<UserStatistics>();
        public DbSet<GamesHistory> GamesHistory => Set<GamesHistory>();

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
            });
        }
    }
}