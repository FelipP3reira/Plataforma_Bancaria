using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banco.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class FilaDeExtracao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Confianca",
                table: "Documentos",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConteudoExtraido",
                table: "Documentos",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExtraidoEm",
                table: "Documentos",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseAte",
                table: "Documentos",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Tentativas",
                table: "Documentos",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "UltimoErro",
                table: "Documentos",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_Estado_LeaseAte",
                table: "Documentos",
                columns: new[] { "Estado", "LeaseAte" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documentos_Estado_LeaseAte",
                table: "Documentos");

            migrationBuilder.DropColumn(
                name: "Confianca",
                table: "Documentos");

            migrationBuilder.DropColumn(
                name: "ConteudoExtraido",
                table: "Documentos");

            migrationBuilder.DropColumn(
                name: "ExtraidoEm",
                table: "Documentos");

            migrationBuilder.DropColumn(
                name: "LeaseAte",
                table: "Documentos");

            migrationBuilder.DropColumn(
                name: "Tentativas",
                table: "Documentos");

            migrationBuilder.DropColumn(
                name: "UltimoErro",
                table: "Documentos");
        }
    }
}
