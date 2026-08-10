using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Biozin_Royale_Backend.AccesoDatos.Migrations
{
    /// <inheritdoc />
    public partial class AddInternalRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"CREATE TABLE IF NOT EXISTS internal_requests (
                    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                    request_number serial NOT NULL,
                    requested_by uuid NOT NULL REFERENCES staff_members(id),
                    target_admin_id uuid NOT NULL REFERENCES staff_members(id),
                    subject text NOT NULL,
                    description text NOT NULL,
                    status text NOT NULL DEFAULT 'nuevo',
                    created_at timestamptz NOT NULL DEFAULT now(),
                    updated_at timestamptz NOT NULL DEFAULT now()
                );"
            );

            migrationBuilder.Sql(
                @"CREATE TABLE IF NOT EXISTS internal_request_messages (
                    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                    internal_request_id uuid NOT NULL REFERENCES internal_requests(id),
                    sender_id uuid NOT NULL,
                    sender_role text NOT NULL,
                    sender_name text NOT NULL,
                    body text NOT NULL,
                    created_at timestamptz NOT NULL DEFAULT now()
                );"
            );

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS ix_internal_request_messages_request_created ON internal_request_messages (internal_request_id, created_at);"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS internal_request_messages;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS internal_requests;");
        }
    }
}
