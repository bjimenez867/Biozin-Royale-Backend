namespace Biozin_Royale_Backend.Dominio.InterfacesLN
{

    public interface IEmailService
    {

        
        Task EnviarCredencialesStaffAsync(
            string correoDestino,
            string nombre,
            string correoEmpresarial,
            string password,
            string rol,
            string correoRemitente
        );
        

    }

}