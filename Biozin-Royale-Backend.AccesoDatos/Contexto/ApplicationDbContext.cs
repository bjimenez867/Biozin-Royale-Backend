using Microsoft.EntityFrameworkCore;

namespace Biozin_Royale_Backend.AccesoDatos.Contexto
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }
    }
}