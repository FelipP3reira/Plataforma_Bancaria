using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banco.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class EstadoDaConta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // O padrao gerado seria 0, que nao corresponde a nenhum EstadoDaConta — toda
            // conta que ja existia ficaria num estado que o enum nao conhece. Conta aberta
            // antes desta migracao estava ativa, entao o padrao e Ativa (1).
            migrationBuilder.AddColumn<int>(
                name: "Estado",
                table: "Contas",
                type: "int",
                nullable: false,
                defaultValue: (int)Banco.Dominio.Contas.EstadoDaConta.Ativa);

            migrationBuilder.AddColumn<long>(
                name: "SequenciaDeEstado",
                table: "Contas",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "MudancasDeEstadoDaConta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequencia = table.Column<long>(type: "bigint", nullable: false),
                    De = table.Column<int>(type: "int", nullable: false),
                    Para = table.Column<int>(type: "int", nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Origem = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OcorridaEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MudancasDeEstadoDaConta", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MudancasDeEstadoDaConta_Contas_ContaId",
                        column: x => x.ContaId,
                        principalTable: "Contas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MudancasDeEstadoDaConta_ContaId_Sequencia",
                table: "MudancasDeEstadoDaConta",
                columns: new[] { "ContaId", "Sequencia" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MudancasDeEstadoDaConta");

            migrationBuilder.DropColumn(
                name: "Estado",
                table: "Contas");

            migrationBuilder.DropColumn(
                name: "SequenciaDeEstado",
                table: "Contas");
        }
    }
}
