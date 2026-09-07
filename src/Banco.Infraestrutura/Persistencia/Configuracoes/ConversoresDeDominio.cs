using Banco.Dominio.Comum;
using Banco.Dominio.Contas;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Banco.Infraestrutura.Persistencia.Configuracoes;

/// <summary>
/// Conversores entre os tipos do dominio e as colunas.
/// </summary>
/// <remarks>
/// Existem para o dominio nao ter que abrir mao dos proprios tipos so porque o EF sabe
/// gravar <c>decimal</c> e <c>string</c>. Sem eles, saldo e valor voltariam a ser
/// <c>decimal</c> cru na entidade — e a garantia de que dinheiro nunca e negativo nem tem
/// tres casas sairia de dentro do tipo, que e o unico lugar onde ela nao tem como ser
/// esquecida.
/// </remarks>
internal static class ConversoresDeDominio
{
    public static readonly ValueConverter<Dinheiro, decimal> Dinheiro =
        new(dinheiro => dinheiro.Valor, valor => Banco.Dominio.Comum.Dinheiro.De(valor));

    public static readonly ValueConverter<NumeroDaConta, string> NumeroDaConta =
        new(numero => numero.Texto, texto => Banco.Dominio.Contas.NumeroDaConta.Criar(texto));
}
