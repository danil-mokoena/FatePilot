# Validação da versão experimental

## Já executado fora do jogo

- Compilação Release: zero erros e zero avisos.
- 40 testes aprovados: comportamento do controlador com snapshots simulados, gastos, traduções, acesso a APIs sensíveis e recursos incorporados nas DLLs.
- Conferência dos contratos utilizados no código disponível das integrações.
- Conferência do manifesto, autor, versão, conteúdo dos pacotes e hashes.

## Pendente: cliente real

Estes passos são um roteiro para o primeiro teste acompanhado, não resultados já obtidos.

1. Carregar o plugin e verificar se os quatro módulos aparecem disponíveis no diagnóstico. Testar abertura/fechamento da UI e leitura do logo. Mudar entre os seis idiomas, conferir cortes de texto e retornar ao idioma preferido.
2. Fora de combate, com outras automações paradas, configurar uma zona conhecida, sem troca de instância, meta de um FATE e uma cidade de retorno desbloqueada. Confirmar que Parar funciona durante o deslocamento e que os ajustes anteriores são restaurados.
3. Iniciar novamente e acompanhar seleção, montaria, navegação, pouso, sincronização, habilidades e posicionamento. Observar a contagem e o retorno. Não considerar uma simples ausência de erro como teste de todas as etapas.
4. Ativar a troca de instâncias após a descoberta no aetheryte. Acompanhar uma troca completa; depois testar rota de duas zonas.
5. Em Gastos, testar um destino pago com ticket disponível, confirmar inventário e gil antes/depois. Testar o interruptor desligado com as opções do jogo conhecidas. A opção de continuar pagando gil deve permanecer desligada até esse fluxo ser validado. Depois testar tarifa mínima e orçamento.
6. Comparar presets e estado de habilidades antes/depois de Parar. Testar o botão de recuperação com uma sessão interrompida de forma controlada, somente após o fluxo normal funcionar.
7. Confirmar que o botão “Buy my cat a treat” abre somente o endereço exibido e apenas ao clicar. O teste local do link verifica o código, sem abrir o navegador automaticamente.

Se ocorrer uma falha, use Parar e consulte o diagnóstico local. “Copiar diagnóstico” copia somente quando clicado; revise o texto antes de compartilhá-lo. Não há envio automático para suporte.

O teste deve registrar versão do cliente, Dalamud e integrações, etapa, resultado esperado e resultado observado. Não é necessário incluir nome do personagem ou conta.
