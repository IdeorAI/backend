namespace IdeorAI.Services;

/// <summary>
/// Versões resumidas dos prompts para ambiente de desenvolvimento
/// Contém apenas o essencial para validação rápida
/// </summary>
public static class PromptResumidos
{
    /// <summary>
    /// Etapa 1: Problema e Oportunidade (Resumido)
    /// </summary>
    public static string Etapa1ProblemaOportunidade(Dictionary<string, string> inputs)
    {
        var ideia = inputs.GetValueOrDefault("ideia", "[não fornecido]");

        return $@"Você é um estrategista de startups. Analise esta ideia de negócio de forma objetiva.

**Ideia:** {ideia}

Retorne um JSON com:
```json
{{
  ""declaracao_problema"": {{
    ""dor_central"": ""[descreva a dor principal em 1 frase]"",
    ""quem_sente"": ""[público-alvo]"",
    ""consequencias"": [""[consequência 1]"", ""[consequência 2]""]
  }},
  ""mapa_mercado"": {{
    ""segmentos_promissores"": [""[segmento 1]"", ""[segmento 2]""],
    ""alternativas_atuais"": [""[alternativa 1]"", ""[alternativa 2]""],
    ""diferenciais_potenciais"": [""[diferencial 1]"", ""[diferencial 2]""]
  }},
  ""personas"": [
    {{
      ""nome"": ""[Nome Persona]"",
      ""perfil"": ""[descrição breve]"",
      ""dores"": [""[dor 1]"", ""[dor 2]""]
    }}
  ],
  ""proposta_valor_inicial"": {{
    ""frase_valor"": ""[1 sentença de valor]"",
    ""diferenciais"": [""[diferencial 1]"", ""[diferencial 2]""]
  }}
}}
```

**IMPORTANTE:** Retorne APENAS o JSON válido, sem texto adicional.";
    }

    /// <summary>
    /// Etapa 2: Pesquisa de Mercado (Resumido)
    /// </summary>
    public static string Etapa2PesquisaMercado(Dictionary<string, string> inputs)
    {
        var ideia = inputs.GetValueOrDefault("ideia", "[não fornecido]");

        return $@"Você é um analista de mercado. Faça uma análise resumida de mercado para esta ideia.

**Ideia:** {ideia}

Retorne JSON:
```json
{{
  ""dimensionamento_mercado"": {{
    ""tam"": {{
      ""valor"": ""[valor estimado]"",
      ""descricao"": ""[mercado total]""
    }},
    ""sam"": {{
      ""valor"": ""[valor]"",
      ""descricao"": ""[mercado alcançável]""
    }},
    ""som"": {{
      ""valor"": ""[valor]"",
      ""descricao"": ""[mercado realizável nos próximos 3 anos]""
    }}
  }},
  ""analise_competitiva"": {{
    ""concorrentes_diretos"": [
      {{
        ""nome"": ""[Nome]"",
        ""proposta"": ""[proposta]"",
        ""preco"": ""[faixa de preço]"",
        ""forças"": [""[força 1]""],
        ""fraquezas"": [""[fraqueza 1]""]
      }}
    ],
    ""concorrentes_indiretos"": [""[alternativa 1]"", ""[alternativa 2]""],
    ""vantagens_competitivas"": [""[vantagem 1]"", ""[vantagem 2]""]
  }},
  ""tendencias"": [
    {{
      ""tendencia"": ""[nome]"",
      ""impacto"": ""[alto/médio/baixo]"",
      ""descricao"": ""[breve descrição]""
    }}
  ],
  ""validacao_preco"": {{
    ""faixa_preco_sugerida"": ""[R$ X - R$ Y]"",
    ""modelo_monetizacao"": ""[modelo]"",
    ""justificativa"": ""[justificativa breve]""
  }}
}}
```

**IMPORTANTE:** Retorne APENAS o JSON válido.";
    }

    /// <summary>
    /// Etapa 3: Proposta de Valor (Resumido)
    /// </summary>
    public static string Etapa3PropostaValor(Dictionary<string, string> inputs)
    {
        var ideia = inputs.GetValueOrDefault("ideia", "[não fornecido]");

        return $@"Você é um especialista em Value Proposition. Crie uma proposta de valor clara.

**Ideia:** {ideia}

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
      ""pain_relievers"": [""[como alivia dor 1]""],
      ""gain_creators"": [""[como cria ganho 1]""]
    }}
  }},
  ""proposta_valor_final"": {{
    ""headline"": ""[1 frase impactante]"",
    ""subheadline"": ""[2-3 frases explicativas]"",
    ""beneficios_chave"": [""[benefício 1]"", ""[benefício 2]""],
    ""diferenciais"": [""[diferencial 1]"", ""[diferencial 2]""]
  }},
  ""posicionamento"": {{
    ""para"": ""[público-alvo]"",
    ""que"": ""[necessidade]"",
    ""nosso_produto"": ""[categoria]"",
    ""diferente_de"": ""[concorrentes]"",
    ""porque"": ""[razão principal]""
  }}
}}
```

**IMPORTANTE:** Retorne APENAS o JSON válido.";
    }

    /// <summary>
    /// Etapa 4: Modelo de Negócio (Resumido)
    /// </summary>
    public static string Etapa4ModeloNegocio(Dictionary<string, string> inputs)
    {
        var ideia = inputs.GetValueOrDefault("ideia", "[não fornecido]");

        return $@"Você é um consultor de Business Model Canvas. Estruture o modelo de negócio.

**Ideia:** {ideia}

Retorne JSON:
```json
{{
  ""business_model_canvas"": {{
    ""segmentos_clientes"": [""[segmento 1]"", ""[segmento 2]""],
    ""proposta_valor"": [""[proposta]""],
    ""canais"": [""[canal 1]"", ""[canal 2]""],
    ""relacionamento_clientes"": [""[tipo 1]""],
    ""fluxos_receita"": [
      {{
        ""tipo"": ""[modelo]"",
        ""valor"": ""[R$ X]"",
        ""frequencia"": ""[mensal/anual]""
      }}
    ],
    ""recursos_chave"": [""[recurso 1]"", ""[recurso 2]""],
    ""atividades_chave"": [""[atividade 1]"", ""[atividade 2]""],
    ""parcerias_chave"": [""[parceiro 1]""],
    ""estrutura_custos"": [
      {{
        ""categoria"": ""[categoria]"",
        ""valor_estimado"": ""[R$ X/mês]"",
        ""tipo"": ""[fixo/variável]""
      }}
    ]
  }},
  ""projecao_financeira_simplificada"": {{
    ""ano_1"": {{
      ""receita_mensal_media"": ""[R$ X]"",
      ""custos_mensais"": ""[R$ Y]"",
      ""margem_bruta"": ""[%]"",
      ""break_even_months"": ""[N meses]""
    }},
    ""premissas"": [""[premissa 1]"", ""[premissa 2]""]
  }}
}}
```

**IMPORTANTE:** Retorne APENAS o JSON válido.";
    }

    /// <summary>
    /// Etapa 5: MVP (Resumido)
    /// </summary>
    public static string Etapa5MVP(Dictionary<string, string> inputs)
    {
        var ideia = inputs.GetValueOrDefault("ideia", "[não fornecido]");

        return $@"Você é um Product Manager. Defina o MVP mínimo.

**Ideia:** {ideia}

Retorne JSON:
```json
{{
  ""mvp_core"": {{
    ""descricao"": ""[descrição do MVP]"",
    ""funcionalidades_essenciais"": [
      {{
        ""feature"": ""[nome]"",
        ""descricao"": ""[o que faz]"",
        ""prioridade"": ""Must Have"",
        ""esforço"": ""[baixo/médio/alto]""
      }}
    ]
  }},
  ""priorizacao_moscow"": {{
    ""must_have"": [""[feature 1]"", ""[feature 2]""],
    ""should_have"": [""[feature 3]""],
    ""could_have"": [""[feature 4]""],
    ""wont_have"": [""[feature 5]""]
  }},
  ""stack_tecnologico_sugerido"": {{
    ""frontend"": ""[tecnologia]"",
    ""backend"": ""[tecnologia]"",
    ""banco_dados"": ""[tecnologia]"",
    ""justificativa"": ""[justificativa breve]""
  }},
  ""metricas_validacao"": [
    {{
      ""metrica"": ""[métrica]"",
      ""meta"": ""[valor]"",
      ""prazo"": ""[prazo]""
    }}
  ]
}}
```

**IMPORTANTE:** Retorne APENAS o JSON válido.";
    }

    /// <summary>
    /// Etapa 6: Equipe Mínima (Resumido)
    /// </summary>
    public static string Etapa6EquipeMinima(Dictionary<string, string> inputs)
    {
        var ideia = inputs.GetValueOrDefault("ideia", "[não fornecido]");

        return $@"Você é um consultor de formação de equipes. Defina a equipe mínima.

**Ideia:** {ideia}

Retorne JSON:
```json
{{
  ""equipe_core"": [
    {{
      ""papel"": ""[CEO/CTO/CPO]"",
      ""responsabilidades"": [""[resp 1]"", ""[resp 2]""],
      ""skills_essenciais"": [""[skill 1]"", ""[skill 2]""],
      ""dedicacao"": ""[full-time/part-time]""
    }}
  ],
  ""papeis_terceirizados"": [
    {{
      ""papel"": ""[papel]"",
      ""justificativa"": ""[justificativa]"",
      ""custo_estimado"": ""[R$ X/mês]""
    }}
  ],
  ""estrutura_equity"": {{
    ""founders"": ""[%]"",
    ""early_team"": ""[%]"",
    ""pool_opcoes"": ""[%]""
  }}
}}
```

**IMPORTANTE:** Retorne APENAS o JSON válido.";
    }

    /// <summary>
    /// Etapa 7: Pitch + Plano + Resumo (Resumido)
    /// </summary>
    public static string Etapa7PitchPlanoResumo(Dictionary<string, string> inputs)
    {
        var ideia = inputs.GetValueOrDefault("ideia", "[não fornecido]");
        var nome = inputs.GetValueOrDefault("nome", "[Nome do Projeto]");

        return $@"Você é um consultor de pitch decks. Crie um pitch resumido.

**Projeto:** {nome}
**Ideia:** {ideia}

Retorne JSON:
```json
{{
  ""pitch_deck"": {{
    ""slide_1_capa"": {{
      ""titulo"": ""{nome}"",
      ""subtitulo"": ""[tagline]""
    }},
    ""slide_2_problema"": {{
      ""conteudo"": [""[bullet 1]"", ""[bullet 2]""]
    }},
    ""slide_3_solucao"": {{
      ""conteudo"": [""[bullet 1]"", ""[bullet 2]""]
    }},
    ""slide_4_mercado"": {{
      ""tam"": ""[valor]"",
      ""sam"": ""[valor]""
    }},
    ""slide_5_modelo_negocio"": {{
      ""conteudo"": [""[fluxo receita]""]
    }},
    ""slide_6_equipe"": {{
      ""membros"": [""[nome + papel]""]
    }}
  }},
  ""plano_executivo"": {{
    ""visao_geral"": ""[resumo do negócio]"",
    ""objetivos_6_meses"": [""[objetivo 1]"", ""[objetivo 2]""],
    ""recursos_necessarios"": [""[recurso 1]""]
  }},
  ""resumo_executivo"": {{
    ""elevator_pitch"": ""[30 segundos - 2-3 frases]"",
    ""one_liner"": ""[1 frase de impacto]"",
    ""problema_resumido"": ""[1 frase]"",
    ""solucao_resumida"": ""[1 frase]""
  }}
}}
```

**IMPORTANTE:** Retorne APENAS o JSON válido.";
    }

    /// <summary>
    /// Retorna o prompt resumido apropriado para a etapa especificada
    /// </summary>
    public static string GetPromptForStage(string stage, Dictionary<string, string> inputs)
    {
        return stage switch
        {
            "etapa1" => Etapa1ProblemaOportunidade(inputs),
            "etapa2" => Etapa2PesquisaMercado(inputs),
            "etapa3" => Etapa3PropostaValor(inputs),
            "etapa4" => Etapa4ModeloNegocio(inputs),
            "etapa5" => Etapa5MVP(inputs),
            "etapa6" => Etapa6EquipeMinima(inputs),
            "etapa7" => Etapa7PitchPlanoResumo(inputs),
            _ => throw new ArgumentException($"Stage '{stage}' não reconhecido")
        };
    }
}
