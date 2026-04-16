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
        var segmento = inputs.GetValueOrDefault("segmento", "");
        var regiao = inputs.GetValueOrDefault("regiao", "");
        var restricoes = inputs.GetValueOrDefault("restricoes", "");

        var premissas = "";
        if (string.IsNullOrEmpty(segmento)) premissas += "Assuma segmento razoável. ";
        if (string.IsNullOrEmpty(regiao)) premissas += "Assuma região padrão (Brasil). ";
        if (string.IsNullOrEmpty(restricoes)) premissas += "Sem restrições específicas. ";

        return $@"Você é um estrategista de Customer Development. Analise:
- Ideia: {ideia}
- Segmento: {(string.IsNullOrEmpty(segmento) ? "[assumir]" : segmento)}
- Região: {(string.IsNullOrEmpty(regiao) ? "[assumir]" : regiao)}
- Restrições: {(string.IsNullOrEmpty(restricoes) ? "[nenhuma]" : restricoes)}

{premissas}

Retorne JSON:
```json
{{
  ""declaracao_problema"": {{
    ""dor_central"": ""[1 frase direta]"",
    ""consequencias"": [""[consequência 1]"", ""[consequência 2]""],
    ""quem_sente"": ""[persona resumida: setor, porte, função]"",
    ""situacoes_gatilho"": [""[situação 1]"", ""[situação 2]""]
  }},
  ""mapa_mercado"": {{
    ""segmentos"": [""[subnicho 1]"", ""[subnicho 2]""],
    ""alternativas_atuais"": [""[concorrente/gambiarra 1]""],
    ""vazios"": [""[diferencial/oportunidade 1]""]
  }},
  ""personas"": [{{
    ""perfil"": ""[papel, porte, responsabilidade]"",
    ""dores"": [""[dor 1]"", ""[dor 2]""],
    ""objetivos"": [""[objetivo 1]""]
  }}],
  ""proposta_valor"": {{
    ""frase"": ""[1 sentença]"",
    ""jobs_to_be_done"": [""[job 1]""]
  }},
  ""sintese"": {{
    ""oportunidade"": ""[2-3 frases]"",
    ""publico_prioritario"": ""[quem focar primeiro]"",
    ""hipotese_monetizacao"": ""[modelo + preço sugerido]"",
    ""incertezas"": [""[incerteza 1]"", ""[incerteza 2]""]
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
        var contextoAcumulado = inputs.GetValueOrDefault("contexto_acumulado", "");

        var contextoSection = string.IsNullOrEmpty(contextoAcumulado) 
            ? "" 
            : $"\n## Contexto\n{contextoAcumulado}\n";

        return $@"Você é um analista de mercado especializado em startups early-stage.

A partir da análise inicial abaixo, aprofunde o mapeamento competitivo. Foque no que é usado na prática, não em teoria. Cite soluções reais com nomes quando possível.
{contextoSection}
## Input do Usuário
{ideia}

## Roteiro

A) Competidores e alternativas reais
- 3-5 soluções específicas que o público usa hoje (nome + o que faz + limitação principal)
- Incluir gambiarras e processos manuais se forem comuns

B) Gaps exploráveis
- 2-3 limitações concretas que nenhuma alternativa resolve bem
- Para cada gap: quem sofre mais e por quê

C) Posicionamento sugerido
- 1 frase de posicionamento frente aos competidores
- Principal vantagem competitiva a construir

D) Métricas de mercado
- 3-5 dados concretos: tamanho do segmento, ticket médio praticado, custo do problema para o cliente, frequência de uso das alternativas

Formato: bullets concisos e diretos. Prefira dados específicos a afirmações genéricas.

Retorne JSON:
```json
{{
  ""competidores_alternativas"": {{
    ""solucoes_reais"": [{{
      ""nome"": ""[Nome da solução]"",
      ""o_que_faz"": ""[Descrição breve]"",
      ""limitacao_principal"": ""[Limitação]""
    }}],
    ""processos_manuais"": [""[Gambiarra/processo manual comum]""]
  }},
  ""gaps_exploraveis"": [{{
    ""gap"": ""[Limitação não resolvida]"",
    ""quem_sofre"": ""[Público que mais sofre]"",
    ""por_que"": ""[Motivo]""
  }}],
  ""posicionamento"": {{
    ""frase"": ""[1 frase de posicionamento frente aos competidores]"",
    ""vantagem_competitiva"": ""[Principal vantagem a construir]""
  }},
  ""metricas_mercado"": [{{
    ""dado"": ""[Nome da métrica]"",
    ""valor"": ""[Valor/conteúdo]""
  }}]
}}
```

Retorne APENAS JSON válido.";
    }

    /// <summary>
    /// Etapa 3: Proposta de Valor (Mini)
    /// </summary>
    public static string Etapa3PropostaValor(Dictionary<string, string> inputs)
    {
        var problema = inputs.GetValueOrDefault("problema",
            inputs.GetValueOrDefault("ideia", "[não fornecido]"));
        var personas = inputs.GetValueOrDefault("personas", "");
        var contextoAcumulado = inputs.GetValueOrDefault("contexto_acumulado", "");

        var personasSection = string.IsNullOrEmpty(personas)
            ? ""
            : $"\n**Personas:** {personas}";

        var contextoSection = string.IsNullOrEmpty(contextoAcumulado)
            ? ""
            : $"\n## Contexto\n{contextoAcumulado}\n";

        return $@"Especialista em Value Proposition Canvas. Construa a proposta de valor para a startup abaixo, usando os gaps competitivos e dores identificadas no contexto.

**Problema Validado:** {problema}{personasSection}
{contextoSection}
O `headline` deve ser específico e diferenciado — não genérico. Os `pain_relievers` devem endereçar limitações reais dos concorrentes.

Retorne JSON:
```json
{{
  ""value_proposition_canvas"": {{
    ""customer_profile"": {{
      ""customer_jobs"": [""[job funcional 1]"", ""[job 2]""],
      ""pains"": [""[dor específica — ligada a concorrente]"", ""[dor 2]""],
      ""gains"": [""[ganho mensurável 1]"", ""[ganho 2]""]
    }},
    ""value_map"": {{
      ""products_services"": [""[funcionalidade 1]"", ""[funcionalidade 2]""],
      ""pain_relievers"": [""[como elimina dor 1 — específico]"", ""[alivia dor 2]""],
      ""gain_creators"": [""[entrega ganho 1]"", ""[ganho 2]""]
    }}
  }},
  ""proposta_valor_final"": {{
    ""headline"": ""[1 frase: para [quem], [produto] que [benefício único]]"",
    ""subheadline"": ""[2-3 frases: o quê, para quem, resultado]"",
    ""beneficios_chave"": [""[benefício mensurável 1]"", ""[benefício 2]""],
    ""diferenciais"": [""[diferencial vs concorrente 1]"", ""[diferencial 2]""]
  }},
  ""posicionamento"": {{
    ""para"": ""[público específico]"",
    ""que"": ""[problema específico]"",
    ""nosso_produto"": ""[categoria]"",
    ""diferente_de"": ""[concorrentes]"",
    ""porque"": ""[razão de diferenciação]""
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
        var contextoAcumulado = inputs.GetValueOrDefault("contexto_acumulado", "");

        var contextoSection = string.IsNullOrEmpty(contextoAcumulado)
            ? ""
            : $"\n## Contexto\n{contextoAcumulado}\n";

        return $@"Consultor de Business Model Canvas. Construa o modelo de negócio usando as métricas de mercado e proposta de valor já definidas.

**Ideia:** {ideia}
{contextoSection}
Use o ticket médio e segmento do contexto para tornar as projeções realistas. Inclua unit economics (CAC e LTV).

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
  ""unit_economics"": {{
    ""cac_estimado"": ""[R$ X]"",
    ""ltv_estimado"": ""[R$ Y]"",
    ""ltv_cac_ratio"": ""[N:1]"",
    ""payback_periodo"": ""[N meses]""
  }},
  ""projecao_financeira_simplificada"": {{
    ""ano_1"": {{
      ""receita_mensal_media"": ""[R$ X]"",
      ""custos_mensais"": ""[R$ Y]"",
      ""margem_bruta"": ""[%]"",
      ""break_even_months"": ""[N]""
    }},
    ""premissas"": [""[premissa baseada no mercado]"", ""[premissa 2]""]
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
