using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banco.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "SequenciaDeContas");

            migrationBuilder.CreateTable(
                name: "Contas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Numero = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: false),
                    Titular = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Saldo = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UltimaSequencia = table.Column<long>(type: "bigint", nullable: false),
                    AbertaEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AtualizadaEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Lancamentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequencia = table.Column<long>(type: "bigint", nullable: false),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    Valor = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SaldoDepois = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Descricao = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false),
                    Origem = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ChaveIdempotencia = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lancamentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Lancamentos_Contas_ContaId",
                        column: x => x.ContaId,
                        principalTable: "Contas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Contas_Numero",
                table: "Contas",
                column: "Numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lancamentos_ContaId_ChaveIdempotencia",
                table: "Lancamentos",
                columns: new[] { "ContaId", "ChaveIdempotencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lancamentos_ContaId_CriadoEm",
                table: "Lancamentos",
                columns: new[] { "ContaId", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_Lancamentos_ContaId_Sequencia",
                table: "Lancamentos",
                columns: new[] { "ContaId", "Sequencia" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Lancamentos");

            migrationBuilder.DropTable(
                name: "Contas");

            migrationBuilder.DropSequence(
                name: "SequenciaDeContas");
        }
    }
}
