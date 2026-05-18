namespace IdeorAI.Services.Chat;

/// <summary>
/// Base de conhecimento estática da plataforma IdeorAI.
/// Chunks indexados por palavras-chave para busca simples (sem pgvector em v1).
/// </summary>
public static class RagKnowledgeBase
{
    private sealed record Chunk(string[] Keywords, string Content);

    private static readonly Chunk[] Chunks =
    [
        new(
            ["jornada", "etapas", "validação", "processo", "startup", "fases"],
            """
            ## Jornada de Validação IdeorAI
            A jornada é composta por 6 etapas sequenciais:
            1. **Início (01)** – Definição da ideia central, nome e categoria da startup.
            2. **Problema (02)** – Identificação e validação do problema real que você resolve. Inclui entrevistas com potenciais clientes.
            3. **Pesquisa (03)** – Análise de mercado, concorrentes e tendências. Valide o tamanho do mercado (TAM/SAM/SOM).
            4. **Proposta de Valor (04)** – Defina claramente o que torna sua solução única. Use o Canvas de Proposta de Valor.
            5. **Modelo de Negócio (05)** – Como você vai monetizar. Receitas, custos, canais de distribuição.
            6. **MVP (06)** – Produto Mínimo Viável: o que você vai construir para testar com clientes reais.
            Cada etapa deve ser concluída antes de avançar. A IA avalia seu progresso e gera insights ao completar cada fase.
            """),

        new(
            ["início", "etapa 1", "etapa01", "primeiro", "ideia", "nome projeto", "categoria"],
            """
            ## Etapa 01 — Início
            Esta é a etapa de fundamentação da sua startup. Você deve:
            - Definir o nome do projeto
            - Escolher a categoria (ex: Software/IA, Saúde, Educação, Finanças...)
            - Escrever uma descrição inicial da ideia em 2-3 frases
            - Identificar o público-alvo principal
            Dica: seja específico sobre o cliente. "Pequenas empresas" é vago; "donos de restaurantes com menos de 10 funcionários" é um mercado.
            """),

        new(
            ["problema", "etapa 2", "etapa02", "dor", "entrevista", "validar problema"],
            """
            ## Etapa 02 — Problema
            O objetivo é validar que o problema existe e é relevante. Você deve:
            - Descrever o problema com clareza (qual dor, para quem, com que frequência)
            - Realizar pelo menos 5 entrevistas com potenciais clientes (registre as respostas)
            - Identificar as causas raiz do problema
            - Validar se as pessoas estão dispostas a pagar por uma solução
            Erros comuns: descrever a solução em vez do problema, não entrevistar clientes reais, supor que o problema existe sem validação.
            """),

        new(
            ["pesquisa", "etapa 3", "etapa03", "mercado", "concorrente", "tam", "sam", "som", "tendência"],
            """
            ## Etapa 03 — Pesquisa de Mercado
            Analise o contexto em que sua startup vai competir:
            - **TAM** (Total Addressable Market): mercado total disponível
            - **SAM** (Serviceable Addressable Market): parte que você pode alcançar
            - **SOM** (Serviceable Obtainable Market): fatia realista nos primeiros anos
            - Liste os 3-5 principais concorrentes e diferenciais
            - Identifique tendências que favorecem sua solução
            Ferramentas úteis: Google Trends, Statista, relatórios de mercado, LinkedIn.
            """),

        new(
            ["proposta de valor", "etapa 4", "etapa04", "diferencial", "canvas", "unique value"],
            """
            ## Etapa 04 — Proposta de Valor
            Defina por que clientes escolherão você em vez dos concorrentes:
            - Complete o Canvas de Proposta de Valor (jobs, pains, gains)
            - Escreva sua proposta em uma frase: "Para [cliente] que [problema], [produto] é [categoria] que [benefício único], ao contrário de [alternativa]"
            - Liste os 3 maiores benefícios que você entrega
            - Identifique as objeções mais comuns e como respondê-las
            """),

        new(
            ["modelo de negócio", "etapa 5", "etapa05", "monetização", "receita", "custo", "canal", "bmg", "business model"],
            """
            ## Etapa 05 — Modelo de Negócio
            Defina como sua startup vai gerar receita de forma sustentável:
            - **Modelo de receita**: assinatura, transacional, freemium, marketplace, licença...
            - **Canais**: como você vai adquirir e entregar valor aos clientes
            - **Estrutura de custos**: fixos e variáveis principais
            - **Métricas-chave**: CAC, LTV, churn, MRR
            Calcule o ponto de equilíbrio: quantos clientes você precisa para cobrir os custos?
            """),

        new(
            ["mvp", "etapa 6", "etapa06", "produto mínimo viável", "protótipo", "teste", "lançamento"],
            """
            ## Etapa 06 — MVP (Produto Mínimo Viável)
            O MVP é a versão mais simples do produto que testa sua hipótese principal:
            - Defina a hipótese central a ser testada
            - Escolha o tipo de MVP: landing page, protótipo no-code, versão manual ("Wizard of Oz"), produto físico simples
            - Liste as funcionalidades essenciais (máx 3)
            - Defina métricas de sucesso: o que vai medir para saber se funcionou?
            - Estabeleça um prazo para lançar e coletar feedback
            Lembre-se: o MVP não precisa ser perfeito, precisa ser aprendível.
            """),

        new(
            ["ivo index", "ivo", "score", "pontuação", "avaliação", "nota", "índice"],
            """
            ## IVO Index — Ideor Value Opportunity

            O IVO Index é o indicador principal de valor do seu projeto no IdeorAI, expresso em R$.
            Ele combina 7 variáveis: progresso geral, originalidade, mercado, validação, execução, timing e documentação.

            **Faixas típicas:**
            - Projeto recém-criado: R$ 100.000 (valor base motivador)
            - Com 1-2 etapas iniciadas: R$ 100k - R$ 200k
            - Com 3 etapas concluídas: R$ 200k - R$ 500k (centenas de milhares)
            - Com 5 etapas bem feitas: R$ 1M - R$ 5M (faixa de milhões)
            - Excelente (todas variáveis altas): até R$ 10.000.000 (cap)

            **Como subir o IVO:**
            1. Complete todas as 5 etapas (mais peso é Documentação)
            2. Gere conteúdo rico (>300 caracteres por seção) — sobe a variável D
            3. Refine etapas para subir scores de O/M/V/E/T avaliados pela IA
            4. Atinja marcos (validações de mercado, MVP funcional)

            **Fórmula:** IVO_Index = min(100.000 × (IVO_raw + 1)^0.95, 10.000.000), onde IVO_raw considera todas as 7 variáveis multiplicativamente.

            O IVO é dinâmico — recalcula a cada geração de etapa, refinamento ou edição.
            """),

        new(
            ["go", "pivot", "go or pivot", "go/pivot", "decisão", "continuar", "mudar", "direção"],
            """
            ## Go ou Pivot
            A avaliação Go/Pivot é gerada pela IA após você completar etapas suficientes da jornada.
            - **GO**: a startup tem fundamentos sólidos para avançar. Continue executando o plano atual.
            - **PIVOT**: a análise identificou pontos fracos críticos. Considere mudar um elemento central (problema, mercado, solução ou modelo de negócio).
            **O que leva a um Pivot?**
            - Problema não validado com clientes reais
            - Mercado muito pequeno ou saturado
            - Modelo de negócio inviável financeiramente
            - Proposta de valor sem diferenciação clara
            Um Pivot não é fracasso — é aprendizado. As startups mais bem-sucedidas pivotaram (Instagram, YouTube, Slack).
            Você pode fazer override da recomendação se tiver dados que a IA não considera.
            """),

        new(
            ["plataforma", "como funciona", "funcionalidades", "features", "dashboard", "painel"],
            """
            ## Como funciona a plataforma IdeorAI
            A IdeorAI é uma plataforma de validação de startups com IA. Funcionalidades principais:
            - **Dashboard**: visão geral de todos seus projetos com IVO Index e status
            - **Jornada de Validação**: 6 etapas guiadas com feedback de IA em cada uma
            - **IVO Index**: métrica de maturidade calculada automaticamente
            - **Go/Pivot**: avaliação estratégica por IA
            - **Documentos**: geração automática de documentos de negócio (pitch, relatórios)
            - **Marketplace**: conecte-se com outros empreendedores e serviços
            - **Equipe**: convide colaboradores para o seu projeto
            Para navegar entre etapas: acesse o projeto → clique na etapa desejada → preencha o formulário → salve.
            """),

        new(
            ["equipe", "membros", "colaboradores", "convidar", "time", "compartilhar"],
            """
            ## Equipe e Colaboradores
            Você pode adicionar membros ao seu projeto para colaborar:
            - Acesse o projeto → seção "Equipe"
            - Convide por e-mail
            - Defina o papel: Admin (edita tudo) ou Membro (visualiza e comenta)
            Projetos compartilhados aparecem no dashboard de todos os membros em "Compartilhados comigo".
            """),

        new(
            ["documento", "relatório", "pdf", "gerar", "exportar", "pitch"],
            """
            ## Geração de Documentos
            A IdeorAI gera documentos automaticamente a partir do seu progresso:
            - **Relatório de Etapa**: síntese do trabalho realizado em cada fase
            - **Pitch Deck**: apresentação da startup baseada nos dados preenchidos
            - **Business Canvas**: modelo de negócio em formato visual
            Para gerar: acesse o projeto → clique em uma etapa concluída → "Gerar Relatório".
            Os documentos são salvos e podem ser baixados em PDF.
            """),
    ];

    /// <summary>
    /// Retorna os chunks mais relevantes para a query usando correspondência de palavras-chave.
    /// Retorna até maxChunks chunks ordenados por relevância.
    /// </summary>
    public static IReadOnlyList<string> Retrieve(string query, int maxChunks = 4)
    {
        var queryLower = query.ToLowerInvariant();
        var queryWords = queryLower.Split([' ', '?', '!', ',', '.', '\n'], StringSplitOptions.RemoveEmptyEntries);

        var scored = Chunks
            .Select(chunk =>
            {
                var hits = chunk.Keywords.Count(kw => queryWords.Any(w => w.Contains(kw) || kw.Contains(w)));
                // bonus para match direto na keyword exata
                var exact = chunk.Keywords.Count(kw => queryLower.Contains(kw));
                return (chunk, score: hits + exact * 2);
            })
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(maxChunks)
            .Select(x => x.chunk.Content)
            .ToList();

        // Se nenhum chunk relevante, retorna os 2 mais genéricos (jornada + plataforma)
        if (scored.Count == 0)
            return Chunks.Take(2).Select(c => c.Content).ToList();

        return scored;
    }
}
