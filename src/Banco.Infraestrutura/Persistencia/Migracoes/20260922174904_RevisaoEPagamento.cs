using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banco.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class RevisaoEPagamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LancamentoDoPagamentoId",
                table: "Documentos",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PagoEm",
                table: "Documentos",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CorrigidoEm",
                table: "CamposDoDocumento",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrigidoPor",
                table: "CamposDoDocumento",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValorCorrigido",
                table: "CamposDoDocumento",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_LancamentoDoPagamentoId",
                table: "Documentos",
                column: "LancamentoDoPagamentoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documentos_LancamentoDoPagamentoId",
                table: "Documentos");

            migrationBuilder.DropColumn(
                name: "LancamentoDoPagamentoId",
                table: "Documentos");

            migrationBuilder.DropColumn(
                name: "PagoEm",
                table: "Documentos");

            migrationBuilder.DropColumn(
                name: "CorrigidoEm",
                table: "CamposDoDocumento");

            migrationBuilder.DropColumn(
                name: "CorrigidoPor",
                table: "CamposDoDocumento");

            migrationBuilder.DropColumn(
                name: "ValorCorrigido",
                table: "CamposDoDocumento");
        }
    }
}
