using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Biozin_Royale_Backend.AccesoDatos.Migrations
{
    /// <inheritdoc />
    public partial class AddPromotionTargetUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE promotions ADD COLUMN IF NOT EXISTS target_user_id uuid NULL;"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE promotions DROP COLUMN IF EXISTS target_user_id;"
            );
        }
    }
}
