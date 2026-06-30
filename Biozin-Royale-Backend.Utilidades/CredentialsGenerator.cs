using System.Text;

namespace Biozin_Royale_Backend.Utilidades;

public class CredentialsGenerator
{
        private const string SupportDomain= "@support.biozin.cr";
        private const string AdminDomain = "@admin.biozin.cr";

        private static readonly char[] lowerCase = "abcdefghijklmnopqrstuvwxyz".ToCharArray();
        private static readonly char[] upperCase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();
        private static readonly char[] digits = "0123456789".ToCharArray();
        private static readonly char[] specials = "!@#$%&*".ToCharArray();

        
        
        
        /// Genera la parte local del email a partir del nombre y apellido paterno.
        public static string GenerateBaseEmail(string name, string firtLastName)
        {
            var firtName = name.Trim().Split(' ')[0];
            return $"{Normalize(firtName)}.{Normalize(firtLastName)}";
        }

        
    
        /// Construye el email completo con el dominio del soporte.
        /// Si sufijo > 0, lo agrega al final: "juan.perez2@support.biozin.cr"
        public static string BuildSupportEmail(string baseEmail, int suffix = 0)
        {
            var local = suffix > 0 ? $"{baseEmail}{suffix}" : baseEmail;
            return $"{local}{SupportDomain}";
        }

        
        
        
        
        /// Construye el email completo con el dominio de administradores.
        /// Si sufijo > 0, lo agrega al final: "juan.perez2@admin.biozin.cr"
        public static string BuildEmailAdmin(string baseEmail, int suffix = 0)
        {
            var local = suffix > 0 ? $"{baseEmail}{suffix}" : baseEmail;
            return $"{local}{AdminDomain}";
        }


        /// Determina el rol de la cuenta a partir del dominio del correo: "admin",
        /// "soporte" o "user" para cualquier otro dominio.
        public static string DetectRole(string email)
        {
            var normalizado = email.Trim().ToLowerInvariant();
            if (normalizado.EndsWith(AdminDomain)) return "admin";
            if (normalizado.EndsWith(SupportDomain)) return "soporte";
            return "user";
        }

        
        
        
        
        /// Genera la base del email a partir del nombreCompleto con formato "Nombre PrimerApellido SegundoApellido".
        public static string GenerateBaseEmailWithFullName(string fullName)
        {
            var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var name = parts.Length > 0 ? parts[0] : string.Empty;
            var firtLastName = parts.Length > 1 ? parts[1] : string.Empty;
            return GenerateBaseEmail(name, firtLastName);
        }
        
        

        /// Genera un username a partir del nombre completo (sin tildes, minúsculas, sin espacios).
        /// Si sufijo > 0, lo agrega al final para resolver colisiones de unicidad.
        public static string GenerateUsername(string fullName, int suffix = 0)
        {
            var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var baseUsername = string.Join("", parts.Select(Normalize));
            return suffix > 0 ? $"{baseUsername}{suffix}" : baseUsername;
        }

    
        
        
        
        /// Genera una contraseña aleatoria de 10 caracteres con mayúscula,
        /// minúscula, dígito y carácter especial garantizados.
        public static string GeneratePassword()
        {
            var rng = new Random();
            var chars = new char[10];

            // Garantizar al menos uno de cada tipo
            chars[0] = upperCase[rng.Next(upperCase.Length)];
            chars[1] = lowerCase[rng.Next(lowerCase.Length)];
            chars[2] = digits[rng.Next(digits.Length)];
            chars[3] = specials[rng.Next(specials.Length)];

            // Rellenar el resto con caracteres mixtos
            var todos = new char[lowerCase.Length + upperCase.Length + digits.Length + specials.Length];
            lowerCase.CopyTo(todos, 0);
            upperCase.CopyTo(todos, lowerCase.Length);
            digits.CopyTo(todos, lowerCase.Length + upperCase.Length);
            specials.CopyTo(todos, lowerCase.Length + upperCase.Length + digits.Length);

            for (int i = 4; i < 10; i++)
                chars[i] = todos[rng.Next(todos.Length)];

            // Mezclar para que los tipos no queden en orden fijo
            return new string(chars.OrderBy(_ => rng.Next()).ToArray());
        }
        
        

        private static string Normalize(string texto)
        {
            var normalizado = texto.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in normalizado)
            {
                var categoria = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (categoria != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            return sb.ToString()
                     .Normalize(NormalizationForm.FormC)
                     .ToLower()
                     .Replace('ñ', 'n');
        }
    }
