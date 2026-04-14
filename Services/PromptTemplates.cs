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

* **Ideia inicial:** {ideia}
* **Mercado/segmento-alvo (se houver):** {mercado}
* **Região/país de atuação:** {regiao}
* **Restrições e recursos:** {recursos}

## **Objetivos desta interação**

1. **Clarificar a dor central, público-alvo e contexto competitivo** por meio de pesquisa secundária estruturada e entrevistas exploratórias simuladas.
2. **Formular hipóteses iniciais explícitas** sobre segmentos, dores, proposta de valor e disposição a pagar, incluindo critérios mensuráveis para validação.

## **Instruções de execução**

* Preencha cada seção do roteiro abaixo com **bullets objetivos e claros**.
* Use linguagem simples, prática e **orientada à ação**.
* Evite jargões sem explicação. Dê exemplos sempre que possível.
* Se a ideia estiver vaga, proponha **cenários alternativos** para não travar a análise.

## **Roteiro (formato JSON)**

Retorne um JSON estruturado seguindo as seções abaixo:

**A) Declaração do problema (v1.0)**
* Dor central (1 frase).
* Consequências da dor (2-3 bullets).
* Quem sente a dor (persona resumida: setor, tamanho, função).
* Situações-gatilho típicas em que a dor aparece.

**B) Mapa rápido do mercado (pesquisa secundária sintetizada)**
* Segmento(s) e subnichos promissores.
* Alternativas atuais usadas pelo público (concorrentes, planilhas, processos manuais).
* Vácuos/diferenciais potenciais e barreiras de entrada.
* Tendências relevantes e regulações (se aplicável).
* 3-5 métricas de mercado úteis (ex.: tamanho do nicho, ticket médio inicial, frequência de uso).

**C) Personas iniciais (2 a 3 perfis)**
Para cada persona:
* Perfil e contexto (papel, tamanho da empresa, responsabilidades).
* Objetivos e dores específicas relacionadas à ideia.
* Critérios de decisão e objeções comuns.
* Canais de acesso (onde encontrar, como abordar).

**D) Proposta de valor inicial (v1)**
* Frase de valor (1 sentença).
* Jobs-to-be-done, dores e ganhos que atende (3-5 bullets).
* Diferenciais específicos frente às alternativas mapeadas.

**E) Resumo da ideia**
* Síntese da oportunidade, público prioritário e hipótese de proposta de valor.
* Recomendação inicial de faixa de preço ou modelo de monetização (se possível).
* Principais incertezas a validar nas próximas etapas.

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
      ""[situação 2]"",
      ""[situação 3]""
    ]
  }},
  ""mapa_mercado"": {{
    ""segmentos_promissores"": [""[segmento 1]"", ""[segmento 2]"", ""[subnicho 1]""],
    ""alternativas_atuais"": [""[concorrente/planilha/processo manual 1]"", ""[alternativa 2]"", ""[alternativa 3]""],
    ""diferenciais_potenciais"": [""[vácuo/diferencial 1]"", ""[diferencial 2]""],
    ""barreiras_entrada"": [""[barreira 1]"", ""[barreira 2]""],
    ""tendencias"": [""[tendência relevante 1]"", ""[regulação 1]""],
    ""metricas"": {{
      ""tamanho_nicho"": ""[estimativa de mercado]"",
      ""ticket_medio_inicial"": ""[valor estimado]"",
      ""frequencia_uso"": ""[diária/semanal/mensal]"",
      ""outras_metricas"": [""[métrica adicional 1]"", ""[métrica adicional 2]""]
    }}
  }},
  ""personas"": [
    {{
      ""nome"": ""[Nome da Persona 1]"",
      ""perfil_contexto"": {{
        ""papel"": ""[função/cargo]"",
        ""tamanho_empresa"": ""[pequena/média/grande]"",
        ""responsabilidades"": [""[responsabilidade 1]"", ""[responsabilidade 2]""]
      }},
      ""objetivos"": [""[objetivo 1]"", ""[objetivo 2]""],
      ""dores_especificas"": [""[dor relacionada à ideia 1]"", ""[dor 2]""],
      ""criterios_decisao"": [""[critério 1]"", ""[critério 2]""],
      ""objecoes_comuns"": [""[objeção 1]"", ""[objeção 2]""],
      ""canais_acesso"": [""[onde encontrar]"", ""[como abordar]""]
    }},
    {{
      ""nome"": ""[Nome da Persona 2]"",
      ""perfil_contexto"": {{
        ""papel"": ""[função/cargo]"",
        ""tamanho_empresa"": ""[pequena/média/grande]"",
        ""responsabilidades"": [""[responsabilidade 1]"", ""[responsabilidade 2]""]
      }},
      ""objetivos"": [""[objetivo 1]"", ""[objetivo 2]""],
      ""dores_especificas"": [""[dor relacionada à ideia 1]"", ""[dor 2]""],
      ""criterios_decisao"": [""[critério 1]"", ""[critério 2]""],
      ""objecoes_comuns"": [""[objeção 1]"", ""[objeção 2]""],
      ""canais_acesso"": [""[onde encontrar]"", ""[como abordar]""]
    }},
    {{
      ""nome"": ""[Nome da Persona 3]"",
      ""perfil_contexto"": {{
        ""papel"": ""[função/cargo]"",
        ""tamanho_empresa"": ""[pequena/média/grande]"",
        ""responsabilidades"": [""[responsabilidade 1]"", ""[responsabilidade 2]""]
      }},
      ""objetivos"": [""[objetivo 1]"", ""[objetivo 2]""],
      ""dores_especificas"": [""[dor relacionada à ideia 1]"", ""[dor 2]""],
      ""criterios_decisao"": [""[critério 1]"", ""[critério 2]""],
      ""objecoes_comuns"": [""[objeção 1]"", ""[objeção 2]""],
      ""canais_acesso"": [""[onde encontrar]"", ""[como abordar]""]
    }}
  ],
  ""proposta_valor_inicial"": {{
    ""frase_valor"": ""[1 sentença de valor]"",
    ""jobs_to_be_done"": [""[job 1]"", ""[job 2]"", ""[job 3]"", ""[job 4]"", ""[job 5]""],
    ""dores_ganhos"": [""[dor/ganho 1]"", ""[dor/ganho 2]"", ""[dor/ganho 3]""],
    ""diferenciais_especificos"": [""[diferencial vs alternativa 1]"", ""[diferencial 2]"", ""[diferencial 3]""]
  }},
  ""resumo_ideia"": {{
    ""sintese_oportunidade"": ""[síntese clara da oportunidade]"",
    ""publico_prioritario"": ""[público principal a focar]"",
    ""hipotese_proposta_valor"": ""[hipótese de proposta de valor]"",
    ""faixa_preco_monetizacao"": ""[recomendação inicial de preço ou modelo]"",
    ""incertezas_validar"": [""[incerteza 1 a validar]"", ""[incerteza 2]"", ""[incerteza 3]""]
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
var contextoAcumulado = inputs.GetValueOrDefault("contexto_acumulado", "");

var contextoSection = string.IsNullOrEmpty(contextoAcumulado) 
? "" 
: $@"
## Contexto
{contextoAcumulado}
";

return $@"Você é um analista de mercado especializado em startups early-stage.

A partir da análise inicial abaixo, aprofunde o mapeamento competitivo. Foque no que é usado na prática, não em teoria. Cite soluções reais com nomes quando possível.
{contextoSection}
## Input do Usuário

**Região:** {regiao}
**Segmento:** {segmento}
**Ideia:** {ideiaBase}

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
    ""solucoes_reais"": [
      {{
        ""nome"": ""[Nome da solução]"",
        ""o_que_faz"": ""[Descrição breve]"",
        ""limitacao_principal"": ""[Limitação]""
      }}
    ],
    ""processos_manuais"": [""[Gambiarra/processo manual comum]""]
  }},
  ""gaps_exploraveis"": [
    {{
      ""gap"": ""[Limitação não resolvida]"",
      ""quem_sofre"": ""[Público que mais sofre]"",
      ""por_que"": ""[Motivo]""
    }}
  ],
  ""posicionamento"": {{
    ""frase"": ""[1 frase de posicionamento frente aos competidores]"",
    ""vantagem_competitiva"": ""[Principal vantagem a construir]""
  }},
  ""metricas_mercado"": [
    {{
      ""dado"": ""[Nome da métrica]"",
      ""valor"": ""[Valor/conteúdo]""
    }}
  ]
}}
```

**IMPORTANTE:** Retorne APENAS o JSON válido.";
}

    /// <summary>
    /// Etapa 3: Proposta de Valor
    /// </summary>
    public static string Etapa3PropostaValor(Dictionary<string, string> inputs)
    {
        var ideia = inputs.GetValueOrDefault("ideia", "[não fornecido]");
        var contextoAcumulado = inputs.GetValueOrDefault("contexto_acumulado", "");

        var contextoSection = string.IsNullOrEmpty(contextoAcumulado)
            ? ""
            : $@"
## Contexto das etapas anteriores
{contextoAcumulado}

Use esse contexto — especialmente a análise competitiva e as dores mapeadas — para tornar a proposta de valor específica e diferenciada.
";

        return $@"Você é um **especialista em Value Proposition Canvas e posicionamento competitivo**. Construa uma proposta de valor concreta para a startup abaixo.

## Ideia
{ideia}
{contextoSection}
## Diretrizes
- A proposta deve responder diretamente às **dores e gaps competitivos** identificados nas etapas anteriores.
- Evite linguagem genérica. Seja específico sobre **o que a solução faz de diferente** e **para quem** especificamente.
- Os `pain_relievers` devem endereçar as **limitações reais dos concorrentes** mapeados.
- O `headline` deve ser direto, memorável e diferenciado — não use frases genéricas como ""solução inovadora"".
- `metricas_sucesso` são indicadores mensuráveis que provam que a proposta de valor funcionou (ex: ""reduz tempo X em Y%"").

Retorne JSON:

```json
{{
  ""value_proposition_canvas"": {{
    ""customer_profile"": {{
      ""customer_jobs"": [""[tarefa funcional 1]"", ""[tarefa funcional 2]"", ""[job social 1]""],
      ""pains"": [""[dor específica 1 — ligada a concorrente]"", ""[dor 2]"", ""[dor 3]""],
      ""gains"": [""[ganho mensurável 1]"", ""[ganho 2]"", ""[ganho 3]""]
    }},
    ""value_map"": {{
      ""products_services"": [""[funcionalidade/produto 1]"", ""[funcionalidade/produto 2]""],
      ""pain_relievers"": [""[como elimina dor 1 — específico]"", ""[como elimina dor 2]""],
      ""gain_creators"": [""[como entrega ganho 1 — específico]"", ""[como entrega ganho 2]""]
    }}
  }},
  ""proposta_valor_final"": {{
    ""headline"": ""[1 frase direta: para [quem], [produto] que [benefício único]]"",
    ""subheadline"": ""[2-3 frases explicativas: o que é, para quem, resultado esperado]"",
    ""beneficios_chave"": [""[benefício mensurável 1]"", ""[benefício 2]"", ""[benefício 3]""],
    ""diferenciais"": [""[diferencial vs concorrente 1]"", ""[diferencial vs concorrente 2]""]
  }},
  ""posicionamento"": {{
    ""para"": ""[público-alvo específico]"",
    ""que"": ""[necessidade/problema específico]"",
    ""nosso_produto"": ""[categoria do produto]"",
    ""diferente_de"": ""[principais concorrentes]"",
    ""porque"": ""[razão principal de diferenciação]""
  }},
  ""metricas_sucesso"": [
    {{""metrica"": ""[indicador mensurável]"", ""meta"": ""[valor alvo]"", ""prazo"": ""[período]""}},
    {{""metrica"": ""[indicador 2]"", ""meta"": ""[valor]"", ""prazo"": ""[período]""}}
  ]
}}
```

**IMPORTANTE:** Retorne APENAS o JSON válido.";
    }

    /// <summary>
    /// Etapa 4: Modelo de Negócio
    /// </summary>
    public static string Etapa4ModeloNegocio(Dictionary<string, string> inputs)
    {
        var ideia = inputs.GetValueOrDefault("ideia", "[não fornecido]");
        var contextoAcumulado = inputs.GetValueOrDefault("contexto_acumulado", "");

        var contextoSection = string.IsNullOrEmpty(contextoAcumulado)
            ? ""
            : $@"
## Contexto das etapas anteriores
{contextoAcumulado}

Use esse contexto — especialmente métricas de mercado, ticket médio e proposta de valor — para tornar as projeções financeiras realistas e ancoradas.
";

        return $@"Você é um **consultor de Business Model Canvas e modelagem financeira para startups**. Construa o modelo de negócio completo e projeções realistas.

## Ideia
{ideia}
{contextoSection}
## Diretrizes
- Os **fluxos de receita** devem especificar valores realistas baseados no ticket médio do mercado identificado (se disponível no contexto).
- A **projeção financeira** deve incluir premissas explícitas e números defensáveis — evite projeções excessivamente otimistas sem justificativa.
- O `break_even_months` deve ser calculado a partir dos custos fixos e receita recorrente projetada.
- Inclua **unit economics**: CAC estimado e LTV, pois são cruciais para a viabilidade.

Retorne JSON:

```json
{{
  ""business_model_canvas"": {{
    ""segmentos_clientes"": [""[segmento principal]"", ""[segmento secundário]""],
    ""proposta_valor"": [""[proposta central]""],
    ""canais"": [""[canal de aquisição 1]"", ""[canal 2]"", ""[canal de entrega]""],
    ""relacionamento_clientes"": [""[tipo de relacionamento 1]"", ""[tipo 2]""],
    ""fluxos_receita"": [
      {{
        ""tipo"": ""[assinatura/pay-per-use/freemium/marketplace]"",
        ""valor"": ""[R$ X/mês ou % ou por uso]"",
        ""frequencia"": ""[mensal/anual/por transação]"",
        ""justificativa"": ""[por que esse modelo para esse segmento]""
      }}
    ],
    ""recursos_chave"": [""[recurso crítico 1]"", ""[recurso 2]""],
    ""atividades_chave"": [""[atividade principal 1]"", ""[atividade 2]""],
    ""parcerias_chave"": [""[parceiro estratégico 1]"", ""[parceiro 2]""],
    ""estrutura_custos"": [
      {{
        ""categoria"": ""[categoria]"",
        ""valor_estimado"": ""[R$ X/mês]"",
        ""tipo"": ""[fixo/variável]""
      }}
    ]
  }},
  ""unit_economics"": {{
    ""cac_estimado"": ""[R$ X por cliente adquirido]"",
    ""ltv_estimado"": ""[R$ Y ao longo do relacionamento]"",
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
    ""premissas"": [
      ""[premissa 1: ex. ticket médio de R$ X com base no mercado]"",
      ""[premissa 2: ex. crescimento mensal de Y%]"",
      ""[premissa 3: ex. custo de aquisição baseado em canal Z]""
    ]
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
        var ideia = inputs.GetValueOrDefault("ideia", "[não fornecido]");
        var contextoAcumulado = inputs.GetValueOrDefault("contexto_acumulado", "");

        var contextoSection = string.IsNullOrEmpty(contextoAcumulado)
            ? ""
            : $@"
## Contexto das etapas anteriores
{contextoAcumulado}

Use esse contexto — especialmente as funcionalidades da proposta de valor e o modelo de negócio — para definir um MVP que valide as hipóteses mais críticas primeiro.
";

        return $@"Você é um **Product Manager especializado em MVPs Lean**. Defina o MVP mínimo viável que valida as hipóteses mais importantes com o menor esforço.

## Ideia
{ideia}
{contextoSection}
## Diretrizes
- O MVP deve ser o menor escopo possível para validar a hipótese central de negócio.
- `core_features` deve ter no máximo 3-5 funcionalidades — prefira menos e mais foco.
- O `roadmap_3_meses` deve ser um array com 3 objetos (um por mês), cada um com objetivo claro e entregas concretas.
- `custo_desenvolvimento` deve ser realista para o Brasil (freelancers ou agências nacionais).
- As `metricas_validacao` devem ser métricas de negócio (conversão, retenção, receita), não apenas técnicas.

Retorne JSON com exatamente esta estrutura:

```json
{{
  ""definicao_mvp"": {{
    ""descricao"": ""[o que é o MVP em 2-3 frases]"",
    ""hipotese_central"": ""[qual hipótese crítica este MVP valida]"",
    ""core_features"": [""[feature essencial 1]"", ""[feature essencial 2]"", ""[feature essencial 3]""],
    ""nice_to_have"": [""[feature para versão posterior 1]"", ""[feature 2]""],
    ""justificativa"": ""[por que esse escopo é suficiente para validar]""
  }},
  ""priorizacao_moscow"": {{
    ""must_have"": [""[feature obrigatória 1]"", ""[feature 2]""],
    ""should_have"": [""[feature importante 1]""],
    ""could_have"": [""[feature desejável 1]""],
    ""wont_have"": [""[feature para depois 1]""]
  }},
  ""roadmap_3_meses"": [
    {{ ""mes"": 1, ""objetivo"": ""[objetivo do mês 1]"", ""entregas"": [""[entrega 1]"", ""[entrega 2]""] }},
    {{ ""mes"": 2, ""objetivo"": ""[objetivo do mês 2]"", ""entregas"": [""[entrega 1]"", ""[entrega 2]""] }},
    {{ ""mes"": 3, ""objetivo"": ""[objetivo do mês 3]"", ""entregas"": [""[entrega 1]"", ""[entrega 2]""] }}
  ],
  ""stack_tecnologica"": {{
    ""frontend"": ""[tecnologia recomendada]"",
    ""backend"": ""[tecnologia recomendada]"",
    ""database"": ""[banco de dados]"",
    ""infra"": ""[cloud/hosting]"",
    ""justificativa"": ""[por que essa stack para esse contexto]""
  }},
  ""metricas_validacao"": [
    {{ ""metrica"": ""[nome da métrica de negócio]"", ""meta"": ""[valor alvo]"", ""motivo"": ""[por que essa métrica prova validação]"" }},
    {{ ""metrica"": ""[métrica 2]"", ""meta"": ""[valor]"", ""motivo"": ""[motivo]"" }},
    {{ ""metrica"": ""[métrica 3]"", ""meta"": ""[valor]"", ""motivo"": ""[motivo]"" }}
  ],
  ""custo_desenvolvimento"": {{
    ""estimativa_total"": ""[R$ X]"",
    ""tempo_estimado"": ""[N meses]"",
    ""composicao"": [
      {{ ""item"": ""[dev frontend]"", ""valor"": ""[R$ X]"" }},
      {{ ""item"": ""[dev backend]"", ""valor"": ""[R$ Y]"" }},
      {{ ""item"": ""[design/UX]"", ""valor"": ""[R$ Z]"" }}
    ]
  }}
}}
```

**IMPORTANTE:** Retorne APENAS o JSON válido com exatamente as chaves especificadas.";
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
