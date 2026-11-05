# Revisão de segurança — FatePilot 0.1

Data: 4 de novembro de 2026. Escopo: código deste projeto e DLLs produzidas pela compilação Release. Revisão manual, testes do controlador simulado e inspeção dos metadados das DLLs. Não foi feito teste dinâmico dentro do jogo nem captura de tráfego do processo completo.

## Resultado verificável

Não foram identificados no código próprio clientes HTTP, sockets, webhooks, serviços de telemetria, leitura de nomes de jogadores, Content IDs, contas, credenciais ou chat. As DLLs próprias não referenciam tipos de rede, criação de processos, carregamento dinâmico de assemblies ou P/Invoke declarado pelo projeto. Os acessos nativos necessários ao jogo passam pelas estruturas fornecidas pelo host.

O único endereço web no código de execução é `https://ko-fi.com/mokoena`. Ele é entregue ao navegador pelo utilitário do Dalamud **somente após clicar em “Buy my cat a treat”**. Não é aberto no carregamento do plugin, nem contém parâmetros de personagem, mundo, sessão ou rastreamento adicionados pelo FatePilot. A visita ao site tem o comportamento normal do navegador e do próprio Ko-fi.

O logo, o retrato do gato e os seis catálogos de idioma estão incorporados na DLL; não são baixados pela interface. O manifesto desabilita a opção de feedback do empacotador. Não há servidor próprio, autenticação, atualização própria, anúncios ou código remoto.

## Dados e armazenamento

| Informação | Uso | Destino |
|---|---|---|
| Idioma, filtros, rota, cidade, tickets e orçamento | Preferências | Configuração local gerenciada pelo Dalamud |
| Estado anterior de posicionamento e nomes de presets | Restauração | `session-recovery.json`, na pasta de configuração do plugin; removido após restauração bem-sucedida |
| Estado, posição, classe e nível do próprio personagem | Navegação e controle | Memória durante a leitura; não exportados |
| FATEs, inimigos NPC e aetherytes | Seleção de objetivos e trajeto | Memória; sem registro de jogadores próximos |
| Contadores de tickets/gil e alvo anterior | Controle da sessão e restauração | Memória |
| Últimos 120 eventos da sessão | Diagnóstico | Fila em memória; não gravada em histórico pelo plugin |
| Falha de atualização/restauração | Aviso técnico | Log do host com mensagem genérica, sem payload de jogador |
| Diagnóstico copiado | Ação manual do usuário | Área de transferência, apenas pelo botão “Copiar diagnóstico” |

O registro de recuperação contém rótulos de presets definidos pelo usuário. Não se deve tratá-lo como arquivo anônimo se o próprio usuário colocou dados pessoais nesses rótulos. Não há envio desse arquivo. A enumeração da tabela de objetos filtra NPCs e aetherytes e não lê nomes de outros personagens. O alvo anterior é mantido apenas como identificador transitório para restaurá-lo.

Com a UI fechada e nenhuma sessão em execução, não são feitas as leituras periódicas de mundo para o painel. Uma limpeza de recurso temporário ainda pode ser concluída.

## Proteções aplicadas

- Chamadas de API com identificadores fixos; nenhum comando de chat arbitrário é montado a partir de nomes ou configurações.
- Link externo fixo e iniciado por clique; sem URL de imagem remota.
- Controle de habilidades por lease, com desativação e liberação ao encerrar. Preset de posicionamento temporário, limitado a movimento e validado após criação.
- Detecção de alteração externa de controle; interrupção da sessão em conflito.
- Cancelamento da tarefa de cálculo de rota e descarte do resultado tardio após Parar.
- Restauração do estado anterior, inclusive o estado de desativação forçada; preservação de presets alterados externamente.
- Registro de recuperação limitado a 64 KiB e estrutura validada antes da restauração.
- Cancelamento apenas da conjuração de Teleport iniciada por esta sessão. Sem confirmação genérica de caixas de diálogo.
- Orçamento verificado antes da solicitação de viagem e conferência de consumo de tickets na chegada.
- Prazos para deslocamento, teleporte, troca de instância, sincronização e combate. Ausência de progresso exclui temporariamente o FATE.
- Interrupções em morte, desconexão, duty/PvP, mudança de classe ou zona inesperada e interação não prevista.
- Testes de regressão bloqueiam introdução de APIs sensíveis conhecidas, endpoints adicionais, leituras de identidade e ausência de recursos locais nas DLLs.

## Limites da revisão

As verificações estáticas são barreiras de regressão, não um isolamento de permissões. O plugin funciona dentro do processo do jogo e usa APIs nativas: mudanças no cliente, no host ou nos contratos externos podem causar falhas. A suíte não executa os adaptadores em um cliente real.

As integrações descritas em [INTEGRATIONS.md](INTEGRATIONS.md) têm comportamentos e permissões próprios, inclusive consultas de rede. Algumas dessas consultas podem ser desencadeadas ao usar suas APIs. Não foi auditado integralmente todo o código dessas dependências nem o processo completo. A conclusão sobre ausência de telemetria própria se limita ao FatePilot entregue.

O mecanismo nativo de ticket ainda requer validação com as opções reais de Teleport. A conferência na chegada detecta inconsistências, mas não desfaz consumo ocorrido. Durante uma mudança de área já confirmada pelo jogo, Parar não reverte a viagem.

## Reproduzir

Execute `./build.ps1` com .NET SDK 10 e Dalamud API 15. O resultado inclui compilação, 40 testes, inspeção de referências/métodos/recursos das duas DLLs e SHA-256 dos artefatos. `tests/FatePilot.Tests/PrivacyAudit.cs` contém os critérios automatizados. Revise também os adaptadores manualmente ao mudar qualquer API.
