namespace IdeorAI.Services;

/// <summary>
/// Versões ULTRA-RESUMIDAS dos prompts para reduzir carga no Gemini API
/// Prompts mínimos (20-30% do tamanho original) para evitar rate limiting e timeouts
/// </summary>
public static class PromptMiniResumidos
{
    /// <summary>
    /// Etapa 1: Problema e Oportunidade (Mini)
    /// </summary>
    public static string Etapa1ProblemaOportunidade(Dictionary<string, string> inputs)
    {
        var ideia = inputs.GetValueOrDefault("ideia", "[não fornecido]");

        return $@"Analise esta startup: {ideia}

Retorne JSON:
```json
{{
  ""declaracao_problema"": {{
    ""dor_central"": ""[dor em 1 frase]"",
    ""quem_sente"": ""[público]"",
    ""consequencias"": [""[consequência 1]"", ""[consequência 2]""]
  }},
  ""mapa_mercado"": {{
    ""segmentos_promissores"": [""[segmento 1]"", ""[segmento 2]""],
    ""alternativas_atuais"": [""[alternativa 1]""],
    ""diferenciais_potenciais"": [""[diferencial 1]"", ""[diferencial 2]""]
  }},
  ""personas"": [{{
    ""nome"": ""[Nome]"",
    ""perfil"": ""[descrição breve]"",
    ""dores"": [""[dor 1]"", ""[dor 2]""]
  }}],
  ""proposta_valor_inicial"": {{
    ""frase_valor"": ""[1 frase]"",
    ""diferenciais"": [""[diferencial 1]"", ""[diferencial 2]""]
  }}
}}
```

Retorne APENAS JSON válido.";
    }

    /// <summary>
    /// Etapa 2: Pesquisa de Mercado (Mini)
    /// </summary>
    public static string Etapa2PesquisaMercado(Dictionary<string, string> inputs)
    {
        var ideia = inputs.GetValueOrDefault("ideia", "[não fornecido]");

        return $@"Análise de mercado para: {ideia}

Retorne JSON:
```json
{{
  ""dimensionamento_mercado"": {{
    ""tam"": {{ ""valor"": ""[valor]"", ""descricao"": ""[mercado total]"" }},
    ""sam"": {{ ""valor"": ""[valor]"", ""descricao"": ""[alcançável]"" }},
    ""som"": {{ ""valor"": ""[valor]"", ""descricao"": ""[realizável 3 anos]"" }}
  }},
  ""analise_competitiva"": {{
    ""concorrentes_diretos"": [{{
      ""nome"": ""[Nome]"",
      ""proposta"": ""[proposta]"",
      ""preco"": ""[preço]"",
      ""forças"": [""[força 1]""],
      ""fraquezas"": [""[fraqueza 1]""]
    }}],
    ""concorrentes_indiretos"": [""[alternativa 1]""],
    ""vantagens_competitivas"": [""[vantagem 1]"", ""[vantagem 2]""]
  }},
  ""tendencias"": [{{
    ""tendencia"": ""[nome]"",
    ""impacto"": ""alto"",
    ""descricao"": ""[breve]""
  }}],
  ""validacao_preco"": {{
    ""faixa_preco_sugerida"": ""[R$ X - R$ Y]"",
    ""modelo_monetizacao"": ""[modelo]"",
    ""justificativa"": ""[breve]""
  }}
}}
```

Retorne APENAS JSON válido.";
    }

    /// <summary>
    /// Etapa 3: Proposta de Valor (Mini)
    /// </summary>
    public static string Etapa3PropostaValor(Dictionary<string, string> inputs)
    {
        var ideia = inputs.GetValueOrDefault("ideia", "[não fornecido]");

        return $@"Proposta de valor para: {ideia}

Retorne JSON:
```json
{{
  ""value_proposition_canvas"": {{
    ""customer_profile"": {{
      ""customer_jobs"": [""[job 1]"", ""[job 2]""],
      ""pains"": [""[dor 1]"", ""[dor 2]""],
      ""gains"": [""[ganho 1]"", ""[ganho 2]""]
    }},
    ""value_map"": {{
      ""products_services"": [""[produto 1]""],
      ""pain_relievers"": [""[alivia dor 1]""],
      ""gain_creators"": [""[cria ganho 1]""]
    }}
  }},
  ""proposta_valor_final"": {{
    ""headline"": ""[1 frase impactante]"",
    ""subheadline"": ""[2-3 frases]"",
    ""beneficios_chave"": [""[benefício 1]"", ""[benefício 2]""],
    ""diferenciais"": [""[diferencial 1]"", ""[diferencial 2]""]
  }},
  ""posicionamento"": {{
    ""para"": ""[público]"",
    ""que"": ""[necessidade]"",
    ""nosso_produto"": ""[categoria]"",
    ""diferente_de"": ""[concorrentes]"",
    ""porque"": ""[razão]""
  }}
}}
```

Retorne APENAS JSON válido.";
    }

    /// <summary>
    /// Etapa 4: Modelo de Negócio (Mini)
    /// </summary>
    public static string Etapa4ModeloNegocio(Dictionary<string, string> inputs)
    {
        var ideia = inputs.GetValueOrDefault("ideia", "[não fornecido]");

        return $@"Business Model Canvas para: {ideia}

Retorne JSON:
```json
{{
  ""business_model_canvas"": {{
    ""segmentos_clientes"": [""[segmento 1]"", ""[segmento 2]""],
    ""proposta_valor"": [""[proposta]""],
    ""canais"": [""[canal 1]"", ""[canal 2]""],
    ""relacionamento_clientes"": [""[tipo 1]""],
    ""fluxos_receita"": [{{
      ""tipo"": ""[modelo]"",
      ""valor"": ""[R$ X]"",
      ""frequencia"": ""mensal""
    }}],
    ""recursos_chave"": [""[recurso 1]"", ""[recurso 2]""],
    ""atividades_chave"": [""[atividade 1]"", ""[atividade 2]""],
    ""parcerias_chave"": [""[parceiro 1]""],
    ""estrutura_custos"": [{{
      ""categoria"": ""[categoria]"",
      ""valor_estimado"": ""[R$ X/mês]"",
      ""tipo"": ""fixo""
    }}]
  }},
  ""projecao_financeira_simplificada"": {{
    ""ano_1"": {{
      ""receita_mensal_media"": ""[R$ X]"",
      ""custos_mensais"": ""[R$ Y]"",
      ""margem_bruta"": ""[%]"",
      ""break_even_months"": ""[N]""
    }},
    ""premissas"": [""[premissa 1]"", ""[premissa 2]""]
  }}
}}
```

Retorne APENAS JSON válido.";
    }

    /// <summary>
    /// Etapa 5: MVP (Mini)
    /// </summary>
    public static string Etapa5MVP(Dictionary<string, string> inputs)
    {
        var ideia = inputs.GetValueOrDefault("ideia", "[não fornecido]");

        return $@"MVP para: {ideia}

Retorne JSON:
```json
{{
  ""definicao_mvp"": {{
    ""core_features"": [""[feature 1]"", ""[feature 2]"", ""[feature 3]""],
    ""nice_to_have"": [""[feature opcional 1]""],
    ""justificativa"": ""[por que essas features]""
  }},
  ""roadmap_3_meses"": [
    {{ ""mes"": 1, ""objetivo"": ""[objetivo]"", ""entregas"": [""[entrega 1]""] }},
    {{ ""mes"": 2, ""objetivo"": ""[objetivo]"", ""entregas"": [""[entrega 1]""] }},
    {{ ""mes"": 3, ""objetivo"": ""[objetivo]"", ""entregas"": [""[entrega 1]""] }}
  ],
  ""metricas_validacao"": [
    {{ ""metrica"": ""[nome]"", ""meta"": ""[valor]"", ""motivo"": ""[por que importante]"" }}
  ],
  ""stack_tecnologica"": {{
    ""frontend"": ""[tech]"",
    ""backend"": ""[tech]"",
    ""database"": ""[tech]"",
    ""infra"": ""[cloud provider]""
  }},
  ""custo_desenvolvimento"": {{
    ""estimativa_total"": ""[R$ X]"",
    ""tempo_estimado"": ""[N meses]"",
    ""composicao"": [{{ ""item"": ""[item]"", ""valor"": ""[R$ Y]"" }}]
  }}
}}
```

Retorne APENAS JSON válido.";
    }

    /// <summary>
    /// Etapa 6: Equipe Mínima (Mini)
    /// </summary>
    public static string Etapa6EquipeMinima(Dictionary<string, string> inputs)
    {
        var ideia = inputs.GetValueOrDefault("ideia", "[não fornecido]");

        return $@"Equipe para: {ideia}

Retorne JSON:
```json
{{
  ""estrutura_equipe"": [
    {{
      ""cargo"": ""[cargo]"",
      ""responsabilidades"": [""[resp 1]"", ""[resp 2]""],
      ""skills_obrigatorias"": [""[skill 1]""],
      ""dedicacao"": ""full-time"",
      ""custo_mensal"": ""[R$ X]""
    }}
  ],
  ""custo_total_mensal"": ""[R$ Y]"",
  ""custo_total_6_meses"": ""[R$ Z]"",
  ""terceirizacao_vs_inhouse"": [
    {{ ""funcao"": ""[função]"", ""recomendacao"": ""terceirizar"", ""motivo"": ""[motivo]"" }}
  ],
  ""plano_contratacao"": [
    {{ ""mes"": 1, ""contratar"": ""[cargo]"", ""prioridade"": ""alta"" }}
  ]
}}
```

Retorne APENAS JSON válido.";
    }

    /// <summary>
    /// Etapa 7: Pitch Deck + Plano Executivo (Mini)
    /// </summary>
    public static string Etapa7PitchDeck(Dictionary<string, string> inputs)
    {
        var ideia = inputs.GetValueOrDefault("ideia", "[não fornecido]");

        return $@"Pitch Deck para: {ideia}

Retorne JSON:
```json
{{
  ""pitch_deck"": {{
    ""slide_1_problema"": ""[1 frase: problema principal]"",
    ""slide_2_solucao"": ""[1 frase: solução]"",
    ""slide_3_mercado"": ""[TAM: R$ X | SAM: R$ Y]"",
    ""slide_4_produto"": ""[descrição curta do produto]"",
    ""slide_5_modelo_negocio"": ""[como ganha dinheiro]"",
    ""slide_6_tracao"": ""[métricas ou milestones]"",
    ""slide_7_competicao"": ""[principais concorrentes]"",
    ""slide_8_equipe"": ""[perfil dos fundadores]"",
    ""slide_9_financeiro"": ""[projeção 3 anos]"",
    ""slide_10_ask"": ""[quanto busca investir e para quê]""
  }},
  ""plano_executivo"": {{
    ""visao"": ""[1 frase: onde quer chegar]"",
    ""missao"": ""[1 frase: propósito]"",
    ""objetivos_6_meses"": [""[objetivo 1]"", ""[objetivo 2]""],
    ""investimento_necessario"": ""[R$ X]"",
    ""uso_recursos"": [{{ ""categoria"": ""[categoria]"", ""valor"": ""[R$ Y]"", ""percentual"": ""[%]"" }}]
  }},
  ""one_pager"": {{
    ""elevator_pitch"": ""[2-3 frases: resumo completo da startup]"",
    ""diferencial_competitivo"": ""[1 frase: por que escolher você]"",
    ""ask"": ""[o que precisa agora]""
  }}
}}
```

Retorne APENAS JSON válido.";
    }

    /// <summary>
    /// Retorna o prompt apropriado para cada etapa
    /// </summary>
    public static string GetPromptForStage(string stage, Dictionary<string, string> inputs)
    {
        return stage.ToLower() switch
        {
            "etapa1" => Etapa1ProblemaOportunidade(inputs),
            "etapa2" => Etapa2PesquisaMercado(inputs),
            "etapa3" => Etapa3PropostaValor(inputs),
            "etapa4" => Etapa4ModeloNegocio(inputs),
            "etapa5" => Etapa5MVP(inputs),
            "etapa6" => Etapa6EquipeMinima(inputs),
            "etapa7" => Etapa7PitchDeck(inputs),
            _ => throw new ArgumentException($"Stage '{stage}' não reconhecido")
        };
    }
}
