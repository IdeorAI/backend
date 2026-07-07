namespace IdeorAI.Model.DTOs;

/// <summary>
/// Síntese financeira canônica derivada da DRE (Spec 022 v2).
/// Os 6 números exibidos no card "Resumo Financeiro" e injetados nos documentos finais.
/// Todos em Reais (R$). Os "anuais" são a soma das 12 colunas da DRE.
/// </summary>
public class FinancialSummaryDto
{
    /// <summary>Soma anual do grupo "receita" (receita bruta + receitas adicionadas).</summary>
    public decimal ReceitaBrutaAnual { get; set; }

    /// <summary>Soma anual das deduções e impostos sobre vendas.</summary>
    public decimal DeducoesAnual { get; set; }

    /// <summary>Receita Bruta − Deduções (anual).</summary>
    public decimal ReceitaLiquidaAnual { get; set; }

    /// <summary>Receita Líquida − CPV (anual).</summary>
    public decimal LucroBrutoAnual { get; set; }

    /// <summary>Média mensal das Despesas Operacionais (OPEX anual ÷ 12).</summary>
    public decimal OpexMensalMedia { get; set; }

    /// <summary>Lucro Líquido (anual) — cascata completa da DRE.</summary>
    public decimal LucroLiquidoAnual { get; set; }

    /// <summary>True quando a síntese veio do cache (task já existente).</summary>
    public bool FromCache { get; set; }
}
