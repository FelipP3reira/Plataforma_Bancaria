using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banco.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class Extrato : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Lancamentos_ContaId_CriadoEm",
                table: "Lancamentos");

            migrationBuilder.CreateIndex(
                name: "IX_Lancamentos_ContaId_CriadoEm_Sequencia",
                table: "Lancamentos",
                columns: new[] { "ContaId", "CriadoEm", "Sequencia" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Lancamentos_ContaId_CriadoEm_Sequencia",
                table: "Lancamentos");

            migrationBuilder.CreateIndex(
                name: "IX_Lancamentos_ContaId_CriadoEm",
                table: "Lancamentos",
                columns: new[] { "ContaId", "CriadoEm" });
        }
    }
}
