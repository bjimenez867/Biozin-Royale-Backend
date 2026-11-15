using Microsoft.EntityFrameworkCore;
using Biozin_Royale_Backend.AccesoDatos.Contexto;
using Biozin_Royale_Backend.Dominio.Entities;
using Biozin_Royale_Backend.Dominio.InterfacesAD;

namespace Biozin_Royale_Backend.AccesoDatos.Repositories.Implementaciones;

public class UnitWorkEF : IUnitWork
{
    private readonly ApplicationDbContext _contexto;

    public IRepositoryAD<Profile> Profiles { get; }
    public IRepositoryAD<Wallet> Wallets { get; }
    public IRepositoryAD<WalletTransaction> WalletTransactions { get; }
    public IRepositoryAD<UserStatistics> Statistics { get; }
    public IRepositoryAD<GamesHistory> GamesHistory { get; }
    public IRepositoryAD<Promotion> Promotions { get; }
    public IRepositoryAD<PromotionClaim> PromotionClaims { get; }
    public IRepositoryAD<StaffMember> StaffMembers { get; }
    public IRepositoryAD<UserBlock> UserBlocks { get; }
    public IRepositoryAD<Session> Sessions { get; }
    public IRepositoryAD<SecurityEvent> SecurityEvents { get; }
    public IRepositoryAD<PaymentMethod> PaymentMethods { get; }
    public IRepositoryAD<SupportTicket> SupportTickets { get; }
    public IRepositoryAD<TicketMessage> TicketMessages { get; }
    public IRepositoryAD<Avatar> Avatars { get; }
    public IRepositoryAD<InternalRequest> InternalRequests { get; }
    public IRepositoryAD<InternalRequestMessage> InternalRequestMessages { get; }
    public IRepositoryAD<StoreCategory> StoreCategories { get; }
    public IRepositoryAD<StoreProduct> StoreProducts { get; }
    public IRepositoryAD<StoreProductVariant> StoreProductVariants { get; }
    public IRepositoryAD<StoreOrder> StoreOrders { get; }
    public IRepositoryAD<StoreOrderItem> StoreOrderItems { get; }
    public IRepositoryAD<StorePayment> StorePayments { get; }
    public IRepositoryAD<StoreOrderEvent> StoreOrderEvents { get; }
    public IRepositoryAD<StoreNotification> StoreNotifications { get; }
    public IRepositoryAD<StoreSetting> StoreSettings { get; }
    public IRepositoryAD<StoreReturnRequest> StoreReturnRequests { get; }

    public UnitWorkEF(ApplicationDbContext contexto)
    {
        _contexto = contexto;
        Profiles = new RepositoryAD<Profile>(contexto);
        Wallets = new RepositoryAD<Wallet>(contexto);
        WalletTransactions = new RepositoryAD<WalletTransaction>(contexto);
        Statistics = new RepositoryAD<UserStatistics>(contexto);
        GamesHistory = new RepositoryAD<GamesHistory>(contexto);
        Promotions = new RepositoryAD<Promotion>(contexto);
        PromotionClaims = new RepositoryAD<PromotionClaim>(contexto);
        StaffMembers = new RepositoryAD<StaffMember>(contexto);
        UserBlocks = new RepositoryAD<UserBlock>(contexto);
        Sessions = new RepositoryAD<Session>(contexto);
        SecurityEvents = new RepositoryAD<SecurityEvent>(contexto);
        PaymentMethods = new RepositoryAD<PaymentMethod>(contexto);
        SupportTickets = new RepositoryAD<SupportTicket>(contexto);
        TicketMessages = new RepositoryAD<TicketMessage>(contexto);
        Avatars = new RepositoryAD<Avatar>(contexto);
        InternalRequests = new RepositoryAD<InternalRequest>(contexto);
        InternalRequestMessages = new RepositoryAD<InternalRequestMessage>(contexto);
        StoreCategories = new RepositoryAD<StoreCategory>(contexto);
        StoreProducts = new RepositoryAD<StoreProduct>(contexto);
        StoreProductVariants = new RepositoryAD<StoreProductVariant>(contexto);
        StoreOrders = new RepositoryAD<StoreOrder>(contexto);
        StoreOrderItems = new RepositoryAD<StoreOrderItem>(contexto);
        StorePayments = new RepositoryAD<StorePayment>(contexto);
        StoreOrderEvents = new RepositoryAD<StoreOrderEvent>(contexto);
        StoreNotifications = new RepositoryAD<StoreNotification>(contexto);
        StoreSettings = new RepositoryAD<StoreSetting>(contexto);
        StoreReturnRequests = new RepositoryAD<StoreReturnRequest>(contexto);
    }

    /// SQL parametrizado. Se usa en la tienda para actualizaciones con condición
    /// (stock >= n, balance >= n) que deben ser atómicas.
    public async Task<int> EjecutarSqlAsync(FormattableString sql)
        => await _contexto.Database.ExecuteSqlInterpolatedAsync(sql);

    public async Task<IDbTransaccion> IniciarTransaccionAsync()
        => new DbTransaccionEF(await _contexto.Database.BeginTransactionAsync());

    private sealed class DbTransaccionEF : IDbTransaccion
    {
        private readonly Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction _tx;
        public DbTransaccionEF(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx) => _tx = tx;
        public Task CommitAsync()   => _tx.CommitAsync();
        public Task RollbackAsync() => _tx.RollbackAsync();
        public ValueTask DisposeAsync() => _tx.DisposeAsync();
    }

    public int Completar()
    {
        return _contexto.SaveChanges();
    }

    public async Task<int> CompletarAsync()
        => await _contexto.SaveChangesAsync();

    /// auth.users es la tabla interna de Supabase Auth; no se mapea como entidad EF
    /// porque el esquema lo administra Supabase, no este proyecto. Solo se inserta
    /// la fila mínima necesaria para satisfacer la FK de profiles.user_id en el
    /// registro manual (sin pasar por la API de GoTrue/Supabase Auth).
    /// GoTrue (el servicio de Auth de Supabase) espera estas columnas de tokens como
    /// '' (string vacío), nunca NULL: si quedan en NULL, cualquier operación de Auth
    /// que escanee esta fila (ej. login con Google buscando si el email ya existe)
    /// falla con "converting NULL to string is unsupported".
    /// instance_id tampoco puede quedar en NULL por la misma razón: GoTrue lo escanea
    /// como UUID no-nullable. Se usa el UUID cero, la convención de Supabase cuando no
    /// hay múltiples instancias.
    private static readonly Guid InstanciaGoTrue = Guid.Empty;

    public async Task InsertarUsuarioAuthAsync(Guid id, string email)
    {
        await _contexto.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO auth.users (
                id, instance_id, aud, role, email, created_at, updated_at, is_sso_user, is_anonymous,
                confirmation_token, recovery_token, email_change_token_new,
                email_change_token_current, email_change, phone_change,
                phone_change_token, reauthentication_token
            )
            VALUES (
                {id}, {InstanciaGoTrue}, 'authenticated', 'authenticated', {email}, now(), now(), false, false,
                '', '', '', '', '', '', '', ''
            )");
    }

    /// Verifica si ya existe un usuario en auth.users con este correo (no-SSO), sin
    /// importar si tiene o no un Profile asociado. Necesario porque un login con Google
    /// puede dejar una fila en auth.users sin Profile (si el sync nunca se completó), y
    /// el registro manual chocaría con la restricción "users_email_partial_key" al
    /// intentar insertar el mismo correo de nuevo.
    public async Task<bool> ExisteUsuarioAuthAsync(string email)
    {
        var conteo = await _contexto.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(*)::int AS \"Value\" FROM auth.users WHERE email = {0} AND is_sso_user = false",
                email)
            .SingleAsync();
        return conteo > 0;
    }

    public void Dispose()
    {
        _contexto.Dispose();
    }
}
