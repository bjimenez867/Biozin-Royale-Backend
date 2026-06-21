using Microsoft.EntityFrameworkCore;
using Biozin_Royale_Backend.Dominio.Entities;

namespace Biozin_Royale_Backend.AccesoDatos.Contexto
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public DbSet<Profile> Profiles => Set<Profile>();
        public DbSet<UserStatistics> UserStatistics => Set<UserStatistics>();

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
        }
    }
}