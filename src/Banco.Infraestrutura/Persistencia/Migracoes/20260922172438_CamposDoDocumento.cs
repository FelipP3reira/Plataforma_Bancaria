using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banco.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class CamposDoDocumento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ConfiancaDoTexto",
                table: "Documentos",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CamposDoDocumento",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nome = table.Column<int>(type: "int", nullable: false),
                    ValorLido = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Confianca = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    Origem = table.Column<int>(type: "int", nullable: false),
                    Observacao = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CamposDoDocumento", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CamposDoDocumento_Documentos_DocumentoId",
                        column: x => x.DocumentoId,
                        principalTable: "Documentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CamposDoDocumento_DocumentoId_Nome",
                table: "CamposDoDocumento",
                columns: new[] { "DocumentoId", "Nome" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CamposDoDocumento");

            migrationBuilder.DropColumn(
                name: "ConfiancaDoTexto",
                table: "Documentos");
        }
    }
}
