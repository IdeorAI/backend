namespace IdeorAI.Services;

/// <summary>
/// Repositório de templates de prompts para as 7 etapas da Fase Projeto
/// </summary>
public static class PromptTemplates
{
    /// <summary>
    /// Etapa 1: Problema e Oportunidade
    /// </summary>
    public static string Etapa1ProblemaOportunidade(Dictionary<string, string> inputs)
    {
        var ideia = inputs.GetValueOrDefault("ideia", "[não fornecido]");
        var mercado = inputs.GetValueOrDefault("mercado", "[não especificado]");
        var regiao = inputs.GetValueOrDefault("regiao", "Brasil");
        var recursos = inputs.GetValueOrDefault("recursos", "[não especificado]");

        return $@"Você é um **estrategista de Customer Development e Lean Startup**. Sua missão é **conduzir integralmente a etapa ""Ideação e Problema""** de uma startup, evitando o risco de ""solução em busca de problema"" e entregando **insights prontos para uso**.

## **Contexto do projeto**
- **Ideia inicial:** {ideia}
- **Mercado/segmento-alvo:** {mercado}
- **Região/país de atuação:** {regiao}
- **Restrições e recursos:** {recursos}

## **Objetivos desta interação**
1. Clarificar a dor central, público-alvo e contexto competitivo por meio de pesquisa secundária estruturada.
2. Formular hipóteses iniciais explícitas sobre segmentos, dores, proposta de valor e disposição a pagar.

## **Instruções de execução**
- Use linguagem simples, prática e orientada à ação.
- Evite jargões sem explicação. Dê exemplos sempre que possível.
- Se a ideia estiver vaga, proponha cenários alternativos.

## **Roteiro (formato JSON)**

Retorne um JSON estruturado com as seguintes seções:

```json
{{
  ""declaracao_problema"": {{
    ""dor_central"": ""[1 frase descrevendo a dor]"",
    ""consequencias"": [
      ""[consequência 1]"",
      ""[consequência 2]"",
      ""[consequência 3]""
    ],
    ""quem_sente"": {{
      ""setor"": ""[setor/indústria]"",
      ""tamanho"": ""[pequenas/médias/grandes empresas ou pessoas físicas]"",
      ""funcao"": ""[função/cargo típico]""
    }},
    ""situacoes_gatilho"": [
      ""[situação 1]"",
      ""[situação 2]""
    ]
  }},
  ""mapa_mercado"": {{
    ""segmentos_promissores"": [""[segmento 1]"", ""[segmento 2]""],
    ""alternativas_atuais"": [""[alternativa 1]"", ""[alternativa 2]""],
    ""diferenciais_potenciais"": [""[diferencial 1]"", ""[diferencial 2]""],
    ""barreiras_entrada"": [""[barreira 1]"", ""[barreira 2]""],
    ""tendencias"": [""[tendência 1]"", ""[tendência 2]""],
    ""metricas"": {{
      ""tamanho_mercado"": ""[estimativa]"",
      ""ticket_medio"": ""[valor estimado]"",
      ""frequencia_uso"": ""[diária/semanal/mensal]""
    }}
  }},
  ""personas"": [
    {{
      ""nome"": ""[Nome da Persona 1]"",
      ""perfil"": ""[descrição do perfil]"",
      ""objetivos"": [""[objetivo 1]"", ""[objetivo 2]""],
      ""dores"": [""[dor 1]"", ""[dor 2]""],
      ""criterios_decisao"": [""[critério 1]"", ""[critério 2]""],
      ""objecoes"": [""[objeção 1]"", ""[objeção 2]""],
      ""canais_acesso"": [""[canal 1]"", ""[canal 2]""]
    }},
    {{
      ""nome"": ""[Nome da Persona 2]"",
      ""perfil"": ""[descrição do perfil]"",
      ""objetivos"": [""[objetivo 1]"", ""[objetivo 2]""],
      ""dores"": [""[dor 1]"", ""[dor 2]""],
      ""criterios_decisao"": [""[critério 1]"", ""[critério 2]""],
      ""objecoes"": [""[objeção 1]"", ""[objeção 2]""],
      ""canais_acesso"": [""[canal 1]"", ""[canal 2]""]
    }}
  ],
  ""proposta_valor_inicial"": {{
    ""frase_valor"": ""[1 sentença de valor]"",
    ""jobs_to_be_done"": [""[job 1]"", ""[job 2]"", ""[job 3]""],
    ""diferenciais"": [""[diferencial 1]"", ""[diferencial 2]""]
  }},
  ""resumo"": {{
    ""oportunidade"": ""[síntese da oportunidade]"",
    ""publico_prioritario"": ""[público principal]"",
    ""hipotese_valor"": ""[hipótese de proposta de valor]"",
    ""faixa_preco"": ""[recomendação inicial]"",
    ""incertezas"": [""[incerteza 1]"", ""[incerteza 2]"", ""[incerteza 3]""]
  }}
}}
```

**IMPORTANTE:** Retorne APENAS o JSON válido, sem texto adicional antes ou depois.";
    }

    /// <summary>
    /// Etapa 2: Pesquisa de Mercado
    /// </summary>
    public static string Etapa2PesquisaMercado(Dictionary<string, string> inputs)
    {
        var regiao = inputs.GetValueOrDefault("regiao", "Brasil");
        var segmento = inputs.GetValueOrDefault("segmento", "[não especificado]");
        var ideiaBase = inputs.GetValueOrDefault("ideia", "[não fornecido]");

        return $@"Você é um **analista de mercado especializado em startups**. Conduza uma pesquisa de mercado estruturada para validar a viabilidade comercial da ideia.

## **Contexto**
- **Região de atuação:** {regiao}
- **Segmento:** {segmento}
- **Ideia base:** {ideiaBase}

## **Objetivos**
1. Dimensionar o mercado (TAM, SAM, SOM)
2. Mapear concorrentes diretos e indiretos
3. Identificar tendências e oportunidades
4. Validar disposição a pagar

Retorne um JSON com a seguinte estrutura:

```json
{{
  ""dimensionamento_mercado"": {{
    ""tam"": {{
      ""valor"": ""[valor em USD/BRL]"",
      ""descricao"": ""[Total Addressable Market - mercado total]""
    }},
    ""sam"": {{
      ""valor"": ""[valor]"",
      ""descricao"": ""[Serviceable Available Market - mercado alcançável]""
    }},
    ""som"": {{
      ""valor"": ""[valor]"",
      ""descricao"": ""[Serviceable Obtainable Market - mercado realizável]""
    }}
  }},
  ""analise_competitiva"": {{
    ""concorrentes_diretos"": [
      {{
        ""nome"": ""[Nome]"",
        ""proposta"": ""[proposta de valor]"",
        ""preco"": ""[faixa de preço]"",
        ""forças"": [""[força 1]"", ""[força 2]""],
        ""fraquezas"": [""[fraqueza 1]"", ""[fraqueza 2]""]
      }}
    ],
    ""concorrentes_indiretos"": [""[alternativa 1]"", ""[alternativa 2]""],
    ""barreiras_entrada"": [""[barreira 1]"", ""[barreira 2]""],
    ""vantagens_competitivas"": [""[vantagem 1]"", ""[vantagem 2]""]
  }},
  ""tendencias"": [
    {{
      ""tendencia"": ""[nome da tendência]"",
      ""impacto"": ""[alto/médio/baixo]"",
      ""descricao"": ""[como afeta o negócio]""
    }}
  ],
  ""validacao_preco"": {{
    ""faixa_preco_sugerida"": ""[R$ X - R$ Y / mês]"",
    ""modelo_monetizacao"": ""[freemium/assinatura/pay-per-use]"",
    ""justificativa"": ""[por que este preço]"",
    ""referencias_mercado"": [""[ref 1]"", ""[ref 2]""]
  }}
}}
```

**IMPORTANTE:** Retorne APENAS o JSON válido.";
    }

    /// <summary>
    /// Etapa 3: Proposta de Valor
    /// </summary>
    public static string Etapa3PropostaValor(Dictionary<string, string> inputs)
    {
        var problemaValidado = inputs.GetValueOrDefault("problema", "[não fornecido]");
        var personas = inputs.GetValueOrDefault("personas", "[não fornecido]");

        return $@"Você é um **especialista em Value Proposition Canvas**. Crie uma proposta de valor clara e convincente.

## **Contexto**
- **Problema validado:** {problemaValidado}
- **Personas:** {personas}

## **Objetivos**
1. Definir jobs-to-be-done principais
2. Aliviar dores específicas
3. Criar ganhos tangíveis
4. Formular proposta de valor única

Retorne JSON:

```json
{{
  ""value_proposition_canvas"": {{
    ""customer_profile"": {{
      ""customer_jobs"": [""[job 1]"", ""[job 2]"", ""[job 3]""],
      ""pains"": [""[dor 1]"", ""[dor 2]"", ""[dor 3]""],
      ""gains"": [""[ganho 1]"", ""[ganho 2]"", ""[ganho 3]""]
    }},
    ""value_map"": {{
      ""products_services"": [""[produto/serviço 1]"", ""[produto/serviço 2]""],
      ""pain_relievers"": [""[como alivia dor 1]"", ""[como alivia dor 2]""],
      ""gain_creators"": [""[como cria ganho 1]"", ""[como cria ganho 2]""]
    }}
  }},
  ""proposta_valor_final"": {{
    ""headline"": ""[1 frase impactante]"",
    ""subheadline"": ""[2-3 frases explicativas]"",
    ""beneficios_chave"": [""[benefício 1]"", ""[benefício 2]"", ""[benefício 3]""],
    ""diferenciais"": [""[diferencial 1]"", ""[diferencial 2]""]
  }},
  ""posicionamento"": {{
    ""para"": ""[público-alvo]"",
    ""que"": ""[necessidade/oportunidade]"",
    ""nosso_produto"": ""[categoria]"",
    ""diferente_de"": ""[concorrentes]"",
    ""porque"": ""[razão principal]""
  }}
}}
```

**IMPORTANTE:** Retorne APENAS o JSON válido.";
    }

    /// <summary>
    /// Etapa 4: Modelo de Negócio
    /// </summary>
    public static string Etapa4ModeloNegocio(Dictionary<string, string> inputs)
    {
        var propostaValor = inputs.GetValueOrDefault("proposta_valor", "[não fornecido]");
        var segmento = inputs.GetValueOrDefault("segmento", "[não especificado]");

        return $@"Você é um **consultor de Business Model Canvas**. Estruture o modelo de negócio completo.

## **Contexto**
- **Proposta de Valor:** {propostaValor}
- **Segmento:** {segmento}

## **Objetivos**
Preencher todos os 9 blocos do Business Model Canvas

Retorne JSON:

```json
{{
  ""business_model_canvas"": {{
    ""segmentos_clientes"": [""[segmento 1]"", ""[segmento 2]""],
    ""proposta_valor"": [""[proposta principal]""],
    ""canais"": [""[canal 1]"", ""[canal 2]"", ""[canal 3]""],
    ""relacionamento_clientes"": [""[tipo 1]"", ""[tipo 2]""],
    ""fluxos_receita"": [
      {{
        ""tipo"": ""[assinatura/pay-per-use/freemium]"",
        ""valor"": ""[R$ X]"",
        ""frequencia"": ""[mensal/anual]""
      }}
    ],
    ""recursos_chave"": [""[recurso 1]"", ""[recurso 2]""],
    ""atividades_chave"": [""[atividade 1]"", ""[atividade 2]""],
    ""parcerias_chave"": [""[parceiro 1]"", ""[parceiro 2]""],
    ""estrutura_custos"": [
      {{
        ""categoria"": ""[categoria de custo]"",
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
    /// Etapa 5: MVP (Minimum Viable Product)
    /// </summary>
    public static string Etapa5MVP(Dictionary<string, string> inputs)
    {
        var propostaValor = inputs.GetValueOrDefault("proposta_valor", "[não fornecido]");
        var recursos = inputs.GetValueOrDefault("recursos", "limitados");

        return $@"Você é um **Product Manager especializado em MVPs**. Defina o MVP mínimo para validar a ideia.

## **Contexto**
- **Proposta de Valor:** {propostaValor}
- **Recursos disponíveis:** {recursos}

## **Objetivos**
1. Definir funcionalidades core do MVP
2. Priorizar features (MoSCoW)
3. Estimar esforço de desenvolvimento
4. Definir métricas de validação

Retorne JSON:

```json
{{
  ""mvp_core"": {{
    ""descricao"": ""[descrição do MVP em 2-3 frases]"",
    ""funcionalidades_essenciais"": [
      {{
        ""feature"": ""[nome da feature]"",
        ""descricao"": ""[o que faz]"",
        ""prioridade"": ""Must Have"",
        ""esforço"": ""[baixo/médio/alto]""
      }}
    ]
  }},
  ""priorizacao_moscow"": {{
    ""must_have"": [""[feature 1]"", ""[feature 2]""],
    ""should_have"": [""[feature 3]"", ""[feature 4]""],
    ""could_have"": [""[feature 5]"", ""[feature 6]""],
    ""wont_have"": [""[feature 7]"", ""[feature 8]""]
  }},
  ""roadmap_desenvolvimento"": {{
    ""sprint_1"": [""[task 1]"", ""[task 2]""],
    ""sprint_2"": [""[task 3]"", ""[task 4]""],
    ""sprint_3"": [""[task 5]"", ""[task 6]""],
    ""duracao_estimada"": ""[X semanas]""
  }},
  ""stack_tecnologico_sugerido"": {{
    ""frontend"": ""[tecnologia]"",
    ""backend"": ""[tecnologia]"",
    ""banco_dados"": ""[tecnologia]"",
    ""infraestrutura"": ""[cloud provider]"",
    ""justificativa"": ""[por que essa stack]""
  }},
  ""metricas_validacao"": [
    {{
      ""metrica"": ""[nome da métrica]"",
      ""meta"": ""[valor alvo]"",
      ""prazo"": ""[X semanas/meses]""
    }}
  ]
}}
```

**IMPORTANTE:** Retorne APENAS o JSON válido.";
    }

    /// <summary>
    /// Etapa 6: Equipe Mínima
    /// </summary>
    public static string Etapa6EquipeMinima(Dictionary<string, string> inputs)
    {
        var mvp = inputs.GetValueOrDefault("mvp", "[não fornecido]");
        var fase = inputs.GetValueOrDefault("fase", "pré-seed");

        return $@"Você é um **consultor de formação de equipes de startups**. Defina a equipe mínima necessária.

## **Contexto**
- **MVP:** {mvp}
- **Fase:** {fase}

## **Objetivos**
1. Definir papéis essenciais
2. Perfil ideal de cada membro
3. Opções de terceirização vs contratação
4. Estrutura de equity/remuneração

Retorne JSON:

```json
{{
  ""equipe_core"": [
    {{
      ""papel"": ""[CEO/CTO/CPO]"",
      ""responsabilidades"": [""[resp 1]"", ""[resp 2]""],
      ""perfil_ideal"": ""[descrição do perfil]"",
      ""skills_essenciais"": [""[skill 1]"", ""[skill 2]""],
      ""dedicacao"": ""[full-time/part-time]"",
      ""remuneracao_sugerida"": ""[R$ X ou Y% equity]""
    }}
  ],
  ""papeis_terceirizados"": [
    {{
      ""papel"": ""[Designer/Dev Backend]"",
      ""justificativa"": ""[por que terceirizar]"",
      ""custo_estimado"": ""[R$ X/mês]""
    }}
  ],
  ""estrutura_equity"": {{
    ""founders"": ""[%]"",
    ""early_team"": ""[%]"",
    ""pool_opcoes"": ""[%]"",
    ""investidores"": ""[%]""
  }},
  ""organograma_inicial"": ""[descrição textual da estrutura]"",
  ""plano_contratacao"": [
    {{
      ""papel"": ""[papel]"",
      ""quando"": ""[Mês X ou após milestone Y]"",
      ""prioridade"": ""[alta/média/baixa]""
    }}
  ]
}}
```

**IMPORTANTE:** Retorne APENAS o JSON válido.";
    }

    /// <summary>
    /// Etapa 7: Pitch Deck + Plano Executivo + Resumo
    /// </summary>
    public static string Etapa7PitchPlanoResumo(Dictionary<string, string> inputs)
    {
        var nomeProjeto = inputs.GetValueOrDefault("nome", "[Nome do Projeto]");
        var etapasAnteriores = inputs.GetValueOrDefault("etapas_anteriores", "[consolidar dados das etapas 1-6]");

        return $@"Você é um **consultor de pitch decks e planos executivos para startups**. Consolide todas as etapas anteriores.

## **Contexto**
- **Nome do Projeto:** {nomeProjeto}
- **Etapas anteriores:** {etapasAnteriores}

## **Objetivos**
1. Estruturar pitch deck (12-15 slides)
2. Criar plano executivo (1-2 páginas)
3. Gerar resumo executivo (elevator pitch)

Retorne JSON:

```json
{{
  ""pitch_deck"": {{
    ""slide_1_capa"": {{
      ""titulo"": ""{nomeProjeto}"",
      ""subtitulo"": ""[tagline]"",
      ""apresentador"": ""[nome]""
    }},
    ""slide_2_problema"": {{
      ""titulo"": ""O Problema"",
      ""conteudo"": [""[bullet 1]"", ""[bullet 2]"", ""[bullet 3]""]
    }},
    ""slide_3_solucao"": {{
      ""titulo"": ""Nossa Solução"",
      ""conteudo"": [""[bullet 1]"", ""[bullet 2]""]
    }},
    ""slide_4_produto"": {{
      ""titulo"": ""Produto/MVP"",
      ""conteudo"": [""[feature 1]"", ""[feature 2]""]
    }},
    ""slide_5_mercado"": {{
      ""titulo"": ""Oportunidade de Mercado"",
      ""tam"": ""[valor]"",
      ""sam"": ""[valor]"",
      ""som"": ""[valor]""
    }},
    ""slide_6_modelo_negocio"": {{
      ""titulo"": ""Modelo de Negócio"",
      ""conteudo"": [""[fluxo receita 1]"", ""[fluxo receita 2]""]
    }},
    ""slide_7_competicao"": {{
      ""titulo"": ""Competição"",
      ""diferenciais"": [""[diferencial 1]"", ""[diferencial 2]""]
    }},
    ""slide_8_tracao"": {{
      ""titulo"": ""Tração/Roadmap"",
      ""metricas"": [""[métrica 1]"", ""[métrica 2]""]
    }},
    ""slide_9_financeiro"": {{
      ""titulo"": ""Projeções Financeiras"",
      ""ano_1"": ""[receita]"",
      ""ano_2"": ""[receita]"",
      ""ano_3"": ""[receita]""
    }},
    ""slide_10_equipe"": {{
      ""titulo"": ""Equipe"",
      ""membros"": [""[nome + papel + background]""]
    }},
    ""slide_11_ask"": {{
      ""titulo"": ""O Pedido"",
      ""valor"": ""[R$ X]"",
      ""uso"": [""[uso 1]"", ""[uso 2]""],
      ""equity"": ""[%]""
    }},
    ""slide_12_contato"": {{
      ""titulo"": ""Contato"",
      ""email"": ""[email]"",
      ""site"": ""[url]""
    }}
  }},
  ""plano_executivo"": {{
    ""visao_geral"": ""[2-3 parágrafos resumindo o negócio]"",
    ""objetivos_6_meses"": [""[objetivo 1]"", ""[objetivo 2]""],
    ""objetivos_12_meses"": [""[objetivo 1]"", ""[objetivo 2]""],
    ""recursos_necessarios"": [""[recurso 1]"", ""[recurso 2]""],
    ""riscos_principais"": [""[risco 1]"", ""[risco 2]""],
    ""proximos_passos"": [""[passo 1]"", ""[passo 2]""]
  }},
  ""resumo_executivo"": {{
    ""elevator_pitch"": ""[30 segundos - 2-3 frases]"",
    ""one_liner"": ""[1 frase de impacto]"",
    ""problema_resumido"": ""[1 frase]"",
    ""solucao_resumida"": ""[1 frase]"",
    ""mercado_resumido"": ""[TAM em 1 frase]"",
    ""diferencial_resumido"": ""[1 frase]""
  }}
}}
```

**IMPORTANTE:** Retorne APENAS o JSON válido.";
    }

    /// <summary>
    /// Retorna o prompt apropriado para a etapa especificada
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
