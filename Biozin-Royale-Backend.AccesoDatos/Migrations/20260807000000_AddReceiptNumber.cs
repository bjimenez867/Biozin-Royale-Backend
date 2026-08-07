using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Biozin_Royale_Backend.AccesoDatos.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE wallet_transactions ADD COLUMN IF NOT EXISTS receipt_number TEXT;"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE wallet_transactions DROP COLUMN IF EXISTS receipt_number;"
            );
        }
    }
}
